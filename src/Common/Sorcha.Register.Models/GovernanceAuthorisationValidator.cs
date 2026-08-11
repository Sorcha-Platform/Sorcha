// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>Why an authorisation was refused. Every refusal carries one — nothing is silently dropped (FR-011c).</summary>
public enum AuthorisationRefusalReason
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>No authorisation at all. An approval must resolve to a named individual (FR-029).</summary>
    Missing,

    /// <summary>Required fields absent or empty.</summary>
    Incomplete,

    /// <summary>Delegated form with no delegation attached, or direct form carrying one.</summary>
    KindMismatch,

    /// <summary>The delegation empowers a different organisation than the one approving.</summary>
    WrongOrganisation,

    /// <summary>The delegation names a different individual than the authorisation claims.</summary>
    IndividualMismatch,

    /// <summary>The signing key is not the one the delegation empowers.</summary>
    ApproverKeyMismatch,

    /// <summary>The operation is outside the delegation's scope.</summary>
    OutOfScope,

    /// <summary>The delegation has lapsed.</summary>
    Expired,

    /// <summary>The delegation has been revoked.</summary>
    Revoked,

    /// <summary>
    /// The delegated form is refused outright: nothing on the platform can GRANT a delegation, so
    /// no delegation presented to it can have been legitimately issued (T095 / R-023).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Verifiable is not the same as reachable, and the gap between them was the hole.</b> Every
    /// cryptographic check below this one is sound — the delegation must be signed by a key that
    /// genuinely belongs to the individual named as granting it, it empowers exactly one approver
    /// key, and scope, expiry and revocation are all enforced. What nothing checks is whether that
    /// individual had any <i>authority</i> to grant it: there is no roster check, no Owner check, not
    /// even a test of organisational affiliation. R-023 says granting is Owner-only; no code enforces
    /// it, because there is no granting path to enforce it in.
    /// </para>
    /// <para>
    /// The absence of a granting path does <b>not</b> make the delegated form unreachable — a
    /// hand-crafted submission needs no UI. Anyone able to produce the organisation's signature plus
    /// a wallet key of their own could name themselves as the empowering individual and have a
    /// machine approve in their name. That does not escalate authority (the organisation's signature
    /// is still required, so it is bounded by the R-006 custody limitation), but it degrades the
    /// accountability record from "a named individual consented" to "somebody asserted they were
    /// entitled to delegate" — which is the one thing FR-029 exists to prevent.
    /// </para>
    /// <para>
    /// So the form is refused until granting exists. The verification code below is kept rather than
    /// deleted: it is correct and tested, and a real granting path needs exactly it. Remove this
    /// refusal in the same change that adds granting — not before.
    /// </para>
    /// </remarks>
    DelegationNotAvailable,

    /// <summary>
    /// A signature was present and well-formed but did not verify against the key offered with it.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Incomplete"/> on purpose. A structurally incomplete authorisation is
    /// a caller that has not finished assembling one; a signature that fails to verify is a caller
    /// presenting something that is not what it claims to be. Folding them together made the two
    /// indistinguishable to anything reading the reason, so an endpoint could only tell them apart by
    /// matching on English text in the detail string.
    /// </remarks>
    SignatureInvalid,
}

/// <summary>A signature the caller must verify cryptographically before accepting the approval.</summary>
/// <param name="Purpose">What it covers, for diagnostics.</param>
/// <param name="Digest">The bytes that must verify.</param>
/// <param name="SignatureBase64">The signature offered.</param>
/// <param name="PublicKeyBase64">The key that must have produced it.</param>
public readonly record struct RequiredSignatureCheck(
    string Purpose,
    byte[] Digest,
    string SignatureBase64,
    string PublicKeyBase64);

/// <summary>Outcome of structural validation.</summary>
/// <param name="IsAcceptable">Whether the structure permits the approval, pending signature checks.</param>
/// <param name="Reason">Why not, when it does not.</param>
/// <param name="AccountableIndividualDid">The person the approval resolves to.</param>
/// <param name="RequiredChecks">Signatures the caller must still verify.</param>
public sealed record AuthorisationValidationResult(
    bool IsAcceptable,
    AuthorisationRefusalReason Reason,
    string? AccountableIndividualDid,
    IReadOnlyList<RequiredSignatureCheck> RequiredChecks)
{
    internal static AuthorisationValidationResult Refuse(AuthorisationRefusalReason reason)
        => new(false, reason, null, Array.Empty<RequiredSignatureCheck>());
}

/// <summary>
/// Structural validation of an approval's accountability link.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately performs <b>no cryptography</b>. <c>Sorcha.Register.Models</c> is a
/// zero-dependency leaf so every consumer can reference it — services, CLI, Blazor UI and the WASM
/// wallet alike — and pulling in a native-P/Invoke crypto module would break the browser targets. It
/// instead returns the exact digests that must verify, and the layer holding <c>ICryptoModule</c>
/// does the verifying. Same split <see cref="GovernanceApprovalStatement"/> already uses.
/// </para>
/// <para>
/// <b>Refusal is total.</b> An approval whose authorisation is invalid, out of scope, expired or
/// revoked is refused outright — never accepted with the authorisation quietly discarded (FR-032).
/// Silently downgrading accountability is worse than refusing, because it leaves a record that looks
/// complete.
/// </para>
/// </remarks>
public static class GovernanceAuthorisationValidator
{
    /// <summary>
    /// Validates the structure of an approval's authorisation.
    /// </summary>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The proposal being approved.</param>
    /// <param name="submission">The detached approval.</param>
    /// <param name="now">Evaluation time. Passed in so the result is deterministic across nodes (R-009).</param>
    /// <param name="isRevoked">
    /// Whether a delegation id has been revoked. Supplied by the caller because revocation lives on
    /// the ledger — the answer must come from sealed content so every node folds identically.
    /// </param>
    public static AuthorisationValidationResult Validate(
        string registerId,
        GovernanceOperation operation,
        GovernanceApprovalSubmission submission,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        bool allowDelegated = false)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return Validate(
            registerId, operation, submission.ApproverDid, submission.IsApproval,
            submission.Authorisation, now, isRevoked, allowDelegated);
    }

    /// <summary>
    /// Validates the structure of an approval's authorisation, from the fields the check needs.
    /// </summary>
    /// <remarks>
    /// The same rules as the submission overload, reachable from an approval read back off the
    /// ledger. It takes the three fields rather than a record so that a
    /// <see cref="GovernanceApprovalSubmission"/> and a <c>GovernanceApprovalActionPayload</c> can be
    /// validated by <b>one</b> implementation with no conversion between them: a hand-maintained
    /// mapping is precisely what dropped <c>ValidatorEntry</c> on the propose path and made
    /// <c>AddValidator</c> unusable without anything failing.
    /// </remarks>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The proposal being approved.</param>
    /// <param name="approverDid">Approving organisation.</param>
    /// <param name="isApproval">Approve or reject. Bound by the digest the signatures cover.</param>
    /// <param name="authorisation">Who stands behind the approval. <c>null</c> is refused (FR-029).</param>
    /// <param name="now">Evaluation time. Passed in so the result is deterministic across nodes (R-009).</param>
    /// <param name="isRevoked">
    /// Whether a delegation id has been revoked. Supplied by the caller because revocation lives on
    /// the ledger — the answer must come from sealed content so every node folds identically.
    /// </param>
    /// <param name="allowDelegated">
    /// Whether the delegated form may be considered at all. <b>The default is the policy</b>
    /// (T095 / R-023): there is no way to GRANT a delegation, so a delegation cannot have been
    /// legitimately issued and is refused as
    /// <see cref="AuthorisationRefusalReason.DelegationNotAvailable"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>On <paramref name="allowDelegated"/> being a parameter rather than a constant.</b> The
    /// delegation machinery below is correct and covered by tests that must keep running, so a hard
    /// constant would have forced either deleting that coverage or deleting the code it covers. A
    /// defaulted parameter keeps both: production reaches this method through exactly one call site
    /// (<c>DetachedApprovalVerifier</c>) which never passes it, so the refusal is unconditional in
    /// practice, while the tests that pin scope, expiry, revocation and key-binding opt in
    /// explicitly and remain honest about what they exercise.
    /// </para>
    /// <para>
    /// Any production caller passing <c>true</c> is therefore a bug, and a greppable one. When
    /// granting lands, flip the default here rather than spreading the argument through callers.
    /// </para>
    /// </remarks>
    public static AuthorisationValidationResult Validate(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval,
        ApprovalAuthorisation? authorisation,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        bool allowDelegated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);

        var auth = authorisation;
        if (auth is null)
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.Missing);
        }

        if (string.IsNullOrWhiteSpace(auth.IndividualDid)
            || string.IsNullOrWhiteSpace(auth.Signature)
            || string.IsNullOrWhiteSpace(auth.PublicKey))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.Incomplete);
        }

        // The approval statement is the same value the organisation signed: the individual (or the
        // machine they empowered) commits to exactly what the organisation committed to, not to a
        // summary of it.
        var approvalDigest = GovernanceApprovalStatement.ComputeDigest(
            registerId, operation, approverDid, isApproval);

        var checks = new List<RequiredSignatureCheck>
        {
            new("authorisation", approvalDigest, auth.Signature, auth.PublicKey),
        };

        if (auth.Kind == AuthorisationKind.Direct)
        {
            return auth.Delegation is not null
                ? AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.KindMismatch)
                : new AuthorisationValidationResult(true, AuthorisationRefusalReason.None, auth.IndividualDid, checks);
        }

        // T095 / R-023 — FAIL CLOSED. Nothing on the platform can grant a delegation, so a delegation
        // arriving here cannot have been legitimately issued, and nothing constrains who may claim to
        // have issued it. Refused BEFORE any of the checks below, so an unreachable-but-verifiable
        // path cannot be entered by hand-crafting a submission.
        //
        // Deliberately not a deletion: everything below is correct and a real granting path needs it
        // unchanged. Lift this refusal in the same change that adds granting.
        if (!allowDelegated)
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.DelegationNotAvailable);
        }

        var delegation = auth.Delegation;
        if (delegation is null
            || string.IsNullOrWhiteSpace(auth.DelegationSignature)
            || string.IsNullOrWhiteSpace(auth.DelegationPublicKey))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.KindMismatch);
        }

        if (!string.Equals(delegation.OrganisationDid, approverDid, StringComparison.Ordinal))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.WrongOrganisation);
        }

        if (!string.Equals(delegation.IndividualDid, auth.IndividualDid, StringComparison.Ordinal))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.IndividualMismatch);
        }

        // The delegation empowers ONE key. Without this a valid delegation would authorise any
        // machine that could quote it.
        if (!string.Equals(delegation.ApproverPublicKey, auth.PublicKey, StringComparison.Ordinal))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.ApproverKeyMismatch);
        }

        if (!delegation.Scope.Contains(operation.OperationType))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.OutOfScope);
        }

        if (delegation.ExpiresAt <= now)
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.Expired);
        }

        if (isRevoked is not null
            && !string.IsNullOrWhiteSpace(delegation.DelegationId)
            && isRevoked(delegation.DelegationId))
        {
            return AuthorisationValidationResult.Refuse(AuthorisationRefusalReason.Revoked);
        }

        // The grant itself must have been signed by the empowering individual — never asserted by the
        // server, which mints tokens and could therefore forge a claim (FR-033).
        checks.Add(new RequiredSignatureCheck(
            "delegation",
            GovernanceDelegationStatement.ComputeDigest(delegation),
            auth.DelegationSignature!,
            auth.DelegationPublicKey!));

        return new AuthorisationValidationResult(
            true, AuthorisationRefusalReason.None, delegation.IndividualDid, checks);
    }
}
