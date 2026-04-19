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
    /// Reconstructs disclosed claims from a mix of top-level and nested disclosures,
    /// merging them into a unified claims dictionary.
    /// </summary>
    /// <remarks>
    /// WARNING: This method currently places nested disclosures at the top level
    /// (e.g., "locality" instead of "address.locality"). It does not walk nested
    /// _sd digests to inject values at the correct depth. This is a known gap
    /// from PR #226 review item #2. The method is not called from any production
    /// path — SdJwtService.VerifyTokenAsync uses its own disclosure parsing.
    /// Fix required before nested selective disclosure is used end-to-end.
    /// </remarks>
    /// <param name="basePayload">The JWT payload (may contain nested _sd arrays).</param>
    /// <param name="disclosures">Decoded disclosure arrays.</param>
    /// <returns>Merged claims dictionary.</returns>
    public static Dictionary<string, object> Reconstruct(
        Dictionary<string, JsonElement> basePayload,
        List<(string salt, string name, object value)> disclosures)
    {
        var result = new Dictionary<string, object>();
        var reservedClaims = new HashSet<string> { "iss", "sub", "iat", "exp", "_sd", "_sd_alg", "cnf" };

        // Add non-reserved, non-_sd claims from payload
        foreach (var (key, value) in basePayload)
        {
            if (reservedClaims.Contains(key))
                continue;

            result[key] = ConvertAndMergeDisclosures(value, disclosures);
        }

        // Add top-level disclosures
        foreach (var (_, name, value) in disclosures)
        {
            if (!result.ContainsKey(name))
                result[name] = value;
        }

        return result;
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

    private static object ConvertAndMergeDisclosures(JsonElement value,
        List<(string salt, string name, object value)> disclosures)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in value.EnumerateObject())
            {
                if (prop.Name == "_sd")
                    continue; // Skip _sd arrays in nested objects — disclosures handle these

                dict[prop.Name] = ConvertAndMergeDisclosures(prop.Value, disclosures);
            }
            return dict;
        }

        return ConvertJsonElementToObject(value);
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
