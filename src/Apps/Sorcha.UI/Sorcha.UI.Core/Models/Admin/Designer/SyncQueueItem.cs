// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Designer;

/// <summary>
/// Represents an item in the offline sync queue for blueprint operations.
/// </summary>
public class SyncQueueItem
{
    /// <summary>Unique identifier for this queue item.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The type of operation to perform.</summary>
    public SyncOperation Operation { get; set; }

    /// <summary>The ID of the blueprint being operated on.</summary>
    public string BlueprintId { get; set; } = string.Empty;

    /// <summary>The serialized blueprint JSON for create/update operations.</summary>
    public string? BlueprintJson { get; set; }

    /// <summary>When this queue item was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this queue item was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Number of times this operation has been retried.</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>The last error message if the operation failed.</summary>
    public string? LastError { get; set; }

    /// <summary>Maximum number of retry attempts before abandoning.</summary>
    public const int MaxRetries = 3;

    /// <summary>Whether this item can be retried.</summary>
    public bool CanRetry => RetryCount < MaxRetries;

    /// <summary>When the last attempt was made.</summary>
    public DateTimeOffset? LastAttemptAt { get; set; }
}

/// <summary>
/// Types of sync operations for offline queue.
/// </summary>
public enum SyncOperation
{
    /// <summary>Create a new blueprint on the server.</summary>
    Create,

    /// <summary>Update an existing blueprint on the server.</summary>
    Update,

    /// <summary>Delete a blueprint from the server.</summary>
    Delete
}
