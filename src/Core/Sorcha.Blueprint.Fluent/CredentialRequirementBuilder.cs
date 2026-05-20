// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Fluent;

/// <summary>
/// Fluent builder for creating credential requirements on blueprint actions.
/// </summary>
public class CredentialRequirementBuilder
{
    private readonly CredentialRequirement _requirement = new();
    private readonly List<ClaimConstraint> _claims = new();
    private readonly List<string> _issuers = new();

    /// <summary>
    /// Sets the credential type required (e.g., "LicenseCredential").
    /// </summary>
    public CredentialRequirementBuilder OfType(string credentialType)
    {
        _requirement.Type = credentialType;
        return this;
    }

    /// <summary>
    /// Adds an accepted issuer DID or wallet address (feature 135: collected into a
    /// did-allowlist trust source on <see cref="Build"/>).
    /// </summary>
    public CredentialRequirementBuilder FromIssuer(string issuerDid)
    {
        _issuers.Add(issuerDid);
        return this;
    }

    /// <summary>
    /// Sets the trust policy directly (feature 135), overriding any issuers added via
    /// <see cref="FromIssuer"/>.
    /// </summary>
    public CredentialRequirementBuilder WithTrustPolicy(TrustPolicy policy)
    {
        _requirement.TrustPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the accepted credential format (feature 135).
    /// </summary>
    public CredentialRequirementBuilder OfFormat(CredentialFormat format)
    {
        _requirement.Format = format;
        return this;
    }

    /// <summary>
    /// Requires a claim to be present with any value.
    /// </summary>
    public CredentialRequirementBuilder RequireClaim(string claimName)
    {
        _claims.Add(new ClaimConstraint { ClaimName = claimName });
        return this;
    }

    /// <summary>
    /// Requires a claim to be present with a specific expected value.
    /// </summary>
    public CredentialRequirementBuilder RequireClaim(string claimName, object expectedValue)
    {
        _claims.Add(new ClaimConstraint { ClaimName = claimName, ExpectedValue = expectedValue });
        return this;
    }

    /// <summary>
    /// Sets the revocation check policy.
    /// </summary>
    public CredentialRequirementBuilder WithRevocationCheck(RevocationCheckPolicy policy)
    {
        _requirement.RevocationCheckPolicy = policy;
        return this;
    }

    /// <summary>
    /// Sets the human-readable description displayed in the UI.
    /// </summary>
    public CredentialRequirementBuilder WithDescription(string description)
    {
        _requirement.Description = description;
        return this;
    }

    internal CredentialRequirement Build()
    {
        // Feature 135: issuers added via FromIssuer become a did-allowlist trust source,
        // unless an explicit policy was supplied via WithTrustPolicy.
        if (_requirement.TrustPolicy is null && _issuers.Count > 0)
            _requirement.TrustPolicy = TrustPolicyExtensions.FromLegacyIssuers(_issuers);

        if (_claims.Count > 0)
            _requirement.RequiredClaims = _claims;

        return _requirement;
    }
}
