// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Sorcha.Blueprint.Models.Forms;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Forms.Authoring;

/// <summary>
/// T047 [US5] — verify the shared <see cref="FormLayoutWriter"/> writes ONLY presentational
/// <c>x-*</c> keywords, and that each writer produces exactly the keyword we asserted via
/// <see cref="FormKeywordClassifier"/>. This is the structural contract that keeps FR-023
/// honest: presentational edits don't re-lock the rehearsal gate because they don't touch
/// the executable-definition hash.
/// </summary>
public class FormLayoutWriterTests
{
    private static JsonDocument MinimalSchema() => JsonDocument.Parse(
        """
        {
            "type": "object",
            "properties": {
                "email": { "type": "string", "format": "email", "title": "Email" },
                "name":  { "type": "string", "title": "Name" }
            }
        }
        """);

    [Fact]
    public void SetSections_WritesXSections_AndIsPresentational()
    {
        var schema = MinimalSchema();
        var sections = new JsonArray
        {
            new JsonObject
            {
                ["title"] = "Personal",
                ["fields"] = new JsonArray { "name", "email" },
            },
        };

        var result = FormLayoutWriter.SetSections(schema, sections);

        result.RootElement.TryGetProperty("x-sections", out var written).Should().BeTrue();
        written.ValueKind.Should().Be(JsonValueKind.Array);
        FormKeywordClassifier.IsPresentational("x-sections").Should().BeTrue();
    }

    [Fact]
    public void SetPages_WritesXPages_AndIsPresentational()
    {
        var schema = MinimalSchema();
        var pages = new JsonArray
        {
            new JsonObject
            {
                ["title"] = "Step 1",
                ["sections"] = new JsonArray
                {
                    new JsonObject { ["title"] = "About", ["fields"] = new JsonArray { "name" } },
                },
            },
            new JsonObject
            {
                ["title"] = "Step 2",
                ["sections"] = new JsonArray
                {
                    new JsonObject { ["title"] = "Contact", ["fields"] = new JsonArray { "email" } },
                },
            },
        };

        var result = FormLayoutWriter.SetPages(schema, pages);

        result.RootElement.TryGetProperty("x-pages", out var written).Should().BeTrue();
        written.GetArrayLength().Should().Be(2);
        FormKeywordClassifier.IsPresentational("x-pages").Should().BeTrue();
    }

    [Fact]
    public void SetFieldWidth_WritesXWidth_OnPropertyAndIsPresentational()
    {
        var schema = MinimalSchema();

        var result = FormLayoutWriter.SetFieldWidth(schema, "/email", "half");

        var email = result.RootElement.GetProperty("properties").GetProperty("email");
        email.TryGetProperty("x-width", out var width).Should().BeTrue();
        width.GetString().Should().Be("half");
        FormKeywordClassifier.IsPresentational("x-width").Should().BeTrue();
    }

    [Fact]
    public void SetFieldPersona_WritesXPersona_OnPropertyAndIsPresentational()
    {
        var schema = MinimalSchema();

        var result = FormLayoutWriter.SetFieldPersona(schema, "/email", "email");

        var email = result.RootElement.GetProperty("properties").GetProperty("email");
        email.TryGetProperty("x-persona", out var persona).Should().BeTrue();
        persona.GetString().Should().Be("email");
        FormKeywordClassifier.IsPresentational("x-persona").Should().BeTrue();
    }

    [Fact]
    public void SetFieldPersona_NullKey_ClearsExistingBinding()
    {
        var schema = MinimalSchema();
        var bound = FormLayoutWriter.SetFieldPersona(schema, "/email", "email");

        var cleared = FormLayoutWriter.SetFieldPersona(bound, "/email", null);

        var email = cleared.RootElement.GetProperty("properties").GetProperty("email");
        email.TryGetProperty("x-persona", out _).Should().BeFalse();
    }

    [Fact]
    public void SetReviewPage_MarksOnlyTargetPageWithXReview()
    {
        var schema = MinimalSchema();
        var withPages = FormLayoutWriter.SetPages(schema, new JsonArray
        {
            new JsonObject { ["title"] = "One" },
            new JsonObject { ["title"] = "Two" },
            new JsonObject { ["title"] = "Three" },
        });

        var result = FormLayoutWriter.SetReviewPage(withPages, pageIndex: 2);

        var pages = result.RootElement.GetProperty("x-pages");
        pages[0].TryGetProperty("x-review", out _).Should().BeFalse();
        pages[1].TryGetProperty("x-review", out _).Should().BeFalse();
        pages[2].TryGetProperty("x-review", out var review).Should().BeTrue();
        review.GetProperty("layout").GetString().Should().Be("id-card");
        FormKeywordClassifier.IsPresentational("x-review").Should().BeTrue();
    }

    [Fact]
    public void SetIntroduction_WritesXIntroduction_AndIsPresentational()
    {
        var schema = MinimalSchema();

        var result = FormLayoutWriter.SetIntroduction(schema, "Please answer carefully.");

        result.RootElement.GetProperty("x-introduction").GetString().Should().Be("Please answer carefully.");
        FormKeywordClassifier.IsPresentational("x-introduction").Should().BeTrue();
    }

    [Fact]
    public void SetAddressLookup_WritesXAddressLookup_AndIsPresentational()
    {
        var schema = MinimalSchema();

        var result = FormLayoutWriter.SetAddressLookup(schema, "/name", true);

        var name = result.RootElement.GetProperty("properties").GetProperty("name");
        name.GetProperty("x-address-lookup").GetBoolean().Should().BeTrue();
        FormKeywordClassifier.IsPresentational("x-address-lookup").Should().BeTrue();
    }

    [Fact]
    public void EveryWrittenKeyword_IsClassifiedPresentational_NotBehavioural()
    {
        // The structural promise: every keyword FormLayoutWriter touches is presentational.
        // If a future contributor adds a behavioural writer, this test fails loudly.
        var presentational = new[]
        {
            "x-sections", "x-pages", "x-width", "x-persona", "x-review",
            "x-introduction", "x-address-lookup",
        };
        foreach (var k in presentational)
        {
            FormKeywordClassifier.IsPresentational(k).Should().BeTrue($"{k} is what the writer touches");
            FormKeywordClassifier.IsBehavioural(k).Should().BeFalse($"{k} must not re-lock the gate");
        }
    }

    [Fact]
    public void SetFormLayout_BulkApply_ProducesByteEquivalentToSequentialCalls()
    {
        // Convergence guard between the SetFormLayout aggregate (what set_form_layout chat tool
        // will call) and equivalent sequential single-purpose calls (what the LayoutToolsPanel
        // buttons call). Byte equivalence is the FR-016 contract.
        var schema = MinimalSchema();

        var sections = new JsonArray
        {
            new JsonObject { ["title"] = "All", ["fields"] = new JsonArray { "name", "email" } },
        };
        var intro = "Hi.";
        var widths = new Dictionary<string, string> { ["/email"] = "half" };

        var bulk = FormLayoutWriter.SetFormLayout(schema, sections: sections, introduction: intro, widths: widths);

        var sequential = schema;
        sequential = FormLayoutWriter.SetSections(sequential, sections);
        sequential = FormLayoutWriter.SetIntroduction(sequential, intro);
        sequential = FormLayoutWriter.SetFieldWidth(sequential, "/email", "half");

        bulk.RootElement.GetRawText().Should().Be(sequential.RootElement.GetRawText());
    }

    [Fact]
    public void Writer_DoesNotMutateInputDocument()
    {
        var schema = MinimalSchema();
        var before = schema.RootElement.GetRawText();

        _ = FormLayoutWriter.SetSections(schema, new JsonArray { new JsonObject { ["title"] = "x", ["fields"] = new JsonArray { "name" } } });
        _ = FormLayoutWriter.SetIntroduction(schema, "hi");
        _ = FormLayoutWriter.SetFieldWidth(schema, "/name", "third");

        schema.RootElement.GetRawText().Should().Be(before, "FormLayoutWriter must be pure — inputs are immutable.");
    }

    [Fact]
    public void SetFieldWidth_OnUnknownProperty_Throws()
    {
        var schema = MinimalSchema();
        var act = () => FormLayoutWriter.SetFieldWidth(schema, "/missing", "half");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*missing*");
    }
}
