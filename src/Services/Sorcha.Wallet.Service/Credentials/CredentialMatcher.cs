// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.ServiceClients.Did;
using Sorcha.Wallet.Core.Domain.Entities;

namespace Sorcha.Wallet.Service.Credentials;

/// <summary>
/// Matches stored credentials against a list of credential requirements.
/// Returns the best match for each requirement.
/// </summary>
public class CredentialMatcher
{
    private readonly IDidResolverRegistry? _registry;

    /// <summary>Default constructor — sync direct-string allowlist matching only.</summary>
    public CredentialMatcher() { }

    /// <summary>
    /// DI constructor — when wired with an <see cref="IDidResolverRegistry"/>,
    /// <see cref="MatchAsync"/> honours the Feature 120 US5 alsoKnownAs
    /// equivalence rules. The legacy sync <see cref="Match"/> path is unchanged.
    /// </summary>
    public CredentialMatcher(IDidResolverRegistry registry)
    {
        _registry = registry;
    }

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

            // Issuer check (feature 135: issuer allowlist now lives on the trust policy)
            var acceptedIssuers = requirement.TrustPolicy.AllowedIssuerDids();
            if (acceptedIssuers.Count > 0 &&
                !acceptedIssuers.Contains(credential.IssuerDid, StringComparer.Ordinal))
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

    /// <summary>
    /// Async match honouring DID alsoKnownAs equivalence (Feature 120 US5 / FR-014).
    /// Behaves identically to <see cref="Match"/> when the matcher is constructed
    /// without a resolver registry.
    /// </summary>
    public async Task<Dictionary<string, CredentialEntity?>> MatchAsync(
        IEnumerable<CredentialRequirement> requirements,
        IReadOnlyList<CredentialEntity> credentials,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(credentials);

        var result = new Dictionary<string, CredentialEntity?>();
        foreach (var requirement in requirements)
        {
            CredentialEntity? match = null;
            foreach (var credential in credentials)
            {
                if (!string.Equals(credential.Type, requirement.Type, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Feature 120 US5 — equivalence-aware allowlist match.
                // Feature 135 — the allowlist is sourced from the trust policy's did-allowlist sources.
                var allowed = requirement.TrustPolicy.AllowedIssuerDids();
                if (!await IssuerEquivalenceMatcher.IsAcceptedAsync(
                        credential.IssuerDid,
                        allowed.Count > 0 ? allowed.ToArray() : null,
                        _registry, ct).ConfigureAwait(false))
                    continue;

                if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value <= DateTimeOffset.UtcNow)
                    continue;
                if (credential.Status != CredentialStatus.Active)
                    continue;

                if (requirement.RequiredClaims?.Any() == true)
                {
                    var claims = ParseClaims(credential.ClaimsJson);
                    if (claims is null) continue;

                    var claimsMatch = true;
                    foreach (var constraint in requirement.RequiredClaims)
                    {
                        if (!claims.TryGetValue(constraint.ClaimName, out var value))
                        {
                            claimsMatch = false; break;
                        }
                        if (constraint.ExpectedValue != null
                            && !string.Equals(constraint.ExpectedValue.ToString(), value?.ToString(), StringComparison.Ordinal))
                        {
                            claimsMatch = false; break;
                        }
                    }
                    if (!claimsMatch) continue;
                }

                match = credential;
                break;
            }
            result[requirement.Type] = match;
        }
        return result;
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
