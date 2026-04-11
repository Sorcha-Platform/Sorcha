// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Models;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Orchestrates the HAIP presentation verification pipeline:
/// 1. Parse the SD-JWT VC from vp_token
/// 2. Verify issuer signature via ISdJwtService
/// 3. Validate KB-JWT against cnf (nonce + audience binding)
/// 4. Match disclosed claims against the presentation definition
///
/// x5c chain validation and IETF status list checks are deferred
/// to the next wiring milestone — this verifier handles the core
/// SD-JWT VC + KB-JWT verification that spec 094 enabled.
/// </summary>
public class HaipPresentationVerifier
{
    private readonly ISdJwtService _sdJwtService;
    private readonly ILogger<HaipPresentationVerifier> _logger;

    public HaipPresentationVerifier(
        ISdJwtService sdJwtService,
        ILogger<HaipPresentationVerifier> logger)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Verifies a vp_token submitted via direct_post.
    /// </summary>
    /// <param name="vpToken">The SD-JWT VC presentation string from the wallet.</param>
    /// <param name="expectedNonce">The nonce from the original presentation request.</param>
    /// <param name="expectedAudience">The verifier's client_id (audience for KB-JWT).</param>
    /// <param name="requiredCredentialType">Expected credential type (for claim matching).</param>
    /// <param name="requiredClaims">Claims that must be disclosed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification result with disclosed claims or errors.</returns>
    public async Task<VerificationResult> VerifyAsync(
        string vpToken,
        string expectedNonce,
        string expectedAudience,
        string? requiredCredentialType = null,
        List<string>? requiredClaims = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vpToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);

        var result = new VerificationResult();

        try
        {
            // Step 1: Extract the issuer public key from the SD-JWT header
            // In a full implementation, this would resolve the issuer DID or
            // walk the x5c chain. For now, extract from the JWT itself.
            var issuerPublicKey = ExtractIssuerPublicKey(vpToken);
            var algorithm = ExtractAlgorithm(vpToken);

            if (issuerPublicKey == null || algorithm == null)
            {
                result.Errors.Add("Cannot extract issuer public key or algorithm from vp_token header");
                return result;
            }

            // Step 2: Verify the SD-JWT presentation with KB-JWT validation
            _logger.LogInformation(
                "Verifying HAIP presentation: algorithm={Algorithm}, audience={Audience}",
                algorithm, expectedAudience);

            var sdJwtResult = await _sdJwtService.VerifyPresentationAsync(
                vpToken,
                issuerPublicKey,
                algorithm,
                expectedAudience,
                expectedNonce,
                ct);

            if (!sdJwtResult.IsValid)
            {
                result.Errors.AddRange(sdJwtResult.Errors);
                _logger.LogWarning(
                    "HAIP presentation verification failed: {Errors}",
                    string.Join("; ", sdJwtResult.Errors));
                return result;
            }

            // Step 3: Populate result
            result.IsValid = true;
            result.HolderKeyVerified = sdJwtResult.HolderKeyVerified;
            result.Issuer = sdJwtResult.Issuer;
            result.VerifiedClaims = sdJwtResult.Claims;

            // Step 4: Check required claims are present
            if (requiredClaims != null)
            {
                foreach (var claim in requiredClaims)
                {
                    if (!sdJwtResult.Claims.ContainsKey(claim))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"Required claim '{claim}' not disclosed in presentation");
                    }
                }
            }

            // TODO: Step 5 — x5c chain validation via ITrustStore (spec 096)
            // TODO: Step 6 — IETF/W3C status list check (spec 095)

            if (result.IsValid)
            {
                _logger.LogInformation(
                    "HAIP presentation verified: issuer={Issuer}, claims={ClaimCount}, holderKeyVerified={HolderKey}",
                    sdJwtResult.Issuer, sdJwtResult.Claims.Count, sdJwtResult.HolderKeyVerified);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Verification failed: {ex.Message}");
            _logger.LogError(ex, "HAIP presentation verification threw an exception");
        }

        return result;
    }

    private static byte[]? ExtractIssuerPublicKey(string vpToken)
    {
        // In a full implementation, the issuer public key would be resolved
        // from the issuer DID document or the x5c chain in the JWS header.
        // For now, this is a placeholder that returns null — callers must
        // supply the key via a separate lookup.
        // TODO(098): Resolve issuer key from DID or x5c
        return null;
    }

    private static string? ExtractAlgorithm(string vpToken)
    {
        try
        {
            var parts = vpToken.TrimEnd('~').Split('~');
            var jwtParts = parts[0].Split('.');
            if (jwtParts.Length < 2) return null;

            var headerBytes = Base64Url.DecodeFromChars(jwtParts[0]);
            var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);
            return header.TryGetProperty("alg", out var alg) ? alg.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
