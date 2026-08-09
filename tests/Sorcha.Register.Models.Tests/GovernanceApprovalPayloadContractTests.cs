// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// The approval payload the platform emits and the payload contract the published blueprint declares
/// must be the same thing (T075 / FR-018).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is driven off serialisation rather than a hand-written field list.</b> A blueprint
/// declaring a contract and code producing a payload is precisely the seam this feature keeps being
/// bitten by: both sides individually correct, nothing verifying the join, and the failure silent —
/// <c>System.Text.Json</c> drops an unknown property without complaint, so a renamed or added field
/// simply stops travelling. Two council blueprints on the F127 branch shipped fifteen disclosures
/// that parsed to empty defaults for exactly this reason.
/// </para>
/// <para>
/// So the assertions are <b>bidirectional and derived</b>: everything the model emits must be
/// declared, everything declared must be producible, and every enum's wire values must be the set
/// the schema lists. Adding a property to either side without the other fails here rather than in a
/// deployment.
/// </para>
/// <para>
/// It reads the shipped file, not a fixture. A copy drifts the same way the blueprint did.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalPayloadContractTests
{
    /// <summary>
    /// Every optional member populated, so the emitted property set is the maximal one. A partially
    /// populated instance would make the "schema declares nothing the model cannot produce" direction
    /// pass vacuously.
    /// </summary>
    private static GovernanceApprovalActionPayload FullyPopulatedPayload() => new()
    {
        ProposalId = "5b1f0c0e5b1f0c0e5b1f0c0e5b1f0c0e",
        ApproverDid = "did:sorcha:w:ws11qapprover",
        IsApproval = true,
        Signature = "c2lnbmF0dXJl",
        PublicKey = "cHVibGlja2V5",
        AuthMethod = ApprovalAuthMethod.HardwareBacked,
        StatementVersion = GovernanceApprovalStatement.StatementVersion,
        Comment = "Reviewed the roster diff.",
        Authorisation = new ApprovalAuthorisation
        {
            Kind = AuthorisationKind.Delegated,
            IndividualDid = "did:sorcha:w:ws11qindividual",
            Signature = "aW5kaXZpZHVhbA==",
            PublicKey = "Ym90a2V5",
            AuthMethod = ApprovalAuthMethod.Service,
            Algorithm = SignatureAlgorithm.ED25519,
            DelegationAlgorithm = SignatureAlgorithm.ED25519,
            DelegationSignature = "Z3JhbnQ=",
            DelegationPublicKey = "Z3JhbnRvcmtleQ==",
            Delegation = new GovernanceDelegation
            {
                DelegationId = "delegation-1",
                OrganisationDid = "did:sorcha:w:ws11qapprover",
                IndividualDid = "did:sorcha:w:ws11qindividual",
                ApproverPublicKey = "Ym90a2V5",
                Scope = [GovernanceOperationType.CryptoPolicyUpdate],
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
                GrantedAt = DateTimeOffset.UtcNow,
            },
        },
    };

    private static JsonElement EmittedPayload() =>
        JsonDocument.Parse(JsonSerializer.Serialize(
                FullyPopulatedPayload(), GovernanceApprovalActionPayload.CanonicalJsonOptions))
            .RootElement.Clone();

    private static JsonElement ApprovalSchema() =>
        BlueprintAction(GovernanceBlueprint.CollectQuorumActionId)
            .GetProperty("dataSchemas")[0];

    [Fact]
    public void EveryFieldTheModelEmits_IsDeclaredByThePublishedSchema()
    {
        // The direction that catches "a property was added to the payload and the contract never
        // heard about it" — the field then rides the wire undeclared, and a consumer written from the
        // blueprint never reads it.
        AssertEmittedAreDeclared(EmittedPayload(), ApprovalSchema(), "approval");
    }

    [Fact]
    public void EveryFieldTheSchemaDeclares_IsProducibleByTheModel()
    {
        // The opposite direction, and the one that would have caught the pre-T075 state: the schema
        // declared `delegation.signature` and `delegation.publicKey`, which live on the authorisation
        // and not on the grant, so nothing could ever have produced a conforming payload.
        AssertDeclaredAreEmitted(EmittedPayload(), ApprovalSchema(), "approval");
    }

    [Fact]
    public void EveryRequiredField_IsAlwaysEmitted()
    {
        AssertRequiredAreEmitted(EmittedPayload(), ApprovalSchema(), "approval");
    }

    [Fact]
    public void TheDiscriminator_IsTheConstantTheSchemaPins()
    {
        // `const` in the schema and a C# constant are two declarations of one value. A reader that
        // matches on the schema's spelling would silently classify nothing if the model's drifted.
        ApprovalSchema().GetProperty("properties").GetProperty("type").GetProperty("const").GetString()
            .Should().Be(GovernanceApprovalActionPayload.PayloadType);

        EmittedPayload().GetProperty("type").GetString()
            .Should().Be(GovernanceApprovalActionPayload.PayloadType);
    }

    [Theory]
    [InlineData("authMethod")]
    public void ApprovalAuthMethod_WireValues_AreExactlyWhatTheSchemaLists(string property)
    {
        AssertEnumWireValues<ApprovalAuthMethod>(
            ApprovalSchema().GetProperty("properties").GetProperty(property));
    }

    [Fact]
    public void AuthorisationEnums_WireValues_AreExactlyWhatTheSchemaLists()
    {
        var authorisation = ApprovalSchema().GetProperty("properties").GetProperty("authorisation")
            .GetProperty("properties");

        AssertEnumWireValues<AuthorisationKind>(authorisation.GetProperty("kind"));
        AssertEnumWireValues<ApprovalAuthMethod>(authorisation.GetProperty("authMethod"));
        AssertEnumWireValues<SignatureAlgorithm>(authorisation.GetProperty("algorithm"));
        AssertEnumWireValues<SignatureAlgorithm>(authorisation.GetProperty("delegationAlgorithm"));

        AssertEnumWireValues<GovernanceOperationType>(
            authorisation.GetProperty("delegation").GetProperty("properties")
                .GetProperty("scope").GetProperty("items"));
    }

    [Fact]
    public void TheApprovalActionId_NamesTheActionThePublishedDefinitionUsesForApprovals()
    {
        // FR-018. The constant is what the submitter records as the transaction's ActionId, so if it
        // stopped naming the approval-collecting action the ledger sequence would no longer diff
        // against the blueprint (T057) — and nothing else would notice, because control transactions
        // are exempt from action-schema validation.
        BlueprintAction(GovernanceBlueprint.CollectQuorumActionId).GetProperty("title").GetString()
            .Should().Be("Collect Quorum");

        BlueprintAction(GovernanceBlueprint.ProposeChangeActionId).GetProperty("title").GetString()
            .Should().Be("Propose Change");
    }

    [Fact]
    public void TheBlueprintIdConstant_MatchesTheShippedDefinition()
    {
        Template().GetProperty("id").GetString().Should().Be(GovernanceBlueprint.BlueprintId);
    }

    // ---- assertions over the two shapes -------------------------------------------------------

    private static void AssertEmittedAreDeclared(JsonElement emitted, JsonElement schema, string path)
    {
        var declared = DeclaredProperties(schema);

        foreach (var property in emitted.EnumerateObject())
        {
            declared.Should().ContainKey(property.Name,
                "the published schema at '{0}' must declare every field the payload emits", path);

            if (property.Value.ValueKind == JsonValueKind.Object)
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

            if (value.ValueKind == JsonValueKind.Object)
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
                emitted.TryGetProperty(name, out _).Should().BeTrue(
                    "'{0}.{1}' is required by the published schema", path, name);
            }
        }

        foreach (var declared in DeclaredProperties(schema))
        {
            if (emitted.TryGetProperty(declared.Key, out var value)
                && value.ValueKind == JsonValueKind.Object)
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
    /// Asserts a schema node's <c>enum</c> is exactly the set of wire values the CLR enum serialises
    /// to under the payload's own options.
    /// </summary>
    /// <remarks>
    /// Set equality both ways on purpose. A subset check would pass while the schema listed a value
    /// no member produces (unreachable, and misleading to anyone reading the contract), and a
    /// superset check would pass while a member had no declaration at all — which is how
    /// <see cref="ApprovalAuthMethod.Unknown"/> was inexpressible before T075.
    /// </remarks>
    private static void AssertEnumWireValues<TEnum>(JsonElement schemaNode) where TEnum : struct, Enum
    {
        var declared = schemaNode.GetProperty("enum").EnumerateArray()
            .Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);

        var produced = Enum.GetValues<TEnum>()
            .Select(v => JsonSerializer
                .Serialize(v, GovernanceApprovalActionPayload.CanonicalJsonOptions).Trim('"'))
            .ToHashSet(StringComparer.Ordinal);

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

    // ---- golden vector: an approval captured off a sealed n1 ledger ----------------------------

    /// <summary>
    /// An approval payload taken verbatim from a sealed transaction on n1 (2026-08-09). It proves
    /// the half the constructed fixture cannot: that the <b>producer</b> emits this shape. The
    /// constructed fixture stays because it is maximal — this real one carries no <c>comment</c>,
    /// and its authorisation is <c>direct</c> rather than <c>delegated</c>, so it exercises neither.
    /// </summary>
    private static JsonElement SealedApproval()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Governance", "approval-sealed-n1.json");
        File.Exists(path).Should().BeTrue("the golden vector ships at {0}", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void TheSealedApproval_IsDeclaredByThePublishedSchema()
    {
        AssertEmittedAreDeclared(SealedApproval(), ApprovalSchema(), "sealed-approval");
    }

    [Fact]
    public void TheSealedApproval_SatisfiesEveryRequiredField()
    {
        AssertRequiredAreEmitted(SealedApproval(), ApprovalSchema(), "sealed-approval");
    }

    [Fact]
    public void TheSealedApproval_CarriesTheDiscriminatorAndAnAccountabilityBlock()
    {
        var approval = SealedApproval();

        approval.GetProperty("type").GetString().Should().Be(
            GovernanceApprovalActionPayload.PayloadType);

        // FR-029: an approval that resolves to nobody is one the register can never attribute.
        approval.TryGetProperty("authorisation", out var authorisation).Should().BeTrue();
        authorisation.ValueKind.Should().Be(JsonValueKind.Object);
        authorisation.GetProperty("individualDid").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheSealedApproval_RoundTripsThroughTheModel()
    {
        // The read direction, against real bytes: every consumer decodes with these options inside a
        // try/catch that treats a throw as "not an approval", so a decode failure is total and
        // silent. That is how a swallowed deserialisation counted zero approvals for a whole live run.
        var payload = JsonSerializer.Deserialize<GovernanceApprovalActionPayload>(
            SealedApproval().GetRawText(), GovernanceApprovalActionPayload.CanonicalJsonOptions);

        payload.Should().NotBeNull();
        payload!.ProposalId.Should().NotBeNullOrWhiteSpace();
        payload.ApproverDid.Should().NotBeNullOrWhiteSpace();
        payload.Signature.Should().NotBeNullOrWhiteSpace();
        payload.PublicKey.Should().NotBeNullOrWhiteSpace();
        payload.StatementVersion.Should().Be(GovernanceApprovalStatement.StatementVersion);
        payload.Authorisation.Should().NotBeNull();
    }
}
