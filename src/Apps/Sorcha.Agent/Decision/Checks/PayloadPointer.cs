// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Text.Json.Nodes;

namespace Sorcha.Agent.Decision.Checks;

/// <summary>
/// Resolves a JSON-Pointer (RFC 6901, e.g. <c>/portrait/tokenImageBase64</c>) against the
/// top-level payload dictionary the checks receive. The first segment indexes the dictionary;
/// remaining segments navigate into the resolved <see cref="JsonNode"/>.
/// </summary>
public static class PayloadPointer
{
    /// <summary>
    /// Resolves <paramref name="pointer"/> against <paramref name="payload"/>, returning the
    /// referenced node or <c>null</c> when any segment is missing.
    /// </summary>
    public static JsonNode? Resolve(IReadOnlyDictionary<string, object?> payload, string pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
            return null;

        var segments = pointer.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        if (!payload.TryGetValue(Unescape(segments[0]), out var first) || first is not JsonNode node)
            return null;

        for (var i = 1; i < segments.Length; i++)
        {
            var key = Unescape(segments[i]);
            switch (node)
            {
                case JsonObject obj when obj.TryGetPropertyValue(key, out var child) && child is not null:
                    node = child;
                    break;
                case JsonArray arr when int.TryParse(key, out var idx) && idx >= 0 && idx < arr.Count && arr[idx] is not null:
                    node = arr[idx]!;
                    break;
                default:
                    return null;
            }
        }

        return node;
    }

    /// <summary>
    /// Resolves <paramref name="pointer"/> and returns its string value, or <c>null</c> when the
    /// node is absent or not a JSON string/scalar.
    /// </summary>
    public static string? ResolveString(IReadOnlyDictionary<string, object?> payload, string pointer)
    {
        var node = Resolve(payload, pointer);
        return node is JsonValue value ? value.ToString() : null;
    }

    /// <summary>
    /// Returns true when <paramref name="pointer"/> resolves to a present, non-empty value
    /// (non-empty string, or any object/array/scalar).
    /// </summary>
    public static bool IsPresentAndNonEmpty(IReadOnlyDictionary<string, object?> payload, string pointer)
    {
        var node = Resolve(payload, pointer);
        return node switch
        {
            null => false,
            JsonValue value when value.TryGetValue(out string? s) => !string.IsNullOrWhiteSpace(s),
            _ => true
        };
    }

    /// <summary>
    /// Flattens every string scalar reachable from <paramref name="node"/> into a single
    /// space-joined string (used by free-text scans such as profanity over a nested address).
    /// </summary>
    public static string FlattenText(JsonNode? node)
    {
        if (node is null)
            return string.Empty;

        var parts = new List<string>();
        Collect(node, parts);
        return string.Join(' ', parts);
    }

    private static void Collect(JsonNode? node, List<string> parts)
    {
        switch (node)
        {
            case JsonValue value when value.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s):
                parts.Add(s);
                break;
            case JsonObject obj:
                foreach (var kv in obj)
                    Collect(kv.Value, parts);
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    Collect(item, parts);
                break;
        }
    }

    private static string Unescape(string segment) =>
        segment.Replace("~1", "/").Replace("~0", "~");
}
