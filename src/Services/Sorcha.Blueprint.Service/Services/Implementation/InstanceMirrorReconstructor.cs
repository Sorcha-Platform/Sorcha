// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;

using StackExchange.Redis;

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
    private const string DocketConfirmedChannel = "docket:confirmed";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConnectionMultiplexer? _redis;
    private readonly InstanceMirrorReconstructorMetrics _metrics;
    private readonly ILogger<InstanceMirrorReconstructor> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public InstanceMirrorReconstructor(
        IServiceScopeFactory scopeFactory,
        InstanceMirrorReconstructorMetrics metrics,
        ILogger<InstanceMirrorReconstructor> logger,
        IConnectionMultiplexer? redis = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _redis = redis;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_redis is null)
        {
            _logger.LogWarning(
                "InstanceMirrorReconstructor: Redis not available — cross-node instance mirroring is disabled. " +
                "Feature 106 MyActions queries from a holder node will return empty until Redis is reachable.");
            return;
        }

        _logger.LogInformation(
            "InstanceMirrorReconstructor starting — subscribing to {Channel}",
            DocketConfirmedChannel);

        try
        {
            var subscriber = _redis.GetSubscriber();
            await subscriber.SubscribeAsync(
                RedisChannel.Literal(DocketConfirmedChannel),
                async (_, message) =>
                {
                    try
                    {
                        await HandleDocketConfirmedAsync(message!, stoppingToken);
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex,
                            "InstanceMirrorReconstructor: malformed docket:confirmed event");
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex,
                            "InstanceMirrorReconstructor: unexpected error processing docket:confirmed");
                        _metrics.RecordErrored();
                    }
                });

            _logger.LogInformation(
                "InstanceMirrorReconstructor subscribed to {Channel}", DocketConfirmedChannel);

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

    private async Task HandleDocketConfirmedAsync(string message, CancellationToken ct)
    {
        _metrics.RecordDocketObserved();
        var sw = Stopwatch.StartNew();

        var evt = JsonSerializer.Deserialize<DocketConfirmedEvent>(message, JsonOptions);
        if (evt?.TransactionIds is null || evt.TransactionIds.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();
        var walletClient = scope.ServiceProvider.GetRequiredService<IWalletServiceClient>();
        var instanceStore = scope.ServiceProvider.GetRequiredService<IInstanceStore>();

        foreach (var txId in evt.TransactionIds)
        {
            ct.ThrowIfCancellationRequested();
            _metrics.RecordTransactionInspected();

            try
            {
                await InspectTransactionAsync(evt.RegisterId, txId, registerClient, walletClient, instanceStore, ct);
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

        // Build (or refresh) the mirror row. The ParticipantWallets map is keyed by
        // participant id, but from the tx alone we don't know the mapping — seed
        // with the wallet addresses keyed by themselves so GetPendingActionsByWalletAsync
        // can still match them. A richer blueprint-aware reconstruction can be
        // bolted on later without changing the persistence shape.
        //
        // ⚠️ TODO(feature-106-follow-up): self-keying breaks role-based routing on
        // the mirror. Any action dispatch that resolves by participant id instead
        // of wallet address will fail to find the participant. Two options:
        //   (a) thread participant ids through TransactionMetaData at write time
        //       and read them here for a structurally valid mirror, or
        //   (b) fetch the blueprint on this node and walk its participants to
        //       reverse-map wallet → participant id when the blueprint has
        //       pre-bound wallets.
        // Acceptable for MVP because MyCredentials PENDING tab and the
        // GetPendingActionsByWalletAsync query both match on wallet address
        // directly. Tracked as a known gap in specs/106-register-native-credentials.
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

        // Merge newly-discovered local wallets into the participant map keyed by
        // the wallet address (self-keyed) so the pending-actions query matches.
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

    /// <summary>
    /// Shape of the <c>docket:confirmed</c> event payload published by
    /// Register Service. Field names match
    /// <see cref="TransactionLifecycleEventBridge"/> in Wallet Service.
    /// </summary>
    private sealed record DocketConfirmedEvent
    {
        public string RegisterId { get; init; } = string.Empty;
        public ulong DocketId { get; init; }
        public List<string> TransactionIds { get; init; } = [];
        public string Hash { get; init; } = string.Empty;
    }
}
