// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Configuration options for the <see cref="Services.Implementation.OrphanChunkCleanupService"/>.
/// Bind from <c>"OrphanChunkCleanup"</c> in <c>appsettings.json</c>.
/// </summary>
public class OrphanChunkCleanupOptions
{
    /// <summary>
    /// Configuration section key.
    /// </summary>
    public const string SectionName = "OrphanChunkCleanup";

    /// <summary>
    /// How often the cleanup service runs.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Minimum age a file metadata record must have before it is considered an orphan.
    /// Records newer than this threshold are left in place to allow for in-flight uploads
    /// that have not yet had their parent action transaction confirmed.
    /// Defaults to 30 minutes.
    /// </summary>
    public TimeSpan OrphanAgeThreshold { get; set; } = TimeSpan.FromMinutes(30);
}
