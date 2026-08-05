// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// The Anchor check: does this register's origin trace to the trust anchor this node holds?
/// </summary>
/// <remarks>
/// The interesting case is the third one. A node whose anchor does not match the network — or which
/// holds no anchor at all — must report <see cref="VerificationStatus.Unverified"/>, not
/// <see cref="VerificationStatus.Failed"/>. It cannot tell the difference between "this register is
/// not ours" and "we do not know what ours is", and claiming the former would be an accusation
/// founded on our own missing configuration. See issue #1374, which tracks making the anchor
/// independently configurable.
/// </remarks>
public class AnchorCheckTests
{
    private static ProvenanceCheck Anchor(AnchorEvidence? anchor) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(TestEvidence.RegisterId, TestEvidence.HealthyDocket(), roster(), anchor)
            .Checks.Single(c => c.Layer == ProvenanceLayer.Anchor);

    private static RosterAsOf roster() =>
        TestEvidence.Roster(1, TestEvidence.Validator("validator-a", "key-a"));

    [Fact]
    public void OriginTracingToTheAnchor_IsVerified()
    {
        var check = Anchor(TestEvidence.TraceableAnchor());

        check.Status.Should().Be(VerificationStatus.Verified);
        check.CheckedAgainst.Should().Contain("trust anchor");
        check.Reason.Should().BeNull("a check that ran has nothing to excuse");
    }

    [Fact]
    public void OriginNotMatchingTheAnchor_IsFailed()
    {
        var check = Anchor(TestEvidence.TraceableAnchor() with
        {
            OriginFingerprint = "0000000000000000000000000000dead",
        });

        check.Status.Should().Be(VerificationStatus.Failed);
        check.Detail.Should().Contain("0000000000000000000000000000dead",
            "the reader needs both fingerprints to act on a mismatch");
        check.Detail.Should().Contain(TestEvidence.AnchorFingerprint);
    }

    [Fact]
    public void AnchorNotKnownToThisNode_IsUnverified_NotFailed()
    {
        var check = Anchor(new AnchorEvidence { IsAnchorKnown = false });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().NotBeNullOrWhiteSpace();
        check.Status.Should().NotBe(VerificationStatus.Failed,
            "a node that does not know what it trusts is misconfigured, not compromised (#1374)");
    }

    [Fact]
    public void NoAnchorEvidenceAtAll_IsUnverified()
    {
        var check = Anchor(null);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A register whose origin the node's anchor does not cover — the ordinary case for a register
    /// created by an organisation rather than by the network genesis ceremony. The assembler says
    /// why; the engine surfaces that verbatim rather than inventing a verdict.
    /// </summary>
    [Fact]
    public void OriginThatCannotBeTraced_IsUnverified_WithTheAssemblersReason()
    {
        var check = Anchor(new AnchorEvidence
        {
            IsAnchorKnown = true,
            AnchorFingerprint = TestEvidence.AnchorFingerprint,
            OriginFingerprint = null,
            UntraceableReason = "this register's origin is attested by its owner, not by the network genesis key",
        });

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().Be(
            "this register's origin is attested by its owner, not by the network genesis key");
    }

    [Fact]
    public void FingerprintComparison_IsCaseInsensitive()
    {
        var check = Anchor(TestEvidence.TraceableAnchor() with
        {
            OriginFingerprint = TestEvidence.AnchorFingerprint.ToUpperInvariant(),
        });

        check.Status.Should().Be(VerificationStatus.Verified,
            "fingerprints are hex; a casing difference is not a mismatch and must not read as tampering");
    }
}
