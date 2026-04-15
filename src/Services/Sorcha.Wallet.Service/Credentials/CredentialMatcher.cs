// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Matches stored credentials against a list of credential requirements.
/// Returns the best match for each requirement.
/// </summary>
public class CredentialMatcher
{
    /// <summary>
    /// For each requirement, finds the best matching credential from the stored credentials.
    /// </summary>
    /// <param name="requirements">The credential requirements to match against.</param>
    /// <param name="credentials">Available stored credentials.</param>
    /// <returns>A dictionary mapping requirement type to the matched credential, or null if unmatched.</returns>
    public Dictionary<string, CredentialEntity?> Match(
        IEnumerable<CredentialRequirement> requirements,
        IReadOnlyList<CredentialEntity> credentials)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(credentials);

        var result = new Dictionary<string, CredentialEntity?>();

        foreach (var requirement in requirements)
        {
            var match = FindMatch(requirement, credentials);
            result[requirement.Type] = match;
        }

        return result;
    }

    private static CredentialEntity? FindMatch(
        CredentialRequirement requirement,
        IReadOnlyList<CredentialEntity> credentials)
    {
        foreach (var credential in credentials)
        {
            // Type match
            if (!string.Equals(credential.Type, requirement.Type, StringComparison.OrdinalIgnoreCase))
                continue;

            // Issuer check
            if (requirement.AcceptedIssuers?.Any() == true &&
                !requirement.AcceptedIssuers.Contains(credential.IssuerDid))
                continue;

            // Expiry check
            if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value <= DateTimeOffset.UtcNow)
                continue;

            // Status check
            if (credential.Status != CredentialStatus.Active)
                continue;

            // Claim constraints check
            if (requirement.RequiredClaims?.Any() == true)
            {
                var claims = ParseClaims(credential.ClaimsJson);
                if (claims == null)
                    continue;

                var claimsMatch = true;
                foreach (var constraint in requirement.RequiredClaims)
                {
                    if (!claims.TryGetValue(constraint.ClaimName, out var value))
                    {
                        claimsMatch = false;
                        break;
                    }

                    if (constraint.ExpectedValue != null)
                    {
                        var expectedStr = constraint.ExpectedValue.ToString();
                        var actualStr = value?.ToString();
                        if (!string.Equals(expectedStr, actualStr, StringComparison.Ordinal))
                        {
                            claimsMatch = false;
                            break;
                        }
                    }
                }

                if (!claimsMatch)
                    continue;
            }

            return credential;
        }

        return null;
    }

    private static Dictionary<string, object>? ParseClaims(string claimsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(claimsJson);
        }
        catch
        {
            return null;
        }
    }
}
