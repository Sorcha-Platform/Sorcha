// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Register.Models;

/// <summary>
/// Builds the canonical bytes an organisation signs to accept a seat on a register's governance
/// roster (Feature 193).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (#1464).</b> A governance <c>Add</c> seated a member with an empty public key
/// and nothing ever filled it in. Roster authority is matched BY KEY, so the member was entitled on
/// paper and inert in fact: every approval it produced was excluded from every tally and every
/// transaction it signed was refused.
/// </para>
/// <para>
/// <b>Why the key cannot simply be looked up.</b> Two shortcuts were tried and both are wrong.
/// Deriving it from the subject DID yields the wallet's PRIMARY key — a <c>ws1</c> address is
/// Bech32 over <c>[network][publicKey]</c> — while governance matches the organisation's SLOT-100
/// key (<c>sorcha:register-attestation</c>). Measured on n1: <c>VHWQB/…</c> against
/// <c>thfb6l9P…</c>, different keys, so the derived value is non-empty and WRONG, which is worse
/// than empty because it passes every check and fails silently at tally time. Resolving the key
/// from the wallet service at enactment breaks DETERMINISM: a node that does not host that
/// organisation's wallet would fold different bytes into the same transaction id.
/// </para>
/// <para>
/// So the key has to arrive in sealed content, carried on the proposal — and a carried key is only
/// worth anything if its holder proved they hold it. That proof is a signature over this statement.
/// </para>
/// <para>
/// <b>What the statement binds, and why each part matters.</b>
/// </para>
/// <list type="bullet">
/// <item><b>registerId</b> — an acceptance for one register must not seat the organisation on
/// another.</item>
/// <item><b>subject</b> — so one organisation's acceptance cannot be attributed to another.</item>
/// <item><b>role</b> — this is consent to a SPECIFIC SEAT. Accepting an <c>Admin</c> seat must not
/// authorise being seated as <c>Owner</c>, which is the difference between a party that can vote
/// and one that can transfer the register.</item>
/// <item><b>publicKey</b> — the statement commits to the very key being recorded, so a valid
/// signature cannot be paired with a different key in the same proposal.</item>
/// <item><b>rosterSnapshotId</b> — the roster head the acceptance was produced against. Without it
/// an acceptance is valid forever: an organisation deliberately REMOVED from a roster could be
/// re-seated by replaying its original signature. This mirrors FR-011b, which already invalidates a
/// proposal whose snapshot no longer matches the current head, so the concept and its failure mode
/// are established rather than invented here.</item>
/// </list>
/// <para>
/// Fields are joined with a unit separator (<c>0x1F</c>) rather than a printable delimiter, so a
/// value containing the delimiter cannot shift the field boundaries and make two different
/// statements hash identically.
/// </para>
/// <para>
/// <b>One implementation.</b> The producer (the organisation accepting), the Register Service at
/// propose time, and the Validator recounting sealed content on every node must all call this.
/// Rebuilding the statement anywhere else is how a producer and a verifier come to disagree about
/// what was signed — the failure then surfaces as an opaque authorisation refusal a long way from
/// the cause.
/// </para>
/// </remarks>
public static class GovernanceSeatAcceptanceStatement
{
    /// <summary>Field separator — a control character that cannot occur in a DID, role or key.</summary>
    private const char UnitSeparator = '';

    /// <summary>Domain tag. A v1 signature MUST NOT verify under a future v2.</summary>
    public const string StatementVersion = "sorcha:governance-seat-acceptance:v1";

    /// <summary>
    /// Computes the SHA-256 digest an organisation signs to accept a roster seat, pre-hashed.
    /// </summary>
    /// <param name="registerId">Register offering the seat.</param>
    /// <param name="subject">DID of the organisation being seated.</param>
    /// <param name="role">Role being accepted.</param>
    /// <param name="publicKey">Base64 slot-100 governance key being recorded on the roster.</param>
    /// <param name="rosterSnapshotId">Roster head the acceptance is produced against.</param>
    public static byte[] ComputeDigest(
        string registerId,
        string subject,
        RegisterRole role,
        string publicKey,
        string rosterSnapshotId)
        => SHA256.HashData(Encoding.UTF8.GetBytes(
            BuildStatement(registerId, subject, role, publicKey, rosterSnapshotId)));

    /// <summary>
    /// The canonical statement string, exposed for diagnostics and for producers that need to show
    /// an operator exactly what is being signed.
    /// </summary>
    public static string BuildStatement(
        string registerId,
        string subject,
        RegisterRole role,
        string publicKey,
        string rosterSnapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(rosterSnapshotId);

        return string.Join(UnitSeparator,
            StatementVersion,
            registerId,
            subject,
            role.ToString(),
            publicKey,
            rosterSnapshotId);
    }
}
