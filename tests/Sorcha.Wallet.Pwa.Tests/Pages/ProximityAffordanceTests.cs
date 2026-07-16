// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Threading;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sorcha.Proximity;
using Sorcha.UI.Testing;
using Sorcha.Wallet.Pwa.Models.Device;
using Sorcha.Wallet.Pwa.Services;
using Sorcha.Wallet.Pwa.Services.Device;
using Sorcha.Wallet.Pwa.Services.Presentation;
using Xunit;

using PresentPage = Sorcha.Wallet.Pwa.Pages.Present;

namespace Sorcha.Wallet.Pwa.Tests.Pages;

/// <summary>
/// The in-person sharing affordance must be <b>hidden</b> where the device cannot honour it — not shown and
/// then broken (FR-020).
/// </summary>
/// <remarks>
/// <para>
/// This matters more than it looks. The wallet is a PWA and the <em>same build</em> runs in a plain browser
/// (the native shell points at the hosted app), where there is no BLE at all. An always-visible "Share in
/// person" button would be dead for a large share of sessions — and a citizen who taps it at a counter and
/// gets an error is worse off than one who was never offered it.
/// </para>
/// <para>
/// The page resolves the transport through <c>IServiceProvider</c> rather than <c>@inject</c>, so a host with
/// <b>no</b> proximity registration renders perfectly and simply offers nothing. That is what the first test
/// pins — and it is why adding this feature did not force every unrelated Present-page test to register a BLE
/// transport it does not care about.
/// </para>
/// </remarks>
public sealed class ProximityAffordanceTests : ComponentTestFixture
{
    private readonly Mock<IPresentationEngine> _engine = new();
    private readonly Mock<ICredentialCache> _credentials = new();
    private readonly Mock<IDeviceKeyService> _deviceKey = new();
    private readonly Mock<IDelegationStore> _delegation = new();
    private readonly Mock<IStatusListService> _statusList = new();
    private readonly Mock<IPresentationLog> _log = new();

    public ProximityAffordanceTests()
    {
        Services.AddSingleton(_engine.Object);
        Services.AddSingleton(_credentials.Object);
        Services.AddSingleton(_deviceKey.Object);
        Services.AddSingleton(_delegation.Object);
        Services.AddSingleton(_statusList.Object);
        Services.AddSingleton(_log.Object);
        // Task 7 wiring — the page resolves the selection inputs (holder + wallet clients).
        Services.AddSingleton(new Mock<Sorcha.ServiceClients.CitizenWallet.ICitizenWalletClient>().Object);
        Services.AddSingleton(new Mock<Sorcha.UI.Core.Services.HolderKeys.IHolderKeyClient>().Object);
        Services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PresentPage>>(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PresentPage>.Instance);
        Services.AddScoped<System.Net.Http.HttpClient>(_ => new System.Net.Http.HttpClient());

        var probe = new Mock<IDeviceProfileProbe>();
        probe.Setup(p => p.GetProfileAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new DeviceProfile(DeviceFormFactor.Desktop, CameraAvailability.Unavailable));
        Services.AddScoped(_ => probe.Object);
    }

    private void RegisterTransport(ProximityCapability capability)
    {
        var transport = new Mock<IProximityTransport>();
        transport
            .Setup(t => t.ProbeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(capability);

        Services.AddSingleton(transport.Object);
    }

    /// <summary>A host with no proximity registration at all renders fine and offers nothing.</summary>
    [Fact]
    public void NoProximityTransportRegistered_TheAffordanceIsAbsent()
    {
        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-proximity-button]").Should().BeEmpty(because:
            "a host with no proximity support must render the online path perfectly and offer nothing it " +
            "cannot honour");
    }

    /// <summary>A real device that cannot do it — no radio, or permission refused — is offered nothing.</summary>
    [Fact]
    public void TransportReportsUnsupported_TheAffordanceIsAbsent()
    {
        RegisterTransport(new ProximityCapability(
            Supported: false,
            BluetoothEnabled: false,
            PermissionGranted: false,
            ProximityUnsupportedReason.NoBluetoothHardware));

        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-proximity-button]").Should().BeEmpty();
    }

    /// <summary>A device that can do it is offered it.</summary>
    [Fact]
    public void TransportReportsSupported_TheAffordanceIsOffered()
    {
        RegisterTransport(new ProximityCapability(
            Supported: true,
            BluetoothEnabled: true,
            PermissionGranted: true));

        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-proximity-button]").Should().ContainSingle();
    }

    /// <summary>A misbehaving native layer must never take the online presentation path down with it.</summary>
    [Fact]
    public void AProbeThatThrows_DoesNotBreakTheOnlinePath()
    {
        var transport = new Mock<IProximityTransport>();
        transport
            .Setup(t => t.ProbeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("the plugin exploded"));

        Services.AddSingleton(transport.Object);

        var cut = Render<PresentPage>();

        cut.FindAll("[data-testid=present-proximity-button]").Should().BeEmpty();
        cut.Markup.Should().NotBeNullOrWhiteSpace(because: "the online path still renders");
    }
}
