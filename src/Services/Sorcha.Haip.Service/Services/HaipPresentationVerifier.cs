// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Models;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Orchestrates the HAIP presentation verification pipeline (feature 135 — unified trust):
/// 1. Extract the issuer public key from the x5c chain or DID resolution.
/// 2. Verify the issuer signature + KB-JWT (holder binding) via <see cref="ISdJwtService"/>.
/// 3. Build an <see cref="IssuerContext"/> and route the trust decision — issuer trust,
///    X.509 chain validation, and revocation/status — through the single
///    <see cref="ITrustEvaluator"/> shared with the internal engine path.
/// 4. Enforce required-claim presence.
///
/// The verifier no longer owns trusted roots or bespoke W3C/IETF status branching: the x509-tenant
/// trust source validates the chain against the tenant anchors, and the unified
/// <see cref="IStatusListChecker"/> reads revocation. This closes the gap where x5c chain validity
/// was reported but never actually gated trust.
/// </summary>
public class HaipPresentationVerifier
{
    private readonly ISdJwtService _sdJwtService;
    private readonly ITrustEvaluator _trustEvaluator;
    private readonly IDidResolverRegistry? _didResolver;
    private readonly ILogger<HaipPresentationVerifier> _logger;

    public HaipPresentationVerifier(
        ISdJwtService sdJwtService,
        ITrustEvaluator trustEvaluator,
        ILogger<HaipPresentationVerifier> logger,
        IDidResolverRegistry? didResolver = null)
    {
        _sdJwtService = sdJwtService ?? throw new ArgumentNullException(nameof(sdJwtService));
        _trustEvaluator = trustEvaluator ?? throw new ArgumentNullException(nameof(trustEvaluator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _didResolver = didResolver;
    }

    /// <summary>
    /// Verifies a vp_token submitted via direct_post. <paramref name="acceptedIssuers"/> (from the
    /// presentation request) seeds the did-allowlist trust source; the x509-tenant source is always
    /// consulted so an x5c-rooted credential is trusted iff it chains to a tenant anchor.
    /// </summary>
    public async Task<VerificationResult> VerifyAsync(
        string vpToken,
        string expectedNonce,
        string expectedAudience,
        string? requiredCredentialType = null,
        List<string>? requiredClaims = null,
        List<string>? acceptedIssuers = null,
        IReadOnlyList<string>? acceptedVctValues = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vpToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAudience);

        var result = new VerificationResult();

        try
        {
            // Step 1: Extract issuer key + algorithm — x5c first, then DID resolution.
            var algorithm = ExtractAlgorithm(vpToken);
            if (algorithm == null)
            {
                result.Errors.Add("Cannot extract algorithm from vp_token header");
                return result;
            }

            var (issuerPublicKey, x5cChain, signingKeyId) = await ResolveIssuerKeyAsync(vpToken, ct);
            if (issuerPublicKey == null)
            {
                result.Errors.Add("Cannot resolve issuer public key from x5c chain or DID document");
                return result;
            }

            // Step 2: Verify the issuer signature + KB-JWT (holder binding).
            var sdJwtResult = await _sdJwtService.VerifyPresentationAsync(
                vpToken, issuerPublicKey, algorithm, expectedAudience, expectedNonce, ct);

            if (!sdJwtResult.IsValid)
            {
                result.Errors.AddRange(sdJwtResult.Errors);
                _logger.LogWarning("HAIP presentation signature/KB verification failed: {Errors}",
                    string.Join("; ", sdJwtResult.Errors));
                return result;
            }

            result.HolderKeyVerified = sdJwtResult.HolderKeyVerified;
            result.Issuer = sdJwtResult.Issuer;
            result.VerifiedClaims = sdJwtResult.Claims;

            // Step 3: Route the trust decision through the unified evaluator.
            var statusRef = ExtractStatusReference(sdJwtResult.Claims);
            var issuer = new IssuerContext
            {
                IssuerId = sdJwtResult.Issuer ?? string.Empty,
                Format = CredentialFormat.SdJwtVc,
                SignatureVerified = true,
                X5cChain = x5cChain,
                SigningKeyId = signingKeyId,
                Status = statusRef,
                RevocationPolicy = RevocationCheckPolicy.FailClosed
            };

            var policy = BuildPolicy(acceptedIssuers);
            var decision = await _trustEvaluator.EvaluateAsync(issuer, policy, ct);

            result.TrustEvidence = decision.Evidence;
            result.X5cChainValid = ResolveChainValidity(decision, x5cChain);
            result.StatusCheckResult = MapStatus(decision, statusRef);
            result.IsValid = decision.IsTrusted;
            TrustMetrics.RecordDecision(decision, CredentialFormat.SdJwtVc);

            if (!decision.IsTrusted)
            {
                result.Errors.Add(decision.Message ?? $"Credential not trusted: {decision.FailureReason}");
                _logger.LogWarning(
                    "HAIP presentation rejected by trust evaluator: issuer={Issuer} reason={Reason}",
                    issuer.IssuerId, decision.FailureReason);
                return result;
            }

            // Step 4a: credential TYPE gate (issue #1198).
            //
            // This parameter was accepted and never read. The only real match gates were the
            // object-keyed envelope id, required-CLAIM presence and issuer trust — so a holder could
            // present a credential of an entirely DIFFERENT type and pass, provided it came from a
            // trusted issuer and disclosed claims with the right NAMES. Claim-name overlap across
            // credential types (givenName, dateOfBirth, …) makes that reachable in practice, and it
            // weakens "prove you hold THIS KIND of credential" to "prove you hold SOME trusted
            // credential carrying these field names".
            //
            // Matching is Ordinal (case-SENSITIVE) — a vct is an absolute URI and an exact machine
            // identifier, consistent with the platform-wide rule since #1187. Fails closed: a
            // credential carrying no vct at all cannot demonstrate it is of the requested type.
            var acceptedTypes = BuildAcceptedVctSet(requiredCredentialType, acceptedVctValues);
            if (acceptedTypes.Count > 0)
            {
                var presentedVct = sdJwtResult.Claims.TryGetValue("vct", out var vctClaim)
                    ? vctClaim?.ToString()
                    : null;

                if (presentedVct is null || !acceptedTypes.Contains(presentedVct))
                {
                    result.IsValid = false;
                    result.Errors.Add(
                        $"Presented credential vct '{presentedVct ?? "(none)"}' is not among the "
                        + $"requested type(s): {string.Join(", ", acceptedTypes)}.");
                    _logger.LogWarning(
                        "HAIP presentation rejected on credential type: presented={Presented} accepted={Accepted}",
                        presentedVct ?? "(none)", string.Join(", ", acceptedTypes));
                }
            }

            // Step 4: Required-claim presence (verifier-level constraint, not trust).
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
                    "HAIP presentation verified: issuer={Issuer}, claims={ClaimCount}, source={Source}, " +
                    "assurance={Assurance}, holderKeyVerified={HolderKey}",
                    sdJwtResult.Issuer, sdJwtResult.Claims.Count, decision.Evidence.VouchingSource,
                    decision.EstablishedAssurance, sdJwtResult.HolderKeyVerified);
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
    /// The set of credential types this request will accept, unioning the DCQL
    /// <c>meta.vct_values</c> (a SET of acceptable URIs, per OpenID4VP) with the legacy single
    /// <c>requiredCredentialType</c>. Empty means the request asked for no particular type, and the
    /// gate does not apply — so this stays opt-in and does not break a caller that never declared one.
    ///
    /// <para><see cref="StringComparer.Ordinal"/> deliberately: matching a vct is exact-identifier
    /// matching, not label matching (#1187).</para>
    /// </summary>
    private static HashSet<string> BuildAcceptedVctSet(
        string? requiredCredentialType,
        IReadOnlyList<string>? acceptedVctValues)
    {
        var accepted = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(requiredCredentialType))
        {
            accepted.Add(requiredCredentialType);
        }

        if (acceptedVctValues is not null)
        {
            foreach (var vct in acceptedVctValues)
            {
                if (!string.IsNullOrWhiteSpace(vct)) accepted.Add(vct);
            }
        }

        return accepted;
    }

    /// <summary>
    /// Synthesises the HAIP trust policy: x509-tenant (chain to a tenant anchor) combined with
    /// either the request's did-allowlist (when accepted issuers are pinned) or the register
    /// source. AnyOf — an x5c-rooted credential or an allow-listed/registered issuer is trusted.
    /// </summary>
    private static TrustPolicy BuildPolicy(List<string>? acceptedIssuers)
    {
        var sources = new List<TrustSourceRef>
        {
            new() { Kind = TrustSourceKind.X509Tenant, ConfersAssurance = AssuranceLevel.Substantial }
        };

        if (acceptedIssuers is { Count: > 0 })
        {
            sources.Add(new TrustSourceRef
            {
                Kind = TrustSourceKind.DidAllowlist,
                AllowedIssuers = acceptedIssuers,
                ConfersAssurance = AssuranceLevel.Substantial
            });
        }
        else
        {
            sources.Add(new TrustSourceRef { Kind = TrustSourceKind.Register, ConfersAssurance = AssuranceLevel.Low });
        }

        return new TrustPolicy
        {
            Sources = sources,
            Combinator = TrustCombinator.AnyOf,
            MinAssuranceLevel = AssuranceLevel.Low
        };
    }

    private static bool? ResolveChainValidity(TrustDecision decision, IReadOnlyList<byte[]>? x5cChain)
    {
        if (x5cChain is null || x5cChain.Count == 0)
            return null;
        // The x509-tenant source ran the chain build; it vouched iff the chain validated to an anchor.
        return decision.DecidingSources.Contains(TrustSourceKind.X509Tenant);
    }

    private static string? MapStatus(TrustDecision decision, StatusReference? statusRef)
    {
        if (decision.FailureReason == TrustFailureReason.Revoked)
            return "Revoked";
        // Feature 192 — a suspension is reversible, so it must not be reported as the terminal
        // status. This is a verifier-visible wire value: HaipPresentationConsumer.MapReason reads
        // it back by string to pick the decline reason.
        if (decision.FailureReason == TrustFailureReason.Suspended)
            return "Suspended";
        if (decision.FailureReason == TrustFailureReason.RevocationUnavailable)
            return "Unknown";
        if (decision.IsTrusted && statusRef is not null)
            return "Active";
        return null;
    }

    /// <summary>
    /// Resolves the issuer public key + x5c chain (DER) + signing key id from the JWS header.
    /// Priority: x5c chain (leaf key) → DID resolution (kid-matched, assertionMethod-gated) →
    /// embedded jwk. Chain validation itself is the x509-tenant trust source's job now.
    /// </summary>
    private async Task<(byte[]? PublicKey, IReadOnlyList<byte[]>? X5cChain, string? SigningKeyId)> ResolveIssuerKeyAsync(
        string vpToken, CancellationToken ct)
    {
        try
        {
            var parts = vpToken.TrimEnd('~').Split('~');
            var jwtParts = parts[0].Split('.');
            if (jwtParts.Length < 2) return (null, null, null);

            var headerBytes = Base64Url.DecodeFromChars(jwtParts[0]);
            var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);
            var kid = header.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;

            // x5c chain — extract the leaf public key and surface the chain for the x509-tenant source.
            if (header.TryGetProperty("x5c", out var x5cArray) && x5cArray.ValueKind == JsonValueKind.Array)
            {
                var chain = new List<byte[]>();
                foreach (var certB64 in x5cArray.EnumerateArray())
                    chain.Add(Convert.FromBase64String(certB64.GetString()!));

                if (chain.Count > 0)
                {
                    using var leaf = X509CertificateLoader.LoadCertificate(chain[0]);
                    var publicKey = leaf.GetECDsaPublicKey()?.ExportSubjectPublicKeyInfo()
                                    ?? leaf.GetRSAPublicKey()?.ExportSubjectPublicKeyInfo();
                    if (publicKey != null)
                    {
                        _logger.LogInformation("Resolved issuer key from x5c chain ({Count} certs)", chain.Count);
                        return (publicKey, chain, kid);
                    }
                }
            }

            // DID resolution — match kid against an assertionMethod verification method (Feature 120).
            if (_didResolver != null)
            {
                var payloadBytes = Base64Url.DecodeFromChars(jwtParts[1]);
                var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);
                if (payload.TryGetProperty("iss", out var iss))
                {
                    var issuerDid = iss.GetString();
                    if (!string.IsNullOrWhiteSpace(issuerDid) && issuerDid.StartsWith("did:", StringComparison.Ordinal))
                    {
                        var didDoc = await _didResolver.ResolveAsync(issuerDid, ct);
                        if (didDoc?.VerificationMethod?.Count > 0)
                        {
                            VerificationMethod? matched = null;
                            if (!string.IsNullOrEmpty(kid))
                                matched = didDoc.VerificationMethod.FirstOrDefault(v => string.Equals(v.Id, kid, StringComparison.Ordinal));
                            matched ??= didDoc.VerificationMethod.FirstOrDefault(v => v.PublicKeyJwk.HasValue);

                            if (matched is null || !matched.PublicKeyJwk.HasValue)
                            {
                                _logger.LogWarning("DID document resolved but no VM matched kid {Kid} for {Did}", kid, issuerDid);
                                return (null, null, null);
                            }

                            if (didDoc.AssertionMethod is { Count: > 0 } assertion
                                && !assertion.Any(id => string.Equals(id, matched.Id, StringComparison.Ordinal)))
                            {
                                _logger.LogWarning(
                                    "Issuer VM matched but is not in assertionMethod (revoked/rotated): iss={Did} kid={Kid}",
                                    issuerDid, matched.Id);
                                return (null, null, null);
                            }

                            var keyBytes = ExtractPublicKeyFromJwk(matched.PublicKeyJwk.Value);
                            if (keyBytes != null)
                            {
                                _logger.LogInformation("Resolved issuer key from DID: {Did} kid={Kid}", issuerDid, kid ?? "(first-vm)");
                                return (keyBytes, null, matched.Id);
                            }
                        }
                    }
                }
            }

            // Embedded jwk header (self-signed dev/test mode).
            if (header.TryGetProperty("jwk", out var issuerJwk))
            {
                var keyBytes = ExtractPublicKeyFromJwk(issuerJwk);
                if (keyBytes != null)
                {
                    _logger.LogWarning("Resolved issuer key from JWS header jwk (self-signed test mode)");
                    return (keyBytes, null, kid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve issuer key");
        }

        return (null, null, null);
    }

    /// <summary>
    /// Reads the credential's status reference (IETF <c>status.status_list</c> preferred, W3C
    /// <c>credentialStatus</c> fallback) into a <see cref="StatusReference"/> for the evaluator.
    /// </summary>
    private static StatusReference? ExtractStatusReference(Dictionary<string, object> claims)
    {
        var (ietfUri, ietfIdx) = TryExtractIetfStatusList(claims);
        if (ietfUri is not null && ietfIdx.HasValue)
            return new StatusReference { Uri = ietfUri, Index = ietfIdx.Value };

        var (w3cUri, w3cIdx, w3cPurpose) = TryExtractW3cCredentialStatus(claims);
        if (w3cUri is not null && w3cIdx.HasValue)
            return new StatusReference { Uri = w3cUri, Index = w3cIdx.Value, Purpose = w3cPurpose };

        return null;
    }

    private static (string? Uri, int? Idx) TryExtractIetfStatusList(Dictionary<string, object> claims)
    {
        if (!claims.TryGetValue("status", out var statusRaw) || statusRaw is null)
            return (null, null);
        if (!TryGetObjectProperty(statusRaw, "status_list", out var statusList))
            return (null, null);
        return (TryReadString(statusList, "uri"), TryReadInt(statusList, "idx"));
    }

    private static (string? Uri, int? Idx, string? Purpose) TryExtractW3cCredentialStatus(Dictionary<string, object> claims)
    {
        if (!claims.TryGetValue("credentialStatus", out var raw) || raw is null)
            return (null, null, null);
        return (TryReadString(raw, "statusListCredential"), TryReadInt(raw, "statusListIndex"), TryReadString(raw, "statusPurpose"));
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
