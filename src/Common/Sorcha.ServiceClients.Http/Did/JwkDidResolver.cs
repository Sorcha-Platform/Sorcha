// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves <c>did:jwk</c> DIDs (https://github.com/quvox/did-jwk-spec) by decoding the base64url-encoded
/// JWK embedded in the identifier. Curve-agnostic — EC (P-256, secp256k1), OKP (Ed25519), and RSA keys
/// are all surfaced verbatim as the verification method's <c>publicKeyJwk</c>. No network calls are made.
/// </summary>
public class JwkDidResolver : IDidResolver
{
    private const string Method = "jwk";
    private const string DidJwkPrefix = "did:jwk:";

    private readonly ILogger<JwkDidResolver> _logger;

    public JwkDidResolver(ILogger<JwkDidResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did) || !did.StartsWith(DidJwkPrefix, StringComparison.Ordinal))
            return Task.FromResult<DidDocument?>(null);

        var encoded = did[DidJwkPrefix.Length..];
        if (encoded.Length == 0)
            return Task.FromResult<DidDocument?>(null);

        JsonElement jwk;
        try
        {
            var json = Base64Url.DecodeFromChars(encoded);
            jwk = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to base64url-decode did:jwk payload in {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        if (jwk.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("did:jwk payload is not a JSON object in {Did}", did);
            return Task.FromResult<DidDocument?>(null);
        }

        // did:jwk fixes the verification method fragment to '#0'.
        var keyId = $"{did}#0";

        var doc = new DidDocument
        {
            Id = did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = keyId,
                    Type = "JsonWebKey2020",
                    Controller = did,
                    PublicKeyJwk = jwk
                }
            ],
            Authentication = [keyId],
            AssertionMethod = [keyId]
        };

        return Task.FromResult<DidDocument?>(doc);
    }
}
