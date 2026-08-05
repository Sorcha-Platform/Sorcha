// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Transaction = Sorcha.Validator.Service.Models.Transaction;

namespace Sorcha.Validator.Service.Tests.Services;

/// <summary>
/// Guards the docket-level half of the projection — the fields that describe the docket itself
/// rather than its transactions: proposer, Merkle root, and the consensus votes.
/// </summary>
/// <remarks>
/// <para>
/// Feature 187 (#1371/#1372). Before it, all three failed in different ways: the proposer id was
/// smuggled through a <c>string?</c> field named <c>Votes</c>, the sealed <c>MerkleRoot</c> was
/// discarded at persistence and recomputed on demand, and real consensus votes were never written to
/// the register at all — so "this docket achieved quorum, and here are the signed votes" was not
/// recoverable from the ledger.
/// </para>
/// <para>
/// The reflection test below is deliberately shaped like <c>DocketProjectionCompletenessTests</c>:
/// it asserts over <b>every</b> DocketModel property rather than a fixed list, because a fixed list
/// is exactly what has repeatedly failed at this seam.
/// </para>
/// </remarks>
public class DocketProjectionVotesTests
{
    [Fact]
    public void Projection_CarriesConsensusVotesOntoTheLedgerModel()
    {
        var docket = CreateDocket(withVotes: true);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.Votes.Should().ContainSingle("quorum evidence must reach the ledger");
        var vote = model.Votes[0];
        vote.ValidatorId.Should().Be("validator-1");
        vote.Decision.Should().Be(VoteDecision.Approve);
        vote.DocketHash.Should().Be(docket.DocketHash);
        vote.ValidatorSignature.Algorithm.Should().Be("ED25519");
    }

    [Fact]
    public void Projection_CarriesProposerAndMerkleRoot()
    {
        var docket = CreateDocket(withVotes: true);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        model.ProposerValidatorId.Should().Be("validator-1");
        model.MerkleRoot.Should().Be("merkle-root");
    }

    [Fact]
    public void Projection_SingleValidatorMode_EmptyVoteListIsValidNotAnError()
    {
        // A node with no IConsensusEngine registered seals dockets with nothing to vote. This is the
        // normal configuration for local dev and single-node deployments, so an empty list must
        // project cleanly — never null, never an exception, and never a guard that rejects it.
        var docket = CreateDocket(withVotes: false);

        var act = () => DocketRegisterProjection.ToDocketModel(docket);

        act.Should().NotThrow();
        act().Votes.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void Projection_PopulatesEveryDocketModelProperty()
    {
        // Same guard shape as DocketProjectionCompletenessTests, one level up: every docket-level
        // property must survive the projection, so a field added to DocketModel cannot be silently
        // omitted here the way Votes, ProposerValidatorId and MerkleRoot each were.
        var docket = CreateDocket(withVotes: true);

        var model = DocketRegisterProjection.ToDocketModel(docket);

        var unpopulated = typeof(DocketModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !IsPopulated(p.GetValue(model)))
            .Select(p => p.Name)
            .ToList();

        unpopulated.Should().BeEmpty(
            "every DocketModel property must survive the projection; a docket-level field dropped " +
            "here is written nowhere and errors nowhere. Dropped: [{0}]",
            string.Join(", ", unpopulated));
    }

    private static bool IsPopulated(object? value) => value switch
    {
        null => false,
        string s => !string.IsNullOrWhiteSpace(s),
        System.Collections.ICollection c => c.Count > 0,
        _ => true
    };

    private static Docket CreateDocket(bool withVotes)
    {
        var signedAt = DateTimeOffset.UtcNow;
        var signature = new RegisterSignature
        {
            PublicKey = [1, 2, 3],
            SignatureValue = [4, 5, 6],
            Algorithm = "ED25519",
            SignedAt = signedAt,
            SignedBy = "ws1qvalidator"
        };

        var docket = new Docket
        {
            DocketId = "docket-1",
            RegisterId = "register-1",
            DocketNumber = 7,
            DocketHash = "hash-abc",
            PreviousHash = "hash-prev",
            MerkleRoot = "merkle-root",
            CreatedAt = signedAt,
            ProposerValidatorId = "validator-1",
            Status = DocketStatus.Confirmed,
            ProposerSignature = signature,
            Transactions =
            [
                new Transaction
                {
                    TransactionId = "tx-1",
                    RegisterId = "register-1",
                    BlueprintId = "blueprint-1",
                    ActionId = "1",
                    Payload = System.Text.Json.JsonSerializer
                        .Deserialize<System.Text.Json.JsonElement>("{\"a\":1}"),
                    PayloadHash = "hash-tx-1",
                    CreatedAt = signedAt,
                    PreviousTransactionId = "tx-0",
                    RecipientsWallets = ["ws1qrecipient"],
                    Priority = TransactionPriority.Normal,
                    Signatures = [signature],
                    Metadata = new Dictionary<string, string> { ["Type"] = "Action" }
                }
            ]
        };

        if (withVotes)
        {
            docket.Votes.Add(new ConsensusVote
            {
                VoteId = "vote-1",
                DocketId = docket.DocketId,
                ValidatorId = "validator-1",
                Decision = VoteDecision.Approve,
                VotedAt = signedAt,
                DocketHash = docket.DocketHash,
                ValidatorSignature = signature
            });
        }

        return docket;
    }
}
