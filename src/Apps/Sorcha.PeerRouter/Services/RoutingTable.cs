// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;

namespace Sorcha.PeerRouter.Services;

/// <summary>
/// Thread-safe in-memory routing table of connected peers.
/// </summary>
public sealed class RoutingTable
{
    private readonly ConcurrentDictionary<string, RoutingEntry> _entries = new();
    private readonly EventBuffer _eventBuffer;
    private readonly string _selfPeerId;

    public RoutingTable(EventBuffer eventBuffer, RouterConfiguration config)
    {
        _eventBuffer = eventBuffer;
        _selfPeerId = config.PeerId;
    }

    /// <summary>
    /// Registers or updates a peer in the routing table.
    /// Returns true if the peer was newly added, false if updated.
    /// If a stale (unhealthy) entry exists at the same IP:port, it is replaced.
    /// </summary>
    public bool RegisterPeer(PeerInfo peerInfo, string? sourceAddress = null)
    {
        // Prevent the router from appearing in its own peer table
        if (!string.IsNullOrEmpty(_selfPeerId) &&
            string.Equals(peerInfo.PeerId, _selfPeerId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Address-based dedup: remove stale entries at the same IP:port
        var newIp = !string.IsNullOrEmpty(peerInfo.Address) ? ExtractIp(peerInfo.Address) : ExtractGrpcPeerIp(sourceAddress);
        var newPort = peerInfo.Port;

        foreach (var (existingId, existing) in _entries)
        {
            if (existingId == peerInfo.PeerId)
                continue;

            if (!existing.IsHealthy &&
                existing.Port == newPort &&
                string.Equals(existing.IpAddress, newIp, StringComparison.OrdinalIgnoreCase))
            {
                if (_entries.TryRemove(existingId, out _))
                {
                    _eventBuffer.Add(RouterEvent.Create(
                        RouterEventType.PeerReplaced,
                        existingId,
                        existing.IpAddress,
                        existing.Port,
                        detail: new Dictionary<string, object?>
                        {
                            ["replaced_by"] = peerInfo.PeerId,
                            ["reason"] = "Stale entry at same address replaced by new registration"
                        }));
                }
            }
        }

        var isNew = false;
        _entries.AddOrUpdate(
            peerInfo.PeerId,
            _ =>
            {
                isNew = true;
                var entry = CreateEntry(peerInfo, sourceAddress);
                _eventBuffer.Add(RouterEvent.Create(
                    RouterEventType.PeerConnected,
                    entry.PeerId,
                    entry.IpAddress,
                    entry.Port,
                    entry.NodeName));
                return entry;
            },
            (_, existing) =>
            {
                existing.Address = peerInfo.Address;
                existing.IpAddress = !string.IsNullOrEmpty(peerInfo.Address) ? ExtractIp(peerInfo.Address) : ExtractGrpcPeerIp(sourceAddress);
                existing.Port = peerInfo.Port;
                existing.Capabilities = peerInfo.Capabilities;
                existing.AdvertisedRegisters = [.. peerInfo.AdvertisedRegisters];
                existing.LastSeen = DateTimeOffset.UtcNow;
                existing.IsHealthy = true;
                if (!string.IsNullOrEmpty(peerInfo.NodeName))
                    existing.NodeName = peerInfo.NodeName;
                return existing;
            });

        return isNew;
    }

    /// <summary>
    /// Updates LastSeen for a peer (ping/heartbeat). Returns false if peer not found.
    /// </summary>
    public bool TouchPeer(string peerId)
    {
        if (!_entries.TryGetValue(peerId, out var entry))
            return false;

        entry.LastSeen = DateTimeOffset.UtcNow;
        entry.IsHealthy = true;
        entry.HeartbeatCount++;
        return true;
    }

    /// <summary>
    /// Updates a peer's register versions from a heartbeat.
    /// </summary>
    public bool UpdateRegisterVersions(string peerId, IDictionary<string, long> registerVersions)
    {
        if (!_entries.TryGetValue(peerId, out var entry))
            return false;

        entry.LastSeen = DateTimeOffset.UtcNow;
        entry.IsHealthy = true;
        entry.HeartbeatCount++;

        foreach (var (registerId, version) in registerVersions)
        {
            entry.RegisterVersions[registerId] = version;
        }

        return true;
    }

    /// <summary>
    /// Returns all healthy peers, optionally excluding a specific peer.
    /// </summary>
    public IReadOnlyList<RoutingEntry> GetHealthyPeers(string? excludePeerId = null, int maxPeers = 100)
    {
        return _entries.Values
            .Where(e => e.IsHealthy && e.PeerId != excludePeerId)
            .OrderByDescending(e => e.LastSeen)
            .Take(maxPeers)
            .ToList();
    }

    /// <summary>
    /// Returns all peers (healthy and unhealthy) for debug visibility.
    /// </summary>
    public IReadOnlyList<RoutingEntry> GetAllPeers() => [.. _entries.Values];

    /// <summary>
    /// Returns peers that advertise a specific register.
    /// </summary>
    public IReadOnlyList<RoutingEntry> FindPeersForRegister(
        string registerId,
        bool requireFullReplica = false,
        string? excludePeerId = null,
        int maxPeers = 100)
    {
        return _entries.Values
            .Where(e => e.IsHealthy && e.PeerId != excludePeerId)
            .Where(e => e.AdvertisedRegisters.Any(r =>
                r.RegisterId == registerId &&
                (!requireFullReplica || r.HasFullReplica)))
            .OrderByDescending(e => e.LastSeen)
            .Take(maxPeers)
            .ToList();
    }

    /// <summary>
    /// Marks peers as unhealthy if they haven't been seen within the timeout.
    /// Returns the list of peers that were marked unhealthy.
    /// </summary>
    public IReadOnlyList<RoutingEntry> SweepUnhealthyPeers(TimeSpan timeout)
    {
        var cutoff = DateTimeOffset.UtcNow - timeout;
        var markedUnhealthy = new List<RoutingEntry>();

        foreach (var entry in _entries.Values)
        {
            if (entry.IsHealthy && entry.LastSeen < cutoff)
            {
                entry.IsHealthy = false;
                markedUnhealthy.Add(entry);
                _eventBuffer.Add(RouterEvent.Create(
                    RouterEventType.PeerDisconnected,
                    entry.PeerId,
                    entry.IpAddress,
                    entry.Port,
                    entry.NodeName,
                    new Dictionary<string, object?> { ["reason"] = "timeout" }));
            }
        }

        return markedUnhealthy;
    }

    /// <summary>
    /// Removes peer entries that have been unhealthy for longer than the eviction timeout.
    /// Returns the list of evicted peers.
    /// </summary>
    public IReadOnlyList<RoutingEntry> EvictStalePeers(TimeSpan evictionTimeout)
    {
        var cutoff = DateTimeOffset.UtcNow - evictionTimeout;
        var evicted = new List<RoutingEntry>();

        foreach (var (peerId, entry) in _entries)
        {
            if (!entry.IsHealthy && entry.LastSeen < cutoff)
            {
                if (_entries.TryRemove(peerId, out _))
                {
                    evicted.Add(entry);
                    _eventBuffer.Add(RouterEvent.Create(
                        RouterEventType.PeerEvicted,
                        entry.PeerId,
                        entry.IpAddress,
                        entry.Port,
                        entry.NodeName,
                        new Dictionary<string, object?>
                        {
                            ["reason"] = "eviction_timeout",
                            ["last_seen"] = entry.LastSeen.ToString("O")
                        }));
                }
            }
        }

        return evicted;
    }

    /// <summary>
    /// Updates the advertised registers for a peer from heartbeat data.
    /// Converts heartbeat RegisterAdvertisement to PeerRegisterAdvertisement.
    /// </summary>
    public bool UpdateAdvertisedRegisters(string peerId, IEnumerable<RegisterAdvertisement> advertisements)
    {
        if (!_entries.TryGetValue(peerId, out var entry))
            return false;

        entry.AdvertisedRegisters = advertisements.Select(a => new PeerRegisterAdvertisement
        {
            RegisterId = a.RegisterId,
            HasFullReplica = a.SyncState == SyncStateProto.FullyReplicated,
            LatestVersion = a.LatestVersion,
            IsPublic = a.IsPublic,
            Name = a.Name,
            Description = a.Description
        }).ToList();

        return true;
    }

    /// <summary>
    /// Gets the entry for a specific peer, or null if not found.
    /// </summary>
    public RoutingEntry? GetPeer(string peerId) =>
        _entries.TryGetValue(peerId, out var entry) ? entry : null;

    public int TotalCount => _entries.Count;
    public int HealthyCount => _entries.Values.Count(e => e.IsHealthy);

    private static RoutingEntry CreateEntry(PeerInfo peerInfo, string? sourceAddress = null) => new()
    {
        PeerId = peerInfo.PeerId,
        NodeName = string.IsNullOrEmpty(peerInfo.NodeName) ? null : peerInfo.NodeName,
        Address = peerInfo.Address,
        IpAddress = !string.IsNullOrEmpty(peerInfo.Address) ? ExtractIp(peerInfo.Address) : ExtractGrpcPeerIp(sourceAddress),
        Port = peerInfo.Port,
        Capabilities = peerInfo.Capabilities,
        AdvertisedRegisters = [.. peerInfo.AdvertisedRegisters]
    };

    /// <summary>
    /// Extracts the IP address from a gRPC context.Peer string (e.g. "ipv4:1.2.3.4:5000").
    /// </summary>
    private static string ExtractGrpcPeerIp(string? contextPeer)
    {
        if (string.IsNullOrEmpty(contextPeer))
            return "";

        // gRPC Peer format: "ipv4:1.2.3.4:port" or "ipv6:[::1]:port"
        if (contextPeer.StartsWith("ipv4:", StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = contextPeer[5..];
            var lastColon = afterPrefix.LastIndexOf(':');
            return lastColon > 0 ? afterPrefix[..lastColon] : afterPrefix;
        }

        if (contextPeer.StartsWith("ipv6:", StringComparison.OrdinalIgnoreCase))
        {
            var afterPrefix = contextPeer[5..];
            // IPv6 addresses are in brackets: [::1]:port
            var closeBracket = afterPrefix.IndexOf(']');
            if (closeBracket > 0 && afterPrefix.StartsWith('['))
                return afterPrefix[1..closeBracket];
            return afterPrefix;
        }

        return contextPeer;
    }

    private static string ExtractIp(string address)
    {
        // Handle formats: "host:port", "http://host:port", "https://host:port"
        var uri = address.Contains("://") ? address : $"http://{address}";
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? parsed.Host
            : address;
    }
}
