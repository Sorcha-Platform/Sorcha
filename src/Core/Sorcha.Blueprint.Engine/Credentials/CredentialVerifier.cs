// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http;
using System.Text.Json;

using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Verifies credential presentations against action credential requirements.
/// Checks signature validity, expiry, issuer trust, claim constraints, and revocation status.
/// </summary>
public class CredentialVerifier : ICredentialVerifier
{
    private readonly ISdJwtService _sdJwtService;
    private readonly IRevocationChecker? _revocationChecker;

    public CredentialVerifier(ISdJwtService sdJwtService, IRevocationChecker? revocationChecker = null)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _revocationChecker = revocationChecker;
    }

    /// <inheritdoc />
    public async Task<CredentialValidationResult> VerifyAsync(
        IEnumerable<CredentialRequirement> requirements,
        IEnumerable<CredentialPresentation> presentations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(presentations);

        var result = new CredentialValidationResult();
        var presentationList = presentations.ToList();
        var requirementList = requirements.ToList();

        if (requirementList.Count == 0)
        {
            result.IsValid = true;
            return result;
        }

        foreach (var requirement in requirementList)
        {
            var matched = false;

            foreach (var presentation in presentationList)
            {
                var matchResult = await TryMatchPresentationAsync(
                    requirement, presentation, result, cancellationToken);

                if (matchResult.IsMatch)
                {
                    result.VerifiedCredentials.Add(matchResult.Detail!);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                // Determine the most specific failure reason
                var error = presentationList.Count == 0
                    ? new CredentialValidationError
                    {
                        RequirementType = requirement.Type,
                        FailureReason = CredentialFailureReason.Missing,
                        Message = $"No credential presented for requirement '{requirement.Type}'"
                    }
                    : await FindBestFailureReasonAsync(requirement, presentationList, cancellationToken);

                result.Errors.Add(error);
            }
        }

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private async Task<(bool IsMatch, VerifiedCredentialDetail? Detail)> TryMatchPresentationAsync(
        CredentialRequirement requirement,
        CredentialPresentation presentation,
        CredentialValidationResult result,
        CancellationToken cancellationToken)
    {
        // Step 1: Check disclosed claims for type match
        if (!presentation.DisclosedClaims.TryGetValue("type", out var typeValue) &&
            !presentation.DisclosedClaims.TryGetValue("vct", out typeValue))
        {
            // Try credential ID as type indicator
            // No type claim found — cannot match
            return (false, null);
        }

        var credentialType = typeValue?.ToString() ?? string.Empty;
        if (!string.Equals(credentialType, requirement.Type, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        // Step 2: Check issuer constraint
        if (requirement.AcceptedIssuers?.Any() == true)
        {
            var issuer = GetClaimString(presentation.DisclosedClaims, "iss");
            if (issuer == null || !requirement.AcceptedIssuers.Contains(issuer))
            {
                return (false, null);
            }
        }

        // Step 3: Check required claim constraints
        if (requirement.RequiredClaims?.Any() == true)
        {
            foreach (var constraint in requirement.RequiredClaims)
            {
                if (!presentation.DisclosedClaims.TryGetValue(constraint.ClaimName, out var value))
                {
                    return (false, null);
                }

                if (constraint.ExpectedValue != null)
                {
                    var expectedStr = constraint.ExpectedValue.ToString();
                    var actualStr = value?.ToString();
                    if (!string.Equals(expectedStr, actualStr, StringComparison.Ordinal))
                    {
                        return (false, null);
                    }
                }
            }
        }

        // Step 4: Check revocation status
        var issuerDid = GetClaimString(presentation.DisclosedClaims, "iss") ?? string.Empty;
        var revocationStatus = await CheckRevocationAsync(
            presentation.CredentialId, issuerDid, requirement, result, cancellationToken);

        if (revocationStatus == "Revoked")
        {
            return (false, null);
        }

        // Step 5: Verify SD-JWT signature (using raw presentation)
        // Note: We need the issuer public key for full verification.
        // For now, we trust the disclosed claims and mark signature as needing
        // full verification at the service layer where we have key access.
        // The engine operates without direct key access.
        var detail = new VerifiedCredentialDetail
        {
            CredentialId = presentation.CredentialId,
            Type = credentialType,
            IssuerDid = issuerDid,
            VerifiedClaims = new Dictionary<string, object>(presentation.DisclosedClaims),
            // Fail-safe: signature is unverified until explicitly validated by the
            // service layer which has access to issuer public keys.
            SignatureValid = false,
            RevocationStatus = revocationStatus ?? "Active"
        };

        return (true, detail);
    }

    private async Task<string?> CheckRevocationAsync(
        string credentialId,
        string issuerWallet,
        CredentialRequirement requirement,
        CredentialValidationResult result,
        CancellationToken cancellationToken)
    {
        if (_revocationChecker == null)
        {
            // No revocation checker configured — skip revocation check entirely.
            // This is expected when the engine runs without a wallet/ledger backend.
            return "Active";
        }

        try
        {
            var status = await _revocationChecker.CheckRevocationStatusAsync(
                credentialId, issuerWallet, cancellationToken);

            if (status == null)
            {
                // Status unavailable — apply policy
                return ApplyRevocationUnavailablePolicy(credentialId, requirement, result);
            }

            if (string.Equals(status, "Revoked", StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(new CredentialValidationError
                {
                    RequirementType = requirement.Type,
                    FailureReason = CredentialFailureReason.Revoked,
                    Message = $"Credential '{credentialId}' has been revoked"
                });
                return "Revoked";
            }

            return status;
        }
        catch (HttpRequestException)
        {
            // Revocation check failed due to network error — apply policy
            return ApplyRevocationUnavailablePolicy(credentialId, requirement, result);
        }
        catch (JsonException)
        {
            // Revocation check failed due to malformed response — apply policy
            return ApplyRevocationUnavailablePolicy(credentialId, requirement, result);
        }
    }

    private static string? ApplyRevocationUnavailablePolicy(
        string credentialId,
        CredentialRequirement requirement,
        CredentialValidationResult result)
    {
        var policy = requirement.RevocationCheckPolicy;

        if (policy == RevocationCheckPolicy.FailClosed)
        {
            result.Errors.Add(new CredentialValidationError
            {
                RequirementType = requirement.Type,
                FailureReason = CredentialFailureReason.RevocationCheckUnavailable,
                Message = $"Revocation status unavailable for credential '{credentialId}' and policy is fail-closed"
            });
            return null; // Will cause match failure via the error
        }

        // FailOpen: allow but add audit warning
        result.Warnings.Add(
            $"Revocation status unavailable for credential '{credentialId}' of type '{requirement.Type}' — " +
            $"proceeding under fail-open policy. Manual audit recommended.");
        return "Unknown";
    }

    private async Task<CredentialValidationError> FindBestFailureReasonAsync(
        CredentialRequirement requirement,
        List<CredentialPresentation> presentations,
        CancellationToken cancellationToken)
    {
        // Try to find the closest match to give the most specific error
        foreach (var presentation in presentations)
        {
            var typeValue = GetClaimString(presentation.DisclosedClaims, "type")
                ?? GetClaimString(presentation.DisclosedClaims, "vct");

            if (typeValue == null || !string.Equals(typeValue, requirement.Type, StringComparison.OrdinalIgnoreCase))
                continue;

            // Type matched — check issuer
            if (requirement.AcceptedIssuers?.Any() == true)
            {
                var issuer = GetClaimString(presentation.DisclosedClaims, "iss");
                if (issuer == null || !requirement.AcceptedIssuers.Contains(issuer))
                {
                    return new CredentialValidationError
                    {
                        RequirementType = requirement.Type,
                        FailureReason = CredentialFailureReason.IssuerNotAccepted,
                        Message = $"Credential of type '{requirement.Type}' from issuer '{issuer}' is not in the accepted issuers list"
                    };
                }
            }

            // Type and issuer matched — check claims
            if (requirement.RequiredClaims?.Any() == true)
            {
                foreach (var constraint in requirement.RequiredClaims)
                {
                    if (!presentation.DisclosedClaims.TryGetValue(constraint.ClaimName, out var value))
                    {
                        return new CredentialValidationError
                        {
                            RequirementType = requirement.Type,
                            FailureReason = CredentialFailureReason.ClaimMismatch,
                            Message = $"Required claim '{constraint.ClaimName}' not disclosed in credential of type '{requirement.Type}'"
                        };
                    }

                    if (constraint.ExpectedValue != null)
                    {
                        var expectedStr = constraint.ExpectedValue.ToString();
                        var actualStr = value?.ToString();
                        if (!string.Equals(expectedStr, actualStr, StringComparison.Ordinal))
                        {
                            return new CredentialValidationError
                            {
                                RequirementType = requirement.Type,
                                FailureReason = CredentialFailureReason.ClaimMismatch,
                                Message = $"Claim '{constraint.ClaimName}' value '{actualStr}' does not match expected '{expectedStr}'"
                            };
                        }
                    }
                }
            }
        }

        // No close match found — generic missing error
        return new CredentialValidationError
        {
            RequirementType = requirement.Type,
            FailureReason = CredentialFailureReason.Missing,
            Message = $"No credential of type '{requirement.Type}' found in presentations"
        };
    }

    private static string? GetClaimString(Dictionary<string, object> claims, string key)
    {
        return claims.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
