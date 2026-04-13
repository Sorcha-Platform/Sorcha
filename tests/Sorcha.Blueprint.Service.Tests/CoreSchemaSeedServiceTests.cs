// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Unit tests for <see cref="CoreSchemaSeedService"/>.
/// </summary>
/// <remarks>
/// The tests exercise the internal <c>ValidatePrimitive</c> helper with
/// handcrafted <see cref="JsonNode"/> trees. The full hosted-service disk scan
/// is better covered by integration tests against the real
/// <c>blueprints/schemas/sorcha-core/</c> directory, which this project's
/// primitives suite (loaded by the integration-test Docker stack) already
/// exercises.
/// </remarks>
public class CoreSchemaSeedServiceTests
{
    [Fact]
    public void Validate_ValidPrimitive_DoesNotThrow()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NonObjectRoot_Throws()
    {
        var node = JsonNode.Parse("\"not an object\"")!;
        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "Foo.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*top-level value must be a JSON object*");
    }

    [Fact]
    public void Validate_MissingId_Throws()
    {
        var node = BuildValidPrimitive("PersonName", "v1");
        ((JsonObject)node).Remove("$id");

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PersonName.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*missing '$id'*");
    }

    [Fact]
    public void Validate_WrongIdPrefix_Throws()
    {
        var node = BuildValidPrimitive("Foo", "v1");
        ((JsonObject)node)["$id"] = "https://example.com/core/Foo/v1";

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "Foo.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*must start with 'https://schemas.sorcha.dev/core/'*");
    }

    [Fact]
    public void Validate_IdWithTooManySegments_Throws()
    {
        var node = BuildValidPrimitive("Foo", "v1");
        ((JsonObject)node)["$id"] = "https://schemas.sorcha.dev/core/Foo/nested/v1";

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "Foo.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*path after the prefix must be '{Name}/v{N}'*");
    }

    [Fact]
    public void Validate_FileNameMismatch_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "WrongName.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*file name must be 'PostalAddress.v1.json'*");
    }

    [Fact]
    public void Validate_MissingType_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node).Remove("type");

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*missing 'type'*");
    }

    [Fact]
    public void Validate_TypeNotObject_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node)["type"] = "string";

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*'type' must be 'object'*");
    }

    [Fact]
    public void Validate_MissingTitle_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node).Remove("title");

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*missing 'title'*");
    }

    [Fact]
    public void Validate_EmptyTitle_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node)["title"] = "   ";

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*'title' must be non-empty*");
    }

    [Fact]
    public void Validate_MissingProperties_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node).Remove("properties");

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*'properties' must be present and contain at least one property*");
    }

    [Fact]
    public void Validate_EmptyProperties_Throws()
    {
        var node = BuildValidPrimitive("PostalAddress", "v1");
        ((JsonObject)node)["properties"] = new JsonObject();

        var act = () => CoreSchemaSeedService.ValidatePrimitive(node, "PostalAddress.v1.json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*'properties' must be present and contain at least one property*");
    }

    [Fact]
    public void Repository_Upsert_StoresAndRetrieves()
    {
        var repo = new InMemoryCoreSchemaRepository();
        var node = BuildValidPrimitive("DateOfBirth", "v1");

        repo.Upsert("https://schemas.sorcha.dev/core/DateOfBirth/v1", node);

        var retrieved = repo.Get("https://schemas.sorcha.dev/core/DateOfBirth/v1");
        retrieved.Should().NotBeNull();
        retrieved!["title"]!.GetValue<string>().Should().Be("Test Title");
    }

    [Fact]
    public void Repository_Get_UnknownId_ReturnsNull()
    {
        var repo = new InMemoryCoreSchemaRepository();
        repo.Get("https://schemas.sorcha.dev/core/Unknown/v1").Should().BeNull();
    }

    [Fact]
    public void Repository_GetAll_ReturnsSnapshot()
    {
        var repo = new InMemoryCoreSchemaRepository();
        repo.Upsert("a", BuildValidPrimitive("A", "v1"));
        repo.Upsert("b", BuildValidPrimitive("B", "v1"));

        var all = repo.GetAll();
        all.Count.Should().Be(2);
        all.Keys.Should().Contain(new[] { "a", "b" });
    }

    // ----- helpers -----

    private static JsonNode BuildValidPrimitive(string name, string version)
    {
        return new JsonObject
        {
            ["$id"] = $"https://schemas.sorcha.dev/core/{name}/{version}",
            ["type"] = "object",
            ["title"] = "Test Title",
            ["properties"] = new JsonObject
            {
                ["field1"] = new JsonObject
                {
                    ["type"] = "string",
                    ["title"] = "Field 1"
                }
            }
        };
    }
}
