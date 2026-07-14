// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// The projection of an SD-JWT VC that the holder UI consumes: the reconstructed
/// claim tree, plus which of its top-level claims the holder actually controls.
/// </summary>
/// <param name="ClaimsJson">
/// Reconstructed claims as a JSON object. Nested disclosures land at their correct
/// depth (<c>/address/town</c> inside <c>address</c>, not beside it) and no
/// <c>_sd</c> / <c>_sd_alg</c> digest array survives at any depth.
/// </param>
/// <param name="DisclosableClaims">
/// Top-level claim names the holder can choose to withhold when presenting.
/// Everything else in <paramref name="ClaimsJson"/> always travels.
/// </param>
public sealed record SdJwtProjection(string ClaimsJson, IReadOnlyList<string> DisclosableClaims)
{
    /// <summary>A malformed or absent token projects to nothing — never to a throw.</summary>
    public static readonly SdJwtProjection Empty = new("{}", []);
}

/// <summary>
/// Decodes an SD-JWT VC into the shape the credential cards render.
///
/// Signature verification is deliberately NOT performed here: this projection is
/// for *display* on the pending-offer card, before the holder has chosen to trust
/// the issuer. Verification runs on accept.
///
/// This is the single authority for both the claim tree and the disclosable set —
/// the ingest path and the list endpoint MUST both come through it. The previous
/// hand-rolled decoder resolved only TOP-LEVEL <c>_sd</c>, so a nested disclosure
/// left <c>address</c> rendering as a raw digest array on a citizen's phone while
/// its children leaked out as flat top-level claims.
/// </summary>
public static class SdJwtClaimProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// SD-JWT / JWT envelope fields. Not credential claims, so they never reach a card.
    /// <see cref="NestedDisclosure.Reconstruct"/> already drops iss/sub/iat/exp/cnf/_sd/_sd_alg;
    /// these are the ones it leaves behind.
    /// </summary>
    private static readonly HashSet<string> ProtocolFields = new(StringComparer.Ordinal)
    {
        "iss", "sub", "iat", "exp", "nbf", "jti", "aud", "vct",
        "_sd", "_sd_alg", "cnf", "credentialStatus", "type", "status"
    };

    /// <summary>
    /// Projects a raw compact SD-JWT. Never throws — a malformed token yields
    /// <see cref="SdJwtProjection.Empty"/>, because one bad credential must not
    /// take down a holder's whole credential list.
    /// </summary>
    public static SdJwtProjection Project(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return SdJwtProjection.Empty;

        try
        {
            // <header>.<body>.<sig>~<disclosure>~…~[<kb-jwt>]
            var segments = rawToken.Split('~');
            var jwtParts = segments[0].Split('.');
            if (jwtParts.Length < 2) return SdJwtProjection.Empty;

            using var bodyDoc = JsonDocument.Parse(Base64Url.Decode(jwtParts[1]));
            if (bodyDoc.RootElement.ValueKind != JsonValueKind.Object) return SdJwtProjection.Empty;

            // Clone: the JsonElements must outlive the JsonDocument's using scope.
            var basePayload = bodyDoc.RootElement
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);

            // The optional trailing KB-JWT has dots; a disclosure never does.
            var disclosures = segments
                .Skip(1)
                .Where(s => !string.IsNullOrEmpty(s) && !s.Contains('.'))
                .ToArray();

            // Resolve nested _sd digests at their correct depth, stripping _sd/_sd_alg.
            var reconstructed = NestedDisclosure.Reconstruct(basePayload, disclosures);

            // Reconstruct keeps vct/nbf/jti/aud/credentialStatus/type — drop them.
            var claims = reconstructed
                .Where(kvp => !ProtocolFields.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);

            var disclosable = ResolveDisclosableClaims(basePayload, claims.Keys);

            return new SdJwtProjection(JsonSerializer.Serialize(claims, JsonOptions), disclosable);
        }
        catch
        {
            return SdJwtProjection.Empty;
        }
    }

    /// <summary>
    /// A top-level claim is ALWAYS disclosed iff it appears verbatim in the JWT body
    /// and nothing in its subtree carries an <c>_sd</c> array. Everything else in the
    /// reconstructed tree is disclosable — including a parent object such as
    /// <c>address</c> whose children are individually disclosable, because the holder
    /// does control what of it is revealed.
    /// </summary>
    private static List<string> ResolveDisclosableClaims(
        IReadOnlyDictionary<string, JsonElement> basePayload,
        IEnumerable<string> reconstructedKeys)
    {
        var alwaysDisclosed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, value) in basePayload)
        {
            if (ProtocolFields.Contains(key)) continue;
            if (!ContainsSd(value)) alwaysDisclosed.Add(key);
        }

        return reconstructedKeys
            .Where(k => !alwaysDisclosed.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// True if a selective-disclosure marker appears anywhere in this subtree —
    /// either shape from RFC 9901: an <c>_sd</c> digest array (object-field
    /// disclosure, §5.2.1) or an array-element placeholder object of the form
    /// <c>{"...": digest}</c> (§5.2.4). The array shape carries no <c>_sd</c> key
    /// at all, so it needs its own check or it walks straight through as
    /// "always disclosed".
    /// </summary>
    private static bool ContainsSd(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => IsArrayElementPlaceholder(element)
            || element.EnumerateObject().Any(p => p.Name == "_sd" || ContainsSd(p.Value)),
        JsonValueKind.Array => element.EnumerateArray().Any(ContainsSd),
        _ => false
    };

    /// <summary>
    /// True if <paramref name="element"/> is an SD-JWT array-element disclosure
    /// placeholder: an object with exactly one property, literally named
    /// <c>"..."</c> (three dots — RFC 9901 §5.2.4), whose value is a digest string.
    /// </summary>
    private static bool IsArrayElementPlaceholder(JsonElement element)
    {
        JsonProperty? only = null;
        foreach (var prop in element.EnumerateObject())
        {
            if (only is not null) return false; // more than one property
            only = prop;
        }

        return only is { Name: "...", Value.ValueKind: JsonValueKind.String };
    }

    /// <summary>
    /// Base64url with tolerant padding. The write path emits base64url (RFC 4648 §5);
    /// older payloads used raw base64, so both are accepted.
    /// </summary>
    private static class Base64Url
    {
        public static byte[] Decode(string raw)
        {
            try
            {
                var padded = raw.Replace('-', '+').Replace('_', '/');
                padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
                return Convert.FromBase64String(padded);
            }
            catch
            {
                return Convert.FromBase64String(raw);
            }
        }
    }
}
