// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Agent.Haip;

/// <summary>
/// Builds a Key Binding JWT per the SD-JWT spec for credential presentations.
/// The KB-JWT proves the presenter holds the key named in the credential's cnf claim.
/// </summary>
public static class KbJwtBuilder
{
    /// <summary>
    /// Builds and signs a Key Binding JWT.
    /// </summary>
    /// <param name="holderKey">The holder's ECDsa key pair (must match cnf.jwk).</param>
    /// <param name="audience">The verifier's audience URI.</param>
    /// <param name="nonce">The verifier's nonce from the presentation request.</param>
    /// <param name="presentationPrefix">The SD-JWT presentation prefix (everything before the KB-JWT) for sd_hash computation.</param>
    /// <returns>The compact JWS string.</returns>
    public static string Build(ECDsa holderKey, string audience, string nonce, string presentationPrefix)
    {
        var sdHash = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(presentationPrefix)));

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt"
        };

        var payload = new Dictionary<string, object>
        {
            ["aud"] = audience,
            ["nonce"] = nonce,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["sd_hash"] = sdHash
        };

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        var signature = holderKey.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = Base64Url.EncodeToString(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }
}
