// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Minimal RFC 6901 JSON Pointer resolver/setter for use by the routing engine
/// when evaluating <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/> entries.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>Root pointer ("") — whole document.</item>
///   <item>Nested object paths — "/foo/bar".</item>
///   <item>Array indices — "/items/0" reads position 0.</item>
///   <item>RFC 6901 escaping — "~0" → "~", "~1" → "/".</item>
/// </list>
/// Does NOT support RFC 6902 patch semantics, "-" array append, or URI fragment form.
/// Introduced in Feature 104 wave 14a.
/// </remarks>
internal static class JsonPointerHelper
{
    /// <summary>
    /// Attempts to resolve the given JSON Pointer against the source document.
    /// Returns true if the path was fully traversed; false if any segment was
    /// missing. Absent paths are expected to be common and produce a silent skip
    /// in the caller rather than an error.
    /// </summary>
    public static bool TryResolve(JsonNode? source, string pointer, out JsonNode? value)
    {
        value = null;
        if (source == null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(pointer))
        {
            value = source;
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        var segments = pointer[1..].Split('/');
        JsonNode? current = source;
        foreach (var rawSegment in segments)
        {
            if (current == null)
            {
                return false;
            }

            var segment = Unescape(rawSegment);
            switch (current)
            {
                case JsonObject obj:
                    if (!obj.TryGetPropertyValue(segment, out current))
                    {
                        return false;
                    }
                    break;
                case JsonArray arr:
                    if (!int.TryParse(segment, out var idx) || idx < 0 || idx >= arr.Count)
                    {
                        return false;
                    }
                    current = arr[idx];
                    break;
                default:
                    return false;
            }
        }

        value = current;
        return true;
    }

    /// <summary>
    /// Attempts to set the given JSON Pointer on the target object, creating
    /// intermediate object nodes as needed. Array writes and root replacement are
    /// not supported in v1. Returns true on success, false on invalid pointer.
    /// </summary>
    /// <remarks>
    /// The source value is deep-cloned before being written so that the caller
    /// can mutate the source document without affecting previously-set targets.
    /// </remarks>
    public static bool TrySet(JsonObject target, string pointer, JsonNode? value)
    {
        if (string.IsNullOrEmpty(pointer) || pointer[0] != '/')
        {
            return false;
        }

        var segments = pointer[1..].Split('/');
        if (segments.Length == 0)
        {
            return false;
        }

        JsonObject current = target;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = Unescape(segments[i]);
            if (current.TryGetPropertyValue(segment, out var existing) && existing is JsonObject existingObj)
            {
                current = existingObj;
                continue;
            }

            var created = new JsonObject();
            current[segment] = created;
            current = created;
        }

        var leafKey = Unescape(segments[^1]);

        // Deep clone via round-trip to insulate the target from mutations on the source.
        JsonNode? cloned = value is null ? null : JsonNode.Parse(value.ToJsonString());
        current[leafKey] = cloned;
        return true;
    }

    /// <summary>
    /// Validates that a string is a syntactically well-formed JSON Pointer
    /// (RFC 6901 simple form). Empty string is valid (root).
    /// </summary>
    public static bool IsValidPointer(string? pointer)
    {
        if (pointer is null)
        {
            return false;
        }
        if (pointer.Length == 0)
        {
            return true;
        }
        if (pointer[0] != '/')
        {
            return false;
        }
        // Any other character is allowed; escape sequences are lenient.
        return true;
    }

    private static string Unescape(string segment)
    {
        if (!segment.Contains('~'))
        {
            return segment;
        }
        // ~1 → / MUST be replaced before ~0 → ~
        return segment.Replace("~1", "/").Replace("~0", "~");
    }
}
