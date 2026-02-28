// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using System.Text.Json;
using Sorcha.Validator.Core.Models;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;

namespace Sorcha.Validator.Core.Validators;

/// <summary>
/// Validates transactions against blueprint rules and cryptographic requirements
/// </summary>
public class TransactionValidator : ITransactionValidator
{
    private readonly IHashProvider _hashProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransactionValidator"/> class.
    /// </summary>
    /// <param name="hashProvider">Hash provider for payload verification.</param>
    public TransactionValidator(IHashProvider hashProvider)
    {
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <summary>
    /// Validates a transaction's structure, signatures, and basic integrity
    /// </summary>
    public ValidationResult ValidateTransactionStructure(
        string transactionId,
        string registerId,
        string? blueprintId,
        JsonElement payload,
        string payloadHash,
        List<TransactionSignature> signatures,
        DateTimeOffset createdAt)
    {
        var errors = new List<ValidationError>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            errors.Add(new ValidationError
            {
                Code = "TX_001",
                Message = "Transaction ID is required",
                Field = nameof(transactionId)
            });
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            errors.Add(new ValidationError
            {
                Code = "TX_002",
                Message = "Register ID is required",
                Field = nameof(registerId)
            });
        }

        if (string.IsNullOrWhiteSpace(blueprintId))
        {
            errors.Add(new ValidationError
            {
                Code = "TX_003",
                Message = "Blueprint ID is required",
                Field = nameof(blueprintId)
            });
        }

        // Validate payload
        if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null)
        {
            errors.Add(new ValidationError
            {
                Code = "TX_004",
                Message = "Payload is required",
                Field = nameof(payload)
            });
        }

        // Validate payload hash
        if (string.IsNullOrWhiteSpace(payloadHash))
        {
            errors.Add(new ValidationError
            {
                Code = "TX_005",
                Message = "Payload hash is required",
                Field = nameof(payloadHash)
            });
        }

        // Validate signatures
        if (signatures == null || signatures.Count == 0)
        {
            errors.Add(new ValidationError
            {
                Code = "TX_006",
                Message = "At least one signature is required",
                Field = nameof(signatures)
            });
        }
        else
        {
            for (int i = 0; i < signatures.Count; i++)
            {
                var sig = signatures[i];
                if (string.IsNullOrWhiteSpace(sig.PublicKey))
                {
                    errors.Add(new ValidationError
                    {
                        Code = "TX_007",
                        Message = $"Signature {i} is missing public key",
                        Field = $"{nameof(signatures)}[{i}].PublicKey"
                    });
                }

                if (string.IsNullOrWhiteSpace(sig.SignatureValue))
                {
                    errors.Add(new ValidationError
                    {
                        Code = "TX_008",
                        Message = $"Signature {i} is missing signature value",
                        Field = $"{nameof(signatures)}[{i}].SignatureValue"
                    });
                }

                if (string.IsNullOrWhiteSpace(sig.Algorithm))
                {
                    errors.Add(new ValidationError
                    {
                        Code = "TX_009",
                        Message = $"Signature {i} is missing algorithm",
                        Field = $"{nameof(signatures)}[{i}].Algorithm"
                    });
                }
            }
        }

        // Validate timestamp is not in the future
        if (createdAt > DateTimeOffset.UtcNow.AddMinutes(5)) // Allow 5 minute clock skew
        {
            errors.Add(new ValidationError
            {
                Code = "TX_010",
                Message = "Transaction timestamp cannot be in the future",
                Field = nameof(createdAt)
            });
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <summary>
    /// Canonical JSON serializer options for deterministic payload hashing.
    /// MUST match the options used by Blueprint Service (TransactionBuilderServiceExtensions)
    /// and all serialization boundaries (ValidatorServiceClient, TransactionPoolPoller).
    /// Contract: compact, no property renaming, UnsafeRelaxedJsonEscaping (no \u002B for +).
    /// </summary>
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Validates that the payload hash matches the actual payload
    /// </summary>
    public ValidationResult ValidatePayloadHash(JsonElement payload, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return ValidationResult.Failure("TX_011", "Expected hash is required", nameof(expectedHash));
        }

        try
        {
            // Re-canonicalize the payload through deterministic serializer options.
            // This ensures hash verification is independent of how the JSON arrived
            // (HTTP encoding, Redis round-trip, etc.) — only the logical data matters.
            var payloadJson = JsonSerializer.Serialize(payload, CanonicalJsonOptions);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Compute hash
            var computedHashBytes = _hashProvider.ComputeHash(payloadBytes, HashType.SHA256);
            var computedHash = Convert.ToHexString(computedHashBytes).ToLowerInvariant();

            // Compare hashes
            if (!string.Equals(computedHash, expectedHash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Failure(
                    "TX_012",
                    $"Payload hash mismatch. Expected: {expectedHash}, Computed: {computedHash}",
                    nameof(expectedHash));
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(
                "TX_013",
                $"Failed to validate payload hash: {ex.Message}",
                nameof(payload));
        }
    }

    /// <summary>
    /// Validates transaction signatures (structure only - cryptographic verification done by Wallet Service)
    /// </summary>
    public ValidationResult ValidateSignatures(List<TransactionSignature> signatures, string transactionId)
    {
        if (signatures == null || signatures.Count == 0)
        {
            return ValidationResult.Failure("TX_014", "At least one signature is required", nameof(signatures));
        }

        var errors = new List<ValidationError>();

        foreach (var sig in signatures)
        {
            // Validate signature structure
            if (string.IsNullOrWhiteSpace(sig.PublicKey))
            {
                errors.Add(new ValidationError
                {
                    Code = "TX_015",
                    Message = "Signature missing public key",
                    Field = nameof(sig.PublicKey)
                });
            }

            if (string.IsNullOrWhiteSpace(sig.SignatureValue))
            {
                errors.Add(new ValidationError
                {
                    Code = "TX_016",
                    Message = "Signature missing signature value",
                    Field = nameof(sig.SignatureValue)
                });
            }

            // Validate algorithm
            var validAlgorithms = new[] { "ED25519", "NIST-P256", "RSA-4096", "ML-DSA-65", "SLH-DSA-128s", "SLH-DSA-192s" };
            if (!validAlgorithms.Contains(sig.Algorithm, StringComparer.OrdinalIgnoreCase))
            {
                errors.Add(new ValidationError
                {
                    Code = "TX_017",
                    Message = $"Unsupported signature algorithm: {sig.Algorithm}. Must be one of: {string.Join(", ", validAlgorithms)}",
                    Field = nameof(sig.Algorithm)
                });
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }
}
