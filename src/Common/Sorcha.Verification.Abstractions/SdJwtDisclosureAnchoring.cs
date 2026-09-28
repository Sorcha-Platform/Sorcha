// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Verification.Abstractions;

/// <summary>
/// RFC 9901 SD-JWT disclosure anchoring — the ONE rule every Sorcha verifier applies (#1199).
/// </summary>
/// <remarks>
/// <para>
/// A disclosure is only a claim the ISSUER made if its digest is committed by the issuer-signed
/// payload: an entry of an <c>_sd</c> array, or an array-element <c>{"...": digest}</c> marker —
/// in the payload itself or inside the value of an already-anchored disclosure (nested selective
/// disclosure, resolved to fixpoint so segment order does not matter). Anything else was appended
/// by whoever presented the token. The KB-JWT <c>sd_hash</c> does NOT substitute for this: it is
/// signed by the presenter, so it proves only that the presenter chose that set of segments.
/// </para>
/// <para>
/// This lived only in <c>VerifiablePresentationValidator</c>; <c>SdJwtService.VerifyTokenAsync</c>
/// — behind the HAIP OpenID4VP verifier, the blueprint credential gate and the wallet's
/// presentation checks — accepted every disclosure, so the two verifiers disagreed on what a valid
/// presentation is and the weaker one was on the external surface. One implementation, shared, is
/// the fix; a second copy would recreate the disagreement.
/// </para>
/// </remarks>
public static class SdJwtDisclosureAnchoring
{
    /// <summary>The only <c>_sd_alg</c> this platform verifies (RFC 9901's default).</summary>
    public const string SupportedSdAlg = "sha-256";

    /// <summary>
    /// Whether the payload's <c>_sd_alg</c> can be evaluated. Absent means the RFC default
    /// (sha-256). An unsupported algorithm must be reported as such — reporting it as
    /// "unanchored" would accuse a legitimate issuer of tampering.
    /// </summary>
    public static bool IsSupportedSdAlg(JsonElement credentialPayload, out string? sdAlg)
    {
        sdAlg = credentialPayload.ValueKind == JsonValueKind.Object
                && credentialPayload.TryGetProperty("_sd_alg", out var alg)
                && alg.ValueKind == JsonValueKind.String
            ? alg.GetString()
            : null;
        return sdAlg is null || string.Equals(sdAlg, SupportedSdAlg, StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns the display names of every disclosure segment whose SHA-256 digest is NOT committed
    /// by the credential. Empty means every presented disclosure is issuer-committed.
    /// </summary>
    public static IReadOnlyList<string> FindUnanchoredDisclosures(
        JsonElement credentialPayload,
        IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) return [];

        var committed = new HashSet<string>(StringComparer.Ordinal);
        CollectSdDigests(credentialPayload, committed);

        var anchored = new bool[segments.Count];
        var progressed = true;
        while (progressed)
        {
            progressed = false;
            for (var i = 0; i < segments.Count; i++)
            {
                if (anchored[i]) continue;
                if (!committed.Contains(DigestOf(segments[i]))) continue;

                anchored[i] = true;
                progressed = true;

                // Nested SD: the disclosed value may itself carry _sd arrays that commit
                // further disclosures (e.g. address sub-fields).
                try
                {
                    using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segments[i]));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array &&
                        doc.RootElement.GetArrayLength() is 2 or 3)
                    {
                        CollectSdDigests(doc.RootElement[doc.RootElement.GetArrayLength() - 1], committed);
                    }
                }
                catch
                {
                    // A segment that anchors but doesn't parse contributes no nested digests; the
                    // caller's own parse reports it.
                }
            }
        }

        var unanchoredNames = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            if (!anchored[i]) unanchoredNames.Add(ReadDisclosureDisplayName(segments[i]));
        }
        return unanchoredNames;
    }

    /// <summary>The RFC 9901 digest of a disclosure segment: base64url(SHA-256(ASCII(segment))).</summary>
    public static string DigestOf(string segment) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(segment)));

    /// <summary>
    /// Recursively collect every digest the credential commits: string entries of <c>_sd</c>
    /// arrays and the values of <c>{"...": digest}</c> array-element markers. Harvesting a marker
    /// anywhere in an anchored subtree is deliberate lenience: digests are unguessable SHA-256
    /// values, so over-collection can never anchor a forged disclosure.
    /// </summary>
    private static void CollectSdDigests(JsonElement element, HashSet<string> sink)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("_sd") && property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var digest in property.Value.EnumerateArray())
                        {
                            if (digest.ValueKind == JsonValueKind.String)
                                sink.Add(digest.GetString()!);
                        }
                    }
                    else if (property.NameEquals("...") && property.Value.ValueKind == JsonValueKind.String)
                    {
                        sink.Add(property.Value.GetString()!);
                    }
                    else
                    {
                        CollectSdDigests(property.Value, sink);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectSdDigests(item, sink);
                break;
        }
    }

    /// <summary>Best-effort claim name of a disclosure segment for error messages; never throws.</summary>
    public static string ReadDisclosureDisplayName(string segment)
    {
        try
        {
            using var doc = JsonDocument.Parse(Base64Url.DecodeFromChars(segment));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "<malformed>";
            return doc.RootElement.GetArrayLength() switch
            {
                3 => doc.RootElement[1].GetString() ?? "<unnamed>",
                2 => doc.RootElement[0].GetString() ?? "<unnamed>",
                _ => "<malformed>",
            };
        }
        catch
        {
            return "<malformed>";
        }
    }
}
