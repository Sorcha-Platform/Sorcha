// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

public class SchemaFieldResolverTests
{
    private readonly SchemaFieldResolver _resolver = new();

    [Fact]
    public void ResolveFields_WithDescriptions_ReturnsFieldsWithDescriptions()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "required": ["name", "amount"],
            "properties": {
                "name": {
                    "type": "string",
                    "description": "The applicant's full name",
                    "maxLength": 100
                },
                "amount": {
                    "type": "number",
                    "description": "Requested amount in GBP",
                    "minimum": 0
                },
                "notes": {
                    "type": "string"
                }
            }
        }
        """);

        var fields = _resolver.ResolveFields([schema]);

        fields.Should().HaveCount(3);

        var name = fields.First(f => f.Name == "name");
        name.Description.Should().Be("The applicant's full name");
        name.Type.Should().Be("string");
        name.IsRequired.Should().BeTrue();
        name.Constraints.Should().ContainKey("maxLength");

        var amount = fields.First(f => f.Name == "amount");
        amount.Description.Should().Be("Requested amount in GBP");
        amount.IsRequired.Should().BeTrue();
        amount.Constraints.Should().ContainKey("minimum");

        var notes = fields.First(f => f.Name == "notes");
        notes.Description.Should().BeNull();
        notes.IsRequired.Should().BeFalse();
    }

    [Fact]
    public void ResolveFields_EmptySchema_ReturnsEmpty()
    {
        var schema = JsonDocument.Parse("""{ "type": "object" }""");

        var fields = _resolver.ResolveFields([schema]);

        fields.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFields_NullSchemas_ReturnsEmpty()
    {
        var fields = _resolver.ResolveFields(null);
        fields.Should().BeEmpty();
    }

    [Fact]
    public void ResolveFields_NestedProperties_ReturnsNestedPaths()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "address": {
                    "type": "object",
                    "properties": {
                        "street": {
                            "type": "string",
                            "description": "Street address"
                        },
                        "city": {
                            "type": "string"
                        }
                    }
                }
            }
        }
        """);

        var fields = _resolver.ResolveFields([schema]);

        fields.Should().Contain(f => f.Name == "address");
        fields.Should().Contain(f => f.Name == "address.street");
        fields.Should().Contain(f => f.Name == "address.city");

        var street = fields.First(f => f.Name == "address.street");
        street.Description.Should().Be("Street address");
    }

    [Fact]
    public void ResolveFieldNames_ReturnsPathsOnly()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "amount": { "type": "number" }
            }
        }
        """);

        var names = _resolver.ResolveFieldNames([schema]);

        names.Should().BeEquivalentTo(["name", "amount"]);
    }

    [Fact]
    public void ResolveFields_MultipleSchemas_CombinesFields()
    {
        var schema1 = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" }
            }
        }
        """);

        var schema2 = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "amount": { "type": "number" }
            }
        }
        """);

        var fields = _resolver.ResolveFields([schema1, schema2]);

        fields.Should().HaveCount(2);
        fields.Should().Contain(f => f.Name == "name");
        fields.Should().Contain(f => f.Name == "amount");
    }

    [Fact]
    public void ResolveFields_WithEnumConstraints_ExtractsEnum()
    {
        var schema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "status": {
                    "type": "string",
                    "enum": ["pending", "approved", "rejected"]
                }
            }
        }
        """);

        var fields = _resolver.ResolveFields([schema]);

        var status = fields.First(f => f.Name == "status");
        status.Constraints.Should().ContainKey("enum");
        status.Constraints!["enum"].Should().Contain("approved");
    }
}
