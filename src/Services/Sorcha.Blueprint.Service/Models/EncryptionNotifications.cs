// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Thin signal notification for encryption operations sent via SignalR.
/// Contains only operation ID, progress percentage, and status — clients pull
/// detailed per-recipient information through authenticated REST endpoints.
/// Replaces EncryptionProgressNotification, EncryptionCompleteNotification,
/// EncryptionFailedNotification, and RecipientEncryptionNotification.
/// </summary>
public sealed record EncryptionSignal
{
    /// <summary>
    /// Unique encryption operation identifier.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public required int PercentComplete { get; init; }

    /// <summary>
    /// Operation status: "encrypting", "complete", "failed".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Timestamp of the signal.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
