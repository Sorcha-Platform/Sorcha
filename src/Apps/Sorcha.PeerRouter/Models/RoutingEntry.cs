// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Peer.Service.Protos;

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// Represents a peer in the router's in-memory routing table.
/// </summary>
public sealed class RoutingEntry
{
    public required string PeerId { get; init; }
    public string? NodeName { get; set; }
    public required string Address { get; set; }
    public required string IpAddress { get; set; }
    public required int Port { get; set; }
    public PeerCapabilities? Capabilities { get; set; }
    public List<PeerRegisterAdvertisement> AdvertisedRegisters { get; set; } = [];
    public DateTimeOffset FirstSeen { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public bool IsHealthy { get; set; } = true;
    public long HeartbeatCount { get; set; }

    /// <summary>
    /// Per-register version numbers from heartbeats.
    /// Key: register_id, Value: latest version.
    /// </summary>
    public Dictionary<string, long> RegisterVersions { get; set; } = new();
}
