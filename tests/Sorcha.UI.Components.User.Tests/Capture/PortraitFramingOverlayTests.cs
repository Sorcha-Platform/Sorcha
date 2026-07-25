// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Components.User.Components.Capture;
using Xunit;

namespace Sorcha.UI.Components.User.Tests.Capture;

/// <summary>
/// Issue #1277 (UT-014) — the post-capture review overlay. The only framing help before this was a
/// list of written tips beside the file picker: advice the citizen read BEFORE taking a photo and
/// could not check their photo against afterwards.
/// </summary>
public sealed class PortraitFramingOverlayTests : BunitContext
{
    private const string Jpeg = "/9j/4AAQSkZJRg==";

    public PortraitFramingOverlayTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<PortraitFramingOverlay> RenderOverlay(PortraitFramingRules? rules = null) =>
        Render<PortraitFramingOverlay>(ps =>
        {
            ps.Add(p => p.Base64Jpeg, Jpeg);
            if (rules is not null) ps.Add(p => p.Rules, rules);
        });

    [Fact]
    public void ItShowsThePhotoTheCitizenActuallyCaptured()
    {
        var cut = RenderOverlay();

        cut.Find("[data-testid='portrait-framing-image']")
            .GetAttribute("src").Should().Be($"data:image/jpeg;base64,{Jpeg}");
    }

    [Fact]
    public void RetakeIsAlwaysOffered_AndSoIsKeepingThePhoto()
    {
        var cut = RenderOverlay();

        // Never a gate: the citizen can always proceed with what they took. A wrong client-side
        // rejection would stop them submitting at all, which is worse than the claim-drop this
        // issue is about.
        cut.FindAll("[data-testid='portrait-framing-accept']").Should().ContainSingle();
        cut.FindAll("[data-testid='portrait-framing-retake']").Should().ContainSingle();
    }

    [Fact]
    public void RetakeRaisesItsCallback()
    {
        var retaken = false;
        var cut = Render<PortraitFramingOverlay>(ps => ps
            .Add(p => p.Base64Jpeg, Jpeg)
            .Add(p => p.OnRetake, () => retaken = true));

        cut.Find("[data-testid='portrait-framing-retake']").Click();

        retaken.Should().BeTrue();
    }

    [Fact]
    public void TheGuidesFollowTheAuthoredRules()
    {
        var cut = RenderOverlay(new PortraitFramingRules
        {
            OvalWidthPct = 70,
            HeadTopPct = 12,
            HeadBottomPct = 88,
        });

        var markup = cut.Markup;
        markup.Should().Contain("width:70%");
        markup.Should().Contain("top:12%");
        // Oval height is the band between the two bounds, not an independent number.
        markup.Should().Contain("height:76%");
        markup.Should().Contain("top:88%");
    }

    [Fact]
    public void NonsenseRulesAreClampedByTheOverlayItself()
    {
        // The host may pass rules straight from a blueprint. The overlay clamps again rather than
        // trusting its caller — guides drawn outside their own frame are worse than default guides.
        var cut = RenderOverlay(new PortraitFramingRules
        {
            OvalWidthPct = 900,
            HeadTopPct = 95,
            HeadBottomPct = 5,
        });

        cut.Markup.Should().NotContain("width:900%");
        cut.Markup.Should().Contain($"top:{PortraitFramingRules.Default.HeadTopPct.ToString("0.##", CultureInfo.InvariantCulture)}%");
    }

    [Fact]
    public void PercentagesUseAnInvariantDecimalSeparator()
    {
        // A comma separator from a de/fr locale produces CSS the browser silently drops, which would
        // put every guide at its default position with no error anywhere.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var cut = RenderOverlay(new PortraitFramingRules
            {
                OvalWidthPct = 62.5,
                HeadTopPct = 8,
                HeadBottomPct = 82,
            });

            cut.Markup.Should().Contain("width:62.5%").And.NotContain("62,5");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
