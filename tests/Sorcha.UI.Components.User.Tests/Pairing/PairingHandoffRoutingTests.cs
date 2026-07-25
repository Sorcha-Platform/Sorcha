// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using FluentAssertions;
using Sorcha.UI.Core.Services.User.Pairing;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Pairing;

/// <summary>
/// Issue #1270 (UT-007) — the F128 handoff surface showed the DESKTOP QR variant on a phone.
/// <para>
/// The cause is that the surface only ever had one axis to switch on: <c>IPwaInstallabilityProbe</c>
/// answers "can this browser install the PWA?", and the surface treated "no" as "this is a desktop".
/// <b>Mobile-but-not-installable is neither.</b> An Android citizen who already installed the wallet
/// (so <c>beforeinstallprompt</c> never fires again), anyone in an in-app browser, and every
/// non-Safari iOS browser all land there — and are shown a QR code to scan with the phone they are
/// already holding.
/// </para>
/// <para>
/// FR-008 exists to give the same-device path prominence on mobile. Adding the mobile axis is what
/// makes that expressible.
/// </para>
/// </summary>
public sealed class PairingHandoffRoutingTests
{
    [Theory]
    [InlineData(PwaInstallabilityVerdict.CanInstallProgrammatically)]
    [InlineData(PwaInstallabilityVerdict.CanInstallManually)]
    public void AnInstallableBrowserInstallsOnThisPhone(PwaInstallabilityVerdict verdict)
        => PairingHandoffRouting.Choose(verdict, isMobile: true)
            .Should().Be(PairingHandoffRoute.InstallOnThisPhone);

    [Fact]
    public void AMobileBrowserThatCannotInstall_OpensOnThisDevice_NotAQrToScanWithItself()
    {
        PairingHandoffRouting.Choose(PwaInstallabilityVerdict.CannotInstall, isMobile: true)
            .Should().Be(PairingHandoffRoute.OpenOnThisDevice,
                "a QR code is useless to the phone that would have to scan it");
    }

    [Fact]
    public void ADesktopBrowserScansWithAPhone()
        => PairingHandoffRouting.Choose(PwaInstallabilityVerdict.CannotInstall, isMobile: false)
            .Should().Be(PairingHandoffRoute.ScanWithYourPhone);

    [Fact]
    public void ADesktopThatSomehowReportsInstallable_StillInstallsRatherThanScanning()
    {
        // Chromium on a desktop genuinely does fire beforeinstallprompt. Installing there is a real
        // and useful outcome, so the mobile axis must not override a positive install verdict.
        PairingHandoffRouting.Choose(PwaInstallabilityVerdict.CanInstallProgrammatically, isMobile: false)
            .Should().Be(PairingHandoffRoute.InstallOnThisPhone);
    }

    [Fact]
    public void EveryVerdictIsRouted_NoneFallsThroughToTheDesktopDefaultByAccident()
    {
        // Derived from the real enum: a verdict added later must be routed deliberately, not inherit
        // "scan with your phone" — which is precisely the silent mis-routing this issue was.
        foreach (var verdict in Enum.GetValues<PwaInstallabilityVerdict>())
        {
            var mobile = PairingHandoffRouting.Choose(verdict, isMobile: true);
            mobile.Should().NotBe(PairingHandoffRoute.ScanWithYourPhone,
                $"'{verdict}' on a phone must never ask the citizen to scan a code with that phone");
        }
    }
}
