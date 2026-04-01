// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cli.Models;

/// <summary>
/// Result of a bulk operation, tracking success/failure counts and individual errors.
/// </summary>
public class BulkOperationResult
{
    /// <summary>
    /// Total number of items processed.
    /// </summary>
    public int TotalItems { get; set; }

    /// <summary>
    /// Number of items that succeeded.
    /// </summary>
    public int Succeeded { get; set; }

    /// <summary>
    /// Number of items that failed.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Details of individual item failures.
    /// </summary>
    public List<BulkItemError> Errors { get; set; } = [];

    /// <summary>
    /// Total duration of the bulk operation.
    /// </summary>
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Details of a single item failure within a bulk operation.
/// </summary>
public class BulkItemError
{
    /// <summary>
    /// Zero-based index of the failed item in the batch.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Identifier of the failed item (e.g., wallet name or address).
    /// </summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// Error message describing the failure.
    /// </summary>
    public string Error { get; set; } = string.Empty;
}
