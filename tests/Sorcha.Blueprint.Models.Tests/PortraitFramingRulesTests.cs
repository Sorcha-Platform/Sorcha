// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models;
using Xunit;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Issue #1277 (UT-014) — framing rules for the post-capture review overlay, authored in
/// <c>x-file</c>.
/// <para>
/// Before this the only framing help was written tips shown beside the file picker: advice the
/// citizen read BEFORE taking a photo and could not check their photo against afterwards. The
/// numbers live in the blueprint rather than the component because they are a property of the
/// credential being issued — an issuer with looser or stricter portrait rules than ICAO should be
/// able to say so without a UI change.
/// </para>
/// <para>
/// Every failure mode degrades to the default overlay. Framing is guidance, never a gate: nonsense
/// guidance is worse than standard guidance, and removing the citizen's only visual check would be
/// worse still.
/// </para>
/// </summary>
public sealed class PortraitFramingRulesTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void AnAuthoredFramingBlockIsHonoured()
    {
        var rules = PortraitFramingRules.FromXFile(
            Json("""{"framing":{"ovalWidthPct":70,"headTopPct":12,"headBottomPct":88}}"""));

        rules.OvalWidthPct.Should().Be(70);
        rules.HeadTopPct.Should().Be(12);
        rules.HeadBottomPct.Should().Be(88);
    }

    [Fact]
    public void NoFramingBlockYieldsTheIcaoDefault()
        => PortraitFramingRules.FromXFile(Json("""{"capture":"user"}"""))
            .Should().Be(PortraitFramingRules.Default);

    [Fact]
    public void APartialBlockKeepsTheDefaultsForWhatItOmits()
    {
        var rules = PortraitFramingRules.FromXFile(Json("""{"framing":{"ovalWidthPct":70}}"""));

        rules.OvalWidthPct.Should().Be(70);
        rules.HeadTopPct.Should().Be(PortraitFramingRules.Default.HeadTopPct);
        rules.HeadBottomPct.Should().Be(PortraitFramingRules.Default.HeadBottomPct);
    }

    [Fact]
    public void AMalformedBlockDegradesToTheDefault_RatherThanRemovingTheOverlay()
    {
        PortraitFramingRules.FromXFile(Json("""{"framing":"nonsense"}"""))
            .Should().Be(PortraitFramingRules.Default);
        PortraitFramingRules.FromXFile(Json("""{"framing":{"headTopPct":"eight"}}"""))
            .Should().Be(PortraitFramingRules.Default);
    }

    [Fact]
    public void AnInvertedBandFallsBackToTheDefaultBand()
    {
        // Chin above the crown describes nothing. Drawing it would put the overlay's own guides in
        // the wrong order on screen.
        var rules = PortraitFramingRules.FromXFile(
            Json("""{"framing":{"headTopPct":90,"headBottomPct":10}}"""));

        rules.HeadTopPct.Should().Be(PortraitFramingRules.Default.HeadTopPct);
        rules.HeadBottomPct.Should().Be(PortraitFramingRules.Default.HeadBottomPct);
    }

    [Fact]
    public void ValuesOutsideTheFrameAreClampedIntoIt()
    {
        var rules = PortraitFramingRules.FromXFile(
            Json("""{"framing":{"ovalWidthPct":400,"headTopPct":-30,"headBottomPct":260}}"""));

        rules.OvalWidthPct.Should().BeLessThanOrEqualTo(100);
        rules.HeadTopPct.Should().BeGreaterThanOrEqualTo(0);
        rules.HeadBottomPct.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void TheDefaultBandMatchesIcaoHeadHeightGuidance()
    {
        var d = PortraitFramingRules.Default;

        // ICAO/ISO 19794-5: the head occupies roughly 70–80% of the image height.
        (d.HeadBottomPct - d.HeadTopPct).Should().BeInRange(70, 80);
        d.HeadTopPct.Should().BeGreaterThan(0, "leaving no headroom above the crown crops the head");
    }
}
