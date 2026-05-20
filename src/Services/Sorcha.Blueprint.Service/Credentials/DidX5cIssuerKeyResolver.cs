// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Blueprint.Service.Credentials;

/// <summary>
/// Service-layer adapter implementing the engine-local <see cref="IIssuerKeyResolver"/> for the
/// internal engine verification path (feature 135, T034). Resolves the issuer public key from a
/// raw SD-JWT VC by the same precedence the HAIP verifier uses — x5c chain (leaf key) → DID
/// resolution (kid-matched, assertionMethod-gated verification method) → embedded JWS jwk header.
/// Keeps the engine WASM-friendly: the <see cref="IDidResolverRegistry"/> dependency lives here.
/// </summary>
public sealed class DidX5cIssuerKeyResolver : IIssuerKeyResolver
{
    private readonly IDidResolverRegistry? _didResolver;
    private readonly ILogger<DidX5cIssuerKeyResolver> _logger;

    public DidX5cIssuerKeyResolver(ILogger<DidX5cIssuerKeyResolver> logger, IDidResolverRegistry? didResolver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _didResolver = didResolver;
    }

    /// <inheritdoc />
    public async Task<IssuerKeyResolution?> ResolveAsync(string rawSdJwt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawSdJwt))
            return null;

        try
        {
            var jwtParts = rawSdJwt.TrimEnd('~').Split('~')[0].Split('.');
            if (jwtParts.Length < 2)
                return null;

            var header = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(jwtParts[0]));
            var algorithm = header.TryGetProperty("alg", out var algEl) ? algEl.GetString() : null;
            if (string.IsNullOrEmpty(algorithm))
                return null;

            var kid = header.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;

            // 1) x5c chain — leaf certificate public key.
            if (header.TryGetProperty("x5c", out var x5cArray) && x5cArray.ValueKind == JsonValueKind.Array)
            {
                var chain = new List<byte[]>();
                foreach (var entry in x5cArray.EnumerateArray())
                {
                    var der = Convert.FromBase64String(entry.GetString()!);
                    chain.Add(der);
                }

                if (chain.Count > 0)
                {
                    using var leaf = X509CertificateLoader.LoadCertificate(chain[0]);
                    var publicKey = leaf.GetECDsaPublicKey()?.ExportSubjectPublicKeyInfo()
                                    ?? leaf.GetRSAPublicKey()?.ExportSubjectPublicKeyInfo();
                    if (publicKey is not null)
                    {
                        return new IssuerKeyResolution
                        {
                            PublicKey = publicKey,
                            Algorithm = algorithm,
                            SigningKeyId = kid,
                            X5cChain = chain
                        };
                    }
                }
            }

            // 2) DID resolution — match kid against a verification method that is still in assertionMethod.
            if (_didResolver is not null)
            {
                var payload = JsonSerializer.Deserialize<JsonElement>(Base64Url.DecodeFromChars(jwtParts[1]));
                if (payload.TryGetProperty("iss", out var iss))
                {
                    var issuerDid = iss.GetString();
                    if (!string.IsNullOrWhiteSpace(issuerDid) && issuerDid.StartsWith("did:", StringComparison.Ordinal))
                    {
                        var document = await _didResolver.ResolveAsync(issuerDid, cancellationToken).ConfigureAwait(false);
                        if (document?.VerificationMethod is { Count: > 0 })
                        {
                            VerificationMethod? matched = null;
                            if (!string.IsNullOrEmpty(kid))
                                matched = document.VerificationMethod.FirstOrDefault(v => string.Equals(v.Id, kid, StringComparison.Ordinal));
                            matched ??= document.VerificationMethod.FirstOrDefault(v => v.PublicKeyJwk.HasValue);

                            if (matched?.PublicKeyJwk is null)
                            {
                                _logger.LogWarning("DID {Did} resolved but no verification method matched kid {Kid}", issuerDid, kid);
                                return null;
                            }

                            // Reject keys dropped from assertionMethod (rotated / revoked — Feature 120).
                            if (document.AssertionMethod is { Count: > 0 } assertion
                                && !assertion.Any(id => string.Equals(id, matched.Id, StringComparison.Ordinal)))
                            {
                                _logger.LogWarning(
                                    "Issuer key matched but is not in assertionMethod (rotated/revoked): iss={Did} kid={Kid}",
                                    issuerDid, matched.Id);
                                return null;
                            }

                            var keyBytes = ExtractPublicKeyFromJwk(matched.PublicKeyJwk.Value);
                            if (keyBytes is not null)
                            {
                                return new IssuerKeyResolution
                                {
                                    PublicKey = keyBytes,
                                    Algorithm = algorithm,
                                    SigningKeyId = matched.Id
                                };
                            }
                        }
                    }
                }
            }

            // 3) Embedded JWS jwk header (self-signed dev mode).
            if (header.TryGetProperty("jwk", out var jwk))
            {
                var keyBytes = ExtractPublicKeyFromJwk(jwk);
                if (keyBytes is not null)
                {
                    _logger.LogWarning("Resolved issuer key from JWS header jwk (self-signed mode)");
                    return new IssuerKeyResolution { PublicKey = keyBytes, Algorithm = algorithm, SigningKeyId = kid };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve issuer key from SD-JWT VC");
        }

        return null;
    }

    private static byte[]? ExtractPublicKeyFromJwk(JsonElement jwk)
    {
        if (!jwk.TryGetProperty("kty", out var kty))
            return null;

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
}
