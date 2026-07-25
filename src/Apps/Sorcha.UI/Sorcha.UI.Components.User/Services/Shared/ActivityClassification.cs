// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Components.User.Services.Shared;

/// <summary>
/// Client-side static helper that mirrors the server-side
/// <c>InboxClassification.IsActionable(InboxCategory, InboxSeverity)</c> logic.
/// Used by activity-timeline components to decide whether an inbox entry
/// requires immediate user attention (FR-011).
/// </summary>
/// <remarks>
/// <para>
/// <b>Wire values are kebab/lower-case, not PascalCase.</b> The server sends these as enums, and
/// <c>SorchaJson</c> registers a kebab-case <c>JsonStringEnumConverter</c> in <c>Converters</c> —
/// which System.Text.Json ranks ABOVE a type-level <c>[JsonConverter]</c> attribute. So
/// <c>InboxSeverity.ActionRequired</c> arrives as <c>"action-required"</c> and
/// <c>InboxCategory.Workflow</c> as <c>"workflow"</c>.
/// </para>
/// <para>
/// Issue #1273: every comparison here and in <c>ActivityFeed</c> was case-sensitive PascalCase, so
/// none of them ever matched a real payload. The visible cost was that an AIAS rejection
/// (<c>severity: warning</c>, <c>iconKey: workflow.rejected</c>) read with the same weight as
/// "Profile updated", every row drew the generic bell glyph because the category never matched, and
/// <see cref="IsActionable"/> returned <see langword="false"/> for <i>every</i> severity — making the
/// Actionable chip effectively unreachable. Comparisons are now normalised, so PascalCase,
/// kebab-case and lower-case all resolve.
/// </para>
/// </remarks>
public static class ActivityClassification
{
    /// <summary>
    /// Severity ranks in ascending order, mirroring the server-side <c>InboxSeverity</c> enum.
    /// Positional comparison reproduces the server's <c>severity &gt;= ActionRequired</c> ordinal check.
    /// Keys are normalised, so any wire casing resolves. Unknown values rank below threshold — the
    /// safe default is Informational.
    /// </summary>
    private static readonly string[] SeverityAscending = ["info", "warning", "actionrequired", "critical"];

    private static readonly int ActionRequiredIndex = Array.IndexOf(SeverityAscending, "actionrequired");
    private static readonly int WarningIndex = Array.IndexOf(SeverityAscending, "warning");

    /// <summary>
    /// Strips casing and separators so a value matches whatever spelling the wire uses
    /// (<c>ActionRequired</c>, <c>action-required</c>, <c>action_required</c>).
    /// </summary>
    /// <remarks>
    /// The dot is stripped too, because the same normalisation serves dotted <c>iconKey</c> values
    /// like <c>workflow.rejected</c>. Leaving it out is what made the icon map miss on the very key
    /// from the bug report.
    /// </remarks>
    public static string Normalise(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace("-", "").Replace("_", "").Replace(".", "").Replace(" ", "").ToLowerInvariant();

    /// <summary>The severity's ordinal rank, or -1 when unrecognised.</summary>
    public static int SeverityRank(string? severity) => Array.IndexOf(SeverityAscending, Normalise(severity));

    /// <summary>
    /// True when the severity is Warning or above — i.e. the entry must be visually distinguishable
    /// from routine information. A rejected identity application is the motivating case.
    /// </summary>
    public static bool IsElevated(string? severity) => SeverityRank(severity) >= WarningIndex;

    /// <summary>
    /// Returns <c>true</c> when the entry is Actionable — that is, when
    /// <paramref name="category"/> is <c>Action</c>, or <paramref name="severity"/> ranks at
    /// <c>ActionRequired</c> or above in the server's <c>InboxSeverity</c> ordinal order.
    /// Unknown or unrecognised strings default to Informational (returns <c>false</c>).
    /// </summary>
    /// <param name="category">The inbox entry category (e.g. "Action", "workflow", "security").</param>
    /// <param name="severity">The inbox entry severity (e.g. "action-required", "critical", "info").</param>
    public static bool IsActionable(string category, string severity) =>
        Normalise(category) == "action" ||
        SeverityRank(severity) >= ActionRequiredIndex;
}
