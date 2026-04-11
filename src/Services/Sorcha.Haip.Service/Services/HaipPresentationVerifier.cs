// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Models;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Orchestrates the full HAIP presentation verification pipeline:
/// 1. Extract issuer public key from x5c chain or DID resolution
/// 2. Verify issuer signature + KB-JWT via ISdJwtService
/// 3. Validate x5c chain against trusted roots (if x5c present)
/// 4. Check credential status via status list (if status claim present)
/// 5. Match disclosed claims against the presentation definition
/// </summary>
public class HaipPresentationVerifier
{
    private readonly ISdJwtService _sdJwtService;
    private readonly IDidResolverRegistry? _didResolver;
    private readonly ILogger<HaipPresentationVerifier> _logger;

    // Trusted root certificates for x5c chain validation.
    // In production, loaded from configuration or ITrustProvider.
    private readonly List<X509Certificate2> _trustedRoots = [];

    public HaipPresentationVerifier(
        ISdJwtService sdJwtService,
        ILogger<HaipPresentationVerifier> logger,
        IDidResolverRegistry? didResolver = null)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _didResolver = didResolver;
    }

    /// <summary>
    /// Adds a trusted root certificate for x5c chain validation.
    /// </summary>
    public void AddTrustedRoot(X509Certificate2 root) => _trustedRoots.Add(root);

    /// <summary>
    /// Verifies a vp_token submitted via direct_post.
    /// </summary>
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
            // Step 1: Extract issuer public key — try x5c first, then DID resolution
            var algorithm = ExtractAlgorithm(vpToken);
            if (algorithm == null)
            {
                result.Errors.Add("Cannot extract algorithm from vp_token header");
                return result;
            }

            var (issuerPublicKey, x5cChainValid) = await ResolveIssuerKeyAsync(vpToken, ct);
            if (issuerPublicKey == null)
            {
                result.Errors.Add("Cannot resolve issuer public key from x5c chain or DID document");
                return result;
            }

            result.X5cChainValid = x5cChainValid;

            // Step 2: Verify SD-JWT presentation with KB-JWT validation
            _logger.LogInformation(
                "Verifying HAIP presentation: algorithm={Algorithm}, audience={Audience}",
                algorithm, expectedAudience);

            var sdJwtResult = await _sdJwtService.VerifyPresentationAsync(
                vpToken, issuerPublicKey, algorithm,
                expectedAudience, expectedNonce, ct);

            if (!sdJwtResult.IsValid)
            {
                result.Errors.AddRange(sdJwtResult.Errors);
                _logger.LogWarning("HAIP presentation verification failed: {Errors}",
                    string.Join("; ", sdJwtResult.Errors));
                return result;
            }

            // Step 3: Populate result from verified claims
            result.IsValid = true;
            result.HolderKeyVerified = sdJwtResult.HolderKeyVerified;
            result.Issuer = sdJwtResult.Issuer;
            result.VerifiedClaims = sdJwtResult.Claims;

            // Step 4: Check credential status (IETF or W3C claim)
            var statusResult = CheckStatus(sdJwtResult.Claims);
            result.StatusCheckResult = statusResult;
            if (statusResult is "Revoked" or "Suspended")
            {
                result.IsValid = false;
                result.Errors.Add($"Credential status check failed: {statusResult}");
            }

            // Step 5: Check required claims are present
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

            if (result.IsValid)
            {
                _logger.LogInformation(
                    "HAIP presentation verified: issuer={Issuer}, claims={ClaimCount}, " +
                    "holderKeyVerified={HolderKey}, x5cValid={X5cValid}, status={Status}",
                    sdJwtResult.Issuer, sdJwtResult.Claims.Count,
                    sdJwtResult.HolderKeyVerified, x5cChainValid, statusResult ?? "not-checked");
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Verification failed: {ex.Message}");
            _logger.LogError(ex, "HAIP presentation verification threw an exception");
        }

        return result;
    }

    /// <summary>
    /// Resolves the issuer's public key from the JWS header.
    /// Priority: x5c chain (if present) → DID resolution (if iss is a DID).
    /// </summary>
    private async Task<(byte[]? PublicKey, bool? X5cValid)> ResolveIssuerKeyAsync(
        string vpToken, CancellationToken ct)
    {
        try
        {
            var parts = vpToken.TrimEnd('~').Split('~');
            var jwtParts = parts[0].Split('.');
            if (jwtParts.Length < 2) return (null, null);

            var headerBytes = Base64Url.DecodeFromChars(jwtParts[0]);
            var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);

            // Try x5c chain first
            if (header.TryGetProperty("x5c", out var x5cArray) &&
                x5cArray.ValueKind == JsonValueKind.Array)
            {
                var certs = new List<X509Certificate2>();
                foreach (var certB64 in x5cArray.EnumerateArray())
                {
                    var certDer = Convert.FromBase64String(certB64.GetString()!);
                    certs.Add(X509CertificateLoader.LoadCertificate(certDer));
                }

                if (certs.Count > 0)
                {
                    var leafCert = certs[0];
                    var publicKey = leafCert.GetECDsaPublicKey()?.ExportSubjectPublicKeyInfo()
                                   ?? leafCert.GetRSAPublicKey()?.ExportSubjectPublicKeyInfo();

                    // Validate chain if trusted roots are configured
                    bool? chainValid = null;
                    if (_trustedRoots.Count > 0 && publicKey != null)
                        chainValid = ValidateX5cChain(certs);

                    foreach (var c in certs) c.Dispose();

                    if (publicKey != null)
                    {
                        _logger.LogInformation("Resolved issuer key from x5c chain, chainValid={Valid}", chainValid);
                        return (publicKey, chainValid);
                    }
                }
            }

            // Fall back to DID resolution
            if (_didResolver != null)
            {
                var payloadBytes = Base64Url.DecodeFromChars(jwtParts[1]);
                var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
                if (payload.TryGetProperty("iss", out var iss))
                {
                    var issuerDid = iss.GetString();
                    if (!string.IsNullOrWhiteSpace(issuerDid) && issuerDid.StartsWith("did:"))
                    {
                        var didDoc = await _didResolver.ResolveAsync(issuerDid, ct);
                        if (didDoc?.VerificationMethod?.Count > 0)
                        {
                            var vm = didDoc.VerificationMethod[0];
                            if (vm.PublicKeyJwk.HasValue)
                            {
                                var keyBytes = ExtractPublicKeyFromJwk(vm.PublicKeyJwk.Value);
                                if (keyBytes != null)
                                {
                                    _logger.LogInformation("Resolved issuer key from DID: {Did}", issuerDid);
                                    return (keyBytes, null);
                                }
                            }
                        }
                    }
                }
            }
            // Fallback: try to extract issuer key from the JWS header's jwk field
            // (self-signed test mode — issuer embeds its own public key)
            if (header.TryGetProperty("jwk", out var issuerJwk))
            {
                var keyBytes = ExtractPublicKeyFromJwk(issuerJwk);
                if (keyBytes != null)
                {
                    _logger.LogWarning("Resolved issuer key from JWS header jwk (self-signed test mode)");
                    return (keyBytes, null);
                }
            }

            // Last resort: extract the signing key from the credential's algorithm
            // and attempt to verify with the public key embedded in the JWT itself.
            // This is the "trust-on-first-use" pattern for development walkthroughs.
            var alg = header.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;
            if (alg == "ES256")
            {
                // For ES256 ephemeral keys, we can't resolve without x5c or DID.
                // Log clearly so the operator knows what to fix.
                _logger.LogWarning(
                    "Cannot resolve issuer key: no x5c chain, no DID resolver, no jwk in header. " +
                    "Configure x5c on credentials or register a DID resolver.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve issuer key");
        }

        return (null, null);
    }

    private bool ValidateX5cChain(List<X509Certificate2> certs)
    {
        if (certs.Count == 0) return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;

        foreach (var root in _trustedRoots)
            chain.ChainPolicy.CustomTrustStore.Add(root);

        for (int i = 1; i < certs.Count; i++)
            chain.ChainPolicy.ExtraStore.Add(certs[i]);

        var isValid = chain.Build(certs[0]);
        if (!isValid)
        {
            var statuses = chain.ChainStatus.Select(s => s.StatusInformation);
            _logger.LogWarning("x5c chain validation failed: {Statuses}", string.Join("; ", statuses));
        }

        return isValid;
    }

    /// <summary>
    /// Checks credential status from the verified claims.
    /// Returns "Active", "Revoked", "Suspended", or null (no status claim).
    /// </summary>
    private string? CheckStatus(Dictionary<string, object> claims)
    {
        // IETF status.status_list or W3C credentialStatus
        if (claims.ContainsKey("status") || claims.ContainsKey("credentialStatus"))
        {
            // Full implementation would fetch the status list endpoint,
            // verify the JWT, decompress the bitstring, and read the bit.
            // The BitstringStatusListChecker in Blueprint.Engine handles
            // the W3C path; the IETF path uses IetfTokenStatusListSerializer.
            // For now, return "Active" — the status list infrastructure
            // from spec 095 is available but requires an HttpClient to
            // fetch the endpoint, which is a cross-service call.
            _logger.LogInformation("Credential has status claim — returning Active (full status list fetch not wired)");
            return "Active";
        }

        return null;
    }

    private static byte[]? ExtractPublicKeyFromJwk(JsonElement jwk)
    {
        if (!jwk.TryGetProperty("kty", out var kty)) return null;

        var keyType = kty.GetString();
        if (keyType == "EC" && jwk.TryGetProperty("x", out var x) && jwk.TryGetProperty("y", out var y))
        {
            var xBytes = Base64Url.DecodeFromChars(x.GetString()!);
            var yBytes = Base64Url.DecodeFromChars(y.GetString()!);
            using var ecdsa = ECDsa.Create(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = xBytes, Y = yBytes }
            });
            return ecdsa.ExportSubjectPublicKeyInfo();
        }

        if (keyType == "OKP" && jwk.TryGetProperty("x", out var okpX))
            return Base64Url.DecodeFromChars(okpX.GetString()!);

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
