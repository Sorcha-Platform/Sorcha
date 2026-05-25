// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;
using Sorcha.ServiceClients.Register;
using RelayModels = Sorcha.Peer.Service.Communication.Models;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Background service that manages per-register replication lifecycle.
/// Orchestrates sync state transitions for each subscribed register:
/// - ForwardOnly: Subscribing → Active
/// - FullReplica: Subscribing → Syncing → FullyReplicated
/// </summary>
public class RegisterSyncBackgroundService : BackgroundService
{
    private readonly ILogger<RegisterSyncBackgroundService> _logger;
    private readonly RegisterReplicationService _replicationService;
    private readonly RelayCommunicationService? _relayCommunication;
    private readonly PeerListManager? _peerListManager;
    private readonly RegisterCache? _registerCache;
    private readonly IDbContextFactory<PeerDbContext>? _dbContextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RegisterSyncConfiguration _syncConfig;
    private readonly ConcurrentDictionary<string, RegisterSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, Task> _liveSubscriptionTasks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _syncSemaphores = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _offlineDebounceTimers = new();
    private readonly ManualResetEventSlim _immediateSyncSignal = new(false);
    private static readonly TimeSpan OfflineDebounceDelay = TimeSpan.FromSeconds(30);
    private CancellationTokenSource? _serviceCts;

    public RegisterSyncBackgroundService(
        ILogger<RegisterSyncBackgroundService> logger,
        RegisterReplicationService replicationService,
        IOptions<PeerServiceConfiguration> configuration,
        IServiceScopeFactory scopeFactory,
        IDbContextFactory<PeerDbContext>? dbContextFactory = null,
        RelayCommunicationService? relayCommunication = null,
        PeerListManager? peerListManager = null,
        RegisterCache? registerCache = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _replicationService = replicationService ?? throw new ArgumentNullException(nameof(replicationService));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _relayCommunication = relayCommunication;
        _peerListManager = peerListManager;
        _registerCache = registerCache;
        _dbContextFactory = dbContextFactory;
        _syncConfig = configuration?.Value?.RegisterSync ?? new RegisterSyncConfiguration();
    }

    /// <summary>
    /// Gets or creates the per-register sync semaphore.
    /// </summary>
    public SemaphoreSlim GetSyncSemaphore(string registerId)
    {
        return _syncSemaphores.GetOrAdd(registerId, _ => new SemaphoreSlim(1, 1));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegisterSyncBackgroundService starting");
        _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Load existing subscriptions from database
        await LoadSubscriptionsAsync(stoppingToken);

        var periodicInterval = TimeSpan.FromMinutes(_syncConfig.PeriodicSyncIntervalMinutes);

        // Establish reverse stream to seed node before processing subscriptions
        Task? reverseStreamTask = null;
        if (_relayCommunication != null)
        {
            reverseStreamTask = Task.Run(
                () => _relayCommunication.EstablishReverseStreamAsync(stoppingToken),
                stoppingToken);
        }

        // Start relay poll loop if relay is available
        Task? relayPollTask = null;
        if (_relayCommunication != null && _peerListManager != null && _registerCache != null)
        {
            relayPollTask = RunRelayPollLoopAsync(stoppingToken);
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptionsAsync(stoppingToken);

                    // Wait for either the periodic interval or an immediate sync signal.
                    //
                    // The earlier implementation used a single PeriodicTimer shared across
                    // iterations and Task.WhenAny(timer.WaitForNextTickAsync(), signalTask).
                    // When the signal won the race, the timer's pending WaitForNextTickAsync
                    // was orphaned but PeriodicTimer holds internal "wait in progress" state
                    // — the next iteration's WaitForNextTickAsync threw InvalidOperationException
                    // and the loop never made another pass. (PeriodicTimer disallows concurrent
                    // or interleaved waiters by design.)
                    //
                    // Fresh linked CTS per iteration ensures whichever side loses the race is
                    // cancelled cleanly before the next iteration starts; Task.Delay is
                    // stateless across iterations, so no leaked state survives.
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    var delayTask = Task.Delay(periodicInterval, waitCts.Token);
                    var signalTask = Task.Run(
                        () => _immediateSyncSignal.Wait(waitCts.Token),
                        waitCts.Token);
                    await Task.WhenAny(delayTask, signalTask);
                    _immediateSyncSignal.Reset();
                    waitCts.Cancel();
                    // Drain both tasks so cancellation completes before next iteration —
                    // expected OperationCanceledException is swallowed.
                    try { await delayTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                    try { await signalTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in RegisterSyncBackgroundService loop");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }
        finally
        {
            // Cancel and await all live subscription tasks
            _serviceCts.Cancel();

            if (reverseStreamTask != null)
            {
                try { await reverseStreamTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            if (relayPollTask != null)
            {
                try { await relayPollTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            var activeTasks = _liveSubscriptionTasks.Values.ToArray();
            if (activeTasks.Length > 0)
            {
                _logger.LogInformation("Waiting for {Count} live subscription tasks to complete", activeTasks.Length);
                await Task.WhenAll(activeTasks).ConfigureAwait(false);
            }
            _serviceCts.Dispose();
        }

        _logger.LogInformation("RegisterSyncBackgroundService stopped");
    }

    /// <summary>
    /// Periodic relay sync poll loop. Fires every RelayPollIntervalSeconds to sync
    /// subscribed registers from NAT'd peers via relay as a safety net for missed notifications.
    /// </summary>
    private async Task RunRelayPollLoopAsync(CancellationToken stoppingToken)
    {
        using var relayTimer = new PeriodicTimer(
            TimeSpan.FromSeconds(_syncConfig.RelayPollIntervalSeconds));

        _logger.LogInformation(
            "Relay poll loop started with interval {Interval}s",
            _syncConfig.RelayPollIntervalSeconds);

        try
        {
            while (await relayTimer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var (registerId, subscription) in _subscriptions.ToList())
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        await TryRelayPollForRegisterAsync(registerId, subscription, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Error in relay poll for register {RegisterId}", registerId);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("Relay poll loop cancelled");
        }
    }

    /// <summary>
    /// Attempts relay sync poll for a single register. Finds NAT'd peers and sends
    /// sync requests, stopping after the first successful response.
    /// </summary>
    private async Task TryRelayPollForRegisterAsync(
        string registerId,
        RegisterSubscription subscription,
        CancellationToken cancellationToken)
    {
        var semaphore = GetSyncSemaphore(registerId);
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogDebug("Relay poll skipped for register {RegisterId} — sync already in progress", registerId);
            return;
        }

        try
        {
            var peers = _peerListManager!.GetPeersForRegister(registerId);
            var natdPeers = peers.Where(p => string.IsNullOrEmpty(p.Address)).ToList();

            if (natdPeers.Count == 0) return;

            var cacheEntry = _registerCache!.GetOrCreate(registerId);

            foreach (var peer in natdPeers)
            {
                var correlationId = Guid.NewGuid().ToString();
                var syncRequest = new RelayModels.RegisterSyncRequest
                {
                    CorrelationId = correlationId,
                    RegisterId = registerId,
                    FromDocketVersion = subscription.LastSyncedDocketVersion
                };

                var response = await _relayCommunication!.SendAndWaitAsync<RelayModels.RegisterSyncResponse>(
                    peer.PeerId,
                    MessageType.RegisterSyncRequest,
                    syncRequest,
                    correlationId,
                    cancellationToken: cancellationToken);

                if (response != null)
                {
                    foreach (var docket in response.Dockets)
                    {
                        cacheEntry.AddOrUpdateDocket(new CachedDocket
                        {
                            RegisterId = registerId,
                            Version = docket.Version,
                            Data = docket.Data,
                            DocketHash = docket.DocketHash,
                            PreviousHash = docket.PreviousHash,
                            TransactionIds = docket.TransactionIds,
                            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(docket.CreatedAt)
                        });
                    }

                    _logger.LogDebug(
                        "Relay poll synced {Count} dockets for register {RegisterId} from peer {PeerId}",
                        response.Dockets.Count, registerId, peer.PeerId);

                    // Stop after first successful peer per register
                    return;
                }
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Adds a new register subscription.
    /// </summary>
    public async Task<RegisterSubscription> SubscribeToRegisterAsync(
        string registerId,
        ReplicationMode mode,
        CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryGetValue(registerId, out var existing))
        {
            _logger.LogWarning("Already subscribed to register {RegisterId}", registerId);
            return existing;
        }

        var subscription = new RegisterSubscription
        {
            RegisterId = registerId,
            Mode = mode,
            SyncState = RegisterSyncState.Subscribing
        };

        _subscriptions[registerId] = subscription;
        await PersistSubscriptionAsync(subscription, cancellationToken);

        _logger.LogInformation(
            "Subscribed to register {RegisterId} with mode {Mode}",
            registerId, mode);

        // Signal immediate sync instead of waiting for periodic timer
        _immediateSyncSignal.Set();

        return subscription;
    }

    /// <summary>
    /// Removes a register subscription.
    /// </summary>
    public async Task UnsubscribeFromRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        if (_subscriptions.TryRemove(registerId, out _))
        {
            // Stop live subscription if running
            if (_liveSubscriptionTasks.TryRemove(registerId, out var liveTask) && !liveTask.IsCompleted)
            {
                _logger.LogDebug("Waiting for live subscription task to stop for register {RegisterId}", registerId);
                await liveTask.ConfigureAwait(false);
            }

            // Dispose the per-register semaphore
            if (_syncSemaphores.TryRemove(registerId, out var semaphore))
            {
                semaphore.Dispose();
            }

            await DeleteSubscriptionAsync(registerId, cancellationToken);
            _logger.LogInformation("Unsubscribed from register {RegisterId}", registerId);
        }
    }

    /// <summary>
    /// Gets all current subscriptions.
    /// </summary>
    public IReadOnlyCollection<RegisterSubscription> GetSubscriptions()
    {
        return _subscriptions.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets a specific subscription by register ID.
    /// </summary>
    public RegisterSubscription? GetSubscription(string registerId)
    {
        _subscriptions.TryGetValue(registerId, out var sub);
        return sub;
    }

    internal async Task ProcessSubscriptionsAsync(CancellationToken cancellationToken)
    {
        foreach (var (registerId, subscription) in _subscriptions.ToList())
        {
            try
            {
                await ProcessSubscriptionAsync(subscription, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error processing subscription for register {RegisterId}",
                    registerId);
                subscription.RecordSyncFailure(ex.Message);
            }
        }
    }

    private async Task ProcessSubscriptionAsync(
        RegisterSubscription subscription,
        CancellationToken cancellationToken)
    {
        switch (subscription.SyncState)
        {
            case RegisterSyncState.Subscribing:
                // Transition based on mode (FullReplica → Syncing, ForwardOnly → Active).
                subscription.TransitionToNextState();
                await PersistSubscriptionAsync(subscription, cancellationToken);
                await ReportSyncStatusAsync(subscription.RegisterId, subscription.SyncState, cancellationToken);

                // #473: re-fire the immediate-sync signal so the new state is
                // processed in the same iteration burst rather than waiting a
                // full PeriodicSyncIntervalMinutes (default 5 min). Without
                // this, a fresh subscription stutters: iteration 1 transitions
                // Subscribing→Syncing and returns; the actual PullFullReplica
                // doesn't happen until iteration 2, after the periodic delay.
                _immediateSyncSignal.Set();
                break;

            case RegisterSyncState.Syncing:
                // Full replica mode: pull docket chain
                await ReportSyncStatusAsync(subscription.RegisterId, RegisterSyncState.Syncing, cancellationToken);
                var result = await _replicationService.PullFullReplicaAsync(subscription, cancellationToken);
                if (result.Success)
                {
                    subscription.TransitionToNextState();
                    _logger.LogInformation(
                        "Register {RegisterId} fully replicated ({Dockets} dockets, {Txs} transactions)",
                        subscription.RegisterId, result.DocketsSynced, result.TransactionsSynced);
                    await ReportSyncStatusAsync(subscription.RegisterId, subscription.SyncState, cancellationToken);
                    // Same rationale as Subscribing → kick the loop so EnsureLiveSubscription
                    // for the FullyReplicated state runs without another 5-min wait.
                    _immediateSyncSignal.Set();
                }
                await PersistSubscriptionAsync(subscription, cancellationToken);
                break;

            case RegisterSyncState.FullyReplicated:
                // Subscribe to live transactions (no-op if already streaming)
                EnsureLiveSubscription(subscription);
                // Safety-net incremental re-pull. The live subscription is the low-latency path
                // for new dockets, but it is best-effort: on a TLS-terminated seed node the
                // bidirectional reverse stream may be unavailable (Caddy does not relay h2c
                // bidi on the gRPC vhost), so a FullyReplicated replica would otherwise sit
                // indefinitely behind the owner — sealed credentials/results never arrive. Each
                // periodic pass we re-pull any dockets sealed since LastSyncedDocketVersion via
                // the same direct PullDocketChain path that performed the initial sync. Cheap
                // no-op when already current.
                await TryIncrementalResyncAsync(subscription, cancellationToken);
                break;

            case RegisterSyncState.Active:
                // ForwardOnly mode: live transactions only (no chain to keep replicated).
                EnsureLiveSubscription(subscription);
                break;

            case RegisterSyncState.Error:
                // Retry after cooldown
                if (subscription.ConsecutiveFailures < _syncConfig.MaxRetryAttempts)
                {
                    _logger.LogInformation(
                        "Retrying register {RegisterId} after {Failures} failures",
                        subscription.RegisterId, subscription.ConsecutiveFailures);
                    subscription.SyncState = RegisterSyncState.Subscribing;
                    await PersistSubscriptionAsync(subscription, cancellationToken);
                    _immediateSyncSignal.Set();
                }
                break;
        }
    }

    /// <summary>
    /// Safety-net incremental re-pull for a fully-replicated register. Runs each periodic pass
    /// alongside the live subscription so that, when the live (reverse-stream) path is
    /// unavailable, the replica still converges on the owner by pulling dockets sealed since
    /// <see cref="RegisterSubscription.LastSyncedDocketVersion"/>. Delegates to
    /// <see cref="RegisterReplicationService.PullFullReplicaAsync"/> (incremental from the last
    /// synced version via the direct <c>PullDocketChain</c> channel; finalised dockets are
    /// written through the Register Service, which routes inbound transactions to local wallets).
    /// Guarded by the per-register semaphore so it never overlaps the live sync or relay poll;
    /// failures are non-fatal (the live subscription remains the primary path).
    /// </summary>
    private async Task TryIncrementalResyncAsync(
        RegisterSubscription subscription,
        CancellationToken cancellationToken)
    {
        var semaphore = GetSyncSemaphore(subscription.RegisterId);
        if (!await semaphore.WaitAsync(0, cancellationToken))
        {
            _logger.LogDebug(
                "Incremental resync skipped for register {RegisterId} — sync already in progress",
                subscription.RegisterId);
            return;
        }

        try
        {
            var beforeVersion = subscription.LastSyncedDocketVersion;
            var result = await _replicationService.PullFullReplicaAsync(subscription, cancellationToken);

            if (result.Success && result.DocketsSynced > 0)
            {
                _logger.LogInformation(
                    "Incremental resync pulled {Dockets} docket(s)/{Txs} transaction(s) for register {RegisterId} (v{Before} → v{After})",
                    result.DocketsSynced, result.TransactionsSynced, subscription.RegisterId,
                    beforeVersion, subscription.LastSyncedDocketVersion);
                await PersistSubscriptionAsync(subscription, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Incremental resync failed for register {RegisterId} (non-critical; live subscription remains primary)",
                subscription.RegisterId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Test seam: returns true and resets the immediate-sync signal if it was
    /// set, otherwise returns false. Used by regression tests for #473 to
    /// assert the signal is re-fired on state transitions.
    /// </summary>
    internal bool ConsumeImmediateSyncSignal()
    {
        if (!_immediateSyncSignal.IsSet) return false;
        _immediateSyncSignal.Reset();
        return true;
    }

    /// <summary>
    /// Ensures a live subscription task is running for the given register.
    /// No-op if the task is already active.
    /// </summary>
    private void EnsureLiveSubscription(RegisterSubscription subscription)
    {
        var registerId = subscription.RegisterId;

        // Check if there's already an active (non-completed) task
        if (_liveSubscriptionTasks.TryGetValue(registerId, out var existingTask)
            && !existingTask.IsCompleted)
        {
            return;
        }

        var token = _serviceCts?.Token ?? CancellationToken.None;
        var task = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation(
                    "Starting live subscription for register {RegisterId}",
                    registerId);
                await _replicationService.SubscribeToLiveTransactionsAsync(subscription, token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Live subscription cancelled for register {RegisterId}", registerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live subscription failed for register {RegisterId}", registerId);
                subscription.RecordSyncFailure($"Live subscription error: {ex.Message}");
            }
            finally
            {
                _liveSubscriptionTasks.TryRemove(registerId, out _);
            }
        }, token);

        _liveSubscriptionTasks[registerId] = task;
    }

    private async Task LoadSubscriptionsAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entities = await context.RegisterSubscriptions.ToListAsync(cancellationToken);

            foreach (var entity in entities)
            {
                var sub = entity.ToDomain();
                _subscriptions[sub.RegisterId] = sub;
            }

            _logger.LogInformation("Loaded {Count} register subscriptions from database",
                entities.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading register subscriptions from database");
        }
    }

    private async Task PersistSubscriptionAsync(
        RegisterSubscription subscription,
        CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = RegisterSubscriptionEntity.FromDomain(subscription);

            var existing = await context.RegisterSubscriptions
                .FirstOrDefaultAsync(s => s.RegisterId == subscription.RegisterId, cancellationToken);

            if (existing != null)
            {
                existing.Mode = entity.Mode;
                existing.SyncState = entity.SyncState;
                existing.LastSyncedDocketVersion = entity.LastSyncedDocketVersion;
                existing.LastSyncedTransactionVersion = entity.LastSyncedTransactionVersion;
                existing.TotalDocketsInChain = entity.TotalDocketsInChain;
                existing.SourcePeerIds = entity.SourcePeerIds;
                existing.LastSyncAt = entity.LastSyncAt;
                existing.ErrorMessage = entity.ErrorMessage;
                existing.ConsecutiveFailures = entity.ConsecutiveFailures;
            }
            else
            {
                context.RegisterSubscriptions.Add(entity);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting subscription for register {RegisterId}",
                subscription.RegisterId);
        }
    }

    private async Task DeleteSubscriptionAsync(string registerId, CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.RegisterSubscriptions
                .FirstOrDefaultAsync(s => s.RegisterId == registerId, cancellationToken);

            if (entity != null)
            {
                context.RegisterSubscriptions.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting subscription for register {RegisterId}", registerId);
        }
    }

    /// <summary>
    /// Disposes all per-register sync semaphores and base resources.
    /// </summary>
    public override void Dispose()
    {
        foreach (var semaphore in _syncSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _syncSemaphores.Clear();
        _immediateSyncSignal.Dispose();

        foreach (var cts in _offlineDebounceTimers.Values)
            cts.Dispose();
        _offlineDebounceTimers.Clear();

        base.Dispose();
    }

    /// <summary>
    /// Starts a 30-second debounce timer before reporting a register as Offline.
    /// If the source peer reconnects within 30 seconds, the timer is cancelled
    /// and no Offline status is reported (prevents status flapping).
    /// </summary>
    public void StartOfflineDebounce(string registerId)
    {
        // Cancel any existing debounce for this register
        CancelOfflineDebounce(registerId);

        var cts = new CancellationTokenSource();
        _offlineDebounceTimers[registerId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(OfflineDebounceDelay, cts.Token);

                // Timer expired — report Offline
                _logger.LogWarning(
                    "Offline debounce expired for register {RegisterId} — reporting Offline",
                    registerId);
                await ReportSyncStatusAsync(registerId, RegisterSyncState.Error, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Debounce cancelled — source peer reconnected within 30s
                _logger.LogDebug(
                    "Offline debounce cancelled for register {RegisterId} — peer reconnected",
                    registerId);
            }
            finally
            {
                _offlineDebounceTimers.TryRemove(registerId, out _);
            }
        });
    }

    /// <summary>
    /// Cancels an active offline debounce timer. Called when a source peer reconnects.
    /// If no debounce is active, reports Checking status to begin recovery.
    /// </summary>
    public void CancelOfflineDebounce(string registerId)
    {
        if (_offlineDebounceTimers.TryRemove(registerId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    /// <summary>
    /// Called when a source peer reconnects for a register.
    /// Cancels any offline debounce and reports Checking status.
    /// </summary>
    public async Task OnSourcePeerReconnectedAsync(string registerId, CancellationToken cancellationToken)
    {
        CancelOfflineDebounce(registerId);
        await ReportSyncStatusAsync(registerId, RegisterSyncState.Subscribing, cancellationToken);
    }

    /// <summary>
    /// Reports sync state changes to the Register Service so it can update RegisterStatus.
    /// Fire-and-forget — failures are logged but don't block sync processing.
    /// </summary>
    private async Task ReportSyncStatusAsync(
        string registerId,
        RegisterSyncState syncState,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var registerClient = scope.ServiceProvider.GetRequiredService<IRegisterServiceClient>();

            await registerClient.ReportSyncStatusAsync(
                registerId,
                syncState.ToString(),
                peerConnectionActive: syncState != RegisterSyncState.Error,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to report sync status {SyncState} for register {RegisterId} (non-critical)",
                syncState, registerId);
        }
    }
}
