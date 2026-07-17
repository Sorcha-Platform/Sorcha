// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Wallet.Pwa.Services;

/// <summary>One disclosed SD-JWT claim — a human name and its rendered value.</summary>
public sealed record DisclosedClaim(string Name, string Value);

/// <summary>
/// Reads the disclosed claims and expiry out of a cached SD-JWT VC, in pure
/// .NET (Base64Url → JSON of <c>[salt, name, value]</c>). This does NOT need
/// the libsodium / xchacha bridge — that's the at-rest cache cipher, a separate
/// concern from decoding the (already-plaintext) disclosure segments.
/// </summary>
public static class SdJwtReader
{
    /// <summary>
    /// SD-JWT / JWT envelope fields. Not credential claims, so they never reach a card.
    /// Mirrors the server's <c>SdJwtClaimProjection.ProtocolFields</c> — this reader is
    /// the on-device equivalent of that projection and must drop the same fields.
    /// </summary>
    private static readonly HashSet<string> ProtocolFields = new(StringComparer.Ordinal)
    {
        "iss", "sub", "iat", "exp", "nbf", "jti", "aud", "vct",
        "_sd", "_sd_alg", "cnf", "credentialStatus", "type", "status",
    };

    /// <summary>
    /// Returns the name→value pairs the wallet holds for this credential. Reads BOTH
    /// the JWT body and the disclosures, resolving nested <c>_sd</c> digest arrays at
    /// their correct depth — e.g. a disclosed <c>town</c> lands inside the reconstructed
    /// <c>address</c> object, not as a flat top-level "town" claim — and includes
    /// always-disclosed body claims (present verbatim, no digest indirection). Strips
    /// <c>_sd</c>/<c>_sd_alg</c> and JWT protocol fields at every depth.
    /// <para>
    /// The previous implementation read ONLY the disclosure segments, so a nested
    /// disclosure left the parent object unresolved (or missing) and its children
    /// leaked out as flat top-level claims — the same class of bug that shipped the
    /// raw-digest-array leak on the server side.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DisclosedClaim> ReadDisclosedClaims(string? rawSdJwt)
    {
        var result = new List<DisclosedClaim>();
        if (string.IsNullOrWhiteSpace(rawSdJwt)) return result;

        var segments = rawSdJwt.Split('~');
        if (segments.Length < 2) return result;

        var jwtParts = segments[0].Split('.');
        if (jwtParts.Length < 2) return result;

        try
        {
            var bodyBytes = Base64Url.DecodeFromChars(jwtParts[1]);
            using var bodyDoc = JsonDocument.Parse(bodyBytes);
            if (bodyDoc.RootElement.ValueKind != JsonValueKind.Object) return result;

            // A trailing key-binding JWT (3 dot-separated parts) is not a disclosure.
            var last = segments[^1];
            var hasKbJwt = !string.IsNullOrEmpty(last) && last.Count(c => c == '.') == 2;
            var disclosureCount = segments.Length - 1 - (hasKbJwt ? 1 : 0);

            var digestMap = BuildDigestMap(segments, disclosureCount);
            var reconstructed = ProcessObject(bodyDoc.RootElement, digestMap);

            foreach (var (key, node) in reconstructed)
            {
                if (ProtocolFields.Contains(key)) continue;
                result.Add(new DisclosedClaim(key, NodeToString(node)));
            }
        }
        catch
        {
            // Undecodable / malformed token — no disclosed claims rather than a throw.
        }

        return result;
    }

    /// <summary>Reads the credential's <c>exp</c> claim (the JWT body before the first '~').</summary>
    public static DateTimeOffset? ReadExpiry(string? rawSdJwt)
    {
        if (string.IsNullOrWhiteSpace(rawSdJwt)) return null;

        var jwt = rawSdJwt.Split('~')[0];
        var parts = jwt.Split('.');
        if (parts.Length < 2) return null;

        try
        {
            var bytes = Base64Url.DecodeFromChars(parts[1]);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("exp", out var exp) && exp.ValueKind == JsonValueKind.Number)
                return DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        }
        catch
        {
            // Not a decodable JWT body — treat as "no expiry known".
        }
        return null;
    }

    /// <summary>Reads the credential's <c>vct</c> claim (the JWT body before the first '~'); empty when absent.</summary>
    public static string ReadVct(string? rawSdJwt)
    {
        if (string.IsNullOrWhiteSpace(rawSdJwt)) return string.Empty;

        var jwt = rawSdJwt.Split('~')[0];
        var parts = jwt.Split('.');
        if (parts.Length < 2) return string.Empty;

        try
        {
            var bytes = Base64Url.DecodeFromChars(parts[1]);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.TryGetProperty("vct", out var vct) && vct.ValueKind == JsonValueKind.String)
                return vct.GetString() ?? string.Empty;
        }
        catch
        {
            // Not a decodable JWT body — treat as "no vct known".
        }
        return string.Empty;
    }

    // --- Disclosure reconstruction (ports the server's NestedDisclosure.Reconstruct /
    // SdJwtClaimProjection algorithm into pure BCL — System.Text.Json[.Nodes] only, no
    // Sorcha.Cryptography, since this runs in Blazor WebAssembly.) ---

    /// <summary>A decoded disclosure: its claim name (null for the array-element form) and value.</summary>
    private sealed record DecodedDisclosure(string? Name, JsonElement Value);

    /// <summary>
    /// Decodes every disclosure segment and indexes it by its digest — <c>base64url(SHA256(ascii(disclosure)))</c> —
    /// exactly as it would appear inside an <c>_sd</c> array or an array-element placeholder.
    /// </summary>
    private static Dictionary<string, DecodedDisclosure> BuildDigestMap(string[] segments, int disclosureCount)
    {
        var map = new Dictionary<string, DecodedDisclosure>(StringComparer.Ordinal);
        for (var i = 1; i <= disclosureCount; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrEmpty(seg)) continue;
            try
            {
                var bytes = Base64Url.DecodeFromChars(seg);
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) continue;

                var len = root.GetArrayLength();
                if (len == 3)
                {
                    var name = root[1].GetString();
                    map[ComputeDigest(seg)] = new DecodedDisclosure(name, root[2].Clone());
                }
                else if (len == 2)
                {
                    map[ComputeDigest(seg)] = new DecodedDisclosure(null, root[1].Clone());
                }
            }
            catch
            {
                // Undecodable / malformed disclosure — skip, don't break the page.
            }
        }
        return map;
    }

    /// <summary>
    /// Reconstructs an object element: copies its non-<c>_sd</c> properties through
    /// unchanged (recursing so nested containers resolve too), then resolves this
    /// object's own <c>_sd</c> array AT THIS DEPTH — so a disclosed field merges into
    /// its parent rather than surfacing as a sibling of it.
    /// </summary>
    private static JsonObject ProcessObject(JsonElement element, IReadOnlyDictionary<string, DecodedDisclosure> digestMap)
    {
        var obj = new JsonObject();
        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Name is "_sd" or "_sd_alg") continue;
            obj[prop.Name] = ProcessValue(prop.Value, digestMap);
        }

        if (element.TryGetProperty("_sd", out var sdArray) && sdArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var digestEl in sdArray.EnumerateArray())
            {
                if (digestEl.ValueKind != JsonValueKind.String) continue;
                var digest = digestEl.GetString();
                if (string.IsNullOrEmpty(digest)) continue;
                if (!digestMap.TryGetValue(digest, out var disclosed)) continue;
                if (disclosed.Name is null) continue; // array-element disclosure in an object slot — malformed
                obj[disclosed.Name] = ProcessValue(disclosed.Value, digestMap);
            }
        }

        return obj;
    }

    private static JsonNode? ProcessValue(JsonElement element, IReadOnlyDictionary<string, DecodedDisclosure> digestMap)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ProcessObject(element, digestMap);

            case JsonValueKind.Array:
            {
                var array = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    if (TryReadArrayPlaceholder(item, out var placeholderDigest))
                    {
                        if (digestMap.TryGetValue(placeholderDigest, out var disclosed) && disclosed.Name is null)
                            array.Add(ProcessValue(disclosed.Value, digestMap));
                        // else: placeholder remains hidden — drop it from output.
                        continue;
                    }
                    array.Add(ProcessValue(item, digestMap));
                }
                return array;
            }

            case JsonValueKind.Null:
                return null;

            default:
                // String / Number / True / False — round-trip through JsonNode.Parse so
                // the scalar's exact representation survives.
                return JsonNode.Parse(element.GetRawText());
        }
    }

    /// <summary>
    /// True if <paramref name="element"/> is an SD-JWT array-element disclosure
    /// placeholder: an object with exactly one property, literally named <c>"..."</c>
    /// (three dots — RFC 9901 §5.2.4), whose value is a digest string.
    /// </summary>
    private static bool TryReadArrayPlaceholder(JsonElement element, out string digest)
    {
        digest = string.Empty;
        if (element.ValueKind != JsonValueKind.Object) return false;

        string? found = null;
        var propertyCount = 0;
        foreach (var prop in element.EnumerateObject())
        {
            propertyCount++;
            if (propertyCount > 1) return false;
            if (prop.Name != "...") return false;
            if (prop.Value.ValueKind != JsonValueKind.String) return false;
            found = prop.Value.GetString();
        }

        if (propertyCount == 1 && !string.IsNullOrEmpty(found))
        {
            digest = found;
            return true;
        }
        return false;
    }

    private static string ComputeDigest(string disclosure)
    {
        var bytes = Encoding.ASCII.GetBytes(disclosure);
        var hash = SHA256.HashData(bytes);
        return Base64Url.EncodeToString(hash);
    }

    /// <summary>Renders a reconstructed node through the same safe formatting as a raw claim value.</summary>
    private static string NodeToString(JsonNode? node)
    {
        if (node is null) return string.Empty;
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return JsonValueToString(doc.RootElement);
    }

    /// <summary>
    /// Renders a disclosure value for the card. An object or array NEVER renders as
    /// raw JSON — that is how an unresolved SD-JWT digest array reached a citizen's
    /// card on the web. This reader is the on-device equivalent and must not repeat it.
    /// </summary>
    private static string JsonValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Object => SummariseObject(value),
        JsonValueKind.Array => SummariseArray(value),
        _ => string.Empty,
    };

    /// <summary>
    /// A nested object renders as "field: value" pairs, dropping SD-JWT protocol keys
    /// so a digest array degrades to an empty string rather than a blob of base64.
    /// </summary>
    private static string SummariseObject(JsonElement value)
    {
        var parts = value.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => $"{p.Name}: {JsonValueToString(p.Value)}")
            .Where(s => !s.EndsWith(": ", StringComparison.Ordinal))
            .ToList();

        return string.Join(", ", parts);
    }

    private static string SummariseArray(JsonElement value)
    {
        var parts = value.EnumerateArray()
            .Select(JsonValueToString)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return string.Join(", ", parts);
    }
}
