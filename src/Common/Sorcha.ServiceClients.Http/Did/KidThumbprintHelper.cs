// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves a credential's <c>kid</c> header to a <see cref="VerificationMethod"/>
/// inside a W3C DID document, supporting both the versioned-id and RFC 7638
/// thumbprint-id schemes that Sorcha publishes (Feature 120 D3).
/// </summary>
/// <remarks>
/// The verifier path calls <see cref="TryMatchExact"/> first, then falls back to
/// <see cref="TryMatchByThumbprint"/>. Hybrid matching lets external wallets cite
/// Sorcha-issued keys by either the platform-default versioned form
/// (<c>did:sorcha:org:{addr}#vc-issuance-{rotationIndex}</c>) or by the
/// algorithm-stable thumbprint form (<c>did:sorcha:org:{addr}#{thumbprint}</c>).
/// </remarks>
public static class KidThumbprintHelper
{
    /// <summary>
    /// Attempts to find a verification method whose <c>id</c> exactly matches the supplied <paramref name="kid"/>.
    /// </summary>
    /// <param name="document">DID document to search.</param>
    /// <param name="kid">JWS <c>kid</c> header value.</param>
    /// <param name="vm">Matched verification method on success.</param>
    /// <returns><c>true</c> if a match was found.</returns>
    public static bool TryMatchExact(
        DidDocument document,
        string kid,
        [NotNullWhen(true)] out VerificationMethod? vm)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(kid))
        {
            vm = null;
            return false;
        }

        foreach (var candidate in document.VerificationMethod)
        {
            if (string.Equals(candidate.Id, kid, StringComparison.Ordinal))
            {
                vm = candidate;
                return true;
            }
        }

        vm = null;
        return false;
    }

    /// <summary>
    /// Attempts to find a verification method whose RFC 7638 JWK thumbprint matches the
    /// fragment of the supplied <paramref name="kid"/>. The kid fragment may be either
    /// the bare 43-char base64url thumbprint or a full DID URL ending in <c>#{thumbprint}</c>.
    /// </summary>
    /// <param name="document">DID document to search.</param>
    /// <param name="kid">JWS <c>kid</c> header value.</param>
    /// <param name="vm">Matched verification method on success.</param>
    /// <returns><c>true</c> if a match was found.</returns>
    /// <remarks>
    /// Verification methods that expose only <c>publicKeyMultibase</c> (no <c>publicKeyJwk</c>)
    /// cannot be thumbprint-matched in v1 — the multibase-to-JWK round-trip is deferred. Sorcha
    /// emits dual VMs per active key (Feature 120 D3) so the JWK form is always available on
    /// the Sorcha-issued path.
    /// </remarks>
    public static bool TryMatchByThumbprint(
        DidDocument document,
        string kid,
        [NotNullWhen(true)] out VerificationMethod? vm)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrEmpty(kid))
        {
            vm = null;
            return false;
        }

        var fragment = ExtractFragment(kid);
        if (string.IsNullOrEmpty(fragment))
        {
            vm = null;
            return false;
        }

        foreach (var candidate in document.VerificationMethod)
        {
            if (candidate.PublicKeyJwk is not { } jwk) continue;

            if (!TryComputeThumbprint(jwk, out var thumbprint))
                continue;

            if (string.Equals(thumbprint, fragment, StringComparison.Ordinal))
            {
                vm = candidate;
                return true;
            }
        }

        vm = null;
        return false;
    }

    private static string ExtractFragment(string kid)
    {
        var hash = kid.LastIndexOf('#');
        return hash < 0 ? kid : kid[(hash + 1)..];
    }

    /// <summary>
    /// RFC 7638 SHA-256 JWK thumbprint, base64url, no padding. Inlined here to keep
    /// <c>Sorcha.ServiceClients.Http</c> free of a project reference to
    /// <c>Sorcha.Cryptography</c> (Sodium/BouncyCastle) — see the <c>Multicodec</c> helper
    /// for the same architectural rationale.
    /// </summary>
    internal static bool TryComputeThumbprint(JsonElement jwk, [NotNullWhen(true)] out string? thumbprint)
    {
        thumbprint = null;
        if (jwk.ValueKind != JsonValueKind.Object) return false;
        if (!jwk.TryGetProperty("kty", out var ktyEl) || ktyEl.ValueKind != JsonValueKind.String) return false;

        string[] required = ktyEl.GetString() switch
        {
            "EC" => ["crv", "kty", "x", "y"],
            "RSA" => ["e", "kty", "n"],
            "OKP" => ["crv", "kty", "x"],
            "oct" => ["k", "kty"],
            _ => []
        };
        if (required.Length == 0) return false;

        var sb = new StringBuilder("{");
        for (var i = 0; i < required.Length; i++)
        {
            if (!jwk.TryGetProperty(required[i], out var v) || v.ValueKind != JsonValueKind.String) return false;
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(required[i]).Append("\":\"").Append(v.GetString()).Append('"');
        }
        sb.Append('}');

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        Span<byte> b64 = stackalloc byte[Base64.GetMaxEncodedToUtf8Length(hash.Length)];
        Base64.EncodeToUtf8(hash, b64, out _, out var written);
        Span<char> chars = stackalloc char[written];
        var pad = 0;
        for (var i = 0; i < written; i++)
        {
            var c = (char)b64[i];
            if (c == '=') { pad++; continue; }
            chars[i] = c switch { '+' => '-', '/' => '_', _ => c };
        }
        thumbprint = new string(chars[..(written - pad)]);
        return true;
    }
}
