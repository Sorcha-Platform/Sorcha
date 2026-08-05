// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// The Proposer check: was the proposing validator a member of the set applying when this docket
/// sealed?
/// </summary>
/// <remarks>
/// Same roster-as-of discipline as the Signers check — the membership consulted is the one that
/// applied at this docket, never today's. A proposer removed later must still read as entitled for
/// the dockets it proposed while it held authority.
/// </remarks>
public class ProposerCheckTests
{
    private static ProvenanceCheck Proposer(DocketEvidence docket, RosterAsOf? roster) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(TestEvidence.RegisterId, docket, roster, TestEvidence.TraceableAnchor())
            .Checks.Single(c => c.Layer == ProvenanceLayer.Proposer);

    private static RosterAsOf Roster() => TestEvidence.Roster(
        2,
        TestEvidence.Validator("validator-a", "key-a"),
        TestEvidence.Validator("validator-b", "key-b"));

    [Fact]
    public void ProposerPresentInTheRosterAsOf_IsVerified()
    {
        var check = Proposer(TestEvidence.HealthyDocket(proposer: "validator-a"), Roster());

        check.Status.Should().Be(VerificationStatus.Verified);
        check.Reason.Should().BeNull();
        check.CheckedAgainst.Should().Contain("2", "the roster version applied must be stated");
    }

    [Fact]
    public void ProposerAbsentFromTheRosterAsOf_IsFailed()
    {
        var check = Proposer(TestEvidence.HealthyDocket(proposer: "stranger"), Roster());

        check.Status.Should().Be(VerificationStatus.Failed);
        check.Detail.Should().Contain("stranger");
    }

    [Fact]
    public void RosterVersionUnresolvable_IsUnverified_NotFailed()
    {
        var check = Proposer(TestEvidence.HealthyDocket(), roster: null);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A docket sealed before Feature 187 made the proposer a first-class field records no proposer
    /// at all. Nothing to check, so nothing is claimed.
    /// </summary>
    [Fact]
    public void NoProposerRecorded_IsUnverified_NotFailed()
    {
        var check = Proposer(TestEvidence.HealthyDocket() with { ProposerValidatorId = null }, Roster());

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The register stores <c>ProposerValidatorId</c> as a non-nullable string defaulting to empty,
    /// so a pre-Feature-187 docket arrives blank rather than null. Both must behave alike.
    /// </summary>
    [Fact]
    public void BlankProposer_IsTreatedTheSameAsAbsent()
    {
        var check = Proposer(TestEvidence.HealthyDocket() with { ProposerValidatorId = "  " }, Roster());

        check.Status.Should().Be(VerificationStatus.Unverified);
    }

    /// <summary>
    /// A proposer present in the version but not holding authority then (revoked or ejected at that
    /// point) was not entitled to propose.
    /// </summary>
    [Fact]
    public void ProposerPresentButNotHoldingAuthorityThen_IsFailed()
    {
        var roster = TestEvidence.Roster(
            3,
            TestEvidence.Validator("validator-a", "key-a"),
            TestEvidence.Validator("validator-b", "key-b", heldAuthority: false));

        var check = Proposer(TestEvidence.HealthyDocket(proposer: "validator-b"), roster);

        check.Status.Should().Be(VerificationStatus.Failed);
    }

    /// <summary>
    /// A proposer removed after the fact must still read as entitled for the dockets it proposed
    /// while it held authority — the roster-as-of rule applied to this layer (FR-010).
    /// </summary>
    [Fact]
    public void ProposerRemovedLater_IsStillVerified_ForItsOwnDocket()
    {
        var rosterThen = TestEvidence.Roster(
            3,
            TestEvidence.Validator("validator-a", "key-a"),
            TestEvidence.Validator("validator-b", "key-b"));

        var check = Proposer(TestEvidence.HealthyDocket(number: 10, proposer: "validator-b"), rosterThen);

        check.Status.Should().Be(VerificationStatus.Verified);
    }

    /// <summary>
    /// A roster version that resolved with no members cannot say whether anyone was entitled.
    /// </summary>
    [Fact]
    public void RosterWithNoEntries_IsUnverified_NotFailed()
    {
        var empty = new RosterAsOf
        {
            RosterVersion = 4,
            Entries = [],
            ResolvedFrom = "control transaction ctrl-v4",
        };

        var check = Proposer(TestEvidence.HealthyDocket(), empty);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed);
    }
}
