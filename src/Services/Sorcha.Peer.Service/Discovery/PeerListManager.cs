// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Data;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Discovery;

/// <summary>
/// Manages the list of known peers with EF Core persistence (PostgreSQL or InMemory).
/// Provides register-aware queries for targeted sync and gossip.
/// </summary>
public class PeerListManager : IDisposable
{
    private readonly ILogger<PeerListManager> _logger;
    private readonly PeerDiscoveryConfiguration _configuration;
    private readonly string _localPeerId;
    private readonly ConcurrentDictionary<string, PeerNode> _peers;
    private readonly IDbContextFactory<PeerDbContext>? _dbContextFactory;
    private bool _disposed;
    private ActivePeerInfo? _localPeerInfo;

    public PeerListManager(
        ILogger<PeerListManager> logger,
        IOptions<PeerServiceConfiguration> configuration,
        IDbContextFactory<PeerDbContext>? dbContextFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var config = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _configuration = config.PeerDiscovery;
        _localPeerId = config.ResolvedPeerId;
        _peers = new ConcurrentDictionary<string, PeerNode>();
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>
    /// Gets all known peers
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetAllPeers()
    {
        return _peers.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets healthy peers (low failure count, recently seen, not banned)
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetHealthyPeers()
    {
        var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-_configuration.RefreshIntervalMinutes * 2);

        return _peers.Values
            .Where(p => !p.IsBanned && p.FailureCount < 3 && p.LastSeen > cutoffTime)
            .OrderBy(p => p.FailureCount)
            .ThenByDescending(p => p.LastSeen)
            .Take(_configuration.MinHealthyPeers * 2)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets all healthy peers without the subset cap applied by GetHealthyPeers().
    /// Use this for aggregate queries (e.g. building the full available registers list)
    /// where truncation would silently omit valid data.
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetAllHealthyPeers()
    {
        var cutoffTime = DateTimeOffset.UtcNow.AddMinutes(-_configuration.RefreshIntervalMinutes * 2);

        return _peers.Values
            .Where(p => !p.IsBanned && p.FailureCount < 3 && p.LastSeen > cutoffTime)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets a random selection of peers for gossip
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetRandomPeers(int count)
    {
        var healthyPeers = GetHealthyPeers();
        return healthyPeers
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets peers that advertise a specific register (register-aware peering).
    /// Intentionally includes degraded peers (high failure count, stale) because
    /// replication should attempt all known sources — gRPC calls will fail gracefully
    /// if the peer is unreachable, and the failure count will trigger eventual eviction.
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetPeersForRegister(string registerId)
    {
        return _peers.Values
            .Where(p => p.AdvertisedRegisters.Any(r => r.RegisterId == registerId))
            .OrderBy(p => p.FailureCount)
            .ThenByDescending(p => p.LastSeen)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets peers that can serve a full replica for a specific register
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetFullReplicaPeersForRegister(string registerId)
    {
        return _peers.Values
            .Where(p => p.AdvertisedRegisters.Any(r =>
                r.RegisterId == registerId && r.CanServeFullReplica))
            .OrderBy(p => p.AverageLatencyMs)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets the configured seed node(s). A full-replica / SyncOnly node replicates FROM its
    /// seed by configuration, so the seed is the authoritative replication source of last
    /// resort when no peer has advertised a subscribed register (e.g. advertisements have not
    /// been re-exchanged after a restart). Seed channels are established at bootstrap, so the
    /// server-streaming pull path works against them even when the bidirectional live stream
    /// does not.
    /// </summary>
    public IReadOnlyCollection<PeerNode> GetSeedNodes()
    {
        return _peers.Values
            .Where(p => p.IsSeedNode)
            .OrderBy(p => p.FailureCount)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Adds or updates a peer in the list
    /// </summary>
    public async Task<bool> AddOrUpdatePeerAsync(PeerNode peer, CancellationToken cancellationToken = default)
    {
        if (peer == null)
            throw new ArgumentNullException(nameof(peer));

        if (string.IsNullOrEmpty(peer.PeerId))
        {
            _logger.LogWarning("Cannot add peer with empty PeerId");
            return false;
        }

        if (_peers.Count >= _configuration.MaxPeersInList && !_peers.ContainsKey(peer.PeerId))
        {
            _logger.LogWarning("Peer list is full ({Count}/{Max}), cannot add new peer",
                _peers.Count, _configuration.MaxPeersInList);
            return false;
        }

        var isNew = _peers.TryAdd(peer.PeerId, peer);
        if (!isNew)
        {
            _peers[peer.PeerId] = peer;
        }

        _logger.LogDebug("{Action} peer: {PeerId} at {Address}:{Port}",
            isNew ? "Added" : "Updated", peer.PeerId, peer.Address, peer.Port);

        await PersistPeerAsync(peer, cancellationToken);
        return true;
    }

    /// <summary>
    /// Removes a peer from the list
    /// </summary>
    public async Task<bool> RemovePeerAsync(string peerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(peerId))
            return false;

        var removed = _peers.TryRemove(peerId, out _);

        if (removed)
        {
            _logger.LogInformation("Removed peer: {PeerId}", peerId);
            await DeletePeerAsync(peerId, cancellationToken);
        }

        return removed;
    }

    /// <summary>
    /// Gets a specific peer by ID
    /// </summary>
    public PeerNode? GetPeer(string peerId)
    {
        _peers.TryGetValue(peerId, out var peer);
        return peer;
    }

    /// <summary>
    /// Updates the last seen time for a peer
    /// </summary>
    public async Task UpdateLastSeenAsync(string peerId, CancellationToken cancellationToken = default)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            peer.LastSeen = DateTimeOffset.UtcNow;
            peer.FailureCount = 0;
            await PersistPeerAsync(peer, cancellationToken);
        }
    }

    /// <summary>
    /// Increments the failure count for a peer
    /// </summary>
    public async Task IncrementFailureCountAsync(string peerId, CancellationToken cancellationToken = default)
    {
        if (_peers.TryGetValue(peerId, out var peer))
        {
            peer.FailureCount++;
            _logger.LogWarning("Peer {PeerId} failure count: {Count}", peerId, peer.FailureCount);

            if (peer.FailureCount >= 5 && !peer.IsSeedNode)
            {
                _logger.LogWarning("Removing peer {PeerId} due to excessive failures", peerId);
                await RemovePeerAsync(peerId, cancellationToken);
            }
            else
            {
                await PersistPeerAsync(peer, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Bans a peer, preventing all communication.
    /// If duration is provided, the ban auto-expires after that time. If null, the ban is permanent.
    /// </summary>
    public async Task<bool> BanPeerAsync(string peerId, string? reason = null, TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        if (!_peers.TryGetValue(peerId, out var peer))
            return false;

        if (peer.IsBanned)
            return false;

        if (peer.IsSeedNode)
        {
            _logger.LogWarning("Banning seed node {PeerId} — operator override", peerId);
        }

        peer.IsBanned = true;
        peer.BannedAt = DateTimeOffset.UtcNow;
        peer.BanReason = reason;
        peer.BanExpiresAt = duration.HasValue ? DateTimeOffset.UtcNow + duration.Value : null;

        _logger.LogInformation("Banned peer {PeerId}: {Reason} (expires: {Expires})",
            peerId, reason ?? "(no reason)", peer.BanExpiresAt?.ToString("o") ?? "never");
        await PersistPeerAsync(peer, cancellationToken);
        return true;
    }

    /// <summary>
    /// Unbans a peer, restoring normal communication.
    /// </summary>
    public async Task<bool> UnbanPeerAsync(string peerId, CancellationToken cancellationToken = default)
    {
        if (!_peers.TryGetValue(peerId, out var peer))
            return false;

        if (!peer.IsBanned)
            return false;

        peer.IsBanned = false;
        peer.BannedAt = null;
        peer.BanReason = null;

        _logger.LogInformation("Unbanned peer {PeerId}", peerId);
        await PersistPeerAsync(peer, cancellationToken);
        return true;
    }

    /// <summary>
    /// Resets a peer's failure count to zero. Returns the previous failure count, or -1 if peer not found.
    /// </summary>
    public async Task<int> ResetFailureCountAsync(string peerId, CancellationToken cancellationToken = default)
    {
        if (!_peers.TryGetValue(peerId, out var peer))
            return -1;

        var previous = peer.FailureCount;
        peer.FailureCount = 0;

        _logger.LogInformation("Reset failure count for peer {PeerId}: {Previous} → 0", peerId, previous);
        await PersistPeerAsync(peer, cancellationToken);
        return previous;
    }

    /// <summary>
    /// Evicts non-seed peers that haven't been seen within the given threshold.
    /// Removes from both in-memory dictionary and database.
    /// Returns the list of evicted peer IDs.
    /// </summary>
    public async Task<IReadOnlyList<string>> EvictStalePeersAsync(TimeSpan staleThreshold, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow - staleThreshold;
        var stalePeerIds = _peers.Values
            .Where(p => !p.IsSeedNode && p.LastSeen < cutoff)
            .Select(p => p.PeerId)
            .ToList();

        foreach (var peerId in stalePeerIds)
        {
            _peers.TryRemove(peerId, out _);
            await DeletePeerAsync(peerId, cancellationToken);
        }

        if (stalePeerIds.Count > 0)
        {
            _logger.LogInformation("Evicted {Count} stale peers (threshold: {Threshold})",
                stalePeerIds.Count, staleThreshold);
        }

        return stalePeerIds;
    }

    /// <summary>
    /// Unbans peers whose ban has expired (BanExpiresAt is in the past).
    /// Returns the count of peers unbanned.
    /// </summary>
    public async Task<int> UnbanExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredPeers = _peers.Values
            .Where(p => p.IsBanned && p.BanExpiresAt.HasValue && p.BanExpiresAt.Value < now)
            .ToList();

        foreach (var peer in expiredPeers)
        {
            peer.IsBanned = false;
            peer.BannedAt = null;
            peer.BanReason = null;
            peer.BanExpiresAt = null;
            await PersistPeerAsync(peer, cancellationToken);
            _logger.LogInformation("Auto-unbanned peer {PeerId} (ban expired)", peer.PeerId);
        }

        return expiredPeers.Count;
    }

    /// <summary>
    /// Gets the count of healthy peers
    /// </summary>
    public int GetHealthyPeerCount()
    {
        return GetHealthyPeers().Count;
    }

    /// <summary>
    /// Loads peers from database on startup
    /// </summary>
    public async Task LoadPeersFromDatabaseAsync(CancellationToken cancellationToken = default)
    {
        if (_dbContextFactory == null)
        {
            _logger.LogDebug("No database configured, skipping peer load");
            return;
        }

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entities = await context.Peers.ToListAsync(cancellationToken);
            var loadedCount = 0;

            foreach (var entity in entities)
            {
                var peer = entity.ToDomain();
                _peers.TryAdd(peer.PeerId, peer);
                loadedCount++;
            }

            _logger.LogInformation("Loaded {Count} peers from database", loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading peers from database");
        }
    }

    /// <summary>
    /// Updates the local peer connection status
    /// </summary>
    /// <param name="connectedPeerId">ID of connected peer (null if disconnected)</param>
    /// <param name="status">Current connection status</param>
    public void UpdateLocalPeerStatus(string? connectedPeerId, PeerConnectionStatus status)
    {
        if (_localPeerInfo == null)
        {
            _localPeerInfo = new ActivePeerInfo
            {
                PeerId = _localPeerId,
                ConnectionEstablished = DateTime.UtcNow
            };
        }

        if (connectedPeerId != null)
        {
            if (!_localPeerInfo.ConnectedPeerIds.Contains(connectedPeerId))
            {
                _localPeerInfo.ConnectedPeerIds.Clear();
                _localPeerInfo.ConnectedPeerIds.Add(connectedPeerId);
            }
        }
        else
        {
            _localPeerInfo.ConnectedPeerIds.Clear();
        }

        _localPeerInfo.Status = status;
        _localPeerInfo.LastHeartbeat = DateTime.UtcNow;

        _logger.LogDebug(
            "Updated local peer status: Connected to {Peer}, Status={Status}",
            connectedPeerId ?? "none",
            status);
    }

    /// <summary>
    /// Gets the current local peer connection status
    /// </summary>
    public ActivePeerInfo? GetLocalPeerStatus()
    {
        return _localPeerInfo;
    }

    private async Task PersistPeerAsync(PeerNode peer, CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = PeerNodeEntity.FromDomain(peer);

            var existing = await context.Peers.FindAsync([peer.PeerId], cancellationToken);
            if (existing != null)
            {
                context.Entry(existing).CurrentValues.SetValues(entity);
            }
            else
            {
                context.Peers.Add(entity);
            }

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting peer {PeerId}", peer.PeerId);
        }
    }

    private async Task DeletePeerAsync(string peerId, CancellationToken cancellationToken)
    {
        if (_dbContextFactory == null) return;

        try
        {
            await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var entity = await context.Peers.FindAsync([peerId], cancellationToken);
            if (entity != null)
            {
                context.Peers.Remove(entity);
                await context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting peer {PeerId}", peerId);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
