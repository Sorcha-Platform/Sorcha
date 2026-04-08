// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Models;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Tests;

public class SchemaLayoutParserTests
{
    private static JsonElement Parse(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    // --- Parse: Combined x-pages + x-sections ---

    [Fact]
    public void Parse_SchemaWithXPagesAndXSections_ReturnsPopulatedLayout()
    {
        // Arrange
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Project Details",
                    "description": "Basic project information",
                    "layout": "single-column",
                    "x-sections": [
                        {
                            "title": "Location",
                            "help": "Where the project is based",
                            "layout": "horizontal",
                            "fields": ["address", "postcode"]
                        }
                    ]
                },
                {
                    "title": "Budget",
                    "x-sections": [
                        {
                            "title": "Costs",
                            "layout": "grid",
                            "fields": ["materials", "labour"]
                        }
                    ]
                }
            ],
            "properties": {
                "address": { "type": "string" },
                "postcode": { "type": "string" },
                "materials": { "type": "number" },
                "labour": { "type": "number" }
            }
        }
        """);

        // Act
        var result = SchemaLayoutParser.Parse(schema);

        // Assert
        result.Should().NotBeNull();
        result.HasWizard.Should().BeTrue();
        result.HasSections.Should().BeTrue();
        result.Pages.Should().NotBeNull().And.HaveCount(2);
        result.Pages![0].Title.Should().Be("Project Details");
        result.Pages[0].Sections.Should().NotBeNull().And.HaveCount(1);
        result.Pages[0].Sections![0].Fields.Should().Equal("address", "postcode");
        result.Pages[1].Sections![0].Layout.Should().Be("grid");
    }

    // --- Parse: x-sections only (no wizard) ---

    [Fact]
    public void Parse_SchemaWithXSectionsOnly_ReturnsLayoutWithoutWizard()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-sections": [
                {
                    "title": "Contact",
                    "layout": "horizontal",
                    "fields": ["email", "phone"]
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.HasWizard.Should().BeFalse();
        result.HasSections.Should().BeTrue();
        result.Sections.Should().NotBeNull().And.HaveCount(1);
        result.Sections![0].Title.Should().Be("Contact");
        result.Sections[0].Layout.Should().Be("horizontal");
    }

    // --- Parse: No extensions at all ---

    [Fact]
    public void Parse_SchemaWithoutExtensions_ReturnsEmptyLayout()
    {
        var schema = Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            }
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Should().BeSameAs(SchemaLayoutInfo.Empty);
        result.HasWizard.Should().BeFalse();
        result.HasSections.Should().BeFalse();
        result.Introduction.Should().BeNull();
    }

    // --- Parse: x-introduction ---

    [Fact]
    public void Parse_SchemaWithXIntroduction_PopulatesIntroduction()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-introduction": "Please complete all sections of this form."
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Introduction.Should().Be("Please complete all sections of this form.");
    }

    [Fact]
    public void Parse_SchemaWithEmptyXIntroduction_IgnoresIt()
    {
        var schema = Parse("""{ "type": "object", "x-introduction": "   " }""");

        var result = SchemaLayoutParser.Parse(schema);

        result.Introduction.Should().BeNull();
    }

    // --- Parse: x-width on properties ---

    [Fact]
    public void Parse_SchemaWithXWidthOnProperties_PopulatesFieldWidths()
    {
        var schema = Parse("""
        {
            "type": "object",
            "properties": {
                "firstName": { "type": "string", "x-width": "half" },
                "lastName": { "type": "string", "x-width": "half" },
                "title": { "type": "string", "x-width": "third" },
                "description": { "type": "string", "x-width": "full" },
                "notes": { "type": "string" }
            }
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.FieldWidths.Should().NotBeNull();
        result.FieldWidths!.Should().HaveCount(4);
        result.FieldWidths["firstName"].Should().Be("half");
        result.FieldWidths["lastName"].Should().Be("half");
        result.FieldWidths["title"].Should().Be("third");
        result.FieldWidths["description"].Should().Be("full");
        result.FieldWidths.Should().NotContainKey("notes");
    }

    [Fact]
    public void Parse_SchemaWithInvalidXWidth_IgnoresInvalidValues()
    {
        var schema = Parse("""
        {
            "type": "object",
            "properties": {
                "a": { "type": "string", "x-width": "quarter" },
                "b": { "type": "string", "x-width": 42 },
                "c": { "type": "string", "x-width": "half" }
            }
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.FieldWidths.Should().NotBeNull();
        result.FieldWidths!.Should().ContainSingle().Which.Key.Should().Be("c");
    }

    // --- Parse: Malformed / defensive ---

    [Fact]
    public void Parse_MalformedXPages_ReturnsEmptyLayoutWithoutThrowing()
    {
        // x-pages is a string instead of an array
        var schema = Parse("""{ "type": "object", "x-pages": "not an array" }""");

        var result = SchemaLayoutParser.Parse(schema);

        result.HasWizard.Should().BeFalse();
    }

    [Fact]
    public void Parse_XPagesWithMissingTitles_SkipsInvalidEntries()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                { "description": "no title here" },
                { "title": "" },
                { "title": "Valid Page" }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Pages.Should().NotBeNull().And.HaveCount(1);
        result.Pages![0].Title.Should().Be("Valid Page");
    }

    [Fact]
    public void Parse_XSectionsWithEmptyFields_SkipsEntry()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-sections": [
                { "title": "Empty", "fields": [] },
                { "title": "Valid", "fields": ["a"] }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.Sections.Should().NotBeNull().And.HaveCount(1);
        result.Sections![0].Title.Should().Be("Valid");
    }

    [Fact]
    public void Parse_NonObjectRoot_ReturnsEmptyLayout()
    {
        var schema = Parse("""[1, 2, 3]""");

        var result = SchemaLayoutParser.Parse(schema);

        result.Should().BeSameAs(SchemaLayoutInfo.Empty);
    }

    // --- Parse: Nested sections inside pages ---

    [Fact]
    public void Parse_PagesContainingNestedXSections_ParsesNestedSections()
    {
        var schema = Parse("""
        {
            "type": "object",
            "x-pages": [
                {
                    "title": "Page One",
                    "x-sections": [
                        { "title": "Section A", "fields": ["a"] },
                        { "title": "Section B", "fields": ["b", "c"] }
                    ]
                }
            ]
        }
        """);

        var result = SchemaLayoutParser.Parse(schema);

        result.HasSections.Should().BeTrue();
        result.Pages![0].Sections.Should().HaveCount(2);
        result.Pages[0].Sections![1].Fields.Should().Equal("b", "c");
    }

    // --- HasWizard / HasSections computed properties ---

    [Fact]
    public void HasWizard_WithEmptyPages_ReturnsFalse()
    {
        var info = new SchemaLayoutInfo { Pages = new List<BlueprintPageDefinition>() };

        info.HasWizard.Should().BeFalse();
    }

    [Fact]
    public void HasSections_WithSectionsInsidePages_ReturnsTrue()
    {
        var info = new SchemaLayoutInfo
        {
            Pages = new List<BlueprintPageDefinition>
            {
                new()
                {
                    Title = "Page",
                    Sections = new List<BlueprintSectionDefinition>
                    {
                        new() { Title = "Section", Fields = new List<string> { "x" } }
                    }
                }
            }
        };

        info.HasSections.Should().BeTrue();
    }

    [Fact]
    public void HasSections_WithNoSectionsAnywhere_ReturnsFalse()
    {
        var info = new SchemaLayoutInfo
        {
            Pages = new List<BlueprintPageDefinition>
            {
                new() { Title = "Page", Sections = null }
            }
        };

        info.HasSections.Should().BeFalse();
    }

    // --- TryGetFieldWidth ---

    [Theory]
    [InlineData("full")]
    [InlineData("half")]
    [InlineData("third")]
    public void TryGetFieldWidth_ValidValue_ReturnsTrueAndValue(string widthValue)
    {
        var schema = Parse($$"""{ "type": "string", "x-width": "{{widthValue}}" }""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeTrue();
        width.Should().Be(widthValue);
    }

    [Theory]
    [InlineData("FULL", "full")]
    [InlineData("Half", "half")]
    [InlineData("THIRD", "third")]
    public void TryGetFieldWidth_CaseInsensitive_ReturnsLowercaseValue(string input, string expected)
    {
        var schema = Parse($$"""{ "type": "string", "x-width": "{{input}}" }""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeTrue();
        width.Should().Be(expected);
    }

    [Theory]
    [InlineData("quarter")]
    [InlineData("")]
    [InlineData("10")]
    public void TryGetFieldWidth_InvalidValue_ReturnsFalse(string widthValue)
    {
        var schema = Parse($$"""{ "type": "string", "x-width": "{{widthValue}}" }""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeFalse();
        width.Should().BeNull();
    }

    [Fact]
    public void TryGetFieldWidth_MissingProperty_ReturnsFalse()
    {
        var schema = Parse("""{ "type": "string" }""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeFalse();
        width.Should().BeNull();
    }

    [Fact]
    public void TryGetFieldWidth_NonObjectSchema_ReturnsFalse()
    {
        var schema = Parse("""[1, 2]""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeFalse();
        width.Should().BeNull();
    }

    [Fact]
    public void TryGetFieldWidth_NumericValue_ReturnsFalse()
    {
        var schema = Parse("""{ "type": "string", "x-width": 6 }""");

        var result = SchemaLayoutParser.TryGetFieldWidth(schema, out var width);

        result.Should().BeFalse();
    }
}
