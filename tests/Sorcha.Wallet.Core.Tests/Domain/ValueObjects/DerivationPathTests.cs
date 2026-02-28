// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Xunit;

namespace Sorcha.Wallet.Core.Tests.Domain.ValueObjects;

public class DerivationPathTests
{
    [Fact]
    public void Constructor_ValidPath_StoresPath()
    {
        var path = new DerivationPath("m/44'/0'/0'/0/0");

        path.Path.Should().Be("44'/0'/0'/0/0");
    }

    [Fact]
    public void Constructor_EmptyPath_ThrowsArgumentException()
    {
        var act = () => new DerivationPath("");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("path");
    }

    [Fact]
    public void Constructor_NullPath_ThrowsArgumentException()
    {
        var act = () => new DerivationPath(null!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("path");
    }

    [Fact]
    public void Constructor_InvalidPath_ThrowsArgumentException()
    {
        var act = () => new DerivationPath("not/a/valid/path/format");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("path");
    }

    [Fact]
    public void CreateBip44_DefaultParameters_ReturnsCorrectPath()
    {
        var path = DerivationPath.CreateBip44();

        path.Path.Should().Be("44'/0'/0'/0/0");
    }

    [Fact]
    public void CreateBip44_CustomParameters_ReturnsCorrectPath()
    {
        var path = DerivationPath.CreateBip44(coinType: 60, account: 1, change: 1, addressIndex: 5);

        path.Path.Should().Be("44'/60'/1'/1/5");
    }

    [Fact]
    public void CreateBip44_BitcoinCoinType_ReturnsCorrectPath()
    {
        var path = DerivationPath.CreateBip44(coinType: 0, account: 0, change: 0, addressIndex: 0);

        path.Path.Should().Be("44'/0'/0'/0/0");
    }

    [Fact]
    public void CreatePqcBip44_DefaultParameters_UsesCoinType1()
    {
        var path = DerivationPath.CreatePqcBip44();

        path.Path.Should().Be("44'/1'/0'/0/0");
    }

    [Fact]
    public void CreatePqcBip44_CustomParameters_UsesCoinType1WithCustomValues()
    {
        var path = DerivationPath.CreatePqcBip44(account: 2, change: 1, addressIndex: 3);

        path.Path.Should().Be("44'/1'/2'/1/3");
    }

    [Fact]
    public void TryParseBip44_ValidPath_ReturnsTrueWithComponents()
    {
        var success = DerivationPath.TryParseBip44(
            "m/44'/60'/1'/0/5",
            out uint coinType,
            out uint account,
            out uint change,
            out uint addressIndex);

        success.Should().BeTrue();
        coinType.Should().Be(60);
        account.Should().Be(1);
        change.Should().Be(0);
        addressIndex.Should().Be(5);
    }

    [Fact]
    public void TryParseBip44_DefaultBip44Path_ParsesCorrectly()
    {
        var success = DerivationPath.TryParseBip44(
            "m/44'/0'/0'/0/0",
            out uint coinType,
            out uint account,
            out uint change,
            out uint addressIndex);

        success.Should().BeTrue();
        coinType.Should().Be(0);
        account.Should().Be(0);
        change.Should().Be(0);
        addressIndex.Should().Be(0);
    }

    [Fact]
    public void TryParseBip44_WrongPurpose_ReturnsFalse()
    {
        var success = DerivationPath.TryParseBip44(
            "m/49'/0'/0'/0/0",
            out _, out _, out _, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseBip44_TooFewComponents_ReturnsFalse()
    {
        var success = DerivationPath.TryParseBip44(
            "m/44'/0'/0'",
            out _, out _, out _, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseBip44_InvalidString_ReturnsFalse()
    {
        var success = DerivationPath.TryParseBip44(
            "not-a-path",
            out _, out _, out _, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void TryParseBip44_EmptyString_ReturnsFalse()
    {
        var success = DerivationPath.TryParseBip44(
            "",
            out _, out _, out _, out _);

        success.Should().BeFalse();
    }

    [Fact]
    public void ToString_ReturnsPathString()
    {
        var path = DerivationPath.CreateBip44(coinType: 0, account: 0, change: 0, addressIndex: 0);

        path.ToString().Should().Be("44'/0'/0'/0/0");
    }

    [Fact]
    public void CreateBip44_RoundTrip_TryParseBip44RecoversComponents()
    {
        var original = DerivationPath.CreateBip44(coinType: 42, account: 3, change: 1, addressIndex: 7);

        var success = DerivationPath.TryParseBip44(
            $"m/{original.Path}",
            out uint coinType,
            out uint account,
            out uint change,
            out uint addressIndex);

        success.Should().BeTrue();
        coinType.Should().Be(42);
        account.Should().Be(3);
        change.Should().Be(1);
        addressIndex.Should().Be(7);
    }
}
