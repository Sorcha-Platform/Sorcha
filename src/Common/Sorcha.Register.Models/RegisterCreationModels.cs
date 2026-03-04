// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Request to initiate register creation (Phase 1)
/// </summary>
public class InitiateRegisterCreationRequest
{
    /// <summary>
    /// Human-readable register name
    /// </summary>
    [Required]
    [StringLength(38, MinimumLength = 1)]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Purpose and scope of the register
    /// </summary>
    [StringLength(500)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Owning tenant/organization identifier
    /// </summary>
    [Required]
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Register owners (at least one required)
    /// </summary>
    /// <remarks>
    /// Each owner will need to sign an attestation to approve register creation.
    /// Multiple owners provide multi-party approval for register creation.
    /// </remarks>
    [Required]
    [MinLength(1, ErrorMessage = "At least one owner is required")]
    [JsonPropertyName("owners")]
    public List<OwnerInfo> Owners { get; set; } = new();

    /// <summary>
    /// Additional administrators to grant access
    /// </summary>
    [JsonPropertyName("additionalAdmins")]
    public List<AdditionalAdminInfo>? AdditionalAdmins { get; set; }

    /// <summary>
    /// Additional register metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Whether to advertise this register to the peer network (default: false/private)
    /// </summary>
    [JsonPropertyName("advertise")]
    public bool Advertise { get; set; }

    /// <summary>
    /// Optional operational policy for the register. When omitted, default policy is applied at genesis.
    /// </summary>
    [JsonPropertyName("policy")]
    public RegisterPolicy? Policy { get; set; }
}

/// <summary>
/// Owner information for register initialization
/// </summary>
public class OwnerInfo
{
    /// <summary>
    /// User identifier (DID)
    /// </summary>
    [Required]
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Wallet identifier for signing
    /// </summary>
    [Required]
    [JsonPropertyName("walletId")]
    public string WalletId { get; set; } = string.Empty;
}

/// <summary>
/// Creator information for register initialization (legacy compatibility)
/// </summary>
[Obsolete("Use OwnerInfo instead. This class is maintained for backward compatibility.")]
public class CreatorInfo : OwnerInfo
{
}

/// <summary>
/// Additional administrator information
/// </summary>
public class AdditionalAdminInfo
{
    /// <summary>
    /// User identifier
    /// </summary>
    [Required]
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Wallet identifier for signing
    /// </summary>
    [Required]
    [JsonPropertyName("walletId")]
    public string WalletId { get; set; } = string.Empty;

    /// <summary>
    /// Role to grant (defaults to Admin)
    /// </summary>
    [JsonPropertyName("role")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisterRole Role { get; set; } = RegisterRole.Admin;
}

/// <summary>
/// Response from register initiation (Phase 1)
/// </summary>
public class InitiateRegisterCreationResponse
{
    /// <summary>
    /// Generated register identifier
    /// </summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Attestations that need to be signed by owners/admins
    /// </summary>
    /// <remarks>
    /// Each owner/admin must sign their individual attestation data.
    /// The attestation data includes role, subject, registerId, registerName, and grantedAt.
    /// </remarks>
    [JsonPropertyName("attestationsToSign")]
    public List<AttestationToSign> AttestationsToSign { get; set; } = new();

    /// <summary>
    /// Expiration time for this pending registration
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Nonce for replay protection
    /// </summary>
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Legacy field: SHA-256 hash of control record (deprecated)
    /// </summary>
    [Obsolete("Use AttestationsToSign instead. Each owner/admin signs their individual attestation.")]
    [JsonPropertyName("dataToSign")]
    public string? DataToSign { get; set; }

    /// <summary>
    /// Legacy field: Control record template (deprecated)
    /// </summary>
    [Obsolete("Control record is constructed during finalization after all attestations are signed.")]
    [JsonPropertyName("controlRecord")]
    public RegisterControlRecord? ControlRecord { get; set; }
}

/// <summary>
/// Attestation data that needs to be signed by an owner or admin
/// </summary>
public class AttestationToSign
{
    /// <summary>
    /// User identifier (wallet address) for the person who needs to sign
    /// </summary>
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Wallet identifier for signing
    /// </summary>
    [JsonPropertyName("walletId")]
    public string WalletId { get; set; } = string.Empty;

    /// <summary>
    /// Role being attested to
    /// </summary>
    [JsonPropertyName("role")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisterRole Role { get; set; }

    /// <summary>
    /// Attestation data to sign (canonical JSON)
    /// </summary>
    [JsonPropertyName("attestationData")]
    public AttestationSigningData AttestationData { get; set; } = new();

    /// <summary>
    /// Hex-encoded SHA-256 hash of the canonical JSON attestation data
    /// </summary>
    /// <remarks>
    /// This is the SHA-256 hash that should be signed by the wallet at walletId.
    /// The value is a lowercase hex string (64 characters) representing the hash bytes.
    /// Callers should convert this hex string to bytes and sign with isPreHashed=true.
    /// </remarks>
    [JsonPropertyName("dataToSign")]
    public string DataToSign { get; set; } = string.Empty;
}

/// <summary>
/// Request to finalize register creation (Phase 2)
/// </summary>
public class FinalizeRegisterCreationRequest
{
    /// <summary>
    /// Register identifier from initiation phase
    /// </summary>
    [Required]
    [StringLength(32, MinimumLength = 32)]
    [RegularExpression("^[a-f0-9]{32}$")]
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Nonce from initiation (replay protection)
    /// </summary>
    [Required]
    [JsonPropertyName("nonce")]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// Signed attestations from all owners/admins
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one signed attestation is required")]
    [JsonPropertyName("signedAttestations")]
    public List<SignedAttestation> SignedAttestations { get; set; } = new();

    /// <summary>
    /// Legacy field: Control record (deprecated)
    /// </summary>
    [Obsolete("Use SignedAttestations instead. Control record is constructed from attestations.")]
    [JsonPropertyName("controlRecord")]
    public RegisterControlRecord? ControlRecord { get; set; }
}

/// <summary>
/// A signed attestation from an owner or admin
/// </summary>
public class SignedAttestation
{
    /// <summary>
    /// The attestation data that was signed
    /// </summary>
    [Required]
    [JsonPropertyName("attestationData")]
    public AttestationSigningData AttestationData { get; set; } = new();

    /// <summary>
    /// Public key used for signing (Base64)
    /// </summary>
    [Required]
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; } = string.Empty;

    /// <summary>
    /// Signature of the attestation data hash (Base64)
    /// </summary>
    [Required]
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = string.Empty;

    /// <summary>
    /// Algorithm used for signing
    /// </summary>
    [Required]
    [JsonPropertyName("algorithm")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SignatureAlgorithm Algorithm { get; set; }
}

/// <summary>
/// Response from register finalization (Phase 2)
/// </summary>
public class FinalizeRegisterCreationResponse
{
    /// <summary>
    /// Register identifier
    /// </summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Creation status
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "created";

    /// <summary>
    /// Genesis transaction identifier
    /// </summary>
    [JsonPropertyName("genesisTransactionId")]
    public string GenesisTransactionId { get; set; } = string.Empty;

    /// <summary>
    /// Genesis docket identifier (always "0")
    /// </summary>
    [JsonPropertyName("genesisDocketId")]
    public string GenesisDocketId { get; set; } = "0";

    /// <summary>
    /// When the register was created
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Data structure that each owner/admin signs to attest to register creation
/// </summary>
/// <remarks>
/// This structure is serialized to canonical JSON (RFC 8785) and hashed with SHA-256.
/// The hash is then signed by the owner's wallet using their private key.
/// Including registerName prevents name changes after attestation signing.
/// </remarks>
public class AttestationSigningData
{
    /// <summary>
    /// Role being granted (Owner, Admin, etc.)
    /// </summary>
    [Required]
    [JsonPropertyName("role")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RegisterRole Role { get; set; }

    /// <summary>
    /// Subject DID (e.g., "did:sorcha:user-001")
    /// </summary>
    [Required]
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Register identifier being attested to
    /// </summary>
    [Required]
    [JsonPropertyName("registerId")]
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Register name (immutable after signing)
    /// </summary>
    [Required]
    [JsonPropertyName("registerName")]
    public string RegisterName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when this attestation was granted
    /// </summary>
    [Required]
    [JsonPropertyName("grantedAt")]
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>
/// Pending register registration (stored temporarily during two-phase creation)
/// </summary>
public class PendingRegistration
{
    /// <summary>
    /// Generated register identifier
    /// </summary>
    public string RegisterId { get; set; } = string.Empty;

    /// <summary>
    /// Control record template
    /// </summary>
    public RegisterControlRecord ControlRecord { get; set; } = new();

    /// <summary>
    /// SHA-256 hash of canonical control record JSON
    /// </summary>
    public string ControlRecordHash { get; set; } = string.Empty;

    /// <summary>
    /// When this pending registration was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// When this pending registration expires (5 minutes from creation)
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Nonce for replay protection
    /// </summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 hash bytes for each attestation, keyed by "{role}:{subject}"
    /// </summary>
    /// <remarks>
    /// Stored at initiate, consumed at finalize for signature verification.
    /// Using stored hashes eliminates the need to re-serialize and re-hash
    /// attestation data during finalization, avoiding canonicalization fragility.
    /// </remarks>
    public Dictionary<string, byte[]> AttestationHashes { get; set; } = new();

    /// <summary>
    /// Whether to advertise this register to the peer network
    /// </summary>
    public bool Advertise { get; set; }

    /// <summary>
    /// Checks if this pending registration has expired
    /// </summary>
    public bool IsExpired() => DateTimeOffset.UtcNow > ExpiresAt;
}
