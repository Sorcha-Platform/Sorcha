// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// SignalR notification for encryption progress updates.
/// </summary>
public sealed record EncryptionProgressNotification
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Current step number (1-based).
    /// </summary>
    public required int Step { get; init; }

    /// <summary>
    /// Human-readable step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Total number of steps.
    /// </summary>
    public required int TotalSteps { get; init; }

    /// <summary>
    /// Percentage complete (0-100).
    /// </summary>
    public required int PercentComplete { get; init; }

    /// <summary>
    /// Timestamp of the progress update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// SignalR notification for encryption completion.
/// </summary>
public sealed record EncryptionCompleteNotification
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Transaction hash of the submitted transaction.
    /// </summary>
    public required string TransactionHash { get; init; }

    /// <summary>
    /// Timestamp of completion.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// SignalR notification for encryption failure.
/// </summary>
public sealed record EncryptionFailedNotification
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>
    public required string OperationId { get; init; }

    /// <summary>
    /// Error message describing the failure.
    /// </summary>
    public required string Error { get; init; }

    /// <summary>
    /// Wallet address of the recipient that caused the failure (if applicable).
    /// </summary>
    public string? FailedRecipient { get; init; }

    /// <summary>
    /// Step number at which the failure occurred.
    /// </summary>
    public int? Step { get; init; }

    /// <summary>
    /// Timestamp of the failure.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
