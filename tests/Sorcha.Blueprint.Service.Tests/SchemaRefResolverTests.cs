// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Tests;

/// <summary>
/// Tests for <see cref="SchemaRefResolver"/> — the JSON Schema <c>$ref</c>
/// flattener that inlines Sorcha core identity primitives.
/// </summary>
public class SchemaRefResolverTests
{
    private const string PostalAddressUri = "https://schemas.sorcha.dev/core/PostalAddress/v1";
    private const string PersonNameUri = "https://schemas.sorcha.dev/core/PersonName/v1";
    private const string DateOfBirthUri = "https://schemas.sorcha.dev/core/DateOfBirth/v1";

    private readonly InMemoryCoreSchemaRepository _repo;
    private readonly SchemaRefResolver _sut;

    public SchemaRefResolverTests()
    {
        _repo = new InMemoryCoreSchemaRepository();
        _sut = new SchemaRefResolver(_repo, Mock.Of<ILogger<SchemaRefResolver>>());
    }

    // ----- Happy path -----

    [Fact]
    public void Flatten_SimpleRef_InlinesPrimitiveContents()
    {
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            {
              "type": "object",
              "properties": {
                "address": { "$ref": "{{PostalAddressUri}}" }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var address = flattened["properties"]!["address"]!;
        address["type"]!.GetValue<string>().Should().Be("object");
        address["title"]!.GetValue<string>().Should().Be("Postal Address");
        address["properties"]!["line1"]!["type"]!.GetValue<string>().Should().Be("string");
        address.AsObject().ContainsKey("$ref").Should().BeFalse("the $ref marker is removed after resolution");
    }

    [Fact]
    public void Flatten_DropsTheComponentsOwnIdentityKeywords()
    {
        // $id and $schema identify the PRIMITIVE AS A DOCUMENT. Once its body is spliced into a
        // property site of a different document they no longer identify anything — but they are not
        // merely redundant, they are harmful: JSON Schema implementations register every identified
        // subschema they parse in a process-wide registry, so two blueprints that inline the same
        // primitive collide. On n1 the Validator refused every AssuredIdentity submission with
        // VAL_SCHEMA_005 "Malformed JSON schema ... Overwriting registered schemas is not
        // permitted" — a blueprint that was not malformed, named as the culprit (#1427).
        //
        // The identifier is also what a reader would use to resolve a $ref INTO this subtree; there
        // is nothing left to resolve once the content is inlined.
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            {
              "type": "object",
              "properties": {
                "address": { "$ref": "{{PostalAddressUri}}" }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var address = flattened["properties"]!["address"]!.AsObject();
        address.ContainsKey("$id").Should().BeFalse("an inlined component no longer identifies a document");
        address.ContainsKey("$schema").Should().BeFalse("the dialect is declared by the host schema, not the inlined fragment");
        // The constraints must survive — dropping identity must not drop meaning.
        address["properties"]!["line1"]!["type"]!.GetValue<string>().Should().Be("string");
    }

    [Fact]
    public void Flatten_DoesNotMutateInputTree()
    {
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            { "properties": { "address": { "$ref": "{{PostalAddressUri}}" } } }
            """)!;
        var consumerJsonBefore = consumer.ToJsonString();

        _sut.Flatten(consumer);

        var consumerJsonAfter = consumer.ToJsonString();
        consumerJsonAfter.Should().Be(consumerJsonBefore, "Flatten must clone the input tree");
    }

    // ----- Layout transclusion -----

    [Fact]
    public void Flatten_ComponentLayout_TranscludesByDefault()
    {
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            { "properties": { "address": { "$ref": "{{PostalAddressUri}}" } } }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var addressSections = flattened["properties"]!["address"]!["x-sections"]!.AsArray();
        addressSections.Should().NotBeNull();
        addressSections.Count.Should().Be(3, "PostalAddress declares 3 x-sections (street, locality, country)");
    }

    [Fact]
    public void Flatten_ChildLayoutOverride_WinsOverComponent()
    {
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            {
              "properties": {
                "address": {
                  "$ref": "{{PostalAddressUri}}",
                  "x-sections": [
                    { "title": "Compact Address", "layout": "horizontal", "fields": ["line1", "town", "postcode", "country"] }
                  ]
                }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var sections = flattened["properties"]!["address"]!["x-sections"]!.AsArray();
        sections.Count.Should().Be(1, "child override wins for x-sections — the component's 3 sections are dropped");
        sections[0]!["title"]!.GetValue<string>().Should().Be("Compact Address");
    }

    [Fact]
    public void Flatten_ChildXPagesOverride_WinsOverComponent()
    {
        // Component declares x-sections; consumer overrides with a different x-pages
        // layout. Both x-pages and x-sections are layout-tier so child wins.
        var component = BuildPostalAddress();
        ((JsonObject)component)["x-pages"] = new JsonArray
        {
            new JsonObject { ["title"] = "Component Page" }
        };
        _repo.Upsert(PostalAddressUri, component);

        var consumer = JsonNode.Parse($$"""
            {
              "properties": {
                "address": {
                  "$ref": "{{PostalAddressUri}}",
                  "x-pages": [ { "title": "Consumer Page" } ]
                }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var pages = flattened["properties"]!["address"]!["x-pages"]!.AsArray();
        pages.Count.Should().Be(1);
        pages[0]!["title"]!.GetValue<string>().Should().Be("Consumer Page");
    }

    [Fact]
    public void Flatten_PerPropertyMetadata_PreservedFromComponent()
    {
        // x-persona / x-address-lookup / format / formatMaximum live on individual
        // properties INSIDE the component and must survive flattening intact.
        _repo.Upsert(PostalAddressUri, BuildPostalAddress());

        var consumer = JsonNode.Parse($$"""
            { "properties": { "address": { "$ref": "{{PostalAddressUri}}" } } }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var properties = flattened["properties"]!["address"]!["properties"]!.AsObject();
        properties["postcode"]!["x-address-lookup"]!.GetValue<bool>().Should().BeTrue();
        properties["postcode"]!["x-persona"]!.GetValue<string>().Should().Be("address.postcode");
        properties["line1"]!["x-persona"]!.GetValue<string>().Should().Be("address.line1");
    }

    // ----- Multiple refs in one schema -----

    [Fact]
    public void Flatten_MultipleRefs_AllResolved()
    {
        _repo.Upsert(PersonNameUri, BuildPersonName());
        _repo.Upsert(DateOfBirthUri, BuildDateOfBirth());

        var consumer = JsonNode.Parse($$"""
            {
              "type": "object",
              "properties": {
                "name": { "$ref": "{{PersonNameUri}}" },
                "dob":  { "$ref": "{{DateOfBirthUri}}" }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        var name = flattened["properties"]!["name"]!;
        name["properties"]!["givenName"]!["type"]!.GetValue<string>().Should().Be("string");

        var dob = flattened["properties"]!["dob"]!;
        dob["properties"]!["dateOfBirth"]!["formatMaximum"]!.GetValue<string>().Should().Be("today");
    }

    // ----- Cycle detection -----

    [Fact]
    public void Flatten_DirectCycle_Throws()
    {
        // A primitive that references itself.
        var selfReferencing = JsonNode.Parse($$"""
            {
              "$id": "{{PostalAddressUri}}",
              "type": "object",
              "title": "Self-referencing",
              "properties": {
                "nested": { "$ref": "{{PostalAddressUri}}" }
              }
            }
            """)!;
        _repo.Upsert(PostalAddressUri, selfReferencing);

        var consumer = JsonNode.Parse($$"""
            { "properties": { "address": { "$ref": "{{PostalAddressUri}}" } } }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().Throw<SchemaRefResolutionException>()
           .WithMessage("*Cycle detected*")
           .Where(ex => ex.RefUri == PostalAddressUri);
    }

    [Fact]
    public void Flatten_TransitiveCycle_Throws()
    {
        // A → B → A
        var aUri = "https://schemas.sorcha.dev/core/A/v1";
        var bUri = "https://schemas.sorcha.dev/core/B/v1";

        _repo.Upsert(aUri, JsonNode.Parse($$"""
            { "$id": "{{aUri}}", "type": "object", "title": "A", "properties": { "ref": { "$ref": "{{bUri}}" } } }
            """)!);
        _repo.Upsert(bUri, JsonNode.Parse($$"""
            { "$id": "{{bUri}}", "type": "object", "title": "B", "properties": { "ref": { "$ref": "{{aUri}}" } } }
            """)!);

        var consumer = JsonNode.Parse($$"""
            { "properties": { "x": { "$ref": "{{aUri}}" } } }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().Throw<SchemaRefResolutionException>()
           .WithMessage("*Cycle detected*");
    }

    // ----- Error paths -----

    [Fact]
    public void Flatten_UnknownUri_Throws()
    {
        var consumer = JsonNode.Parse("""
            { "properties": { "x": { "$ref": "https://schemas.sorcha.dev/core/Unknown/v1" } } }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().Throw<SchemaRefResolutionException>()
           .WithMessage("*Unknown $ref*Unknown/v1*")
           .Where(ex => ex.RefUri == "https://schemas.sorcha.dev/core/Unknown/v1");
    }

    [Fact]
    public void Flatten_DidUri_Throws()
    {
        // Reserved future URI scheme — should fail loud rather than silently no-op.
        var consumer = JsonNode.Parse("""
            { "properties": { "x": { "$ref": "did:sorcha:register:abc/schemas/Foo/v1" } } }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().Throw<SchemaRefResolutionException>()
           .WithMessage("*DID-based primitive references*not yet supported*");
    }

    [Fact]
    public void Flatten_NonSorchaInternalRef_LeftAlone()
    {
        // JSON Schema $defs / #/definitions $refs are the validator's concern,
        // not ours. The resolver must not touch them.
        var consumer = JsonNode.Parse("""
            {
              "$defs": {
                "Foo": { "type": "string" }
              },
              "properties": {
                "foo": { "$ref": "#/$defs/Foo" }
              }
            }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().NotThrow();

        var flattened = _sut.Flatten(consumer);
        flattened["properties"]!["foo"]!["$ref"]!.GetValue<string>().Should().Be("#/$defs/Foo",
            "internal $refs are passed through untouched");
    }

    [Fact]
    public void Flatten_NullInput_Throws()
    {
        var act = () => _sut.Flatten(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("rootSchema");
    }

    [Fact]
    public void Flatten_PrimitiveStoredAsArray_Throws()
    {
        // Defensive: the repository accepts any JsonNode but core primitives
        // are required to be objects. A primitive stored as an array (or
        // value) must surface as a clear SchemaRefResolutionException, not
        // a downstream cast failure.
        _repo.Upsert(PostalAddressUri, JsonNode.Parse("[]")!);

        var consumer = JsonNode.Parse($$"""
            { "properties": { "address": { "$ref": "{{PostalAddressUri}}" } } }
            """)!;

        var act = () => _sut.Flatten(consumer);
        act.Should().Throw<SchemaRefResolutionException>()
           .WithMessage("*not a JSON object*")
           .Where(ex => ex.RefUri == PostalAddressUri);
    }

    [Fact]
    public void Flatten_NestedSchemaWithNoRefs_ReturnsClone()
    {
        var consumer = JsonNode.Parse("""
            {
              "type": "object",
              "properties": {
                "name": { "type": "string", "title": "Name" },
                "age": { "type": "integer", "minimum": 0 }
              }
            }
            """)!;

        var flattened = _sut.Flatten(consumer);

        flattened.ToJsonString().Should().Be(consumer.ToJsonString(),
            "a schema with no Sorcha refs round-trips unchanged (modulo cloning)");
    }

    // ----- Test fixture builders -----

    private static JsonNode BuildPostalAddress() => JsonNode.Parse($$"""
        {
          "$id": "{{PostalAddressUri}}",
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "title": "Postal Address",
          "x-sections": [
            { "title": "Street",   "layout": "vertical",   "fields": ["line1", "line2"] },
            { "title": "Locality", "layout": "horizontal", "fields": ["town", "region", "postcode"] },
            { "title": "Country",  "layout": "vertical",   "fields": ["country"] }
          ],
          "properties": {
            "line1":    { "type": "string", "title": "Address line 1", "x-persona": "address.line1" },
            "line2":    { "type": "string", "title": "Address line 2", "x-persona": "address.line2" },
            "town":     { "type": "string", "title": "Town",           "x-persona": "address.town" },
            "region":   { "type": "string", "title": "Region",         "x-persona": "address.region" },
            "postcode": { "type": "string", "title": "Postcode",       "x-persona": "address.postcode", "x-address-lookup": true },
            "country":  { "type": "string", "title": "Country",        "x-persona": "address.country" }
          },
          "required": ["line1", "town", "postcode", "country"]
        }
        """)!;

    private static JsonNode BuildPersonName() => JsonNode.Parse($$"""
        {
          "$id": "{{PersonNameUri}}",
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "title": "Personal Name",
          "properties": {
            "givenName":  { "type": "string", "x-persona": "givenName" },
            "middleName": { "type": "string", "x-persona": "middleName" },
            "familyName": { "type": "string", "x-persona": "familyName" }
          },
          "required": ["givenName", "familyName"]
        }
        """)!;

    private static JsonNode BuildDateOfBirth() => JsonNode.Parse($$"""
        {
          "$id": "{{DateOfBirthUri}}",
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "title": "Date of Birth",
          "properties": {
            "dateOfBirth": {
              "type": "string",
              "format": "date",
              "formatMaximum": "today",
              "x-persona": "dateOfBirth"
            }
          },
          "required": ["dateOfBirth"]
        }
        """)!;
}
