// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Threading;
using System.Threading.Tasks;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MudBlazor.Services;
using Sorcha.UI.Core.Components.Presentation;
using Sorcha.UI.Core.Services.User.Devices;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Components.Presentation;

/// <summary>
/// Issue #1280 (UT-017) — the web "Present" affordance told the citizen that verifiable-credential
/// presentation was "planned for a future release". It is not: it ships on the wallet PWA
/// (<c>Present.razor</c>). Sorcha is companion-first, so web is deliberately not the presenting
/// surface — which makes this copy-and-redirect, not a missing feature.
/// <para>
/// These tests pin the three states the notice must distinguish. The pairing state comes from
/// <see cref="IHasPairedDeviceProbe"/>, whose <c>HasAnyDevice</c> is deliberately tri-state: the
/// <c>null</c> arm means "the probe has not resolved", and telling a citizen who already has a
/// phone to go pair one is exactly as wrong as telling a citizen with no phone to go open a wallet
/// they never installed. So unknown offers both routes rather than guessing.
/// </para>
/// </summary>
public sealed class PresentOnPhoneNoticeTests : BunitContext
{
    private readonly Mock<IHasPairedDeviceProbe> _probe = new();

    public PresentOnPhoneNoticeTests()
    {
        Services.AddMudServices();
        Services.AddSingleton(_probe.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void ProbeReports(bool? hasAnyDevice) =>
        _probe.SetupGet(p => p.HasAnyDevice).Returns(hasAnyDevice);

    [Fact]
    public void PairedCitizen_IsPointedAtTheirPhone_NotToldPresentationIsUnbuilt()
    {
        ProbeReports(true);

        var cut = Render<PresentOnPhoneNotice>(ps => ps.Add(p => p.CredentialName, "Assured Identity"));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().NotContain("future release",
                "presentation ships on the PWA — claiming otherwise is false");
            cut.FindAll("[data-testid='present-on-phone-open-wallet']").Should().ContainSingle(
                "a citizen who already paired a phone needs the route to their wallet");
            cut.FindAll("[data-testid='present-on-phone-pair']").Should().BeEmpty(
                "they have a paired phone already — offering to pair one is noise");
        });
    }

    [Fact]
    public void UnpairedCitizen_IsOfferedPairing_NotAWalletTheyHaveNotInstalled()
    {
        ProbeReports(false);

        var cut = Render<PresentOnPhoneNotice>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='present-on-phone-pair']").Should().ContainSingle();
            cut.FindAll("[data-testid='present-on-phone-open-wallet']").Should().BeEmpty(
                "there is no wallet to open on a phone that was never paired");
        });
    }

    [Fact]
    public void UnresolvedProbe_OffersBothRoutes_RatherThanGuessing()
    {
        ProbeReports(null);

        var cut = Render<PresentOnPhoneNotice>();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll("[data-testid='present-on-phone-open-wallet']").Should().ContainSingle();
            cut.FindAll("[data-testid='present-on-phone-pair']").Should().ContainSingle();
        });
    }

    [Fact]
    public void CredentialName_IsNamedSoTheCitizenKnowsWhichOneTheyAskedFor()
    {
        ProbeReports(true);

        var cut = Render<PresentOnPhoneNotice>(ps => ps.Add(p => p.CredentialName, "Assured Identity"));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Assured Identity"));
    }

    [Fact]
    public void Dismiss_RaisesTheCallback_SoTheHostCanRetractTheNotice()
    {
        ProbeReports(true);
        var dismissed = false;

        var cut = Render<PresentOnPhoneNotice>(ps => ps
            .Add(p => p.OnDismiss, () => dismissed = true));

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='present-on-phone-dismiss']").Should().ContainSingle());
        cut.Find("[data-testid='present-on-phone-dismiss']").Click();

        dismissed.Should().BeTrue();
    }

    [Fact]
    public async Task Notice_AwaitsTheProbe_SoItNeverRendersTheUnknownArmForAResolvableCitizen()
    {
        ProbeReports(null);
        _probe.Setup(p => p.EnsureLoadedAsync(It.IsAny<CancellationToken>()))
            .Callback(() => ProbeReports(true))
            .Returns(Task.CompletedTask);

        var cut = Render<PresentOnPhoneNotice>();

        await cut.InvokeAsync(() => { });

        cut.WaitForAssertion(() =>
            cut.FindAll("[data-testid='present-on-phone-pair']").Should().BeEmpty(
                "the probe resolved to true during load — the unknown arm must not linger"));
    }
}
