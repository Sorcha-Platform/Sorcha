// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Request model for recovering a wallet from mnemonic
/// </summary>
public class RecoverWalletRequest
{
    /// <summary>
    /// BIP39 mnemonic phrase words
    /// </summary>
    [Required]
    [MinLength(12)]
    [MaxLength(24)]
    public required string[] MnemonicWords { get; set; }

    /// <summary>
    /// Friendly name for the recovered wallet
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    /// <summary>
    /// Cryptographic algorithm (must match original wallet)
    /// </summary>
    [Required]
    public required string Algorithm { get; set; }

    /// <summary>
    /// Optional passphrase (must match original if used)
    /// </summary>
    public string? Passphrase { get; set; }
}
