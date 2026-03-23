// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Models;

/// <summary>
/// Aggregated register info from peer advertisements across the network.
/// </summary>
public class AvailableRegisterInfo
{
    public string RegisterId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int PeerCount { get; set; }
    public long LatestVersion { get; set; }
    public long LatestDocketVersion { get; set; }
    public bool IsPublic { get; set; }
    public int FullReplicaPeerCount { get; set; }
}

/// <summary>
/// Response after banning a peer.
/// </summary>
public class BanResponse
{
    public string PeerId { get; set; } = string.Empty;
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
}

/// <summary>
/// Request body for banning a peer.
/// </summary>
public class BanRequest
{
    public string? Reason { get; set; }
}

/// <summary>
/// Response after resetting a peer's failure count.
/// </summary>
public class ResetResponse
{
    public string PeerId { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int PreviousFailureCount { get; set; }
}

/// <summary>
/// Request body for advertising or removing advertisement of a register.
/// </summary>
public class AdvertiseRegisterRequest
{
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
    public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Response after subscribing to a register.
/// </summary>
public class SubscribeResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string SyncState { get; set; } = string.Empty;
    public long LastSyncedDocketVersion { get; set; }
    public long LastSyncedTransactionVersion { get; set; }
    public double SyncProgressPercent { get; set; }
}

/// <summary>
/// Response after unsubscribing from a register.
/// </summary>
public class UnsubscribeResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public bool Unsubscribed { get; set; }
    public bool CacheRetained { get; set; }
}

/// <summary>
/// Response after purging cached data for a register.
/// </summary>
public class PurgeResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public bool Purged { get; set; }
    public int TransactionsRemoved { get; set; }
    public int DocketsRemoved { get; set; }
}
