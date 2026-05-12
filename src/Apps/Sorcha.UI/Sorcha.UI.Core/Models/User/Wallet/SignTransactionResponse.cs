// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Response model for transaction signing
/// </summary>
public class SignTransactionResponse
{
    /// <summary>
    /// Base64-encoded digital signature
    /// </summary>
    public required string Signature { get; set; }

    /// <summary>
    /// Address of the wallet that signed the transaction
    /// </summary>
    public required string SignedBy { get; set; }

    /// <summary>
    /// Timestamp when the transaction was signed
    /// </summary>
    public DateTime SignedAt { get; set; }

    /// <summary>
    /// Base64-encoded public key used for signing (derived key if derivation path was specified)
    /// </summary>
    public string? PublicKey { get; set; }
}
