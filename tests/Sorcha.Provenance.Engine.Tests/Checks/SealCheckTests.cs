// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// The Seal check: do the docket's recorded contents still match the commitment sealed over them?
/// </summary>
/// <remarks>
/// This is the check SC-003 is measured against — altering a docket's recorded contents must produce
/// <see cref="VerificationStatus.Failed"/>, demonstrated rather than assumed. It is also the check
/// most at risk of overclaiming: recomputing a Merkle root with the same algorithm that sealed it
/// proves the stored transaction list is unchanged since sealing. It does not prove the algorithm is
/// correct, and it does not independently validate the transactions. FR-005 requires the check to
/// say so, which is why <see cref="CheckedAgainst_SaysRecomputedFromStoredIds_AndDoesNotImplyIndependentValidation"/>
/// asserts on wording.
/// </remarks>
public class SealCheckTests
{
    private static ProvenanceCheck Seal(DocketEvidence docket) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(
                TestEvidence.RegisterId,
                docket,
                TestEvidence.Roster(1, TestEvidence.Validator("validator-a", "key-a")),
                TestEvidence.TraceableAnchor())
            .Checks.Single(c => c.Layer == ProvenanceLayer.Seal);

    [Fact]
    public void RecomputedRootEqualToTheSealedRoot_IsVerified()
    {
        var check = Seal(TestEvidence.HealthyDocket());

        check.Status.Should().Be(VerificationStatus.Verified);
        check.Reason.Should().BeNull();
    }

    /// <summary>SC-003, demonstrated: alter a stored transaction id and the seal check must fail.</summary>
    [Fact]
    public void TamperedTransactionId_IsFailed()
    {
        var healthy = TestEvidence.HealthyDocket();

        var tamperedIds = new[] { "tx-1", "tampered", "tx-3" };
        var check = Seal(healthy with
        {
            TransactionIds = tamperedIds,
            LeafHashes = TestEvidence.LeavesFor(tamperedIds),
        });

        check.Status.Should().Be(VerificationStatus.Failed);
        check.Detail.Should().Contain(healthy.SealedMerkleRoot!,
            "an auditor needs the sealed root and the recomputation side by side");
    }

    /// <summary>
    /// Order is part of the commitment. A reordering that preserved the set would slip past any
    /// check that sorted before comparing.
    /// </summary>
    [Fact]
    public void ReorderedTransactionIds_IsFailed()
    {
        var reordered = new[] { "tx-3", "tx-2", "tx-1" };
        var check = Seal(TestEvidence.HealthyDocket() with
        {
            TransactionIds = reordered,
            LeafHashes = TestEvidence.LeavesFor(reordered),
        });

        check.Status.Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public void RemovedTransactionId_IsFailed()
    {
        var removed = new[] { "tx-1", "tx-2" };
        var check = Seal(TestEvidence.HealthyDocket() with
        {
            TransactionIds = removed,
            LeafHashes = TestEvidence.LeavesFor(removed),
        });

        check.Status.Should().Be(VerificationStatus.Failed);
    }

    /// <summary>
    /// A docket sealed before Feature 187 kept the sealed commitment has nothing to compare against.
    /// Absence of the commitment is not evidence that the contents changed.
    /// </summary>
    [Fact]
    public void PreFeature187Docket_WithNoSealedRoot_IsUnverified_NotFailed()
    {
        var check = Seal(TestEvidence.HealthyDocket() with { SealedMerkleRoot = null });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed,
            "no stored commitment means the question cannot be asked, not that the answer is bad");
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The register stores <c>MerkleRoot</c> as a non-nullable string defaulting to empty, so a
    /// pre-Feature-187 docket reaches the assembler as blank rather than null. Both must behave the
    /// same way, or the rule holds only for whichever form the tests happened to use.
    /// </summary>
    [Fact]
    public void BlankSealedRoot_IsTreatedTheSameAsAbsent()
    {
        var check = Seal(TestEvidence.HealthyDocket() with { SealedMerkleRoot = "   " });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A node holding the docket header but not every transaction it lists cannot recompute the
    /// commitment. Missing contents are Unverified, not tampering.
    /// </summary>
    [Fact]
    public void LeavesThatCannotBeAssembled_IsUnverified_NotFailed()
    {
        var check = Seal(TestEvidence.HealthyDocket() with { LeafHashes = null });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>FR-005 — the check must not claim more than recomputation establishes.</summary>
    [Fact]
    public void CheckedAgainst_SaysRecomputedFromStoredIds_AndDoesNotImplyIndependentValidation()
    {
        var check = Seal(TestEvidence.HealthyDocket());

        check.CheckedAgainst.Should().Contain("recomputed");
        check.CheckedAgainst.Should().Contain("stored transactions");
        check.CheckedAgainst.Should().NotContain("independent",
            "recomputing with the algorithm that sealed proves the stored data is unchanged, " +
            "not that it is independently correct (FR-005)");
    }

    /// <summary>
    /// An empty docket still has a well-defined root, so the comparison is still meaningful. This
    /// pins that the check does not quietly special-case emptiness into a pass.
    /// </summary>
    [Fact]
    public void EmptyTransactionList_StillCompares()
    {
        var verified = Seal(TestEvidence.HealthyDocket() with
        {
            TransactionIds = [],
            LeafHashes = [],
            SealedMerkleRoot = StubMerkle.RootOf([]),
        });
        verified.Status.Should().Be(VerificationStatus.Verified);

        var failed = Seal(TestEvidence.HealthyDocket() with
        {
            TransactionIds = [],
            LeafHashes = [],
            SealedMerkleRoot = "some-other-root",
        });
        failed.Status.Should().Be(VerificationStatus.Failed);
    }
}
