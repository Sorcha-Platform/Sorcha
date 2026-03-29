// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Models;

/// <summary>
/// Tracks the finalization status of a replicated docket being persisted to the Register Service.
/// </summary>
public class DocketFinalizationRecord
{
    /// <summary>
    /// Register ID this docket belongs to.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Sequential docket number within the register.
    /// </summary>
    public required long DocketNumber { get; init; }

    /// <summary>
    /// Hash of the docket content.
    /// </summary>
    public required string DocketHash { get; init; }

    /// <summary>
    /// Current finalization status.
    /// </summary>
    public FinalizationStatus Status { get; set; } = FinalizationStatus.Pending;

    /// <summary>
    /// When the finalization was first attempted.
    /// </summary>
    public DateTimeOffset AttemptedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the docket was successfully finalized (null if not yet finalized).
    /// </summary>
    public DateTimeOffset? FinalizedAt { get; set; }

    /// <summary>
    /// Error message if finalization was rejected or failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Status of a docket finalization attempt.
/// </summary>
public enum FinalizationStatus
{
    /// <summary>
    /// Docket is queued for finalization.
    /// </summary>
    Pending,

    /// <summary>
    /// Docket was successfully written to the Register Service.
    /// </summary>
    Finalized,

    /// <summary>
    /// Docket was rejected due to validation failure (hash mismatch, chain break, etc.).
    /// </summary>
    Rejected
}
