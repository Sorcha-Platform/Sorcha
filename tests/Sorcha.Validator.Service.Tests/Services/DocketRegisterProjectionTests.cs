// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Transaction = Sorcha.Validator.Service.Models.Transaction;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Behavioural pins for <see cref="DocketRegisterProjection"/>.
/// </summary>
/// <remarks>
/// Ported from <c>DocketSerializerTests</c>'s <c>ToRegisterModel</c> region during Feature 187
/// (#1370), which collapsed the two competing docket→register projections into one and deleted
/// <c>DocketSerializer.ToRegisterModel</c>. The assertions are unchanged in substance — they pin
/// behaviour that had already been paid for in production incidents, so they moved rather than died
/// with the method. Field-level completeness is covered separately and by reflection in
/// <see cref="DocketProjectionCompletenessTests"/>.
/// </remarks>
public class DocketRegisterProjectionTests
{
    [Fact]
    public void ToDocketModel_ValidDocket_MapsDocketLevelFields()
    {
        var docket = CreateDocketWithTransactions();

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Should().NotBeNull();
        model.DocketId.Should().Be(docket.DocketId);
        model.RegisterId.Should().Be(docket.RegisterId);
        model.DocketNumber.Should().Be(docket.DocketNumber);
        model.DocketHash.Should().Be(docket.DocketHash);
        model.MerkleRoot.Should().Be(docket.MerkleRoot);
        model.ProposerValidatorId.Should().Be(docket.ProposerValidatorId);
    }

    [Fact]
    public void ToDocketModel_NullDocket_ThrowsArgumentNullException()
    {
        var act = () => DocketRegisterProjection.ToDocketModel(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToDocketModel_ConvertsTransactions()
    {
        var docket = CreateDocketWithTransactions();

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions.Should().HaveCount(2);
        model.Transactions[0].TxId.Should().Be("tx-1");
        model.Transactions[1].TxId.Should().Be("tx-2");
    }

    [Fact]
    public void ToDocketModel_MapsRecipientsWallets()
    {
        var docket = CreateValidDocket();
        docket.Transactions.Add(CreateTransaction(
            "tx-with-recipients", recipientsWallets: ["ws11qalice", "ws11qbob"]));

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions.Should().ContainSingle();
        model.Transactions[0].RecipientsWallets.Should().BeEquivalentTo(["ws11qalice", "ws11qbob"]);
    }

    [Fact]
    public void ToDocketModel_NullRecipientsWallets_MapsToEmptyList()
    {
        // CreateTransaction does not set RecipientsWallets (defaults to null).
        var docket = CreateDocketWithTransactions();

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions.Should().HaveCount(2);
        model.Transactions[0].RecipientsWallets.Should().BeEmpty();
        model.Transactions[1].RecipientsWallets.Should().BeEmpty();
    }

    // PR #883 regression: the projection MUST carry the submission Metadata through to
    // TransactionMetaData.TrackingData. Omitting it drops the F138 US4 blueprint contentHash, so a
    // recovering subscriber rejects every blueprint with no_provenance.
    [Fact]
    public void ToDocketModel_PreservesSubmissionMetadataAsTrackingData()
    {
        var docket = CreateValidDocket();
        var tx = CreateTransaction("tx-with-metadata");
        tx.Metadata["Type"] = "BlueprintPublish";
        tx.Metadata["contentHash"] = "212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea";
        tx.Metadata["publishedBy"] = "system";
        docket.Transactions.Add(tx);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        var meta = model.Transactions[0].MetaData;
        meta.Should().NotBeNull();
        meta!.TrackingData.Should().NotBeNull();
        meta.TrackingData!.Should().ContainKey("contentHash")
            .WhoseValue.Should().Be("212b4f05497106dc11cc3db7366edad0110f0432afdb53a7d462da8060e95aea");
        meta.TrackingData.Should().ContainKey("publishedBy");
        meta.TransactionType.Should().Be(Sorcha.Register.Models.Enums.TransactionType.BlueprintPublish);
    }

    [Fact]
    public void ToDocketModel_EmptyMetadata_TrackingDataIsNull()
    {
        // CreateTransaction starts with an empty Metadata dictionary; empty maps to null TrackingData
        // (not an empty dict).
        var docket = CreateDocketWithTransactions();

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions[0].MetaData!.TrackingData.Should().BeNull();
    }

    [Fact]
    public void ToDocketModel_NonNumericActionId_MapsToNull()
    {
        // Genesis / control / blueprint-publish transactions carry a free-form string ActionId that
        // deliberately does not parse as a uint (TransactionTypeClassifier documents this). The
        // canonical model's ActionId is uint?, so those map to null — and every live reader guards on
        // HasValue, treating null as "not a blueprint action".
        var docket = CreateValidDocket();
        docket.Transactions.Add(CreateTransaction("tx-genesis", actionId: "register-creation"));

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Transactions[0].MetaData!.ActionId.Should().BeNull();
    }

    private static Docket CreateValidDocket() => new()
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
        ProposerSignature = new RegisterSignature
        {
            PublicKey = [1, 2, 3],
            SignatureValue = [4, 5, 6],
            Algorithm = "ED25519",
            SignedAt = DateTimeOffset.UtcNow
        },
        Transactions = []
    };

    private static Docket CreateDocketWithTransactions()
    {
        var docket = CreateValidDocket();
        docket.Transactions.Add(CreateTransaction("tx-1"));
        docket.Transactions.Add(CreateTransaction("tx-2"));
        return docket;
    }

    private static Transaction CreateTransaction(
        string transactionId,
        string? actionId = "1",
        List<string>? recipientsWallets = null) => new()
    {
        TransactionId = transactionId,
        RegisterId = "register-1",
        BlueprintId = "blueprint-1",
        ActionId = actionId,
        RecipientsWallets = recipientsWallets,
        Payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{}"),
        PayloadHash = $"hash-{transactionId}",
        CreatedAt = DateTimeOffset.UtcNow,
        Priority = TransactionPriority.Normal,
        Signatures =
        [
            new RegisterSignature
            {
                PublicKey = [1, 2, 3],
                SignatureValue = [4, 5, 6],
                Algorithm = "ED25519",
                SignedAt = DateTimeOffset.UtcNow
            }
        ],
        Metadata = new Dictionary<string, string>()
    };
}
