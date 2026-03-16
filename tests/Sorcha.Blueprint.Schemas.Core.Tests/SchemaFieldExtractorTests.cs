// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Schemas.Client;

namespace Sorcha.Blueprint.Schemas.Core.Tests;

public class SchemaFieldExtractorTests
{
    [Fact]
    public void ExtractFields_FlatProperties_ReturnsFlatList()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "integer" }
            },
            "required": ["name"]
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().HaveCount(2);
        fields.Should().Contain(f => f.Path == "name" && f.Type == "string" && f.IsRequired);
        fields.Should().Contain(f => f.Path == "age" && f.Type == "integer" && !f.IsRequired);
    }

    [Fact]
    public void ExtractFields_NestedObject_ProducesFlattened()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "address": {
                    "type": "object",
                    "properties": {
                        "street": { "type": "string" },
                        "city": { "type": "string" }
                    },
                    "required": ["street"]
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().HaveCount(3);
        fields.Should().Contain(f => f.Path == "address" && f.Type == "object");
        fields.Should().Contain(f => f.Path == "address.street" && f.IsRequired && f.Depth == 1);
        fields.Should().Contain(f => f.Path == "address.city" && !f.IsRequired && f.Depth == 1);
    }

    [Fact]
    public void ExtractFields_DeepNesting_HandlesMultipleLevels()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "level1": {
                    "type": "object",
                    "properties": {
                        "level2": {
                            "type": "object",
                            "properties": {
                                "level3": { "type": "string" }
                            }
                        }
                    }
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().Contain(f => f.Path == "level1.level2.level3" && f.Depth == 2);
    }

    [Fact]
    public void ExtractFields_ArrayOfObjects_ExtractsItemFields()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "items": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "id": { "type": "string" },
                            "value": { "type": "number" }
                        }
                    }
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().Contain(f => f.Path == "items" && f.Type == "array");
        fields.Should().Contain(f => f.Path == "items[].id");
        fields.Should().Contain(f => f.Path == "items[].value");
    }

    [Fact]
    public void ExtractFields_WithRefResolution_ExpandsDefinitions()
    {
        var schema = """
        {
            "type": "object",
            "$defs": {
                "Address": {
                    "type": "object",
                    "properties": {
                        "street": { "type": "string" },
                        "zip": { "type": "string" }
                    }
                }
            },
            "properties": {
                "home": { "$ref": "#/$defs/Address" }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().Contain(f => f.Path == "home" && f.Type == "object");
        fields.Should().Contain(f => f.Path == "home.street");
        fields.Should().Contain(f => f.Path == "home.zip");
    }

    [Fact]
    public void ExtractFields_EnumValues_Extracted()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "status": {
                    "type": "string",
                    "enum": ["active", "inactive", "pending"]
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var statusField = fields.Should().ContainSingle(f => f.Path == "status").Subject;
        statusField.EnumValues.Should().BeEquivalentTo("active", "inactive", "pending");
    }

    [Fact]
    public void ExtractFields_Constraints_Extracted()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "age": {
                    "type": "integer",
                    "minimum": 0,
                    "maximum": 150
                },
                "name": {
                    "type": "string",
                    "minLength": 1,
                    "maxLength": 100,
                    "pattern": "^[A-Za-z]+"
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var ageField = fields.Should().ContainSingle(f => f.Path == "age").Subject;
        ageField.Minimum.Should().Be(0);
        ageField.Maximum.Should().Be(150);

        var nameField = fields.Should().ContainSingle(f => f.Path == "name").Subject;
        nameField.MinLength.Should().Be(1);
        nameField.MaxLength.Should().Be(100);
        nameField.Pattern.Should().Be("^[A-Za-z]+");
    }

    [Fact]
    public void ExtractFields_FormatAndDescription_Extracted()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "email": {
                    "type": "string",
                    "format": "email",
                    "description": "User email address"
                },
                "created": {
                    "type": "string",
                    "format": "date-time"
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var emailField = fields.Should().ContainSingle(f => f.Path == "email").Subject;
        emailField.Format.Should().Be("email");
        emailField.Description.Should().Be("User email address");

        var createdField = fields.Should().ContainSingle(f => f.Path == "created").Subject;
        createdField.Format.Should().Be("date-time");
    }

    [Fact]
    public void ExtractFields_EmptySchema_ReturnsEmpty()
    {
        var schema = """{ "type": "object" }""";

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFields_NoProperties_ReturnsEmpty()
    {
        var schema = """{ "type": "string" }""";

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFields_AlreadyDraft202012_Works()
    {
        var schema = """
        {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "properties": {
                "id": { "type": "string" }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        fields.Should().ContainSingle(f => f.Path == "id");
    }

    [Fact]
    public void ExtractFields_MaxDepthRespected()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "a": {
                    "type": "object",
                    "properties": {
                        "b": {
                            "type": "object",
                            "properties": {
                                "c": { "type": "string" }
                            }
                        }
                    }
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema, maxDepth: 1);

        fields.Should().Contain(f => f.Path == "a");
        fields.Should().Contain(f => f.Path == "a.b");
        fields.Should().NotContain(f => f.Path == "a.b.c");
    }

    [Fact]
    public void ExtractFields_Example_Extracted()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "name": {
                    "type": "string",
                    "examples": ["John", "Jane"]
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var nameField = fields.Should().ContainSingle(f => f.Path == "name").Subject;
        nameField.Example.Should().Be("John");
    }

    [Fact]
    public void ExtractFields_NameIsLastSegment()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "address": {
                    "type": "object",
                    "properties": {
                        "street": { "type": "string" }
                    }
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var streetField = fields.Should().ContainSingle(f => f.Path == "address.street").Subject;
        streetField.Name.Should().Be("street");
    }

    [Fact]
    public void ExtractFields_UnionType_JoinedWithPipe()
    {
        var schema = """
        {
            "type": "object",
            "properties": {
                "value": {
                    "type": ["string", "null"]
                }
            }
        }
        """;

        var fields = SchemaFieldExtractor.ExtractFields(schema);

        var valueField = fields.Should().ContainSingle(f => f.Path == "value").Subject;
        valueField.Type.Should().Be("string|null");
    }
}
