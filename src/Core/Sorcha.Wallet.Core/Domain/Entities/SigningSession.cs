// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// A multi-round signing ceremony for a threshold key group. Tracks the
/// collection of partial signatures until the threshold is met or the session expires.
/// Schema only — threshold signing is a future capability.
/// </summary>
public class SigningSession
{
    /// <summary>
    /// Unique identifier for this signing session.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the threshold key group performing the signing.
    /// </summary>
    public required Guid ThresholdKeyGroupId { get; set; }

    /// <summary>
    /// Transaction being signed. Null during pre-commitment rounds.
    /// </summary>
    public string? TransactionId { get; set; }

    /// <summary>
    /// Current state of the signing ceremony.
    /// </summary>
    public SigningSessionState State { get; set; } = SigningSessionState.Initializing;

    /// <summary>
    /// Number of partial signatures required to complete the session.
    /// </summary>
    public required int RequiredSigners { get; set; }

    /// <summary>
    /// Number of valid partial signatures collected so far.
    /// </summary>
    public int CollectedPartials { get; set; }

    /// <summary>
    /// Deadline after which this session is considered expired.
    /// </summary>
    public required DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Timestamp when this signing session was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the session completed (succeeded or failed). Null if still in progress.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Navigation to the parent threshold key group.
    /// </summary>
    public ThresholdKeyGroup ThresholdKeyGroup { get; set; } = null!;
}
