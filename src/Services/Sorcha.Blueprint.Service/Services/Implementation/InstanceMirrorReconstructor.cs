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
using Sorcha.ServiceClients.Events;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 106 Wave D — background service that subscribes to <c>docket:confirmed</c>
/// Redis events and reconstructs read-only mirror rows for workflow instances whose
/// participants include a locally-registered wallet.
/// </summary>
/// <remarks>
/// <para>
/// Cross-node motivation: when an issuer on node A approves a Verified Citizen
/// application, the sealed acceptance transaction peer-replicates to node B's
/// register. Node B's Blueprint Service never executed the instance locally so it
/// has no Instance row for it — but its MyActions / MyCredentials UI still needs
/// to reason about pending actions and credential ownership. This reconstructor
/// listens for every new docket, walks its transactions, and upserts a minimal
/// mirror row into the local Blueprint Service store whenever any participant
/// wallet is local.
/// </para>
/// <para>
/// The reconstructor does NOT re-run routing logic locally. It populates the
/// mirror with the minimum fields the holder UI needs: <c>Id</c>,
/// <c>BlueprintId</c>, <c>RegisterId</c>, <c>ParticipantWallets</c>,
/// <c>CurrentActionIds</c> (seeded from <c>NextActionId</c> on the tx metadata),
/// <c>FirstTransactionId</c> / <c>LastTransactionId</c>. The authoritative state
/// remains on the issuer node; any holder action submission on the mirrored
/// instance travels through the register's normal transaction path.
/// </para>
/// <para>
/// Contract: <c>specs/106-register-native-credentials/contracts/instance-mirror-reconstructor.md</c>.
/// </para>
/// </remarks>
public sealed class InstanceMirrorReconstructor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventSubscriber? _subscriber;
    private readonly InstanceMirrorReconstructorMetrics _metrics;
    private readonly ILogger<InstanceMirrorReconstructor> _logger;

    public InstanceMirrorReconstructor(
        IServiceScopeFactory scopeFactory,
        InstanceMirrorReconstructorMetrics metrics,
        ILogger<InstanceMirrorReconstructor> logger,
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
                "InstanceMirrorReconstructor: event subscriber not available — cross-node instance mirroring is disabled. " +
                "Feature 106 MyActions queries from a holder node will return empty until the event bus is reachable.");
            return;
        }

        _logger.LogInformation(
            "InstanceMirrorReconstructor starting — subscribing to {Channel}",
            RegisterEventChannels.DocketConfirmed);

        try
        {
            // Subscribe via the Redis Streams IEventSubscriber — the SAME mechanism the Register
            // Service publishes through (RedisStreamEventPublisher.StreamAddAsync). The previous raw
            // _redis.GetSubscriber() pub/sub never received these events (the publisher uses Streams,
            // not pub/sub channels), so the owner node never materialised a mirror for a replica-
            // originated instance and the analyst had nothing to act on. Mirrors the working
            // PresentationSealSubscriber pattern.
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
                        _logger.LogError(ex,
                            "InstanceMirrorReconstructor: unexpected error processing docket:confirmed");
                        _metrics.RecordErrored();
                    }
                },
                stoppingToken);

            _logger.LogInformation(
                "InstanceMirrorReconstructor subscribed to {Channel}", RegisterEventChannels.DocketConfirmed);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("InstanceMirrorReconstructor stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InstanceMirrorReconstructor failed");
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
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IInstanceStore>();
        // Feature 137 — resolve the blueprint so the mirror's ParticipantWallets can be
        // keyed by participant id (e.g. "citizen") rather than self-keyed by wallet address.
        var actionResolver = scope.ServiceProvider.GetRequiredService<IActionResolverService>();

        foreach (var txId in evt.TransactionIds)
        {
            ct.ThrowIfCancellationRequested();
            _metrics.RecordTransactionInspected();

            try
            {
                await InspectTransactionAsync(evt.RegisterId, txId, registerClient, walletClient, instanceStore, actionResolver, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "InstanceMirrorReconstructor: failed to inspect tx {TxId} in docket {Docket}",
                    txId, evt.DocketId);
                _metrics.RecordErrored();
            }
        }

        sw.Stop();
        _metrics.RecordReconstructionLatency(sw.Elapsed.TotalMilliseconds);
    }

    private async Task InspectTransactionAsync(
        string registerId,
        string txId,
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        IInstanceStore instanceStore,
        IActionResolverService actionResolver,
        CancellationToken ct)
    {
        var tx = await registerClient.GetTransactionAsync(registerId, txId, ct);
        if (tx is null)
            return;

        var blueprintId = tx.MetaData?.BlueprintId;
        var instanceId = tx.MetaData?.InstanceId;
        if (string.IsNullOrEmpty(blueprintId) || string.IsNullOrEmpty(instanceId))
        {
            // Not an instance-scoped action — ignore (credential-issuance records,
            // governance transactions, etc. don't need mirrors).
            return;
        }

        // Collect candidate participant wallets: sender + recipients.
        var candidateWallets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(tx.SenderWallet))
            candidateWallets.Add(tx.SenderWallet);
        if (tx.RecipientsWallets is not null)
        {
            foreach (var w in tx.RecipientsWallets)
                if (!string.IsNullOrEmpty(w))
                    candidateWallets.Add(w);
        }

        if (candidateWallets.Count == 0)
        {
            _metrics.RecordSkippedNoLocalWallet();
            return;
        }

        // Probe each candidate — if none are locally registered, skip.
        // claude-review PR#294: parallelise with a bounded semaphore so a wide
        // recipient set doesn't issue N blocking HTTP calls in sequence. The
        // concurrency cap protects the Wallet Service from thundering-herd on
        // high-volume dockets.
        const int MaxConcurrentProbes = 10;
        using var probeSemaphore = new SemaphoreSlim(MaxConcurrentProbes);
        var probeTasks = candidateWallets.Select(async wallet =>
        {
            await probeSemaphore.WaitAsync(ct);
            try
            {
                var info = await walletClient.GetWalletAsync(wallet, ct);
                return info is not null ? wallet : null;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "InstanceMirrorReconstructor: wallet probe failed for {Wallet}", wallet);
                return null;
            }
            finally
            {
                probeSemaphore.Release();
            }
        }).ToList();

        var probeResults = await Task.WhenAll(probeTasks);
        var localWallets = probeResults.Where(w => w is not null).Cast<string>().ToList();

        if (localWallets.Count == 0)
        {
            _metrics.RecordSkippedNoLocalWallet();
            return;
        }

        // Check if the instance is already locally authoritative. If so, skip
        // (we don't want to overwrite a real row with a mirror).
        var existing = await instanceStore.GetAsync(instanceId, ct);
        if (existing is not null && !existing.IsReadOnlyMirror)
        {
            _metrics.RecordSkippedLocallyAuthoritative();
            return;
        }

        // Feature 137 (option (b) from the former TODO) — resolve the blueprint on this
        // node and map participant ids → wallets so the mirror's ParticipantWallets is
        // STRUCTURALLY VALID, not just self-keyed by wallet address. Credential issuance
        // on the owner node resolves the recipient by participant id
        // (CredentialIssuanceConfig.RecipientParticipantId, e.g. "citizen"); a self-keyed
        // mirror fails VAL_RUNTIME_CRED_001 even though the recipient wallet is known.
        //
        // The sender of THIS action binds its participant (action 1 sender = "citizen").
        // The next action's sender binds the (single) recipient when known (next action
        // sender = "verification-analyst"). Mappings accumulate across dockets, so by the
        // time the analyst approves action 2 the mirror already carries "citizen" from
        // action 1. Best-effort: on any resolution failure we fall back to self-keying.
        var participantBindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var bp = await actionResolver.GetBlueprintAsync(blueprintId, ct);
            if (bp is not null)
            {
                if (tx.MetaData?.ActionId is uint senderActionId && !string.IsNullOrEmpty(tx.SenderWallet))
                {
                    var senderAction = actionResolver.GetActionDefinition(bp, senderActionId.ToString());
                    if (senderAction is not null && !string.IsNullOrEmpty(senderAction.Sender))
                        participantBindings[senderAction.Sender] = tx.SenderWallet;
                }

                if (tx.MetaData?.NextActionId is uint nextActionId)
                {
                    var nextActionDef = actionResolver.GetActionDefinition(bp, nextActionId.ToString());
                    // The recipient (next actor) is every candidate that is not the sender.
                    var recipientWallet = candidateWallets.FirstOrDefault(
                        w => !string.Equals(w, tx.SenderWallet, StringComparison.OrdinalIgnoreCase));
                    if (nextActionDef is not null && !string.IsNullOrEmpty(nextActionDef.Sender) && recipientWallet is not null)
                        participantBindings[nextActionDef.Sender] = recipientWallet;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex,
                "InstanceMirrorReconstructor: blueprint-aware participant resolution failed for {BlueprintId}; falling back to self-keying",
                blueprintId);
        }

        var nextAction = tx.MetaData?.NextActionId;
        var mirror = new Instance
        {
            Id = instanceId,
            BlueprintId = blueprintId,
            BlueprintVersion = (existing?.BlueprintVersion) ?? 1,
            RegisterId = registerId,
            TenantId = existing?.TenantId ?? string.Empty,
            State = InstanceState.Active,
            CurrentActionIds = nextAction.HasValue ? [(int)nextAction.Value] : (existing?.CurrentActionIds ?? []),
            ParticipantWallets = existing?.ParticipantWallets ?? new Dictionary<string, string>(),
            FirstTransactionId = existing?.FirstTransactionId ?? tx.TxId,
            LastTransactionId = tx.TxId,
            CompletedActionCount = existing?.CompletedActionCount ?? 0,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsReadOnlyMirror = true,
        };

        // Merge blueprint-resolved participant→wallet bindings (keyed by participant id).
        // These are the authoritative entries credential issuance + role routing rely on.
        foreach (var binding in participantBindings)
        {
            mirror.ParticipantWallets[binding.Key] = binding.Value;
        }

        // Fallback: self-key any local wallet we could not map to a participant id so the
        // wallet-address-matching GetPendingActionsByWalletAsync query still resolves it.
        foreach (var wallet in localWallets)
        {
            if (!mirror.ParticipantWallets.ContainsValue(wallet))
            {
                mirror.ParticipantWallets[wallet] = wallet;
            }
        }

        if (existing is null)
        {
            await instanceStore.CreateMirrorAsync(mirror, ct);
            _metrics.RecordMirrorCreated();
            _logger.LogInformation(
                "InstanceMirrorReconstructor: created mirror for instance {InstanceId} (blueprint {BlueprintId}) with {WalletCount} local participant wallet(s)",
                instanceId, blueprintId, localWallets.Count);
        }
        else
        {
            mirror.Version = existing.Version; // UpdateMirrorAsync will bump
            await instanceStore.UpdateMirrorAsync(mirror, ct);
            _metrics.RecordMirrorUpdated();
            _logger.LogDebug(
                "InstanceMirrorReconstructor: advanced mirror for instance {InstanceId} to action {NextAction}",
                instanceId, nextAction);
        }
    }

}
