// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Feature 093 US3 — tests for the Multicodec helper that produces W3C-valid
// publicKeyMultibase values via "z" + Base58btc(multicodec || rawKeyBytes).
//
// After review: the helper moved from Sorcha.Cryptography.Utilities to
// Sorcha.ServiceClients.Http.Utilities so SorchaDidResolver can reuse it
// without pulling in the full crypto assembly. The API is string-based
// (algorithm name) rather than enum-based to match the resolver's usage.

using FluentAssertions;
using SimpleBase;
using Sorcha.ServiceClients.Http.Utilities;
using Xunit;

namespace Sorcha.ServiceClients.Tests.Utilities;

public class MulticodecTests
{
    // 32 bytes — a real Ed25519 public key is exactly this size.
    private static readonly byte[] TestEd25519Key = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };

    // 33 bytes — SEC1 compressed form of a NIST P-256 public key.
    private static readonly byte[] TestP256Key = new byte[]
    {
        0x02, // SEC1 compressed form tag
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };

    // 64 bytes — synthetic RSA test stand-in. Note: a real RSA-4096 SubjectPublicKeyInfo
    // is ~550 bytes DER-encoded. These tests only exercise the multicodec varint prefix,
    // which is algorithm-independent and does not care about the real key length.
    private static readonly byte[] TestRsaKey = new byte[64];

    [Fact]
    public void Ed25519_EncodePublicKey_PrefixesWithEd25519Varint()
    {
        var result = Multicodec.EncodePublicKey("ED25519", TestEd25519Key);

        result.Should().NotBeNull();
        // Unsigned varint of 0xed is 0xed 0x01 (high bit set, so needs 2 bytes).
        result![0].Should().Be(0xed, because: "Ed25519 multicodec varint first byte");
        result[1].Should().Be(0x01, because: "Ed25519 multicodec varint second byte");
        result.Length.Should().Be(2 + TestEd25519Key.Length);
        result.Skip(2).Should().Equal(TestEd25519Key);
    }

    [Fact]
    public void NistP256_EncodePublicKey_PrefixesWithP256Varint()
    {
        var result = Multicodec.EncodePublicKey("NIST-P256", TestP256Key);

        result.Should().NotBeNull();
        // Unsigned varint of 0x1200 is 0x80 0x24.
        result![0].Should().Be(0x80, because: "P-256 multicodec varint first byte (0x1200 varint)");
        result[1].Should().Be(0x24, because: "P-256 multicodec varint second byte");
        result.Length.Should().Be(2 + TestP256Key.Length);
        result.Skip(2).Should().Equal(TestP256Key);
    }

    [Fact]
    public void Rsa4096_EncodePublicKey_PrefixesWithRsaVarint()
    {
        var result = Multicodec.EncodePublicKey("RSA-4096", TestRsaKey);

        result.Should().NotBeNull();
        // Unsigned varint of 0x1205 is 0x85 0x24.
        result![0].Should().Be(0x85, because: "RSA multicodec varint first byte (0x1205 varint)");
        result[1].Should().Be(0x24, because: "RSA multicodec varint second byte");
        result.Length.Should().Be(2 + TestRsaKey.Length);
    }

    [Fact]
    public void Ed25519_ToMultibasePublicKey_StartsWithZPrefix()
    {
        var result = Multicodec.ToMultibasePublicKey("ED25519", TestEd25519Key);

        result.Should().NotBeNull();
        result!.Should().StartWith("z", because: "W3C base58btc multibase prefix");
        result.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void NistP256_ToMultibasePublicKey_RoundTrip_ThroughBase58Decode()
    {
        var multibase = Multicodec.ToMultibasePublicKey("NIST-P256", TestP256Key);

        multibase.Should().NotBeNull();
        multibase![0].Should().Be('z');

        var base58Portion = multibase[1..];
        var decoded = Base58.Bitcoin.Decode(base58Portion);
        decoded[0].Should().Be(0x80);
        decoded[1].Should().Be(0x24);
        decoded.Skip(2).Should().Equal(TestP256Key);
    }

    [Fact]
    public void Rsa4096_ToMultibasePublicKey_RoundTrip_ThroughBase58Decode()
    {
        var multibase = Multicodec.ToMultibasePublicKey("RSA-4096", TestRsaKey);

        multibase.Should().NotBeNull();
        multibase![0].Should().Be('z');

        var base58Portion = multibase[1..];
        var decoded = Base58.Bitcoin.Decode(base58Portion);
        decoded[0].Should().Be(0x85);
        decoded[1].Should().Be(0x24);
        decoded.Skip(2).Should().Equal(TestRsaKey);
    }

    [Theory]
    [InlineData("ML-DSA-65")]
    [InlineData("SLH-DSA-128s")]
    [InlineData("SLH-DSA-192s")]
    [InlineData("ML-KEM-768")]
    [InlineData("unknown-algorithm")]
    public void UnsupportedAlgorithm_EncodePublicKey_ReturnsNull(string algorithm)
    {
        var result = Multicodec.EncodePublicKey(algorithm, new byte[32]);

        result.Should().BeNull(
            because: "algorithms without an assigned multicodec identifier must return null rather than emit malformed output");
    }

    [Theory]
    [InlineData("ML-DSA-65")]
    [InlineData("SLH-DSA-128s")]
    public void UnsupportedAlgorithm_ToMultibasePublicKey_ReturnsNull(string algorithm)
    {
        var result = Multicodec.ToMultibasePublicKey(algorithm, new byte[32]);

        result.Should().BeNull();
    }

    [Fact]
    public void EncodePublicKey_NullKey_Throws()
    {
        var act = () => Multicodec.EncodePublicKey("ED25519", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EncodePublicKey_NullAlgorithmName_Throws()
    {
        var act = () => Multicodec.EncodePublicKey(null!, new byte[32]);

        act.Should().Throw<ArgumentNullException>();
    }

    // --- DecodePublicKeyBytes helper tests ---

    [Fact]
    public void DecodePublicKeyBytes_Base64_ReturnsBytes()
    {
        var expected = new byte[] { 0x01, 0x02, 0x03 };
        var base64 = Convert.ToBase64String(expected);

        var result = Multicodec.DecodePublicKeyBytes(base64);

        result.Should().NotBeNull();
        result!.Should().Equal(expected);
    }

    [Fact]
    public void DecodePublicKeyBytes_Hex_FallsBack_WhenNotValidBase64()
    {
        // 5-byte hex string = 10 characters. Length 10 is NOT a valid base64 length
        // (must be a multiple of 4), so base64 decode throws FormatException and the
        // helper falls through to hex decode. This is the intended semantics: base64
        // is preferred, hex is a fallback for wallets whose PublicKey is stored in hex.
        var expected = new byte[] { 0xaa, 0xbb, 0xcc, 0xdd, 0xee };
        var hex = Convert.ToHexString(expected); // "AABBCCDDEE"

        var result = Multicodec.DecodePublicKeyBytes(hex);

        result.Should().NotBeNull();
        result!.Should().Equal(expected);
    }

    [Fact]
    public void DecodePublicKeyBytes_AmbiguousInput_PrefersBase64()
    {
        // A string that is valid as both base64 AND hex will decode as base64 because
        // base64 is tried first. Documented behaviour — production wallets store
        // PublicKey as base64 per WalletEndpoints.cs; hex is only a legacy fallback
        // for strings that fail base64 parsing.
        var ambiguous = "AABBCCDD"; // 8 chars — valid base64 length
        var base64Decoded = Convert.FromBase64String(ambiguous);

        var result = Multicodec.DecodePublicKeyBytes(ambiguous);

        result.Should().Equal(base64Decoded,
            because: "base64 is preferred when both interpretations are valid");
    }

    [Fact]
    public void DecodePublicKeyBytes_Invalid_ReturnsNull()
    {
        var result = Multicodec.DecodePublicKeyBytes("!!!not-base64-or-hex!!!");

        result.Should().BeNull();
    }

    [Fact]
    public void DecodePublicKeyBytes_Null_ReturnsNull()
    {
        Multicodec.DecodePublicKeyBytes(null).Should().BeNull();
        Multicodec.DecodePublicKeyBytes("").Should().BeNull();
        Multicodec.DecodePublicKeyBytes("   ").Should().BeNull();
    }
}
