// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Models;
using Sorcha.Blueprint.Models.Credentials;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Unit tests for <see cref="ExecutableDefinitionHasher"/> (Feature 142 / T004 / T011).
/// Verifies determinism, presentational-keyword stability, behavioural-change sensitivity,
/// and JSON-object-key order insensitivity.
/// </summary>
public class ExecutableDefinitionHasherTests
{
    private static readonly ExecutableDefinitionHasher Hasher = new();

    // -- helpers ----------------------------------------------------------------

    private static BlueprintModel BaseBlueprint()
    {
        return new BlueprintModel
        {
            Id = "bp-1",
            Title = "Permit Application",
            Description = "A workflow for applying for a permit.",
            Version = 1,
            Participants =
            [
                new Participant { Id = "applicant", Name = "Applicant", WalletAddress = null },
                new Participant { Id = "assessor", Name = "Assessor", WalletAddress = "ws11qassessor" },
            ],
            Actions =
            [
                new ActionModel
                {
                    Id = 1,
                    Title = "Apply",
                    Description = "Submit the application.",
                    Sender = "applicant",
                    IsStartingAction = true,
                    Disclosures =
                    [
                        new Disclosure("assessor", ["/projectName"]),
                    ],
                    Routes =
                    [
                        new Route { Id = "r1", NextActionIds = [2], IsDefault = true },
                    ],
                    DataSchemas = [SchemaWithLayout()],
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Assess",
                    Description = "Assess the application.",
                    Sender = "assessor",
                    IsStartingAction = false,
                    Disclosures =
                    [
                        new Disclosure("applicant", ["/decision"]),
                    ],
                },
            ],
        };
    }

    /// <summary>A data schema carrying both presentational and behavioural x-* keywords.</summary>
    private static JsonDocument SchemaWithLayout()
    {
        const string json = """
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """;
        return JsonDocument.Parse(json);
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    // -- determinism ------------------------------------------------------------

    [Fact]
    public void ComputeHash_SameBlueprintTwice_ReturnsIdenticalHash()
    {
        var bp = BaseBlueprint();
        var h1 = Hasher.ComputeHash(bp);
        var h2 = Hasher.ComputeHash(bp);
        h1.Should().Be(h2);
        h1.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ComputeHash_TwoEquivalentlyBuiltBlueprints_ReturnsIdenticalHash()
    {
        Hasher.ComputeHash(BaseBlueprint()).Should().Be(Hasher.ComputeHash(BaseBlueprint()));
    }

    // -- presentational keywords do NOT change the hash -------------------------

    [Fact]
    public void ComputeHash_RemovingXIntroduction_HashUnchanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().Be(baseline, "x-introduction is presentational");
    }

    [Fact]
    public void ComputeHash_ChangingXWidth_HashUnchanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "full" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().Be(baseline, "x-width is presentational");
    }

    [Theory]
    [InlineData("x-sections")]
    [InlineData("x-pages")]
    [InlineData("x-review")]
    [InlineData("x-address-lookup")]
    [InlineData("x-persona")]
    public void ComputeHash_AddingPresentationalKeyword_HashUnchanged(string keyword)
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        // Add an extra presentational keyword on the property schema.
        var schema = Parse($$"""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half", "{{keyword}}": { "added": true } },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().Be(baseline, $"{keyword} is presentational");
    }

    // -- behavioural keyword changes DO change the hash -------------------------

    [Fact]
    public void ComputeHash_ChangingXFile_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 9999999 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().NotBe(baseline, "x-file is behavioural");
    }

    [Fact]
    public void ComputeHash_AddingXCredentialOffer_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-credential-offer": { "type": "PermitCredential" },
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().NotBe(baseline, "x-credential-offer is behavioural");
    }

    [Fact]
    public void ComputeHash_AddingUnknownExtensionKeyword_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-some-unknown-thing": { "drives": "execution" },
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "string", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().NotBe(baseline, "unknown x-* keywords fail safe to behavioural");
    }

    // -- structural / executable changes DO change the hash ---------------------

    [Fact]
    public void ComputeHash_ChangingDataSchemaStructuralField_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        var schema = Parse("""
        {
          "type": "object",
          "x-introduction": "Please complete the application form.",
          "x-pages": [
            { "title": "Project", "x-sections": ["details"], "x-review": { "layout": "summary" } }
          ],
          "properties": {
            "projectName": { "type": "number", "x-width": "half" },
            "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
          },
          "required": ["projectName"]
        }
        """);
        bp.Actions[0].DataSchemas = [schema];

        Hasher.ComputeHash(bp).Should().NotBe(baseline, "changing a property type is structural");
    }

    [Fact]
    public void ComputeHash_AddingRoute_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Actions[0].Routes =
        [
            new Route { Id = "r1", NextActionIds = [2], IsDefault = true },
            new Route { Id = "r2", NextActionIds = [2], Condition = JsonNode.Parse("{\"==\":[1,1]}") },
        ];

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeHash_AddingAction_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Actions.Add(new ActionModel
        {
            Id = 3,
            Title = "Notify",
            Sender = "assessor",
            Disclosures = [new Disclosure("applicant", ["/*"])],
        });

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeHash_AddingParticipant_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Participants.Add(new Participant { Id = "reviewer", Name = "Reviewer", WalletAddress = "ws11qreviewer" });

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeHash_AddingDisclosure_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Actions[0].Disclosures =
        [
            new Disclosure("assessor", ["/projectName"]),
            new Disclosure("assessor", ["/budget"]),
        ];

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeHash_AddingCredentialRequirement_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Actions[0].CredentialRequirements =
        [
            new CredentialRequirement { Type = "IdentityCredential" },
        ];

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    [Fact]
    public void ComputeHash_AddingCalculation_HashChanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Actions[0].Calculations = new Dictionary<string, JsonNode>
        {
            ["total"] = JsonNode.Parse("{\"+\":[1,2]}")!,
        };

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    // -- presentational top-level (title/description) does NOT change the hash --

    [Fact]
    public void ComputeHash_ChangingTitleAndDescription_HashUnchanged()
    {
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Title = "Totally Different Display Title";
        bp.Description = "A reworded description that does not change execution.";
        bp.Actions[0].Title = "Renamed Apply Step";
        bp.Actions[0].Description = "Reworded.";

        Hasher.ComputeHash(bp).Should().Be(baseline, "titles/descriptions are presentational");
    }

    // -- the ORDINAL version is not part of the content address (Feature 194) ----

    [Fact]
    public void ComputeHash_ChangingTheOrdinalVersion_HashUnchanged()
    {
        // Feature 194 removed `version` from the hashed projection. The ordinal is a DISPLAY LABEL
        // — assigned from in-memory insert order and re-derived on recovery — so including it in a
        // content address is a contradiction: two blueprints that execute identically would hash
        // differently purely because someone renumbered one.
        //
        // The consequence if it were still hashed: `Blueprint.Version` is a plain settable int, so
        // an author editing it would strand every in-flight instance on the previous definition for
        // no behavioural reason at all — the exact harm Story 4 exists to prevent — and would
        // re-lock the F142 rehearsal gate at the same time.
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Version = 47;

        Hasher.ComputeHash(bp).Should().Be(baseline,
            "the ordinal version says nothing about how the blueprint executes");
    }

    // Feature 195 — the VersionMajor/VersionMinor test is DELETED with the fields it covered.
    // Both were written by two call sites and read by nothing; a test asserting that a dead field
    // does not affect the hash proves only that the field is dead.

    [Fact]
    public void ComputeHash_ChangingTheBlueprintId_HashChanged()
    {
        // The counterweight to the two above: identity IS structural. Without this, "the ordinal is
        // not hashed" could quietly become "nothing at the top level is hashed", and two different
        // blueprints would share a pin.
        var baseline = Hasher.ComputeHash(BaseBlueprint());

        var bp = BaseBlueprint();
        bp.Id = "a-completely-different-blueprint";

        Hasher.ComputeHash(bp).Should().NotBe(baseline);
    }

    // -- JSON object-key order insensitivity ------------------------------------

    [Fact]
    public void ComputeHash_ReorderingJsonObjectKeysInDataSchema_HashUnchanged()
    {
        var bpA = BaseBlueprint();
        bpA.Actions[0].DataSchemas =
        [
            Parse("""
            {
              "type": "object",
              "properties": {
                "projectName": { "type": "string" },
                "evidence": { "type": "string", "x-file": { "maxBytes": 1048576 } }
              },
              "required": ["projectName"]
            }
            """),
        ];

        var bpB = BaseBlueprint();
        bpB.Actions[0].DataSchemas =
        [
            Parse("""
            {
              "required": ["projectName"],
              "properties": {
                "evidence": { "x-file": { "maxBytes": 1048576 }, "type": "string" },
                "projectName": { "type": "string" }
              },
              "type": "object"
            }
            """),
        ];

        Hasher.ComputeHash(bpA).Should().Be(Hasher.ComputeHash(bpB),
            "semantically-equal schemas with reordered object keys must hash identically");
    }
}
