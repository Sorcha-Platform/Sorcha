// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Xunit;

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
            Signatures = new List<Signature>(),
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
    public void IsGenesisOrControlTransaction_TypeMetadataMatches_ReturnsTrue(string type)
    {
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

    [Theory]
    [InlineData("PresentationInitiated", true)]
    [InlineData("PresentationOutcome", true)]
    [InlineData("PresentationAbandoned", true)]
    [InlineData("presentationoutcome", true)]
    [InlineData("Action", false)]
    [InlineData("Control", false)]
    public void IsLifecycleTransaction_CoversAllThreeLifecycleTypes(string type, bool expected)
    {
        var tx = TxWithMetadata(("Type", type));

        TransactionTypeClassifier.IsLifecycleTransaction(tx).Should().Be(expected);
    }

    [Theory]
    [InlineData("PresentationOutcome", true)]
    [InlineData("PresentationAbandoned", true)]
    [InlineData("PresentationInitiated", false)] // intentionally excluded — gets full reachability check
    [InlineData("Action", false)]
    public void IsIntraActionLifecycleTerminal_ExcludesInitiated(string type, bool expected)
    {
        var tx = TxWithMetadata(("Type", type));

        TransactionTypeClassifier.IsIntraActionLifecycleTerminal(tx).Should().Be(expected);
    }

    [Fact]
    public void IsLifecycleTransaction_NoTypeMetadata_ReturnsFalse()
    {
        var tx = TxWithMetadata();

        TransactionTypeClassifier.IsLifecycleTransaction(tx).Should().BeFalse();
        TransactionTypeClassifier.IsIntraActionLifecycleTerminal(tx).Should().BeFalse();
    }
}
