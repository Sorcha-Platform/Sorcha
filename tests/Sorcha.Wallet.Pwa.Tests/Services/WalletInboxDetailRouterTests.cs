// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Pwa.Services;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services;

/// <summary>
/// Guards the wallet half of issue #1266. Tapping the AIAS decision notice in the PWA Activity feed
/// left the app, opened an external browser at <c>about:blank</c>, and produced a zero-byte download
/// named after the instance id — because the stored <c>detailHref</c> is a raw API route and the
/// client navigated to it directly.
/// <para>
/// Unlike the web host, the PWA genuinely HAS a page for an instance
/// (<c>/applications/{InstanceId:guid}</c>), so here the reference resolves to a real view rather
/// than being refused. That asymmetry is why detail routing is a per-host seam.
/// </para>
/// </summary>
public sealed class WalletInboxDetailRouterTests
{
    private readonly WalletInboxDetailRouter _router = new();

    /// <summary>The exact href from the reported notice.</summary>
    private const string InstanceApiHref = "/api/instances/adff1578-08ac-45f4-8fb6-a421e33d45aa";

    [Fact]
    public void MapsAnInstanceReferenceOntoTheApplicationPage()
        => _router.Resolve(InstanceApiHref).Should().Be(
            "applications/adff1578-08ac-45f4-8fb6-a421e33d45aa");

    /// <summary>
    /// Base-relative, with NO leading slash: the PWA is mounted under /wallet/ by the gateway, so a
    /// rooted path escapes the app base and 404s — the same class of mistake as the F149
    /// web-base-path gotcha.
    /// </summary>
    [Fact]
    public void ResolvesBaseRelative_NotRooted()
        => _router.Resolve(InstanceApiHref).Should().NotStartWith("/");

    /// <summary>The override is narrow — API routes with no page here are still refused.</summary>
    [Fact]
    public void StillRefusesOtherApiRoutes()
        => _router.Resolve("/api/credentials/abc").Should().BeNull(
            "navigating to an API URL is the #1266 defect, whichever route it is");

    /// <summary>F150's app-relative security href must keep working.</summary>
    [Fact]
    public void StillPassesThroughAppRelativeRoutes()
        => _router.Resolve("/security").Should().Be("/security");

    /// <summary>A malformed instance href is refused rather than turned into a broken route.</summary>
    [Theory]
    [InlineData("/api/instances/not-a-guid")]
    [InlineData("/api/instances/")]
    [InlineData("/api/instances/adff1578-08ac-45f4-8fb6-a421e33d45aa/extra")]
    public void RefusesMalformedInstanceHrefs(string href)
        => _router.Resolve(href).Should().BeNull();

    [Fact]
    public void RefusesAbsoluteUrls()
        => _router.Resolve("https://n1.sorcha.dev/api/instances/abc").Should().BeNull();
}
