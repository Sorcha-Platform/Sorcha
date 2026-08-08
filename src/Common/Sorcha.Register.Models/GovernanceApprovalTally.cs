// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>Why an approval was excluded from the tally.</summary>
public enum ApprovalTallyRefusal
{
    /// <summary>Not refused.</summary>
    None = 0,

    /// <summary>The approver is not on the roster the proposal was raised against.</summary>
    NotOnRoster,

    /// <summary>On the roster, but holding a role that cannot vote.</summary>
    NotAVotingRole,

    /// <summary>
    /// The key offered with the approval is not the key the roster records for that organisation.
    /// </summary>
    KeyNotTheRosterKey,

    /// <summary>A second approval from an organisation that has already voted.</summary>
    DuplicateApprover,

    /// <summary>The payload named no approver.</summary>
    NoApprover,
}

/// <summary>An approval excluded from the tally, with the reason.</summary>
/// <param name="ApproverDid">Organisation the approval claimed to be from.</param>
/// <param name="Refusal">Why it was excluded.</param>
public readonly record struct ExcludedApproval(string ApproverDid, ApprovalTallyRefusal Refusal);

/// <summary>
/// A signature the caller must verify before the approval counts.
/// </summary>
/// <param name="ApproverDid">Approving organisation.</param>
/// <param name="IsApproval">Whether this is an approval or a rejection.</param>
/// <param name="Digest">Statement digest the signature must verify against.</param>
/// <param name="SignatureBase64">Signature as it was recorded on the ledger.</param>
/// <param name="PublicKeyBase64">Key as the ROSTER records it, not as the payload offered it.</param>
/// <param name="Algorithm">Algorithm the roster records for that organisation.</param>
public readonly record struct ApprovalTallyCheck(
    string ApproverDid,
    bool IsApproval,
    byte[] Digest,
    string SignatureBase64,
    string PublicKeyBase64,
    SignatureAlgorithm Algorithm);

/// <summary>What the caller must verify, and what was excluded before it got that far.</summary>
/// <param name="Checks">Signatures to verify. Only verified ones may be counted.</param>
/// <param name="Excluded">Approvals refused structurally, with reasons (FR-011c).</param>
public sealed record ApprovalTallyPlan(
    IReadOnlyList<ApprovalTallyCheck> Checks,
    IReadOnlyList<ExcludedApproval> Excluded);

/// <summary>
/// Turns the approvals sealed against a proposal into the signature checks that decide quorum.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a plan rather than a verdict.</b> <c>Sorcha.Register.Models</c> is a zero-dependency
/// leaf and cannot verify a signature, but both the Validator (which must authorise an enactment) and
/// the Register Service (which decides when to raise one) need to reach the <i>same</i> answer from
/// the same sealed content. Reimplementing the filtering on each side is how the two would come to
/// disagree about whether a register's governance change is authorised — so the filtering lives here
/// once and each side supplies its own crypto. Same split as
/// <see cref="GovernanceAuthorisationValidator"/>.
/// </para>
/// <para>
/// <b>The key comes from the roster, never from the payload.</b> An approval carries the public key it
/// was signed with, which is convenient — and trusting it would make the signature self-certifying: an
/// attacker could sign with any key and supply that key. The roster is the authority on which key an
/// organisation governs with, so the check is built against the roster's key and an approval offering
/// a different one is excluded as <see cref="ApprovalTallyRefusal.KeyNotTheRosterKey"/> rather than
/// verified against its own choice.
/// </para>
/// <para>
/// <b>Counting is not done here.</b> The threshold arithmetic belongs to
/// <c>GovernanceRosterService.ValidateQuorumAsync</c>, which knows the register's configured
/// <see cref="QuorumFormula"/> (R-007). This produces the verified-approval list that goes into it.
/// </para>
/// </remarks>
public static class GovernanceApprovalTally
{
    /// <summary>
    /// Builds the signature checks for the approvals sealed against a proposal.
    /// </summary>
    /// <param name="registerId">Register the proposal belongs to.</param>
    /// <param name="operation">
    /// The operation <b>as stored on the proposal</b>. The digest is rebuilt from this, never from
    /// anything an approval carried, so an approval cannot choose what it is taken to have authorised.
    /// </param>
    /// <param name="roster">Roster the proposal was raised against.</param>
    /// <param name="approvals">
    /// Approval payloads decoded from the sealed approval transactions, in seal order. Order matters
    /// only for duplicate resolution: the first vote from an organisation stands.
    /// </param>
    public static ApprovalTallyPlan Prepare(
        string registerId,
        GovernanceOperation operation,
        RegisterControlRecord roster,
        IReadOnlyList<GovernanceApprovalActionPayload> approvals)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(approvals);

        var checks = new List<ApprovalTallyCheck>();
        var excluded = new List<ExcludedApproval>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var approval in approvals)
        {
            var did = approval.ApproverDid;

            if (string.IsNullOrWhiteSpace(did))
            {
                excluded.Add(new ExcludedApproval(string.Empty, ApprovalTallyRefusal.NoApprover));
                continue;
            }

            // First vote from an organisation stands. Checked before roster lookup so a repeated
            // approval is reported as the duplicate it is rather than re-derived.
            if (!seen.Add(did))
            {
                excluded.Add(new ExcludedApproval(did, ApprovalTallyRefusal.DuplicateApprover));
                continue;
            }

            var attestation = roster.Attestations.FirstOrDefault(
                a => string.Equals(a.Subject, did, StringComparison.Ordinal));

            if (attestation is null)
            {
                excluded.Add(new ExcludedApproval(did, ApprovalTallyRefusal.NotOnRoster));
                continue;
            }

            if (attestation.Role is not (RegisterRole.Owner or RegisterRole.Admin))
            {
                excluded.Add(new ExcludedApproval(did, ApprovalTallyRefusal.NotAVotingRole));
                continue;
            }

            if (!GovernanceKeyMatcher.Matches(attestation.PublicKey, approval.PublicKey))
            {
                excluded.Add(new ExcludedApproval(did, ApprovalTallyRefusal.KeyNotTheRosterKey));
                continue;
            }

            checks.Add(new ApprovalTallyCheck(
                ApproverDid: did,
                IsApproval: approval.IsApproval,
                Digest: GovernanceApprovalStatement.ComputeDigest(
                    registerId, operation, did, approval.IsApproval),
                SignatureBase64: approval.Signature,
                // The roster's key, deliberately — see the remarks on this type.
                PublicKeyBase64: attestation.PublicKey,
                Algorithm: attestation.Algorithm));
        }

        return new ApprovalTallyPlan(checks, excluded);
    }

    /// <summary>
    /// Projects the checks the caller verified onto the vote list the quorum arithmetic consumes.
    /// </summary>
    /// <param name="plan">The plan the checks came from.</param>
    /// <param name="verifiedApproverDids">
    /// Organisations whose signature the caller verified. Anything absent is discarded — a signature
    /// that did not verify is not a vote.
    /// </param>
    public static List<ApprovalSignature> ToVotes(
        ApprovalTallyPlan plan, ISet<string> verifiedApproverDids)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(verifiedApproverDids);

        return plan.Checks
            .Where(c => verifiedApproverDids.Contains(c.ApproverDid))
            .Select(c => new ApprovalSignature
            {
                ApproverDid = c.ApproverDid,
                Signature = c.SignatureBase64,
                IsApproval = c.IsApproval,
            })
            .ToList();
    }
}
