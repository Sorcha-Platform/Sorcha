// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// A validator's cryptographic signature on a receipt.
/// </summary>
public record ValidatorSignature
{
    /// <summary>Bech32 wallet address of the signing validator.</summary>
    public required string ValidatorAddress { get; init; }

    /// <summary>Raw signature bytes (Base64-encoded for serialization).</summary>
    public required byte[] SignatureValue { get; init; }

    /// <summary>Signing algorithm (ED25519, NISTP256, RSA4096).</summary>
    public required string Algorithm { get; init; }

    /// <summary>When the signature was produced.</summary>
    public required DateTimeOffset SignedAt { get; init; }
}

/// <summary>
/// Public key information for a validator, used in verification bundles.
/// </summary>
public record ValidatorKeyInfo
{
    /// <summary>Bech32 wallet address of the validator.</summary>
    public required string Address { get; init; }

    /// <summary>Base64-encoded public key.</summary>
    public required string PublicKey { get; init; }

    /// <summary>Key algorithm (ED25519, NISTP256, RSA4096).</summary>
    public required string Algorithm { get; init; }
}

/// <summary>
/// A cryptographically signed attestation that a transaction was sealed in a specific docket.
/// Immutable once created. Contains an embedded Merkle inclusion proof.
/// </summary>
public record TransactionReceipt
{
    /// <summary>Deterministic receipt ID (SHA-256 hash of receipt content).</summary>
    public required string ReceiptId { get; init; }

    /// <summary>Transaction this receipt proves.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Register containing the transaction.</summary>
    public required string RegisterId { get; init; }

    /// <summary>Docket height where the transaction was sealed.</summary>
    public required long DocketNumber { get; init; }

    /// <summary>Merkle root of the docket.</summary>
    public required string MerkleRoot { get; init; }

    /// <summary>Merkle inclusion proof from leaf to root.</summary>
    public required MerkleInclusionProof InclusionProof { get; init; }

    /// <summary>
    /// Validator signature(s) over receipt content.
    /// Array format to accommodate future multi-validator consensus (TRUST-6).
    /// </summary>
    public required IReadOnlyList<ValidatorSignature> Signatures { get; init; }

    /// <summary>When the docket was confirmed/sealed.</summary>
    public required DateTimeOffset SealedAt { get; init; }

    /// <summary>Receipt format version.</summary>
    public int Version { get; init; } = 1;
}
