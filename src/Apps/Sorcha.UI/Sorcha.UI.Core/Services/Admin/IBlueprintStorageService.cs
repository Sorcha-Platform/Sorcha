// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Service for blueprint storage operations with server and offline support.
/// </summary>
public interface IBlueprintStorageService
{
    /// <summary>
    /// Gets all blueprints for the current user.
    /// Prefers server data, falls back to local cache.
    /// </summary>
    Task<IReadOnlyList<Blueprint.Models.Blueprint>> GetBlueprintsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific blueprint by ID.
    /// </summary>
    Task<Blueprint.Models.Blueprint?> GetBlueprintAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a blueprint to the server or local storage.
    /// </summary>
    /// <returns>Result indicating where the blueprint was saved.</returns>
    Task<BlueprintSaveResult> SaveBlueprintAsync(Blueprint.Models.Blueprint blueprint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blueprint.
    /// </summary>
    Task<bool> DeleteBlueprintAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the server is currently reachable.
    /// </summary>
    Task<bool> IsServerAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Migrates blueprints from LocalStorage to the server.
    /// </summary>
    Task<MigrationResult> MigrateLocalBlueprintsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the local blueprint cache.
    /// </summary>
    Task ClearLocalCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when a blueprint is saved.
    /// </summary>
    event EventHandler<BlueprintSavedEventArgs>? BlueprintSaved;
}

/// <summary>
/// Result of a blueprint save operation.
/// </summary>
public class BlueprintSaveResult
{
    /// <summary>Whether the save operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Whether the blueprint was saved to the server.</summary>
    public bool SavedToServer { get; init; }

    /// <summary>Whether the save was queued for later sync.</summary>
    public bool QueuedForSync { get; init; }

    /// <summary>Error message if the save failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The ID of the saved blueprint.</summary>
    public string? BlueprintId { get; init; }

    public static BlueprintSaveResult ServerSuccess(string blueprintId) => new()
    {
        Success = true,
        SavedToServer = true,
        BlueprintId = blueprintId
    };

    public static BlueprintSaveResult Queued(string blueprintId) => new()
    {
        Success = true,
        SavedToServer = false,
        QueuedForSync = true,
        BlueprintId = blueprintId
    };

    public static BlueprintSaveResult Failure(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

/// <summary>
/// Result of migrating local blueprints to the server.
/// </summary>
public class MigrationResult
{
    /// <summary>Number of blueprints successfully migrated.</summary>
    public int MigratedCount { get; set; }

    /// <summary>Number of blueprints that failed to migrate.</summary>
    public int FailedCount { get; set; }

    /// <summary>IDs of blueprints that failed to migrate.</summary>
    public List<string> FailedIds { get; } = [];
}

/// <summary>
/// Event args for when a blueprint is saved.
/// </summary>
public class BlueprintSavedEventArgs : EventArgs
{
    public required string BlueprintId { get; init; }
    public required bool SavedToServer { get; init; }
}

/// <summary>
/// Represents a conflict between local and server versions of a blueprint.
/// </summary>
public class BlueprintConflict
{
    /// <summary>The local version of the blueprint.</summary>
    public required Blueprint.Models.Blueprint LocalVersion { get; init; }

    /// <summary>The server version of the blueprint.</summary>
    public required Blueprint.Models.Blueprint ServerVersion { get; init; }

    /// <summary>Gets the blueprint ID.</summary>
    public string BlueprintId => LocalVersion.Id;

    /// <summary>Gets the blueprint title.</summary>
    public string Title => LocalVersion.Title;
}

/// <summary>
/// Resolution strategy for a blueprint conflict.
/// </summary>
public enum ConflictResolution
{
    /// <summary>Keep the local version and overwrite server.</summary>
    KeepLocal,

    /// <summary>Keep the server version and discard local changes.</summary>
    KeepServer,

    /// <summary>Merge changes (create a new version with combined data).</summary>
    Merge,

    /// <summary>Create a copy of the local version with a new ID.</summary>
    KeepBoth
}
