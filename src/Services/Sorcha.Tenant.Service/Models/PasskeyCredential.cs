// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Status of a passkey credential in the system.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialStatus
{
    /// <summary>
    /// Credential is active and can be used for authentication.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Credential has been disabled (e.g., due to cloned authenticator detection).
    /// </summary>
    Disabled = 1,

    /// <summary>
    /// Credential has been permanently revoked by the owner or administrator.
    /// </summary>
    Revoked = 2
}

/// <summary>
/// Represents a FIDO2/WebAuthn passkey credential stored in the system.
/// Can be owned by either an OrgUser (UserIdentity) or a PublicIdentity.
/// </summary>
public class PasskeyCredential
{
    /// <summary>
    /// Unique identifier for this credential record.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FIDO2 credential ID (globally unique, assigned by the authenticator).
    /// Used to look up the authenticator during login.
    /// </summary>
    public byte[] CredentialId { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// COSE-encoded public key for signature verification.
    /// </summary>
    public byte[] PublicKeyCose { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Signature counter for cloned authenticator detection.
    /// Increments on each authentication; regression indicates a cloned authenticator.
    /// </summary>
    public int SignatureCounter { get; set; } = 0;

    /// <summary>
    /// Type of the credential owner: "OrgUser" or "PublicIdentity".
    /// </summary>
    public string OwnerType { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to the owner entity (UserIdentity.Id or PublicIdentity.Id).
    /// </summary>
    public Guid OwnerId { get; set; }

    /// <summary>
    /// Organization ID when the owner is an OrgUser; null for PublicIdentity owners.
    /// </summary>
    public Guid? OrganizationId { get; set; }

    /// <summary>
    /// Human-readable name for this credential (e.g., "My YubiKey", "Work Laptop").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Authenticator device type (e.g., "YubiKey 5 NFC", "Windows Hello", "TouchID").
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Attestation type returned during registration (e.g., "none", "packed", "tpm").
    /// </summary>
    public string AttestationType { get; set; } = "none";

    /// <summary>
    /// Authenticator Attestation GUID identifying the authenticator model.
    /// </summary>
    public Guid AaGuid { get; set; }

    /// <summary>
    /// Current status of this credential.
    /// </summary>
    public CredentialStatus Status { get; set; } = CredentialStatus.Active;

    /// <summary>
    /// Timestamp when the credential was registered (UTC).
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of the last successful authentication using this credential (UTC).
    /// Null if never used after registration.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Timestamp when the credential was disabled (UTC). Null if not disabled.
    /// </summary>
    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>
    /// Reason the credential was disabled (e.g., "Signature counter regression detected").
    /// </summary>
    public string? DisabledReason { get; set; }
}
