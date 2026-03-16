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
/// Owned by a PlatformUser (platform-wide identity anchor).
/// Stored in the public schema.
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
    /// Stored as long to avoid lossy cast from FIDO2's uint SignCount.
    /// </summary>
    public long SignatureCounter { get; set; } = 0;

    /// <summary>
    /// Foreign key to the PlatformUser who owns this credential.
    /// </summary>
    public Guid PlatformUserId { get; set; }

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

    /// <summary>
    /// Navigation property to the owning PlatformUser.
    /// </summary>
    public PlatformUser PlatformUser { get; set; } = null!;
}
