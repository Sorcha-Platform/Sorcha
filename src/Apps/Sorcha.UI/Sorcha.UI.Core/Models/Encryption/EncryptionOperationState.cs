// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Encryption;

/// <summary>
/// Client-side model tracking an active encryption operation for the popover.
/// </summary>
public sealed class EncryptionOperationState
{
    /// <summary>Operation tracking ID.</summary>
    public required string OperationId { get; init; }

    /// <summary>Overall operation status.</summary>
    public OperationDisplayStatus Status { get; set; } = OperationDisplayStatus.Pending;

    /// <summary>Progress percentage (0-100).</summary>
    public int PercentComplete { get; set; }

    /// <summary>Per-recipient state for UI rendering.</summary>
    public List<RecipientDisplayState> Recipients { get; set; } = [];

    /// <summary>Transaction hash on successful completion.</summary>
    public string? TransactionHash { get; set; }

    /// <summary>Error message on failure.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Name of the recipient that caused failure.</summary>
    public string? FailedRecipient { get; set; }

    /// <summary>Current visual state of the popover for this operation.</summary>
    public PopoverState PanelState { get; set; } = PopoverState.Expanded;

    /// <summary>When the operation started.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the operation has finished (success or failure).</summary>
    public bool IsComplete => Status is OperationDisplayStatus.Complete or OperationDisplayStatus.Failed;

    /// <summary>Whether the operation completed successfully.</summary>
    public bool IsSuccess => Status == OperationDisplayStatus.Complete;
}

/// <summary>
/// Per-recipient state for rendering in the popover.
/// </summary>
public sealed class RecipientDisplayState
{
    /// <summary>Display name or truncated wallet address.</summary>
    public required string Name { get; init; }

    /// <summary>Human-readable fields summary (e.g., "all fields" or "decision, site details").</summary>
    public required string FieldsSummary { get; init; }

    /// <summary>Processing status.</summary>
    public string Status { get; set; } = "waiting";
}

/// <summary>
/// Overall display status for an encryption operation.
/// </summary>
public enum OperationDisplayStatus
{
    /// <summary>Queued, not yet started.</summary>
    Pending,

    /// <summary>Actively processing.</summary>
    InProgress,

    /// <summary>Successfully completed.</summary>
    Complete,

    /// <summary>Failed with error.</summary>
    Failed
}
