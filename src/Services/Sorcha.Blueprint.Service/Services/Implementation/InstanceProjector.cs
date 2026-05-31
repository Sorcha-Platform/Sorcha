// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Events;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 — the single deterministic instance projector. Subscribes to
/// <c>docket:confirmed</c> on <b>every</b> node holding the register and folds each sealed
/// action transaction into the instance materialized view via the pure
/// <see cref="InstanceProjection"/> fold. There is no origin/mirror split — every node derives
/// the same instance from the same sealed ledger (SC-001). Replaces the owner-only
/// <c>InstanceMirrorReconstructor</c> and the submitter's imperative state mutation.
/// </summary>
/// <remarks>
/// <para>
/// The projector is <b>pure with respect to state</b>: it advances <c>CurrentActionIds</c>,
/// participant bindings, counts, and terminal state from the validated <see cref="RoutingDecision"/>
/// carried on the transaction in the clear — it never decrypts payload (FR-010). Side effects
/// (credential mint/deliver, notifications) are the separate <c>ReactionDispatcher</c>'s job (US2).
/// </para>
/// <para>
/// Idempotent: re-observing an already-folded transaction is a no-op, guarded by
/// <see cref="Instance.LastAppliedTxId"/> (FR-004). Transactions are folded in seal order as the
/// subscriber delivers them; a full <see cref="InstanceProjection.Project"/> rebuild (US4) repairs
/// any out-of-order or missed delivery.
/// </para>
/// </remarks>
public sealed class InstanceProjector : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InstanceProjectorMetrics _metrics;
    private readonly ILogger<InstanceProjector> _logger;
    private readonly IEventSubscriber? _subscriber;

    public InstanceProjector(
        IServiceScopeFactory scopeFactory,
        InstanceProjectorMetrics metrics,
        ILogger<InstanceProjector> logger,
        IEventSubscriber? subscriber = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subscriber = subscriber;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_subscriber is null)
        {
            _logger.LogWarning(
                "InstanceProjector: event subscriber not available — instance projection is disabled. " +
                "Instances will not advance until the event bus is reachable.");
            return;
        }

        _logger.LogInformation(
            "InstanceProjector starting — subscribing to {Channel} (runs on every node holding the register)",
            RegisterEventChannels.DocketConfirmed);

        try
        {
            await _subscriber.SubscribeAsync<DocketConfirmedEvent>(
                RegisterEventChannels.DocketConfirmed,
                async evt =>
                {
                    try
                    {
                        await HandleDocketConfirmedAsync(evt, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "InstanceProjector: unexpected error processing docket:confirmed");
                        _metrics.RecordErrored();
                    }
                },
                stoppingToken);

            _logger.LogInformation("InstanceProjector subscribed to {Channel}", RegisterEventChannels.DocketConfirmed);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("InstanceProjector stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InstanceProjector failed");
        }
    }

    private async Task HandleDocketConfirmedAsync(DocketConfirmedEvent evt, CancellationToken ct)
    {
        _metrics.RecordDocketObserved();
        var sw = Stopwatch.StartNew();

        if (evt?.TransactionIds is null || evt.TransactionIds.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IInstanceStore>();
        var actionResolver = scope.ServiceProvider.GetRequiredService<IActionResolverService>();

        foreach (var txId in evt.TransactionIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await FoldTransactionAsync(evt.RegisterId, txId, registerClient, instanceStore, actionResolver, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "InstanceProjector: failed to fold tx {TxId} in docket {Docket}", txId, evt.DocketId);
                _metrics.RecordErrored();
            }
        }

        sw.Stop();
        _metrics.RecordFoldLatency(sw.Elapsed.TotalMilliseconds);
    }

    private async Task FoldTransactionAsync(
        string registerId,
        string txId,
        IRegisterServiceClient registerClient,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver,
        CancellationToken ct)
    {
        var tx = await registerClient.GetTransactionAsync(registerId, txId, ct);
        if (tx?.MetaData is null)
            return;

        var blueprintId = tx.MetaData.BlueprintId;
        var instanceId = tx.MetaData.InstanceId;
        if (string.IsNullOrEmpty(blueprintId) || string.IsNullOrEmpty(instanceId) || tx.MetaData.ActionId is null)
        {
            // Not an instance-scoped action (genesis, governance, credential-issuance record, etc.).
            _metrics.RecordSkippedNotInstanceScoped();
            return;
        }

        var completedActionId = (int)tx.MetaData.ActionId.Value;
        var decision = ResolveRoutingDecision(tx.MetaData);
        var nextActionIds = decision is not null
            ? decision.NextActions.Select(a => a.ActionId).ToList()
            : tx.MetaData.NextActionId.HasValue ? [(int)tx.MetaData.NextActionId.Value] : [];

        var bindings = await ResolveParticipantBindingsAsync(
            blueprintId, completedActionId, nextActionIds, tx, actionResolver, ct);

        var projected = new ProjectedTransaction(
            TxId: tx.TxId,
            PreviousTransactionId: string.IsNullOrEmpty(tx.PrevTxId) ? null : tx.PrevTxId,
            CompletedActionId: completedActionId,
            NextActionIds: nextActionIds,
            ParticipantBindings: bindings);

        var existing = await instanceStore.GetAsync(instanceId, ct);
        if (existing is null)
        {
            var created = InstanceProjection.Project(
                instanceId, registerId, blueprintId, blueprintVersion: 1,
                ResolveTenantId(tx), [projected]);
            if (created is null)
                return;
            await instanceStore.CreateAsync(created, ct);
            _metrics.RecordFolded();
            _logger.LogInformation(
                "InstanceProjector: created instance {InstanceId} (blueprint {BlueprintId}) at action(s) {Actions}",
                instanceId, blueprintId, string.Join(",", created.CurrentActionIds));
            return;
        }

        var advanced = InstanceProjection.Apply(existing, projected);
        if (!advanced)
        {
            _metrics.RecordSkippedIdempotent();
            return;
        }

        await instanceStore.UpdateAsync(existing, ct);
        _metrics.RecordFolded();
        _logger.LogInformation(
            "InstanceProjector: advanced instance {InstanceId} via tx {TxId} to action(s) {Actions} (state {State})",
            instanceId, tx.TxId, string.Join(",", existing.CurrentActionIds), existing.State);
    }

    /// <summary>
    /// Reads the carried <see cref="RoutingDecision"/> — preferring the typed
    /// <see cref="TransactionMetaData.RoutingDecision"/> field, falling back to the canonical
    /// JSON the producer wrote into the clear tracking metadata (key <c>routingDecision</c>),
    /// which rides to the sealed docket via the validator's TrackingData copy. Returns null when
    /// neither is present (legacy transactions predating Feature 145).
    /// </summary>
    private RoutingDecision? ResolveRoutingDecision(TransactionMetaData metadata)
    {
        if (metadata.RoutingDecision is not null)
            return metadata.RoutingDecision;

        if (metadata.TrackingData is not null
            && metadata.TrackingData.TryGetValue("routingDecision", out var json)
            && !string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<RoutingDecision>(
                    json, Sorcha.Register.Models.RegisterSerializationOptions.Canonical);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "InstanceProjector: could not deserialize carried routingDecision metadata");
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves participant-id → wallet bindings this transaction contributes, keyed by the
    /// blueprint participant id (never self-keyed by wallet address). Binds the completed action's
    /// sender to the tx sender, and the next action's sender to the recipient (the next actor),
    /// so bindings accumulate across the chain. Best-effort — on any resolution failure the
    /// transaction still folds control state with whatever bindings resolved.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ResolveParticipantBindingsAsync(
        string blueprintId,
        int completedActionId,
        IReadOnlyList<int> nextActionIds,
        Sorcha.Register.Models.TransactionModel tx,
        IActionResolverService actionResolver,
        CancellationToken ct)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bp = await actionResolver.GetBlueprintAsync(blueprintId, ct);
            if (bp is null)
                return bindings;

            var completedAction = actionResolver.GetActionDefinition(bp, completedActionId.ToString());
            if (completedAction is { Sender.Length: > 0 } && !string.IsNullOrEmpty(tx.SenderWallet))
                bindings[completedAction.Sender] = tx.SenderWallet;

            var recipientWallet = tx.RecipientsWallets?.FirstOrDefault(
                w => !string.IsNullOrEmpty(w) && !string.Equals(w, tx.SenderWallet, StringComparison.OrdinalIgnoreCase));
            if (recipientWallet is not null)
            {
                foreach (var nextId in nextActionIds)
                {
                    var nextAction = actionResolver.GetActionDefinition(bp, nextId.ToString());
                    if (nextAction is { Sender.Length: > 0 })
                        bindings[nextAction.Sender] = recipientWallet;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "InstanceProjector: participant-binding resolution failed for blueprint {BlueprintId}", blueprintId);
        }

        return bindings;
    }

    private static string ResolveTenantId(Sorcha.Register.Models.TransactionModel tx)
        => tx.MetaData?.TrackingData?.GetValueOrDefault("tenantId") ?? string.Empty;
}
