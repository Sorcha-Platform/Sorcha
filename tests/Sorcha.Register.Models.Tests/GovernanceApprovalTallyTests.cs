// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Sorcha.Register.Models;
using Xunit;

namespace Sorcha.Register.Models.Tests;

/// <summary>
/// Which approvals may count towards quorum, and which are excluded before a signature is even
/// checked (T044).
/// </summary>
public sealed class GovernanceApprovalTallyTests
{
    private const string RegisterId = "cbb1fa4c1bc942b7a1f86eabcfb96ea6";
    private const string OwnerDid = "did:sorcha:w:ws11qowner";
    private const string AdminDid = "did:sorcha:w:ws11qadmin";
    private const string AuditorDid = "did:sorcha:w:ws11qauditor";
    private const string StrangerDid = "did:sorcha:w:ws11qstranger";

    private static string Key(byte seed) => Convert.ToBase64String(Enumerable.Repeat(seed, 32).ToArray());

    private static RegisterControlRecord Roster() => new()
    {
        RegisterId = RegisterId,
        Name = "Test",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Attestations =
        [
            Attestation(OwnerDid, RegisterRole.Owner, Key(1)),
            Attestation(AdminDid, RegisterRole.Admin, Key(2)),
            Attestation(AuditorDid, RegisterRole.Auditor, Key(3)),
        ]
    };

    private static RegisterAttestation Attestation(string did, RegisterRole role, string publicKey) => new()
    {
        Subject = did,
        Role = role,
        PublicKey = publicKey,
        Signature = Convert.ToBase64String(new byte[64]),
        Algorithm = SignatureAlgorithm.ED25519,
        GrantedAt = DateTimeOffset.UnixEpoch,
    };

    private static GovernanceOperation Operation() => new()
    {
        OperationType = GovernanceOperationType.Add,
        ProposerDid = OwnerDid,
        TargetDid = "did:sorcha:w:ws11qnewadmin",
        TargetRole = RegisterRole.Admin,
        Status = ProposalStatus.Pending,
        ProposedAt = DateTimeOffset.UnixEpoch,
        ExpiresAt = DateTimeOffset.UnixEpoch.AddDays(7),
        RosterSnapshotId = "genesis-tx",
        QuorumFormulaAtRaise = QuorumFormula.Unanimous,
    };

    private static GovernanceApprovalActionPayload Approval(
        string approverDid, string publicKey, bool isApproval = true) => new()
    {
        ProposalId = "proposal-tx",
        ApproverDid = approverDid,
        IsApproval = isApproval,
        Signature = "c2ln",
        PublicKey = publicKey,
        AuthMethod = ApprovalAuthMethod.HardwareBacked,
    };

    private static ApprovalTallyPlan Prepare(params GovernanceApprovalActionPayload[] approvals) =>
        GovernanceApprovalTally.Prepare(RegisterId, Operation(), Roster(), approvals);

    [Fact]
    public void ARosterMembersApproval_BecomesACheck()
    {
        var plan = Prepare(Approval(AdminDid, Key(2)));

        plan.Checks.Should().ContainSingle();
        plan.Checks[0].ApproverDid.Should().Be(AdminDid);
        plan.Excluded.Should().BeEmpty();
    }

    [Fact]
    public void AnApprovalFromSomebodyNotOnTheRoster_IsExcluded()
    {
        var plan = Prepare(Approval(StrangerDid, Key(9)));

        plan.Checks.Should().BeEmpty();
        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.NotOnRoster);
    }

    [Fact]
    public void AnApprovalFromANonVotingRole_IsExcluded()
    {
        var plan = Prepare(Approval(AuditorDid, Key(3)));

        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.NotAVotingRole,
                "an Auditor can read the register, not govern it");
    }

    /// <summary>
    /// The check that stops a signature certifying itself.
    /// </summary>
    /// <remarks>
    /// An approval carries the public key it was signed with. Verifying against that key would make
    /// any signature valid — sign with a key you control, then supply it. The roster is the authority
    /// on which key an organisation governs with.
    /// </remarks>
    [Fact]
    public void AnApprovalOfferingAKeyTheRosterDoesNotRecord_IsExcluded()
    {
        var plan = Prepare(Approval(AdminDid, Key(99)));

        plan.Checks.Should().BeEmpty("verifying against the offered key would make the signature self-certifying");
        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.KeyNotTheRosterKey);
    }

    /// <summary>
    /// The check must carry the roster's record of the key, not the payload's copy of it.
    /// </summary>
    /// <remarks>
    /// Asserting this needs an input where the two genuinely differ, which the
    /// <see cref="ApprovalTallyRefusal.KeyNotTheRosterKey"/> guard normally prevents — it compares
    /// decoded bytes, so it admits a differently-<i>encoded</i> spelling of the same key. Base64url
    /// (unpadded, <c>-_</c>) is that case. Without it the test passed against a payload whose key was
    /// already byte-for-byte identical, so it could not tell the two sources apart at all.
    /// </remarks>
    [Fact]
    public void AVerifiedApproval_CarriesTheRostersRecordOfTheKey_NotThePayloadsSpelling()
    {
        var rosterSpelling = Key(2);
        var payloadSpelling = rosterSpelling.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        payloadSpelling.Should().NotBe(rosterSpelling, "the test is only meaningful if they differ");

        var plan = Prepare(Approval(AdminDid, payloadSpelling));

        plan.Checks.Should().ContainSingle("the same key in another encoding is still the same key");
        plan.Checks[0].PublicKeyBase64.Should().Be(rosterSpelling);
        plan.Checks[0].Algorithm.Should().Be(SignatureAlgorithm.ED25519,
            "the roster records the algorithm; an approval payload does not carry one");
    }

    [Fact]
    public void ASecondApprovalFromTheSameOrganisation_IsExcludedAsADuplicate()
    {
        var plan = Prepare(Approval(AdminDid, Key(2)), Approval(AdminDid, Key(2)));

        plan.Checks.Should().ContainSingle("one organisation is one authority, however many times it votes");
        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.DuplicateApprover);
    }

    [Fact]
    public void ARejection_IsStillACheck_ButCarriesItsVote()
    {
        // A rejection must be verified too: it is recorded on the ledger and an unverified one could
        // otherwise be planted to make a proposal look contested.
        var plan = Prepare(Approval(AdminDid, Key(2), isApproval: false));

        plan.Checks.Should().ContainSingle().Which.IsApproval.Should().BeFalse();
    }

    [Fact]
    public void TheDigest_IsBuiltFromTheStoredOperation_SoItBindsWhatWasProposed()
    {
        var asStored = GovernanceApprovalTally
            .Prepare(RegisterId, Operation(), Roster(), [Approval(AdminDid, Key(2))]).Checks[0].Digest;

        var tampered = Operation();
        tampered.TargetRole = RegisterRole.Owner;   // "add as Admin" becomes "add as Owner"

        var asTampered = GovernanceApprovalTally
            .Prepare(RegisterId, tampered, Roster(), [Approval(AdminDid, Key(2))]).Checks[0].Digest;

        asTampered.Should().NotEqual(asStored,
            "approving 'add B as Admin' must not authorise 'add B as Owner'");
    }

    [Fact]
    public void ADigestIsPerRegister_SoAnApprovalCannotBeLiftedOntoAnother()
    {
        var here = Prepare(Approval(AdminDid, Key(2))).Checks[0].Digest;
        var elsewhere = GovernanceApprovalTally
            .Prepare("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", Operation(), Roster(), [Approval(AdminDid, Key(2))])
            .Checks[0].Digest;

        elsewhere.Should().NotEqual(here);
    }

    [Fact]
    public void ToVotes_KeepsOnlyWhatTheCallerVerified()
    {
        var plan = Prepare(Approval(OwnerDid, Key(1)), Approval(AdminDid, Key(2)));

        var votes = GovernanceApprovalTally.ToVotes(
            plan, new HashSet<string>(StringComparer.Ordinal) { AdminDid });

        votes.Should().ContainSingle().Which.ApproverDid.Should().Be(AdminDid,
            "a signature that did not verify is not a vote");
    }

    [Fact]
    public void ToVotes_VerifyingNothing_YieldsNoVotes()
    {
        var plan = Prepare(Approval(OwnerDid, Key(1)), Approval(AdminDid, Key(2)));

        GovernanceApprovalTally.ToVotes(plan, new HashSet<string>(StringComparer.Ordinal))
            .Should().BeEmpty("quorum must never be reachable without verification");
    }

    [Fact]
    public void AnApprovalNamingNobody_IsExcluded()
    {
        var plan = Prepare(Approval(string.Empty, Key(2)));

        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.NoApprover);
    }

    /// <summary>
    /// T081 / R-016: how the approving key was held travels with the vote, so a register that sets
    /// its own minimum standard has the fact to gate on.
    /// </summary>
    /// <remarks>
    /// The value is the same token the sealed approval payload carries, deliberately: an operator
    /// comparing the vote against the transaction it came from should not have to translate between
    /// two spellings of one fact.
    /// </remarks>
    [Theory]
    [InlineData(ApprovalAuthMethod.HardwareBacked, "hardware-backed")]
    [InlineData(ApprovalAuthMethod.Software, "software")]
    [InlineData(ApprovalAuthMethod.Service, "service")]
    [InlineData(ApprovalAuthMethod.Unknown, "unknown")]
    public void ToVotes_CarriesHowTheKeyWasHeld(ApprovalAuthMethod held, string expected)
    {
        var approval = Approval(AdminDid, Key(2));
        approval.AuthMethod = held;

        var plan = Prepare(approval);

        var votes = GovernanceApprovalTally.ToVotes(
            plan, new HashSet<string>(StringComparer.Ordinal) { AdminDid });

        votes.Should().ContainSingle().Which.AuthMethod.Should().Be(expected);
    }

    /// <summary>
    /// The value must match what the payload serialises, or the two records of one fact drift and a
    /// policy written against either spelling silently stops matching.
    /// </summary>
    [Fact]
    public void TheRecordedAuthMethod_IsTheSameTokenTheSealedPayloadCarries()
    {
        var approval = Approval(AdminDid, Key(2));
        approval.AuthMethod = ApprovalAuthMethod.HardwareBacked;

        var vote = GovernanceApprovalTally.ToVotes(
            Prepare(approval), new HashSet<string>(StringComparer.Ordinal) { AdminDid }).Single();

        // Derived from the payload's own serialisation rather than written out here, so a change to
        // the payload's converter cannot leave this assertion pinning a stale spelling.
        var sealedJson = System.Text.Json.JsonSerializer.Serialize(
            approval, GovernanceApprovalActionPayload.CanonicalJsonOptions);

        sealedJson.Should().Contain($"\"authMethod\":\"{vote.AuthMethod}\"");
    }

    // ---- R-009 / T055: quorum is a pure function of sealed content -----------------------------

    /// <summary>
    /// Every ordering of the same three distinct approvals, so the claim is checked exhaustively
    /// rather than on one arbitrary shuffle.
    /// </summary>
    private static IEnumerable<T[]> Permutations<T>(T[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Length; i++)
        {
            var rest = items.Take(i).Concat(items.Skip(i + 1)).ToArray();
            foreach (var tail in Permutations(rest))
            {
                yield return new[] { items[i] }.Concat(tail).ToArray();
            }
        }
    }

    private const string SecondAdminDid = "did:sorcha:w:ws11qadmin2";

    /// <summary>
    /// Three <b>voting</b> members. The shared <see cref="Roster"/> has an Auditor, which is a
    /// non-voting role and is excluded before any signature is considered — so building the
    /// order-independence check on it would have compared two counted approvals while claiming to
    /// compare three.
    /// </summary>
    private static RegisterControlRecord VotingRoster() => new()
    {
        RegisterId = RegisterId,
        Name = "Test",
        CreatedAt = DateTimeOffset.UnixEpoch,
        Attestations =
        [
            Attestation(OwnerDid, RegisterRole.Owner, Key(1)),
            Attestation(AdminDid, RegisterRole.Admin, Key(2)),
            Attestation(SecondAdminDid, RegisterRole.Admin, Key(4)),
        ]
    };

    [Fact]
    public void TheTally_IsIdenticalUnderEveryInputOrder_WhenTheApproversAreDistinct()
    {
        // R-009: two nodes reaching different quorum answers for the same sealed approvals is the
        // failure this feature cannot have, and it would not announce itself — the enactment simply
        // seals on one node and is refused on another. Replication does not guarantee that two nodes
        // enumerate a proposal's approvals in the same order, so the tally must not depend on it.
        var approvals = new[]
        {
            Approval(OwnerDid, Key(1)),
            Approval(AdminDid, Key(2)),
            Approval(SecondAdminDid, Key(4)),
        };

        ApprovalTallyPlan Tally(GovernanceApprovalActionPayload[] a) =>
            GovernanceApprovalTally.Prepare(RegisterId, Operation(), VotingRoster(), a);

        var orderings = Permutations(approvals).ToList();
        orderings.Should().HaveCount(6, "all six orderings must actually be exercised");

        var expected = Tally(approvals).Checks
            .Select(c => (c.ApproverDid, c.PublicKeyBase64, c.IsApproval, Digest: Convert.ToBase64String(c.Digest)))
            .OrderBy(c => c.ApproverDid, StringComparer.Ordinal)
            .ToList();

        expected.Should().HaveCount(3, "the fixture must produce a non-empty tally, or this passes vacuously");

        foreach (var ordering in orderings)
        {
            var plan = Tally(ordering);

            plan.Checks
                .Select(c => (c.ApproverDid, c.PublicKeyBase64, c.IsApproval, Digest: Convert.ToBase64String(c.Digest)))
                .OrderBy(c => c.ApproverDid, StringComparer.Ordinal)
                .Should().Equal(expected,
                    "the counted approvals must not depend on the order they are read in");

            plan.Excluded.Should().BeEmpty();
        }
    }

    [Fact]
    public void WhenAnOrganisationVotesTwice_TheFirstStands_AndTheSecondIsExcluded()
    {
        // The one place order is deliberately load-bearing, pinned so it cannot drift to last-wins.
        // The order that decides is SEAL order, which is the same on every node because it is fixed
        // by the docket — so this is determinism, not an exception to it. An organisation that
        // approved and then rejected must not be able to change the outcome by resubmitting.
        var plan = Prepare(
            Approval(AdminDid, Key(2), isApproval: true),
            Approval(AdminDid, Key(2), isApproval: false));

        plan.Checks.Should().ContainSingle()
            .Which.IsApproval.Should().BeTrue("the first vote from an organisation stands");

        plan.Excluded.Should().ContainSingle()
            .Which.Refusal.Should().Be(ApprovalTallyRefusal.DuplicateApprover);
    }

    [Fact]
    public void TheTallyReadsNothingButSealedContent()
    {
        // The structural half of R-009. Prepare takes only the register id, the operation stored on
        // the proposal, the roster it was raised against, and the sealed approval payloads — no
        // clock, no node identity, no configuration, no ambient state. Anything else in the
        // signature would be a way for two nodes to differ, so the shape is the guarantee and this
        // pins it against a well-meant extra parameter.
        var parameters = typeof(GovernanceApprovalTally)
            .GetMethod(nameof(GovernanceApprovalTally.Prepare))!
            .GetParameters()
            .Select(p => p.ParameterType);

        parameters.Should().Equal(
            [
                typeof(string),
                typeof(GovernanceOperation),
                typeof(RegisterControlRecord),
                typeof(IReadOnlyList<GovernanceApprovalActionPayload>),
            ],
            "quorum must be a function of sealed content alone (R-009)");
    }
}
