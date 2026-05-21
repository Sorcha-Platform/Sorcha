// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Auth;

/// <summary>
/// Tests for <see cref="SorchaAudiences"/> — the single source of truth for tier
/// audiences (spec 136). Issuance and validation both derive from this, so its
/// derivation must be exact and stable.
/// </summary>
public sealed class SorchaAudiencesTests
{
    [Fact]
    public void DefaultInstallation_NullName_UsesSorchaPrefix()
    {
        var aud = new SorchaAudiences(null);

        aud.InstallationName.Should().Be("sorcha");
        aud.For(Tier.Consumer).Should().Be("sorcha:consumer");
        aud.For(Tier.Platform).Should().Be("sorcha:platform");
        aud.For(Tier.Service).Should().Be("sorcha:service");
        aud.For(Tier.EnrolSession).Should().Be("sorcha:enrol-session");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_NormalisesToDefault(string name)
    {
        new SorchaAudiences(name).InstallationName.Should().Be("sorcha");
    }

    [Fact]
    public void OverriddenName_NamespacesAllTiers()
    {
        var aud = new SorchaAudiences("acme");

        aud.For(Tier.Consumer).Should().Be("acme:consumer");
        aud.For(Tier.Platform).Should().Be("acme:platform");
        aud.For(Tier.Service).Should().Be("acme:service");
        aud.For(Tier.EnrolSession).Should().Be("acme:enrol-session");
    }

    [Theory]
    [InlineData(" N1 ", "n1")]
    [InlineData("ACME", "acme")]
    public void Name_IsTrimmedAndLowercased(string input, string expected)
    {
        new SorchaAudiences(input).InstallationName.Should().Be(expected);
    }

    [Fact]
    public void All_ContainsExactlyTheFourTierAudiences()
    {
        var aud = new SorchaAudiences("n1");

        aud.All.Should().BeEquivalentTo(new[]
        {
            "n1:consumer", "n1:platform", "n1:service", "n1:enrol-session",
        });
    }

    [Fact]
    public void TierFor_RoundTripsEveryTier()
    {
        var aud = new SorchaAudiences("n1");

        aud.TierFor("n1:consumer").Should().Be(Tier.Consumer);
        aud.TierFor("n1:platform").Should().Be(Tier.Platform);
        aud.TierFor("n1:service").Should().Be(Tier.Service);
        aud.TierFor("n1:enrol-session").Should().Be(Tier.EnrolSession);
    }

    [Theory]
    [InlineData("sorcha:consumer")]  // different installation
    [InlineData("n1:admin")]          // unknown suffix
    [InlineData("")]
    [InlineData(null)]
    public void TierFor_UnknownAudience_ReturnsNull(string? audience)
    {
        new SorchaAudiences("n1").TierFor(audience).Should().BeNull();
    }
}
