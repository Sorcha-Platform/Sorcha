// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Forms;
using Sorcha.UI.Core.Services.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Forms;

/// <summary>
/// Tests for <see cref="ReviewSummaryDataSource.BuildConfig"/> — the helper
/// that shapes prior-page form values into an <see cref="IdCardLayoutConfig"/>
/// for the review card. Feature 107 Assured Identity v1 (T021).
/// </summary>
public class ReviewSummaryDataSourceTests
{
    private static XReviewExtension CitizenReview() => new()
    {
        Layout = XReviewLayoutVariant.IdCard,
        Editable = true,
        Header = new XReviewHeader
        {
            IssuerName = "Acme Verification Co.",
            CredentialName = "Assured Identity",
            ColourTheme = XReviewColourTheme.IdentityNavy
        }
    };

    private static IReadOnlyList<BlueprintPageDefinition> AssuredIdentityPages() =>
    [
        new BlueprintPageDefinition
        {
            Title = "About you",
            Sections =
            [
                new BlueprintSectionDefinition { Title = "Your name", Fields = ["givenName", "familyName"] },
                new BlueprintSectionDefinition { Title = "Your date of birth", Fields = ["dateOfBirth"] }
            ]
        },
        new BlueprintPageDefinition
        {
            Title = "Your address",
            Sections = [new BlueprintSectionDefinition { Title = "Address", Fields = ["address"] }]
        }
    ];

    private static FormContext ContextWithValues()
    {
        var ctx = new FormContext();
        ctx.FormData["/givenName"] = "Alex";
        ctx.FormData["/familyName"] = "MacLeod";
        ctx.FormData["/dateOfBirth"] = "1995-07-14";
        ctx.FormData["/address/line1"] = "12 Castle St";
        ctx.FormData["/address/town"] = "Edinburgh";
        return ctx;
    }

    [Fact]
    public void BuildConfig_HeaderPopulatedFromExtension()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.CitizenDraft, AssuredIdentityPages());

        cfg.IssuerName.Should().Be("Acme Verification Co.");
        cfg.CredentialName.Should().Be("Assured Identity");
        cfg.ColourTheme.Should().Be(XReviewColourTheme.IdentityNavy);
        cfg.Editable.Should().BeTrue();
    }

    [Fact]
    public void BuildConfig_CitizenDraftState_SetsDraftWatermark()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.CitizenDraft, AssuredIdentityPages());

        cfg.Watermark.Should().Be(IdCardWatermark.Draft);
    }

    [Fact]
    public void BuildConfig_AssessorPendingState_SetsPendingWatermark()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.AssessorPending, AssuredIdentityPages());

        cfg.Watermark.Should().Be(IdCardWatermark.Pending);
    }

    [Fact]
    public void BuildConfig_IssuedCredentialState_SetsIssuedWatermark()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.IssuedCredential, AssuredIdentityPages());

        cfg.Watermark.Should().Be(IdCardWatermark.Issued);
    }

    [Fact]
    public void BuildConfig_SectionsInheritTitlesAndPageIndices()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.CitizenDraft, AssuredIdentityPages());

        cfg.Sections.Should().HaveCount(3);
        cfg.Sections[0].Title.Should().Be("Your name");
        cfg.Sections[0].OriginatingPageIndex.Should().Be(0);
        cfg.Sections[0].FieldPointers.Should().Contain("/givenName").And.Contain("/familyName");

        cfg.Sections[1].Title.Should().Be("Your date of birth");
        cfg.Sections[1].OriginatingPageIndex.Should().Be(0);

        cfg.Sections[2].Title.Should().Be("Address");
        cfg.Sections[2].OriginatingPageIndex.Should().Be(1);
    }

    [Fact]
    public void BuildConfig_FieldValuesPulledFromFormContext()
    {
        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ContextWithValues(), ActionRuntimeState.CitizenDraft, AssuredIdentityPages());

        cfg.FieldValues["/givenName"].Should().Be("Alex");
        cfg.FieldValues["/familyName"].Should().Be("MacLeod");
        cfg.FieldValues["/dateOfBirth"].Should().Be("1995-07-14");
    }

    [Fact]
    public void BuildConfig_UnfilledFields_StoredAsNull()
    {
        var ctx = new FormContext();
        ctx.FormData["/givenName"] = "Alex";
        // familyName left unfilled

        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ctx, ActionRuntimeState.CitizenDraft, AssuredIdentityPages());

        cfg.FieldValues.Should().ContainKey("/familyName");
        cfg.FieldValues["/familyName"].Should().BeNull();
    }

    [Fact]
    public void BuildConfig_ComplexRefField_GathersFlattenedLeavesForDisplay()
    {
        // The AIAS regression: a section field is the $ref PARENT ("name"), but the renderer stores the
        // core-primitive value flattened at leaf pointers ("/name/givenName", "/name/familyName"). The
        // card looks up "/name" — which isn't in the form data — so without gathering it renders empty.
        var pages = new List<BlueprintPageDefinition>
        {
            new() { Title = "About you", Sections = [new BlueprintSectionDefinition { Title = "Your name", Fields = ["name"] }] }
        };
        var ctx = new FormContext();
        ctx.FormData["/name/givenName"] = "Alex";
        ctx.FormData["/name/familyName"] = "MacLeod";

        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ctx, ActionRuntimeState.CitizenDraft, pages);

        cfg.FieldValues["/name"].Should().Be("Alex MacLeod");
    }

    [Fact]
    public void BuildConfig_FlattenedGather_ExcludesLongTokenImageLeaves()
    {
        // The portrait section stores both a short display value and a huge base64 token image at
        // "/portrait/tokenImageBase64" — the token must NOT leak into the card's text (it's the image).
        var pages = new List<BlueprintPageDefinition>
        {
            new() { Title = "Photo", Sections = [new BlueprintSectionDefinition { Title = "Portrait", Fields = ["portrait"] }] }
        };
        var ctx = new FormContext();
        ctx.FormData["/portrait/fileName"] = "portrait.jpg";
        ctx.FormData["/portrait/tokenImageBase64"] = new string('A', 5000); // base64 blob

        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), ctx, ActionRuntimeState.CitizenDraft, pages);

        cfg.FieldValues["/portrait"].Should().Be("portrait.jpg");
        ((string?)cfg.FieldValues["/portrait"]).Should().NotContain("AAAA");
    }

    [Fact]
    public void BuildConfig_PagesWithoutSections_SkippedFromSectionList()
    {
        var pages = new List<BlueprintPageDefinition>
        {
            new() { Title = "Intro only", Sections = null },
            new() { Title = "Details", Sections = [new BlueprintSectionDefinition { Title = "Detail", Fields = ["field"] }] }
        };

        var sut = new ReviewSummaryDataSource();

        var cfg = sut.BuildConfig(CitizenReview(), new FormContext(), ActionRuntimeState.CitizenDraft, pages);

        cfg.Sections.Should().HaveCount(1);
        cfg.Sections[0].Title.Should().Be("Detail");
        cfg.Sections[0].OriginatingPageIndex.Should().Be(1);
    }
}
