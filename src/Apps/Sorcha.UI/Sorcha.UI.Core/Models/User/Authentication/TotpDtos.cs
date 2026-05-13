// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models;

/// <summary>
/// Response from TOTP setup containing the secret and provisioning URI.
/// </summary>
public class TotpSetupResponse
{
    /// <summary>
    /// The TOTP secret (base32-encoded).
    /// </summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>
    /// The otpauth:// URI for QR code generation.
    /// </summary>
    public string QrUri { get; set; } = string.Empty;

    /// <summary>
    /// One-time backup codes for account recovery.
    /// </summary>
    public string[] BackupCodes { get; set; } = [];
}

/// <summary>
/// Request to verify a TOTP code.
/// </summary>
public class TotpVerifyRequest
{
    /// <summary>
    /// The 6-digit TOTP code from the authenticator app.
    /// </summary>
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// Response containing TOTP enrollment status.
/// </summary>
public class TotpStatusResponse
{
    /// <summary>
    /// Whether TOTP two-factor authentication is enabled for the user.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// When TOTP was verified and activated, if enabled.
    /// </summary>
    public DateTime? VerifiedAt { get; set; }
}
