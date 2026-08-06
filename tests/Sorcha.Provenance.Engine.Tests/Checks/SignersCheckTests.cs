// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// The Signers check: did the validators who signed hold authority at that point in history?
/// </summary>
/// <remarks>
/// <para>
/// The roster-as-of behaviour lives in <see cref="RosterAsOfTests"/>. This file covers the rest, and
/// its most important case is <see cref="EmptyVoteSet_IsUnverified_NeverVerified"/>.
/// </para>
/// <para>
/// <b>An empty vote set is the common case, not an edge case.</b> Local development, and every
/// current single-node deployment including n1, run a single validator and record no votes. There is
/// then no quorum evidence, and SC-005 requires that no check report Verified for quorum on such a
/// deployment. A green tick here would be the single most serious defect this feature can have: it
/// would report the strongest claim the platform makes — that a docket was agreed by a set of
/// independent validators — on a node where nothing of the kind happened.
/// </para>
/// </remarks>
public class SignersCheckTests
{
    private static ProvenanceCheck Signers(DocketEvidence docket, RosterAsOf? roster) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(TestEvidence.RegisterId, docket, roster, TestEvidence.TraceableAnchor())
            .Checks.Single(c => c.Layer == ProvenanceLayer.Signers);

    private static RosterAsOf TwoValidators() => TestEvidence.Roster(
        2,
        TestEvidence.Validator("validator-a", "key-a"),
        TestEvidence.Validator("validator-b", "key-b"));

    [Fact]
    public void EverySignatureAttributableToTheRosterAsOf_IsVerified()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes = [TestEvidence.Vote("validator-a", "key-a"), TestEvidence.Vote("validator-b", "key-b")],
        };

        var check = Signers(docket, TwoValidators());

        check.Status.Should().Be(VerificationStatus.Verified);
        check.Reason.Should().BeNull();
    }

    /// <summary>SC-005. The reason must name the absence, so an operator reads it as "no quorum
    /// evidence exists here", not as a hedge.</summary>
    [Fact]
    public void EmptyVoteSet_IsUnverified_NeverVerified()
    {
        var check = Signers(TestEvidence.HealthyDocket() with { Votes = [] }, TwoValidators());

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Verified,
            "reporting quorum on a single-validator deployment would be the feature lying about the " +
            "strongest claim it makes (SC-005)");
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void EmptyVoteSet_IsNotAFailureEither()
    {
        var check = Signers(TestEvidence.HealthyDocket() with { Votes = [] }, TwoValidators());

        check.Status.Should().NotBe(VerificationStatus.Failed,
            "a single-validator deployment is a supported configuration, not a broken register");
    }

    /// <summary>
    /// Attribution is on the PUBLIC KEY, not the validator id. A vote naming itself differently from
    /// the roster — the ordinary case, since dockets record configured ids and rosters record wallet
    /// addresses — must still verify when its key was authorised.
    /// </summary>
    [Fact]
    public void SignerNamedDifferentlyFromTheRosterButUsingAnAuthorisedKey_IsVerified()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes = [TestEvidence.Vote("local-validator", "key-a")],
        };

        var check = Signers(docket, TwoValidators());

        check.Status.Should().Be(VerificationStatus.Verified,
            "the key was authorised at this docket; what the signer called itself is a label, and " +
            "requiring the id to match too reports a false failure on every real register");
    }

    [Fact]
    public void SignerAbsentFromTheRosterAsOf_IsFailed()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes = [TestEvidence.Vote("validator-a", "key-a"), TestEvidence.Vote("stranger", "key-x")],
        };

        var check = Signers(docket, TwoValidators());

        check.Status.Should().Be(VerificationStatus.Failed);
        check.Detail.Should().Contain("stranger");
    }

    /// <summary>
    /// One bad signature among good ones must fail the check. A check that reported Verified because
    /// "most" signers were fine would be the same class of defect as reporting quorum where there is
    /// none.
    /// </summary>
    [Fact]
    public void OneUnattributableSignerAmongValidOnes_FailsTheWholeCheck()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes =
            [
                TestEvidence.Vote("validator-a", "key-a"),
                TestEvidence.Vote("validator-b", "key-b"),
                TestEvidence.Vote("stranger", "key-x"),
            ],
        };

        Signers(docket, TwoValidators()).Status.Should().Be(VerificationStatus.Failed);
    }

    [Fact]
    public void RosterVersionUnresolvable_IsUnverified_NotFailed()
    {
        var check = Signers(TestEvidence.HealthyDocket(), roster: null);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A roster version that resolved but recorded no members cannot vouch for anything. Treating it
    /// as an empty set that nobody belongs to would turn every signature into a failure.
    /// </summary>
    [Fact]
    public void RosterWithNoEntries_IsUnverified_NotFailed()
    {
        var empty = new RosterAsOf
        {
            RosterVersion = 7,
            Entries = [],
            ResolvedFrom = "control transaction ctrl-v7",
        };

        var check = Signers(TestEvidence.HealthyDocket(), empty);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
    }

    /// <summary>
    /// A vote with no signer key recorded cannot be attributed to a key. It is missing evidence, not
    /// contradicted evidence.
    /// </summary>
    [Fact]
    public void VoteWithNoRecordedKey_IsUnverified_NotFailed()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes = [new DocketVoteEvidence { ValidatorId = "validator-a", PublicKey = null }],
        };

        var check = Signers(docket, TwoValidators());

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// FR-005 again. The engine performs no cryptography (plan D1), so the check attributes votes to
    /// the roster rather than re-verifying signature bytes. It must not describe itself in terms that
    /// imply it did the latter.
    /// </summary>
    [Fact]
    public void CheckedAgainst_DescribesAttributionToTheRoster_NotCryptographicReverification()
    {
        var docket = TestEvidence.HealthyDocket() with
        {
            Votes = [TestEvidence.Vote("validator-a", "key-a")],
        };

        var check = Signers(docket, TwoValidators());

        check.CheckedAgainst.Should().Contain("roster");
        check.CheckedAgainst.Should().NotContain("re-verif",
            "the engine cannot perform signature verification and must not imply that it did (FR-005)");
    }
}
