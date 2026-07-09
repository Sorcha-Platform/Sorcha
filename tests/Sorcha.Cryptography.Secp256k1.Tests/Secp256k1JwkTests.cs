// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class Secp256k1JwkTests
{
    // The secp256k1 generator point G — a convenient, well-known on-curve point.
    private const string GxHex = "79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798";
    private const string GyHex = "483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8";

    [Fact]
    public void TryParse_ThenToCoordinates_RoundTrips()
    {
        var x = Convert.FromHexString(GxHex);
        var y = Convert.FromHexString(GyHex);
        var jwk = Jwk("EC", "secp256k1", Base64Url.EncodeToString(x), Base64Url.EncodeToString(y));

        Secp256k1Jwk.TryParse(jwk, out var key).Should().BeTrue();
        key!.X.Should().Equal(x);
        key.Y.Should().Equal(y);

        var (bx, by) = Secp256k1Jwk.ToCoordinates(key);
        bx.Should().Be(Base64Url.EncodeToString(x));
        by.Should().Be(Base64Url.EncodeToString(y));
    }

    [Fact]
    public void FromSec1_CompressedPoint_DecompressesToSameCoordinates()
    {
        var x = Convert.FromHexString(GxHex);
        var y = Convert.FromHexString(GyHex);
        var full = Secp256k1PublicKey.FromCoordinates(x, y);
        var compressed = full.Point.GetEncoded(true); // 33 bytes, 0x02/0x03 || X

        var decompressed = Secp256k1PublicKey.FromSec1(compressed);
        decompressed.X.Should().Equal(x);
        decompressed.Y.Should().Equal(y);
    }

    [Fact]
    public void TryParse_NonSecp256k1Curve_ReturnsFalse()
    {
        Secp256k1Jwk.TryParse(Jwk("EC", "P-256", "AAAA", "AAAA"), out var p256).Should().BeFalse();
        p256.Should().BeNull();

        Secp256k1Jwk.TryParse(Jwk("OKP", "Ed25519", "AAAA", null), out var okp).Should().BeFalse();
        okp.Should().BeNull();
    }

    [Fact]
    public void TryParse_OffCurveCoordinates_ReturnsFalse()
    {
        var x = new byte[32];
        x[31] = 1;
        var y = new byte[32];
        y[31] = 1; // (1,1) is not on y^2 = x^3 + 7

        var jwk = Jwk("EC", "secp256k1", Base64Url.EncodeToString(x), Base64Url.EncodeToString(y));
        Secp256k1Jwk.TryParse(jwk, out var key).Should().BeFalse();
        key.Should().BeNull();
    }

    private static JsonElement Jwk(string kty, string crv, string x, string? y)
    {
        var json = y is null
            ? $$"""{"kty":"{{kty}}","crv":"{{crv}}","x":"{{x}}"}"""
            : $$"""{"kty":"{{kty}}","crv":"{{crv}}","x":"{{x}}","y":"{{y}}"}""";
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
