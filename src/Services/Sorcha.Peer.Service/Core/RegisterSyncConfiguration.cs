// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Configuration for register synchronization behaviour
/// </summary>
public class RegisterSyncConfiguration
{
    /// <summary>
    /// Periodic sync interval in seconds (default: 90s). Drives the RegisterSyncBackgroundService
    /// re-pull loop — the upper bound on how long a fully-replicated subscriber lags the owner
    /// before its incremental re-pull catches a freshly-sealed docket (e.g. a cross-node credential
    /// delivery). Tightened from the former 5-minute cadence so replication-back is timely.
    /// </summary>
    [Range(20, 3600)]
    public int PeriodicSyncIntervalSeconds { get; set; } = 90;

    /// <summary>
    /// Heartbeat interval in seconds (default: 30 seconds)
    /// </summary>
    [Range(10, 120)]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Heartbeat timeout in seconds (default: 30 seconds)
    /// </summary>
    [Range(10, 120)]
    public int HeartbeatTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts before trying a different source peer
    /// </summary>
    [Range(1, 20)]
    public int MaxRetryAttempts { get; set; } = 10;

    /// <summary>
    /// Maximum missed heartbeats before marking peer as timed out (default: 2 = 60 seconds total)
    /// </summary>
    [Range(1, 10)]
    public int MaxMissedHeartbeats { get; set; } = 2;

    /// <summary>
    /// Connection timeout per attempt in seconds
    /// </summary>
    [Range(10, 120)]
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent docket chain pulls during full replica sync
    /// </summary>
    [Range(1, 10)]
    public int MaxConcurrentDocketPulls { get; set; } = 3;

    /// <summary>
    /// Batch size for transaction retrieval during docket chain pull
    /// </summary>
    [Range(10, 1000)]
    public int DocketPullBatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of cached transactions per register before eviction
    /// </summary>
    [Range(100, 1_000_000)]
    public int MaxCachedTransactionsPerRegister { get; set; } = 100_000;

    /// <summary>
    /// Maximum number of cached dockets per register before eviction
    /// </summary>
    [Range(100, 100_000)]
    public int MaxCachedDocketsPerRegister { get; set; } = 10_000;

    /// <summary>
    /// Overall replication timeout in minutes for full replica sync
    /// </summary>
    [Range(5, 120)]
    public int ReplicationTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Interval in seconds for periodic relay sync polling of NAT'd peers (default: 20 seconds).
    /// Tightened from 60s so a NAT'd subscriber (e.g. the local replica behind Caddy) pulls
    /// freshly-sealed dockets from the seed quickly rather than lagging up to a minute.
    /// </summary>
    [Range(10, 300)]
    public int RelayPollIntervalSeconds { get; set; } = 20;
}
