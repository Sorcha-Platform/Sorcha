// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Feature 093 US3 — tests for the Multicodec helper that produces W3C-valid
// publicKeyMultibase values via "z" + Base58btc(multicodec || rawKeyBytes).

using FluentAssertions;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;
using Xunit;

namespace Sorcha.Cryptography.Tests.Utilities;

public class MulticodecTests
{
    // Fixed deterministic test keys so assertions are reproducible.
    private static readonly byte[] TestEd25519Key = new byte[]
    {
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };

    private static readonly byte[] TestP256Key = new byte[]
    {
        0x02, // SEC1 compressed form tag
        0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
        0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f, 0x10,
        0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18,
        0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1e, 0x1f, 0x20
    };

    private static readonly byte[] TestRsaKey = new byte[64]; // Placeholder RSA SPKI

    [Fact]
    public void Ed25519_EncodePublicKey_PrefixesWithEd25519Varint()
    {
        var result = Multicodec.EncodePublicKey(WalletNetworks.ED25519, TestEd25519Key);

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
        var result = Multicodec.EncodePublicKey(WalletNetworks.NISTP256, TestP256Key);

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
        var result = Multicodec.EncodePublicKey(WalletNetworks.RSA4096, TestRsaKey);

        result.Should().NotBeNull();
        // Unsigned varint of 0x1205 is 0x85 0x24.
        result![0].Should().Be(0x85, because: "RSA multicodec varint first byte (0x1205 varint)");
        result[1].Should().Be(0x24, because: "RSA multicodec varint second byte");
        result.Length.Should().Be(2 + TestRsaKey.Length);
    }

    [Fact]
    public void Ed25519_ToMultibasePublicKey_StartsWithZPrefix()
    {
        var result = Multicodec.ToMultibasePublicKey(WalletNetworks.ED25519, TestEd25519Key);

        result.Should().NotBeNull();
        result!.Should().StartWith("z", because: "W3C base58btc multibase prefix");
        result.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void NistP256_ToMultibasePublicKey_RoundTrip_ThroughBase58Decode()
    {
        var multibase = Multicodec.ToMultibasePublicKey(WalletNetworks.NISTP256, TestP256Key);

        multibase.Should().NotBeNull();
        multibase![0].Should().Be('z');

        // Decode and verify the varint prefix + raw key round-trips.
        var base58Portion = multibase[1..];
        var decoded = Base58.Decode(base58Portion);
        decoded[0].Should().Be(0x80);
        decoded[1].Should().Be(0x24);
        decoded.Skip(2).Should().Equal(TestP256Key);
    }

    [Fact]
    public void Rsa4096_ToMultibasePublicKey_RoundTrip_ThroughBase58Decode()
    {
        var multibase = Multicodec.ToMultibasePublicKey(WalletNetworks.RSA4096, TestRsaKey);

        multibase.Should().NotBeNull();
        multibase![0].Should().Be('z');

        var base58Portion = multibase[1..];
        var decoded = Base58.Decode(base58Portion);
        decoded[0].Should().Be(0x85);
        decoded[1].Should().Be(0x24);
        decoded.Skip(2).Should().Equal(TestRsaKey);
    }

    [Theory]
    [InlineData(WalletNetworks.ML_DSA_65)]
    [InlineData(WalletNetworks.SLH_DSA_128s)]
    [InlineData(WalletNetworks.SLH_DSA_192s)]
    [InlineData(WalletNetworks.ML_KEM_768)]
    public void UnsupportedAlgorithm_EncodePublicKey_ReturnsNull(WalletNetworks algorithm)
    {
        var result = Multicodec.EncodePublicKey(algorithm, new byte[32]);

        result.Should().BeNull(
            because: "PQC algorithms have no assigned multicodec identifier yet — the helper must return null rather than emit malformed output");
    }

    [Theory]
    [InlineData(WalletNetworks.ML_DSA_65)]
    [InlineData(WalletNetworks.SLH_DSA_128s)]
    public void UnsupportedAlgorithm_ToMultibasePublicKey_ReturnsNull(WalletNetworks algorithm)
    {
        var result = Multicodec.ToMultibasePublicKey(algorithm, new byte[32]);

        result.Should().BeNull();
    }

    [Fact]
    public void EncodePublicKey_NullKey_Throws()
    {
        var act = () => Multicodec.EncodePublicKey(WalletNetworks.ED25519, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
