// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Provenance.Engine;
using Sorcha.Provenance.Engine.Evidence;
using Sorcha.Verification.Abstractions;

namespace Sorcha.Provenance.Engine.Tests.Checks;

/// <summary>
/// FR-010 / SC-004 — a signature must be judged against the validator set <b>as it stood when the
/// docket was sealed</b>, not as it stands now.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only test in the feature that fails against an implementation that looks entirely
/// correct.</b> A Signers check that verifies against the <i>current</i> roster passes every other
/// test in this suite — every one of them uses a register whose validator set never changed. It then
/// starts reporting false tampering the moment a validator is removed, which is to say the moment
/// the network does the thing this feature was built to make safe. The failure grows more likely the
/// more the platform grows, and it accuses the operator of tampering when nothing is wrong.
/// </para>
/// <para>
/// The scenario below is the one from plan D5: validator-b is removed at docket 12.
/// </para>
/// <list type="bullet">
///   <item><description>
///     Docket <b>10</b> carries a vote from validator-b. It held authority then, so this must be
///     <see cref="VerificationStatus.Verified"/> — <i>forever</i>. A signature valid when made stays
///     valid.
///   </description></item>
///   <item><description>
///     Docket <b>14</b> carries a vote from the same key. It no longer held authority, so this must
///     be <see cref="VerificationStatus.Failed"/>.
///   </description></item>
/// </list>
/// <para>
/// Both directions matter. A check that always said Verified would pass the first and fail the
/// second; one that judged against today's set would pass the second and fail the first.
/// </para>
/// </remarks>
[CoversLayer(ProvenanceLayer.Signers)]
public class RosterAsOfTests
{
    private const string StableValidator = "validator-a";
    private const string StableKey = "key-a";

    /// <summary>The validator removed at docket 12.</summary>
    private const string RemovedValidator = "validator-b";
    private const string RemovedKey = "key-b";

    /// <summary>Roster version 3 — what applied up to and including docket 11. Both validators.</summary>
    private static RosterAsOf RosterBeforeRemoval() => new()
    {
        RosterVersion = 3,
        Entries =
        [
            TestEvidence.Validator(StableValidator, StableKey),
            TestEvidence.Validator(RemovedValidator, RemovedKey),
        ],
        ResolvedFrom = "control transaction ctrl-v3 (genesis roster)",
    };

    /// <summary>
    /// Roster version 4 — established by the removal at docket 12. validator-b is gone, exactly as a
    /// resolver walking control transactions up to docket 14 would report it.
    /// </summary>
    private static RosterAsOf RosterAfterRemoval() => new()
    {
        RosterVersion = 4,
        Entries = [TestEvidence.Validator(StableValidator, StableKey)],
        ResolvedFrom = "control transaction ctrl-v4 (RemoveValidator at docket 12)",
    };

    private static ProvenanceCheck Signers(DocketEvidence docket, RosterAsOf roster) =>
        new DocketProvenanceVerifier(new StubMerkle())
            .Verify(TestEvidence.RegisterId, docket, roster, TestEvidence.TraceableAnchor())
            .Checks.Single(c => c.Layer == ProvenanceLayer.Signers);

    private static DocketEvidence DocketSignedBy(ulong number, params DocketVoteEvidence[] votes) =>
        TestEvidence.HealthyDocket(number, proposer: StableValidator, proposerKey: StableKey) with
        {
            Votes = votes,
        };

    /// <summary>
    /// A signature made by a validator that has since been removed still reports Verified for the
    /// docket it signed (SC-004, first half).
    /// </summary>
    [Fact]
    public void SignatureFromAValidatorRemovedLater_StaysVerified_ForTheDocketItSigned()
    {
        var docket10 = DocketSignedBy(
            10,
            TestEvidence.Vote(StableValidator, StableKey),
            TestEvidence.Vote(RemovedValidator, RemovedKey));

        var check = Signers(docket10, RosterBeforeRemoval());

        check.Status.Should().Be(VerificationStatus.Verified,
            "validator-b held authority at docket 10; it was removed at docket 12. Judging this " +
            "signature against today's set would report tampering where there is none.");
    }

    /// <summary>
    /// A signature purporting to be made by that validator after removal reports Failed (SC-004,
    /// second half).
    /// </summary>
    [Fact]
    public void SignatureFromTheSameKeyAfterRemoval_IsFailed()
    {
        var docket14 = DocketSignedBy(
            14,
            TestEvidence.Vote(StableValidator, StableKey),
            TestEvidence.Vote(RemovedValidator, RemovedKey));

        var check = Signers(docket14, RosterAfterRemoval());

        check.Status.Should().Be(VerificationStatus.Failed,
            "validator-b was removed at docket 12, so it could not legitimately have signed docket 14");
        check.Detail.Should().Contain(RemovedValidator,
            "the reader must be told WHICH signer was not entitled to sign");
    }

    /// <summary>
    /// The roster version that was applied must be visible to the reader, or they cannot check the
    /// engine's working — which is the point of stating what each check compared against (FR-002).
    /// </summary>
    [Fact]
    public void CheckNamesTheRosterVersionAndTheControlTransactionThatEstablishedIt()
    {
        var check = Signers(
            DocketSignedBy(10, TestEvidence.Vote(StableValidator, StableKey)),
            RosterBeforeRemoval());

        check.CheckedAgainst.Should().Contain("3", "the roster VERSION applied must be stated");
        check.CheckedAgainst.Should().Contain("ctrl-v3",
            "an auditor must be able to go and read the control transaction that set the membership");
    }

    /// <summary>
    /// The same docket, judged against the two different roster versions, must produce different
    /// verdicts. If it does not, the check is ignoring the roster it was handed — which is precisely
    /// what the naive current-roster implementation looks like from the inside.
    /// </summary>
    [Fact]
    public void TheVerdictActuallyDependsOnWhichRosterVersionIsSupplied()
    {
        var docket = DocketSignedBy(10, TestEvidence.Vote(RemovedValidator, RemovedKey));

        var before = Signers(docket, RosterBeforeRemoval());
        var after = Signers(docket, RosterAfterRemoval());

        before.Status.Should().Be(VerificationStatus.Verified);
        after.Status.Should().Be(VerificationStatus.Failed);
        before.Status.Should().NotBe(after.Status,
            "if the roster version makes no difference, the check is not consulting it");
    }

    /// <summary>
    /// A rotated key is exactly what a historical docket was signed with. Dropping rotated entries
    /// while resolving is the other way this trap gets sprung — quieter, because it only bites
    /// registers that have rotated.
    /// </summary>
    [Fact]
    public void KeyThatHeldAuthorityButHasSinceRotated_IsStillVerified_ForItsOwnDocket()
    {
        var roster = new RosterAsOf
        {
            RosterVersion = 5,
            Entries = [TestEvidence.Validator(StableValidator, "key-a-old", heldAuthority: true)],
            ResolvedFrom = "control transaction ctrl-v5 (RotateValidatorKey)",
        };

        var check = Signers(DocketSignedBy(10, TestEvidence.Vote(StableValidator, "key-a-old")), roster);

        check.Status.Should().Be(VerificationStatus.Verified);
    }

    /// <summary>
    /// An entry present in the version but marked as not holding authority then (revoked or ejected
    /// at that point) must not vouch for a signature.
    /// </summary>
    [Fact]
    public void KeyPresentButNotHoldingAuthorityAtThatDocket_IsFailed()
    {
        var roster = new RosterAsOf
        {
            RosterVersion = 6,
            Entries =
            [
                TestEvidence.Validator(StableValidator, StableKey),
                TestEvidence.Validator(RemovedValidator, RemovedKey, heldAuthority: false),
            ],
            ResolvedFrom = "control transaction ctrl-v6",
        };

        var check = Signers(DocketSignedBy(14, TestEvidence.Vote(RemovedValidator, RemovedKey)), roster);

        check.Status.Should().Be(VerificationStatus.Failed);
    }

    /// <summary>
    /// A vote naming a validator that IS in the roster but carrying a different key than the one
    /// authorised then. It claims an identity it cannot substantiate.
    /// </summary>
    [Fact]
    public void VoteClaimingARosterValidatorWithAnUnauthorisedKey_IsFailed()
    {
        var check = Signers(
            DocketSignedBy(10, TestEvidence.Vote(StableValidator, "some-other-key")),
            RosterBeforeRemoval());

        check.Status.Should().Be(VerificationStatus.Failed);
    }
}
