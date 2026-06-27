// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Shared;

/// <summary>
/// Client-side static helper that mirrors the server-side
/// <c>InboxClassification.IsActionable(InboxCategory, InboxSeverity)</c> logic.
/// Used by activity-timeline components to decide whether an inbox entry
/// requires immediate user attention (FR-011).
/// </summary>
public static class ActivityClassification
{
    /// <summary>
    /// Returns <c>true</c> when the entry is Actionable — that is, when
    /// <paramref name="category"/> is <c>"Action"</c>, or
    /// <paramref name="severity"/> is <c>"ActionRequired"</c> or <c>"Critical"</c>.
    /// Unknown or unrecognised strings default to <c>Informational</c>
    /// (returns <c>false</c>) as the safe default.
    /// </summary>
    /// <param name="category">The inbox entry category string (e.g. "Action", "Workflow", "Security").</param>
    /// <param name="severity">The inbox entry severity string (e.g. "ActionRequired", "Critical", "Info").</param>
    public static bool IsActionable(string category, string severity) =>
        category == "Action" ||
        severity == "ActionRequired" ||
        severity == "Critical";
}
