// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Models.Auth;

/// <summary>
/// Response from TOTP setup, carrying the shared secret and the one-time backup codes.
/// </summary>
/// <remarks>
/// <see cref="BackupCodes"/> is shown exactly once. The Tenant Service does not retain a
/// retrievable copy, so a client that discards this response cannot ask for it again.
/// </remarks>
/// <param name="Secret">Base32-encoded shared secret, for manual entry.</param>
/// <param name="QrUri">The <c>otpauth://</c> URI for QR-code scanning.</param>
/// <param name="BackupCodes">One-time backup codes for account recovery.</param>
public sealed record TotpSetupResponse(
    string Secret,
    string QrUri,
    IReadOnlyList<string> BackupCodes);

/// <summary>
/// Submit a code from the authenticator app (setup verification, or validation at sign-in).
/// </summary>
/// <param name="Code">The 6-digit time-based code.</param>
public sealed record TotpCodeRequest(string Code);

/// <summary>
/// TOTP enrolment status for the signed-in user.
/// </summary>
/// <param name="IsEnabled">Whether TOTP two-factor authentication is enabled.</param>
/// <param name="VerifiedAt">When TOTP was verified and activated; null when not enabled.</param>
/// <param name="Message">Optional operator-facing status message.</param>
public sealed record TotpStatusResponse(
    bool IsEnabled,
    DateTime? VerifiedAt,
    string? Message = null);
