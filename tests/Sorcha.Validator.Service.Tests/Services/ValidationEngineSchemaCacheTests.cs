// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Json.Schema;
using Sorcha.Validator.Service.Services;
using Xunit;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Regression coverage for the "Overwriting registered schemas is not permitted"
/// failure first observed in Feature 103 wave 13 follow-up when a second citizen
/// attempted to submit the Verified Citizen Action 1 against a validator that had
/// already processed Alice's transaction.
/// <para>
/// The root cause: <c>SchemaRefResolver</c> flattens core identity primitives with
/// stable <c>$id</c> URIs into blueprint action schemas. JsonSchema.Net's
/// <see cref="JsonSchema.FromText(string)"/> eagerly registers any sub-schema with an
/// <c>$id</c> in the process-global <c>SchemaRegistry.Global</c>, so the second parse
/// of the same flattened schema throws.
/// </para>
/// <para>
/// <see cref="ValidationEngine"/> now routes action schema parsing through an internal
/// content-addressed cache (<c>GetOrParseActionSchema</c>) so <see cref="JsonSchema.FromText(string)"/>
/// is called exactly once per unique schema text within a validator process lifetime.
/// </para>
/// </summary>
public class ValidationEngineSchemaCacheTests
{
    /// <summary>
    /// Uses a unique <c>$id</c> per test run so repeat runs of the same test in the
    /// same process don't collide with each other's global registry entries.
    /// </summary>
    private static string MakeFlattenedSchemaWithIdPrimitive(string uniqueId)
    {
        // Mirrors the shape produced by SchemaRefResolver: a wrapper object schema
        // that inlines a core primitive carrying a stable $id.
        return $$"""
        {
          "type": "object",
          "properties": {
            "name": {
              "$id": "https://schemas.sorcha.dev/test/{{uniqueId}}",
              "type": "object",
              "properties": {
                "givenName": { "type": "string" },
                "familyName": { "type": "string" }
              },
              "required": ["givenName", "familyName"]
            }
          },
          "required": ["name"]
        }
        """;
    }

    [Fact]
    public void JsonSchemaFromText_CalledTwiceWithSameIdSchema_ThrowsOnSecondCall_RegressionProof()
    {
        // This test PROVES the underlying JsonSchema.Net behaviour the cache exists
        // to work around. If this test ever starts failing because FromText becomes
        // idempotent, the cache could potentially be simplified — but the cache still
        // saves re-parsing cost, so it stays.
        var unique = Guid.NewGuid().ToString("N");
        var schemaText = MakeFlattenedSchemaWithIdPrimitive(unique);

        var firstParse = () => JsonSchema.FromText(schemaText);
        var secondParse = () => JsonSchema.FromText(schemaText);

        firstParse.Should().NotThrow("the first parse seeds the global SchemaRegistry");
        secondParse.Should().Throw<Exception>()
            .Where(ex => ex.Message.Contains("Overwriting registered schemas is not permitted"),
                "JsonSchema.Net rejects duplicate registrations by design");
    }

    [Fact]
    public void GetOrParseActionSchema_CalledTwiceWithSameSchemaText_ReturnsCachedInstanceWithoutThrowing()
    {
        // The cache's primary job: calling FromText exactly once per unique schema
        // text so Feature 103 v2 blueprints (which flatten core primitives with $ids)
        // survive repeated validation across multiple submitters.
        var unique = Guid.NewGuid().ToString("N");
        var schemaText = MakeFlattenedSchemaWithIdPrimitive(unique);

        var first = ValidationEngine.GetOrParseActionSchema(schemaText);
        var second = ValidationEngine.GetOrParseActionSchema(schemaText);

        first.Should().NotBeNull();
        second.Should().BeSameAs(first,
            "cache should return the identical parsed instance on repeat calls");
    }

    [Fact]
    public void GetOrParseActionSchema_DifferentSchemaText_GetsFreshParse()
    {
        var unique1 = Guid.NewGuid().ToString("N");
        var unique2 = Guid.NewGuid().ToString("N");
        var schemaA = MakeFlattenedSchemaWithIdPrimitive(unique1);
        var schemaB = MakeFlattenedSchemaWithIdPrimitive(unique2);

        var parsedA = ValidationEngine.GetOrParseActionSchema(schemaA);
        var parsedB = ValidationEngine.GetOrParseActionSchema(schemaB);

        parsedA.Should().NotBeSameAs(parsedB,
            "different schema text must yield a fresh cache entry so blueprint republishes with changed content get reparsed");
    }
}
