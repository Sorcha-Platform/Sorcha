// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Cryptography.SdJwt;

/// <summary>
/// Translates JSON Pointer paths (e.g., "/address/locality") into nested
/// _sd digest arrays and reconstructs disclosed subsets from presentations.
/// Supports both nested object fields and array element disclosure.
/// </summary>
public static class NestedDisclosure
{
    /// <summary>
    /// Determines whether the given disclosable entries contain any JSON Pointer paths
    /// (entries starting with "/").
    /// </summary>
    public static bool HasNestedPaths(IEnumerable<string> disclosableEntries)
    {
        return disclosableEntries.Any(e => e.StartsWith('/'));
    }

    /// <summary>
    /// Processes claims and disclosable paths to produce a payload with nested _sd arrays
    /// and a list of disclosure strings. Supports both top-level name-keyed and JSON Pointer
    /// path-keyed disclosables.
    /// </summary>
    /// <param name="claims">All credential claims.</param>
    /// <param name="disclosablePaths">Mix of top-level names and JSON Pointer paths.</param>
    /// <returns>A tuple of (processedPayloadClaims, disclosures, sdDigests).</returns>
    public static (Dictionary<string, object> PayloadClaims, List<string> Disclosures, List<string> TopLevelSdDigests)
        Translate(Dictionary<string, object> claims, IEnumerable<string> disclosablePaths)
    {
        var pathSet = disclosablePaths.ToList();
        var topLevelNames = pathSet.Where(p => !p.StartsWith('/')).ToHashSet();
        var nestedPaths = pathSet.Where(p => p.StartsWith('/')).ToList();

        var disclosures = new List<string>();
        var topLevelSdDigests = new List<string>();

        // Deep-clone claims to avoid mutating the input
        var processedClaims = DeepClone(claims);

        // Handle top-level name-keyed disclosables (existing behaviour)
        foreach (var name in topLevelNames)
        {
            if (!processedClaims.TryGetValue(name, out var value))
                continue;

            var disclosure = CreateDisclosure(name, value);
            disclosures.Add(disclosure);
            topLevelSdDigests.Add(ComputeDigest(disclosure));
            processedClaims.Remove(name);
        }

        // Handle nested JSON Pointer paths
        // Group by parent object to batch nested _sd arrays
        var pathsByParent = new Dictionary<string, List<(string segment, string fullPath)>>();
        foreach (var path in nestedPaths)
        {
            var segments = ParsePointer(path);
            if (segments.Length == 0)
                continue;

            if (segments.Length == 1)
            {
                // Single-segment pointer like "/name" — treat as top-level
                var name = segments[0];
                if (!processedClaims.TryGetValue(name, out var value))
                    continue;

                var disclosure = CreateDisclosure(name, value);
                disclosures.Add(disclosure);
                topLevelSdDigests.Add(ComputeDigest(disclosure));
                processedClaims.Remove(name);
            }
            else
            {
                // Multi-segment: group by all-but-last segment
                var parentPath = "/" + string.Join("/", segments[..^1]);
                var leafSegment = segments[^1];

                if (!pathsByParent.ContainsKey(parentPath))
                    pathsByParent[parentPath] = new List<(string, string)>();

                pathsByParent[parentPath].Add((leafSegment, path));
            }
        }

        // Process each parent container. The container may be an object (nested
        // field disclosure → _sd array) or an array (element disclosure → each
        // requested index is replaced with a {"...": digest} placeholder).
        foreach (var (parentPath, leafEntries) in pathsByParent)
        {
            var parentSegments = ParsePointer(parentPath);
            var parentContainer = NavigateIntoContainer(processedClaims, parentSegments);
            if (parentContainer is null)
                continue;

            if (parentContainer is Dictionary<string, object> parentDict)
            {
                // Object container: extract named fields into a local _sd array.
                var nestedSdDigests = new List<string>();
                foreach (var (leafSegment, _) in leafEntries)
                {
                    if (!parentDict.TryGetValue(leafSegment, out var leafValue))
                        continue;
                    var disclosure = CreateDisclosure(leafSegment, leafValue);
                    disclosures.Add(disclosure);
                    nestedSdDigests.Add(ComputeDigest(disclosure));
                    parentDict.Remove(leafSegment);
                }

                if (nestedSdDigests.Count > 0)
                {
                    if (parentDict.TryGetValue("_sd", out var existingSd) && existingSd is List<string> existing)
                        existing.AddRange(nestedSdDigests);
                    else
                        parentDict["_sd"] = nestedSdDigests;
                }
            }
            else if (parentContainer is List<object> parentArray)
            {
                // Array container: replace each requested index with a placeholder
                // {"...": digest} object and emit a 2-element [salt, value] disclosure.
                // Per SD-JWT §5.2.4 — preserves the array length for non-disclosed
                // elements while hiding the disclosable ones cryptographically.
                foreach (var (leafSegment, _) in leafEntries)
                {
                    if (!int.TryParse(leafSegment, out var idx))
                        continue;
                    if (idx < 0 || idx >= parentArray.Count)
                        continue;

                    var disclosure = CreateArrayElementDisclosure(parentArray[idx]);
                    disclosures.Add(disclosure);
                    var digest = ComputeDigest(disclosure);
                    parentArray[idx] = new Dictionary<string, object> { ["..."] = digest };
                }
            }
        }

        return (processedClaims, disclosures, topLevelSdDigests);
    }

    /// <summary>
    /// Reconstructs the disclosed claims tree from an SD-JWT payload and the
    /// raw disclosures carried in the presentation. Implements the verifier
    /// algorithm from IETF SD-JWT §7.1:
    /// <list type="number">
    ///   <item>Hash each raw disclosure to build a digest → disclosed-value map.</item>
    ///   <item>Walk the payload tree. At every object, replace each digest in the
    ///   local <c>_sd</c> array with its matching object-field disclosure and merge
    ///   at the SAME depth (so <c>/address/locality</c> lands inside <c>address</c>,
    ///   not at the root).</item>
    ///   <item>At every array, replace <c>{"...": digest}</c> placeholders with the
    ///   matching array-element disclosure value. Placeholders with no matching
    ///   disclosure are dropped (cryptographically hidden).</item>
    ///   <item>Strip <c>_sd</c> and <c>_sd_alg</c> from the output. JWT-standard
    ///   claims (<c>iss</c>, <c>sub</c>, <c>iat</c>, <c>exp</c>, <c>cnf</c>) are
    ///   dropped — callers extract those from <c>basePayload</c> directly.</item>
    /// </list>
    /// </summary>
    /// <param name="basePayload">The JWT payload (may contain nested <c>_sd</c> arrays).</param>
    /// <param name="rawDisclosures">
    /// Base64url-encoded disclosure strings exactly as they appear in the tilde-
    /// separated presentation (no padding, no JWT signature suffix). Both the
    /// 3-element object-field form <c>[salt, name, value]</c> and the 2-element
    /// array-element form <c>[salt, value]</c> are supported. Malformed entries
    /// are silently skipped; they cannot cause a throw.
    /// </param>
    /// <returns>
    /// Merged claims dictionary at the correct tree depth. Objects become
    /// <see cref="Dictionary{TKey, TValue}"/>, arrays become <see cref="List{T}"/>,
    /// scalars are converted to their CLR equivalent.
    /// </returns>
    public static Dictionary<string, object> Reconstruct(
        Dictionary<string, JsonElement> basePayload,
        IEnumerable<string> rawDisclosures)
    {
        ArgumentNullException.ThrowIfNull(basePayload);
        ArgumentNullException.ThrowIfNull(rawDisclosures);

        var digestMap = new Dictionary<string, DecodedDisclosure>(StringComparer.Ordinal);
        foreach (var raw in rawDisclosures)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            if (!TryDecodeDisclosure(raw, out var decoded))
                continue;
            var digest = ComputeDigest(raw);
            // Duplicate digests would indicate a malformed presentation; keep the first.
            digestMap.TryAdd(digest, decoded);
        }

        // JWT-standard claims belong to the envelope, not the application claims.
        // Callers who need them read them directly off basePayload.
        var envelopeClaims = new HashSet<string>(StringComparer.Ordinal)
        {
            "iss", "sub", "iat", "exp", "cnf", "_sd", "_sd_alg",
        };

        var result = new Dictionary<string, object>(StringComparer.Ordinal);

        // Walk application-level properties, recursing through the tree.
        foreach (var (key, value) in basePayload)
        {
            if (envelopeClaims.Contains(key))
                continue;

            var processed = ProcessElement(value, digestMap);
            if (processed is not null)
                result[key] = processed;
        }

        // Resolve top-level _sd digests INTO the result (same depth = root).
        if (basePayload.TryGetValue("_sd", out var topLevelSd)
            && topLevelSd.ValueKind == JsonValueKind.Array)
        {
            ApplyObjectSdDigests(result, topLevelSd, digestMap);
        }

        return result;
    }

    // --- Reconstruct tree walk ---

    /// <summary>
    /// Recursively walks a single JSON element, resolving nested <c>_sd</c>
    /// digests and array-element disclosure placeholders against
    /// <paramref name="digestMap"/>. Returns the reconstructed subtree as a
    /// CLR object (Dictionary / List / primitive).
    /// </summary>
    private static object? ProcessElement(
        JsonElement element,
        IReadOnlyDictionary<string, DecodedDisclosure> digestMap)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);

                // First pass: copy through all non-envelope properties.
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name is "_sd" or "_sd_alg")
                        continue;
                    var child = ProcessElement(prop.Value, digestMap);
                    if (child is not null)
                        dict[prop.Name] = child;
                }

                // Second pass: resolve this object's _sd array AT this depth.
                if (element.TryGetProperty("_sd", out var sdArray)
                    && sdArray.ValueKind == JsonValueKind.Array)
                {
                    ApplyObjectSdDigests(dict, sdArray, digestMap);
                }

                return dict;
            }

            case JsonValueKind.Array:
            {
                var list = new List<object?>();
                foreach (var item in element.EnumerateArray())
                {
                    // Array-element placeholder: { "...": "<digest>" }.
                    // Per SD-JWT §5.2.4 the key is the literal three-dot string.
                    if (TryReadArrayPlaceholder(item, out var placeholderDigest))
                    {
                        if (digestMap.TryGetValue(placeholderDigest, out var disclosed)
                            && disclosed.Name is null)
                        {
                            // 2-element [salt, value] disclosure — inject the value at
                            // this index, recursing so an object value with further
                            // nested _sd also resolves.
                            list.Add(ProcessDisclosedValue(disclosed.Value, digestMap));
                        }
                        // else: placeholder remains hidden — drop it from output.
                        continue;
                    }

                    list.Add(ProcessElement(item, digestMap));
                }
                return list;
            }

            case JsonValueKind.String:
                return element.GetString() ?? string.Empty;
            case JsonValueKind.Number:
                return element.TryGetInt64(out var l) ? l : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return element.GetRawText();
        }
    }

    /// <summary>
    /// Injects object-field disclosures whose digests appear in the supplied
    /// <c>_sd</c> array into <paramref name="target"/> at this depth.
    /// Two-element array disclosures are ignored here — they only make sense
    /// inside arrays, not object <c>_sd</c> slots.
    /// </summary>
    private static void ApplyObjectSdDigests(
        Dictionary<string, object> target,
        JsonElement sdArray,
        IReadOnlyDictionary<string, DecodedDisclosure> digestMap)
    {
        foreach (var digestEl in sdArray.EnumerateArray())
        {
            if (digestEl.ValueKind != JsonValueKind.String)
                continue;
            var digest = digestEl.GetString();
            if (string.IsNullOrEmpty(digest))
                continue;
            if (!digestMap.TryGetValue(digest, out var disclosed))
                continue;
            if (disclosed.Name is null)
                continue; // array-element disclosure in an object slot — malformed

            var processed = ProcessDisclosedValue(disclosed.Value, digestMap);
            if (processed is not null)
                target[disclosed.Name] = processed;
        }
    }

    /// <summary>
    /// Recursively resolves any further <c>_sd</c> or array placeholders that
    /// live inside the disclosed value itself. This covers the "entire address
    /// is disclosable and contains its own nested _sd" shape where one
    /// disclosure transitively unlocks more.
    /// </summary>
    private static object? ProcessDisclosedValue(
        JsonElement? value,
        IReadOnlyDictionary<string, DecodedDisclosure> digestMap)
    {
        if (value is null)
            return null;
        return ProcessElement(value.Value, digestMap);
    }

    /// <summary>
    /// Returns true if <paramref name="element"/> is an SD-JWT array-element
    /// placeholder object with exactly one property keyed <c>"..."</c> whose
    /// value is a digest string.
    /// </summary>
    private static bool TryReadArrayPlaceholder(JsonElement element, out string digest)
    {
        digest = string.Empty;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        string? found = null;
        var propertyCount = 0;
        foreach (var prop in element.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 1)
                return false;
            if (prop.Name != "...")
                return false;
            if (prop.Value.ValueKind != JsonValueKind.String)
                return false;
            found = prop.Value.GetString();
        }

        if (propertyCount == 1 && !string.IsNullOrEmpty(found))
        {
            digest = found;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Decoded disclosure shape. <see cref="Name"/> is null for the 2-element
    /// array-element form; set for the 3-element object-field form.
    /// </summary>
    private sealed record DecodedDisclosure(string Salt, string? Name, JsonElement? Value);

    /// <summary>
    /// Decodes a base64url disclosure string into the (salt, name, value)
    /// tuple. Returns false on any parse error — disclosures that fail to
    /// decode are silently skipped, matching the spec's guidance that the
    /// verifier must never throw on attacker-controlled input.
    /// </summary>
    private static bool TryDecodeDisclosure(string raw, out DecodedDisclosure decoded)
    {
        decoded = null!;
        try
        {
            var bytes = Base64Url.DecodeFromChars(raw);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var items = doc.RootElement;
            var len = items.GetArrayLength();
            if (len == 3)
            {
                var salt = items[0].GetString() ?? string.Empty;
                var name = items[1].GetString();
                decoded = new DecodedDisclosure(salt, name, items[2].Clone());
                return true;
            }
            if (len == 2)
            {
                var salt = items[0].GetString() ?? string.Empty;
                decoded = new DecodedDisclosure(salt, null, items[1].Clone());
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    // --- Private helpers ---

    /// <summary>
    /// Parses an RFC 6901 JSON Pointer into its unescaped segments. Exposed
    /// internally so <see cref="SdJwtService"/> can reuse the same parser
    /// when correlating array-element paths to placeholder digests.
    /// </summary>
    internal static string[] ParsePointer(string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || pointer == "/")
            return [];

        // RFC 6901: split by /, skip the leading empty segment, then unescape
        // per §4: ~1 → / first, then ~0 → ~
        return pointer.Split('/').Where(s => s.Length > 0)
            .Select(s => s.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }

    /// <summary>
    /// Navigates into the claim tree following all segments. Returns either a
    /// <see cref="Dictionary{TKey, TValue}"/> (object container) or a
    /// <see cref="List{T}"/> of <see cref="object"/> (array container) at the
    /// final depth, or null if the path does not resolve. Arrays cause
    /// <see cref="NavigateInto"/> to return null; this helper exists so array
    /// disclosures (<c>/qualifications/1</c>) can resolve to the backing list.
    /// </summary>
    private static object? NavigateIntoContainer(Dictionary<string, object> root, string[] segments)
    {
        object current = root;
        foreach (var segment in segments)
        {
            if (current is Dictionary<string, object> dict)
            {
                if (!dict.TryGetValue(segment, out var child))
                    return null;
                var materialised = MaterialiseJsonElement(child);
                if (materialised is null)
                    return null;
                current = materialised;
            }
            else if (current is List<object> list)
            {
                if (!int.TryParse(segment, out var idx))
                    return null;
                if (idx < 0 || idx >= list.Count)
                    return null;
                var materialised = MaterialiseJsonElement(list[idx]);
                if (materialised is null)
                    return null;
                current = materialised;
            }
            else
            {
                return null;
            }
        }
        return current;
    }

    /// <summary>
    /// Lazily materialises a JsonElement container into Dictionary/List so
    /// subsequent navigation can mutate it. Non-container values are returned
    /// unchanged.
    /// </summary>
    private static object? MaterialiseJsonElement(object value)
    {
        if (value is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Object)
                return JsonElementToDict(elem);
            if (elem.ValueKind == JsonValueKind.Array)
                return elem.EnumerateArray().Select(e => (object)ConvertJsonElementToObject(e)).ToList();
        }
        return value;
    }

    /// <summary>
    /// Navigates into the object tree following all segments, returning
    /// the dictionary at the final segment depth.
    /// </summary>
    private static Dictionary<string, object>? NavigateInto(
        Dictionary<string, object> root, string[] segments)
    {
        var current = root;
        foreach (var segment in segments)
        {
            if (current.TryGetValue(segment, out var child))
            {
                if (child is Dictionary<string, object> dict)
                    current = dict;
                else if (child is JsonElement { ValueKind: JsonValueKind.Object } elem)
                {
                    var converted = JsonElementToDict(elem);
                    current[segment] = converted;
                    current = converted;
                }
                else
                    return null;
            }
            else
                return null;
        }
        return current;
    }

    private static Dictionary<string, object> JsonElementToDict(JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonElementToObject(prop.Value);
        }
        return dict;
    }

    private static object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => JsonElementToDict(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            _ => element.GetRawText()
        };
    }

    private static string CreateDisclosure(string claimName, object claimValue)
    {
        var salt = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var disclosure = JsonSerializer.Serialize(new object[] { salt, claimName, claimValue });
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(disclosure));
    }

    private static string CreateArrayElementDisclosure(object elementValue)
    {
        // SD-JWT spec §5.2.4: array element disclosures are two-element [salt, value]
        var salt = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var disclosure = JsonSerializer.Serialize(new object[] { salt, elementValue });
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(disclosure));
    }

    private static string ComputeDigest(string disclosure)
    {
        var bytes = Encoding.ASCII.GetBytes(disclosure);
        var hash = SHA256.HashData(bytes);
        return Base64Url.EncodeToString(hash);
    }

    private static Dictionary<string, object> DeepClone(Dictionary<string, object> source)
    {
        var json = JsonSerializer.Serialize(source);
        var element = JsonSerializer.Deserialize<JsonElement>(json);
        return JsonElementToDict(element);
    }
}
