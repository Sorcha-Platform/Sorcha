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

    /// <summary>
    /// A proposer absent from the roster is <b>Unverified, not Failed</b>, and this is the most
    /// important behaviour in this file.
    /// </summary>
    /// <remarks>
    /// A docket names its proposer by the validator's <i>configured</i> identifier
    /// (<c>DocketBuilder</c> sets <c>ProposerValidatorId = _validatorConfig.ValidatorId</c>, e.g.
    /// <c>local-validator</c>), while a roster names validators by <i>wallet address</i>. The two are
    /// different identifier spaces, and a docket header carries no public key to compare instead — so
    /// a non-match genuinely cannot be resolved from the register. Reporting Failed would accuse
    /// every healthy register on every single-validator deployment of an unauthorised proposer. See
    /// <see cref="RealNodeShape_ConfigIdProposerAgainstWalletAddressRoster_IsUnverified"/>.
    /// </remarks>
    [Fact]
    public void ProposerAbsentFromTheRosterAsOf_IsUnverified_NotFailed()
    {
        var check = Proposer(TestEvidence.HealthyDocket(proposer: "stranger"), Roster());

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Status.Should().NotBe(VerificationStatus.Failed,
            "a name that does not appear in the roster cannot be distinguished from a name recorded " +
            "in a different identifier space, and a false accusation is the worst output this " +
            "feature can produce");
        check.Detail.Should().Contain("stranger");
        check.Reason.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The exact shape observed on n1: every docket names <c>local-validator</c>, every roster entry
    /// is a <c>ws1…</c> wallet address. This must not read as tampering.
    /// </summary>
    [Fact]
    public void RealNodeShape_ConfigIdProposerAgainstWalletAddressRoster_IsUnverified()
    {
        var roster = TestEvidence.Roster(
            1,
            TestEvidence.Validator(
                "ws11qq5dj49wcpjsjs8w9fs3pwahrxwxpa89fchnffq7h3r4zqxgutwt5sf56qv",
                "DdM/2pb1oBdY//Jp9Gem"));

        var check = Proposer(TestEvidence.HealthyDocket(proposer: "local-validator"), roster);

        check.Status.Should().Be(VerificationStatus.Unverified);
        check.Reason.Should().Contain("local-validator");
        check.Detail.Should().Contain("ws11qq5dj49",
            "the reader must see both identifiers to recognise this as a naming mismatch at a glance");
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
