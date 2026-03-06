// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// DTO for EncryptionOperationCompleted events received via EventsHub.
/// </summary>
public record EncryptionOperationCompletedDto
{
    /// <summary>
    /// Unique operation identifier.
    /// </summary>
    public string OperationId { get; init; } = string.Empty;

    /// <summary>
    /// Whether the encryption operation completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Transaction hash if the operation succeeded.
    /// </summary>
    public string? TransactionHash { get; init; }

    /// <summary>
    /// Error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp of the event.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }
}
