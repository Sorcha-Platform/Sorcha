// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;
using Sorcha.Register.Models.Enums;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Models.Registers;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Admin / governance operations on a register. Consumed by admin pages that
/// initiate registers, edit policy, propose policy updates, view governance
/// rosters, or toggle developer-mode controls.
/// </summary>
/// <remarks>
/// Split from <c>IRegisterService</c> as part of Feature 123. The user-facing
/// read half lives in <see cref="IRegisterReadService"/>. Pages that need both
/// halves inject both narrower interfaces — no derivation between them, per
/// the cross-audience convention documented in
/// <c>specs/123-ui-core-boundary-split/research.md</c> R7.
/// </remarks>
public interface IRegisterGovernanceService
{
    /// <summary>
    /// Gets the governance roster for a register.
    /// </summary>
    Task<Sorcha.UI.Core.Models.Blueprints.GovernanceRosterViewModel?> GetGovernanceRosterAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates register creation (phase 1 of genesis).
    /// </summary>
    Task<InitiateRegisterResponse?> InitiateRegisterAsync(
        CreateRegisterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes register creation (phase 2 of genesis).
    /// </summary>
    Task<FinalizeRegisterResponse?> FinalizeRegisterAsync(
        FinalizeRegisterRequest request,
        CancellationToken cancellationToken = default);

    // Policy
    Task<RegisterPolicyViewModel?> GetPolicyAsync(string registerId, CancellationToken ct = default);
    Task<PolicyHistoryViewModel> GetPolicyHistoryAsync(string registerId, int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<PolicyUpdateProposalViewModel?> ProposePolicyUpdateAsync(string registerId, RegisterPolicyFields policy, CancellationToken ct = default);

    // Governance proposals (Feature 189 T046/T084). Every field is REPORTED by the Register Service —
    // status, counts and the roster diff are all derived server-side from sealed content, never
    // recomputed here. A console deriving its own preview of a governance change would eventually
    // show an approver something other than what enacts.

    /// <summary>
    /// Lists the register's governance proposals with their derived status.
    /// </summary>
    /// <param name="registerId">Register to read.</param>
    /// <param name="status">
    /// <c>Open</c> / <c>Enacted</c> / <c>Invalidated</c> / <c>Expired</c>, or null for all. There is
    /// deliberately no <c>Withdrawn</c> — nothing on the platform can withdraw a proposal.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<GovernanceProposalPageViewModel> ListProposalsAsync(
        string registerId, string? status = null, CancellationToken ct = default);

    /// <summary>
    /// Full audit detail for one proposal: every approval attributed, the ones that cannot count with
    /// their reasons, and the roster as it would be. Null when the register carries no such proposal.
    /// </summary>
    Task<GovernanceProposalSummaryViewModel?> GetProposalAsync(
        string registerId, string proposalId, CancellationToken ct = default);

    /// <summary>
    /// Irreversibly disables dev mode on a register, enabling mandatory field-level encryption.
    /// </summary>
    Task DisableDevModeAsync(string registerId, CancellationToken ct = default);
}

// =====================================================================
// Governance request/response DTOs.
// These types are consumed only by the methods on IRegisterGovernanceService
// and were originally co-located with IRegisterService.cs. Kept here together
// because they're tightly coupled to the initiate/finalize flow.
// =====================================================================

/// <summary>
/// Request model for creating a new register (matches InitiateRegisterCreationRequest).
/// </summary>
public record CreateRegisterRequest
{
    /// <summary>
    /// Register name (1-38 characters)
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Purpose and scope of the register
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Register owners (at least one required)
    /// </summary>
    [JsonPropertyName("owners")]
    public required List<OwnerInfo> Owners { get; init; }

    /// <summary>
    /// Purpose classification for the register
    /// </summary>
    [JsonPropertyName("purpose")]
    public RegisterPurpose Purpose { get; init; } = RegisterPurpose.General;

    /// <summary>
    /// Whether to advertise this register to the peer network (public visibility)
    /// </summary>
    [JsonPropertyName("advertise")]
    public bool Advertise { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Owner information for register initialization.
/// </summary>
public record OwnerInfo
{
    /// <summary>
    /// User identifier
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Wallet identifier/address for signing
    /// </summary>
    [JsonPropertyName("walletId")]
    public required string WalletId { get; init; }
}

/// <summary>
/// Response from initiating register creation (matches InitiateRegisterCreationResponse).
/// </summary>
public record InitiateRegisterResponse
{
    /// <summary>
    /// Generated register ID
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Attestations that need to be signed by owners
    /// </summary>
    [JsonPropertyName("attestationsToSign")]
    public required List<AttestationToSign> AttestationsToSign { get; init; }

    /// <summary>
    /// When this initiation request expires
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Nonce for replay protection
    /// </summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }
}

/// <summary>
/// Attestation data that needs to be signed.
/// </summary>
public record AttestationToSign
{
    /// <summary>
    /// User identifier for who needs to sign
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Wallet identifier for signing
    /// </summary>
    [JsonPropertyName("walletId")]
    public required string WalletId { get; init; }

    /// <summary>
    /// Role being attested to
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// The attestation data structure
    /// </summary>
    [JsonPropertyName("attestationData")]
    public required AttestationSigningData AttestationData { get; init; }

    /// <summary>
    /// Hex-encoded SHA-256 hash to sign
    /// </summary>
    [JsonPropertyName("dataToSign")]
    public required string DataToSign { get; init; }
}

/// <summary>
/// Data structure that each owner signs to attest to register creation.
/// </summary>
public record AttestationSigningData
{
    /// <summary>
    /// Role being granted
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    /// <summary>
    /// Subject DID
    /// </summary>
    [JsonPropertyName("subject")]
    public required string Subject { get; init; }

    /// <summary>
    /// Register identifier
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Register name
    /// </summary>
    [JsonPropertyName("registerName")]
    public required string RegisterName { get; init; }

    /// <summary>
    /// When this attestation was granted
    /// </summary>
    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; init; }
}

/// <summary>
/// Request to finalize register creation (matches FinalizeRegisterCreationRequest).
/// </summary>
public record FinalizeRegisterRequest
{
    /// <summary>
    /// Register ID from initiation response
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Nonce from initiation (replay protection)
    /// </summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    /// <summary>
    /// Signed attestations from all owners
    /// </summary>
    [JsonPropertyName("signedAttestations")]
    public required List<SignedAttestation> SignedAttestations { get; init; }
}

/// <summary>
/// A signed attestation from an owner.
/// </summary>
public record SignedAttestation
{
    /// <summary>
    /// The attestation data that was signed
    /// </summary>
    [JsonPropertyName("attestationData")]
    public required AttestationSigningData AttestationData { get; init; }

    /// <summary>
    /// Public key used for signing (Base64)
    /// </summary>
    [JsonPropertyName("publicKey")]
    public required string PublicKey { get; init; }

    /// <summary>
    /// Signature of the attestation data hash (Base64)
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>
    /// Algorithm used for signing
    /// </summary>
    [JsonPropertyName("algorithm")]
    public required string Algorithm { get; init; }
}

/// <summary>
/// Response from finalizing register creation (matches FinalizeRegisterCreationResponse).
/// </summary>
public record FinalizeRegisterResponse
{
    /// <summary>
    /// Register identifier
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; init; }

    /// <summary>
    /// Creation status
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "created";

    /// <summary>
    /// Genesis transaction identifier
    /// </summary>
    [JsonPropertyName("genesisTransactionId")]
    public string? GenesisTransactionId { get; init; }

    /// <summary>
    /// When the register was created
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }
}
