// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Represents a peer node in the network.
/// </summary>
public class PeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public List<string> SupportedProtocols { get; set; } = new();
    public DateTimeOffset FirstSeen { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int FailureCount { get; set; }
    public bool IsBootstrapNode { get; set; }
    public int AverageLatencyMs { get; set; }
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
    public double QualityScore { get; set; }
    public string QualityRating { get; set; } = string.Empty;
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
/// Peer network statistics response.
/// </summary>
public class PeerServiceStatistics
{
    public DateTimeOffset Timestamp { get; set; }
    public PeerStatistics PeerStats { get; set; } = new();
    public QualityStatistics QualityStats { get; set; } = new();
    public QueueStatistics QueueStats { get; set; } = new();
    public CircuitBreakerSummary CircuitBreakerStats { get; set; } = new();
}

public class PeerStatistics
{
    public int TotalPeers { get; set; }
    public int HealthyPeers { get; set; }
    public int UnhealthyPeers { get; set; }
    public int SeedNodes { get; set; }
    public double AverageLatencyMs { get; set; }
    public int TotalFailures { get; set; }
}

public class QualityStatistics
{
    public int TotalTrackedPeers { get; set; }
    public int ExcellentPeers { get; set; }
    public int GoodPeers { get; set; }
    public int FairPeers { get; set; }
    public int PoorPeers { get; set; }
    public double AverageQualityScore { get; set; }
}

public class QueueStatistics
{
    public int QueueSize { get; set; }
    public bool IsEmpty { get; set; }
}

public class CircuitBreakerSummary
{
    public int TotalCircuitBreakers { get; set; }
    public int OpenCircuits { get; set; }
    public int HalfOpenCircuits { get; set; }
    public int ClosedCircuits { get; set; }
}

/// <summary>
/// Peer health response.
/// </summary>
public class PeerHealthResponse
{
    public int TotalPeers { get; set; }
    public int HealthyPeers { get; set; }
    public int UnhealthyPeers { get; set; }
    public double HealthPercentage { get; set; }
    public List<HealthyPeerInfo> Peers { get; set; } = new();
}

public class HealthyPeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
    public DateTimeOffset LastSeen { get; set; }
    public int AverageLatencyMs { get; set; }
}

/// <summary>
/// Connection quality information for a peer.
/// </summary>
public class ConnectionQualityInfo
{
    public string PeerId { get; set; } = string.Empty;
    public double AverageLatencyMs { get; set; }
    public double MinLatencyMs { get; set; }
    public double MaxLatencyMs { get; set; }
    public double SuccessRate { get; set; }
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    public double QualityScore { get; set; }
    public string QualityRating { get; set; } = string.Empty;
    public DateTimeOffset LastUpdated { get; set; }
}

/// <summary>
/// Register subscription information.
/// </summary>
public class SubscriptionInfo
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
/// Available register discovered via peer advertisements.
/// </summary>
public class AvailableRegisterInfo
{
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The register's name, when advertised. Present on the wire; previously dropped.</summary>
    public string? Name { get; set; }

    /// <summary>The register's description, when advertised. Present on the wire; previously dropped.</summary>
    public string? Description { get; set; }

    public int PeerCount { get; set; }
    public long LatestVersion { get; set; }
    public long LatestDocketVersion { get; set; }
    public bool IsPublic { get; set; }
    public int FullReplicaPeerCount { get; set; }
}

/// <summary>
/// Request to subscribe to a register.
/// </summary>
public class CliSubscribeRequest
{
    public string Mode { get; set; } = string.Empty;
}

/// <summary>
/// Response after subscribing to a register.
/// </summary>
public class CliSubscribeResponse
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
public class CliUnsubscribeResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public bool Unsubscribed { get; set; }
    public bool CacheRetained { get; set; }
}

/// <summary>
/// Request to ban a peer.
/// </summary>
public class CliBanRequest
{
    public string? Reason { get; set; }
}

/// <summary>
/// Response after banning a peer.
/// </summary>
public class CliBanResponse
{
    public string PeerId { get; set; } = string.Empty;
    public bool IsBanned { get; set; }
    public DateTimeOffset? BannedAt { get; set; }
    public string? BanReason { get; set; }
}

/// <summary>
/// Response after resetting a peer's failure count.
/// </summary>
public class CliResetResponse
{
    public string PeerId { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public int PreviousFailureCount { get; set; }
}

/// <summary>
/// Response after purging a register's cache.
/// </summary>
public class CliPurgeResponse
{
    public string RegisterId { get; set; } = string.Empty;
    public bool Purged { get; set; }
    public int TransactionsRemoved { get; set; }
    public int DocketsRemoved { get; set; }
}
