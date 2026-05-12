// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Request model for signing a transaction
/// </summary>
public class SignTransactionRequest
{
    /// <summary>
    /// Transaction data to sign (base64 encoded)
    /// </summary>
    [Required]
    public required string TransactionData { get; set; }

    /// <summary>
    /// Optional key derivation path for signing
    /// </summary>
    /// <remarks>
    /// Can be a custom path (e.g., "m/44'/0'/0'/0/5") or a Sorcha system path (e.g., "sorcha:register-attestation").
    /// If not specified, the wallet's default signing key is used.
    /// </remarks>
    public string? DerivationPath { get; set; }

    /// <summary>
    /// When true, TransactionData contains a pre-computed hash (e.g., SHA-256) that should
    /// be signed directly without additional hashing by the wallet.
    /// When false (default), the wallet applies SHA-256 to the data before signing.
    /// </summary>
    public bool IsPreHashed { get; set; }
}
