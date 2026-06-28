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
    /// Severity strings in ascending order, mirroring the server-side <c>InboxSeverity</c> enum.
    /// Positional comparison reproduces the server's <c>severity &gt;= ActionRequired</c> ordinal check.
    /// Unknown strings resolve to -1, which is below threshold — safe default is Informational.
    /// </summary>
    private static readonly string[] SeverityAscending = ["Info", "Warning", "ActionRequired", "Critical"];

    private static readonly int ActionRequiredIndex = Array.IndexOf(SeverityAscending, "ActionRequired");

    /// <summary>
    /// Returns <c>true</c> when the entry is Actionable — that is, when
    /// <paramref name="category"/> is <c>"Action"</c>, or
    /// <paramref name="severity"/> ranks at <c>ActionRequired</c> or above in the
    /// server's <c>InboxSeverity</c> ordinal order.
    /// Unknown or unrecognised strings default to <c>Informational</c>
    /// (returns <c>false</c>) as the safe default.
    /// </summary>
    /// <param name="category">The inbox entry category string (e.g. "Action", "Workflow", "Security").</param>
    /// <param name="severity">The inbox entry severity string (e.g. "ActionRequired", "Critical", "Info").</param>
    public static bool IsActionable(string category, string severity) =>
        category == "Action" ||
        Array.IndexOf(SeverityAscending, severity) >= ActionRequiredIndex;
}
