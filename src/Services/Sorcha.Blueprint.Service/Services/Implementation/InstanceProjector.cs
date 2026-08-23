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
        var reactionDispatcher = scope.ServiceProvider.GetRequiredService<IReactionDispatcher>();

        foreach (var txId in evt.TransactionIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await FoldTransactionAsync(evt.RegisterId, txId, registerClient, instanceStore, actionResolver, reactionDispatcher, ct);
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
        IReactionDispatcher reactionDispatcher,
        CancellationToken ct)
    {
        var tx = await registerClient.GetTransactionAsync(registerId, txId, ct);
        if (tx?.MetaData is null)
            return;

        // Single shared resolution (also used by InstanceRebuildService → guarantees rebuild parity).
        var resolved = await InstanceProjectionResolver.ResolveAsync(tx, actionResolver, _logger, ct);
        if (resolved is null)
        {
            // Not an instance-scoped action (genesis, governance, credential-issuance record, etc.).
            _metrics.RecordSkippedNotInstanceScoped();
            return;
        }

        var blueprintId = resolved.BlueprintId;
        var instanceId = resolved.InstanceId;
        var projected = resolved.Tx;

        // Feature 194: a transaction carrying no pin predates the feature. It still folds — via the
        // documented fallback, taken IDENTICALLY here and in InstanceRebuildService so FR-003 parity
        // holds — but it is counted, because this counter is how an operator tells "pinning is
        // working" from "pinning is silently not happening". Every failure mode of this feature
        // degrades to the old behaviour rather than to an error, so the zero reading is the proof.
        if (string.IsNullOrWhiteSpace(projected.BlueprintExecDefHash))
        {
            _metrics.RecordPinFallback("projector");
            _logger.LogWarning(
                "InstanceProjector: transaction {TxId} for instance {InstanceId} carries no blueprint " +
                "definition pin; folding via the pre-Feature-194 fallback (latest definition).",
                txId, instanceId);
        }

        var existing = await instanceStore.GetAsync(instanceId, ct);
        if (existing is null)
        {
            var created = InstanceProjection.Project(
                instanceId, registerId, blueprintId, blueprintVersion: 1,
                resolved.TenantId, [projected]);
            if (created is null)
                return;
            await instanceStore.CreateAsync(created, ct);
            _metrics.RecordFolded();
            _logger.LogInformation(
                "InstanceProjector: created instance {InstanceId} (blueprint {BlueprintId}) pinned to {ExecDefHash} at action(s) {Actions}",
                instanceId, blueprintId,
                string.IsNullOrEmpty(created.BlueprintExecDefHash) ? "(unpinned)" : created.BlueprintExecDefHash,
                string.Join(",", created.CurrentActionIds));
            await reactionDispatcher.DispatchAsync(created, tx, ct);
            return;
        }

        var outcome = InstanceProjection.Apply(existing, projected);

        if (outcome == FoldOutcome.AlreadyApplied)
        {
            _metrics.RecordSkippedIdempotent();
            return;
        }

        if (outcome == FoldOutcome.RefusedForeignDefinition)
        {
            // The sender claimed a definition other than the one this instance runs. Refusing is the
            // point of the feature: accepting it would move a running instance onto another
            // definition — exactly what a participant must not be able to do — and would let two
            // nodes reach different answers from the same ledger.
            _metrics.RecordPinMismatch();
            _logger.LogError(
                "InstanceProjector: REFUSED transaction {TxId} for instance {InstanceId} — it claims " +
                "blueprint definition {ClaimedHash} but the instance is pinned to {PinnedHash}. The " +
                "instance has NOT advanced.",
                txId, instanceId, projected.BlueprintExecDefHash, existing.BlueprintExecDefHash);
            return;
        }

        await instanceStore.UpdateAsync(existing, ct);
        _metrics.RecordFolded();
        _logger.LogInformation(
            "InstanceProjector: advanced instance {InstanceId} via tx {TxId} to action(s) {Actions} (state {State})",
            instanceId, tx.TxId, string.Join(",", existing.CurrentActionIds), existing.State);
        await reactionDispatcher.DispatchAsync(existing, tx, ct);
    }
}
