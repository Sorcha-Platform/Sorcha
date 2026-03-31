// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Validator.Core;

/// <summary>
/// Per-check results for bundle verification.
/// </summary>
public record BundleVerificationChecks
{
    public bool CredentialSignatureValid { get; init; }
    public bool InclusionProofValid { get; init; }
    public bool ReceiptSignatureValid { get; init; }
    public bool RevocationStatusCurrent { get; init; }
}

/// <summary>
/// Result of offline verification bundle validation.
/// </summary>
public record BundleVerificationResult
{
    public required bool IsValid { get; init; }
    public required BundleVerificationChecks Checks { get; init; }
    public List<string> Warnings { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

/// <summary>
/// Orchestrates all four verification checks for a portable verification bundle.
/// Portable/enclave-safe: no network or database access.
/// </summary>
public class BundleVerifier
{
    private readonly ReceiptValidator _receiptValidator;
    private readonly InclusionProofValidator _inclusionProofValidator;

    public BundleVerifier(
        ReceiptValidator receiptValidator,
        InclusionProofValidator inclusionProofValidator)
    {
        _receiptValidator = receiptValidator ?? throw new ArgumentNullException(nameof(receiptValidator));
        _inclusionProofValidator = inclusionProofValidator ?? throw new ArgumentNullException(nameof(inclusionProofValidator));
    }

    /// <summary>
    /// Verifies all four components of a verification bundle:
    /// 1. Credential signature (delegated to caller — we check structural validity)
    /// 2. Inclusion proof (Merkle root recomputation)
    /// 3. Receipt signature (validator signature verification)
    /// 4. Revocation status (point-in-time check)
    /// </summary>
    /// <param name="bundle">The verification bundle to check.</param>
    /// <param name="validatorPublicKeys">Public keys for validator signature verification.</param>
    /// <returns>Verification result with per-check status and warnings.</returns>
    public BundleVerificationResult VerifyBundle(
        VerificationBundle bundle,
        IReadOnlyList<ValidatorKeyInfo> validatorPublicKeys)
    {
        var warnings = new List<string>();
        var errors = new List<string>();

        // Check 1: Credential structural validity (signature verification requires issuer key, not validator key)
        bool credentialValid = bundle.Credential.ValueKind != System.Text.Json.JsonValueKind.Undefined
                               && bundle.Credential.ValueKind != System.Text.Json.JsonValueKind.Null;
        if (!credentialValid)
            errors.Add("Credential data is missing or null");

        // Check 2: Inclusion proof verification
        var proofResult = _inclusionProofValidator.Verify(bundle.Receipt.InclusionProof);
        if (!proofResult.IsValid)
            errors.Add(proofResult.Error ?? "Inclusion proof verification failed");

        // Check 3: Receipt signature verification
        bool receiptValid = false;
        if (validatorPublicKeys.Count > 0 && bundle.Receipt.Signatures.Count > 0)
        {
            var validatorSig = bundle.Receipt.Signatures[0];
            var matchingKey = validatorPublicKeys.FirstOrDefault(k =>
                string.Equals(k.Address, validatorSig.ValidatorAddress, StringComparison.OrdinalIgnoreCase));

            if (matchingKey != null)
            {
                var receiptResult = _receiptValidator.Verify(bundle.Receipt, matchingKey.PublicKey);
                receiptValid = receiptResult.SignatureValid;
                if (!receiptValid)
                    errors.AddRange(receiptResult.Errors);
            }
            else
            {
                errors.Add($"No matching public key found for validator {validatorSig.ValidatorAddress}");
            }
        }
        else
        {
            errors.Add("No validator public keys or receipt signatures available for verification");
        }

        // Check 4: Revocation status
        bool revocationCurrent = bundle.RevocationStatus.Status == TransactionLifecycleStatus.Active;
        if (!revocationCurrent)
        {
            warnings.Add($"Transaction was {bundle.RevocationStatus.Status} at export time ({bundle.ExportedAt:O}). Online status check recommended for current status.");
        }

        // Add staleness warning if bundle is old
        var bundleAge = DateTimeOffset.UtcNow - bundle.ExportedAt;
        if (bundleAge.TotalHours > 24)
        {
            warnings.Add($"Verification bundle was exported {bundleAge.TotalDays:F0} days ago. Revocation status may have changed.");
        }

        bool allValid = credentialValid && proofResult.IsValid && receiptValid;

        return new BundleVerificationResult
        {
            IsValid = allValid,
            Checks = new BundleVerificationChecks
            {
                CredentialSignatureValid = credentialValid,
                InclusionProofValid = proofResult.IsValid,
                ReceiptSignatureValid = receiptValid,
                RevocationStatusCurrent = revocationCurrent
            },
            Warnings = warnings,
            Errors = errors
        };
    }
}
