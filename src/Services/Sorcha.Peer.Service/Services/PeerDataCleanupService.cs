// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Services;

/// <summary>
/// Background service that periodically cleans stale transitory data from PostgreSQL.
/// Evicts dead peers, expires timed bans, purges completed/expired queued transactions,
/// and removes orphaned sync checkpoints. Also cleans up Redis advertisements for evicted peers.
/// </summary>
public class PeerDataCleanupService : BackgroundService
{
    private readonly ILogger<PeerDataCleanupService> _logger;
    private readonly PeerListManager _peerListManager;
    private readonly IDbContextFactory<PeerDbContext>? _dbContextFactory;
    private readonly IRedisAdvertisementStore? _advertisementStore;
    private readonly DataCleanupConfiguration _config;

    public PeerDataCleanupService(
        ILogger<PeerDataCleanupService> logger,
        PeerListManager peerListManager,
        IOptions<PeerServiceConfiguration> configuration,
        IDbContextFactory<PeerDbContext>? dbContextFactory = null,
        IRedisAdvertisementStore? advertisementStore = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _dbContextFactory = dbContextFactory;
        _advertisementStore = advertisementStore;
        _config = configuration?.Value?.DataCleanup ?? new DataCleanupConfiguration();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "PeerDataCleanupService starting (interval: {Interval}min, stale threshold: {Stale}min, checkpoint interval: {Checkpoint}min)",
            _config.CleanupIntervalMinutes, _config.StalePeerMinutes, _config.CheckpointCleanupIntervalMinutes);

        var cleanupInterval = TimeSpan.FromMinutes(_config.CleanupIntervalMinutes);
        var checkpointInterval = TimeSpan.FromMinutes(_config.CheckpointCleanupIntervalMinutes);
        var lastCheckpointCleanup = DateTimeOffset.UtcNow;

        // Delay initial run to let the service stabilise
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPrimaryCleanupAsync(stoppingToken);

                if (DateTimeOffset.UtcNow - lastCheckpointCleanup >= checkpointInterval)
                {
                    await PurgeOrphanedCheckpointsAsync(stoppingToken);
                    lastCheckpointCleanup = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during peer data cleanup sweep");
            }

            await Task.Delay(cleanupInterval, stoppingToken);
        }

        _logger.LogInformation("PeerDataCleanupService stopped");
    }

    /// <summary>
    /// Runs the primary cleanup: stale peers, expired bans, completed/expired transactions.
    /// </summary>
    internal async Task RunPrimaryCleanupAsync(CancellationToken cancellationToken)
    {
        var staleThreshold = TimeSpan.FromMinutes(_config.StalePeerMinutes);

        // 1. Evict stale peers (in-memory + database)
        var evictedPeers = await _peerListManager.EvictStalePeersAsync(staleThreshold, cancellationToken);

        // 1b. Clean up Redis advertisements for evicted peers
        if (evictedPeers.Count > 0 && _advertisementStore != null)
        {
            foreach (var peerId in evictedPeers)
            {
                try
                {
                    await _advertisementStore.RemoveRemoteByPeerAsync(peerId, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up Redis advertisements for evicted peer {PeerId}", peerId);
                }
            }
        }

        // 2. Unban expired bans (in-memory + database)
        var unbannedCount = await _peerListManager.UnbanExpiredAsync(cancellationToken);

        // 3. Purge completed/failed/expired queued transactions
        var purgedTransactions = await PurgeQueuedTransactionsAsync(cancellationToken);

        if (evictedPeers.Count > 0 || unbannedCount > 0 || purgedTransactions > 0)
        {
            _logger.LogInformation(
                "Cleanup sweep: evicted {Evicted} stale peers, unbanned {Unbanned} expired bans, purged {Purged} queued transactions",
                evictedPeers.Count, unbannedCount, purgedTransactions);
        }
    }

    /// <summary>
    /// Purges completed, failed, and TTL-expired queued transactions from the database.
    /// </summary>
    internal async Task<int> PurgeQueuedTransactionsAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return 0;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var retentionCutoff = now.AddMinutes(-_config.CompletedTransactionRetentionMinutes);

            // Find completed/failed transactions past retention period
            var completedExpired = await context.QueuedTransactions
                .Where(t => (t.Status == "Completed" || t.Status == "Failed") && t.EnqueuedAt < retentionCutoff)
                .ToListAsync(cancellationToken);

            // Find TTL-expired transactions regardless of status
            var allTransactions = await context.QueuedTransactions.ToListAsync(cancellationToken);
            var ttlExpired = allTransactions
                .Where(t => t.EnqueuedAt.AddSeconds(t.TTL) < now)
                .ToList();

            var toRemove = completedExpired.Union(ttlExpired).Distinct().ToList();
            if (toRemove.Count > 0)
            {
                context.QueuedTransactions.RemoveRange(toRemove);
                await context.SaveChangesAsync(cancellationToken);
            }

            return toRemove.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to purge queued transactions");
            return 0;
        }
    }

    /// <summary>
    /// Removes sync checkpoints that reference peers no longer in the Peers table.
    /// </summary>
    internal async Task PurgeOrphanedCheckpointsAsync(CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

            var activePeerIds = await context.Peers
                .Select(p => p.PeerId)
                .ToListAsync(cancellationToken);

            var orphaned = await context.SyncCheckpoints
                .Where(c => !activePeerIds.Contains(c.PeerId))
                .ToListAsync(cancellationToken);

            if (orphaned.Count > 0)
            {
                context.SyncCheckpoints.RemoveRange(orphaned);
                await context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Purged {Count} orphaned sync checkpoints", orphaned.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to purge orphaned sync checkpoints");
        }
    }
}
