// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for the result of transaction signing.
/// </summary>
public sealed record SignTransactionResponse
{
    /// <summary>Base64-encoded digital signature.</summary>
    public required string Signature { get; init; }

    /// <summary>Address of the wallet that signed the transaction.</summary>
    public required string SignedBy { get; init; }

    /// <summary>Timestamp when the transaction was signed.</summary>
    public DateTime SignedAt { get; init; }

    /// <summary>Base64-encoded public key used for signing (derived key if derivation path was specified).</summary>
    public string? PublicKey { get; init; }
}
