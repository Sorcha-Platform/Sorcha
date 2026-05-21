// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="TierResolver"/> — the pure tier-selection rule (spec 136, FR-008/FR-009).
/// </summary>
public sealed class TierResolverTests
{
    [Fact]
    public void Resolve_NoRequestedTier_DefaultsToConsumer()
    {
        var result = TierResolver.Resolve(requestedTier: null, rolesInActiveContext: [UserRole.Consumer]);

        result.Allowed.Should().BeTrue();
        result.Tier.Should().Be(Tier.Consumer);
    }

    [Fact]
    public void Resolve_ConsumerRequested_AnyHuman_Allowed()
    {
        var result = TierResolver.Resolve(Tier.Consumer, rolesInActiveContext: []);

        result.Allowed.Should().BeTrue();
        result.Tier.Should().Be(Tier.Consumer);
    }

    [Theory]
    [InlineData(UserRole.Administrator)]
    [InlineData(UserRole.Designer)]
    [InlineData(UserRole.Auditor)]
    [InlineData(UserRole.SystemAdmin)]
    public void Resolve_PlatformRequested_WithPlatformRole_Allowed(UserRole role)
    {
        var result = TierResolver.Resolve(Tier.Platform, rolesInActiveContext: [role]);

        result.Allowed.Should().BeTrue();
        result.Tier.Should().Be(Tier.Platform);
    }

    [Fact]
    public void Resolve_PlatformRequested_ByConsumerOnly_Rejected_NotDowngraded()
    {
        var result = TierResolver.Resolve(Tier.Platform, rolesInActiveContext: [UserRole.Consumer]);

        result.Allowed.Should().BeFalse("over-request must be refused, never silently downgraded");
        result.Tier.Should().Be(Tier.Platform);
        result.Reason.Should().Be(TierRejectionReason.NotEntitled);
    }

    [Fact]
    public void Resolve_PlatformRequested_ByRolelessUser_Rejected()
    {
        TierResolver.Resolve(Tier.Platform, rolesInActiveContext: null)
            .Allowed.Should().BeFalse();
    }

    [Theory]
    [InlineData(Tier.Service)]
    [InlineData(Tier.EnrolSession)]
    public void Resolve_MachineTiers_NeverSelectableByHumanPath(Tier tier)
    {
        // Even a SystemAdmin cannot mint a service/enrol-session token via the human path.
        var result = TierResolver.Resolve(tier, rolesInActiveContext: [UserRole.SystemAdmin]);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be(TierRejectionReason.NotEntitled);
    }

    [Fact]
    public void EntitledTiers_DualRole_IncludesConsumerAndPlatform()
    {
        var entitled = TierResolver.EntitledTiers([UserRole.Consumer, UserRole.Administrator]);

        entitled.Should().Contain(Tier.Consumer).And.Contain(Tier.Platform);
        entitled.Should().NotContain(Tier.Service);
    }

    [Fact]
    public void EntitledTiers_NoRoles_ConsumerOnly()
    {
        TierResolver.EntitledTiers(null).Should().ContainSingle().Which.Should().Be(Tier.Consumer);
    }

    // --- ResolvePreference: tier follows the person (downgrade derived / refuse explicit) ---

    [Fact]
    public void ResolvePreference_PlatformPreference_ConsumerOnly_NotExplicit_DowngradesToConsumer()
    {
        var result = TierResolver.ResolvePreference(Tier.Platform, isExplicit: false, [UserRole.Consumer]);

        result.Allowed.Should().BeTrue("a destination-derived preference downgrades rather than locks out");
        result.Tier.Should().Be(Tier.Consumer);
    }

    [Fact]
    public void ResolvePreference_PlatformPreference_ConsumerOnly_Explicit_Refused()
    {
        var result = TierResolver.ResolvePreference(Tier.Platform, isExplicit: true, [UserRole.Consumer]);

        result.Allowed.Should().BeFalse("an explicit over-request is refused, not downgraded (FR-008)");
        result.Reason.Should().Be(TierRejectionReason.NotEntitled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvePreference_PlatformPreference_AdminUser_MintsPlatform(bool isExplicit)
    {
        var result = TierResolver.ResolvePreference(Tier.Platform, isExplicit, [UserRole.Administrator]);

        result.Allowed.Should().BeTrue();
        result.Tier.Should().Be(Tier.Platform);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolvePreference_ConsumerPreference_AlwaysConsumer(bool isExplicit)
    {
        // Everyone is entitled to Consumer, so a consumer preference is always granted as-is.
        var result = TierResolver.ResolvePreference(Tier.Consumer, isExplicit, rolesInActiveContext: null);

        result.Allowed.Should().BeTrue();
        result.Tier.Should().Be(Tier.Consumer);
    }
}
