// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Cryptography.Secp256k1.Tests;

public class EthereumAddressTests
{
    [Fact]
    public void Keccak256_EmptyInput_MatchesKnownVector()
    {
        var hash = Keccak256.ComputeHash(ReadOnlySpan<byte>.Empty);

        // Canonical Keccak-256("") — distinguishes Keccak from NIST SHA3.
        Convert.ToHexStringLower(hash)
            .Should().Be("c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470");
    }

    [Fact]
    public void FromPublicKey_GeneratorPoint_MatchesKnownEip55Address()
    {
        // Private key = 1 → public key = G → the well-known address 0x7E5F…395Bdf.
        var x = Convert.FromHexString("79BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798");
        var y = Convert.FromHexString("483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8");
        var key = Secp256k1PublicKey.FromCoordinates(x, y);

        EthereumAddress.FromPublicKey(key).Should().Be("0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf");
    }
}
