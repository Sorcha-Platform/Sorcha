// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>How the signing key was held. Recorded so a register can set its own bar (R-016).</summary>
public enum ApprovalAuthMethod
{
    /// <summary>Unstated. Treated as the weakest option by any policy that discriminates.</summary>
    Unknown = 0,

    /// <summary>Key held in a browser profile or equivalent software store.</summary>
    Software = 1,

    /// <summary>Key held in a secure element / enclave, typically with biometric unlock.</summary>
    HardwareBacked = 2,

    /// <summary>A service identity's key. Always accompanied by a delegation (R-017).</summary>
    Service = 3,
}

/// <summary>Whether the accountable individual signed directly, or empowered a machine to.</summary>
public enum AuthorisationKind
{
    /// <summary>The individual's own key signed alongside the organisation's.</summary>
    Direct = 0,

    /// <summary>A machine signed, under a delegation the individual granted.</summary>
    Delegated = 1,
}

/// <summary>
/// The accountability half of an approval — who stands behind it.
/// </summary>
/// <remarks>
/// Required on <b>every</b> approval (FR-029). The organisation's signature carries authority; this
/// carries responsibility. Without it the ledger records "org X approved" and can never say which
/// person decided, which is precisely what US3 exists to provide.
/// </remarks>
public sealed class ApprovalAuthorisation
{
    /// <summary>Direct or delegated.</summary>
    public AuthorisationKind Kind { get; set; }

    /// <summary>The individual who stands behind this approval.</summary>
    public string IndividualDid { get; set; } = string.Empty;

    /// <summary>Signature over the approval statement. Base64.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>Public key that produced <see cref="Signature"/>. Base64.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>How that key was held.</summary>
    public ApprovalAuthMethod AuthMethod { get; set; }

    /// <summary>
    /// Algorithm of <see cref="PublicKey"/>. Carried because, unlike an organisation, an individual
    /// has no roster entry recording it.
    /// </summary>
    public SignatureAlgorithm Algorithm { get; set; } = SignatureAlgorithm.ED25519;

    /// <summary>Algorithm of <see cref="DelegationPublicKey"/>.</summary>
    public SignatureAlgorithm DelegationAlgorithm { get; set; } = SignatureAlgorithm.ED25519;

    /// <summary>Present only when <see cref="Kind"/> is <see cref="AuthorisationKind.Delegated"/>.</summary>
    public GovernanceDelegation? Delegation { get; set; }

    /// <summary>Signature over the delegation statement, by the empowering individual. Base64.</summary>
    public string? DelegationSignature { get; set; }

    /// <summary>Public key that produced <see cref="DelegationSignature"/>. Base64.</summary>
    public string? DelegationPublicKey { get; set; }
}

/// <summary>
/// What an approver is asked to sign.
/// </summary>
/// <remarks>
/// <b>Carries no digest, deliberately (FR-028).</b> A server-supplied digest could fail to match the
/// operation the client displayed, reinstating at the transport layer exactly the substitution that
/// statement v2 closes in the digest. The client derives the digest from the operation it rendered,
/// so the two cannot disagree. The client must render the operation — signing an opaque value is not
/// approval (FR-027).
/// </remarks>
public sealed class GovernanceSigningRequest
{
    /// <summary>Correlates the eventual submission.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Register the operation applies to.</summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>The proposal, in full — everything the signature will bind.</summary>
    public GovernanceOperation Operation { get; set; } = new();

    /// <summary>Statement version the client must build. See <see cref="GovernanceApprovalStatement.StatementVersion"/>.</summary>
    public string StatementVersion { get; set; } = GovernanceApprovalStatement.StatementVersion;

    /// <summary>Organisation being asked to approve.</summary>
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>When this request lapses.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// A detached approval produced outside the platform's trust boundary (R-014).
/// </summary>
public sealed class GovernanceApprovalSubmission
{
    /// <summary>The request this answers.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Approving organisation.</summary>
    public string ApproverDid { get; set; } = string.Empty;

    /// <summary>
    /// Approve or reject. Bound by the digest, so a rejection cannot be turned into an approval by
    /// flipping a field the signature does not cover.
    /// </summary>
    public bool IsApproval { get; set; } = true;

    /// <summary>Organisation's slot-100 signature. Base64.</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>Organisation's slot-100 public key. Travels so the roster match needs no lookup. Base64.</summary>
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>How the organisation's key was held.</summary>
    public ApprovalAuthMethod AuthMethod { get; set; }

    /// <summary>Who stands behind this approval. Required (FR-029).</summary>
    public ApprovalAuthorisation? Authorisation { get; set; }

    /// <summary>Free text for the audit trail.</summary>
    public string? Comment { get; set; }
}
