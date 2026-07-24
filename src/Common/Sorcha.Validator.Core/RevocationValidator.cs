// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Core;

/// <summary>
/// Result of revocation payload validation.
/// </summary>
public record RevocationValidationResult
{
    public required bool IsValid { get; init; }
    public List<string> Errors { get; init; } = [];
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Validates revocation transaction payload structure and business rules.
/// Portable/enclave-safe: no database or network access.
/// Authority checks (original signer, roster membership) are done by the caller.
/// </summary>
public class RevocationValidator
{
    private static readonly HashSet<RevocationReason> ValidReasons =
    [
        RevocationReason.Superseded,
        RevocationReason.Erroneous,
        RevocationReason.Compromised,
        RevocationReason.Expired,
        RevocationReason.Withdrawn,
        RevocationReason.Regulatory
    ];

    /// <summary>
    /// Validates the structure and consistency of a revocation payload.
    /// Does NOT check authority or target existence (those require data access).
    /// </summary>
    public RevocationValidationResult ValidatePayload(RevocationPayload payload)
    {
        var errors = new List<string>();
        string? errorCode = null;

        if (payload == null)
            return new RevocationValidationResult
            {
                IsValid = false,
                Errors = ["Revocation payload is null"],
                ErrorCode = ValidationErrorCodes.RevocationInvalid
            };

        // Validate required fields
        if (string.IsNullOrWhiteSpace(payload.OriginalTxId))
        {
            errors.Add("OriginalTxId is required");
            errorCode = ValidationErrorCodes.RevocationInvalid;
        }

        if (payload.OriginalDocketNumber < 0)
        {
            errors.Add("OriginalDocketNumber must be non-negative");
            errorCode = ValidationErrorCodes.RevocationInvalid;
        }

        // Validate reason is a known enum value
        if (!ValidReasons.Contains(payload.Reason))
        {
            errors.Add($"Invalid revocation reason: {payload.Reason}");
            errorCode = "VAL_REV_006";
        }

        // Validate supersededByTxId consistency
        if (payload.Reason == RevocationReason.Superseded && string.IsNullOrWhiteSpace(payload.SupersededByTxId))
        {
            errors.Add("SupersededByTxId is required when reason is Superseded");
            errorCode = "VAL_REV_007";
        }

        if (payload.Reason != RevocationReason.Superseded && !string.IsNullOrWhiteSpace(payload.SupersededByTxId))
        {
            errors.Add("SupersededByTxId must be null when reason is not Superseded");
            errorCode = "VAL_REV_007";
        }

        // Validate metadata constraints
        if (payload.Metadata != null)
        {
            if (payload.Metadata.Count > 10)
            {
                errors.Add("Metadata cannot have more than 10 entries");
                errorCode = ValidationErrorCodes.RevocationInvalid;
            }

            foreach (var kvp in payload.Metadata)
            {
                if (kvp.Key.Length > 50)
                {
                    errors.Add($"Metadata key '{kvp.Key[..20]}...' exceeds 50 characters");
                    errorCode = ValidationErrorCodes.RevocationInvalid;
                }
                if (kvp.Value.Length > 500)
                {
                    errors.Add($"Metadata value for key '{kvp.Key}' exceeds 500 characters");
                    errorCode = ValidationErrorCodes.RevocationInvalid;
                }
            }
        }

        return new RevocationValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            ErrorCode = errorCode
        };
    }
}
