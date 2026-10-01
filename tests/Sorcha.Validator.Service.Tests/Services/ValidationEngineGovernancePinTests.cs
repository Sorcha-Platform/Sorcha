// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
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

    private static TransactionModel Tx(string registerId, string txId, string payloadJson) => new()
    {
        RegisterId = registerId,
        TxId = txId,
        Payloads = [new PayloadModel { Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)) }],
    };

    private string ServeDefinitions()
    {
        var v4 = Definition(false);
        var v5 = Definition(true);
        var id4 = PublicationId(v4, out var v4Json);
        var id5 = PublicationId(v5, out var v5Json);
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

    private void ServeProposal(string? pin)
    {
        var payload = new JsonObject { ["version"] = 1, ["roster"] = null, ["enactsProposalId"] = null };
        if (pin is not null) payload["governanceDefinitionTxId"] = pin;
        _registerClient.Setup(r => r.GetTransactionAsync(Register, ProposalTxId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tx(Register, ProposalTxId, payload.ToJsonString()));
    }

    private ValidationEngine CreateEngine()
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
            governanceRosterService: Mock.Of<IGovernanceRosterService>());
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
            if (inst.Name == "sorcha_governance_definition_pin_fallback") l.EnableMeasurementEvents(inst);
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
}
