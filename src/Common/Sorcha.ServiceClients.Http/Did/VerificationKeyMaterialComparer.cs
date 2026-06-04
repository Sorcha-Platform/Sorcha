// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using SimpleBase;
using Sorcha.ServiceClients.Http.Utilities;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Constant-time public-key-bytes equality for two <see cref="VerificationMethod"/>s,
/// regardless of the encoding used (<c>publicKeyMultibase</c> or <c>publicKeyJwk</c>) or
/// the fragment id (Feature 120 T058 — security-critical comparison underpinning the
/// cross-resolution algorithm).
/// </summary>
public static class VerificationKeyMaterialComparer
{
    /// <summary>
    /// Returns true iff the two verification methods carry the same raw public-key bytes.
    /// </summary>
    public static bool KeysMatch(VerificationMethod a, VerificationMethod b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var aBytes = ExtractKeyBytes(a);
        var bBytes = ExtractKeyBytes(b);
        if (aBytes is null || bBytes is null) return false;

        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    /// <summary>
    /// Extracts the raw public-key bytes from a verification method. Tries
    /// <c>publicKeyMultibase</c> first (canonical W3C form) then <c>publicKeyJwk</c>.
    /// Returns null if neither is present or both fail to decode.
    /// </summary>
    public static byte[]? ExtractKeyBytes(VerificationMethod vm)
    {
        ArgumentNullException.ThrowIfNull(vm);

        if (!string.IsNullOrEmpty(vm.PublicKeyMultibase))
        {
            var decoded = TryDecodeMultibase(vm.PublicKeyMultibase);
            if (decoded is not null) return decoded;
        }

        if (vm.PublicKeyJwk is { } jwk && jwk.ValueKind == JsonValueKind.Object)
        {
            return TryExtractJwkBytes(jwk);
        }

        return null;
    }

    private static byte[]? TryDecodeMultibase(string multibase)
    {
        if (multibase.Length < 2) return null;
        if (multibase[0] != Multicodec.Base58BtcPrefix) return null;

        try
        {
            var decoded = Base58.Bitcoin.Decode(multibase.AsSpan(1));
            // Strip the multicodec varint prefix — the first byte's high bit indicates
            // continuation. For the codecs we care about (ed25519=0xed varint, p256=0x1200,
            // rsa=0x1205) the varint is 1-2 bytes; we strip until we find a byte without
            // the continuation bit set, then take the rest as raw key bytes.
            var i = 0;
            while (i < decoded.Length && (decoded[i] & 0x80) != 0) i++;
            if (i >= decoded.Length) return null;
            return decoded[(i + 1)..].ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? TryExtractJwkBytes(JsonElement jwk)
    {
        if (!jwk.TryGetProperty("kty", out var ktyEl) || ktyEl.ValueKind != JsonValueKind.String)
            return null;

        // The thumbprint approach gives us a stable comparison surface across kty,
        // independent of how each kty serializes its key bytes.
        if (!KidThumbprintHelper.TryComputeThumbprint(jwk, out var thumbprint))
            return null;

        return System.Text.Encoding.UTF8.GetBytes(thumbprint);
    }
}
