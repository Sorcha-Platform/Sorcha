// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;

using Xunit;

namespace Sorcha.Blueprint.Engine.Tests.Credentials;

/// <summary>
/// Feature 192 — a refusal must say WHICH status applied. Suspension is reversible; revocation is
/// not. Reporting one as the other is not a security hole (both refuse) but it is an untruth, and
/// it is the untruth that decides whether a holder waits a day or re-applies from scratch.
/// </summary>
/// <remarks>
/// <para>
/// The distinction used to die at the first step: <c>StatusListBit</c> was a tri-state that could
/// not express three statuses, so both the W3C checker (which already knew the purpose) and the
/// IETF checker (which already read a two-bit value) had to flatten to "set" before the evaluator
/// ever saw it. <c>CredentialStatusValue</c> replaced it.
/// </para>
/// <para>
/// Do not weaken these to "is refused" — that is what the old tests asserted, and it is exactly why
/// the conflation survived three PRs at this seam. Assert the REASON.
/// </para>
/// </remarks>
public class SuspensionIsReportedDistinctlyTests
{
    private const string RevocationList = "https://n1.test/api/v1/credentials/status-lists/f192-revocation-1";
    private const string SuspensionList = "https://n1.test/api/v1/credentials/status-lists/f192-suspension-1";

    [Fact]
    public async Task ASuspendedCredentialIsNotReportedAsRevoked()
    {
        var error = await VerifyAndGetFailure(revocationSet: false, suspensionSet: true);

        error.FailureReason.Should().Be(CredentialFailureReason.Suspended,
            "suspension is reversible and revocation is not — telling a holder their credential was " +
            "revoked when it was suspended is materially misleading");
    }

    [Fact]
    public async Task ARevokedCredentialIsStillReportedAsRevoked()
    {
        var error = await VerifyAndGetFailure(revocationSet: true, suspensionSet: false);

        error.FailureReason.Should().Be(CredentialFailureReason.Revoked);
    }

    [Fact]
    public async Task ACredentialThatIsBothSuspendedAndRevokedReportsRevoked()
    {
        // Revocation is terminal in both specifications. Reporting the reversible status would
        // imply the credential could come back, which is the more damaging of the two errors — so
        // the definite, terminal status wins regardless of which entry the credential lists first.
        var error = await VerifyAndGetFailure(revocationSet: true, suspensionSet: true);

        error.FailureReason.Should().Be(CredentialFailureReason.Revoked);
    }

    [Fact]
    public async Task ASuspendedCredentialIsStillRefused()
    {
        // #1495 exists because fixing the REASON removed the ENFORCEMENT. Renaming a refusal must
        // never narrow it, so this assertion stays alongside the reason assertions permanently.
        var result = await Verify(revocationSet: false, suspensionSet: true);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ADefiniteSuspensionOutranksAnUnresolvedRevocationList()
    {
        // The revocation list is unreachable, but the suspension list answered: the credential is
        // definitely suspended. Reporting "could not check revocation" would be true but useless —
        // we DO know it is unusable, and we know why. A status we resolved always beats one we did
        // not, in both directions: this credential is refused as Suspended, not as unavailable.
        var result = await Verify(
            revocation: CredentialStatusValue.Unresolved,
            suspension: CredentialStatusValue.Suspended);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Subject
            .FailureReason.Should().Be(CredentialFailureReason.Suspended);
    }

    [Fact]
    public async Task AnUnresolvedListWithNothingElseSetIsStillFailClosed()
    {
        // The counterpart: with no definite status anywhere, "could not tell" is the honest answer
        // and the fail-closed policy refuses on it. Feature 192 must not have quietly swallowed
        // this path while collecting statuses instead of returning on the first one.
        var result = await Verify(
            revocation: CredentialStatusValue.Unresolved,
            suspension: CredentialStatusValue.Valid);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle().Subject
            .FailureReason.Should().Be(CredentialFailureReason.RevocationCheckUnavailable);
    }

    private static async Task<CredentialValidationError> VerifyAndGetFailure(
        bool revocationSet, bool suspensionSet)
    {
        var result = await Verify(revocationSet, suspensionSet);

        result.IsValid.Should().BeFalse("a credential with a status set must be refused");
        return result.Errors.Should().ContainSingle().Subject;
    }

    private static Task<CredentialValidationResult> Verify(bool revocationSet, bool suspensionSet) =>
        Verify(
            revocationSet ? CredentialStatusValue.Invalid : CredentialStatusValue.Valid,
            suspensionSet ? CredentialStatusValue.Suspended : CredentialStatusValue.Valid);

    private static async Task<CredentialValidationResult> Verify(
        CredentialStatusValue revocation, CredentialStatusValue suspension)
    {
        var minted = EngineSdJwtTestFactory.MintEs256(
            "LicenseCredential",
            "did:sorcha:issuer:gov",
            credentialStatusClaim: new object[]
            {
                StatusEntry(RevocationList, "revocation"),
                StatusEntry(SuspensionList, "suspension")
            });

        var verifier = EngineSdJwtTestFactory.BuildVerifier(
            directory: null,
            statusChecker: new PerPurposeStatusChecker(revocation, suspension),
            minted);

        var requirements = new[] { new CredentialRequirement { Type = "LicenseCredential" } };
        var presentation = new CredentialPresentation
        {
            CredentialId = "cred-1",
            RawPresentation = minted.Raw
        };

        return await verifier.VerifyAsync(requirements, [presentation]);
    }

    private static Dictionary<string, object> StatusEntry(string list, string purpose) => new()
    {
        ["id"] = $"{list}#7",
        ["type"] = "BitstringStatusListEntry",
        ["statusPurpose"] = purpose,
        ["statusListIndex"] = "7",
        ["statusListCredential"] = list
    };

    /// <summary>
    /// Answers per purpose, so revocation and suspension can differ. A fake that gave both purposes
    /// the same answer would let every test here pass without the production code distinguishing
    /// anything — the fake would be doing the work under test.
    /// </summary>
    private sealed class PerPurposeStatusChecker(
        CredentialStatusValue revocation, CredentialStatusValue suspension) : IStatusListChecker
    {
        public Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken ct = default) =>
            Task.FromResult(statusRef.Purpose == "suspension" ? suspension : revocation);
    }
}
