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

/// <summary>
/// SignalR notification for per-recipient encryption progress.
/// Emitted after each recipient's key wrapping completes or fails.
/// </summary>
public sealed record RecipientEncryptionNotification
{
    /// <summary>Unique operation identifier.</summary>
    public required string OperationId { get; init; }

    /// <summary>Participant display name or truncated wallet address.</summary>
    public required string RecipientName { get; init; }

    /// <summary>1-based index of this recipient in the operation.</summary>
    public required int RecipientIndex { get; init; }

    /// <summary>Total recipients in the operation.</summary>
    public required int TotalRecipients { get; init; }

    /// <summary>JSON Pointer paths disclosed to this recipient.</summary>
    public required string[] DisclosedFieldsSummary { get; init; }

    /// <summary>Processing status: waiting, encrypting, secured, failed.</summary>
    public required string Status { get; init; }

    /// <summary>Pipeline step this event belongs to (2 = encryption).</summary>
    public required int PipelineStep { get; init; }

    /// <summary>Error detail when status is "failed".</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Timestamp of the event.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
