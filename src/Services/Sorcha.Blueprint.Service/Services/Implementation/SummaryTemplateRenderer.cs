// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Renders summary templates by replacing {{payload.field}} tokens with values from a JSON payload.
/// </summary>
public static partial class SummaryTemplateRenderer
{
    [GeneratedRegex(@"\{\{payload\.([a-zA-Z0-9_.]+)\}\}")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Renders a summary template by replacing {{payload.field}} tokens with values from the payload.
    /// </summary>
    /// <param name="template">Template string with {{payload.field}} tokens.</param>
    /// <param name="payload">JSON payload to resolve token values from.</param>
    /// <param name="fallback">Fallback value returned when template is null or all tokens are unresolved.</param>
    /// <returns>Rendered summary string.</returns>
    public static string Render(string? template, JsonElement? payload, string fallback)
    {
        if (string.IsNullOrWhiteSpace(template))
            return fallback;

        if (payload is null || payload.Value.ValueKind == JsonValueKind.Undefined)
            return fallback;

        var result = TokenPattern().Replace(template, match =>
        {
            var fieldPath = match.Groups[1].Value;
            var resolved = ResolveFieldPath(payload.Value, fieldPath);
            return resolved ?? match.Value;
        });

        // If no tokens were resolved (result unchanged) and template had tokens, use fallback
        if (result == template && TokenPattern().IsMatch(template))
            return fallback;

        return result;
    }

    private static string? ResolveFieldPath(JsonElement element, string path)
    {
        var segments = path.Split('.');
        var current = element;

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return null;

            if (!current.TryGetProperty(segment, out var next))
                return null;

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => current.GetRawText()
        };
    }
}
