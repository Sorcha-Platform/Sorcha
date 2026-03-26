// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Represents a peer node in the network
/// </summary>
public class PeerNode : IEquatable<PeerNode>
{
    /// <summary>
    /// Unique identifier for the peer
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string PeerId { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name for the peer node
    /// </summary>
    public string? NodeName { get; set; }

    /// <summary>
    /// Network address of the peer (IP or hostname)
    /// </summary>
    [Required]
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Port number for peer communication
    /// </summary>
    [Required]
    [Range(1, 65535)]
    public int Port { get; set; }

    /// <summary>
    /// Supported communication protocols
    /// </summary>
    public List<string> SupportedProtocols { get; set; } = new();

    /// <summary>
    /// When this peer was first discovered
    /// </summary>
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this peer was last seen online
    /// </summary>
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of consecutive failed connection attempts
    /// </summary>
    public int FailureCount { get; set; } = 0;

    /// <summary>
    /// Whether this is a seed node (well-known bootstrap peer for initial discovery)
    /// </summary>
    public bool IsSeedNode { get; set; } = false;

    /// <summary>
    /// Registers this peer advertises (register-aware peering)
    /// </summary>
    public List<PeerRegisterInfo> AdvertisedRegisters { get; set; } = new();

    /// <summary>
    /// Peer capabilities
    /// </summary>
    public PeerCapabilities Capabilities { get; set; } = new();

    /// <summary>
    /// Average latency to this peer in milliseconds
    /// </summary>
    public int AverageLatencyMs { get; set; } = 0;

    /// <summary>
    /// Whether this peer presented a valid JWT during its last gRPC connection (FR-015).
    /// Authenticated peers receive a higher base reputation score.
    /// </summary>
    public bool IsAuthenticated { get; set; } = false;

    /// <summary>
    /// Whether this peer is banned from communication
    /// </summary>
    public bool IsBanned { get; set; } = false;

    /// <summary>
    /// When the ban was applied (null if not banned)
    /// </summary>
    public DateTimeOffset? BannedAt { get; set; }

    /// <summary>
    /// Operator-provided reason for the ban
    /// </summary>
    public string? BanReason { get; set; }

    /// <summary>
    /// When the ban expires (null = permanent ban)
    /// </summary>
    public DateTimeOffset? BanExpiresAt { get; set; }

    public bool Equals(PeerNode? other)
    {
        return other != null && PeerId == other.PeerId;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as PeerNode);
    }

    public override int GetHashCode()
    {
        return PeerId.GetHashCode();
    }

    public override string ToString()
    {
        return $"Peer {PeerId} at {Address}:{Port}";
    }
}

/// <summary>
/// Capabilities of a peer node
/// </summary>
public class PeerCapabilities
{
    /// <summary>
    /// Supports gRPC streaming connections
    /// </summary>
    public bool SupportsStreaming { get; set; } = true;

    /// <summary>
    /// Supports transaction distribution
    /// </summary>
    public bool SupportsTransactionDistribution { get; set; } = true;

    /// <summary>
    /// Maximum transaction size in bytes
    /// </summary>
    public int MaxTransactionSize { get; set; } = 10 * 1024 * 1024; // 10 MB
}
