// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;

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
    private readonly IDbContextFactory<PeerDbContext>? _dbContextFactory;
    private readonly RegisterSyncConfiguration _syncConfig;
    private readonly ConcurrentDictionary<string, RegisterSubscription> _subscriptions = new();
    private readonly ConcurrentDictionary<string, Task> _liveSubscriptionTasks = new();
    private CancellationTokenSource? _serviceCts;

    public RegisterSyncBackgroundService(
        ILogger<RegisterSyncBackgroundService> logger,
        RegisterReplicationService replicationService,
        IOptions<PeerServiceConfiguration> configuration,
        IDbContextFactory<PeerDbContext>? dbContextFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _replicationService = replicationService ?? throw new ArgumentNullException(nameof(replicationService));
        _dbContextFactory = dbContextFactory;
        _syncConfig = configuration?.Value?.RegisterSync ?? new RegisterSyncConfiguration();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RegisterSyncBackgroundService starting");
        _serviceCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // Load existing subscriptions from database
        await LoadSubscriptionsAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(_syncConfig.PeriodicSyncIntervalMinutes));

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptionsAsync(stoppingToken);
                    await timer.WaitForNextTickAsync(stoppingToken);
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

    private async Task ProcessSubscriptionsAsync(CancellationToken cancellationToken)
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
                // Transition based on mode
                subscription.TransitionToNextState();
                await PersistSubscriptionAsync(subscription, cancellationToken);
                break;

            case RegisterSyncState.Syncing:
                // Full replica mode: pull docket chain
                var result = await _replicationService.PullFullReplicaAsync(subscription, cancellationToken);
                if (result.Success)
                {
                    subscription.TransitionToNextState();
                    _logger.LogInformation(
                        "Register {RegisterId} fully replicated ({Dockets} dockets, {Txs} transactions)",
                        subscription.RegisterId, result.DocketsSynced, result.TransactionsSynced);
                }
                await PersistSubscriptionAsync(subscription, cancellationToken);
                break;

            case RegisterSyncState.FullyReplicated:
            case RegisterSyncState.Active:
                // Subscribe to live transactions (no-op if already streaming)
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
                }
                break;
        }
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
                context.Entry(existing).CurrentValues.SetValues(entity);
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
}
