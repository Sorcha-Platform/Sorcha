// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request to initiate passkey-bound wallet recovery.
/// </summary>
public class RecoverPasskeyRequest
{
    /// <summary>
    /// FIDO2 credential ID used for authentication (Base64).
    /// </summary>
    [Required]
    public required string PasskeyCredentialId { get; set; }

    /// <summary>
    /// Signed WebAuthn assertion proving passkey ownership (Base64).
    /// </summary>
    [Required]
    public required string ChallengeResponse { get; set; }
}
