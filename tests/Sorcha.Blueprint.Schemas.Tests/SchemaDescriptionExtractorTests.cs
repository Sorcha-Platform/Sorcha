// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Schemas.Services;

namespace Sorcha.Blueprint.Schemas.Tests;

public class SchemaDescriptionExtractorTests
{
    private static readonly JsonDocument SampleSchema = JsonDocument.Parse("""
    {
        "type": "object",
        "properties": {
            "requestType": {
                "type": "string",
                "description": "Select the category that best matches your request."
            },
            "amount": {
                "type": "number",
                "description": "The requested amount in GBP."
            },
            "address": {
                "type": "object",
                "properties": {
                    "street": {
                        "type": "string",
                        "description": "Street name and number"
                    },
                    "city": {
                        "type": "string"
                    }
                }
            },
            "noDescription": {
                "type": "string"
            }
        }
    }
    """);

    [Fact]
    public void GetDescription_TopLevelProperty_ReturnsDescription()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/requestType", SampleSchema);
        desc.Should().Be("Select the category that best matches your request.");
    }

    [Fact]
    public void GetDescription_WithoutLeadingSlash_StillWorks()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("amount", SampleSchema);
        desc.Should().Be("The requested amount in GBP.");
    }

    [Fact]
    public void GetDescription_NestedProperty_ReturnsDescription()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/address/street", SampleSchema);
        desc.Should().Be("Street name and number");
    }

    [Fact]
    public void GetDescription_PropertyWithoutDescription_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/noDescription", SampleSchema);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_NestedPropertyWithoutDescription_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/address/city", SampleSchema);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_NonexistentProperty_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/unknown", SampleSchema);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_NullScope_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription(null, SampleSchema);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_EmptyScope_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("", SampleSchema);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_NullSchema_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/requestType", (JsonDocument?)null);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_NullSchemaCollection_ReturnsNull()
    {
        var desc = SchemaDescriptionExtractor.GetDescription("/requestType", (IEnumerable<JsonDocument>?)null);
        desc.Should().BeNull();
    }

    [Fact]
    public void GetDescription_MultipleSchemas_FindsInSecond()
    {
        var schema1 = JsonDocument.Parse("""{ "type": "object", "properties": { "a": { "type": "string" } } }""");
        var schema2 = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "b": { "type": "string", "description": "Found in second schema" }
            }
        }
        """);

        var desc = SchemaDescriptionExtractor.GetDescription("/b", new[] { schema1, schema2 });
        desc.Should().Be("Found in second schema");
    }

    [Fact]
    public void GetDescription_WithRef_ResolvesDescription()
    {
        var schemaWithRef = JsonDocument.Parse("""
        {
            "type": "object",
            "$defs": {
                "Currency": {
                    "type": "string",
                    "description": "ISO 4217 currency code"
                }
            },
            "properties": {
                "currency": { "$ref": "#/$defs/Currency" }
            }
        }
        """);

        var desc = SchemaDescriptionExtractor.GetDescription("/currency", schemaWithRef);
        desc.Should().Be("ISO 4217 currency code");
    }

    [Fact]
    public void GetDescription_SchemaWithoutProperties_ReturnsNull()
    {
        var schema = JsonDocument.Parse("""{ "type": "string" }""");
        var desc = SchemaDescriptionExtractor.GetDescription("/anything", schema);
        desc.Should().BeNull();
    }
}
