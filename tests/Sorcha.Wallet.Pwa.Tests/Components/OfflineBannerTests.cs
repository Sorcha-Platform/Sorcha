// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Components;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Components;

/// <summary>
/// bUnit component tests for the wallet's shell-level <see cref="OfflineBanner"/>.
/// Verifies the connectivity contract: the banner reflects navigator.onLine via
/// js/connectivity.js, flips on the online/offline callback, carries the
/// accessibility attributes screen readers rely on, and unregisters on dispose.
/// </summary>
public sealed class OfflineBannerTests : ComponentTestFixture
{
    private const string Register = "SorchaConnectivity.register";
    private const string Unregister = "SorchaConnectivity.unregister";

    [Fact]
    public void WhenOnline_RendersNothing()
    {
        // register() reports the device is online → no banner, zero layout cost.
        JSInterop.Setup<bool>(Register, _ => true).SetResult(true);

        var cut = Render<OfflineBanner>();

        cut.FindAll("[data-testid=offline-banner]").Should().BeEmpty();
    }

    [Fact]
    public void WhenOffline_RendersAccessibleBanner()
    {
        // register() reports the device is offline → banner shows immediately.
        JSInterop.Setup<bool>(Register, _ => true).SetResult(false);

        var cut = Render<OfflineBanner>();

        var banner = cut.Find("[data-testid=offline-banner]");
        banner.GetAttribute("role").Should().Be("status");
        banner.GetAttribute("aria-live").Should().Be("polite");
        banner.TextContent.Should().Contain("Offline");
    }

    [Fact]
    public void WhenConnectivityDropsToOffline_BannerAppears()
    {
        JSInterop.Setup<bool>(Register, _ => true).SetResult(true);
        var cut = Render<OfflineBanner>();
        cut.FindAll("[data-testid=offline-banner]").Should().BeEmpty();

        // Simulate the window 'offline' event firing through to the JSInvokable.
        cut.InvokeAsync(() => cut.Instance.OnConnectivityChanged(false));

        cut.FindAll("[data-testid=offline-banner]").Should().ContainSingle();
    }

    [Fact]
    public void WhenConnectivityReturns_BannerClears()
    {
        JSInterop.Setup<bool>(Register, _ => true).SetResult(false);
        var cut = Render<OfflineBanner>();
        cut.FindAll("[data-testid=offline-banner]").Should().ContainSingle();

        cut.InvokeAsync(() => cut.Instance.OnConnectivityChanged(true));

        cut.FindAll("[data-testid=offline-banner]").Should().BeEmpty();
    }

    [Fact]
    public async Task OnDispose_UnregistersListeners()
    {
        JSInterop.Setup<bool>(Register, _ => true).SetResult(true);
        var cut = Render<OfflineBanner>();

        await cut.Instance.DisposeAsync();

        JSInterop.VerifyInvoke(Unregister);
    }
}
