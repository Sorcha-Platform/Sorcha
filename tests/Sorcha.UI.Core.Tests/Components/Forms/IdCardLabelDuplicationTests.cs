// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Bunit;
using FluentAssertions;
using Sorcha.UI.Core.Components.Forms.Layouts;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Testing;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms;

/// <summary>
/// Guards issue #1272: the <c>x-review</c> id-card printed both the section title and the field
/// label, so a single-field section showed the same words twice. On the AIAS card three of five
/// sections were exact repeats (ADDRESS/ADDRESS, EMAIL/EMAIL, SECURE DELIVERY/SECURE DELIVERY).
/// </summary>
public sealed class IdCardLabelDuplicationTests : ComponentTestFixture
{
    private static IdCardLayoutConfig Config(params IdCardSection[] sections) => new()
    {
        IssuerName = "Acme Identity Assurance Services",
        CredentialName = "Assured Identity",
        ColourTheme = XReviewColourTheme.IdentityNavy,
        Watermark = IdCardWatermark.Issued,
        Editable = false,
        Sections = sections,
        FieldValues = new Dictionary<string, object?>
        {
            ["/address"] = "6/2 Warrender Park Terrace, Edinburgh, EH91JA, GB",
            ["/email"] = "citizen@example.test",
            ["/givenName"] = "Stuart",
            ["/familyName"] = "Fraser",
            ["/dateOfBirth"] = "1968-08-19",
        },
    };

    /// <summary>An exact repeat — the case that read as "ADDRESS / ADDRESS".</summary>
    [Fact]
    public void SingleFieldSection_LabelMatchingTitle_RendersTheTitleOnce()
    {
        var cut = Render<IdCardLayout>(ps => ps.Add(p => p.Config,
            Config(new IdCardSection("Address", 0, ["/address"]))));

        CountOccurrences(cut.Markup, "Address").Should().Be(
            1, "the section heading already names the field — printing the label repeats it (#1272)");
    }

    /// <summary>
    /// The possessive-phrasing case: "Your date of birth" restates "Date of Birth". Different words,
    /// same meaning, so it still reads as a repeat to a citizen.
    /// </summary>
    [Fact]
    public void SingleFieldSection_TitlePhrasedPossessively_StillSuppressesTheLabel()
    {
        IdCardLayout.LabelRestatesTitle("Your date of birth", "Date of Birth").Should().BeTrue();
        IdCardLayout.LabelRestatesTitle("Your name", "Name").Should().BeTrue();
        IdCardLayout.LabelRestatesTitle("My email", "Email").Should().BeTrue();
    }

    /// <summary>
    /// The load-bearing negative: with two or more fields every label is doing real work telling
    /// them apart, so none may be suppressed however much one resembles the heading.
    /// </summary>
    [Fact]
    public void MultiFieldSection_KeepsEveryLabel_EvenWhenOneMatchesTheTitle()
    {
        var section = new IdCardSection("Given Name", 0, ["/givenName", "/familyName"]);

        IdCardLayout.SuppressesFieldLabel(section, "/givenName").Should().BeFalse(
            "suppressing here would leave two values with only one label between them");

        var cut = Render<IdCardLayout>(ps => ps.Add(p => p.Config, Config(section)));
        cut.Markup.Should().Contain("Family Name");
    }

    /// <summary>A genuinely different label is never matched away.</summary>
    [Fact]
    public void SingleFieldSection_UnrelatedLabel_KeepsBothLines()
    {
        IdCardLayout.LabelRestatesTitle("Your details", "Email").Should().BeFalse();
        IdCardLayout.LabelRestatesTitle("Address", "Date of Birth").Should().BeFalse();

        var cut = Render<IdCardLayout>(ps => ps.Add(p => p.Config,
            Config(new IdCardSection("Contact details", 0, ["/email"]))));

        cut.Markup.Should().Contain("Contact details");
        cut.Markup.Should().Contain("Email");
    }

    /// <summary>
    /// Suppression must never remove the VALUE — only the redundant label. A citizen reading their own
    /// identity card has to see what it says.
    /// </summary>
    [Fact]
    public void SuppressingTheLabel_StillRendersTheValue()
    {
        var cut = Render<IdCardLayout>(ps => ps.Add(p => p.Config,
            Config(new IdCardSection("Email", 0, ["/email"]))));

        cut.Markup.Should().Contain("citizen@example.test");
    }

    [Fact]
    public void EmptyTitleOrLabel_NeverCountsAsARestatement()
    {
        IdCardLayout.LabelRestatesTitle(null, "Email").Should().BeFalse();
        IdCardLayout.LabelRestatesTitle("Email", null).Should().BeFalse();
        IdCardLayout.LabelRestatesTitle("  ", "  ").Should().BeFalse();
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, System.StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
