// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.ServiceDefaults.Tests.Auth;

/// <summary>
/// Tests for the tier-audience authorization predicate (spec 136). The predicate
/// underpins the RequireConsumerAudience / RequirePlatformAudience policies.
/// </summary>
public sealed class TierPolicyTests
{
    private static readonly SorchaAudiences N1 = new("n1");

    private static ClaimsPrincipal WithAudiences(params string[] auds)
    {
        var claims = auds.Select(a => new Claim("aud", a));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    [Fact]
    public void HasTierAudience_MatchingConsumer_True()
    {
        var user = WithAudiences("n1:consumer");

        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Consumer).Should().BeTrue();
        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Platform).Should().BeFalse();
    }

    [Fact]
    public void HasTierAudience_PlatformToken_NotConsumer()
    {
        var user = WithAudiences("n1:platform");

        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Platform).Should().BeTrue();
        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Consumer)
            .Should().BeFalse("a platform token must not satisfy a consumer-audience policy");
    }

    [Fact]
    public void HasTierAudience_WrongInstallation_False()
    {
        // A token minted by a different installation must not match this installation's tier.
        var user = WithAudiences("sorcha:consumer");

        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Consumer).Should().BeFalse();
    }

    [Fact]
    public void HasTierAudience_MultipleAudClaims_AnyMatchSatisfies()
    {
        var user = WithAudiences("https://legacy", "n1:service");

        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Service).Should().BeTrue();
    }

    [Fact]
    public void HasTierAudience_NoAudClaim_False()
    {
        var user = WithAudiences();

        AuthorizationPolicyExtensions.HasTierAudience(user, N1, Tier.Consumer).Should().BeFalse();
    }
}
