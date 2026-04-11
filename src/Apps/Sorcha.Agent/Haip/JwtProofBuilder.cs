// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sorcha.Agent.Haip;

/// <summary>
/// Builds a JWT proof of possession per OpenID4VCI for the credential endpoint.
/// The proof binds the holder's public key and the c_nonce from the token response.
/// </summary>
public static class JwtProofBuilder
{
    /// <summary>
    /// Builds and signs a JWT proof of possession.
    /// </summary>
    /// <param name="holderKey">The holder's ECDsa key pair.</param>
    /// <param name="holderJwk">The holder's public key in JWK form (for the header).</param>
    /// <param name="credentialIssuer">The credential_issuer URL (aud claim).</param>
    /// <param name="cNonce">The c_nonce from the token response.</param>
    /// <returns>The compact JWS string.</returns>
    public static string Build(ECDsa holderKey, JsonElement holderJwk, string credentialIssuer, string cNonce)
    {
        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "openid4vci-proof+jwt",
            ["jwk"] = holderJwk
        };

        var payload = new Dictionary<string, object>
        {
            ["aud"] = credentialIssuer,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["nonce"] = cNonce
        };

        var headerB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadB64 = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}");
        var signature = holderKey.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = Base64Url.EncodeToString(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }
}
