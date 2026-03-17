// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request to initiate organization-managed wallet recovery.
/// </summary>
public class RecoverOrgRequest
{
    /// <summary>
    /// Target user ID whose wallets should be recovered.
    /// </summary>
    [Required]
    public required string UserId { get; set; }

    /// <summary>
    /// Proof of org recovery private key possession (Base64 signature).
    /// </summary>
    [Required]
    public required string OrgRecoveryKeySignature { get; set; }

    /// <summary>
    /// If true, skip delegation revocation (admin opt-out).
    /// </summary>
    public bool SkipDelegationRevocation { get; set; }
}
