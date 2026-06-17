// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.ServiceDefaults.Auth;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="RequestedTierResolver"/> — the request-side tier derivation (spec 136,
/// contract auth-entry-tier-request, FR-010). This resolves the <em>requested</em> tier only;
/// the entitlement gate (<see cref="TierResolver"/>) decides what is actually minted.
/// </summary>
public sealed class RequestedTierResolverTests
{
    private static readonly ReturnToAllowlistOptions Allowlist = new()
    {
        Hosts = ["wallet.strathcarron.gov", "*.consumer.example"],
    };

    // --- Explicit override (rule 1) ---

    [Theory]
    [InlineData("consumer", Tier.Consumer)]
    [InlineData("platform", Tier.Platform)]
    [InlineData("PLATFORM", Tier.Platform)]
    [InlineData("  Consumer  ", Tier.Consumer)]
    public void Resolve_ExplicitHint_Wins(string hint, Tier expected)
    {
        // Even with a conflicting returnTo, the explicit hint takes precedence.
        RequestedTierResolver.Resolve(hint, returnTo: "/app/orgs").Should().Be(expected);
    }

    [Theory]
    [InlineData("service")]        // machine tier — not human-selectable
    [InlineData("enrol-session")]
    [InlineData("garbage")]
    [InlineData("")]
    public void Resolve_UnknownOrMachineHint_IsIgnored_FallsThrough(string hint)
    {
        // Hint ignored → returnTo classifies → /wallet ⇒ Consumer.
        RequestedTierResolver.Resolve(hint, returnTo: "/wallet/home").Should().Be(Tier.Consumer);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("enrol-session")]
    [InlineData("nonsense")]
    public void ParseTierHint_OnlyAcceptsHumanTiers(string hint)
    {
        RequestedTierResolver.ParseTierHint(hint).Should().BeNull();
    }

    // --- returnTo path classification (rule 2) ---

    [Theory]
    [InlineData("/wallet")]
    [InlineData("/wallet/")]
    [InlineData("/wallet/home")]
    [InlineData("/wallet?x=1")]
    public void Resolve_WalletPath_IsConsumer(string returnTo)
    {
        RequestedTierResolver.Resolve(explicitTierHint: null, returnTo).Should().Be(Tier.Consumer);
    }

    [Theory]
    [InlineData("/app")]
    [InlineData("/app/")]
    [InlineData("/app/orgs")]
    [InlineData("/app/designer/blueprint")]
    public void Resolve_AppSpaPath_IsPlatform(string returnTo)
    {
        // The whole platform/admin experience is the /app Blazor SPA — no separate /admin route.
        RequestedTierResolver.Resolve(explicitTierHint: null, returnTo).Should().Be(Tier.Platform);
    }

    [Fact]
    public void Resolve_PathSegmentMatch_NotPrefix()
    {
        // "/wallets" must NOT be classified as the wallet mount.
        RequestedTierResolver.Resolve(null, "/wallets/foo").Should().Be(Tier.Consumer,
            "an unclassifiable returnTo falls through to the default consumer tier — but it must not " +
            "be classified *as* the wallet mount; the default just happens to also be consumer");
    }

    // --- returnTo absolute-URL + allowlist (rule 2) ---

    [Fact]
    public void Resolve_AllowlistedConsumerHost_IsConsumer()
    {
        RequestedTierResolver.Resolve(null, "https://wallet.strathcarron.gov/return", Allowlist)
            .Should().Be(Tier.Consumer);
    }

    [Fact]
    public void Resolve_AbsoluteUrl_NonAllowlistedHost_ClassifiesByPath()
    {
        // Host not allow-listed → fall back to path classification (/app ⇒ platform).
        RequestedTierResolver.Resolve(null, "https://evil.example/app/orgs", Allowlist)
            .Should().Be(Tier.Platform);
    }

    [Theory]
    [InlineData("https://localhost/app/orgs")]
    [InlineData("https://n1.sorcha.dev/app/designer/blueprint")]
    [InlineData("http://localhost/app/")]
    public void ClassifyReturnTo_AllowlistedOwnHost_AppPath_IsPlatform(string returnTo)
    {
        // Regression: the platform's own host is allow-listed for redirect-safety, NOT as a consumer
        // host. A self-referential /app return (RedirectToLogin sends the absolute current URL) must
        // classify Platform — host-first classification minted an entitled admin a roleless consumer
        // token, hiding the entire admin menu.
        var ownHostAllowlist = new ReturnToAllowlistOptions { Hosts = ["localhost", "n1.sorcha.dev"] };
        RequestedTierResolver.ClassifyReturnTo(returnTo, ownHostAllowlist).Should().Be(Tier.Platform);
    }

    [Fact]
    public void ClassifyReturnTo_AllowlistedOwnHost_WalletPath_IsConsumer()
    {
        // /wallet self-return still classifies Consumer — path-first preserves the wallet boundary.
        var ownHostAllowlist = new ReturnToAllowlistOptions { Hosts = ["localhost", "n1.sorcha.dev"] };
        RequestedTierResolver.ClassifyReturnTo("https://n1.sorcha.dev/wallet/home", ownHostAllowlist)
            .Should().Be(Tier.Consumer);
    }

    // --- default (rule 3) ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/some/other/path")]
    public void Resolve_NothingClassifiable_DefaultsToConsumer(string? returnTo)
    {
        RequestedTierResolver.Resolve(explicitTierHint: null, returnTo).Should().Be(Tier.Consumer);
    }
}
