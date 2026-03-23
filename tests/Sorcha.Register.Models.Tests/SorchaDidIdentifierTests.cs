// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;

namespace Sorcha.Register.Models.Tests;

public class SorchaDidIdentifierTests
{
    // --- Wallet DID Parsing ---

    [Fact]
    public void Parse_ValidWalletDid_ReturnsWalletType()
    {
        var did = SorchaDidIdentifier.Parse("did:sorcha:w:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");

        did.Type.Should().Be(SorchaDidType.Wallet);
        did.Locator.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        did.TransactionId.Should().BeNull();
    }

    [Fact]
    public void Parse_ShortWalletAddress_ReturnsWalletType()
    {
        var did = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");

        did.Type.Should().Be(SorchaDidType.Wallet);
        did.Locator.Should().Be("abc123");
    }

    [Fact]
    public void TryParse_ValidWalletDid_ReturnsTrue()
    {
        var success = SorchaDidIdentifier.TryParse("did:sorcha:w:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy", out var result);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Type.Should().Be(SorchaDidType.Wallet);
    }

    [Fact]
    public void TryParse_EmptyWalletAddress_ReturnsFalse()
    {
        SorchaDidIdentifier.TryParse("did:sorcha:w:", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("did:sorcha:w:address with spaces")]
    [InlineData("did:sorcha:w:addr+ess")]
    [InlineData("did:sorcha:w:addr=ess")]
    [InlineData("did:sorcha:w:0address")]  // Base58 doesn't include 0
    public void TryParse_InvalidWalletAddress_ReturnsFalse(string did)
    {
        SorchaDidIdentifier.TryParse(did, out _).Should().BeFalse();
    }

    // --- Register DID Parsing ---

    [Fact]
    public void Parse_ValidRegisterDid_ReturnsRegisterType()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var did = SorchaDidIdentifier.Parse($"did:sorcha:r:{registerId}:t:{txId}");

        did.Type.Should().Be(SorchaDidType.Register);
        did.Locator.Should().Be(registerId);
        did.TransactionId.Should().Be(txId);
    }

    [Fact]
    public void TryParse_RegisterDid_ShortRegisterId_ReturnsFalse()
    {
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        SorchaDidIdentifier.TryParse($"did:sorcha:r:short:t:{txId}", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_RegisterDid_ShortTxId_ReturnsFalse()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        SorchaDidIdentifier.TryParse($"did:sorcha:r:{registerId}:t:short", out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_RegisterDid_UppercaseHex_ReturnsFalse()
    {
        var registerId = "A1B2C3D4E5F6A1B2C3D4E5F6A1B2C3D4";
        var txId = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
        SorchaDidIdentifier.TryParse($"did:sorcha:r:{registerId}:t:{txId}", out _).Should().BeFalse();
    }

    // --- Invalid DIDs ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-did")]
    [InlineData("did:other:w:address")]
    [InlineData("did:sorcha:x:address")]  // Unknown type
    [InlineData("did:sorcha:")]
    public void TryParse_InvalidDid_ReturnsFalse(string? did)
    {
        SorchaDidIdentifier.TryParse(did, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_InvalidDid_ThrowsFormatException()
    {
        var act = () => SorchaDidIdentifier.Parse("not-a-did");
        act.Should().Throw<FormatException>();
    }

    // --- Factory Methods ---

    [Fact]
    public void FromWallet_ValidAddress_CreatesWalletDid()
    {
        var did = SorchaDidIdentifier.FromWallet("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");

        did.Type.Should().Be(SorchaDidType.Wallet);
        did.Locator.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        did.ToString().Should().Be("did:sorcha:w:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
    }

    [Fact]
    public void FromWallet_EmptyAddress_ThrowsArgumentException()
    {
        var act = () => SorchaDidIdentifier.FromWallet("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromRegister_ValidIds_CreatesRegisterDid()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var did = SorchaDidIdentifier.FromRegister(registerId, txId);

        did.Type.Should().Be(SorchaDidType.Register);
        did.ToString().Should().Be($"did:sorcha:r:{registerId}:t:{txId}");
    }

    [Fact]
    public void FromRegister_EmptyRegisterId_ThrowsArgumentException()
    {
        var act = () => SorchaDidIdentifier.FromRegister("", "txid");
        act.Should().Throw<ArgumentException>();
    }

    // --- ToString Roundtrip ---

    [Fact]
    public void ToString_WalletDid_RoundTrips()
    {
        var original = "did:sorcha:w:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        var did = SorchaDidIdentifier.Parse(original);
        did.ToString().Should().Be(original);
    }

    [Fact]
    public void ToString_RegisterDid_RoundTrips()
    {
        var registerId = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4";
        var txId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var original = $"did:sorcha:r:{registerId}:t:{txId}";
        var did = SorchaDidIdentifier.Parse(original);
        did.ToString().Should().Be(original);
    }

    // --- Equality ---

    [Fact]
    public void Equals_SameWalletDid_ReturnsTrue()
    {
        var did1 = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");
        var did2 = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");

        did1.Should().Be(did2);
        (did1 == did2).Should().BeTrue();
        (did1 != did2).Should().BeFalse();
    }

    [Fact]
    public void Equals_DifferentWalletDid_ReturnsFalse()
    {
        var did1 = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");
        var did2 = SorchaDidIdentifier.Parse("did:sorcha:w:xyz789");

        did1.Should().NotBe(did2);
    }

    [Fact]
    public void Equals_WalletVsRegisterDid_ReturnsFalse()
    {
        var wallet = SorchaDidIdentifier.FromWallet("abc123");
        var register = SorchaDidIdentifier.FromRegister(
            "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4",
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");

        wallet.Should().NotBe(register);
    }

    [Fact]
    public void GetHashCode_SameDid_ReturnsSameHash()
    {
        var did1 = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");
        var did2 = SorchaDidIdentifier.Parse("did:sorcha:w:abc123");

        did1.GetHashCode().Should().Be(did2.GetHashCode());
    }

    // --- Organization DID Parsing ---

    [Fact]
    public void Parse_ValidOrgDid_ReturnsOrganizationType()
    {
        var did = SorchaDidIdentifier.Parse("did:sorcha:org:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");

        did.Type.Should().Be(SorchaDidType.Organization);
        did.Locator.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        did.TransactionId.Should().BeNull();
    }

    [Fact]
    public void TryParse_ValidOrgDid_ReturnsTrue()
    {
        var success = SorchaDidIdentifier.TryParse("did:sorcha:org:3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy", out var result);

        success.Should().BeTrue();
        result.Should().NotBeNull();
        result!.Type.Should().Be(SorchaDidType.Organization);
    }

    [Fact]
    public void TryParse_EmptyOrgAddress_ReturnsFalse()
    {
        SorchaDidIdentifier.TryParse("did:sorcha:org:", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("did:sorcha:org:address with spaces")]
    [InlineData("did:sorcha:org:addr+ess")]
    [InlineData("did:sorcha:org:0address")]  // Base58 doesn't include 0
    public void TryParse_InvalidOrgAddress_ReturnsFalse(string did)
    {
        SorchaDidIdentifier.TryParse(did, out _).Should().BeFalse();
    }

    // --- Organization Factory ---

    [Fact]
    public void FromOrganization_ValidAddress_CreatesOrgDid()
    {
        var did = SorchaDidIdentifier.FromOrganization("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");

        did.Type.Should().Be(SorchaDidType.Organization);
        did.Locator.Should().Be("1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
        did.ToString().Should().Be("did:sorcha:org:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa");
    }

    [Fact]
    public void FromOrganization_EmptyAddress_ThrowsArgumentException()
    {
        var act = () => SorchaDidIdentifier.FromOrganization("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_OrgDid_RoundTrips()
    {
        var original = "did:sorcha:org:1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa";
        var did = SorchaDidIdentifier.Parse(original);
        did.ToString().Should().Be(original);
    }

    // --- Org vs Wallet Equality ---

    [Fact]
    public void Equals_OrgVsWalletDid_SameAddress_ReturnsFalse()
    {
        var org = SorchaDidIdentifier.FromOrganization("abc123");
        var wallet = SorchaDidIdentifier.FromWallet("abc123");

        org.Should().NotBe(wallet);
    }

    [Fact]
    public void Equals_SameOrgDid_ReturnsTrue()
    {
        var did1 = SorchaDidIdentifier.Parse("did:sorcha:org:abc123");
        var did2 = SorchaDidIdentifier.Parse("did:sorcha:org:abc123");

        did1.Should().Be(did2);
        (did1 == did2).Should().BeTrue();
    }

    // --- IsValid ---

    [Theory]
    [InlineData("did:sorcha:w:abc123", true)]
    [InlineData("did:sorcha:org:abc123", true)]
    [InlineData("did:sorcha:r:a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4:t:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("not-a-did", false)]
    [InlineData(null, false)]
    public void IsValid_ReturnsExpectedResult(string? did, bool expected)
    {
        SorchaDidIdentifier.IsValid(did).Should().Be(expected);
    }
}
