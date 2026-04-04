// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.ServiceClients.Wallet.Models;

/// <summary>
/// Request model for creating a new wallet.
/// Consolidated DTO shared by Wallet Service and UI.
/// </summary>
public class CreateWalletRequest
{
    /// <summary>
    /// Friendly name for the wallet
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; set; }

    /// <summary>
    /// Cryptographic algorithm (ED25519, NISTP256, RSA4096)
    /// </summary>
    [Required]
    public required string Algorithm { get; set; }

    /// <summary>
    /// Number of words in mnemonic (12, 15, 18, 21, or 24)
    /// </summary>
    [Bip39WordCount]
    public int WordCount { get; set; } = 12;

    /// <summary>
    /// Optional passphrase for additional security
    /// </summary>
    public string? Passphrase { get; set; }

    /// <summary>
    /// Optional PQC algorithm for hybrid wallets (e.g., ML-DSA-65, SLH-DSA-128s)
    /// </summary>
    public string? PqcAlgorithm { get; set; }

    /// <summary>
    /// Enable hybrid mode: creates both classical and PQC key pairs for the wallet
    /// </summary>
    public bool EnableHybrid { get; set; }

    /// <summary>
    /// Optional signing mode override. When provided and AllowSigningModeOverride is true,
    /// overrides the policy-determined signing mode.
    /// Values: "Local" (default — keys stored encrypted locally) or "KmsResident" (keys held in cloud KMS).
    /// </summary>
    public string? SigningMode { get; set; }

    /// <summary>
    /// Optional metadata tags
    /// </summary>
    public Dictionary<string, string>? Tags { get; set; }
}
