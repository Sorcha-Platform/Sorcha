// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for encryption progress display.
/// </summary>
public record EncryptionOperationViewModel
{
    /// <summary>
    /// Operation identifier.
    /// </summary>
    public string OperationId { get; init; } = string.Empty;

    /// <summary>
    /// Current stage (e.g., queued, encrypting-per-recipient, complete, failed).
    /// </summary>
    public string Stage { get; init; } = string.Empty;

    /// <summary>
    /// Progress percentage from 0 to 100.
    /// </summary>
    public int PercentComplete { get; init; }

    /// <summary>
    /// Total number of recipients.
    /// </summary>
    public int RecipientCount { get; init; }

    /// <summary>
    /// Number of recipients processed so far.
    /// </summary>
    public int ProcessedRecipients { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Whether the operation has completed (successfully or with failure).
    /// </summary>
    public bool IsComplete => Stage is "complete" or "failed";

    /// <summary>
    /// Whether the operation completed successfully.
    /// </summary>
    public bool IsSuccess => Stage == "complete";
}
