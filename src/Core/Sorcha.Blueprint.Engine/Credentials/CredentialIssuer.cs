// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Xml;
using Sorcha.Blueprint.Engine.Models;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Issues SD-JWT VC credentials from blueprint action data using the crypto service.
/// </summary>
public class CredentialIssuer : ICredentialIssuer
{
    private readonly ISdJwtService _sdJwtService;

    public CredentialIssuer(ISdJwtService sdJwtService)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
    }

    /// <inheritdoc />
    public async Task<IssuedCredentialInfo> IssueAsync(
        CredentialIssuanceConfig config,
        Dictionary<string, object> processedData,
        string issuerDid,
        string recipientDid,
        byte[] signingKey,
        string algorithm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(processedData);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuerDid);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientDid);
        ArgumentNullException.ThrowIfNull(signingKey);

        // Step 1: Map action data fields to credential claims
        var claims = MapClaims(config.ClaimMappings, processedData);

        // Add standard claims.
        // SD-JWT VC (draft-ietf-oauth-sd-jwt-vc §3.2.2.1): vct is the SOLE type claim, a
        // case-sensitive URI. There is no `type` claim in the profile — do not write one.
        var vct = string.IsNullOrWhiteSpace(config.Vct) ? config.CredentialType : config.Vct;
        claims["vct"] = vct;

        // Step 2: Calculate expiry
        var issuedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? expiresAt = null;
        if (!string.IsNullOrWhiteSpace(config.ExpiryDuration))
        {
            expiresAt = issuedAt + ParseIsoDuration(config.ExpiryDuration);
        }

        // Step 3: Determine disclosable claims
        var disclosable = config.Disclosable?.ToList();

        // Step 4: Create the SD-JWT VC token
        var token = await _sdJwtService.CreateTokenAsync(
            claims,
            disclosable,
            issuerDid,
            recipientDid,
            signingKey,
            algorithm,
            expiresAt,
            cancellationToken);

        // Step 5: Generate credential ID
        var credentialId = $"urn:uuid:{Guid.NewGuid()}";

        // Step 6: Thread the authored display name into the display carrier so it survives
        // to IssuedCredentialInfo.DisplayConfigJson / CredentialEntity independently of vct.
        var displayConfig = config.DisplayConfig;
        if (!string.IsNullOrWhiteSpace(config.DisplayName))
        {
            displayConfig ??= new CredentialDisplayConfig();
            displayConfig.CredentialName = config.DisplayName;
        }

        return new IssuedCredentialInfo
        {
            CredentialId = credentialId,
            Type = vct,
            IssuerDid = issuerDid,
            SubjectDid = recipientDid,
            Claims = claims,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            RawToken = token.RawToken,
            UsagePolicy = config.UsagePolicy.ToString(),
            MaxPresentations = config.MaxPresentations,
            DisplayConfigJson = displayConfig != null
                ? JsonSerializer.Serialize(displayConfig)
                : null
        };
    }

    /// <summary>
    /// Maps action data fields to credential claims using the configured mappings.
    /// </summary>
    private static Dictionary<string, object> MapClaims(
        IEnumerable<ClaimMapping> mappings,
        Dictionary<string, object> processedData)
    {
        var claims = new Dictionary<string, object>();

        foreach (var mapping in mappings)
        {
            var sourceKey = mapping.SourceField.TrimStart('/');
            if (processedData.TryGetValue(sourceKey, out var value))
            {
                claims[mapping.ClaimName] = value;
            }
        }

        return claims;
    }

    /// <summary>
    /// Parses an ISO 8601 duration string (e.g., "P365D", "P1Y", "P6M").
    /// </summary>
    private static TimeSpan ParseIsoDuration(string isoDuration)
    {
        try
        {
            return XmlConvert.ToTimeSpan(isoDuration);
        }
        catch (FormatException)
        {
            // WARNING: Failed to parse ISO 8601 duration, falling back to 365-day default.
            // This may silently produce credentials with unintended lifetimes if the
            // ExpiryDuration configuration value is malformed.
            return TimeSpan.FromDays(365);
        }
    }
}
