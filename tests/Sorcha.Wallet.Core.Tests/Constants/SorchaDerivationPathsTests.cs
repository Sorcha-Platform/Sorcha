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
    public void ResolvePath_PersonaVault_ReturnsCorrectBip44Path()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:persona-vault");

        result.Should().Be("m/44'/0'/0'/0/104");
    }

    [Fact]
    public void PersonaVaultPath_IsDistinctFromOtherPurposes()
    {
        // Guard against accidental collision with existing derivation purposes.
        SorchaDerivationPaths.PersonaVaultPath.Should().NotBe(SorchaDerivationPaths.RegisterAttestationPath);
        SorchaDerivationPaths.PersonaVaultPath.Should().NotBe(SorchaDerivationPaths.RegisterControlPath);
        SorchaDerivationPaths.PersonaVaultPath.Should().NotBe(SorchaDerivationPaths.DocketSigningPath);
        SorchaDerivationPaths.PersonaVaultPath.Should().NotBe(SorchaDerivationPaths.BlueprintPublishPath);
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

    [Fact]
    public void ResolvePath_CitizenHolder_ReturnsSlot108()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:citizen-holder");

        result.Should().Be("m/44'/0'/0'/0/108");
    }

    [Fact]
    public void ResolvePath_CitizenStatusSigning_ReturnsSlot109()
    {
        var result = SorchaDerivationPaths.ResolvePath("sorcha:citizen-status-signing");

        result.Should().Be("m/44'/0'/0'/0/109");
    }

    [Fact]
    public void CitizenHolderPath_IsDistinctFromAllOtherSlots()
    {
        // Guard against accidental collision; citizen-holder must not equal any prior slot.
        var citizenHolder = SorchaDerivationPaths.CitizenHolderPath;

        citizenHolder.Should().NotBe(SorchaDerivationPaths.RegisterAttestationPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.RegisterControlPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.DocketSigningPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.BlueprintPublishPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.PersonaVaultPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.CredentialHolderBindingPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.HaipIssuerSigningPath);
        citizenHolder.Should().NotBe(SorchaDerivationPaths.TenantCaSigningPath);
    }

    [Fact]
    public void CitizenStatusSigningPath_IsDistinctFromAllOtherSlots()
    {
        var statusSigning = SorchaDerivationPaths.CitizenStatusSigningPath;

        statusSigning.Should().NotBe(SorchaDerivationPaths.RegisterAttestationPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.RegisterControlPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.DocketSigningPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.BlueprintPublishPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.PersonaVaultPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.CredentialHolderBindingPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.HaipIssuerSigningPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.TenantCaSigningPath);
        statusSigning.Should().NotBe(SorchaDerivationPaths.CitizenHolderPath);
    }

    [Fact]
    public void CitizenHolder_IsRecognisedAsSystemPath()
    {
        SorchaDerivationPaths.IsSystemPath(SorchaDerivationPaths.CitizenHolder).Should().BeTrue();
        SorchaDerivationPaths.IsSystemPath(SorchaDerivationPaths.CitizenStatusSigning).Should().BeTrue();
    }
}
