// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Testing;
using BpModels = Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Tests.Testing;

/// <summary>
/// #1724 — the Designer's Rehearse stage must submit schema-valid generated data for every step
/// instead of an empty payload. These tests cover <see cref="SchemaSamplePayloadGenerator"/>
/// construct-by-construct against the real <see cref="SchemaValidator"/> — the same validator
/// <c>ActionSchemaValidation</c> (CLAUDE.md pattern 24) applies to a live submission.
/// </summary>
public class SchemaSamplePayloadGeneratorTests
{
    private static BpModels.Action PublishedShapeAction(string schemaJson) => new()
    {
        Id = 1,
        Title = "Test action",
        DataSchemas = [JsonDocument.Parse(schemaJson)],
        // Form deliberately left at its default (layout-only, null Schema) — the shape a
        // published blueprint actually has.
    };

    private static readonly SchemaValidator Validator = new();

    private static bool PassesEverySchema(BpModels.Action action, Dictionary<string, object> payload)
    {
        foreach (var schema in ActionDataSchemasAsNodes(action))
        {
            var result = Validator.ValidateAsync(payload, schema).GetAwaiter().GetResult();
            if (!result.IsValid)
            {
                return false;
            }
        }
        return true;
    }

    private static IEnumerable<System.Text.Json.Nodes.JsonNode> ActionDataSchemasAsNodes(BpModels.Action action) =>
        action.DataSchemas!.Select(d => System.Text.Json.Nodes.JsonNode.Parse(d.RootElement.GetRawText())!);

    // ---- object / properties / required / nested objects --------------------------------------

    [Fact]
    public void Generate_ObjectWithNestedRequiredProperties_ProducesValidPayload()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "applicant": {
              "type": "object",
              "properties": {
                "name": { "type": "string", "minLength": 1 },
                "age": { "type": "integer", "minimum": 18, "maximum": 99 }
              },
              "required": ["name", "age"]
            },
            "notes": { "type": "string" }
          },
          "required": ["applicant"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 1);

        result.IsValid.Should().BeTrue();
        result.Unsatisfied.Should().BeEmpty();
        PassesEverySchema(action, result.Payload).Should().BeTrue();
        result.Payload.Should().ContainKey("applicant");
        var applicant = result.Payload["applicant"].Should().BeOfType<Dictionary<string, object>>().Subject;
        applicant.Should().ContainKey("name");
        applicant.Should().ContainKey("age");
    }

    // ---- enum / const ---------------------------------------------------------------------

    [Fact]
    public void Generate_EnumAndConst_ProducesOneOfTheDeclaredValues()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["approved", "declined", "pending"] },
            "kind":   { "const": "application" }
          },
          "required": ["status", "kind"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 2);

        result.IsValid.Should().BeTrue();
        result.Payload["status"].Should().BeOneOf("approved", "declined", "pending");
        result.Payload["kind"].Should().Be("application");
    }

    // ---- strings: minLength/maxLength + formats ------------------------------------------------

    [Theory]
    [InlineData("date")]
    [InlineData("date-time")]
    [InlineData("time")]
    [InlineData("email")]
    [InlineData("uri")]
    [InlineData("uuid")]
    public void Generate_StringFormats_ProduceValidValuesForTheFormat(string format)
    {
        var schema = $$"""
        {
          "type": "object",
          "properties": { "value": { "type": "string", "format": "{{format}}" } },
          "required": ["value"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 3);

        result.IsValid.Should().BeTrue($"format '{format}' should self-check as valid");
        PassesEverySchema(action, result.Payload).Should().BeTrue();
    }

    [Fact]
    public void Generate_MinAndMaxLength_RespectsBothBounds()
    {
        const string schema = """
        {
          "type": "object",
          "properties": { "code": { "type": "string", "minLength": 6, "maxLength": 8 } },
          "required": ["code"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 4);

        result.IsValid.Should().BeTrue();
        var code = (string)result.Payload["code"];
        code.Length.Should().BeInRange(6, 8);
    }

    [Fact]
    public void Generate_FormatMaximumToday_ProducesADateOnOrBeforeToday()
    {
        // Mirrors DateOfBirth.v1's formatMaximum: "today" — must be in the past (CLAUDE.md
        // "What the validator ACTUALLY enforces": formatMaximum/formatMinimum are ENFORCED.
        const string schema = """
        {
          "type": "object",
          "properties": {
            "dateOfBirth": { "type": "string", "format": "date", "formatMaximum": "today" }
          },
          "required": ["dateOfBirth"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 5);

        result.IsValid.Should().BeTrue();
        var dob = DateOnly.ParseExact((string)result.Payload["dateOfBirth"], "yyyy-MM-dd");
        dob.Should().BeOnOrBefore(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public void Generate_FormatMinimumToday_ProducesADateOnOrAfterToday()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "appointmentDate": { "type": "string", "format": "date", "formatMinimum": "today" }
          },
          "required": ["appointmentDate"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 6);

        result.IsValid.Should().BeTrue();
        var date = DateOnly.ParseExact((string)result.Payload["appointmentDate"], "yyyy-MM-dd");
        date.Should().BeOnOrAfter(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    // ---- pattern ----------------------------------------------------------------------------

    [Fact]
    public void Generate_UkPostcodeLikePattern_ProducesAMatchingValue()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "postcode": { "type": "string", "pattern": "^[A-Z]{1,2}[0-9][0-9A-Z]? [0-9][A-Z]{2}$" }
          },
          "required": ["postcode"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 7);

        result.IsValid.Should().BeTrue();
        result.Unsatisfied.Should().BeEmpty();
        var postcode = (string)result.Payload["postcode"];
        System.Text.RegularExpressions.Regex.IsMatch(
            postcode, "^[A-Z]{1,2}[0-9][0-9A-Z]? [0-9][A-Z]{2}$").Should().BeTrue(
            $"'{postcode}' must match the declared pattern");
    }

    // ---- number / integer ------------------------------------------------------------------

    [Fact]
    public void Generate_IntegerWithBoundsAndMultipleOf_RespectsAllConstraints()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "quantity": {
              "type": "integer",
              "minimum": 10,
              "maximum": 100,
              "exclusiveMinimum": 10,
              "exclusiveMaximum": 100,
              "multipleOf": 5
            }
          },
          "required": ["quantity"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 8);

        result.IsValid.Should().BeTrue();
        var quantity = Convert.ToInt64(result.Payload["quantity"]);
        quantity.Should().BeInRange(11, 99);
        (quantity % 5).Should().Be(0);
    }

    [Fact]
    public void Generate_NumberWithMinAndMax_RespectsBounds()
    {
        const string schema = """
        {
          "type": "object",
          "properties": { "score": { "type": "number", "minimum": 0.0, "maximum": 1.0 } },
          "required": ["score"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 9);

        result.IsValid.Should().BeTrue();
        var score = Convert.ToDouble(result.Payload["score"]);
        score.Should().BeInRange(0.0, 1.0);
    }

    // ---- boolean ------------------------------------------------------------------------------

    [Fact]
    public void Generate_Boolean_ProducesABooleanValue()
    {
        const string schema = """
        {
          "type": "object",
          "properties": { "consent": { "type": "boolean" } },
          "required": ["consent"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 10);

        result.IsValid.Should().BeTrue();
        result.Payload["consent"].Should().BeOfType<bool>();
    }

    // ---- array --------------------------------------------------------------------------------

    [Fact]
    public void Generate_ArrayWithItemsAndBounds_RespectsMinMaxAndUniqueItems()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "tags": {
              "type": "array",
              "items": { "type": "string", "enum": ["a", "b", "c", "d", "e"] },
              "minItems": 2,
              "maxItems": 4,
              "uniqueItems": true
            }
          },
          "required": ["tags"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 11);

        result.IsValid.Should().BeTrue();
        var tags = result.Payload["tags"].Should().BeOfType<List<object>>().Subject;
        tags.Count.Should().BeInRange(2, 4);
        tags.Distinct().Should().HaveCount(tags.Count);
    }

    // ---- oneOf / anyOf / allOf ------------------------------------------------------------------

    [Fact]
    public void Generate_OneOf_ProducesAValueMatchingExactlyOneBranch()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "contact": {
              "oneOf": [
                { "type": "object", "properties": { "email": { "type": "string", "format": "email" } }, "required": ["email"] },
                { "type": "object", "properties": { "phone": { "type": "string", "minLength": 6 } }, "required": ["phone"] }
              ]
            }
          },
          "required": ["contact"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 12);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Generate_AllOf_MergesRequiredPropertiesFromEveryBranch()
    {
        const string schema = """
        {
          "type": "object",
          "allOf": [
            { "properties": { "firstName": { "type": "string" } }, "required": ["firstName"] },
            { "properties": { "lastName": { "type": "string" } }, "required": ["lastName"] }
          ]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 13);

        result.IsValid.Should().BeTrue();
        result.Payload.Should().ContainKey("firstName");
        result.Payload.Should().ContainKey("lastName");
    }

    // ---- required fields --------------------------------------------------------------------

    [Fact]
    public void Generate_EveryRequiredFieldIsPresent()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "reviewNote":    { "type": "string", "minLength": 1 },
            "complianceRef": { "type": "string", "minLength": 1 },
            "optionalNote":  { "type": "string" }
          },
          "required": ["reviewNote", "complianceRef"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 14);

        result.IsValid.Should().BeTrue();
        result.Payload.Should().ContainKeys("reviewNote", "complianceRef");
    }

    // ---- determinism -----------------------------------------------------------------------

    [Fact]
    public void Generate_SameSeed_ProducesTheSamePayload()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "age": { "type": "integer", "minimum": 1, "maximum": 100 },
            "ref": { "type": "string", "format": "uuid" }
          },
          "required": ["name", "age", "ref"]
        }
        """;
        var action = PublishedShapeAction(schema);
        var generator = new SchemaSamplePayloadGenerator();

        var first = generator.Generate(action, seed: 42);
        var second = generator.Generate(action, seed: 42);

        JsonSerializer.Serialize(first.Payload).Should().Be(JsonSerializer.Serialize(second.Payload));
    }

    [Fact]
    public void Generate_DifferentSeeds_GenerallyProduceDifferentPayloads()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "age": { "type": "integer", "minimum": 1, "maximum": 10000 },
            "ref": { "type": "string", "format": "uuid" }
          },
          "required": ["name", "age", "ref"]
        }
        """;
        var action = PublishedShapeAction(schema);
        var generator = new SchemaSamplePayloadGenerator();

        var payloads = Enumerable.Range(1, 5)
            .Select(seed => JsonSerializer.Serialize(generator.Generate(action, seed).Payload))
            .Distinct()
            .Count();

        payloads.Should().BeGreaterThan(1, "at least some of 5 different seeds should differ");
    }

    // ---- unsatisfiable cases: reported, never thrown -------------------------------------------

    [Fact]
    public void Generate_LookaroundRegexPattern_IsReportedAsUnsatisfiedRatherThanThrown()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "value": { "type": "string", "pattern": "^(?=.*[A-Z]).+$" }
          },
          "required": ["value"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var act = () => new SchemaSamplePayloadGenerator().Generate(action, seed: 15);

        var result = act.Should().NotThrow().Subject;
        result.Unsatisfied.Should().NotBeEmpty();
        result.Unsatisfied.Should().Contain(u => u.Path == "/value");
    }

    [Fact]
    public void Generate_FileReferenceFormat_IsReportedAsUnsatisfiedRatherThanGenerated()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "document": { "type": "string", "format": "file-reference" }
          },
          "required": ["document"]
        }
        """;
        var action = PublishedShapeAction(schema);

        var act = () => new SchemaSamplePayloadGenerator().Generate(action, seed: 16);

        var result = act.Should().NotThrow().Subject;
        result.Unsatisfied.Should().Contain(u => u.Path == "/document");
        result.Payload.Should().NotContainKey("document");
        // A required file-reference field can never be satisfied by sample data, so the overall
        // payload is correctly reported invalid rather than silently passing.
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Generate_HolderKeyField_IsReportedAsUnsatisfiedRatherThanGenerated()
    {
        const string schema = """
        {
          "type": "object",
          "properties": {
            "presentation": { "type": "string", "x-holder-key": true }
          }
        }
        """;
        var action = PublishedShapeAction(schema);

        var act = () => new SchemaSamplePayloadGenerator().Generate(action, seed: 17);

        var result = act.Should().NotThrow().Subject;
        result.Unsatisfied.Should().Contain(u => u.Path == "/presentation");
        // Optional (not in "required") — dropping it still leaves a valid payload.
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Generate_NoDataSchemasAndNoFormSchema_ReturnsEmptyValidPayload()
    {
        var action = new BpModels.Action { Id = 1, Title = "No schema" };

        var result = new SchemaSamplePayloadGenerator().Generate(action, seed: 18);

        result.IsValid.Should().BeTrue();
        result.Payload.Should().BeEmpty();
        result.Unsatisfied.Should().BeEmpty();
    }
}
