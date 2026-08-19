// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Validator.Core.Validators;

using Xunit;

namespace Sorcha.Validator.Core.Tests.Validators;

/// <summary>
/// Feature 193 / #1464 — an organisation seated by governance must have proved it holds the key
/// being recorded for it.
/// </summary>
/// <remarks>
/// <para>
/// Real signatures throughout, produced by the platform's own <c>CryptoModule</c>. A fake verifier
/// would let this file pass while the production path rejected every acceptance — and the whole
/// point of the feature is that a carried key is worthless unless the signature over it is real.
/// </para>
/// </remarks>
public class SeatAcceptanceVerifierTests
{
    private const string Register = "0123456789abcdef0123456789abcdef";
    private const string Snapshot = "7bb8165b9f3b3d7a119147f0ca08618ce34d283234928cc7afd019fdefbe339e";

    private readonly ICryptoModule _crypto = new CryptoModule();
    private readonly IWalletUtilities _wallets = new WalletUtilities();
    private SeatAcceptanceVerifier Verifier() => new(_crypto, _wallets);

    /// <summary>
    /// An organisation: its primary keypair, its slot-100 governance key, and the DID its address
    /// yields. Derived rather than hand-written — a literal DID would not match any real key, so
    /// the attribution check would pass or fail for the wrong reason.
    /// </summary>
    private sealed record Org(byte[] PrimaryPublic, byte[] PrimaryPrivate, string GovernanceKeyB64, string Did);

    private async Task<Org> NewOrgAsync()
    {
        var primary = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!;
        var governance = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!;
        var address = _wallets.PublicKeyToWallet(primary.PublicKey.Key!, (byte)WalletNetworks.ED25519)!;

        return new Org(
            primary.PublicKey.Key!,
            primary.PrivateKey.Key!,
            Convert.ToBase64String(governance.PublicKey.Key!),
            $"did:sorcha:w:{address}");
    }

    private GovernanceOperation Add(
        string subject,
        GovernanceSeatAcceptance? acceptanceOverride = null,
        RegisterRole role = RegisterRole.Admin,
        string? snapshot = Snapshot,
        string? governanceKeyB64 = null,
        string? signingKeyB64 = null,
        string? signatureBase64 = null,
        bool includeAcceptance = true)
    {
        var op = new GovernanceOperation
        {
            OperationType = GovernanceOperationType.Add,
            TargetDid = subject,
            TargetRole = role,
            RosterSnapshotId = snapshot,
        };

        if (includeAcceptance)
        {
            op.TargetAcceptance = acceptanceOverride ?? new GovernanceSeatAcceptance
            {
                GovernanceKey = governanceKeyB64 ?? string.Empty,
                SigningPublicKey = signingKeyB64 ?? string.Empty,
                Signature = signatureBase64 ?? string.Empty,
                Algorithm = SignatureAlgorithm.ED25519,
            };
        }

        return op;
    }

    /// <summary>
    /// A genuine acceptance: <paramref name="signer"/> signs with its PRIMARY key, nominating
    /// <paramref name="nominatedGovernanceKey"/> (its own unless a test is forging).
    /// </summary>
    private async Task<GovernanceSeatAcceptance> SignAcceptanceAsync(
        Org signer,
        string subjectDid,
        RegisterRole role = RegisterRole.Admin,
        string snapshot = Snapshot,
        string? nominatedGovernanceKey = null,
        string? registerId = null)
    {
        var governanceKey = nominatedGovernanceKey ?? signer.GovernanceKeyB64;
        var digest = GovernanceSeatAcceptanceStatement.ComputeDigest(
            registerId ?? Register, subjectDid, role, governanceKey, snapshot);
        var signed = await _crypto.SignAsync(digest, (byte)WalletNetworks.ED25519, signer.PrimaryPrivate);

        return new GovernanceSeatAcceptance
        {
            GovernanceKey = governanceKey,
            SigningPublicKey = Convert.ToBase64String(signer.PrimaryPublic),
            Signature = Convert.ToBase64String(signed.Value!),
            Algorithm = SignatureAlgorithm.ED25519,
        };
    }

    // ── the happy path ───────────────────────────────────────────────────────

    [Fact]
    public async Task AGenuineAcceptanceIsAccepted()
    {
        var org = await NewOrgAsync();
        var op = Add(org.Did, await SignAcceptanceAsync(org, org.Did));

        var result = await Verifier().VerifyAsync(Register, op);

        result.Accepted.Should().BeTrue(result.Detail);
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.None);
    }

    [Fact]
    public async Task TheGovernanceKeyNominatedIsNOTTheKeyThatSigned()
    {
        // The premise of the whole design: the roster records the slot-100 governance key, but the
        // acceptance is signed by the PRIMARY key, because only the primary key is attributable to
        // the DID. If these were ever the same value the design would be unnecessary.
        var org = await NewOrgAsync();
        var acceptance = await SignAcceptanceAsync(org, org.Did);

        acceptance.GovernanceKey.Should().NotBe(acceptance.SigningPublicKey);
    }

    // ── the escalation this exists to stop ───────────────────────────────────

    [Fact]
    public async Task AProposerCannotSeatAnotherOrganisationCarryingItsOwnGovernanceKey()
    {
        // THE attack. Attacker signs a perfectly valid acceptance — with its own primary key, and
        // nominating its own governance key — but names the VICTIM as the subject. If this were
        // accepted, the victim's seat would carry the attacker's governance key and the attacker
        // could produce the victim's approvals: two votes from one organisation.
        //
        // It is caught because the signing key must derive to the SUBJECT's address, and the
        // attacker's key derives to the attacker's.
        var attacker = await NewOrgAsync();
        var victim = await NewOrgAsync();

        var forged = await SignAcceptanceAsync(attacker, victim.Did);
        var op = Add(victim.Did, forged);

        var result = await Verifier().VerifyAsync(Register, op);

        result.Accepted.Should().BeFalse("a seat must be nominated by the organisation being seated");
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SubjectMismatch);
    }

    [Fact]
    public async Task AnAcceptanceSignedByAKeyThatIsNotTheSubjectsIsRefused()
    {
        // Same shape, stated as the general rule rather than the attack: the signing key is checked
        // against the subject, not merely against itself.
        var subject = await NewOrgAsync();
        var somebodyElse = await NewOrgAsync();

        // Nominates the SUBJECT's real governance key — still refused, because the wrong party signed.
        var acceptance = await SignAcceptanceAsync(
            somebodyElse, subject.Did, nominatedGovernanceKey: subject.GovernanceKeyB64);

        var result = await Verifier().VerifyAsync(Register, Add(subject.Did, acceptance));

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SubjectMismatch);
    }

    [Fact]
    public async Task ATamperedGovernanceKeyIsRefused()
    {
        // The subject signed, but the proposer swapped the nominated governance key afterwards. The
        // statement binds that key, so the signature no longer verifies.
        var subject = await NewOrgAsync();
        var attacker = await NewOrgAsync();

        var acceptance = await SignAcceptanceAsync(subject, subject.Did);
        acceptance.GovernanceKey = attacker.GovernanceKeyB64;

        var result = await Verifier().VerifyAsync(Register, Add(subject.Did, acceptance));

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SignatureInvalid);
    }

    // ── the bindings ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAcceptanceForAnAdminSeatDoesNotAuthoriseAnOwnerSeat()
    {
        // Gate B, enforced: consent is to a SPECIFIC seat.
        var org = await NewOrgAsync();
        var asAdmin = await SignAcceptanceAsync(org, org.Did, role: RegisterRole.Admin);

        var result = await Verifier().VerifyAsync(Register, Add(org.Did, asAdmin, role: RegisterRole.Owner));

        result.Accepted.Should().BeFalse("accepting Admin must not seat the organisation as Owner");
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SignatureInvalid);
    }

    [Fact]
    public async Task AnAcceptanceCannotBeReplayedAfterTheRosterMoves()
    {
        // Without this an acceptance is valid forever, so an organisation deliberately REMOVED
        // could be re-seated by replaying its original signature.
        var org = await NewOrgAsync();
        var old = await SignAcceptanceAsync(org, org.Did, snapshot: Snapshot);

        var later = Add(org.Did, old, snapshot: "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff");
        var result = await Verifier().VerifyAsync(Register, later);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SignatureInvalid);
    }

    [Fact]
    public async Task AnAcceptanceForAnotherRegisterIsRefused()
    {
        var org = await NewOrgAsync();
        var op = Add(org.Did, await SignAcceptanceAsync(org, org.Did));

        var result = await Verifier().VerifyAsync("ffffffffffffffffffffffffffffffff", op);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SignatureInvalid);
    }

    // ── fail closed ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAddWithNoAcceptanceIsRefused()
    {
        var org = await NewOrgAsync();

        var result = await Verifier().VerifyAsync(Register, Add(org.Did, includeAcceptance: false));

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.Missing);
    }

    [Fact]
    public async Task AnAddWithNoRosterSnapshotIsRefused()
    {
        // Nothing to bind to, so it would be valid forever. Fail closed rather than verify against
        // an empty binding.
        var org = await NewOrgAsync();
        var op = Add(org.Did, await SignAcceptanceAsync(org, org.Did), snapshot: null);

        var result = await Verifier().VerifyAsync(Register, op);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.NoRosterSnapshot);
    }

    [Theory]
    [InlineData("", "c2ln", "c2ln")]
    [InlineData("a2V5", "", "c2ln")]
    [InlineData("a2V5", "c2ln", "")]
    public async Task AnIncompleteAcceptanceIsRefused(string governanceKey, string signingKey, string signature)
    {
        var org = await NewOrgAsync();
        var op = Add(org.Did,
            governanceKeyB64: governanceKey, signingKeyB64: signingKey, signatureBase64: signature);

        var result = await Verifier().VerifyAsync(Register, op);

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.Incomplete);
    }

    [Fact]
    public async Task ASubjectThatIsNotAWalletDidIsRefused()
    {
        // An unrecognised DID method cannot be attributed, so it fails closed. Accepting it would
        // make every other check here decorative.
        var org = await NewOrgAsync();
        var acceptance = await SignAcceptanceAsync(org, "did:sorcha:org:something");

        var result = await Verifier().VerifyAsync(Register, Add("did:sorcha:org:something", acceptance));

        result.Accepted.Should().BeFalse();
        result.Reason.Should().Be(SeatAcceptanceRefusalReason.SubjectMismatch);
    }

    // ── operations that seat nobody ──────────────────────────────────────────

    [Theory]
    [InlineData(GovernanceOperationType.Remove)]
    [InlineData(GovernanceOperationType.Transfer)]
    public async Task OperationsThatSeatNobodyAreUnaffected(GovernanceOperationType type)
    {
        // Transfer's safety comes from the target already being a keyed Admin, enforced by
        // ApplyOperation's promotion guard — not from an acceptance it never carries.
        var op = new GovernanceOperation
        {
            OperationType = type,
            TargetDid = "did:sorcha:w:ws11qanything",
            TargetRole = RegisterRole.Owner,
            RosterSnapshotId = Snapshot,
        };

        var result = await Verifier().VerifyAsync(Register, op);

        result.Accepted.Should().BeTrue();
    }
}
