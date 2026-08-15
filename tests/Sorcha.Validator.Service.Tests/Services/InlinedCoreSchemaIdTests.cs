// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// A blueprint action schema may <c>$ref</c> a Sorcha core primitive
/// (<c>https://schemas.sorcha.dev/core/PersonName/v1</c>). Blueprint Service's
/// <c>SchemaRefResolver.Flatten</c> INLINES that primitive into the action schema before the
/// blueprint is hashed and sealed — and it copies every key of the component, <c>$id</c> included.
/// The sealed schema therefore carries an absolute <c>$id</c> on an embedded subschema.
///
/// JsonSchema.Net registers every identified subschema it parses. Parsing a SECOND, textually
/// different schema that embeds the SAME <c>$id</c> throws "Overwriting registered schemas is not
/// permitted" — which the validator reports as VAL_SCHEMA_005 "Malformed JSON schema", pointing the
/// operator at the blueprint rather than at the collision.
///
/// This bites the moment two blueprints share a core primitive, or one blueprint is republished with
/// any textual change at all: the first parse succeeds and every later one fails, for the life of the
/// validator process. Found on n1 by the AssuredIdentity walkthrough (#1427), where action 1 inlines
/// four core primitives and every submission was rejected unsealed.
/// </summary>
public class InlinedCoreSchemaIdTests
{
    /// <summary>
    /// The shape SchemaRefResolver actually emits: the core primitive's own <c>$id</c> and
    /// <c>$schema</c> copied into the property site.
    /// </summary>
    private static string ActionSchemaEmbeddingPersonName(string extraTitle) => $$"""
    {
      "type": "object",
      "title": "{{extraTitle}}",
      "properties": {
        "applicantName": {
          "$id": "https://schemas.sorcha.dev/core/PersonName/v1",
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "givenName": { "type": "string" },
            "familyName": { "type": "string" }
          },
          "required": ["givenName", "familyName"]
        }
      },
      "required": ["applicantName"]
    }
    """;

    /// <summary>
    /// Exactly the production sequence at ValidationEngine.ValidateSchema — strip, then parse.
    /// Testing the two together is the point: the strip is what makes the parse safe, and a test
    /// that called the parser alone would prove nothing about the path the validator runs.
    /// </summary>
    private static Json.Schema.JsonSchema ParseAsValidatorDoes(string schemaJson)
    {
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(schemaJson);
        return ValidationEngine.GetOrParseActionSchema(
            ValidationEngine.StripCustomExtensionKeywords(element));
    }

    [Fact]
    public void TwoSchemasEmbeddingTheSameCoreId_BothParse()
    {
        // Two different blueprints (or two revisions of one) that both inline PersonName. Nothing
        // about the second is malformed — it embeds the same core primitive the first did, which is
        // the entire point of a shared primitive library.
        var first = ParseAsValidatorDoes(ActionSchemaEmbeddingPersonName("Identity Application"));
        first.Should().NotBeNull();

        var parseSecond = () => ParseAsValidatorDoes(ActionSchemaEmbeddingPersonName("Licence Application"));

        parseSecond.Should().NotThrow(
            "a core primitive is meant to be reused across blueprints; the second blueprint to inline "
            + "it must not be rejected as malformed");
    }

    [Fact]
    public void SchemaEmbeddingACoreId_StillValidatesPayloads()
    {
        // Guards the fix from degenerating into "swallow the exception": dropping the identifier
        // must not drop the constraints the identified subschema carries.
        var schema = ParseAsValidatorDoes(ActionSchemaEmbeddingPersonName("Evaluates Correctly"));

        var valid = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{ "applicantName": { "givenName": "Alex", "familyName": "MacLeod" } }""");
        var invalid = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{ "applicantName": { "givenName": "Alex" } }""");

        schema.Evaluate(valid).IsValid.Should().BeTrue();
        schema.Evaluate(invalid).IsValid.Should().BeFalse("familyName is required by the inlined primitive");
    }

    [Fact]
    public void RootLevelIdIsAlsoStripped_SoTwoBlueprintsMaySharaACatalogueSchema()
    {
        // Catalogue-derived schemas (DppSchemaProvider, FhirSchemaProvider, Iso20022Provider,
        // SchemaOrgProvider) set $id at the ROOT to their source URL. Two blueprints built from the
        // same catalogue entry collide the same way an inlined primitive does.
        const string catalogueSchema = """
        {
          "$id": "https://hl7.org/fhir/StructureDefinition/Patient",
          "type": "object",
          "properties": { "id": { "type": "string" } }
        }
        """;
        const string catalogueSchemaOtherBlueprint = """
        {
          "$id": "https://hl7.org/fhir/StructureDefinition/Patient",
          "type": "object",
          "title": "Second blueprint using the same catalogue entry",
          "properties": { "id": { "type": "string" } }
        }
        """;

        ParseAsValidatorDoes(catalogueSchema).Should().NotBeNull();
        var parseSecond = () => ParseAsValidatorDoes(catalogueSchemaOtherBlueprint);
        parseSecond.Should().NotThrow();
    }
}
