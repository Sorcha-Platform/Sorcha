// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>
/// Parses and builds JOSE EC JWKs for the secp256k1 curve (<c>kty:"EC"</c>, <c>crv:"secp256k1"</c>).
/// Used by the verification paths to read an issuer/holder key, and by the <c>did:key</c>/<c>did:jwk</c>
/// resolvers to emit a <c>publicKeyJwk</c> verification method.
/// </summary>
public static class Secp256k1Jwk
{
    /// <summary>
    /// Try to parse a secp256k1 public key from a JOSE EC JWK. Returns <c>false</c> (never throws) for
    /// any JWK that is not a well-formed secp256k1 <c>EC</c> key, or whose coordinates are off-curve.
    /// </summary>
    public static bool TryParse(JsonElement jwk, out Secp256k1PublicKey? key)
    {
        key = null;

        if (jwk.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!jwk.TryGetProperty("kty", out var kty) || kty.ValueKind != JsonValueKind.String || kty.GetString() != "EC")
        {
            return false;
        }

        if (!jwk.TryGetProperty("crv", out var crv) || crv.ValueKind != JsonValueKind.String || crv.GetString() != "secp256k1")
        {
            return false;
        }

        if (!jwk.TryGetProperty("x", out var xEl) || xEl.ValueKind != JsonValueKind.String ||
            !jwk.TryGetProperty("y", out var yEl) || yEl.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        try
        {
            var x = Base64Url.DecodeFromChars(xEl.GetString());
            var y = Base64Url.DecodeFromChars(yEl.GetString());
            key = Secp256k1PublicKey.FromCoordinates(x, y);
            return true;
        }
        catch
        {
            key = null;
            return false;
        }
    }

    /// <summary>
    /// The base64url-encoded affine coordinates for building a <c>publicKeyJwk</c> verification method.
    /// </summary>
    public static (string X, string Y) ToCoordinates(Secp256k1PublicKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return (Base64Url.EncodeToString(key.X), Base64Url.EncodeToString(key.Y));
    }
}
