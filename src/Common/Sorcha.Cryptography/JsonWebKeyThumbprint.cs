// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Cryptography;

/// <summary>
/// RFC 7638 JSON Web Key Thumbprint helper.
/// </summary>
/// <remarks>
/// Computes a deterministic SHA-256 thumbprint of a JWK by hashing the canonical
/// (lexicographically-ordered, no whitespace) JSON representation of the key's
/// required members. Used by Feature 120 to support thumbprint-based <c>kid</c>
/// matching in W3C DID documents (RFC 7517 §4.7 hybrid kid scheme).
/// </remarks>
public static class JsonWebKeyThumbprint
{
    /// <summary>
    /// Computes the RFC 7638 SHA-256 thumbprint of the supplied JWK and returns
    /// it as a base64url string with no padding (43 chars).
    /// </summary>
    /// <param name="jwk">JWK as a JSON element (object).</param>
    /// <returns>43-char base64url-encoded SHA-256 thumbprint.</returns>
    /// <exception cref="ArgumentException">JWK is not a JSON object, has no <c>kty</c>, or its <c>kty</c> is unsupported.</exception>
    public static string Compute(JsonElement jwk)
    {
        if (jwk.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("JWK must be a JSON object", nameof(jwk));

        if (!jwk.TryGetProperty("kty", out var ktyEl) || ktyEl.ValueKind != JsonValueKind.String)
            throw new ArgumentException("JWK is missing required 'kty' member", nameof(jwk));

        var canonical = BuildCanonicalJson(jwk, ktyEl.GetString()!);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Base64UrlEncodeNoPadding(hash);
    }

    /// <summary>
    /// Computes the RFC 7638 SHA-256 thumbprint of a JWK supplied as a JSON string.
    /// </summary>
    public static string Compute(string jwkJson)
    {
        using var doc = JsonDocument.Parse(jwkJson);
        return Compute(doc.RootElement);
    }

    private static string BuildCanonicalJson(JsonElement jwk, string kty)
    {
        // RFC 7638 §3.2 — required members per kty, in lexicographic order.
        var required = kty switch
        {
            "EC" => new[] { "crv", "kty", "x", "y" },
            "RSA" => new[] { "e", "kty", "n" },
            "OKP" => new[] { "crv", "kty", "x" },
            "oct" => new[] { "k", "kty" },
            _ => throw new ArgumentException($"Unsupported JWK kty: {kty}", nameof(jwk))
        };

        var sb = new StringBuilder();
        sb.Append('{');
        for (var i = 0; i < required.Length; i++)
        {
            var name = required[i];
            if (!jwk.TryGetProperty(name, out var val) || val.ValueKind != JsonValueKind.String)
                throw new ArgumentException($"JWK is missing required '{name}' member for kty={kty}", nameof(jwk));

            if (i > 0) sb.Append(',');
            sb.Append('"').Append(name).Append("\":\"").Append(val.GetString()).Append('"');
        }
        sb.Append('}');
        return sb.ToString();
    }

    private static string Base64UrlEncodeNoPadding(ReadOnlySpan<byte> bytes)
    {
        Span<byte> b64 = stackalloc byte[Base64.GetMaxEncodedToUtf8Length(bytes.Length)];
        Base64.EncodeToUtf8(bytes, b64, out _, out var written);
        Span<char> chars = stackalloc char[written];
        var pad = 0;
        for (var i = 0; i < written; i++)
        {
            var c = (char)b64[i];
            if (c == '=') { pad++; continue; }
            chars[i] = c switch { '+' => '-', '/' => '_', _ => c };
        }
        return new string(chars[..(written - pad)]);
    }
}
