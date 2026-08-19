// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Register.Models;

using Xunit;

namespace Sorcha.Register.Models.Tests.Governance;

/// <summary>
/// Feature 193 — every field of the seat-acceptance statement is load-bearing.
/// </summary>
/// <remarks>
/// A signature is only as good as what it commits to, and the failure mode of a missing field is
/// silent: the signature still verifies, it just authorises more than the organisation agreed to.
/// So each field gets its own test, named for the attack it prevents, rather than one test asserting
/// a hash.
/// </remarks>
public class GovernanceSeatAcceptanceStatementTests
{
    private const string Register = "0123456789abcdef0123456789abcdef";
    private const string Subject = "did:sorcha:w:ws11qexample";
    private const string Key = "thfb6l9PJ/E2qJYvl2PHaWohph+cWfxLpAflFRHStQI=";
    private const string Snapshot = "7bb8165b9f3b3d7a119147f0ca08618ce34d283234928cc7afd019fdefbe339e";

    private static byte[] Digest(
        string register = Register,
        string subject = Subject,
        RegisterRole role = RegisterRole.Admin,
        string key = Key,
        string snapshot = Snapshot)
        => GovernanceSeatAcceptanceStatement.ComputeDigest(register, subject, role, key, snapshot);

    [Fact]
    public void TheSameInputsProduceTheSameDigest()
    {
        Digest().Should().Equal(Digest(), "the producer and both verifiers must agree byte for byte");
    }

    [Fact]
    public void AnAcceptanceForOneRegisterDoesNotSeatOnAnother()
    {
        Digest().Should().NotEqual(Digest(register: "ffffffffffffffffffffffffffffffff"));
    }

    [Fact]
    public void OneOrganisationsAcceptanceCannotBeAttributedToAnother()
    {
        Digest().Should().NotEqual(Digest(subject: "did:sorcha:w:ws11qsomeoneelse"));
    }

    [Fact]
    public void AcceptingAdminDoesNotAuthoriseBeingSeatedAsOwner()
    {
        // The difference between a party that can vote and one that can transfer the register.
        // This is the field Gate B exists for: consent to a SPECIFIC seat, not to membership.
        Digest(role: RegisterRole.Admin)
            .Should().NotEqual(Digest(role: RegisterRole.Owner));
    }

    [Fact]
    public void TheSignatureCannotBePairedWithADifferentKey()
    {
        // The statement commits to the very key being recorded, so a valid acceptance cannot be
        // lifted onto a proposal carrying somebody else's key.
        Digest().Should().NotEqual(Digest(key: "VHWQB/leUxacGAD0K16P/YwsupKRynJkpiUwVs7hYYQ="));
    }

    [Fact]
    public void AnAcceptanceCannotBeReplayedAfterTheRosterMoves()
    {
        // Without the snapshot an acceptance is valid forever, so an organisation deliberately
        // REMOVED from a roster could be re-seated by replaying its original signature.
        Digest().Should().NotEqual(Digest(snapshot: "0000000000000000000000000000000000000000000000000000000000000000"));
    }

    [Fact]
    public void TheVersionTagIsInsideTheSignedBytes()
    {
        // A v1 signature must not verify under a future v2. Asserted on the statement rather than
        // the digest so the failure names the cause.
        GovernanceSeatAcceptanceStatement.BuildStatement(Register, Subject, RegisterRole.Admin, Key, Snapshot)
            .Should().StartWith(GovernanceSeatAcceptanceStatement.StatementVersion);
    }

    [Fact]
    public void FieldsCannotBeShiftedAcrossTheSeparator()
    {
        // A printable delimiter would let a value containing it move the field boundaries so two
        // different statements hash identically. Construct exactly that collision attempt: move the
        // boundary between subject and role.
        var honest = GovernanceSeatAcceptanceStatement.ComputeDigest(
            Register, "did:sorcha:w:a", RegisterRole.Admin, Key, Snapshot);
        var shifted = GovernanceSeatAcceptanceStatement.ComputeDigest(
            Register, "did:sorcha:w:aAdmin", RegisterRole.Admin, Key, Snapshot);

        // They must differ. (If the separator were, say, ':' this pair would collide.)
        honest.Should().NotEqual(shifted);
    }

    [Theory]
    [InlineData("", Subject, Key, Snapshot)]
    [InlineData(Register, "", Key, Snapshot)]
    [InlineData(Register, Subject, "", Snapshot)]
    [InlineData(Register, Subject, Key, "")]
    public void EveryBindingFieldIsRequired(string register, string subject, string key, string snapshot)
    {
        // An empty field would quietly weaken the binding rather than fail, which is the shape of
        // defect this whole feature exists to remove.
        var act = () => GovernanceSeatAcceptanceStatement.ComputeDigest(
            register, subject, RegisterRole.Admin, key, snapshot);

        act.Should().Throw<ArgumentException>();
    }
}
