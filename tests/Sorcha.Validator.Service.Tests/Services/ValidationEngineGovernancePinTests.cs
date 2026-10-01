// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Blueprint.Models.Canonical;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Feature 197 (T015): a governance step is judged under the <c>register-governance-v1</c> definition
/// its PROPOSAL was raised under, not whichever definition is current. The counterfactual: v5 of the
/// definition tightens the approval schema in a way v4 does not; an approval of a v4-pinned proposal
/// must still validate once v5 is current, and the very same approval must fail against v5 — which is
/// what happens when the pin lookup is bypassed.
/// </summary>
public class ValidationEngineGovernancePinTests
{
    private const string Register = "own-register";
    private const string ProposalTxId = "881069fbe994327aba1f27e32aec2310a8d3203e15342735534cceee02c39579";
    private const string ExtraRequired = "xAddedInV5";

    private readonly Mock<IBlueprintCache> _cache = new();
    private readonly Mock<IRegisterServiceClient> _registerClient = new();
    private string _v5Id = string.Empty;

    private static string TemplatePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "blueprints", "templates", "register-governance-v1.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("blueprints/templates/register-governance-v1.json not found above test output");
    }

    /// <summary>The shipped definition (v4), or v5: action 2's schema additionally REQUIRES a property no approval carries.</summary>
    private static BlueprintModel Definition(bool v5)
    {
        var node = JsonNode.Parse(File.ReadAllText(TemplatePath()))!["template"]!.AsObject();
        if (v5)
        {
            var action2 = node["actions"]!.AsArray().Single(a => a!["id"]!.GetValue<int>() == 2)!;
            var schema = action2["dataSchemas"]![0]!.AsObject();
            schema["required"]!.AsArray().Add(ExtraRequired);
            schema["properties"]!.AsObject()[ExtraRequired] = new JsonObject { ["type"] = "string" };
        }

        return JsonSerializer.Deserialize<BlueprintModel>(
            node.ToJsonString(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static string PublicationId(BlueprintModel definition, out string json)
    {
        json = JsonSerializer.Serialize(definition);
        return BlueprintPublicationId.ComputeFromDefinition(
            SystemRegisterConstants.SystemRegisterId, GovernanceBlueprint.BlueprintId, json);
    }

    private static TransactionModel Tx(
        string registerId, string txId, string payloadJson,
        string? blueprintId = null, uint? actionId = null) => new()
    {
        RegisterId = registerId,
        TxId = txId,
        MetaData = blueprintId is null ? null : new TransactionMetaData
        {
            RegisterId = registerId, BlueprintId = blueprintId, ActionId = actionId,
        },
        Payloads = [new PayloadModel { Data = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payloadJson)) }],
    };

    private string ServeDefinitions()
    {
        var v4 = Definition(false);
        var v5 = Definition(true);
        var id4 = PublicationId(v4, out var v4Json);
        var id5 = PublicationId(v5, out var v5Json);
        _v5Id = id5;
        var sys = SystemRegisterConstants.SystemRegisterId;
        _registerClient.Setup(r => r.GetTransactionAsync(sys, id4, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(sys, id4, v4Json));
        _registerClient.Setup(r => r.GetTransactionAsync(sys, id5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(sys, id5, v5Json));
        // "Latest" is v5 — what a pin-blind lookup would judge against.
        _cache.Setup(c => c.GetBlueprintAsync(GovernanceBlueprint.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(v5);
        return id4;
    }

    private void ServeProposal(string? pin, string proposalTxId = ProposalTxId)
    {
        var payload = new JsonObject { ["version"] = 1, ["roster"] = null, ["enactsProposalId"] = null };
        if (pin is not null) payload["governanceDefinitionTxId"] = pin;
        _registerClient.Setup(r => r.GetTransactionAsync(Register, proposalTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(Register, proposalTxId, payload.ToJsonString(),
                GovernanceBlueprint.BlueprintId, (uint)GovernanceBlueprint.ProposeChangeActionId));
    }

    private ValidationEngine CreateEngine(IExemptionAuthorityResolver? exemptionResolver = null)
    {
        var hash = new Mock<IHashProvider>();
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256)).Returns(new byte[32]);
        return new ValidationEngine(
            Options.Create(new ValidationEngineConfiguration()),
            _cache.Object,
            hash.Object,
            Mock.Of<ICryptoModule>(),
            Mock.Of<IWalletUtilities>(),
            _registerClient.Object,
            Mock.Of<IRightsEnforcementService>(),
            Mock.Of<ILogger<ValidationEngine>>(),
            governanceRosterService: Mock.Of<IGovernanceRosterService>(),
            exemptionResolver: exemptionResolver);
    }

    private const string ApprovalJson = """
        {
          "type": "governance-approval",
          "proposalId": "881069fbe994327aba1f27e32aec2310a8d3203e15342735534cceee02c39579",
          "approverDid": "did:sorcha:w:ws11qqdy4gcqgk2xkrumzugh3j5nczamx7v0pf59x08r6p5hj07ygfjr5msqg3y",
          "isApproval": true,
          "signature": "6uZWByrRnjmxuK1L94Y5vOsl2XgCT3Fth786FlxkZGvSeo3XtDJ0N34mkpCh9nB+dvrmOayuY2Zg2bO8vMcVBA==",
          "publicKey": "ekP71xMpn5+YRClF3IMvxGvomumkxWn5nJWh3oK4Ul0=",
          "authMethod": "service",
          "statementVersion": "sorcha:governance-approval:v2",
          "authorisation": {
            "kind": "direct",
            "individualDid": "did:sorcha:w:ws11qz3fukhcn38d7djew2mya5z9pjt9wyh94a5t8fxvt572kly9eze22e4lghs",
            "signature": "WAZE0R9Sw0TNOMg1o7ST0mDsWXnSIGjl+9kiysLtRByG40YLBfiDC96D0RB7tUZajES5m26nVxOb6xQUNDuxAw==",
            "publicKey": "op5a+JxO3zZZcrZO0EUMllcS5a9os6TMXTyrfIXIsqU=",
            "authMethod": "service",
            "algorithm": "ED25519",
            "delegationAlgorithm": "ED25519"
          }
        }
        """;

    // A realistic sealed approval (shape of approval-sealed-n1.json).
    private static Transaction Approval() => new()
    {
        TransactionId = "approval-tx",
        RegisterId = Register,
        BlueprintId = GovernanceBlueprint.BlueprintId,
        ActionId = GovernanceBlueprint.CollectQuorumActionId.ToString(),
        PreviousTransactionId = ProposalTxId,
        PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.Deserialize<JsonElement>(ApprovalJson),
        Signatures =
        [
            new RegisterSignature
            {
                PublicKey = new byte[32], SignatureValue = new byte[64],
                Algorithm = "ED25519", SignedAt = DateTimeOffset.UtcNow,
            },
        ],
    };

    [Fact]
    public async Task ValidateSchema_ApprovalOfProposalPinnedToV4_ValidatesAgainstV4EvenWhenV5IsCurrent()
    {
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_SameApprovalWithPinBypassed_FailsAgainstV5()
    {
        // The counterfactual half: a LEGACY (unpinned) proposal takes the latest-definition path,
        // which is v5 here. Same approval bytes, same engine — only the pin differs.
        ServeDefinitions();
        ServeProposal(pin: null);

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code.StartsWith("VAL_SCHEMA_"));
    }

    [Fact]
    public async Task ValidateSchema_LegacyProposal_CountsFallbackWithStepTag()
    {
        ServeDefinitions();
        ServeProposal(pin: null);

        var recorded = new List<string?>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == FederationValidatorMetrics.MeterName
                && inst.Name == "sorcha_governance_definition_pin_fallback")
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (var t in tags)
                if (t.Key == "step") lock (recorded) recorded.Add(t.Value as string);
        });
        listener.Start();

        await CreateEngine().ValidateSchemaAsync(Approval());

        lock (recorded) recorded.Should().Contain("approval");
    }

    [Fact]
    public async Task ValidateSchema_PinnedProposal_DoesNotCountFallback()
    {
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);

        var count = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == FederationValidatorMetrics.MeterName
                && inst.Name == "sorcha_governance_definition_pin_fallback")
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref count));
        listener.Start();

        await CreateEngine().ValidateSchemaAsync(Approval());

        count.Should().Be(0);
    }

    [Fact]
    public async Task ValidateSchema_ProposalUnreadable_RefusedAsUnresolvable()
    {
        ServeDefinitions();
        // No proposal served: GetTransactionAsync returns null.

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    [Fact]
    public async Task ValidateSchema_ProposalFetchThrows_RefusedAsUnresolvable()
    {
        ServeDefinitions();
        _registerClient.Setup(r => r.GetTransactionAsync(Register, ProposalTxId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    [Fact]
    public async Task ValidateSchema_PinnedDefinitionUnresolvable_RefusedWithGovernancePinField()
    {
        // Proposal pinned to a publication the node cannot produce; latest (v5) is available and
        // must NOT be used as a substitute.
        ServeDefinitions();
        ServeProposal(new string('a', 64));

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle(e => e.Code == "VAL_BP_VERSION_001").Subject;
        error.Field.Should().Be("payload.governanceDefinitionTxId");
        result.Errors.Should().NotContain(e => e.Code.StartsWith("VAL_SCHEMA_"));
    }

    private static Transaction Enactment(string payloadJson) => new()
    {
        TransactionId = "enact-tx",
        RegisterId = Register,
        BlueprintId = GovernanceBlueprint.BlueprintId,
        ActionId = GovernanceBlueprint.RecordControlTransactionActionId.ToString(),
        PreviousTransactionId = "prev",
        PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.Deserialize<JsonElement>(payloadJson),
        Signatures =
        [
            new RegisterSignature
            {
                PublicKey = new byte[32], SignatureValue = new byte[64],
                Algorithm = "ED25519", SignedAt = DateTimeOffset.UtcNow,
            },
        ],
    };

    private static string EnactPayload(string? ownPin = null) =>
        new JsonObject
        {
            ["version"] = 1,
            ["roster"] = null,
            ["enactsProposalId"] = ProposalTxId,
            ["governanceDefinitionTxId"] = ownPin,
        }.ToJsonString();

    [Fact]
    public async Task ValidateSchema_ApprovalReferencesNonProposalTransaction_Refused()
    {
        // A same-register tx that is NOT a governance proposal, whose payload even carries a
        // genuine older pin: it must not be read as a (legacy or pinned) proposal.
        var v4Id = ServeDefinitions();
        var payload = new JsonObject { ["version"] = 1, ["governanceDefinitionTxId"] = v4Id }.ToJsonString();
        _registerClient.Setup(r => r.GetTransactionAsync(Register, ProposalTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(Register, ProposalTxId, payload, "some-other-blueprint", 1));

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    /// <summary>An approval as <see cref="Approval"/>, with a different envelope reference and/or signed proposal id.</summary>
    private static Transaction ApprovalWith(string? previousTransactionId, string signedProposalId)
    {
        var approval = Approval();
        var payload = JsonNode.Parse(ApprovalJson)!.AsObject();
        payload["proposalId"] = signedProposalId;
        return new Transaction
        {
            TransactionId = approval.TransactionId, RegisterId = approval.RegisterId,
            BlueprintId = approval.BlueprintId, ActionId = approval.ActionId,
            PreviousTransactionId = previousTransactionId, PayloadHash = approval.PayloadHash,
            CreatedAt = approval.CreatedAt,
            Payload = JsonSerializer.Deserialize<JsonElement>(payload.ToJsonString()),
            Signatures = approval.Signatures,
        };
    }

    [Fact]
    public async Task ValidateSchema_ApprovalNamingNoProposal_Refused()
    {
        // The signed payload names no proposal: nothing to be judged under, whatever the envelope says.
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(ApprovalWith(ProposalTxId, signedProposalId: ""));

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    private const string OtherProposalTxId = "2c1f6b0e3f7a4d8c9b5e1a2f3d4c5b6a7e8f9a0b1c2d3e4f5a6b7c8d9e0f1a2b";

    [Fact]
    public async Task ValidateSchema_ApprovalEnvelopeReferencesDifferentProposalThanItSigns_Refused()
    {
        // Both proposals are readable and pinned to a definition the approval satisfies, so the ONLY
        // reason to refuse is the disagreement between the unsigned reference and the signed one.
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);
        ServeProposal(v4Id, OtherProposalTxId);

        var result = await CreateEngine().ValidateSchemaAsync(
            ApprovalWith(previousTransactionId: OtherProposalTxId, signedProposalId: ProposalTxId));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VAL_BP_VERSION_001"
            && e.Message.Contains("approval references a different proposal than it signs"));
    }

    [Fact]
    public async Task ValidateSchema_ApprovalWithoutEnvelopeReference_JudgedBySignedProposalsPin()
    {
        // The signed ProposalId alone locates the proposal; its v4 pin governs even with v5 latest.
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(
            ApprovalWith(previousTransactionId: null, signedProposalId: ProposalTxId));

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_ApprovalMatchingReference_JudgedByThatProposalsPin()
    {
        // Signed and envelope references agree; that proposal is pinned to a definition this node
        // cannot produce, so the approval is refused — NOT judged by latest (v5) or by any other proposal.
        var v4Id = ServeDefinitions();
        ServeProposal(new string('c', 64));
        ServeProposal(v4Id, OtherProposalTxId);

        var result = await CreateEngine().ValidateSchemaAsync(
            ApprovalWith(previousTransactionId: ProposalTxId, signedProposalId: ProposalTxId));

        result.Errors.Should().ContainSingle(e => e.Code == "VAL_BP_VERSION_001"
            && e.Field == "payload.governanceDefinitionTxId");
    }

    [Fact]
    public async Task ValidateSchema_SelfPinnedEnactment_Refused()
    {
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(Enactment(EnactPayload(ownPin: v4Id)));

        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001");
    }

    [Fact]
    public async Task ValidateSchema_EnactmentWithoutOwnPin_JudgedByItsProposalsPin()
    {
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);
        (await CreateEngine().ValidateSchemaAsync(Enactment(EnactPayload()))).IsValid.Should().BeTrue();

        // Proposal pinned to a definition this node cannot produce: refused, NOT judged by latest.
        ServeProposal(new string('b', 64));
        var result = await CreateEngine().ValidateSchemaAsync(Enactment(EnactPayload()));
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001"
            && e.Field == "payload.governanceDefinitionTxId");
    }

    [Fact]
    public async Task ValidateSchema_CancellationDuringProposalFetch_NotMappedToGovernanceRefusal()
    {
        ServeDefinitions();
        using var cts = new CancellationTokenSource();
        _registerClient.Setup(r => r.GetTransactionAsync(Register, ProposalTxId, It.IsAny<CancellationToken>()))
            .Returns(() => { cts.Cancel(); throw new OperationCanceledException(cts.Token); });

        var result = await CreateEngine().ValidateSchemaAsync(Approval(), cts.Token);

        result.Errors.Should().NotContain(e => e.Code == "VAL_BP_VERSION_001");
    }

    // ---- Feature 197 (T016): VAL_GOV_DEF_001 at raise -------------------------------------------

    private static Transaction Raise(string pin, bool ownerOverride = false) => new()
    {
        TransactionId = "raise-tx",
        RegisterId = Register,
        BlueprintId = GovernanceBlueprint.BlueprintId,
        ActionId = GovernanceBlueprint.ProposeChangeActionId.ToString(),
        PreviousTransactionId = "prev",
        PayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        CreatedAt = DateTimeOffset.UtcNow,
        Payload = JsonSerializer.Deserialize<JsonElement>(new JsonObject
        {
            ["version"] = 1,
            // An Owner-override is one transaction that proposes AND enacts: it carries the updated
            // roster and its own pin, with no EnactsProposalId (Program.cs, ProposeGovernanceOperation).
            ["roster"] = ownerOverride
                ? new JsonObject { ["registerId"] = Register, ["attestations"] = new JsonArray() }
                : null,
            ["operation"] = new JsonObject
            {
                ["operationType"] = "Add",
                ["proposerDid"] = "did:sorcha:w:proposer",
                ["proposedAt"] = "2026-10-01T00:00:00Z",
                ["rosterSnapshotId"] = "snap",
                ["quorumFormulaAtRaise"] = "StrictMajority",
            },
            ["enactsProposalId"] = null,
            ["governanceDefinitionTxId"] = pin,
        }.ToJsonString()),
        Signatures =
        [
            new RegisterSignature
            {
                PublicKey = new byte[32], SignatureValue = new byte[64],
                Algorithm = "ED25519", SignedAt = DateTimeOffset.UtcNow,
            },
        ],
    };

    private void ServeCurrent(string? currentId) =>
        _registerClient.Setup(r => r.GetSystemRegisterBlueprintPublicationIdAsync(
                GovernanceBlueprint.BlueprintId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentId);

    // Definition() is not byte-stable across calls, so reuse the id ServeDefinitions() published.
    private string V5Id() => _v5Id;

    [Fact]
    public async Task ValidateSchema_RaiseUnderSupersededDefinition_RefusedWithGovDef001()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(V5Id());

        var result = await CreateEngine().ValidateSchemaAsync(Raise(v4Id));

        result.IsValid.Should().BeFalse();
        var error = result.Errors.Should().ContainSingle(e => e.Code == "VAL_GOV_DEF_001").Subject;
        error.Field.Should().Be("payload.governanceDefinitionTxId");
        error.Message.Should().Be(
            $"raised under a superseded governance definition '{v4Id}'; current is '{V5Id()}'");
    }

    [Fact]
    public async Task ValidateSchema_RaiseUnderCurrentDefinition_Passes()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(Raise(v4Id));

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_ApprovalOfSupersededProposal_NotComparedToCurrent()
    {
        // v4-pinned proposal, v5 current: the approval is judged by v4 and is NOT a stale raise.
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);
        ServeCurrent(V5Id());

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.Errors.Should().NotContain(e => e.Code == "VAL_GOV_DEF_001");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_EnactmentOfSupersededProposal_NotComparedToCurrent()
    {
        var v4Id = ServeDefinitions();
        ServeProposal(v4Id);
        ServeCurrent(V5Id());

        var result = await CreateEngine().ValidateSchemaAsync(Enactment(EnactPayload()));

        result.Errors.Should().NotContain(e => e.Code == "VAL_GOV_DEF_001");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_RaiseWhenCurrentUnreadable_RefusedNotAccepted()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(null);

        var result = await CreateEngine().ValidateSchemaAsync(Raise(v4Id));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "VAL_BP_VERSION_001"
            && e.Field == "payload.governanceDefinitionTxId");
        result.Errors.Should().NotContain(e => e.Code == "VAL_GOV_DEF_001");
    }

    // ---- Final review (Important 1): an unsigned label cannot switch pin enforcement off -------

    private static Transaction WithMetadata(Transaction tx, Dictionary<string, string> metadata) => new()
    {
        TransactionId = tx.TransactionId, RegisterId = tx.RegisterId,
        BlueprintId = tx.BlueprintId, ActionId = tx.ActionId,
        PreviousTransactionId = tx.PreviousTransactionId, PayloadHash = tx.PayloadHash,
        CreatedAt = tx.CreatedAt, Payload = tx.Payload, Signatures = tx.Signatures,
        Metadata = metadata,
    };

    private static Dictionary<string, string> ForgedPublishLabel() => new()
    {
        ["Type"] = "Control",
        ["transactionType"] = "BlueprintPublish",
    };

    private static IExemptionAuthorityResolver Resolver(ExemptionDecision decision)
    {
        var resolver = new Mock<IExemptionAuthorityResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(decision);
        return resolver.Object;
    }

    [Fact]
    public async Task ValidateSchema_StaleRaiseWithForgedPublishLabel_NoProvedAuthority_RefusedWithGovDef001()
    {
        // The label is unsigned. The signer holds no publish key, so the resolver refuses the
        // BlueprintPublish claim — and the raise must still be held to the CURRENT definition.
        var v4Id = ServeDefinitions();
        ServeCurrent(V5Id());
        var claim = new ExemptionClaim(ExemptionKind.BlueprintPublish, ExemptionClaimRoute.TypeLabel, "Control");
        var engine = CreateEngine(Resolver(ExemptionDecision.NotEntitled(claim, "not on the validator roster")));

        var result = await engine.ValidateSchemaAsync(WithMetadata(Raise(v4Id), ForgedPublishLabel()));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VAL_GOV_DEF_001");
    }

    [Fact]
    public async Task ValidateSchema_StaleRaiseWithForgedPublishLabel_NoResolver_RefusedWithGovDef001()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(V5Id());

        var result = await CreateEngine().ValidateSchemaAsync(WithMetadata(Raise(v4Id), ForgedPublishLabel()));

        result.Errors.Should().ContainSingle(e => e.Code == "VAL_GOV_DEF_001");
    }

    [Fact]
    public async Task ValidateSchema_GovernanceBlueprintPublicationWithProvedPublishAuthority_NotTreatedAsGovernanceStep()
    {
        // The genuine exception: a publication OF the governance blueprint, signed by a node holding
        // the publish key. It is not a governance action, so no governance pin is read or compared.
        var v4Id = ServeDefinitions();
        ServeCurrent(V5Id());
        var claim = new ExemptionClaim(ExemptionKind.BlueprintPublish, ExemptionClaimRoute.TypeLabel, "Control");
        var engine = CreateEngine(Resolver(ExemptionDecision.Grant(ExemptionKind.BlueprintPublish, claim)));

        var result = await engine.ValidateSchemaAsync(WithMetadata(Raise(v4Id), ForgedPublishLabel()));

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
        _registerClient.Verify(r => r.GetSystemRegisterBlueprintPublicationIdAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Final review (Important 3): a governance pin resolves from the SSR only ---------------

    [Fact]
    public async Task ValidateSchema_GovernancePinPublishedOnlyOnOwnRegister_RefusedAsUnresolvable()
    {
        // A register owner publishes a governance-id definition to THEIR register. Its register-scoped
        // id reproduces against that register, so the own-register arm would verify and accept it.
        // An approval is used (not a raise) so the currency check cannot mask the resolution arm.
        ServeDefinitions();
        var lax = Definition(false);
        var laxJson = JsonSerializer.Serialize(lax);
        var laxId = BlueprintPublicationId.ComputeFromDefinition(Register, GovernanceBlueprint.BlueprintId, laxJson);
        _registerClient.Setup(r => r.GetTransactionAsync(Register, laxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(Register, laxId, laxJson));
        ServeProposal(laxId);

        var result = await CreateEngine().ValidateSchemaAsync(Approval());

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VAL_BP_VERSION_001"
            && e.Field == "payload.governanceDefinitionTxId");
        _registerClient.Verify(r => r.GetTransactionAsync(Register, laxId, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidateSchema_GovernancePin_NeverConsultsBlueprintServicePublishedStore()
    {
        // The Blueprint Service's published store holds register-scoped publications; for a governance
        // pin it must not be asked, even when it would answer.
        ServeDefinitions();
        var fetcher = new Mock<IBlueprintFetcher>();
        fetcher.Setup(f => f.FetchBlueprintByPublicationAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Definition(false));
        var pin = new string('d', 64);
        ServeProposal(pin);

        var hash = new Mock<IHashProvider>();
        hash.Setup(h => h.ComputeHash(It.IsAny<byte[]>(), HashType.SHA256)).Returns(new byte[32]);
        var engine = new ValidationEngine(
            Options.Create(new ValidationEngineConfiguration()),
            _cache.Object, hash.Object, Mock.Of<ICryptoModule>(), Mock.Of<IWalletUtilities>(),
            _registerClient.Object, Mock.Of<IRightsEnforcementService>(), Mock.Of<ILogger<ValidationEngine>>(),
            blueprintFetcher: fetcher.Object,
            governanceRosterService: Mock.Of<IGovernanceRosterService>());

        var result = await engine.ValidateSchemaAsync(Approval());

        result.Errors.Should().ContainSingle(e => e.Code == "VAL_BP_VERSION_001");
        fetcher.Verify(f => f.FetchBlueprintByPublicationAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Final review (seam #15): the Owner-override raise is held to the current definition ---

    [Fact]
    public async Task ValidateSchema_OwnerOverrideUnderSupersededDefinition_RefusedWithGovDef001()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(V5Id());

        var result = await CreateEngine().ValidateSchemaAsync(Raise(v4Id, ownerOverride: true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "VAL_GOV_DEF_001"
            && e.Field == "payload.governanceDefinitionTxId");
    }

    [Fact]
    public async Task ValidateSchema_OwnerOverrideUnderCurrentDefinition_Passes()
    {
        var v4Id = ServeDefinitions();
        ServeCurrent(v4Id);

        var result = await CreateEngine().ValidateSchemaAsync(Raise(v4Id, ownerOverride: true));

        result.Errors.Should().BeEmpty();
        result.IsValid.Should().BeTrue();
    }
}
