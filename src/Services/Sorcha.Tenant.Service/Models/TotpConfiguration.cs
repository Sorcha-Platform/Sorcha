// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// TOTP two-factor authentication configuration for a user.
/// Stored in per-organization schema (org_{organization_id}).
/// One-to-one relationship with UserIdentity.
/// </summary>
public class TotpConfiguration
{
    /// <summary>
    /// Unique configuration record identifier.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// User who owns this TOTP configuration. One-to-one with UserIdentity.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The TOTP shared secret (Base32 form) protected at rest with AES-256-GCM via
    /// <c>ISecretProtectionProvider</c>. Stored as the ciphertext envelope (nonce ∥ ciphertext ∥ tag).
    /// Used with OtpNet to generate/validate time-based codes after decryption.
    /// </summary>
    public byte[] EncryptedSecret { get; set; } = [];

    /// <summary>
    /// Identifier of the key that protected <see cref="EncryptedSecret"/> (e.g. "jwt-derived-v1").
    /// </summary>
    public string EncryptionKeyId { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of SHA-256 hashed backup codes.
    /// Each code is consumed on use (replaced with empty string in array).
    /// </summary>
    public string BackupCodes { get; set; } = "[]";

    /// <summary>
    /// Whether TOTP is fully enabled (user has verified initial code).
    /// When false, setup is pending — user has received secret but not yet verified.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Timestamp when the user successfully verified their first TOTP code,
    /// completing the enrollment. Null if setup is pending.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Record creation timestamp (UTC).
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last modification timestamp (UTC).
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
