// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Register.Models;

/// <summary>
/// Builds the canonical bytes an organisation signs when approving (or rejecting) a governance
/// operation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (Feature 189 US2 / option A).</b> <see cref="ApprovalSignature"/> has always
/// carried a <c>Signature</c> field, and nothing in the platform ever verified it.
/// <c>GovernanceRosterService.ValidateQuorumAsync</c> counted an approval if its
/// <c>ApproverDid</c> merely appeared in the roster — so quorum was satisfied by <i>asserting</i>
/// approvals, with no cryptography involved at any point. Whoever composed the proposal payload
/// could claim every other member's approval.
/// </para>
/// <para>
/// <b>What the statement binds, and why each part matters.</b> A signature is only as good as the
/// thing it commits to. Every field below is included so an otherwise-valid signature cannot be
/// lifted and reused:
/// </para>
/// <list type="bullet">
/// <item><b>registerId</b> — an approval on one register must not authorise the same operation on
/// another.</item>
/// <item><b>operationType</b> + <b>targetDid</b> + <b>targetRole</b> — approving "add B as Auditor"
/// must not authorise "add B as Owner", which is the difference between a reader and a party who
/// can transfer the register.</item>
/// <item><b>proposerDid</b> and <b>proposedAt</b> — together these pin the specific proposal, so an
/// approval cannot be replayed onto a later, differently-intentioned proposal of the same shape.</item>
/// <item><b>approverDid</b> — so one member's signature cannot be attributed to another.</item>
/// <item><b>isApproval</b> — so a <i>rejection</i> cannot be counted as an approval by flipping a
/// boolean the signature does not cover. This is the field most easily forgotten, and the one whose
/// omission silently inverts a vote.</item>
/// </list>
/// <para>
/// Fields are joined with a unit separator (<c>0x1F</c>) rather than a printable delimiter, so a
/// value containing the delimiter cannot shift the field boundaries and make two different
/// statements hash identically.
/// </para>
/// </remarks>
public static class GovernanceApprovalStatement
{
    /// <summary>Field separator — a control character that cannot occur in a DID or enum name.</summary>
    private const char UnitSeparator = '';

    /// <summary>
    /// Computes the SHA-256 digest an approver signs, pre-hashed.
    /// </summary>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The proposal being voted on.</param>
    /// <param name="approverDid">DID of the approving organisation.</param>
    /// <param name="isApproval"><c>true</c> to approve, <c>false</c> to reject.</param>
    public static byte[] ComputeDigest(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverDid);

        return SHA256.HashData(Encoding.UTF8.GetBytes(
            BuildStatement(registerId, operation, approverDid, isApproval)));
    }

    /// <summary>
    /// The canonical statement string, exposed for diagnostics and for producers that need to show
    /// an operator exactly what is being signed.
    /// </summary>
    public static string BuildStatement(
        string registerId,
        GovernanceOperation operation,
        string approverDid,
        bool isApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverDid);

        // Round-trip ("O") on the timestamp: a culture- or precision-dependent rendering would make
        // the producer and the verifier disagree on the bytes for the same proposal.
        return string.Join(UnitSeparator,
            "sorcha:governance-approval:v1",
            registerId,
            operation.OperationType.ToString(),
            operation.ProposerDid ?? string.Empty,
            operation.TargetDid ?? string.Empty,
            operation.TargetRole.ToString(),
            operation.ProposedAt.ToUniversalTime().ToString("O"),
            approverDid,
            isApproval ? "approve" : "reject");
    }
}
