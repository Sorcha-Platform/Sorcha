// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Core.Validators;

/// <summary>Why a seat acceptance was refused.</summary>
public enum SeatAcceptanceRefusalReason
{
    /// <summary>Accepted.</summary>
    None = 0,

    /// <summary>No acceptance was supplied for an operation that seats a member.</summary>
    Missing,

    /// <summary>The acceptance is present but a required field is blank.</summary>
    Incomplete,

    /// <summary>The proposal carries no roster snapshot, so the acceptance cannot be bound to a roster.</summary>
    NoRosterSnapshot,

    /// <summary>The signature did not verify against the carried signing key over the seat statement.</summary>
    SignatureInvalid,

    /// <summary>The signing key does not belong to the organisation being seated.</summary>
    SubjectMismatch,

    /// <summary>The carried key could not be decoded.</summary>
    KeyUndecodable,
}

/// <summary>Outcome of verifying a seat acceptance.</summary>
/// <param name="Accepted">Whether the acceptance is valid.</param>
/// <param name="Reason">Machine-readable refusal reason.</param>
/// <param name="Detail">Operator-facing explanation.</param>
public readonly record struct SeatAcceptanceResult(
    bool Accepted,
    SeatAcceptanceRefusalReason Reason,
    string? Detail)
{
    /// <summary>An accepted result.</summary>
    public static SeatAcceptanceResult Ok() => new(true, SeatAcceptanceRefusalReason.None, null);

    /// <summary>A refusal with a named reason.</summary>
    public static SeatAcceptanceResult Refuse(SeatAcceptanceRefusalReason reason, string detail)
        => new(false, reason, detail);
}

/// <summary>
/// Verifies that an organisation being seated by governance proved it holds the key being recorded
/// for it (Feature 193 / #1464).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives in <c>Sorcha.Validator.Core</c>.</b> It is a LEDGER rule, not a submission
/// check. The Register Service runs it when a proposal is raised, and the Validator runs the
/// identical check on every node when it validates the sealed control transaction. A rule only the
/// proposing node enforced would let another node accept a roster key nobody ever proved — and two
/// implementations of one rule is how the two would come to disagree about whether a governance
/// change is authorised. <see cref="IDetachedApprovalVerifier"/> lives here for exactly the same
/// reason.
/// </para>
/// <para>
/// <b>What it does not do.</b> It does not decide whether the operation is authorised, who may
/// propose, or whether quorum is met. It answers one question: does the organisation named as the
/// target hold the key this proposal wants to put on the roster?
/// </para>
/// </remarks>
public interface ISeatAcceptanceVerifier
{
    /// <summary>
    /// Verifies the acceptance carried by an operation that seats a member. Operations that seat
    /// nobody are accepted unchanged.
    /// </summary>
    Task<SeatAcceptanceResult> VerifyAsync(
        string registerId, GovernanceOperation operation, CancellationToken ct = default);
}

/// <inheritdoc cref="ISeatAcceptanceVerifier" />
public sealed class SeatAcceptanceVerifier : ISeatAcceptanceVerifier
{
    private const string WalletDidPrefix = "did:sorcha:w:";

    private readonly ICryptoModule _cryptoModule;
    private readonly IWalletUtilities _walletUtilities;

    /// <summary>Creates the verifier.</summary>
    /// <remarks>
    /// Takes <see cref="ICryptoModule"/> directly, matching <see cref="DetachedApprovalVerifier"/>.
    /// A new signature-verification abstraction here would be a second seam over the same
    /// operation, which is how two verifiers come to disagree.
    /// </remarks>
    public SeatAcceptanceVerifier(ICryptoModule cryptoModule, IWalletUtilities walletUtilities)
    {
        _cryptoModule = cryptoModule ?? throw new ArgumentNullException(nameof(cryptoModule));
        _walletUtilities = walletUtilities ?? throw new ArgumentNullException(nameof(walletUtilities));
    }

    /// <inheritdoc />
    public async Task<SeatAcceptanceResult> VerifyAsync(
        string registerId, GovernanceOperation operation, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);

        // Only Add seats a member. Remove, Transfer and the validator-roster operations carry no
        // acceptance and are not this verifier's business — Transfer's safety comes from the target
        // already being a keyed Admin, enforced by ApplyOperation's promotion guard.
        if (operation.OperationType != GovernanceOperationType.Add)
        {
            return SeatAcceptanceResult.Ok();
        }

        var acceptance = operation.TargetAcceptance;
        if (acceptance is null)
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.Missing,
                $"Seating '{operation.TargetDid}' requires its signed acceptance carrying the "
                + "governance key to record. Without one the roster would hold an empty key and the "
                + "member could never sign (#1464).");
        }

        if (string.IsNullOrWhiteSpace(acceptance.GovernanceKey)
            || string.IsNullOrWhiteSpace(acceptance.SigningPublicKey)
            || string.IsNullOrWhiteSpace(acceptance.Signature))
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.Incomplete,
                "The seat acceptance is missing its governance key, signing key or signature.");
        }

        // The acceptance is bound to the roster it was produced against, so it cannot be replayed to
        // re-seat an organisation that was later removed. A proposal with no snapshot cannot be
        // bound at all, so it fails closed rather than verifying against nothing.
        if (string.IsNullOrWhiteSpace(operation.RosterSnapshotId))
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.NoRosterSnapshot,
                "The proposal carries no rosterSnapshotId, so the acceptance cannot be bound to a "
                + "roster and would remain valid forever.");
        }

        if (!GovernanceKeyMatcher.TryDecode(acceptance.SigningPublicKey, out var signingKeyBytes))
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.KeyUndecodable,
                "The carried signing key could not be decoded.");
        }

        // THE attribution check. Without it the signature proves only that SOMEBODY holds the
        // signing key -- so a proposer could seat another organisation carrying the proposer's own
        // governance key, then produce that organisation's approvals and vote twice.
        //
        // The primary key is checkable against the subject because a ws1 address is Bech32 over
        // [network][publicKey]. The slot-100 governance key has no such binding, which is exactly
        // why it is NAMED by the statement rather than used to sign it.
        if (!SigningKeyBelongsToSubject(acceptance, operation.TargetDid, signingKeyBytes))
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.SubjectMismatch,
                $"The acceptance for '{operation.TargetDid}' is signed by a key that does not belong "
                + "to that organisation, so nothing shows it nominated this governance key.");
        }

        // The SAME canonicalisation the producer signed and the Validator will rebuild.
        var digest = GovernanceSeatAcceptanceStatement.ComputeDigest(
            registerId,
            operation.TargetDid,
            operation.TargetRole,
            acceptance.GovernanceKey,
            operation.RosterSnapshotId);

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(acceptance.Signature);
        }
        catch (FormatException)
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.Incomplete,
                "The seat acceptance signature is not valid base64.");
        }

        if (!Enum.TryParse<WalletNetworks>(acceptance.Algorithm.ToString(), ignoreCase: true, out var network))
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.KeyUndecodable,
                $"Unrecognised signature algorithm '{acceptance.Algorithm}' on the seat acceptance.");
        }

        bool verified;
        try
        {
            var status = await _cryptoModule.VerifyAsync(signatureBytes, digest, (byte)network, signingKeyBytes, ct);
            verified = status == CryptoStatus.Success;
        }
        catch
        {
            // A verification that throws is a verification that failed. Never treat it as a pass.
            verified = false;
        }

        if (!verified)
        {
            return SeatAcceptanceResult.Refuse(
                SeatAcceptanceRefusalReason.SignatureInvalid,
                $"The acceptance for '{operation.TargetDid}' does not verify against the signing "
                + "key it carries.");
        }

        return SeatAcceptanceResult.Ok();
    }

    /// <summary>
    /// Whether the acceptance's signing key is the primary key of the subject's wallet.
    /// </summary>
    /// <remarks>
    /// An unrecognised DID method cannot be checked, so it is refused. Failing open here would make
    /// every other check in this class decorative — the same reasoning as
    /// <c>DetachedApprovalVerifier.VerifyKeyBelongsToDid</c>, whose logic this mirrors.
    /// </remarks>
    private bool SigningKeyBelongsToSubject(
        GovernanceSeatAcceptance acceptance, string subject, byte[] signingKey)
    {
        if (string.IsNullOrWhiteSpace(subject)
            || !subject.StartsWith(WalletDidPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var claimedAddress = subject[WalletDidPrefix.Length..];

        if (!Enum.TryParse<WalletNetworks>(acceptance.Algorithm.ToString(), ignoreCase: true, out var network))
        {
            return false;
        }

        var derived = _walletUtilities.PublicKeyToWallet(signingKey, (byte)network);
        return derived is not null && string.Equals(derived, claimedAddress, StringComparison.Ordinal);
    }
}
