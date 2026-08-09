// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Json.Schema;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// The control payload the platform emits for a governance proposal, and the payload contract the
/// published blueprint declares for the action it is submitted against, must be the same thing
/// (T054 / FR-018).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this did not exist and why its absence mattered.</b> The approval payload has had a
/// bidirectional contract test since T075. The proposal payload never did — and the two sides had
/// drifted into describing entirely different shapes without anything failing, because a governance
/// transaction carries <c>Metadata["Type"] = "Control"</c> and is therefore exempt from action-schema
/// validation. The contract was decorative: declared on one side, unenforced on the other, and
/// unreadable by anyone who trusted it. Turning schema validation on (T054) without fixing it first
/// would have rejected every governance proposal on every register.
/// </para>
/// <para>
/// The assertions are <b>derived from serialisation</b>, never from a hand-written field list. A
/// hand-written list rots silently and in the same direction as the bug it is supposed to catch.
/// </para>
/// <para>
/// <b>Opaque schema nodes.</b> A declared object property with no <c>properties</c> of its own is
/// treated as an intentionally unconstrained subtree and is not descended into. Two nodes rely on
/// this: <c>validatorEntry</c>, which the shipped schema already declares that way, and
/// <c>roster</c>, a <see cref="RegisterControlRecord"/> whose contract is declared elsewhere and
/// would drift if restated here. The rule is explicit rather than incidental so that an opaque node
/// is a decision someone made, not a gap that opened quietly.
/// </para>
/// </remarks>
public sealed class GovernanceControlPayloadContractTests
{
    /// <summary>
    /// The payload exactly as <c>SubmitGovernanceControlAsync</c> builds it for a pending proposal:
    /// a <see cref="ControlTransactionPayload"/> envelope carrying the operation, with a null roster
    /// (a proposal enacts nothing) and a null <c>enactsProposalId</c> (it is not an enactment).
    /// </summary>
    /// <remarks>
    /// Every optional member of the operation is populated, so the emitted property set is the
    /// maximal one. A partially populated instance would make the "schema declares nothing the model
    /// cannot produce" direction pass vacuously.
    /// </remarks>
    private static ControlTransactionPayload ProposalPayload() => new()
    {
        Version = 1,
        Roster = null,
        EnactsProposalId = null,
        Operation = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.AddValidator,
            ProposerDid = "did:sorcha:w:ws11qproposer",
            TargetDid = "did:sorcha:w:ws11qtarget",
            TargetRole = RegisterRole.Admin,
            ApprovalSignatures = [],
            ProposedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Status = ProposalStatus.Pending,
            Justification = "Adding a second validator before the roster grows.",
            ValidatorEntry = new ValidatorRosterEntry
            {
                ValidatorId = "ws11qvalidator",
                PublicKey = "cHVibGlja2V5",
                Algorithm = SignatureAlgorithm.ED25519,
                DerivationContext = "sorcha:docket-signing",
                Status = ValidatorKeyStatus.Active,
            },
            RosterSnapshotId = "5b1f0c0e5b1f0c0e5b1f0c0e5b1f0c0e",
            QuorumFormulaAtRaise = QuorumFormula.StrictMajority,
        },
    };

    private static JsonElement EmittedProposal() =>
        JsonDocument.Parse(JsonSerializer.Serialize(
                ProposalPayload(), ControlTransactionPayload.CanonicalJsonOptions))
            .RootElement.Clone();

    private static JsonElement ProposalSchema() =>
        BlueprintAction(GovernanceBlueprint.ProposeChangeActionId)
            .GetProperty("dataSchemas")[0];

    [Fact]
    public void EveryFieldTheProposalEmits_IsDeclaredByThePublishedSchema()
    {
        // The direction that catches "the producer emits a shape the contract never described".
        // Before T054 this failed on the envelope itself: the schema described a bare
        // GovernanceOperation while the wire carries it nested under `operation`.
        AssertEmittedAreDeclared(EmittedProposal(), ProposalSchema(), "proposal");
    }

    [Fact]
    public void EveryFieldTheProposalSchemaDeclares_IsProducibleByTheModel()
    {
        // The opposite direction. This is what caught `requiresAcceptance`: declared by the schema,
        // referenced by action 1's own routing conditions, and carried by no property on any model —
        // so no conforming payload could ever have set it and every route reading it saw absent.
        AssertDeclaredAreEmitted(EmittedProposal(), ProposalSchema(), "proposal");
    }

    [Fact]
    public void EveryRequiredProposalField_IsAlwaysEmitted()
    {
        // `rosterSnapshotId` and `quorumFormulaAtRaise` are nullable on the model and omitted when
        // null, so "required" is only honest if the producer always populates them. It does — both
        // are assigned unconditionally when the proposal is raised (FR-011a).
        AssertRequiredAreEmitted(EmittedProposal(), ProposalSchema(), "proposal");
    }

    [Fact]
    public void TheProposalOperationTypeWireValues_AreExactlyWhatTheSchemaLists()
    {
        AssertEnumWireValues<GovernanceOperationType>(
            OperationSchema().GetProperty("properties").GetProperty("operationType"));
    }

    [Fact]
    public void TheQuorumFormulaWireValues_AreExactlyWhatTheSchemaLists()
    {
        AssertEnumWireValues<QuorumFormula>(
            OperationSchema().GetProperty("properties").GetProperty("quorumFormulaAtRaise"));
    }

    [Fact]
    public void TheProposalStatusWireValues_AreExactlyWhatTheSchemaLists()
    {
        AssertEnumWireValues<ProposalStatus>(
            OperationSchema().GetProperty("properties").GetProperty("status"));
    }

    [Fact]
    public void TheTargetRoleWireValues_AreExactlyWhatTheSchemaLists()
    {
        AssertEnumWireValues<RegisterRole>(
            OperationSchema().GetProperty("properties").GetProperty("targetRole"));
    }

    [Fact]
    public void TheEmittedEnumValues_AreTheStringsTheSchemaDeclares()
    {
        // The join the two set-equality tests above cannot make on their own: they compare the
        // schema against the CLR type, and this compares it against what the producer actually
        // writes. A converter moved, dropped, or given a naming policy changes the wire value while
        // leaving both other tests green — and the Register Service configures no JSON options, so
        // an enum silently reverting to its integer form is a live failure mode here, not a
        // hypothetical one.
        var operation = EmittedProposal().GetProperty("operation");

        foreach (var (property, _) in new[]
                 {
                     ("operationType", 0), ("targetRole", 0),
                     ("status", 0), ("quorumFormulaAtRaise", 0),
                 })
        {
            var emitted = operation.GetProperty(property);

            emitted.ValueKind.Should().Be(JsonValueKind.String,
                "'{0}' must travel as a string, not as its underlying integer", property);

            var declared = OperationSchema().GetProperty("properties").GetProperty(property)
                .GetProperty("enum").EnumerateArray().Select(e => e.GetString()!);

            declared.Should().Contain(emitted.GetString()!,
                "the value the producer emits for '{0}' must be one the published contract lists",
                property);
        }
    }

    [Fact]
    public void TheEmittedProposal_PassesEvaluationAgainstTheShippedSchema()
    {
        // The claim the other tests in this file cannot make. They check that the two shapes agree
        // property by property; this runs the payload through the same engine and the same
        // EvaluationOptions the Validator uses, which is what actually decides whether a governance
        // proposal seals. `RequireFormatValidation` is on there and on here — without it the
        // date-time formats would be annotations rather than constraints, and this would pass over a
        // schema that rejects every real proposal.
        //
        // JsonSchema.Net evaluates a JsonElement, never a JsonNode (CLAUDE.md pattern #4).
        var result = ValidatorSchemaEvaluation.Evaluate(ProposalSchema(), EmittedProposal());

        var failures = string.Join("; ", ValidatorSchemaEvaluation.Failures(result));

        result.IsValid.Should().BeTrue(
            "a governance proposal built exactly as the Register Service builds it must satisfy the "
            + "contract the published blueprint declares for the action it is submitted against, "
            + "otherwise every roster change on every register is refused VAL_SCHEMA_004. "
            + "Failures: {0}", failures);
    }

    [Fact]
    public void APayloadFlattenedToTheOldShape_IsRefusedByTheShippedSchema()
    {
        // The mutation proof for the test above. The schema this replaced described a bare
        // GovernanceOperation, so a flattened payload was what it asked for and the envelope was
        // what it got. If the contract had been left permissive enough to accept both, evaluation
        // would pass over exactly the drift T054 exists to close — so the old shape must now fail.
        var flattened = JsonDocument.Parse(JsonSerializer.Serialize(
            ProposalPayload().Operation, ControlTransactionPayload.CanonicalJsonOptions)).RootElement;

        var result = ValidatorSchemaEvaluation.Evaluate(ProposalSchema(), flattened);

        result.IsValid.Should().BeFalse(
            "the operation flattened to the top level is the shape the pre-T054 schema described, "
            + "and it is not what any producer emits");
    }

    [Fact]
    public void TheGovernanceSchemaUsesNoFileReferenceFields()
    {
        ProposalSchema().GetRawText().Should().NotContain("file-reference",
            "the Validator relaxes `type` on file-reference fields, and this file does not mirror "
            + "that branch — so the schema must not depend on it");
    }

    // ---- golden vectors: payloads captured off a sealed n1 ledger ------------------------------

    /// <summary>
    /// Loads a payload captured <b>verbatim</b> from a sealed transaction on n1 (register
    /// <c>bec175b8…</c>, 2026-08-09), decoded from the stored BSON binary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why these sit alongside the constructed fixtures rather than replacing them.</b> They prove
    /// different things and neither subsumes the other.
    /// </para>
    /// <para>
    /// The constructed fixture is <b>maximal</b> — every optional member populated — which is what
    /// makes the "the schema declares nothing the model cannot produce" direction non-vacuous. A real
    /// payload cannot do that job: this proposal is an <c>Add</c>, so it carries no
    /// <c>validatorEntry</c> at all (omitted when null), and a contract checked only against it would
    /// leave that property unexercised in exactly the direction that has already bitten this feature
    /// once — <c>validatorEntry</c> is the field the T085 substitution gate exists to protect.
    /// </para>
    /// <para>
    /// The golden vector proves the half a constructed fixture never can: that the <b>producer</b>
    /// actually emits this shape. The constructed fixture is built from the model with the model's own
    /// options, so a change to the real serialisation path — an option dropped at a call site, a
    /// converter moved off a property — moves fixture and code together and the test stays green. The
    /// sealed bytes cannot move.
    /// </para>
    /// </remarks>
    private static JsonElement Sealed(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Governance", $"{name}-sealed-n1.json");
        File.Exists(path).Should().BeTrue("the golden vector ships at {0}", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void TheSealedProposal_PassesEvaluationAgainstTheShippedSchema()
    {
        // The strongest statement this file makes: bytes the platform actually sealed satisfy the
        // contract the published blueprint declares. Had this existed before T054, the schema/payload
        // divergence could never have survived a single live governance operation.
        var result = ValidatorSchemaEvaluation.Evaluate(ProposalSchema(), Sealed("proposal"));

        result.IsValid.Should().BeTrue(
            "a proposal sealed on a real ledger must satisfy the published contract. Failures: {0}",
            string.Join("; ", ValidatorSchemaEvaluation.Failures(result)));
    }

    [Fact]
    public void TheSealedProposal_AgreesWithTheConstructedFixtureOnEveryFieldItCarries()
    {
        // Ties the two fixtures together. Anything the real payload carries must also be something
        // the model emits — so a producer that started writing a field the model does not know about
        // (or spelled differently) fails here rather than in a deployment.
        var constructed = EmittedProposal();

        foreach (var property in Sealed("proposal").EnumerateObject())
        {
            constructed.TryGetProperty(property.Name, out _).Should().BeTrue(
                "the sealed payload carries '{0}', so the model must emit it too", property.Name);
        }

        foreach (var property in Sealed("proposal").GetProperty("operation").EnumerateObject())
        {
            constructed.GetProperty("operation").TryGetProperty(property.Name, out _).Should().BeTrue(
                "the sealed operation carries '{0}', so the model must emit it too", property.Name);
        }
    }

    [Fact]
    public void TheSealedProposal_OmitsValidatorEntry_WhichIsWhyTheConstructedFixtureIsStillNeeded()
    {
        // Pins the gap the golden vector leaves, so nobody deletes the constructed fixture believing
        // the real one covers the same ground. An `Add` proposal has no validator to carry.
        Sealed("proposal").GetProperty("operation")
            .TryGetProperty("validatorEntry", out _).Should().BeFalse();

        EmittedProposal().GetProperty("operation")
            .TryGetProperty("validatorEntry", out _).Should().BeTrue(
                "the constructed fixture is the one that exercises it");
    }

    [Fact]
    public void TheSealedProposalAndEnactment_AreDistinguishableByContentAlone()
    {
        // T056's requirement, checked against real sealed bytes rather than reasoning. The pair is
        // the whole discriminator: a proposal enacts nothing and names no proposal; an enactment
        // carries the roster it wrote and names the proposal it settles.
        var proposal = Sealed("proposal");
        var enactment = Sealed("enactment");

        proposal.GetProperty("roster").ValueKind.Should().Be(JsonValueKind.Null);
        proposal.GetProperty("enactsProposalId").ValueKind.Should().Be(JsonValueKind.Null);

        enactment.GetProperty("roster").ValueKind.Should().Be(JsonValueKind.Object,
            "the enactment IS the roster mutation");
        enactment.GetProperty("enactsProposalId").ValueKind.Should().Be(JsonValueKind.String,
            "and it names the proposal whose approvals authorise it");

        // Both spellings present on both — key-presence is NOT the discriminator (T056).
        proposal.TryGetProperty("enactsProposalId", out _).Should().BeTrue();
        enactment.TryGetProperty("enactsProposalId", out _).Should().BeTrue();
    }

    [Fact]
    public void TheSealedEnactment_IsTheSameEnvelopeType_SoOneContractDescribesBoth()
    {
        // Action 4 declares no dataSchemas today (skipped by FR-006). If one is ever added, this is
        // the evidence that it is the same envelope with roster and enactsProposalId populated —
        // not a different shape needing a different contract.
        Sealed("enactment").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo(
                Sealed("proposal").EnumerateObject().Select(p => p.Name),
                "proposal and enactment are one payload type in two states");
    }

    [Fact]
    public void AProposalCarriesItsOperation_AndTheContractRequiresIt()
    {
        // `required` alone permits a null (see AssertRequiredAreEmitted). A proposal whose operation
        // were null would carry nothing to approve and nothing to rebuild an approval statement
        // from, so this is asserted on its own rather than folded into the generic rule.
        EmittedProposal().GetProperty("operation").ValueKind.Should().Be(JsonValueKind.Object);

        DeclaredTypes(OperationSchema()).Should().NotContain("null",
            "an operation-less proposal is not a proposal");
    }

    [Fact]
    public void AProposalAndAnEnactment_RemainDistinguishableFromPayloadContentAlone()
    {
        // T056 recorded that `enactsProposalId` is serialised as an explicit null on a proposal
        // rather than omitted, so a reader keying on key-presence classifies every proposal as an
        // enactment. The schema must therefore declare it nullable rather than merely optional, or a
        // conforming proposal would fail validation on its own discriminator.
        var proposal = EmittedProposal();

        proposal.TryGetProperty("enactsProposalId", out var enacts).Should().BeTrue(
            "the discriminator must travel on every control payload, not only on enactments");
        enacts.ValueKind.Should().Be(JsonValueKind.Null);

        var declared = ProposalSchema().GetProperty("properties").GetProperty("enactsProposalId");
        DeclaredTypes(declared).Should().Contain("null",
            "an explicit null must satisfy the published contract");
    }

    [Fact]
    public void TheRosterIsNullOnAProposal_AndTheContractPermitsIt()
    {
        // A pending proposal enacts nothing, so it must not appear to roster reconstruction as a
        // roster change (ControlTransactionPayload.Roster). A schema that required an object here
        // would make the only correct proposal payload invalid.
        EmittedProposal().GetProperty("roster").ValueKind.Should().Be(JsonValueKind.Null);

        DeclaredTypes(ProposalSchema().GetProperty("properties").GetProperty("roster"))
            .Should().Contain("null");
    }

    // ---- assertions over the two shapes -------------------------------------------------------

    private static JsonElement OperationSchema() =>
        ProposalSchema().GetProperty("properties").GetProperty("operation");

    /// <summary>
    /// The declared <c>type</c> of a schema node as a set, tolerating both the single-string and the
    /// array spellings JSON Schema allows.
    /// </summary>
    private static HashSet<string> DeclaredTypes(JsonElement schemaNode)
    {
        if (!schemaNode.TryGetProperty("type", out var type))
        {
            return [];
        }

        return type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [type.GetString()!];
    }

    /// <summary>
    /// True when a schema node declares an object but constrains none of its members — see the
    /// opaque-node rule on the class.
    /// </summary>
    private static bool IsOpaque(JsonElement schemaNode) =>
        !schemaNode.TryGetProperty("properties", out _);

    private static void AssertEmittedAreDeclared(JsonElement emitted, JsonElement schema, string path)
    {
        var declared = DeclaredProperties(schema);

        foreach (var property in emitted.EnumerateObject())
        {
            declared.Should().ContainKey(property.Name,
                "the published schema at '{0}' must declare every field the payload emits", path);

            if (property.Value.ValueKind == JsonValueKind.Object
                && !IsOpaque(declared[property.Name]))
            {
                AssertEmittedAreDeclared(
                    property.Value, declared[property.Name], $"{path}.{property.Name}");
            }
        }
    }

    private static void AssertDeclaredAreEmitted(JsonElement emitted, JsonElement schema, string path)
    {
        foreach (var declared in DeclaredProperties(schema))
        {
            emitted.TryGetProperty(declared.Key, out var value).Should().BeTrue(
                "the model must be able to produce '{0}.{1}', which the published schema declares",
                path, declared.Key);

            if (value.ValueKind == JsonValueKind.Object && !IsOpaque(declared.Value))
            {
                AssertDeclaredAreEmitted(value, declared.Value, $"{path}.{declared.Key}");
            }
        }
    }

    private static void AssertRequiredAreEmitted(JsonElement emitted, JsonElement schema, string path)
    {
        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray().Select(e => e.GetString()!))
            {
                // Presence, not non-nullness. Under JSON Schema `required` constrains the property to
                // be present and says nothing about its value; a null satisfies it whenever the
                // declared type permits null. `roster` and `enactsProposalId` are required precisely
                // so that they always travel — both are explicit nulls on a proposal, and asserting
                // non-nullness here would have forced the contract to stop requiring the very fields
                // whose constant presence is the point.
                emitted.TryGetProperty(name, out _).Should().BeTrue(
                    "'{0}.{1}' is required by the published schema", path, name);
            }
        }

        foreach (var declared in DeclaredProperties(schema))
        {
            if (emitted.TryGetProperty(declared.Key, out var value)
                && value.ValueKind == JsonValueKind.Object
                && !IsOpaque(declared.Value))
            {
                AssertRequiredAreEmitted(value, declared.Value, $"{path}.{declared.Key}");
            }
        }
    }

    private static Dictionary<string, JsonElement> DeclaredProperties(JsonElement schema) =>
        schema.TryGetProperty("properties", out var properties)
            ? properties.EnumerateObject().ToDictionary(p => p.Name, p => p.Value)
            : [];

    /// <summary>
    /// Asserts a schema node's <c>enum</c> is exactly the set of wire values the enum can produce —
    /// set equality both ways, for the reasons given on
    /// <c>GovernanceApprovalPayloadContractTests</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why member names rather than round-tripping a value through the payload options.</b> Every
    /// enum on this payload is converted by a <see cref="JsonStringEnumConverter"/> carrying <b>no
    /// naming policy</b>, so its wire value is exactly <see cref="Enum.GetName{TEnum}"/>. Most of
    /// them are annotated on the <i>property</i> rather than the enum type, so serialising a bare
    /// value under <see cref="ControlTransactionPayload.CanonicalJsonOptions"/> — which registers no
    /// converters — yields the underlying <b>integer</b> instead. A helper written that way compares
    /// "0" against "Pending" and fails for a reason that has nothing to do with the contract.
    /// </para>
    /// <para>
    /// This is the same trap that put numeric enums on the governance proposal endpoints: the wire
    /// form of an enum depends on where its converter is declared, and reading it off the type is a
    /// guess. <see cref="TheEmittedEnumValues_AreTheStringsTheSchemaDeclares"/> closes the loop by
    /// checking the value the producer actually emits.
    /// </para>
    /// </remarks>
    private static void AssertEnumWireValues<TEnum>(JsonElement schemaNode) where TEnum : struct, Enum
    {
        var declared = schemaNode.GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);

        var produced = Enum.GetNames<TEnum>().ToHashSet(StringComparer.Ordinal);

        produced.Should().BeEquivalentTo(declared,
            "every {0} the model can emit must be declared, and the schema must declare nothing the "
            + "model cannot emit", typeof(TEnum).Name);
    }

    // ---- the shipped definition ----------------------------------------------------------------

    private static JsonElement Template()
    {
        var path = Path.Combine(RepoRoot(), "blueprints", "templates", "register-governance-v1.json");
        File.Exists(path).Should().BeTrue("the governance blueprint ships at {0}", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("template").Clone();
    }

    private static JsonElement BlueprintAction(int id) =>
        Template().GetProperty("actions").EnumerateArray()
            .Single(a => a.GetProperty("id").GetInt32() == id);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "blueprints"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (a directory containing both blueprints/ and src/) "
            + $"by walking up from {AppContext.BaseDirectory}.");
    }
}
