// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// The Chain check: does this docket link to its predecessor?
/// </summary>
/// <remarks>
/// <b>The load-bearing case is <see cref="PredecessorNotHeldByThisNode_IsUnverified_NotFailed"/>.</b>
/// Sorcha nodes routinely hold part of a register — a subscriber syncs what it needs. If "I do not
/// have the previous docket" reported <see cref="VerificationStatus.Failed"/>, every subscribing
/// node in the network would render as compromised, and the operator's correct response to a genuine
/// failure would be trained out of them by the noise.
/// </remarks>
public class ChainCheckTests
{
    private static ProvenanceCheck Chain(DocketEvidence docket) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(
                TestEvidence.RegisterId,
                docket,
                TestEvidence.Roster(1, TestEvidence.Validator("validator-a", "key-a")),
                TestEvidence.TraceableAnchor())
            .Checks.Single(c => c.Layer == ProvenanceLayer.Chain);

    [Fact]
    public void ClaimedPreviousHashMatchingThePredecessor_IsVerified()
    {
        var check = Chain(TestEvidence.HealthyDocket(number: 10));

        check.Status.Should().Be(VerificationStatus.Verified);
        check.CheckedAgainst.Should().Contain("predecessor");
        check.Reason.Should().BeNull();
    }

    [Fact]
    public void ClaimedPreviousHashNotMatchingThePredecessor_IsFailed()
    {
        var check = Chain(TestEvidence.HealthyDocket(number: 10) with
        {
            ClaimedPreviousHash = "hash-of-something-else",
        });

        check.Status.Should().Be(VerificationStatus.Failed);
        check.Detail.Should().Contain("hash-of-something-else");
        check.Detail.Should().Contain("hash-9", "the reader needs both sides of the mismatch");
    }

    [Fact]
    public void PredecessorNotHeldByThisNode_IsUnverified_NotFailed()
    {
        var check = Chain(TestEvidence.HealthyDocket(number: 10) with
        {
            PredecessorHash = null,
        });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed,
            "a node holding part of a register is a normal deployment, not a compromised one");
        check.Reason.Should().NotBeNullOrWhiteSpace();
        check.Reason.Should().Contain("9", "the reason must name WHICH link could not be established (SC-009)");
    }

    /// <summary>
    /// The genesis docket has no predecessor to link to. That is a property of being first, not a
    /// broken chain.
    /// </summary>
    [Fact]
    public void GenesisDocket_IsUnverified_NotFailed()
    {
        var check = Chain(new DocketEvidence
        {
            DocketNumber = 0,
            Hash = "hash-0",
            ClaimedPreviousHash = null,
            PredecessorHash = null,
            TransactionIds = ["tx-genesis"],
            SealedMerkleRoot = StubMerkle.RootOf(["tx-genesis"]),
            ProposerValidatorId = "validator-a",
        });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A docket that records no predecessor while this node demonstrably holds one is a genuine
    /// contradiction in the stored evidence, and must not be softened into "unverified".
    /// </summary>
    [Fact]
    public void ClaimingNoPredecessorWhenOneIsHeld_IsFailed()
    {
        var check = Chain(TestEvidence.HealthyDocket(number: 10) with
        {
            ClaimedPreviousHash = null,
        });

        check.Status.Should().Be(VerificationStatus.Failed);
    }
}
