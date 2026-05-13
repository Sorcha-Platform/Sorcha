// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.UI.Core.Models.Configuration;

/// <summary>
/// User interface configuration settings
/// </summary>
public sealed record UiConfiguration
{
    /// <summary>
    /// Active profile name
    /// </summary>
    public string ActiveProfileName { get; init; } = "Development";

    /// <summary>
    /// UI theme (Light, Dark, Auto)
    /// </summary>
    public string Theme { get; init; } = "Auto";

    /// <summary>
    /// UI density (Comfortable, Compact, Dense)
    /// </summary>
    public string Density { get; init; } = "Comfortable";

    /// <summary>
    /// Enable animations
    /// </summary>
    public bool EnableAnimations { get; init; } = true;

    /// <summary>
    /// Show debug information
    /// </summary>
    public bool ShowDebugInfo { get; init; } = false;

    /// <summary>
    /// Auto-refresh interval in seconds (0 = disabled)
    /// </summary>
    public int AutoRefreshIntervalSeconds { get; init; } = 0;

    /// <summary>
    /// Default page size for lists
    /// </summary>
    public int DefaultPageSize { get; init; } = 20;

    /// <summary>
    /// Last modified timestamp
    /// </summary>
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Creates default UI configuration
    /// </summary>
    public static UiConfiguration Default()
    {
        return new UiConfiguration();
    }
}
