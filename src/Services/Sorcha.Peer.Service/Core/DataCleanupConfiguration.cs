// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Configuration for periodic cleanup of stale peer data in PostgreSQL.
/// </summary>
public class DataCleanupConfiguration
{
    /// <summary>
    /// How often to run the primary cleanup sweep (stale peers, expired bans, queued transactions)
    /// </summary>
    [Range(1, 60)]
    public int CleanupIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Non-seed peers not seen within this threshold are evicted
    /// </summary>
    [Range(5, 1440)]
    public int StalePeerMinutes { get; set; } = 30;

    /// <summary>
    /// Completed/failed queued transactions older than this are purged
    /// </summary>
    [Range(10, 1440)]
    public int CompletedTransactionRetentionMinutes { get; set; } = 60;

    /// <summary>
    /// How often to run the checkpoint cleanup sweep (orphaned sync checkpoints)
    /// </summary>
    [Range(5, 120)]
    public int CheckpointCleanupIntervalMinutes { get; set; } = 15;
}
