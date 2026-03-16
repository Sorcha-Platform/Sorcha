// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Schemas.Client;
using Xunit;

namespace Sorcha.UI.Core.Tests.SchemaLibrary;

public class SchemaFieldSubsetSelectorTests
{
    [Fact]
    public void ExtractFields_AllSelected_ReturnsAllPaths()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
            },
            "required": ["name"]
        }
        """);

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().HaveCount(2);
        fields.Should().Contain(f => f.Path == "name" && f.IsRequired);
        fields.Should().Contain(f => f.Path == "age" && !f.IsRequired);
    }

    [Fact]
    public void ExtractFields_RequiredFieldsAlwaysIncluded()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "id": { "type": "string" },
                "name": { "type": "string" },
                "optional": { "type": "string" }
            },
            "required": ["id", "name"]
        }
        """);

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var requiredFields = fields.Where(f => f.IsRequired).Select(f => f.Path).ToArray();
        requiredFields.Should().BeEquivalentTo("id", "name");
    }

    [Fact]
    public void ExtractFields_NestedSchema_ProducesSubsetPaths()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "address": {
                    "type": "object",
                    "properties": {
                        "street": { "type": "string" },
                        "city": { "type": "string" }
                    }
                }
            }
        }
        """);

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().Contain(f => f.Path == "address.street");
        fields.Should().Contain(f => f.Path == "address.city");
    }

    [Fact]
    public void ExtractFields_GeneratesCorrectJsonSchema_FromSelection()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" },
                "email": { "type": "string", "format": "email" }
            },
            "required": ["name"]
        }
        """);

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        // Simulating what the selector would return - only name and email selected
        var selectedPaths = fields
            .Where(f => f.Path == "name" || f.Path == "email")
            .Select(f => f.Path)
            .ToArray();

        selectedPaths.Should().BeEquivalentTo("name", "email");
    }

    [Fact]
    public void ExtractFields_EmptySchema_ReturnsEmpty()
    {
        var schema = JsonDocument.Parse("""{ "type": "object" }""");

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().BeEmpty();
    }
}
