// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Core.Validators;

/// <summary>Outcome of verifying a detached approval.</summary>
/// <param name="Accepted">Whether the approval may be carried to, or counted from, the ledger.</param>
/// <param name="Reason">Machine-readable refusal reason. <see cref="AuthorisationRefusalReason.None"/> when accepted.</param>
/// <param name="Detail">Operator-facing explanation. Always populated on refusal (FR-011c).</param>
/// <param name="AccountableIndividualDid">The person the approval resolves to.</param>
public sealed record DetachedApprovalResult(
    bool Accepted,
    AuthorisationRefusalReason Reason,
    string Detail,
    string? AccountableIndividualDid);

/// <summary>Verifies an approval produced outside the platform's trust boundary.</summary>
public interface IDetachedApprovalVerifier
{
    /// <summary>Verifies structure, key ownership and every signature.</summary>
    Task<DetachedApprovalResult> VerifyAsync(
        string registerId,
        GovernanceOperation operation,
        GovernanceApprovalSubmission submission,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies the accountability block of an approval read back from the ledger.
    /// </summary>
    /// <remarks>
    /// The same check as the submission overload, reached from sealed content rather than from an
    /// HTTP body. It takes the three fields the check actually needs instead of a whole record, so
    /// that a submission and a sealed payload can be verified by <b>one</b> implementation without a
    /// hand-maintained conversion between them — the mapping shape that has already dropped fields
    /// silently in this feature (R-019).
    /// </remarks>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The operation as stored on the proposal, never as an approval offers it.</param>
    /// <param name="approverDid">Approving organisation.</param>
    /// <param name="isApproval">Approve or reject. Bound by the digest.</param>
    /// <param name="authorisation">Who stands behind the approval. <c>null</c> is a refusal, not a pass.</param>
    /// <param name="now">Evaluation time. Passed in so every node reaches the same answer (R-009).</param>
    /// <param name="isRevoked">Whether a delegation id has been revoked, answered from sealed content.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<DetachedApprovalResult> VerifyAuthorisationAsync(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval,
        ApprovalAuthorisation? authorisation,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        CancellationToken ct = default);
}

/// <summary>
/// The cryptographic half of accepting a detached approval (T078/T079).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GovernanceAuthorisationValidator"/> does the structural work in
/// <c>Sorcha.Register.Models</c>, which is a zero-dependency leaf and therefore cannot verify
/// signatures. It returns the digests that must verify; this does the verifying.
/// </para>
/// <para>
/// <b>Why it lives in <c>Sorcha.Validator.Core</c>.</b> Two sides need the identical answer: the
/// Register Service, which admits an approval arriving over HTTP, and the Validator, which recounts
/// the approvals sealed against a proposal before authorising an enactment — on every node, including
/// ones that never saw the submission. Two implementations of one rule is how the two would come to
/// disagree about whether a governance change is authorised, so there is one, in the project both
/// already reference.
/// </para>
/// <para>
/// <b>The check that was missing.</b> Structural validation confirms an authorisation names an
/// individual and that a signature verifies against the public key it supplies — but nothing bound
/// that public key to the claimed <c>IndividualDid</c>. Anyone could therefore have signed with their
/// own key while naming somebody else as accountable, which makes the accountability record a
/// self-declaration rather than evidence. A <c>did:sorcha:w:{address}</c> encodes the wallet address,
/// and an address is derived from its public key, so the binding is checkable:
/// re-derive the address from the offered key and compare. That is what
/// <see cref="VerifyKeyBelongsToDid"/> does, and no approval is accepted without it.
/// </para>
/// <para>
/// <b>It logs nothing, deliberately.</b> Every refusal is returned with a reason and a detail, and
/// the caller logs it in its own terms — the Register Service as a rejected submission, the Validator
/// as an approval excluded from a tally. That keeps this project free of a logging dependency and,
/// more usefully, stops a refusal being reported twice in two different vocabularies.
/// </para>
/// </remarks>
public sealed class DetachedApprovalVerifier : IDetachedApprovalVerifier
{
    private const string WalletDidPrefix = "did:sorcha:w:";

    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;

    /// <summary>Initialises a new instance of the <see cref="DetachedApprovalVerifier"/> class.</summary>
    public DetachedApprovalVerifier(ICryptoModule cryptoModule, IWalletUtilities walletUtilities)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
    }

    /// <inheritdoc />
    public Task<DetachedApprovalResult> VerifyAsync(
        string registerId,
        GovernanceOperation operation,
        GovernanceApprovalSubmission submission,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        return VerifyAuthorisationAsync(
            registerId, operation, submission.ApproverDid, submission.IsApproval,
            submission.Authorisation, now, isRevoked, ct);
    }

    /// <inheritdoc />
    public async Task<DetachedApprovalResult> VerifyAuthorisationAsync(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval,
        ApprovalAuthorisation? authorisation,
        DateTimeOffset now,
        Func<string, bool>? isRevoked = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);

        var structural = GovernanceAuthorisationValidator.Validate(
            registerId, operation, approverDid, isApproval, authorisation, now, isRevoked);

        if (!structural.IsAcceptable)
        {
            return Refuse(structural.Reason, $"Authorisation refused: {structural.Reason}.");
        }

        var auth = authorisation!;

        // Key ownership, before any signature is trusted to mean what it claims.
        //
        // WHICH key must belong to the individual differs by form, and conflating them is easy:
        //   Direct    — the individual signs, so THEIR key is auth.PublicKey.
        //   Delegated — the MACHINE signs, so auth.PublicKey is the bot's key (already tied to the
        //               grant by the structural ApproverKeyMismatch check). The individual's key is
        //               DelegationPublicKey, which signed the grant.
        // Demanding the bot's key derive to the person's DID rejects every valid delegated approval.
        if (auth.Kind == AuthorisationKind.Direct)
        {
            if (!VerifyKeyBelongsToDid(auth.PublicKey, auth.IndividualDid, auth.Algorithm))
            {
                return Refuse(
                    AuthorisationRefusalReason.IndividualMismatch,
                    "The signing key does not belong to the individual named as accountable.");
            }
        }
        else if (!VerifyKeyBelongsToDid(
                     auth.DelegationPublicKey!, auth.Delegation!.IndividualDid, auth.DelegationAlgorithm))
        {
            return Refuse(
                AuthorisationRefusalReason.IndividualMismatch,
                "The delegation was not signed by a key belonging to the individual who granted it.");
        }

        foreach (var check in structural.RequiredChecks)
        {
            var algorithm = check.Purpose == "delegation" ? auth.DelegationAlgorithm : auth.Algorithm;

            if (!await VerifySignatureAsync(check, algorithm, ct))
            {
                return Refuse(
                    AuthorisationRefusalReason.SignatureInvalid,
                    $"The {check.Purpose} signature did not verify.");
            }
        }

        return new DetachedApprovalResult(
            true, AuthorisationRefusalReason.None, "Accepted.", structural.AccountableIndividualDid);
    }

    /// <summary>
    /// Confirms a public key derives to the wallet address inside a <c>did:sorcha:w:{address}</c>.
    /// </summary>
    private bool VerifyKeyBelongsToDid(string publicKeyBase64, string did, SignatureAlgorithm algorithm)
    {
        if (!did.StartsWith(WalletDidPrefix, StringComparison.Ordinal))
        {
            // An unrecognised DID method cannot be checked, so it is not accepted. Failing open here
            // would make every other check in this class decorative.
            return false;
        }

        var claimedAddress = did[WalletDidPrefix.Length..];

        if (!GovernanceKeyMatcher.TryDecode(publicKeyBase64, out var publicKey)
            || !Enum.TryParse<WalletNetworks>(algorithm.ToString(), ignoreCase: true, out var network))
        {
            return false;
        }

        var derived = _walletUtilities.PublicKeyToWallet(publicKey, (byte)network);

        return derived is not null
               && string.Equals(derived, claimedAddress, StringComparison.Ordinal);
    }

    private async Task<bool> VerifySignatureAsync(
        RequiredSignatureCheck check, SignatureAlgorithm algorithm, CancellationToken ct)
    {
        if (!GovernanceKeyMatcher.TryDecode(check.SignatureBase64, out var signature)
            || !GovernanceKeyMatcher.TryDecode(check.PublicKeyBase64, out var publicKey)
            || !Enum.TryParse<WalletNetworks>(algorithm.ToString(), ignoreCase: true, out var network))
        {
            return false;
        }

        try
        {
            var status = await _cryptoModule.VerifyAsync(
                signature, check.Digest, (byte)network, publicKey, ct);

            return status == CryptoStatus.Success;
        }
        catch
        {
            // A verification that throws is a verification that failed. Never treat it as a pass.
            // The caller reports the refusal; see the remarks on this type for why nothing is logged
            // here.
            return false;
        }
    }

    private static DetachedApprovalResult Refuse(AuthorisationRefusalReason reason, string detail)
        => new(false, reason, detail, null);
}
