// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Core;

/// <summary>
/// Delegate for verifying a cryptographic signature.
/// </summary>
/// <param name="publicKey">Public key bytes.</param>
/// <param name="data">Data that was signed.</param>
/// <param name="signature">Signature bytes.</param>
/// <param name="algorithm">Algorithm name (ED25519, NISTP256, RSA4096).</param>
/// <returns>True if the signature is valid.</returns>
public delegate bool SignatureVerifyFunc(byte[] publicKey, byte[] data, byte[] signature, string algorithm);

/// <summary>
/// Result of receipt validation.
/// </summary>
public record ReceiptValidationResult
{
    public required bool IsValid { get; init; }
    public required bool SignatureValid { get; init; }
    public required bool InclusionProofValid { get; init; }
    public required bool MerkleRootConsistent { get; init; }
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Validates transaction receipts — verifies validator signatures and embedded inclusion proofs.
/// Portable/enclave-safe: no network or database access.
/// </summary>
public class ReceiptValidator
{
    private readonly InclusionProofValidator _proofValidator;
    private readonly SignatureVerifyFunc? _verifySignature;

    /// <summary>
    /// Creates a ReceiptValidator with full signature verification capability.
    /// </summary>
    public ReceiptValidator(InclusionProofValidator proofValidator, SignatureVerifyFunc verifySignature)
    {
        _proofValidator = proofValidator ?? throw new ArgumentNullException(nameof(proofValidator));
        _verifySignature = verifySignature ?? throw new ArgumentNullException(nameof(verifySignature));
    }

    /// <summary>
    /// Creates a ReceiptValidator that only checks inclusion proofs (no signature verification).
    /// </summary>
    public ReceiptValidator(InclusionProofValidator proofValidator)
    {
        _proofValidator = proofValidator ?? throw new ArgumentNullException(nameof(proofValidator));
    }

    /// <summary>
    /// Verifies a receipt's validator signature and embedded inclusion proof.
    /// </summary>
    public ReceiptValidationResult Verify(TransactionReceipt receipt, string validatorPublicKey)
    {
        var errors = new List<string>();
        bool signatureValid = false;
        bool inclusionProofValid = false;
        bool merkleRootConsistent = false;

        // Check 1: Verify at least one validator signature
        if (receipt.Signatures.Count == 0)
        {
            errors.Add("Receipt has no validator signatures");
        }
        else if (_verifySignature == null)
        {
            errors.Add("No signature verification function provided");
        }
        else
        {
            var sig = receipt.Signatures[0];
            try
            {
                var receiptData = BuildReceiptSigningData(receipt);
                signatureValid = _verifySignature(
                    Convert.FromBase64String(validatorPublicKey),
                    receiptData,
                    sig.SignatureValue,
                    sig.Algorithm);
            }
            catch (FormatException ex)
            {
                errors.Add($"Signature verification failed: {ex.Message}");
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                errors.Add($"Signature verification failed: {ex.Message}");
            }
        }

        // Check 2: Verify inclusion proof using positional verification
        var proofResult = _proofValidator.Verify(receipt.InclusionProof);
        inclusionProofValid = proofResult.IsValid;
        if (!inclusionProofValid)
            errors.Add(proofResult.Error ?? "Inclusion proof verification failed");

        // Check 3: Verify receipt's Merkle root matches the embedded proof's Merkle root
        merkleRootConsistent = string.Equals(
            receipt.MerkleRoot,
            receipt.InclusionProof.MerkleRoot,
            StringComparison.OrdinalIgnoreCase);

        if (!merkleRootConsistent)
            errors.Add("Receipt Merkle root does not match inclusion proof Merkle root");

        return new ReceiptValidationResult
        {
            IsValid = signatureValid && inclusionProofValid && merkleRootConsistent,
            SignatureValid = signatureValid,
            InclusionProofValid = inclusionProofValid,
            MerkleRootConsistent = merkleRootConsistent,
            Errors = errors
        };
    }

    /// <summary>
    /// Builds the canonical byte representation of receipt data for signature verification.
    /// This format must match what the Validator signs when generating receipts.
    /// </summary>
    public static byte[] BuildReceiptSigningData(TransactionReceipt receipt)
    {
        // Canonical format: txId|docketNumber|merkleRoot|sealedAt(unix ms)
        var canonical = $"{receipt.TransactionId}|{receipt.DocketNumber}|{receipt.MerkleRoot}|{receipt.SealedAt.ToUnixTimeMilliseconds()}";
        return System.Text.Encoding.UTF8.GetBytes(canonical);
    }
}
