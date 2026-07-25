// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Threading;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.UI.Core.Components.Pairing;
using Sorcha.UI.Core.Services.User.Devices;
using Sorcha.UI.Core.Services.Wallet;
using Sorcha.UI.Testing;
using Sorcha.UI.Testing.Builders;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Pairing;

/// <summary>
/// Issue #1270 (UT-007) — two ways the F128 nag banner nagged when it should have stayed quiet.
/// <list type="bullet">
///   <item>It rendered ABOVE the pairing page itself, telling the citizen to go and do the thing
///   they were already doing — and pushing the actual instructions down the screen.</item>
///   <item>It was not wallet-aware the way F149 made <c>PairingTakeover</c>. Pairing a phone before
///   a wallet exists dead-ends at the device-enrol 404. On the web the wallet call-to-action already
///   owns that moment on Home, so the right answer here is silence rather than a second competing
///   prompt.</item>
/// </list>
/// </summary>
public sealed class PairingNagBannerTests : ComponentTestFixture
{
    private readonly Mock<IHasPairedDeviceProbe> _probe = new();
    private readonly Mock<IWalletApiService> _walletApi = new();

    public PairingNagBannerTests()
    {
        Services.AddLogging();
        Services.AddSingleton(_probe.Object);
        Services.AddSingleton(_walletApi.Object);

        // The state the banner exists for: signed in, no paired device.
        _probe.SetupGet(p => p.HasAnyDevice).Returns(false);
        HasWallets(true);
    }

    private void HasWallets(bool any) =>
        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(any
                ? new List<WalletDto> { new WalletDtoBuilder().Build() }
                : new List<WalletDto>());

    private Microsoft.AspNetCore.Components.NavigationManager Nav =>
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();

    [Fact]
    public void OnAnOrdinaryPage_TheBannerStillNags()
    {
        var cut = Render<PairingNagBanner>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pairing-nag-banner']").Should().ContainSingle(
                "this is exactly the state the banner exists for"));
    }

    [Fact]
    public void OnThePairingPageItself_TheBannerStaysQuiet()
    {
        Nav.NavigateTo("setup/add-device");

        var cut = Render<PairingNagBanner>();

        cut.FindAll("[data-testid='pairing-nag-banner']").Should().BeEmpty(
            "nagging a citizen to do the thing they are already doing just pushes the instructions down");
    }

    [Fact]
    public void WithoutAWallet_TheBannerStaysQuiet()
    {
        HasWallets(false);

        var cut = Render<PairingNagBanner>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pairing-nag-banner']").Should().BeEmpty(
                "a paired phone with no wallet behind it dead-ends — F149 made the PWA takeover "
                + "wallet-aware for the same reason"));
    }

    [Fact]
    public void WhenTheWalletCheckFails_TheBannerStillNags()
    {
        // Fail-safe in the same direction as F149's IHasWalletProbe: a transient failure must
        // degrade to the pre-existing prompt, never silently suppress one the citizen needs.
        _walletApi.Setup(w => w.GetWalletsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("network"));

        var cut = Render<PairingNagBanner>();

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='pairing-nag-banner']").Should().ContainSingle());
    }

    [Fact]
    public void WithAPairedDevice_TheBannerStaysQuiet()
    {
        _probe.SetupGet(p => p.HasAnyDevice).Returns(true);

        var cut = Render<PairingNagBanner>();

        cut.FindAll("[data-testid='pairing-nag-banner']").Should().BeEmpty();
    }
}
