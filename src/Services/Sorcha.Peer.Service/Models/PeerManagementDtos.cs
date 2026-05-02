// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Models;

/// <summary>
/// Aggregated register info from peer advertisements across the network.
/// </summary>
public class AvailableRegisterInfo
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Human-readable name.</summary>
    public string? Name { get; set; }
    /// <summary>Free-text description of the resource.</summary>
    public string? Description { get; set; }
    /// <summary>Numeric value for peer count.</summary>
    public int PeerCount { get; set; }
    /// <summary>Numeric value for latest version.</summary>
    public long LatestVersion { get; set; }
    /// <summary>Numeric value for latest docket version.</summary>
    public long LatestDocketVersion { get; set; }
    /// <summary>Indicates whether public.</summary>
    public bool IsPublic { get; set; }
    /// <summary>Numeric value for full replica peer count.</summary>
    public int FullReplicaPeerCount { get; set; }
}

/// <summary>
/// Response after banning a peer.
/// </summary>
public class BanResponse
{
    /// <summary>Identifier of the peer.</summary>
    public string PeerId { get; set; } = string.Empty;
    /// <summary>Indicates whether banned.</summary>
    public bool IsBanned { get; set; }
    /// <summary>Timestamp at which banned occurred (UTC).</summary>
    public DateTimeOffset? BannedAt { get; set; }
    /// <summary>The ban reason.</summary>
    public string? BanReason { get; set; }
    /// <summary>Timestamp at which ban expires occurred (UTC).</summary>
    public DateTimeOffset? BanExpiresAt { get; set; }
}

/// <summary>
/// Request body for banning a peer.
/// </summary>
public class BanRequest
{
    /// <summary>Reason explaining the current state or outcome.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Optional ban duration in minutes. If null or omitted, the ban is permanent.
    /// </summary>
    public int? DurationMinutes { get; set; }
}

/// <summary>
/// Response after resetting a peer's failure count.
/// </summary>
public class ResetResponse
{
    /// <summary>Identifier of the peer.</summary>
    public string PeerId { get; set; } = string.Empty;
    /// <summary>Numeric value for failure count.</summary>
    public int FailureCount { get; set; }
    /// <summary>Numeric value for previous failure count.</summary>
    public int PreviousFailureCount { get; set; }
}

/// <summary>
/// Request body for advertising or removing advertisement of a register.
/// </summary>
public class AdvertiseRegisterRequest
{
    /// <summary>Indicates whether public.</summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// Human-readable register name to include in advertisements.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Register description to include in advertisements.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Request body for subscribing to a register.
/// </summary>
public class SubscribeRequest
{
    /// <summary>The mode.</summary>
    public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Response after subscribing to a register.
/// </summary>
public class SubscribeResponse
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>The mode.</summary>
    public string Mode { get; set; } = string.Empty;
    /// <summary>The sync state.</summary>
    public string SyncState { get; set; } = string.Empty;
    /// <summary>Numeric value for last synced docket version.</summary>
    public long LastSyncedDocketVersion { get; set; }
    /// <summary>Numeric value for last synced transaction version.</summary>
    public long LastSyncedTransactionVersion { get; set; }
    /// <summary>Numeric value for sync progress percent.</summary>
    public double SyncProgressPercent { get; set; }
}

/// <summary>
/// Response after unsubscribing from a register.
/// </summary>
public class UnsubscribeResponse
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Flag indicating unsubscribed.</summary>
    public bool Unsubscribed { get; set; }
    /// <summary>Flag indicating cache retained.</summary>
    public bool CacheRetained { get; set; }
}

/// <summary>
/// Response after purging cached data for a register.
/// </summary>
public class PurgeResponse
{
    /// <summary>Identifier of the register.</summary>
    public string RegisterId { get; set; } = string.Empty;
    /// <summary>Flag indicating purged.</summary>
    public bool Purged { get; set; }
    /// <summary>Numeric value for transactions removed.</summary>
    public int TransactionsRemoved { get; set; }
    /// <summary>Numeric value for dockets removed.</summary>
    public int DocketsRemoved { get; set; }
}
