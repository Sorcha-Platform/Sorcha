// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Sorcha.Haip.Service.Services;

/// <summary>
/// Signs OpenID4VP Request Objects as compact-serialised JWTs per RFC 9101
/// (JWT-Secured Authorization Request, JAR) with typ="oauth-authz-req+jwt".
/// HAIP 1.0 §6.1 mandates a signed Request Object — wallets refuse to act on
/// unsigned JSON bodies.
/// </summary>
/// <remarks>
/// Feature 181 US6 (T053 / R12) — signs with the verifier's X.509 certificate (ES256) and embeds its
/// <c>x5c</c> chain so a wallet can authenticate the verifier: verify the JWS against the leaf key, check
/// the leaf SAN dNSName against the <c>x509_san_dns:{host}</c> client_id, and chain to a trusted anchor.
/// The previous embedded-<c>jwk</c> (self-certifying) header is retired.
/// </remarks>
public sealed class RequestObjectSigner
{
    private readonly VerifierCertificate _certificate;

    public RequestObjectSigner(VerifierCertificate certificate)
    {
        _certificate = certificate ?? throw new ArgumentNullException(nameof(certificate));
    }

    /// <summary>The verifier's prefixed client identifier (<c>x509_san_dns:{PublicHost}</c>).</summary>
    public string ClientId => _certificate.ClientId;

    /// <summary>
    /// Signs <paramref name="payload"/> with the verifier certificate key and returns a compact-serialised
    /// JWT (ES256, <c>x5c</c> header) ready to be served as <c>application/oauth-authz-req+jwt</c>.
    /// </summary>
    public string Sign(Dictionary<string, object> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "oauth-authz-req+jwt",
            ["x5c"] = _certificate.X5cChain.Select(Convert.ToBase64String).ToList(),
        };

        var (signingInput, h, p) = BuildSigningInput(header, payload);
        var signature = _certificate.SignData(signingInput);
        return $"{h}.{p}.{Base64Url.EncodeToString(signature)}";
    }

    private static (byte[] SigningInput, string HeaderB64, string PayloadB64) BuildSigningInput(
        Dictionary<string, object> header,
        Dictionary<string, object> payload)
    {
        var headerJson = JsonSerializer.SerializeToUtf8Bytes(header);
        var payloadJson = JsonSerializer.SerializeToUtf8Bytes(payload);
        var headerB64 = Base64Url.EncodeToString(headerJson);
        var payloadB64 = Base64Url.EncodeToString(payloadJson);
        var signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        return (signingInput, headerB64, payloadB64);
    }
}
