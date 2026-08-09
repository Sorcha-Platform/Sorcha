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
    public void IsGenesisOrControlTransaction_BlueprintIdIsGenesis_ReturnsTrue()
    {
        var tx = Build(blueprintId: GenesisConstants.BlueprintId, payloadJson: "{}");

        TransactionTypeClassifier.IsGenesisOrControlTransaction(tx).Should().BeTrue();
    }

    [Theory]
    [InlineData("Genesis")]
    [InlineData("genesis")]
    [InlineData("Control")]
    [InlineData("CONTROL")]
    [InlineData("BlueprintPublish")]
    [InlineData("blueprintpublish")]
    public void IsGenesisOrControlTransaction_TypeMetadataMatches_ReturnsTrue(string type)
    {
        // BlueprintPublish (post-#876) joins Genesis+Control in bypassing action-schema
        // validation and per-sender replay protection: the publish is signed by the
        // system wallet with an ActionId of "blueprint-publish" (not an int) and no
        // per-sender sequence number — both checks would reject otherwise.
        var tx = TxWithMetadata(("Type", type));

        TransactionTypeClassifier.IsGenesisOrControlTransaction(tx).Should().BeTrue();
    }

    [Fact]
    public void IsGenesisOrControlTransaction_RegularAction_ReturnsFalse()
    {
        var tx = TxWithMetadata(("Type", "Action"));

        TransactionTypeClassifier.IsGenesisOrControlTransaction(tx).Should().BeFalse();
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
}
