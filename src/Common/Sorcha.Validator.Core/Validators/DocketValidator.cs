// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Enums;
using Sorcha.Validator.Core.Models;

namespace Sorcha.Validator.Core.Validators;

/// <summary>
/// Pure validator for docket structure, hash integrity, and chain continuity
/// This validator is stateless and can run in secure enclaves (Intel SGX, AMD SEV)
/// </summary>
public class DocketValidator : IDocketValidator
{
    private readonly IHashProvider _hashProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocketValidator"/> class.
    /// </summary>
    /// <param name="hashProvider">Hash provider for docket integrity verification.</param>
    public DocketValidator(IHashProvider hashProvider)
    {
        _hashProvider = hashProvider ?? throw new ArgumentNullException(nameof(hashProvider));
    }

    /// <inheritdoc/>
    public ValidationResult ValidateDocketStructure(DocketData docket)
    {
        var errors = new List<ValidationError>();

        // Validate DocketId
        if (string.IsNullOrWhiteSpace(docket.DocketId))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_001",
                Message = "Docket ID is required",
                Field = nameof(docket.DocketId)
            });
        }

        // Validate RegisterId
        if (string.IsNullOrWhiteSpace(docket.RegisterId))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_002",
                Message = "Register ID is required",
                Field = nameof(docket.RegisterId)
            });
        }

        // Validate DocketNumber
        if (docket.DocketNumber < 0)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_003",
                Message = "Docket number cannot be negative",
                Field = nameof(docket.DocketNumber)
            });
        }

        // Validate DocketHash
        if (string.IsNullOrWhiteSpace(docket.DocketHash))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_004",
                Message = "Docket hash is required",
                Field = nameof(docket.DocketHash)
            });
        }

        // Validate MerkleRoot
        if (string.IsNullOrWhiteSpace(docket.MerkleRoot))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_005",
                Message = "Merkle root is required",
                Field = nameof(docket.MerkleRoot)
            });
        }

        // Validate ProposerValidatorId
        if (string.IsNullOrWhiteSpace(docket.ProposerValidatorId))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_006",
                Message = "Proposer validator ID is required",
                Field = nameof(docket.ProposerValidatorId)
            });
        }

        // Validate CreatedAt is not in the future (allow 5 minute clock skew)
        if (docket.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_007",
                Message = "Docket creation timestamp cannot be in the future",
                Field = nameof(docket.CreatedAt)
            });
        }

        // Validate TransactionCount is not negative
        if (docket.TransactionCount < 0)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_008",
                Message = "Transaction count cannot be negative",
                Field = nameof(docket.TransactionCount)
            });
        }

        // Validate PreviousHash consistency with DocketNumber
        if (docket.DocketNumber == 0 && docket.PreviousHash != null)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_009",
                Message = "Genesis docket (number 0) cannot have a previous hash",
                Field = nameof(docket.PreviousHash)
            });
        }

        if (docket.DocketNumber > 0 && string.IsNullOrWhiteSpace(docket.PreviousHash))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_010",
                Message = "Non-genesis docket must have a previous hash",
                Field = nameof(docket.PreviousHash)
            });
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public ValidationResult ValidateDocketHash(DocketData docket, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return ValidationResult.Failure("DK_011", "Expected hash is required", nameof(expectedHash));
        }

        try
        {
            // Compute the hash using the same algorithm as the proposer
            var computedHash = ComputeDocketHash(
                docket.RegisterId,
                docket.DocketNumber,
                docket.PreviousHash,
                docket.MerkleRoot,
                docket.CreatedAt);

            // Compare hashes (case insensitive)
            if (!string.Equals(computedHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return ValidationResult.Failure(
                    "DK_012",
                    $"Docket hash mismatch. Expected: {expectedHash}, Computed: {computedHash}",
                    nameof(expectedHash));
            }

            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            return ValidationResult.Failure(
                "DK_013",
                $"Failed to validate docket hash: {ex.Message}",
                nameof(docket.DocketHash));
        }
    }

    /// <inheritdoc/>
    public ValidationResult ValidateChainContinuity(DocketData currentDocket, DocketData previousDocket)
    {
        var errors = new List<ValidationError>();

        // Validate docket number sequence
        if (currentDocket.DocketNumber != previousDocket.DocketNumber + 1)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_014",
                Message = $"Docket number must be sequential. Expected: {previousDocket.DocketNumber + 1}, Actual: {currentDocket.DocketNumber}",
                Field = nameof(currentDocket.DocketNumber)
            });
        }

        // Validate register continuity
        if (!string.Equals(currentDocket.RegisterId, previousDocket.RegisterId, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_015",
                Message = "Current docket must reference the same register as previous docket",
                Field = nameof(currentDocket.RegisterId)
            });
        }

        // Validate hash chain linkage
        if (!string.Equals(currentDocket.PreviousHash, previousDocket.DocketHash, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_016",
                Message = $"Previous hash mismatch. Expected: {previousDocket.DocketHash}, Actual: {currentDocket.PreviousHash}",
                Field = nameof(currentDocket.PreviousHash)
            });
        }

        // Validate timestamp ordering (current must be after or equal to previous)
        if (currentDocket.CreatedAt < previousDocket.CreatedAt)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_017",
                Message = "Current docket timestamp cannot be earlier than previous docket",
                Field = nameof(currentDocket.CreatedAt)
            });
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public ValidationResult ValidateGenesisDocket(DocketData docket)
    {
        var errors = new List<ValidationError>();

        // Genesis must have docket number 0
        if (docket.DocketNumber != 0)
        {
            errors.Add(new ValidationError
            {
                Code = "DK_018",
                Message = $"Genesis docket must have docket number 0, got {docket.DocketNumber}",
                Field = nameof(docket.DocketNumber)
            });
        }

        // Genesis must not have a previous hash
        if (!string.IsNullOrEmpty(docket.PreviousHash))
        {
            errors.Add(new ValidationError
            {
                Code = "DK_019",
                Message = "Genesis docket cannot have a previous hash",
                Field = nameof(docket.PreviousHash)
            });
        }

        // Validate basic structure
        var structureResult = ValidateDocketStructure(docket);
        if (!structureResult.IsValid)
        {
            // Add structure errors with genesis context
            foreach (var error in structureResult.Errors)
            {
                errors.Add(new ValidationError
                {
                    Code = error.Code,
                    Message = $"[Genesis] {error.Message}",
                    Field = error.Field
                });
            }
        }

        if (errors.Count > 0)
        {
            return ValidationResult.Failure(errors.ToArray());
        }

        return ValidationResult.Success();
    }

    /// <inheritdoc/>
    public string ComputeDocketHash(
        string registerId,
        long docketNumber,
        string? previousHash,
        string merkleRoot,
        DateTimeOffset createdAt)
    {
        // Deterministic hash computation
        // Format: RegisterId|DocketNumber|PreviousHash|MerkleRoot|Timestamp
        var hashInput = new StringBuilder();
        hashInput.Append(registerId);
        hashInput.Append('|');
        hashInput.Append(docketNumber);
        hashInput.Append('|');
        hashInput.Append(previousHash ?? "GENESIS");
        hashInput.Append('|');
        hashInput.Append(merkleRoot);
        hashInput.Append('|');
        hashInput.Append(createdAt.ToUnixTimeSeconds());

        var inputBytes = Encoding.UTF8.GetBytes(hashInput.ToString());
        var hashBytes = _hashProvider.ComputeHash(inputBytes, HashType.SHA256);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
