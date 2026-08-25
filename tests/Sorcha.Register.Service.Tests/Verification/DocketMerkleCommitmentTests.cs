// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Service.Verification;
using Sorcha.Verification.Abstractions;
using Xunit;

namespace Sorcha.Register.Service.Tests.Verification;

/// <summary>
/// Issue #1372 — a docket could not verify itself, because the root its proposing validator sealed
/// was discarded at write time and recomputed on demand.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recomputation alone is not a check.</b> A root recomputed over altered stored data is
/// internally self-consistent: an inclusion proof generated against it verifies perfectly, and the
/// commitment the validator actually made is never consulted, because it was never kept.
/// <see cref="AlteredContents_FailAgainstTheSealedRoot_RatherThanPassingAgainstTheirOwn"/> executes
/// that sentence rather than asserting it — it proves the tampered set really does produce a
/// self-consistent root before showing the sealed comparison catch it. Without that first half the
/// test would pass just as well if tampering were impossible, which is the vacuous-guard shape.
/// </para>
/// <para>
/// <b>The leaf rule is the other half, and getting it wrong is worse than not checking.</b> The tree
/// is built from per-transaction composite hashes, not from raw transaction ids, so a check written
/// over ids reports tampering on every docket of every register — a false accusation on a sound
/// ledger. That mistake has been made twice here (Feature 188 found it against real n1 dockets;
/// <c>POST /proofs/inclusion</c> was still making it).
/// </para>
/// </remarks>
public class DocketMerkleCommitmentTests
{
    private static readonly HashProvider Hasher = new();
    private static DocketHasher DocketHasher => new(Hasher);
    private static MerkleTree Merkle => new(Hasher);

    private static TransactionModel Tx(string id, string payloadHash, DateTime at) => new()
    {
        TxId = id,
        RegisterId = "reg-1",
        TimeStamp = at,
        Payloads = [new PayloadModel { Hash = payloadHash, Data = string.Empty }]
    };

    private static readonly DateTime T0 = new(2026, 8, 25, 9, 0, 0, DateTimeKind.Utc);

    private static List<TransactionModel> ThreeTransactions() =>
    [
        Tx("tx-1", "hash-1", T0),
        Tx("tx-2", "hash-2", T0.AddSeconds(1)),
        Tx("tx-3", "hash-3", T0.AddSeconds(2)),
    ];

    /// <summary>A docket sealed over the given transactions, with the root a validator would have produced.</summary>
    private static DocketHeader SealedOver(IReadOnlyList<TransactionModel> transactions)
    {
        var hasher = DocketHasher;
        var leaves = transactions
            .Select(t => hasher.ComputeTransactionHash(
                t.TxId!, t.Payloads![0].Hash, new DateTimeOffset(t.TimeStamp, TimeSpan.Zero)))
            .ToList();

        return new DocketHeader
        {
            Id = 7,
            RegisterId = "reg-1",
            State = DocketState.Sealed,
            TransactionIds = transactions.Select(t => t.TxId!).ToList(),
            MerkleRoot = Merkle.ComputeMerkleRoot(leaves)
        };
    }

    // -----------------------------------------------------------------------------------------
    // The leaf rule
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ADocketDoesNotCommitToItsTransactionIds()
    {
        var txs = ThreeTransactions();
        var docket = SealedOver(txs);

        var overIds = Merkle.ComputeMerkleRoot(docket.TransactionIds);

        overIds.Should().NotBe(docket.MerkleRoot,
            "the tree is built from composite (id, payloadHash, timestamp) hashes — a check written over "
            + "raw ids reports tampering on every docket of every register");
    }

    [Fact]
    public void HealthyContents_ReproduceTheSealedRoot()
    {
        var txs = ThreeTransactions();
        var docket = SealedOver(txs);

        var seal = DocketMerkleCommitment.Compare(
            docket, DocketMerkleCommitment.BuildLeaves(docket, txs, DocketHasher), Merkle);

        seal.Status.Should().Be(VerificationStatus.Verified);
        seal.RecomputedRoot.Should().Be(docket.MerkleRoot);
    }

    [Fact]
    public void LeavesFollowTheDocketsIdList_NotTheOrderTheStoreReturns()
    {
        var txs = ThreeTransactions();
        var docket = SealedOver(txs);

        // Same transactions, different repository ordering. The committed order is the docket's.
        var shuffled = new List<TransactionModel> { txs[2], txs[0], txs[1] };

        var seal = DocketMerkleCommitment.Compare(
            docket, DocketMerkleCommitment.BuildLeaves(docket, shuffled, DocketHasher), Merkle);

        seal.Status.Should().Be(VerificationStatus.Verified,
            "ordering by whatever the store returned would make the id list itself uncommitted");
    }

    [Fact]
    public void ReorderingTheIdList_ChangesTheRoot()
    {
        var txs = ThreeTransactions();
        var docket = SealedOver(txs);

        docket.TransactionIds = [docket.TransactionIds[1], docket.TransactionIds[0], docket.TransactionIds[2]];

        var seal = DocketMerkleCommitment.Compare(
            docket, DocketMerkleCommitment.BuildLeaves(docket, txs, DocketHasher), Merkle);

        seal.Status.Should().Be(VerificationStatus.Failed,
            "order is part of the commitment — if reordering passed, the tamper check would be vacuous");
    }

    // -----------------------------------------------------------------------------------------
    // T022 — the point of the whole issue
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData("payload")]
    [InlineData("timestamp")]
    [InlineData("removal")]
    public void AlteredContents_FailAgainstTheSealedRoot_RatherThanPassingAgainstTheirOwn(string alteration)
    {
        var original = ThreeTransactions();
        var docket = SealedOver(original);

        var altered = ThreeTransactions();
        switch (alteration)
        {
            case "payload":
                altered[1].Payloads![0].Hash = "hash-2-tampered";
                break;
            case "timestamp":
                altered[1].TimeStamp = altered[1].TimeStamp.AddSeconds(30);
                break;
            case "removal":
                altered.RemoveAt(1);
                break;
        }

        var leaves = DocketMerkleCommitment.BuildLeaves(docket, altered, DocketHasher);

        if (alteration == "removal")
        {
            // The docket still lists tx-2, which this node no longer holds. That is UNVERIFIABLE,
            // not tampering: the same state a partial replica is legitimately in.
            leaves.Should().BeNull();
            DocketMerkleCommitment.Compare(docket, leaves, Merkle).Status
                .Should().Be(VerificationStatus.Unverified,
                    "absence of evidence must never be reported as evidence of tampering");
            return;
        }

        // FIRST: the attack is real. The altered set produces a root that is perfectly
        // self-consistent — this is what "recompute on demand" would have handed back, and what an
        // inclusion proof generated from it would verify against. A test that skipped this half
        // would pass identically if tampering were impossible.
        var selfConsistentRoot = Merkle.ComputeMerkleRoot(leaves!.Hashes);
        selfConsistentRoot.Should().NotBeNullOrWhiteSpace();
        selfConsistentRoot.Should().NotBe(docket.MerkleRoot,
            "the alteration must actually move the root, or this case proves nothing");

        // THEN: keeping the sealed commitment is what catches it.
        var seal = DocketMerkleCommitment.Compare(docket, leaves, Merkle);

        seal.Status.Should().Be(VerificationStatus.Failed);
        seal.SealedRoot.Should().Be(docket.MerkleRoot);
        seal.RecomputedRoot.Should().Be(selfConsistentRoot);
        seal.IsAnchored.Should().BeFalse();
    }

    // -----------------------------------------------------------------------------------------
    // The tri-state
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ADocketSealedBeforeTheCommitmentWasKept_IsUnverified_NotVerifiedAndNotFailed()
    {
        var txs = ThreeTransactions();
        var docket = SealedOver(txs);
        docket.MerkleRoot = string.Empty;   // exactly how a pre-Feature-187 docket arrives

        var seal = DocketMerkleCommitment.Compare(
            docket, DocketMerkleCommitment.BuildLeaves(docket, txs, DocketHasher), Merkle);

        seal.Status.Should().Be(VerificationStatus.Unverified);
        seal.IsAnchored.Should().BeFalse("a check that could not run is not a pass");
    }

    [Fact]
    public void IsAnchored_IsVerifiedOnly_NotMerelyNotFailed()
    {
        // The naive spelling of this predicate is `Status != Failed`, which quietly promotes every
        // unverifiable docket to anchored — the single most damaging thing this type could do.
        new[] { VerificationStatus.Unverified, VerificationStatus.Failed }
            .Select(s => new DocketMerkleCommitment.SealComparison(s, "a", "b", "why").IsAnchored)
            .Should().AllSatisfy(anchored => anchored.Should().BeFalse());

        new DocketMerkleCommitment.SealComparison(VerificationStatus.Verified, "a", "a", null)
            .IsAnchored.Should().BeTrue();
    }

    [Fact]
    public void AnEmptyDocket_HasAWellDefinedRoot_AndVerifies()
    {
        var docket = SealedOver([]);

        var seal = DocketMerkleCommitment.Compare(
            docket, DocketMerkleCommitment.BuildLeaves(docket, [], DocketHasher), Merkle);

        seal.Status.Should().Be(VerificationStatus.Verified);
    }

    [Fact]
    public void TheWireForm_IsLowercase_SoAConsumerCanSwitchOnIt()
    {
        new DocketMerkleCommitment.SealComparison(VerificationStatus.Verified, "a", "a", null).Wire
            .Should().Be("verified");
        new DocketMerkleCommitment.SealComparison(VerificationStatus.Unverified, null, null, "r").Wire
            .Should().Be("unverified");
        new DocketMerkleCommitment.SealComparison(VerificationStatus.Failed, "a", "b", "r").Wire
            .Should().Be("failed");
    }
}
