// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Core.Models.Designer;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for managing offline sync queue and automatic synchronization.
/// </summary>
public interface IOfflineSyncService
{
    /// <summary>
    /// Gets the current number of pending items in the sync queue.
    /// </summary>
    int PendingCount { get; }

    /// <summary>
    /// Gets whether there are any pending sync operations.
    /// </summary>
    bool HasPendingChanges { get; }

    /// <summary>
    /// Queues a blueprint operation for later sync.
    /// </summary>
    Task QueueOperationAsync(SyncOperation operation, string blueprintId, string? blueprintJson = null);

    /// <summary>
    /// Attempts to sync all pending operations.
    /// </summary>
    /// <returns>Results of each sync attempt.</returns>
    Task<IReadOnlyList<SyncResult>> SyncNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all items currently in the sync queue.
    /// </summary>
    Task<IReadOnlyList<SyncQueueItem>> GetQueuedItemsAsync();

    /// <summary>
    /// Removes a specific item from the sync queue.
    /// </summary>
    Task RemoveFromQueueAsync(string itemId);

    /// <summary>
    /// Clears all items from the sync queue.
    /// </summary>
    Task ClearQueueAsync();

    /// <summary>
    /// Starts automatic background sync when connectivity changes.
    /// </summary>
    Task StartAutoSyncAsync();

    /// <summary>
    /// Stops automatic background sync.
    /// </summary>
    Task StopAutoSyncAsync();

    /// <summary>
    /// Event raised when the sync queue changes.
    /// </summary>
    event Action<int>? OnQueueChanged;

    /// <summary>
    /// Event raised when a sync operation completes.
    /// </summary>
    event EventHandler<SyncCompletedEventArgs>? OnSyncCompleted;

    /// <summary>
    /// Event raised when a sync conflict is detected.
    /// </summary>
    event EventHandler<ConflictDetectedEventArgs>? OnConflictDetected;
}

/// <summary>
/// Result of a sync operation attempt.
/// </summary>
public class SyncResult
{
    /// <summary>The queue item that was processed.</summary>
    public required SyncQueueItem Item { get; init; }

    /// <summary>Whether the sync succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the sync failed.</summary>
    public string? Error { get; init; }

    /// <summary>The title of the blueprint for display purposes.</summary>
    public string? BlueprintTitle { get; init; }

    /// <summary>Whether a conflict was detected.</summary>
    public bool HasConflict { get; init; }

    /// <summary>The conflict details if one was detected.</summary>
    public BlueprintConflict? Conflict { get; init; }
}

/// <summary>
/// Event args for when sync operations complete.
/// </summary>
public class SyncCompletedEventArgs : EventArgs
{
    /// <summary>Number of successfully synced items.</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of failed items.</summary>
    public int FailedCount { get; init; }

    /// <summary>Number of conflicts detected.</summary>
    public int ConflictCount { get; init; }

    /// <summary>Individual results for each sync attempt.</summary>
    public IReadOnlyList<SyncResult> Results { get; init; } = [];
}

/// <summary>
/// Event args for when a sync conflict is detected.
/// </summary>
public class ConflictDetectedEventArgs : EventArgs
{
    /// <summary>The detected conflict.</summary>
    public required BlueprintConflict Conflict { get; init; }

    /// <summary>The queue item that caused the conflict.</summary>
    public required SyncQueueItem QueueItem { get; init; }
}
