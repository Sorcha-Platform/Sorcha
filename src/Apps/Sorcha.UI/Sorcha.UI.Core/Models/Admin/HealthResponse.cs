// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// Health check response from Blueprint API
/// </summary>
public class BlueprintHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;
    public BlueprintMetricsData? Metrics { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Metrics data from Blueprint API
/// </summary>
public class BlueprintMetricsData
{
    public int TotalBlueprints { get; set; }
    public int PublishedVersions { get; set; }
}

/// <summary>
/// Health check response from Peer Service
/// </summary>
public class PeerHealthResponse
{
    public string Status { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Uptime { get; set; } = string.Empty;
    public PeerMetricsData? Metrics { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Metrics data from Peer Service
/// </summary>
public class PeerMetricsData
{
    public int ActivePeers { get; set; }
    public long TotalTransactions { get; set; }
    public double ThroughputPerSecond { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
}

/// <summary>
/// Peer information from Peer Service
/// </summary>
public class PeerNodeInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long RegisteredAt { get; set; }
}

/// <summary>
/// Enhanced peer info from GET /api/peers (includes ban status, quality, and registers).
/// </summary>
public class EnhancedPeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int FailureCount { get; set; }
    public bool IsSeedNode { get; set; }
    public double AverageLatencyMs { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
    public double QualityScore { get; set; }
    public string QualityRating { get; set; } = "Unknown";
    public int AdvertisedRegisterCount { get; set; }
    public List<PeerRegisterAdvertisement> AdvertisedRegisters { get; set; } = new();
}

/// <summary>
/// Register advertisement from a peer.
/// </summary>
public class PeerRegisterAdvertisement
{
    public string RegisterId { get; set; } = string.Empty;
    public string SyncState { get; set; } = string.Empty;
    public long LatestVersion { get; set; }
    public bool IsPublic { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Connection quality info from GET /api/peers/quality.
/// </summary>
public class PeerQualityInfo
{
    public string PeerId { get; set; } = string.Empty;
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double SuccessRate { get; set; }
    public int TotalRequests { get; set; }
    public int SuccessfulRequests { get; set; }
    public int FailedRequests { get; set; }
    public double QualityScore { get; set; }
    public string QualityRating { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Register subscription info from GET /api/registers/subscriptions.
/// </summary>
public class RegisterSubscriptionInfo
{
    public string RegisterId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string SyncState { get; set; } = string.Empty;
    public long LastSyncedDocketVersion { get; set; }
    public long LastSyncedTransactionVersion { get; set; }
    public long TotalDocketsInChain { get; set; }
    public double SyncProgressPercent { get; set; }
    public bool CanParticipateInValidation { get; set; }
    public bool IsReceiving { get; set; }
    public DateTimeOffset? LastSyncAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Available register info from GET /api/registers/available.
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
/// Response from POST /api/registers/{registerId}/subscribe.
/// </summary>
public class SubscribeResponseInfo
{
    public string RegisterId { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string SyncState { get; set; } = string.Empty;
}

/// <summary>
/// Response from POST /api/peers/{peerId}/ban and DELETE /api/peers/{peerId}/ban.
/// </summary>
public class BanResponseInfo
{
    public string PeerId { get; set; } = string.Empty;
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
}

/// <summary>
/// Response from POST /api/peers/{peerId}/reset.
/// </summary>
public class ResetResponseInfo
{
    public string PeerId { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int PreviousFailureCount { get; set; }
}
