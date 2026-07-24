// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Reflection;

using FluentAssertions;
using Sorcha.Wallet.Contracts.Constants;
using Xunit;

namespace Sorcha.Wallet.Contracts.Tests.Constants;

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

    // ---------------------------------------------------------------------------------------
    // Exhaustive invariants.
    //
    // These replace the previous hand-maintained pairwise "NotBe" blocks, which only compared the
    // constants somebody remembered to list — so an eleventh context could collide with, or be
    // omitted from, the resolver and every test would still pass. Reflection makes the guarantees
    // total: a new constant is covered the moment it is declared.
    // ---------------------------------------------------------------------------------------

    /// <summary>Every declared "sorcha:*" context, discovered by reflection.</summary>
    public static TheoryData<string, string> AllContexts()
    {
        var data = new TheoryData<string, string>();
        foreach (var (name, value) in ContextFields())
        {
            data.Add(name, value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllContexts))]
    public void EveryContext_ResolvesToABip44Path(string name, string context)
    {
        // Guards the "added a constant but forgot the ResolvePath switch arm" drift: the constant
        // would exist, compile, and be usable at call sites, then throw at runtime on first use.
        var act = () => SorchaDerivationPaths.ResolvePath(context);

        act.Should().NotThrow($"{name} is declared but has no ResolvePath mapping");
        SorchaDerivationPaths.ResolvePath(context).Should().StartWith("m/44'/0'/0'/0/");
    }

    [Theory]
    [MemberData(nameof(AllContexts))]
    public void EveryContext_IsRecognisedAsASystemPath(string name, string context)
    {
        SorchaDerivationPaths.IsSystemPath(context)
            .Should().BeTrue($"{name} must be recognised as a Sorcha system path");
    }

    [Fact]
    public void EveryContext_ResolvesToADistinctBip44Slot()
    {
        // Two contexts sharing a slot would silently derive the SAME key — the failure this whole
        // constants class exists to prevent, and one no compiler or runtime check would catch.
        var bySlot = ContextFields()
            .GroupBy(f => SorchaDerivationPaths.ResolvePath(f.Value))
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} <- {string.Join(", ", g.Select(f => f.Name))}")
            .ToList();

        bySlot.Should().BeEmpty("each derivation context must own a distinct BIP44 slot");
    }

    [Fact]
    public void EveryContext_HasAMatchingPathConstant()
    {
        // The class pairs each context (e.g. DocketSigning) with its BIP44 literal
        // (DocketSigningPath). A context without its pair is a half-added constant.
        var declared = typeof(SorchaDerivationPaths)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => f.Name)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ContextFields()
            .Select(f => f.Name + "Path")
            .Where(expected => !declared.Contains(expected))
            .ToList();

        missing.Should().BeEmpty("every context constant needs its matching *Path constant");
    }

    [Fact]
    public void ContextDiscovery_IsNotVacuous()
    {
        // Without this, a reflection bug that discovered nothing would make every exhaustive
        // assertion above pass trivially — a green suite proving the opposite of what it claims.
        var contexts = ContextFields();

        contexts.Should().HaveCountGreaterThanOrEqualTo(10);
        contexts.Select(c => c.Value).Should().Contain(
        [
            "sorcha:register-attestation",
            "sorcha:register-control",
            "sorcha:docket-signing",
            "sorcha:citizen-holder",
        ]);
    }

    private static List<(string Name, string Value)> ContextFields() =>
        typeof(SorchaDerivationPaths)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, Value: (string)f.GetValue(null)!))
            // SystemPrefix is the bare "sorcha:" marker, not a context.
            .Where(f => f.Value.StartsWith("sorcha:", StringComparison.Ordinal)
                        && f.Value.Length > "sorcha:".Length)
            .ToList();
}
