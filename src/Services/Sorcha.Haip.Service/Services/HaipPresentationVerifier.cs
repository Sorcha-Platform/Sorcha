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
    private readonly IetfTokenStatusListChecker? _ietfStatusChecker;
    private readonly X509RevocationMode _revocationMode;
    private readonly ILogger<HaipPresentationVerifier> _logger;

    // Trusted root certificates for x5c chain validation.
    // In production, loaded from configuration or ITrustProvider.
    private readonly List<X509Certificate2> _trustedRoots = [];

    public HaipPresentationVerifier(
        ISdJwtService sdJwtService,
        ILogger<HaipPresentationVerifier> logger,
        IDidResolverRegistry? didResolver = null,
        IetfTokenStatusListChecker? ietfStatusChecker = null,
        X509RevocationMode revocationMode = X509RevocationMode.NoCheck)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _didResolver = didResolver;
        _ietfStatusChecker = ietfStatusChecker;
        _revocationMode = revocationMode;
    }

    /// <summary>
    /// Exposes the effective revocation mode — used by tests and diagnostics.
    /// </summary>
    internal X509RevocationMode RevocationMode => _revocationMode;

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
            var statusResult = await CheckStatusAsync(sdJwtResult.Claims, ct);
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
                            // Feature 120 — match the credential's JWS kid header to a VM
                            // in the resolved doc (exact id match). Falls back to the
                            // first VM only when the credential carries no kid (legacy
                            // single-key documents).
                            var kid = header.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;
                            VerificationMethod? matched = null;
                            if (!string.IsNullOrEmpty(kid))
                            {
                                matched = didDoc.VerificationMethod
                                    .FirstOrDefault(v => string.Equals(v.Id, kid, StringComparison.Ordinal));
                            }
                            matched ??= didDoc.VerificationMethod.FirstOrDefault(v => v.PublicKeyJwk.HasValue);

                            if (matched is null || !matched.PublicKeyJwk.HasValue)
                            {
                                _logger.LogWarning(
                                    "DID document resolved but no VM matched kid {Kid} for {Did}",
                                    kid, issuerDid);
                                return (null, null);
                            }

                            // Feature 120 US6 — reject if the matched VM is not in
                            // assertionMethod (revoked / rotated keys are dropped from
                            // assertionMethod by IssuanceKeyService while remaining in
                            // verificationMethod for verifiable history).
                            if (didDoc.AssertionMethod is { Count: > 0 } assertion
                                && !assertion.Any(id => string.Equals(id, matched.Id, StringComparison.Ordinal)))
                            {
                                _logger.LogWarning(
                                    "Issuer VM matched but is not in assertionMethod (revoked/rotated): " +
                                    "iss={Did} kid={Kid} matched_vm={VmId}",
                                    issuerDid, kid, matched.Id);
                                return (null, null);
                            }

                            var keyBytes = ExtractPublicKeyFromJwk(matched.PublicKeyJwk.Value);
                            if (keyBytes != null)
                            {
                                _logger.LogInformation(
                                    "Resolved issuer key from DID: {Did} kid={Kid}",
                                    issuerDid, kid ?? "(first-vm)");
                                return (keyBytes, null);
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
        // Feature 096 US6 completion — revocation mode defaults to NoCheck for
        // unit-test friendliness (CDP URLs in tests point at unreachable domains)
        // but production deployments set Haip:VerifyRevocation=true at DI wiring
        // time so chain.Build fetches CRLs from the CDP extension embedded in
        // org certs by the Tenant Service. ExcludeRoot skips the self-signed
        // tenant root — it has no CRL issuer and will always read as unknown.
        chain.ChainPolicy.RevocationMode = _revocationMode;
        if (_revocationMode != X509RevocationMode.NoCheck)
        {
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            // 30s is generous enough for a single CDP fetch but tight enough
            // that a slow CRL endpoint doesn't block the verifier indefinitely.
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(30);
        }
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
    /// Feature 095 US4 — resolves a credential's lifecycle status by reading the
    /// <c>status.status_list</c> claim (IETF, preferred) or <c>credentialStatus</c>
    /// claim (W3C, fallback), fetching the referenced status list endpoint, and
    /// reading the bit at the allocated index. Returns:
    /// <list type="bullet">
    ///   <item><c>"Active"</c> — bit is 0 or the credential carries no status claim.</item>
    ///   <item><c>"Revoked"</c> / <c>"Suspended"</c> — bit is 1. Purpose disambiguation
    ///   comes from the W3C <c>statusPurpose</c> field when present; IETF lists default
    ///   to <c>Revoked</c> (bits=1 semantic).</item>
    ///   <item>null — no claim present, caller treats as Active per FR-010 policy.</item>
    ///   <item><c>"Unknown"</c> — claim was present but the endpoint was unreachable
    ///   or unverifiable. Non-fatal by design — lets the orchestrator decide whether
    ///   to fail-open.</item>
    /// </list>
    /// </summary>
    private async Task<string?> CheckStatusAsync(Dictionary<string, object> claims, CancellationToken ct)
    {
        // IETF claim takes precedence over W3C per spec 095 US4.
        var (ietfUri, ietfIdx) = TryExtractIetfStatusList(claims);
        if (ietfUri is not null && ietfIdx.HasValue)
        {
            if (_ietfStatusChecker is null)
            {
                _logger.LogWarning(
                    "Credential carries IETF status claim but IetfTokenStatusListChecker is not wired — returning Unknown");
                return "Unknown";
            }

            var bit = await _ietfStatusChecker.CheckBitAsync(ietfUri, ietfIdx.Value, ct);
            return bit switch
            {
                StatusListBit.NotSet => "Active",
                StatusListBit.Set => "Revoked",
                _ => "Unknown",
            };
        }

        var (w3cUri, w3cIdx, w3cPurpose) = TryExtractW3cCredentialStatus(claims);
        if (w3cUri is not null && w3cIdx.HasValue)
        {
            if (_ietfStatusChecker is null)
            {
                _logger.LogWarning(
                    "Credential carries W3C status claim but IetfTokenStatusListChecker is not wired — returning Unknown");
                return "Unknown";
            }

            // The W3C endpoint also serves a signed JWT envelope in this codebase's
            // deployment — the IETF checker's fetch+decompress path accepts either.
            // When the backing raw bitstring differs (pre-095 W3C-only deployments),
            // this will return Unknown and the caller falls back to server-side.
            var bit = await _ietfStatusChecker.CheckBitAsync(w3cUri, w3cIdx.Value, ct);
            return bit switch
            {
                StatusListBit.NotSet => "Active",
                StatusListBit.Set => string.Equals(w3cPurpose, "suspension", StringComparison.OrdinalIgnoreCase)
                    ? "Suspended"
                    : "Revoked",
                _ => "Unknown",
            };
        }

        // No status claim at all — treat as Active by default (pre-spec-093 credentials).
        return null;
    }

    /// <summary>
    /// Reads the IETF <c>status.status_list</c> claim into a (uri, idx) pair.
    /// Returns (null, null) when the claim is absent or malformed.
    /// </summary>
    private static (string? Uri, int? Idx) TryExtractIetfStatusList(Dictionary<string, object> claims)
    {
        if (!claims.TryGetValue("status", out var statusRaw) || statusRaw is null)
            return (null, null);
        if (!TryGetObjectProperty(statusRaw, "status_list", out var statusList))
            return (null, null);
        var uri = TryReadString(statusList, "uri");
        var idx = TryReadInt(statusList, "idx");
        return (uri, idx);
    }

    /// <summary>
    /// Reads the W3C <c>credentialStatus</c> claim into a (uri, idx, purpose)
    /// tuple. <c>statusListCredential</c> maps to uri; <c>statusListIndex</c>
    /// maps to idx (may be a string or number in the wire form).
    /// </summary>
    private static (string? Uri, int? Idx, string? Purpose) TryExtractW3cCredentialStatus(
        Dictionary<string, object> claims)
    {
        if (!claims.TryGetValue("credentialStatus", out var raw) || raw is null)
            return (null, null, null);
        var uri = TryReadString(raw, "statusListCredential");
        var idx = TryReadInt(raw, "statusListIndex");
        var purpose = TryReadString(raw, "statusPurpose");
        return (uri, idx, purpose);
    }

    private static bool TryGetObjectProperty(object container, string name, out object value)
    {
        if (container is Dictionary<string, object> dict && dict.TryGetValue(name, out var v) && v is not null)
        {
            value = v;
            return true;
        }
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop))
        {
            value = prop;
            return true;
        }
        value = null!;
        return false;
    }

    private static string? TryReadString(object container, string name)
    {
        if (container is Dictionary<string, object> dict && dict.TryGetValue(name, out var v))
            return v?.ToString();
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop))
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() : prop.ToString();
        return null;
    }

    private static int? TryReadInt(object container, string name)
    {
        if (container is Dictionary<string, object> dict && dict.TryGetValue(name, out var v))
        {
            return v switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                string s when int.TryParse(s, out var parsed) => parsed,
                JsonElement jEl => ReadJsonInt(jEl),
                _ => null,
            };
        }
        if (container is JsonElement element && element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop))
            return ReadJsonInt(prop);
        return null;

        static int? ReadJsonInt(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.Number when el.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(el.GetString(), out var n) => n,
            _ => null,
        };
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
