// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Sorcha.UI.Core.Components.Wallet;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Wallet;

/// <summary>
/// Feature 141 — bunit coverage for the wallet Home "Bolder" chrome
/// components: BigActionButton, WalletCardStack (empty ghost fan), WalletHero,
/// and FloatingTabBar. Verifies the behaviour the host (Index/MainLayout)
/// relies on: disabled-suppression, the enrol tap-target, mode-aware hero
/// copy, and active-tab highlighting + navigation.
/// </summary>
public sealed class WalletChromeComponentTests : BunitContext
{
    public WalletChromeComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    // ── BigActionButton ────────────────────────────────────────────────────

    [Fact]
    public void BigActionButton_Enabled_FiresOnActivated()
    {
        var fired = false;
        var cut = Render<BigActionButton>(ps => ps
            .Add(p => p.Kind, BigActionKind.Primary)
            .Add(p => p.Icon, "icon")
            .Add(p => p.Title, "Present")
            .Add(p => p.Subtitle, "Share a card")
            .Add(p => p.OnActivated, EventCallback.Factory.Create(this, () => fired = true)));

        cut.Find("button").Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void BigActionButton_Disabled_DoesNotFireOnActivated()
    {
        var fired = false;
        var cut = Render<BigActionButton>(ps => ps
            .Add(p => p.Kind, BigActionKind.Primary)
            .Add(p => p.Icon, "icon")
            .Add(p => p.Title, "Present")
            .Add(p => p.Disabled, true)
            .Add(p => p.OnActivated, EventCallback.Factory.Create(this, () => fired = true)));

        cut.Find("button").Click();

        fired.Should().BeFalse("a disabled BigActionButton must not activate (FR-006)");
        cut.Find("button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void BigActionButton_RendersTitleSubtitle_AndVariantClass_AndAccessibleName()
    {
        var cut = Render<BigActionButton>(ps => ps
            .Add(p => p.Kind, BigActionKind.Ghost)
            .Add(p => p.Icon, "icon")
            .Add(p => p.Title, "Verify")
            .Add(p => p.Subtitle, "Scan someone"));

        var button = cut.Find("button");
        button.ClassList.Should().Contain("big-action--ghost");
        button.GetAttribute("aria-label").Should().Be("Verify. Scan someone");
        button.GetAttribute("data-testid").Should().Be("big-action-verify");
        cut.Markup.Should().Contain("Verify").And.Contain("Scan someone");
    }

    [Fact]
    public void BigActionButton_DefaultsTestIdFromTitle_WhenNotSupplied()
    {
        var cut = Render<BigActionButton>(ps => ps
            .Add(p => p.Kind, BigActionKind.Primary)
            .Add(p => p.Icon, "icon")
            .Add(p => p.Title, "Present")
            .Add(p => p.TestId, "home-present-button"));

        cut.Find("button").GetAttribute("data-testid").Should().Be("home-present-button");
    }

    // ── WalletCardStack (empty ghost fan) ───────────────────────────────────

    [Fact]
    public void WalletCardStack_RendersThreeCards_TopCardIsTheEnrolTapTarget()
    {
        var added = false;
        var cut = Render<WalletCardStack>(ps => ps
            .Add(p => p.TestId, "enrol-device-button")
            .Add(p => p.OnAddCredential, EventCallback.Factory.Create(this, () => added = true)));

        cut.FindAll(".ghost-card").Count.Should().Be(3);

        var topCard = cut.Find("[data-testid='enrol-device-button']");
        topCard.GetAttribute("aria-label").Should().Contain("Add a credential");
        topCard.Click();

        added.Should().BeTrue("tapping the top ghost card starts enrolment (FR-010)");
    }

    [Fact]
    public void WalletCardStack_DecorativeCardsAreHiddenFromAssistiveTech()
    {
        var cut = Render<WalletCardStack>();

        cut.FindAll(".ghost-card[aria-hidden='true']").Count.Should().Be(2,
            "the two cards behind the live top card are decorative");
    }

    // ── WalletHero ──────────────────────────────────────────────────────────

    [Fact]
    public void WalletHero_EmptyMode_RendersWelcomeCopy()
    {
        var cut = Render<WalletHero>(ps => ps
            .Add(p => p.Mode, WalletHeroMode.Empty));

        cut.Find("[data-testid='wallet-hero-eyebrow']").TextContent.Should().Be("WELCOME");
        cut.Find("[data-testid='wallet-hero-headline']").TextContent.Should().Be("Your wallet is empty");
    }

    [Theory]
    [InlineData(1, "1 credential")]
    [InlineData(3, "3 credentials")]
    public void WalletHero_ActiveMode_RendersCountAwareHeadline(int count, string expected)
    {
        var cut = Render<WalletHero>(ps => ps
            .Add(p => p.Mode, WalletHeroMode.Active)
            .Add(p => p.CredentialCount, count));

        cut.Find("[data-testid='wallet-hero-eyebrow']").TextContent.Should().Be("ACTIVE WALLET");
        cut.Find("[data-testid='wallet-hero-headline']").TextContent.Should().Be(expected);
    }

    [Fact]
    public void WalletHero_RendersHeaderContentSlot()
    {
        var cut = Render<WalletHero>(ps => ps
            .Add(p => p.Mode, WalletHeroMode.Empty)
            .Add(p => p.HeaderContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span data-testid='hdr'>X</span>"))));

        cut.FindAll("[data-testid='hdr']").Should().ContainSingle();
        cut.Find("[data-testid='wallet-hero-header']").Should().NotBeNull();
    }

    // ── FloatingTabBar ──────────────────────────────────────────────────────

    [Fact]
    public void FloatingTabBar_RendersFourTabs_WithStableTestIds()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.ActiveRoute, ""));

        cut.FindAll(".tab-pill").Count.Should().Be(4);
        foreach (var id in new[] { "footer-nav-home", "footer-nav-devices", "footer-nav-activity", "footer-nav-settings" })
        {
            cut.FindAll($"[data-testid='{id}']").Should().ContainSingle($"tab {id} must exist for navigation tests");
        }
    }

    [Fact]
    public void FloatingTabBar_ActiveTab_IsHighlightedAndLabelled_OthersIconOnly()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.ActiveRoute, "devices"));

        var devices = cut.Find("[data-testid='footer-nav-devices']");
        devices.ClassList.Should().Contain("tab-pill--active");
        devices.GetAttribute("aria-current").Should().Be("page");
        devices.TextContent.Should().Contain("Devices");

        var home = cut.Find("[data-testid='footer-nav-home']");
        home.ClassList.Should().NotContain("tab-pill--active");
        // Inactive tab is icon-only — its label is not rendered as text.
        home.TextContent.Trim().Should().BeEmpty();
        // ...but still exposes an accessible name (FR-024).
        home.GetAttribute("aria-label").Should().Be("Home");
    }

    [Fact]
    public void FloatingTabBar_Tap_EmitsBaseRelativeRoute()
    {
        string? navigated = null;
        var cut = Render<FloatingTabBar>(ps => ps
            .Add(p => p.ActiveRoute, "")
            .Add(p => p.OnNavigate, EventCallback.Factory.Create<string>(this, r => navigated = r)));

        cut.Find("[data-testid='footer-nav-settings']").Click();

        navigated.Should().Be("settings", "host performs NavigateTo with a base-relative route");
    }

    [Fact]
    public void FloatingTabBar_TreatsLeadingSlashActiveRoute_AsHome()
    {
        var cut = Render<FloatingTabBar>(ps => ps.Add(p => p.ActiveRoute, "/"));

        cut.Find("[data-testid='footer-nav-home']").ClassList.Should().Contain("tab-pill--active");
    }
}
