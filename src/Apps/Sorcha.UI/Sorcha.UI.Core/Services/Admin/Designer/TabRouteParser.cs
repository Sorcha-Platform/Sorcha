// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Parses and renders the <c>tab</c> query-string value used by the Designer shell URL.
/// </summary>
public static class TabRouteParser
{
    /// <summary>
    /// Parses a raw query-string value into a <see cref="DesignerTab"/>.
    /// Null, empty, whitespace, or unknown values fall back to <see cref="DesignerTab.Ai"/>.
    /// Matching is case-insensitive.
    /// </summary>
    public static DesignerTab Parse(string? queryValue)
    {
        if (string.IsNullOrWhiteSpace(queryValue))
        {
            return DesignerTab.Ai;
        }

        return queryValue.Trim().ToLowerInvariant() switch
        {
            "ai" => DesignerTab.Ai,
            "diagram" => DesignerTab.Diagram,
            "preview" => DesignerTab.Preview,
            _ => DesignerTab.Ai
        };
    }

    /// <summary>
    /// Renders a tab back to its query-string form.
    /// Returns <c>null</c> when the tab is the default (<see cref="DesignerTab.Ai"/>)
    /// so the URL stays clean; otherwise returns the lowercase tab name.
    /// </summary>
    public static string? ToQuery(DesignerTab tab) => tab switch
    {
        DesignerTab.Ai => null,
        DesignerTab.Diagram => "diagram",
        DesignerTab.Preview => "preview",
        _ => null
    };
}
