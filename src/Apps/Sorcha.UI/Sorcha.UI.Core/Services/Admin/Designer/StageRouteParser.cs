// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Parses and renders the <c>stage</c> query-string value used by the Feature 142 Designer shell URL,
/// and maps the legacy <c>tab</c> values onto the staged path for back-compatibility.
/// </summary>
public static class StageRouteParser
{
    /// <summary>
    /// Parses a raw <c>stage</c> query-string value into a <see cref="LifecycleStage"/>.
    /// Null, empty, whitespace, or unknown values return <c>null</c> so the caller can apply
    /// its own default. Matching is case-insensitive.
    /// </summary>
    /// <param name="queryValue">The raw <c>stage=</c> value, or <c>null</c>.</param>
    /// <returns>The parsed stage, or <c>null</c> when the value is absent/unknown.</returns>
    public static LifecycleStage? Parse(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return null;
        }

        return queryValue.Trim().ToLowerInvariant() switch
        {
            "describe" => LifecycleStage.Describe,
            "understand" => LifecycleStage.Understand,
            "rehearse" => LifecycleStage.Rehearse,
            "golive" => LifecycleStage.GoLive,
            _ => null,
        };
    }

    /// <summary>
    /// Maps a legacy <c>tab</c> query-string value onto the staged path (back-compat alias):
    /// <c>ai</c>⇒Describe, <c>diagram</c>⇒Understand, <c>preview</c>⇒Understand. Unknown/absent
    /// values return <c>null</c>. Matching is case-insensitive.
    /// </summary>
    /// <param name="tabValue">The raw legacy <c>tab=</c> value, or <c>null</c>.</param>
    /// <returns>The aliased stage, or <c>null</c> when the value is absent/unknown.</returns>
    public static LifecycleStage? FromLegacyTab(string? tabValue)
    {
        if (string.IsNullOrWhiteSpace(tabValue))
        {
            return null;
        }

        return tabValue.Trim().ToLowerInvariant() switch
        {
            "ai" => LifecycleStage.Describe,
            "diagram" => LifecycleStage.Understand,
            "preview" => LifecycleStage.Understand,
            _ => null,
        };
    }

    /// <summary>
    /// Renders a stage back to its <c>stage=</c> query-string form (always lowercase, never null,
    /// so deep links round-trip cleanly).
    /// </summary>
    /// <param name="stage">The lifecycle stage to render.</param>
    /// <returns>The lowercase stage token used in the URL.</returns>
    public static string ToQuery(LifecycleStage stage) => stage switch
    {
        LifecycleStage.Describe => "describe",
        LifecycleStage.Understand => "understand",
        LifecycleStage.Rehearse => "rehearse",
        LifecycleStage.GoLive => "golive",
        _ => "describe",
    };
}
