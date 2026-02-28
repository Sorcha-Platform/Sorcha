// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using FluentAssertions;
using Sorcha.Wallet.Core.Constants;
using Xunit;

namespace Sorcha.Wallet.Core.Tests.Constants;

public class SorchaDerivationPathsTests
{
    [Fact]
    public void ResolvePath_RegisterAttestation_ReturnsCorrectBip44Path()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:register-attestation");

        result.Should().Be("m/44'/0'/0'/0/100");
    }

    [Fact]
    public void ResolvePath_RegisterControl_ReturnsCorrectBip44Path()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:register-control");

        result.Should().Be("m/44'/0'/0'/0/101");
    }

    [Fact]
    public void ResolvePath_DocketSigning_ReturnsCorrectBip44Path()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:docket-signing");

        result.Should().Be("m/44'/0'/0'/0/102");
    }

    [Fact]
    public void ResolvePath_AlreadyBip44Path_ReturnsAsIs()
    {
        var bip44Path = "m/44'/0'/0'/0/0";

        var result = SorchaDerivationPaths.ResolvePath(bip44Path);

        result.Should().Be(bip44Path);
    }

    [Fact]
    public void ResolvePath_UnknownSystemPath_ThrowsArgumentException()
    {
        var act = () => SorchaDerivationPaths.ResolvePath("sorcha:unknown-path");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("systemPath");
    }

    [Fact]
    public void ResolvePath_EmptyPath_ThrowsArgumentException()
    {
        var act = () => SorchaDerivationPaths.ResolvePath("");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("systemPath");
    }

    [Fact]
    public void IsSystemPath_SorchaPrefix_ReturnsTrue()
    {
        SorchaDerivationPaths.IsSystemPath("sorcha:register-attestation").Should().BeTrue();
        SorchaDerivationPaths.IsSystemPath("sorcha:register-control").Should().BeTrue();
        SorchaDerivationPaths.IsSystemPath("sorcha:docket-signing").Should().BeTrue();
    }

    [Fact]
    public void IsSystemPath_Bip44Path_ReturnsFalse()
    {
        SorchaDerivationPaths.IsSystemPath("m/44'/0'/0'/0/0").Should().BeFalse();
    }

    [Fact]
    public void IsSystemPath_EmptyOrNull_ReturnsFalse()
    {
        SorchaDerivationPaths.IsSystemPath("").Should().BeFalse();
        SorchaDerivationPaths.IsSystemPath(null!).Should().BeFalse();
    }
}
