// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using Sorcha.Wallet.Contracts.Validation;

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for creating a new wallet. This is the single canonical shape shared by the
/// Wallet Service, UI, PWA, CLI, and service clients.
/// </summary>
public sealed record CreateWalletRequest
{
    /// <summary>Friendly name for the wallet.</summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Cryptographic algorithm (ED25519, NISTP256, RSA4096).</summary>
    [Required]
    public required string Algorithm { get; init; }

    /// <summary>Number of words in mnemonic (12, 15, 18, 21, or 24).</summary>
    [Bip39WordCount]
    public int WordCount { get; init; } = 12;

    /// <summary>Optional passphrase for additional security.</summary>
    public string? Passphrase { get; init; }

    /// <summary>Optional PQC algorithm for hybrid wallets (e.g., ML-DSA-65, SLH-DSA-128s).</summary>
    public string? PqcAlgorithm { get; init; }

    /// <summary>Enable hybrid mode: creates both classical and PQC key pairs for the wallet.</summary>
    public bool EnableHybrid { get; init; }

    /// <summary>
    /// Optional signing mode override. When provided and AllowSigningModeOverride is true, overrides the
    /// policy-determined signing mode. Values: "Local" (default — keys stored encrypted locally) or
    /// "KmsResident" (keys held in cloud KMS).
    /// </summary>
    public string? SigningMode { get; init; }

    /// <summary>Optional metadata tags.</summary>
    public Dictionary<string, string>? Tags { get; init; }
}
