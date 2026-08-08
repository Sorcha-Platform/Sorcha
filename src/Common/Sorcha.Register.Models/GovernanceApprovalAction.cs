// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Identifies the published governance workflow and the actions the platform submits against it.
/// </summary>
/// <remarks>
/// <para>
/// The blueprint id was previously declared independently in three services
/// (<c>CryptoPolicyService</c>, <c>RightsEnforcementService</c>, <c>SystemRegisterService</c>) — a
/// literal that one side emits and another matches on, which is the drift shape CLAUDE.md pattern
/// #16 exists for. It lives here now, in the zero-dependency leaf every one of them already
/// references.
/// </para>
/// <para>
/// The action ids are the ids in <c>blueprints/templates/register-governance-v1.json</c>. FR-018
/// requires the recorded action sequence to match the published definition, so they are not free
/// choices — <c>GovernanceApprovalPayloadContractTests</c> reads the shipped blueprint and fails if
/// these drift from it.
/// </para>
/// </remarks>
public static class GovernanceBlueprint
{
    /// <summary>Id of the published governance workflow.</summary>
    public const string BlueprintId = "register-governance-v1";

    /// <summary>"Propose Change" — where a governance operation is raised.</summary>
    public const int ProposeChangeActionId = 1;

    /// <summary>"Collect Quorum" — where each organisation's approval is submitted.</summary>
    public const int CollectQuorumActionId = 2;
}

/// <summary>
/// The payload of a governance approval carried to the ledger as an action submission of
/// <see cref="GovernanceBlueprint"/> (T075 / R-009).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the approval's own signature lives in the payload and not in the transaction's signature
/// list.</b> The validator verifies every transaction signature against
/// <c>SHA-256("{txId}:{payloadHash}")</c>. An approver signs
/// <see cref="GovernanceApprovalStatement"/> — the register, the whole operation, who is approving,
/// and approve-versus-reject — which is deliberately <i>not</i> a transaction digest, because the
/// approver signs before any transaction exists and must bind what they reviewed rather than an
/// envelope they never saw. Putting that signature in <c>Signatures</c> would fail
/// <c>VAL_SIG_002</c> every time.
/// </para>
/// <para>
/// So the transaction envelope is signed by whichever roster organisation this node can sign as —
/// the <i>carry</i> — and the authority is this payload, which any node can re-verify from sealed
/// content alone. That separation is what makes quorum a pure function of the ledger (R-009): the
/// carrier cannot manufacture an approval it does not hold a signature for, and cannot suppress one
/// without the absence being visible.
/// </para>
/// <para>
/// <see cref="ProposalId"/> is not itself bound by the approval signature, and does not need to be:
/// verification rebuilds the statement from the operation stored on the proposal it names, so
/// re-pointing an approval at a different proposal changes the operation and the signature stops
/// verifying.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalActionPayload
{
    /// <summary>Discriminator value carried by every governance-approval payload.</summary>
    public const string PayloadType = "governance-approval";

    /// <summary>
    /// Type discriminator, inside the signed payload.
    /// </summary>
    /// <remarks>
    /// Deliberately in the payload rather than transaction metadata. Metadata is outside the
    /// signature, outside the payload hash and outside the docket merkle leaf, so anyone able to
    /// submit can rewrite it with nothing detecting the change — the C-VAL finding that moved the
    /// presentation-lifecycle predicates onto <c>Payload.type</c>.
    /// </remarks>
    public string Type { get; set; } = PayloadType;

    /// <summary>Transaction id of the proposal being voted on. The proposal <i>is</i> its own id.</summary>
    public string ProposalId { get; set; } = string.Empty;

    /// <summary>Approving organisation, as a roster subject (<c>did:sorcha:w:{address}</c>).</summary>
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>Approve or reject. Bound by the digest, so it cannot be flipped after signing.</summary>
    public bool IsApproval { get; set; } = true;

    /// <summary>The organisation's slot-100 signature over the approval statement. Verbatim from the submission.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>The organisation's slot-100 public key. Verbatim from the submission.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>How the organisation's key was held. Recorded, not enforced (R-016 / R-023).</summary>
    public ApprovalAuthMethod AuthMethod { get; set; }

    /// <summary>
    /// Statement version the signature commits to. Carried so a later reader knows which statement to
    /// rebuild rather than inferring it — v1 signatures must not verify under v2 (R-011).
    /// </summary>
    public string StatementVersion { get; set; } = GovernanceApprovalStatement.StatementVersion;

    /// <summary>Who stands behind this approval. Required on every approval (FR-029).</summary>
    public ApprovalAuthorisation? Authorisation { get; set; }

    /// <summary>Free text for the audit trail.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// The exact options this payload is serialised with, on the wire and in every test.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exposed rather than left to the call site because the bytes are hashed and signed: a caller
    /// that serialised with ambient options would produce a payload hash the envelope signature does
    /// not cover.
    /// </para>
    /// <para>
    /// The enum converters are registered per-type rather than as one blanket
    /// <c>JsonStringEnumConverter</c> because the published schema does not use one convention:
    /// <c>authMethod</c> and <c>kind</c> are kebab-case (<c>hardware-backed</c>, <c>direct</c>)
    /// while a delegation's <c>scope</c> carries <see cref="GovernanceOperationType"/> names as
    /// declared (<c>CryptoPolicyUpdate</c>). A single policy would silently rewrite one of them.
    /// </para>
    /// </remarks>
    public static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters =
        {
            new JsonStringEnumConverter<ApprovalAuthMethod>(JsonNamingPolicy.KebabCaseLower),
            new JsonStringEnumConverter<AuthorisationKind>(JsonNamingPolicy.KebabCaseLower),
            new JsonStringEnumConverter<GovernanceOperationType>(),
        },
    };

    /// <summary>
    /// Projects a verified detached submission onto the ledger payload.
    /// </summary>
    /// <remarks>
    /// Signature, public key and the whole authorisation travel <b>verbatim</b>. Re-encoding them
    /// would change the bytes a later verifier decodes, and a signature that no longer verifies is
    /// indistinguishable from a forged one.
    /// </remarks>
    /// <param name="proposalId">Transaction id of the proposal being voted on.</param>
    /// <param name="submission">The submission, already verified by <c>IDetachedApprovalVerifier</c>.</param>
    public static GovernanceApprovalActionPayload FromSubmission(
        string proposalId, GovernanceApprovalSubmission submission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentNullException.ThrowIfNull(submission);

        return new GovernanceApprovalActionPayload
        {
            ProposalId = proposalId,
            ApproverDid = submission.ApproverDid,
            IsApproval = submission.IsApproval,
            Signature = submission.Signature,
            PublicKey = submission.PublicKey,
            AuthMethod = submission.AuthMethod,
            Authorisation = submission.Authorisation,
            Comment = submission.Comment,
        };
    }
}
