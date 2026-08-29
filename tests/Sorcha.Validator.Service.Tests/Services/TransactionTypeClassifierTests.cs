// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Xunit;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Unit coverage for the centralised transaction-type predicates carved out of
/// <c>ValidationEngine</c> as part of the post-Feature-119 rule-base cleanup.
/// </summary>
public class TransactionTypeClassifierTests
{
    private static Transaction TxWithMetadata(
        params (string Key, string Value)[] entries)
        => Build(blueprintId: "bp-1", payloadJson: "{}", entries);

    private static Transaction Build(
        string? blueprintId,
        string payloadJson,
        params (string Key, string Value)[] entries)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in entries) meta[k] = v;
        return new Transaction
        {
            TransactionId = "tx-1",
            RegisterId = "reg-1",
            BlueprintId = blueprintId,
            ActionId = "1",
            Payload = JsonDocument.Parse(payloadJson).RootElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures = new List<RegisterSignature>(),
            Metadata = meta,
        };
    }

    [Fact]
    public void ReadClaim_BlueprintIdIsGenesis_ClaimsGenesisViaTheBlueprintIdentifierRoute()
    {
        // Feature 196: this used to GRANT the six exemptions. It now only records that a claim was
        // made, and by which route — the grant needs proved signer authority on top.
        var tx = Build(blueprintId: GenesisConstants.BlueprintId, payloadJson: "{}");

        var claim = ExemptionAuthorityResolver.ReadClaim(tx);

        // On a NON-system register this is an ordinary register genesis, not the network's trust
        // anchor. Same label, same route — told apart by which register it is on.
        claim.Kind.Should().Be(ExemptionKind.RegisterGenesis);
        claim.Route.Should().Be(ExemptionClaimRoute.BlueprintIdentifier,
            "this route needs no metadata at all, so closing only the metadata route closes nothing");
    }

    [Theory]
    [InlineData("Genesis")]
    [InlineData("genesis")]
    [InlineData("Control")]
    [InlineData("CONTROL")]
    [InlineData("BlueprintPublish")]
    [InlineData("blueprintpublish")]
    public void ReadClaim_TypeMetadataMatches_RecordsTheClaim(string type)
    {
        // BlueprintPublish (post-#876) joins Genesis+Control in bypassing action-schema
        // validation and per-sender replay protection: the publish is signed by the
        // system wallet with an ActionId of "blueprint-publish" (not an int) and no
        // per-sender sequence number — both checks would reject otherwise.
        //
        // Feature 196: recognising the label is still correct; HONOURING it without checking the
        // signer's entitlement was the defect (#1591).
        var tx = TxWithMetadata(("Type", type));

        var claim = ExemptionAuthorityResolver.ReadClaim(tx);

        claim.IsClaimed.Should().BeTrue();
        claim.Route.Should().Be(ExemptionClaimRoute.TypeLabel);
    }

    [Fact]
    public void ReadClaim_RegularAction_ClaimsNothing()
    {
        var tx = TxWithMetadata(("Type", "Action"));

        ExemptionAuthorityResolver.ReadClaim(tx).IsClaimed.Should().BeFalse();
    }

    [Theory]
    [InlineData("Participant", true)]
    [InlineData("PARTICIPANT", true)]
    [InlineData("Action", false)]
    [InlineData("Control", false)]
    public void IsParticipantTransaction_RecognisesParticipantTypeOnly(string type, bool expected)
    {
        var tx = TxWithMetadata(("Type", type));

        TransactionTypeClassifier.IsParticipantTransaction(tx).Should().Be(expected);
    }

    [Fact]
    public void IsRejectionTransaction_MetadataTypeRejection_ReturnsTrue()
    {
        var tx = TxWithMetadata(("Type", "Rejection"));

        TransactionTypeClassifier.IsRejectionTransaction(tx).Should().BeTrue();
    }

    [Fact]
    public void IsRejectionTransaction_PayloadTypeRejection_ReturnsTrue()
    {
        var tx = Build(blueprintId: "bp-1", payloadJson: """{"type":"rejection"}""");

        TransactionTypeClassifier.IsRejectionTransaction(tx).Should().BeTrue();
    }

    [Fact]
    public void IsRejectionTransaction_NeitherPath_ReturnsFalse()
    {
        var tx = TxWithMetadata(("Type", "Action"));

        TransactionTypeClassifier.IsRejectionTransaction(tx).Should().BeFalse();
    }

    [Theory]
    [InlineData("Type", "Revocation", true)]
    [InlineData("transactionType", "Revocation", true)]
    [InlineData("Type", "revocation", true)]
    [InlineData("Type", "Action", false)]
    public void IsRevocationTransaction_RecognisesEitherKey(string key, string value, bool expected)
    {
        var tx = TxWithMetadata((key, value));

        TransactionTypeClassifier.IsRevocationTransaction(tx).Should().Be(expected);
    }

    // ── C-VAL (catch-up security review 2026-07-29) ─────────────────────────────────────────
    // These predicates waive the action-schema check, the routing-decision attestation and the
    // VAL_BP_003 reachability check. They therefore key on the SIGNED payload's `type` (inside
    // PayloadHash, which the signature covers) and NOT on Metadata["Type"], which sits outside
    // the signature, the payload hash and the merkle leaf. The theories below were previously
    // driven by metadata alone — i.e. they asserted the vulnerable behaviour as correct.

    /// <summary>Builds a transaction whose SIGNED payload declares <paramref name="payloadType"/>.</summary>
    private static Transaction TxWithSignedPayloadType(string payloadType)
        => Build(blueprintId: "bp-1",
                 payloadJson: $"{{\"type\":\"{payloadType}\"}}");

    [Theory]
    [InlineData("presentation-initiated", true)]
    [InlineData("presentation-outcome", true)]
    [InlineData("presentation-abandoned", true)]
    [InlineData("Presentation-Outcome", true)]   // wire values are ours; matching stays case-insensitive
    [InlineData("action", false)]
    [InlineData("control", false)]
    public void IsLifecycleTransaction_CoversAllThreeLifecycleTypes(string payloadType, bool expected)
    {
        var tx = TxWithSignedPayloadType(payloadType);

        TransactionTypeClassifier.IsLifecycleTransaction(tx).Should().Be(expected);
    }

    [Theory]
    [InlineData("presentation-outcome", true)]
    [InlineData("presentation-abandoned", true)]
    [InlineData("presentation-initiated", false)] // intentionally excluded — gets full reachability check
    [InlineData("action", false)]
    public void IsIntraActionLifecycleTerminal_ExcludesInitiated(string payloadType, bool expected)
    {
        var tx = TxWithSignedPayloadType(payloadType);

        TransactionTypeClassifier.IsIntraActionLifecycleTerminal(tx).Should().Be(expected);
    }

    [Theory]
    [InlineData("PresentationInitiated")]
    [InlineData("PresentationOutcome")]
    [InlineData("PresentationAbandoned")]
    public void IsLifecycleTransaction_ForgedMetadataOnly_ReturnsFalse(string metadataType)
    {
        // THE bypass: metadata alone must never buy the lifecycle carve-out.
        var tx = TxWithMetadata(("Type", metadataType));

        TransactionTypeClassifier.IsLifecycleTransaction(tx).Should().BeFalse();
        TransactionTypeClassifier.IsIntraActionLifecycleTerminal(tx).Should().BeFalse();
        TransactionTypeClassifier.HasUncorroboratedLifecycleMetadata(tx).Should().BeTrue(
            "the engine logs this as an attempted exemption it was not entitled to");
    }

    [Fact]
    public void HasUncorroboratedLifecycleMetadata_GenuineLifecycleTransaction_ReturnsFalse()
    {
        // Metadata and signed payload agree — nothing suspicious to report.
        var tx = Build(blueprintId: "bp-1",
                       payloadJson: "{\"type\":\"presentation-outcome\"}",
                       ("Type", "PresentationOutcome"));

        TransactionTypeClassifier.HasUncorroboratedLifecycleMetadata(tx).Should().BeFalse();
    }

    [Fact]
    public void HasUncorroboratedLifecycleMetadata_OrdinaryTransaction_ReturnsFalse()
    {
        var tx = TxWithMetadata(("Type", "Action"));

        TransactionTypeClassifier.HasUncorroboratedLifecycleMetadata(tx).Should().BeFalse();
    }

    [Fact]
    public void IsLifecycleTransaction_NoTypeMetadata_ReturnsFalse()
    {
        var tx = TxWithMetadata();

        TransactionTypeClassifier.IsLifecycleTransaction(tx).Should().BeFalse();
        TransactionTypeClassifier.IsIntraActionLifecycleTerminal(tx).Should().BeFalse();
    }

    // ---- Feature 189 / T054: the three governance actions -------------------------------------

    private static Transaction OnBlueprint(
        string? blueprintId, string actionId, params (string Key, string Value)[] entries)
    {
        var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in entries) meta[k] = v;
        return new Transaction
        {
            TransactionId = "tx-1",
            RegisterId = "reg-1",
            BlueprintId = blueprintId,
            ActionId = actionId,
            Payload = JsonDocument.Parse("{}").RootElement,
            PayloadHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures = new List<RegisterSignature>(),
            Metadata = meta,
        };
    }

    private static Transaction Governance(string actionId, params (string Key, string Value)[] entries)
        => OnBlueprint(GovernanceBlueprint.BlueprintId, actionId, entries);

    [Theory]
    [InlineData(GovernanceBlueprint.ProposeChangeActionId)]
    [InlineData(GovernanceBlueprint.CollectQuorumActionId)]
    [InlineData(GovernanceBlueprint.RecordControlTransactionActionId)]
    public void IsGovernanceActionTransaction_TheThreeNumberedActions_ReturnTrue(int actionId)
    {
        var tx = Governance(actionId.ToString(), ("Type", "Control"));

        TransactionTypeClassifier.IsGovernanceActionTransaction(tx).Should().BeTrue();
    }

    [Fact]
    public void IsGovernanceActionTransaction_CryptoPolicyUpdate_ReturnsFalse()
    {
        // The crypto-policy update is a Control transaction on the governance blueprint, but its
        // ActionId is the free-form "control.crypto.update" that ControlDocketProcessor matches on,
        // its payload is a policy document rather than a ControlTransactionPayload, and the
        // blueprint declares no contract for it. Withdrawing its schema exemption would reject it
        // outright with VAL_SCHEMA_002 — the action id does not parse as an int.
        var tx = Governance("control.crypto.update", ("Type", "Control"));

        TransactionTypeClassifier.IsGovernanceActionTransaction(tx).Should().BeFalse();
        ExemptionAuthorityResolver.ReadClaim(tx).Kind.Should().Be(ExemptionKind.Control,
            "it still claims the exemption it has always claimed — Feature 196 changed who may be "
            + "granted one, not which transactions ask");
    }

    [Fact]
    public void IsGovernanceActionTransaction_PublishOfTheGovernanceBlueprint_ReturnsFalse()
    {
        // Seeding register-governance-v1 onto the system register carries the governance blueprint
        // id, but its payload is the blueprint body — not an action against it. This is the same
        // distinction RightsEnforcementService draws before applying the roster check (#917); a
        // publish that lost its schema exemption would be judged against a contract describing a
        // governance proposal.
        var tx = Governance(
            GovernanceBlueprint.ProposeChangeActionId.ToString(),
            ("Type", "Control"),
            ("transactionType", "BlueprintPublish"));

        TransactionTypeClassifier.IsGovernanceActionTransaction(tx).Should().BeFalse();
    }

    [Fact]
    public void IsGovernanceActionTransaction_AnActionTheBlueprintDoesNotSubmitAgainst_ReturnsFalse()
    {
        // Actions 0 (Assert Ownership) and 3 (Accept Role) exist in the published definition but no
        // producer submits against them. Naming them here would claim a contract nothing emits.
        TransactionTypeClassifier.IsGovernanceActionTransaction(Governance("0")).Should().BeFalse();
        TransactionTypeClassifier.IsGovernanceActionTransaction(Governance("3")).Should().BeFalse();
    }

    [Fact]
    public void IsGovernanceActionTransaction_OtherBlueprint_ReturnsFalse()
    {
        // An ordinary workflow that happens to use action id 1 must not be dragged into the
        // governance contract.
        var tx = OnBlueprint(
            "some-council-blueprint",
            GovernanceBlueprint.ProposeChangeActionId.ToString(),
            ("Type", "Control"));

        TransactionTypeClassifier.IsGovernanceActionTransaction(tx).Should().BeFalse();
    }

    [Fact]
    public void IsGovernanceActionTransaction_IsAWithdrawal_SoGenesisKeepsItsExemption()
    {
        // The predicate is only ever used as `exemption.Granted && !IsGovernanceAction`. Genesis
        // must never satisfy it, or the trust anchor would be judged against a governance contract
        // it cannot possibly meet and no network could bootstrap.
        var genesis = Build(GenesisConstants.BlueprintId, "{}", ("Type", "Genesis"));

        TransactionTypeClassifier.IsGovernanceActionTransaction(genesis).Should().BeFalse();
        ExemptionAuthorityResolver.ReadClaim(genesis).Kind.Should().Be(ExemptionKind.RegisterGenesis);
    }
}
