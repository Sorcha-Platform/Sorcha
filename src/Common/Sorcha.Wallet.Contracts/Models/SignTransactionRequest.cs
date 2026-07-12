// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for signing a transaction.
/// </summary>
public sealed record SignTransactionRequest
{
    /// <summary>Transaction data to sign (base64 encoded).</summary>
    [Required]
    public required string TransactionData { get; init; }

    /// <summary>
    /// Optional key derivation path for signing. Can be a custom path (e.g., "m/44'/0'/0'/0/5") or a Sorcha
    /// system path (e.g., "sorcha:register-attestation"). If not specified, the wallet's default signing key
    /// is used.
    /// </summary>
    public string? DerivationPath { get; init; }

    /// <summary>
    /// When true, TransactionData contains a pre-computed hash (e.g., SHA-256) that should be signed directly
    /// without additional hashing by the wallet. When false (default), the wallet applies SHA-256 to the data
    /// before signing.
    /// </summary>
    public bool IsPreHashed { get; init; }

    /// <summary>
    /// When true, the endpoint signs with both the classical wallet (from URL) and a PQC wallet (from
    /// <see cref="PqcWalletAddress"/>), returning a HybridSignature JSON.
    /// </summary>
    public bool HybridMode { get; init; }

    /// <summary>Address of the PQC wallet to co-sign with. Required when <see cref="HybridMode"/> is true.</summary>
    public string? PqcWalletAddress { get; init; }
}
