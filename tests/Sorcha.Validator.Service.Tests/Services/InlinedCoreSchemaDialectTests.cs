// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// A blueprint action schema that inlines a Sorcha core primitive must still declare the JSON Schema
/// dialect it is written in — otherwise the primitive's non-core keywords become fatal.
/// </summary>
/// <remarks>
/// <para>
/// <b>The regression this pins (found live on n1, 2026-08-17).</b> #1445 correctly stopped
/// <c>SchemaRefResolver</c> copying an inlined component's identity keywords into the host document,
/// because a retained <c>$id</c> collides in JsonSchema.Net's process-wide registry. But it dropped
/// <c>$schema</c> along with <c>$id</c>, and <c>$schema</c> is not only an identity keyword — it
/// declares the <b>dialect</b>. Sorcha action schemas do not declare one themselves, so the only
/// dialect declaration in the whole document was the one riding in on the inlined primitive. Remove
/// it and the document parses under a strict default where an unknown keyword is fatal:
/// </para>
/// <code>
/// VAL_SCHEMA_005: Malformed JSON schema in blueprint 'aias-assured-identity-…' action 1:
/// Unknown keywords (formatMaximum) are disallowed for this dialect.
/// </code>
/// <para>
/// <c>blueprints/schemas/sorcha-core/DateOfBirth.v1.json</c> carries <c>"formatMaximum": "today"</c>,
/// so <b>every blueprint asking for a date of birth</b> became unsubmittable — AIAS, AssuredIdentity,
/// Membership, and the AssuredIdentity walkthrough. Every submission was refused and never sealed,
/// behind an HTTP 202.
/// </para>
/// <para>
/// <b>Why #1445's own tests stayed green:</b> they embed a component carrying <c>$schema</c>, but the
/// component only ever uses core keywords, so losing the dialect changed nothing for them. A keyword
/// outside the core vocabulary is the whole point — this file supplies one.
/// </para>
/// <para>
/// The fix normalises at <i>read</i> time rather than at publish time, deliberately: blueprints
/// already sealed on a ledger cannot be re-flattened, and the dialect a document is evaluated under
/// is an evaluation concern, not part of the signed content.
/// </para>
/// </remarks>
public class InlinedCoreSchemaDialectTests
{
    /// <summary>
    /// The shape <c>SchemaRefResolver.Flatten</c> emits post-#1445: the component's body spliced in
    /// with its <c>$id</c> and <c>$schema</c> removed — and, here, carrying a non-core keyword, as
    /// the real DateOfBirth primitive does.
    /// </summary>
    private const string ActionSchemaInliningDateOfBirth = """
    {
      "type": "object",
      "title": "Assured Identity Application",
      "properties": {
        "dob": {
          "type": "object",
          "properties": {
            "dateOfBirth": {
              "type": "string",
              "format": "date",
              "formatMaximum": "today"
            }
          },
          "required": ["dateOfBirth"]
        }
      },
      "required": ["dob"]
    }
    """;

    /// <summary>
    /// Exactly the production sequence at <c>ValidationEngine.ValidateSchema</c> — strip, then parse.
    /// </summary>
    private static Json.Schema.JsonSchema ParseAsValidatorDoes(string schemaJson)
    {
        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(schemaJson);
        return ValidationEngine.GetOrParseActionSchema(
            ValidationEngine.StripCustomExtensionKeywords(element));
    }

    [Fact]
    public void SchemaCarryingANonCoreKeyword_Parses()
    {
        var parse = () => ParseAsValidatorDoes(ActionSchemaInliningDateOfBirth);

        parse.Should().NotThrow(
            "an inlined core primitive may carry Sorcha keywords such as formatMaximum; losing the "
            + "dialect declaration must not turn them into a malformed-schema rejection");
    }

    [Fact]
    public void SchemaCarryingANonCoreKeyword_StillValidatesPayloads()
    {
        // Guards the fix from degenerating into "tolerate everything": declaring the dialect must not
        // cost us the constraints the schema does express.
        var schema = ParseAsValidatorDoes(ActionSchemaInliningDateOfBirth);

        var valid = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{ "dob": { "dateOfBirth": "1980-04-01" } }""");
        var missing = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{ "dob": { } }""");

        schema.Evaluate(valid).IsValid.Should().BeTrue();
        schema.Evaluate(missing).IsValid.Should().BeFalse("dateOfBirth is required");
    }

    [Fact]
    public void ASchemaThatDeclaresItsOwnDialect_KeepsIt()
    {
        // Normalisation must fill a gap, never overwrite an explicit declaration — a schema written
        // against a different draft must not be silently re-dialected.
        const string declaresDraft7 = """
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "properties": { "id": { "type": "string" } }
        }
        """;

        var element = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(declaresDraft7);
        var stripped = ValidationEngine.StripCustomExtensionKeywords(element);

        stripped.Should().Contain("draft-07",
            "the document already said which dialect it is written in; normalisation only supplies a "
            + "missing declaration");
    }
}
