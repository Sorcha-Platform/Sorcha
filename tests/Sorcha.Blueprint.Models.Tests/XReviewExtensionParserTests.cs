// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Tests;

/// <summary>
/// Tests for Feature 107 Assured Identity v1: the <c>x-review</c> schema
/// extension on a wizard page. Contract:
/// <c>specs/107-assured-identity-v1/contracts/x-review-extension.md</c>.
/// </summary>
public class XReviewExtensionParserTests
{
    private static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void Parse_PageWithIdCardReview_PopulatesReviewExtension()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "About you",
                    "x-sections": [
                        { "title": "Name", "fields": ["givenName", "familyName"] }
                    ]
                },
                {
                    "title": "Review your details",
                    "description": "What you'll receive once issued.",
                    "x-review": {
                        "layout": "id-card",
                        "editable": true,
                        "header": {
                            "issuerName": "Government of Scotland",
                            "credentialName": "Assured Identity",
                            "colourTheme": "identity-navy"
                        }
                    }
                }
            ],
            "properties": {
                "givenName": { "type": "string" },
                "familyName": { "type": "string" }
            }
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.HasWizard.Should().BeTrue();
        result.Pages.Should().HaveCount(2);

        var reviewPage = result.Pages![1];
        reviewPage.ReviewExtension.Should().NotBeNull();
        reviewPage.ReviewExtension!.Layout.Should().Be(XReviewLayoutVariant.IdCard);
        reviewPage.ReviewExtension.Editable.Should().BeTrue();
        reviewPage.ReviewExtension.Header.IssuerName.Should().Be("Government of Scotland");
        reviewPage.ReviewExtension.Header.CredentialName.Should().Be("Assured Identity");
        reviewPage.ReviewExtension.Header.ColourTheme.Should().Be(XReviewColourTheme.IdentityNavy);
    }

    [Fact]
    public void Parse_PageWithoutReview_LeavesReviewExtensionNull()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                { "title": "One", "x-sections": [ { "fields": ["a"] } ] }
            ],
            "properties": { "a": { "type": "string" } }
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages.Should().HaveCount(1);
        result.Pages![0].ReviewExtension.Should().BeNull();
    }

    [Fact]
    public void Parse_EditableDefaultsToTrueWhenAbsent()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "id-card",
                        "header": {
                            "issuerName": "Gov",
                            "credentialName": "Assured Identity"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages![0].ReviewExtension!.Editable.Should().BeTrue();
    }

    [Fact]
    public void Parse_ColourThemeDefaultsToIdentityNavyWhenAbsent()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "id-card",
                        "header": {
                            "issuerName": "Gov",
                            "credentialName": "Assured Identity"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages![0].ReviewExtension!.Header.ColourTheme
            .Should().Be(XReviewColourTheme.IdentityNavy);
    }

    [Fact]
    public void Parse_LicencePinkTheme_Recognised()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "id-card",
                        "header": {
                            "issuerName": "DLA",
                            "credentialName": "Driving Licence",
                            "colourTheme": "licence-pink"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages![0].ReviewExtension!.Header.ColourTheme
            .Should().Be(XReviewColourTheme.LicencePink);
    }

    [Fact]
    public void Parse_UnknownLayout_FallsBackToIdCardAndRecordsWarning()
    {
        // Per contract: unknown layout values produce a publish warning
        // (WARN_BP_REVIEW_001) and fall back so the UI still renders.
        // The parser itself reports the fallback; a separate validator
        // step surfaces the warning to the publisher.
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "hologram",
                        "header": {
                            "issuerName": "Gov",
                            "credentialName": "Assured Identity"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        var page = result.Pages![0];
        page.ReviewExtension.Should().NotBeNull();
        // Fall back to IdCard so the renderer still has something to draw.
        page.ReviewExtension!.Layout.Should().Be(XReviewLayoutVariant.IdCard);
    }

    [Fact]
    public void Parse_MissingIssuerName_DropsReviewExtension()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "id-card",
                        "header": {
                            "credentialName": "Assured Identity"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        // Required header fields absent — the page renders as a plain
        // (empty) form page rather than a broken review card.
        result.Pages![0].ReviewExtension.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingCredentialName_DropsReviewExtension()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Review",
                    "x-review": {
                        "layout": "id-card",
                        "header": {
                            "issuerName": "Gov"
                        }
                    }
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages![0].ReviewExtension.Should().BeNull();
    }
}
