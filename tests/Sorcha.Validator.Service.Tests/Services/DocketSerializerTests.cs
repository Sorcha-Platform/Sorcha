// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;

namespace Sorcha.Validator.Service.Tests.Services;

public class DocketSerializerTests
{
    #region SerializeToBytes Tests

    [Fact]
    public void SerializeToBytes_ValidDocket_ReturnsBytes()
    {
        // Arrange
        var docket = CreateValidDocket();

        // Act
        var bytes = DocketSerializer.SerializeToBytes(docket);

        // Assert
        bytes.Should().NotBeEmpty();
    }

    [Fact]
    public void SerializeToBytes_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => DocketSerializer.SerializeToBytes(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SerializeToBytes_DocketWithTransactions_IncludesTransactions()
    {
        // Arrange
        var docket = CreateDocketWithTransactions();

        // Act
        var bytes = DocketSerializer.SerializeToBytes(docket);
        var deserialized = DocketSerializer.DeserializeFromBytes(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Transactions.Should().HaveCount(2);
    }

    [Fact]
    public void SerializeToBytes_DocketWithVotes_IncludesVotes()
    {
        // Arrange
        var docket = CreateConfirmedDocket();

        // Act
        var bytes = DocketSerializer.SerializeToBytes(docket);
        var deserialized = DocketSerializer.DeserializeFromBytes(bytes);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Votes.Should().HaveCount(2);
    }

    #endregion

    #region DeserializeFromBytes Tests

    [Fact]
    public void DeserializeFromBytes_ValidBytes_ReturnsDocket()
    {
        // Arrange
        var original = CreateValidDocket();
        var bytes = DocketSerializer.SerializeToBytes(original);

        // Act
        var result = DocketSerializer.DeserializeFromBytes(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.DocketId.Should().Be(original.DocketId);
        result.RegisterId.Should().Be(original.RegisterId);
        result.DocketNumber.Should().Be(original.DocketNumber);
        result.DocketHash.Should().Be(original.DocketHash);
    }

    [Fact]
    public void DeserializeFromBytes_NullBytes_ReturnsNull()
    {
        // Act
        var result = DocketSerializer.DeserializeFromBytes(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DeserializeFromBytes_EmptyBytes_ReturnsNull()
    {
        // Act
        var result = DocketSerializer.DeserializeFromBytes(Array.Empty<byte>());

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void DeserializeFromBytes_PreservesSignatures()
    {
        // Arrange
        var original = CreateValidDocket();
        var bytes = DocketSerializer.SerializeToBytes(original);

        // Act
        var result = DocketSerializer.DeserializeFromBytes(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.ProposerSignature.Should().NotBeNull();
        result.ProposerSignature.Algorithm.Should().Be(original.ProposerSignature.Algorithm);
    }

    #endregion

    #region ToRegisterModel Tests

    [Fact]
    public void ToRegisterModel_ValidDocket_ReturnsDocketModel()
    {
        // Arrange
        var docket = CreateDocketWithTransactions();

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert
        model.Should().NotBeNull();
        model.DocketId.Should().Be(docket.DocketId);
        model.RegisterId.Should().Be(docket.RegisterId);
        model.DocketNumber.Should().Be(docket.DocketNumber);
        model.DocketHash.Should().Be(docket.DocketHash);
        model.MerkleRoot.Should().Be(docket.MerkleRoot);
        model.ProposerValidatorId.Should().Be(docket.ProposerValidatorId);
    }

    [Fact]
    public void ToRegisterModel_NullDocket_ThrowsArgumentNullException()
    {
        // Act
        var act = () => DocketSerializer.ToRegisterModel(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToRegisterModel_ConvertsTransactions()
    {
        // Arrange
        var docket = CreateDocketWithTransactions();

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert
        model.Transactions.Should().HaveCount(2);
        model.Transactions[0].TxId.Should().Be("tx-1");
        model.Transactions[1].TxId.Should().Be("tx-2");
    }

    [Fact]
    public void ToRegisterModel_MapsRecipientsWallets()
    {
        // Arrange
        var docket = CreateValidDocket();
        var tx = CreateTransaction("tx-with-recipients");
        tx = new Transaction
        {
            TransactionId = tx.TransactionId,
            RegisterId = tx.RegisterId,
            BlueprintId = tx.BlueprintId,
            ActionId = tx.ActionId,
            Payload = tx.Payload,
            PayloadHash = tx.PayloadHash,
            CreatedAt = tx.CreatedAt,
            Signatures = tx.Signatures,
            Metadata = tx.Metadata,
            RecipientsWallets = ["ws11qalice", "ws11qbob"]
        };
        docket.Transactions.Add(tx);

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert
        model.Transactions.Should().HaveCount(1);
        model.Transactions[0].RecipientsWallets.Should().BeEquivalentTo(["ws11qalice", "ws11qbob"]);
    }

    [Fact]
    public void ToRegisterModel_NullRecipientsWallets_MapsToEmptyList()
    {
        // Arrange — CreateTransaction does not set RecipientsWallets (defaults to null)
        var docket = CreateDocketWithTransactions();

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert
        model.Transactions.Should().HaveCount(2);
        model.Transactions[0].RecipientsWallets.Should().BeEmpty();
        model.Transactions[1].RecipientsWallets.Should().BeEmpty();
    }

    // PR #883 regression: the persisted-projection MUST carry the submission Metadata through to
    // TransactionMetaData.TrackingData. Omitting it (as the parallel DocketBuildTriggerService
    // authoritative path did) drops the F138 US4 blueprint contentHash, so a recovering subscriber
    // rejects every blueprint with no_provenance. Both projections must satisfy this contract.
    [Fact]
    public void ToRegisterModel_PreservesSubmissionMetadataAsTrackingData()
    {
        // Arrange
        var docket = CreateValidDocket();
        var tx = CreateTransaction("tx-with-metadata");
        tx.Metadata["Type"] = "BlueprintPublish";
        tx.Metadata["contentHash"] = "212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea";
        tx.Metadata["publishedBy"] = "system";
        docket.Transactions.Add(tx);

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert
        var meta = model.Transactions[0].MetaData;
        meta.Should().NotBeNull();
        meta!.TrackingData.Should().NotBeNull();
        meta.TrackingData!.Should().ContainKey("contentHash")
            .WhoseValue.Should().Be("212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea");
        meta.TrackingData.Should().ContainKey("publishedBy");
        meta.TransactionType.Should().Be(Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish);
    }

    [Fact]
    public void ToRegisterModel_EmptyMetadata_TrackingDataIsNull()
    {
        // Arrange — CreateTransaction starts with an empty Metadata dictionary.
        var docket = CreateDocketWithTransactions();

        // Act
        var model = DocketSerializer.ToRegisterModel(docket);

        // Assert — empty metadata maps to null TrackingData (not an empty dict).
        model.Transactions[0].MetaData!.TrackingData.Should().BeNull();
    }

    #endregion

    #region Round Trip Tests

    [Fact]
    public void RoundTrip_DocketWithAllFields_PreservesAllData()
    {
        // Arrange
        var original = CreateConfirmedDocket();

        // Act
        var bytes = DocketSerializer.SerializeToBytes(original);
        var result = DocketSerializer.DeserializeFromBytes(bytes);

        // Assert
        result.Should().NotBeNull();
        result!.DocketId.Should().Be(original.DocketId);
        result.RegisterId.Should().Be(original.RegisterId);
        result.DocketNumber.Should().Be(original.DocketNumber);
        result.DocketHash.Should().Be(original.DocketHash);
        result.PreviousHash.Should().Be(original.PreviousHash);
        result.MerkleRoot.Should().Be(original.MerkleRoot);
        result.ProposerValidatorId.Should().Be(original.ProposerValidatorId);
        result.Transactions.Should().HaveCount(original.Transactions.Count);
        result.Votes.Should().HaveCount(original.Votes.Count);
    }

    #endregion

    #region Helper Methods

    private static Docket CreateValidDocket()
    {
        return new Docket
        {
            DocketId = "docket-123",
            RegisterId = "register-1",
            DocketNumber = 5,
            DocketHash = "hash-abc123",
            PreviousHash = "hash-prev",
            MerkleRoot = "merkle-root",
            CreatedAt = DateTimeOffset.UtcNow,
            ProposerValidatorId = "validator-1",
            Status = DocketStatus.Proposed,
            ProposerSignature = new Signature
            {
                PublicKey = new byte[] { 1, 2, 3 },
                SignatureValue = new byte[] { 4, 5, 6 },
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            },
            Transactions = new List<Transaction>()
        };
    }

    private static Docket CreateDocketWithTransactions()
    {
        var docket = CreateValidDocket();
        docket.Transactions.Add(CreateTransaction("tx-1"));
        docket.Transactions.Add(CreateTransaction("tx-2"));
        return docket;
    }

    private static Transaction CreateTransaction(string transactionId)
    {
        return new Transaction
        {
            TransactionId = transactionId,
            RegisterId = "register-1",
            BlueprintId = "blueprint-1",
            ActionId = "1",
            Payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"),
            PayloadHash = $"hash-{transactionId}",
            CreatedAt = DateTimeOffset.UtcNow,
            Priority = TransactionPriority.Normal,
            Signatures = new List<Signature>
            {
                new()
                {
                    PublicKey = new byte[] { 1, 2, 3 },
                    SignatureValue = new byte[] { 4, 5, 6 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            Metadata = new Dictionary<string, string>()
        };
    }

    private static Docket CreateConfirmedDocket()
    {
        var docket = CreateDocketWithTransactions();
        return new Docket
        {
            DocketId = docket.DocketId,
            RegisterId = docket.RegisterId,
            DocketNumber = docket.DocketNumber,
            DocketHash = docket.DocketHash,
            PreviousHash = docket.PreviousHash,
            MerkleRoot = docket.MerkleRoot,
            CreatedAt = docket.CreatedAt,
            ProposerValidatorId = docket.ProposerValidatorId,
            Status = DocketStatus.Confirmed,
            ProposerSignature = docket.ProposerSignature,
            Transactions = docket.Transactions,
            Votes = new List<ConsensusVote>
            {
                new()
                {
                    VoteId = "vote-1",
                    DocketId = docket.DocketId,
                    ValidatorId = "val-1",
                    Decision = VoteDecision.Approve,
                    VotedAt = DateTimeOffset.UtcNow,
                    DocketHash = docket.DocketHash,
                    ValidatorSignature = new Signature
                    {
                        PublicKey = new byte[] { 1, 2, 3 },
                        SignatureValue = new byte[] { 4, 5, 6 },
                        Algorithm = "ED25519",
                        SignedAt = DateTimeOffset.UtcNow
                    }
                },
                new()
                {
                    VoteId = "vote-2",
                    DocketId = docket.DocketId,
                    ValidatorId = "val-2",
                    Decision = VoteDecision.Approve,
                    VotedAt = DateTimeOffset.UtcNow,
                    DocketHash = docket.DocketHash,
                    ValidatorSignature = new Signature
                    {
                        PublicKey = new byte[] { 7, 8, 9 },
                        SignatureValue = new byte[] { 10, 11, 12 },
                        Algorithm = "ED25519",
                        SignedAt = DateTimeOffset.UtcNow
                    }
                }
            }
        };
    }

    #endregion
}
