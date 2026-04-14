// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Converts the flat JSON-Pointer-keyed dictionary produced by
/// <see cref="Sorcha.UI.Core.Models.Forms.FormContext"/> into a nested
/// <see cref="Dictionary{TKey, TValue}"/> tree suitable for submission
/// as an <c>ActionExecuteRequest.PayloadData</c>.
/// <para>
/// The form renderer stores every control's value keyed by its JSON Pointer
/// scope (e.g. <c>"/name/givenName"</c>, <c>"/address/line1"</c>). Prior to
/// Feature 103 wave 6 (<c>SchemaRefResolver</c>), action schemas were flat
/// and the submission path could get away with stripping the leading slash
/// and sending the dictionary as-is. Once core identity primitives
/// (<c>PersonName</c>, <c>DateOfBirth</c>, <c>EmailAddress</c>,
/// <c>PostalAddress</c>) started being flattened into action schemas as
/// nested objects, that shortcut started producing payloads that failed
/// validator schema validation with <c>VAL_SCHEMA_004</c>:
/// <c>Required properties ["name","dob","email","address"] are not present</c>.
/// </para>
/// <para>
/// This helper assembles a nested tree by walking each pointer and creating
/// intermediate <see cref="Dictionary{TKey, TValue}"/> nodes as needed. The
/// final dictionary serialises to JSON with the correct nesting:
/// </para>
/// <code>
///   flat:   { "/name/givenName": "Alice", "/name/familyName": "Smith",
///             "/dob/dateOfBirth": "1990-01-01" }
///   nested: { "name": { "givenName": "Alice", "familyName": "Smith" },
///             "dob":  { "dateOfBirth": "1990-01-01" } }
/// </code>
/// <para>
/// Arrays (pointers with numeric segments such as <c>/emails/0/value</c>)
/// are not currently supported — they would produce a dictionary keyed by
/// the string "0" rather than a real JSON array. None of the in-flight
/// v2 blueprints use array-valued primitives, but this is a known limitation
/// if future primitives (e.g. <c>EmailAddressList/v1</c>) are adopted.
/// </para>
/// </summary>
public static class FormPayloadBuilder
{
    /// <summary>
    /// Builds a nested dictionary from a flat JSON-Pointer-keyed dictionary.
    /// Leading slashes on keys are tolerated. Empty keys and <c>"/"</c>
    /// are ignored. Null values are coerced to <see cref="string.Empty"/>
    /// to mirror the pre-fix submission shape and satisfy the non-nullable
    /// value type on <c>ActionExecuteRequest.PayloadData</c>. Values at
    /// conflicting paths (e.g. a scalar at <c>/name</c> plus a nested value
    /// at <c>/name/givenName</c>) are resolved by the nested value winning
    /// — the later write replaces the earlier one.
    /// </summary>
    public static Dictionary<string, object> BuildNested(
        IReadOnlyDictionary<string, object?> flatPointerKeyed)
    {
        ArgumentNullException.ThrowIfNull(flatPointerKeyed);

        var root = new Dictionary<string, object>();
        foreach (var kvp in flatPointerKeyed)
        {
            SetPointer(root, kvp.Key, kvp.Value ?? string.Empty);
        }
        return root;
    }

    private static void SetPointer(Dictionary<string, object> root, string pointer, object value)
    {
        if (string.IsNullOrEmpty(pointer)) return;

        var trimmed = pointer.TrimStart('/');
        if (string.IsNullOrEmpty(trimmed)) return;

        var segments = trimmed.Split('/');

        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            var key = UnescapePointerSegment(segments[i]);
            if (current.TryGetValue(key, out var existing) && existing is Dictionary<string, object> existingDict)
            {
                current = existingDict;
            }
            else
            {
                var next = new Dictionary<string, object>();
                current[key] = next;
                current = next;
            }
        }

        var leafKey = UnescapePointerSegment(segments[^1]);
        current[leafKey] = value;
    }

    /// <summary>
    /// Decodes the RFC 6901 pointer segment escapes (<c>~1</c> → <c>/</c>,
    /// <c>~0</c> → <c>~</c>). Order matters — <c>~0</c> is decoded after
    /// <c>~1</c> so a literal <c>~1</c> in the original segment survives.
    /// </summary>
    private static string UnescapePointerSegment(string segment)
    {
        return segment.Replace("~1", "/").Replace("~0", "~");
    }
}
