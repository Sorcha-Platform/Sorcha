// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Models;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Replication;

/// <summary>
/// Manages register advertisements — tracking which registers this node holds,
/// building advertisement messages for peers, and processing incoming advertisements.
/// </summary>
public class RegisterAdvertisementService
{
    private readonly ILogger<RegisterAdvertisementService> _logger;
    private readonly PeerListManager _peerListManager;
    private readonly IRedisAdvertisementStore? _store;
    private readonly ConcurrentDictionary<string, LocalRegisterAdvertisement> _localAdvertisements = new();

    public RegisterAdvertisementService(
        ILogger<RegisterAdvertisementService> logger,
        PeerListManager peerListManager,
        IRedisAdvertisementStore? store = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _store = store;
    }

    /// <summary>
    /// Loads previously persisted advertisements from Redis on startup.
    /// Populates the in-memory cache so the service is immediately ready.
    /// </summary>
    public async Task LoadFromRedisAsync(CancellationToken cancellationToken = default)
    {
        if (_store == null) return;

        try
        {
            // Load local advertisements
            var localAds = await _store.GetAllLocalAsync(cancellationToken);
            foreach (var ad in localAds)
            {
                _localAdvertisements[ad.RegisterId] = ad;
            }

            // Load remote advertisements and update peer state
            var remoteAds = await _store.GetAllRemoteAsync(cancellationToken);
            foreach (var (peerId, ads) in remoteAds)
            {
                var peer = _peerListManager.GetPeer(peerId);
                if (peer != null)
                {
                    peer.AdvertisedRegisters = ads;
                    await _peerListManager.AddOrUpdatePeerAsync(peer);
                }
            }

            _logger.LogInformation(
                "Loaded {LocalCount} local and {RemotePeerCount} remote peer advertisement sets from Redis",
                localAds.Count, remoteAds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load advertisements from Redis on startup — starting with empty state");
        }
    }

    /// <summary>
    /// Advertises a register that this node holds or subscribes to.
    /// </summary>
    public void AdvertiseRegister(
        string registerId,
        RegisterSyncState syncState,
        long latestVersion = 0,
        long latestDocketVersion = 0,
        bool isPublic = false,
        string? name = null,
        string? description = null)
    {
        // Idempotency check: skip if unchanged (FR-009)
        if (_localAdvertisements.TryGetValue(registerId, out var existing) &&
            existing.SyncState == syncState &&
            existing.LatestVersion == latestVersion &&
            existing.LatestDocketVersion == latestDocketVersion &&
            existing.IsPublic == isPublic &&
            existing.Name == name &&
            existing.Description == description)
        {
            _logger.LogDebug(
                "Skipping unchanged advertisement for register {RegisterId}",
                registerId);
            return;
        }

        var ad = new LocalRegisterAdvertisement
        {
            RegisterId = registerId,
            Name = name,
            Description = description,
            SyncState = syncState,
            LatestVersion = latestVersion,
            LatestDocketVersion = latestDocketVersion,
            IsPublic = isPublic,
            LastUpdated = DateTimeOffset.UtcNow
        };

        _localAdvertisements[registerId] = ad;

        // Write-through to Redis (fire-and-forget, FR-010 safe)
        if (_store != null)
            FireAndForgetRedis(_store.SetLocalAsync(ad), "SetLocalAsync");

        _logger.LogDebug(
            "Advertising register {RegisterId} with state {SyncState}, version {Version}",
            registerId, syncState, latestVersion);
    }

    /// <summary>
    /// Updates the version information for an already-advertised register.
    /// Uses compare-and-swap to avoid mutating a shared object in-place.
    /// </summary>
    public void UpdateRegisterVersion(
        string registerId,
        long latestVersion,
        long latestDocketVersion)
    {
        // CAS loop: create a new object rather than mutating the existing one
        while (_localAdvertisements.TryGetValue(registerId, out var existing))
        {
            var updated = new LocalRegisterAdvertisement
            {
                RegisterId = existing.RegisterId,
                Name = existing.Name,
                Description = existing.Description,
                SyncState = existing.SyncState,
                LatestVersion = latestVersion,
                LatestDocketVersion = latestDocketVersion,
                IsPublic = existing.IsPublic,
                LastUpdated = DateTimeOffset.UtcNow
            };

            if (_localAdvertisements.TryUpdate(registerId, updated, existing))
            {
                // Write-through to Redis (fire-and-forget, FR-010 safe)
                if (_store != null)
                    FireAndForgetRedis(_store.SetLocalAsync(updated), "SetLocalAsync");
                return;
            }
            // TryUpdate failed — another thread changed the value; retry
        }
    }

    /// <summary>
    /// Removes an advertisement for a register (e.g., when unsubscribing).
    /// </summary>
    public void RemoveAdvertisement(string registerId)
    {
        if (_localAdvertisements.TryRemove(registerId, out _))
        {
            // Write-through to Redis (fire-and-forget, FR-010 safe)
            if (_store != null)
                FireAndForgetRedis(_store.RemoveLocalAsync(registerId), "RemoveLocalAsync");

            _logger.LogDebug("Removed advertisement for register {RegisterId}", registerId);
        }
    }

    /// <summary>
    /// Gets all local register advertisements to include in heartbeats/exchanges.
    /// </summary>
    public IReadOnlyCollection<LocalRegisterAdvertisement> GetLocalAdvertisements()
    {
        return _localAdvertisements.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets only public register advertisements (for sharing with unknown peers).
    /// </summary>
    public IReadOnlyCollection<LocalRegisterAdvertisement> GetPublicAdvertisements()
    {
        return _localAdvertisements.Values
            .Where(a => a.IsPublic)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets a specific local advertisement.
    /// </summary>
    public LocalRegisterAdvertisement? GetAdvertisement(string registerId)
    {
        _localAdvertisements.TryGetValue(registerId, out var ad);
        return ad;
    }

    /// <summary>
    /// Processes incoming register advertisements from a remote peer.
    /// Updates the peer's advertised registers in PeerListManager.
    /// </summary>
    public async Task ProcessRemoteAdvertisementsAsync(
        string sourcePeerId,
        IEnumerable<PeerRegisterInfo> remoteAdvertisements,
        CancellationToken cancellationToken = default)
    {
        var peer = _peerListManager.GetPeer(sourcePeerId);
        if (peer == null)
        {
            _logger.LogDebug(
                "Received advertisements from unknown peer {PeerId}, ignoring",
                sourcePeerId);
            return;
        }

        // Update the peer's advertised registers
        var adList = remoteAdvertisements.ToList();
        peer.AdvertisedRegisters = adList;
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        // Write-through to Redis (fire-and-forget, FR-010 safe)
        if (_store != null)
        {
            foreach (var ad in adList)
            {
                FireAndForgetRedis(_store.SetRemoteAsync(sourcePeerId, ad), "SetRemoteAsync");
            }
        }

        _logger.LogDebug(
            "Updated {Count} register advertisements from peer {PeerId}",
            adList.Count, sourcePeerId);
    }

    /// <summary>
    /// Builds PeerRegisterInfo list from local advertisements for sharing with peers.
    /// </summary>
    public List<PeerRegisterInfo> BuildPeerRegisterInfoList()
    {
        return _localAdvertisements.Values
            .Select(a => new PeerRegisterInfo
            {
                RegisterId = a.RegisterId,
                Name = a.Name,
                Description = a.Description,
                SyncState = a.SyncState,
                LatestVersion = a.LatestVersion,
                IsPublic = a.IsPublic
            })
            .ToList();
    }

    /// <summary>
    /// Aggregates registers advertised across all known peers and local advertisements (public only).
    /// Returns one entry per register with peer count, max versions, and full replica count.
    /// Only includes registers from healthy peers (recently seen, low failure count, not banned).
    /// </summary>
    public IReadOnlyCollection<AvailableRegisterInfo> GetNetworkAdvertisedRegisters()
    {
        var healthyPeers = _peerListManager.GetHealthyPeers();
        var registerMap = new Dictionary<string, AvailableRegisterInfo>();

        // Include local public advertisements first
        foreach (var localAd in _localAdvertisements.Values)
        {
            if (!localAd.IsPublic) continue;

            registerMap[localAd.RegisterId] = new AvailableRegisterInfo
            {
                RegisterId = localAd.RegisterId,
                Name = localAd.Name,
                Description = localAd.Description,
                IsPublic = true,
                LatestVersion = localAd.LatestVersion,
                LatestDocketVersion = localAd.LatestDocketVersion,
                PeerCount = 0, // Local node doesn't count as a "peer"
                FullReplicaPeerCount = 0
            };
        }

        // Aggregate remote peer advertisements (healthy peers only)
        foreach (var peer in healthyPeers)
        {
            foreach (var reg in peer.AdvertisedRegisters)
            {
                if (!reg.IsPublic) continue;

                if (!registerMap.TryGetValue(reg.RegisterId, out var info))
                {
                    info = new AvailableRegisterInfo
                    {
                        RegisterId = reg.RegisterId,
                        Name = reg.Name,
                        Description = reg.Description,
                        IsPublic = true
                    };
                    registerMap[reg.RegisterId] = info;
                }

                info.PeerCount++;
                if (reg.LatestVersion > info.LatestVersion)
                    info.LatestVersion = reg.LatestVersion;
                if (reg.CanServeFullReplica)
                    info.FullReplicaPeerCount++;
                // Use name/description from whichever peer provides it first
                if (string.IsNullOrEmpty(info.Name) && !string.IsNullOrEmpty(reg.Name))
                    info.Name = reg.Name;
                if (string.IsNullOrEmpty(info.Description) && !string.IsNullOrEmpty(reg.Description))
                    info.Description = reg.Description;
            }
        }

        // Update docket versions from local advertisements
        foreach (var (registerId, info) in registerMap)
        {
            var localAd = GetAdvertisement(registerId);
            if (localAd != null && localAd.LatestDocketVersion > info.LatestDocketVersion)
                info.LatestDocketVersion = localAd.LatestDocketVersion;
        }

        return registerMap.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Gets the peer IDs that advertise a specific register (from remote advertisements).
    /// Used by RegisterReplicationService to find relay-capable source peers.
    /// </summary>
    public IReadOnlyCollection<string> GetPeersAdvertisingRegister(string registerId)
    {
        return _peerListManager.GetAllPeers()
            .Where(p => !p.IsBanned && p.AdvertisedRegisters.Any(r =>
                string.Equals(r.RegisterId, registerId, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.PeerId)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Detects registers where the remote peer has a newer version than us.
    /// Returns register IDs that may need syncing.
    /// </summary>
    public IReadOnlyCollection<string> DetectVersionLag(
        IDictionary<string, long> remoteVersions)
    {
        var lagging = new List<string>();

        foreach (var (registerId, remoteVersion) in remoteVersions)
        {
            if (_localAdvertisements.TryGetValue(registerId, out var local))
            {
                if (remoteVersion > local.LatestVersion)
                {
                    lagging.Add(registerId);
                }
            }
        }

        return lagging.AsReadOnly();
    }

    /// <summary>
    /// Safely observes a fire-and-forget Redis task, logging failures without
    /// propagating exceptions to the caller (FR-010).
    /// </summary>
    private void FireAndForgetRedis(Task task, string operationName)
    {
        task.ContinueWith(
            t => _logger.LogWarning(t.Exception?.InnerException ?? t.Exception,
                "Fire-and-forget Redis {Operation} failed", operationName),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}

/// <summary>
/// Local register advertisement state tracked by this node.
/// </summary>
public class LocalRegisterAdvertisement
{
    public required string RegisterId { get; init; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public RegisterSyncState SyncState { get; set; }
    public long LatestVersion { get; set; }
    public long LatestDocketVersion { get; set; }
    public bool IsPublic { get; set; }
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
