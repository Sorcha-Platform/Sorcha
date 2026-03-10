// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// PassKey-authenticated user without organizational affiliation.
/// Uses FIDO2/WebAuthn for passwordless authentication and optionally social login providers.
/// Credential details are stored in associated <see cref="PasskeyCredential"/> entities.
/// </summary>
public class PublicIdentity
{
    /// <summary>
    /// Unique public user identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Human-readable display name for the public user.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional email address for the public user. Used for account recovery and social login linking.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Account status (e.g., "Active", "Suspended", "Deleted").
    /// </summary>
    public string Status { get; set; } = "Active";

    /// <summary>
    /// Whether the user's email address has been verified.
    /// </summary>
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// Timestamp when the email was verified (UTC). Null if not yet verified.
    /// </summary>
    public DateTimeOffset? EmailVerifiedAt { get; set; }

    /// <summary>
    /// Authenticator device type (e.g., "YubiKey 5 NFC", "Windows Hello", "TouchID").
    /// Extracted from authenticator data if available during initial registration.
    /// </summary>
    public string? DeviceType { get; set; }

    /// <summary>
    /// Account registration timestamp (UTC).
    /// </summary>
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last successful authentication timestamp (UTC). Null if never used.
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// Collection of FIDO2/WebAuthn passkey credentials associated with this identity.
    /// </summary>
    public ICollection<PasskeyCredential> PasskeyCredentials { get; set; } = new List<PasskeyCredential>();

    /// <summary>
    /// Collection of social login provider links associated with this identity.
    /// </summary>
    public ICollection<SocialLoginLink> SocialLoginLinks { get; set; } = new List<SocialLoginLink>();
}
