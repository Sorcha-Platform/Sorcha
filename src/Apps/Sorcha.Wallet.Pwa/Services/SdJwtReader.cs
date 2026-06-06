// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
    /// Returns the name→value pairs the wallet holds for this credential.
    /// Skips array-element disclosures (2-tuples, no name) and any segment that
    /// fails to decode. Order is the on-wire disclosure order.
    /// </summary>
    public static IReadOnlyList<DisclosedClaim> ReadDisclosedClaims(string? rawSdJwt)
    {
        var result = new List<DisclosedClaim>();
        if (string.IsNullOrWhiteSpace(rawSdJwt)) return result;

        var segments = rawSdJwt.Split('~');
        if (segments.Length < 2) return result;

        // A trailing key-binding JWT (3 dot-separated parts) is not a disclosure.
        var last = segments[^1];
        var hasKbJwt = !string.IsNullOrEmpty(last) && last.Count(c => c == '.') == 2;
        var disclosureCount = segments.Length - 1 - (hasKbJwt ? 1 : 0);

        for (var i = 1; i <= disclosureCount; i++)
        {
            var seg = segments[i];
            if (string.IsNullOrEmpty(seg)) continue;
            try
            {
                var bytes = Base64Url.DecodeFromChars(seg);
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 3) continue;

                var name = root[1].GetString();
                if (string.IsNullOrEmpty(name)) continue;
                result.Add(new DisclosedClaim(name, JsonValueToString(root[2])));
            }
            catch
            {
                // Undecodable / malformed disclosure — skip, don't break the page.
            }
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

    private static string JsonValueToString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText(),
    };
}
