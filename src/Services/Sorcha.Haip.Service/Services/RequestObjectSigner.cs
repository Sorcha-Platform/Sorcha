// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
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
/// Supports ES256 (P-256) and EdDSA (Ed25519). The verifier's public key is
/// embedded as a <c>jwk</c> header so wallets can self-resolve the signing key
/// without a DID or x5c lookup. When spec 096 (x.509 Org Trust) ships this can
/// be replaced by an <c>x5c</c> chain without changing the request shape.
/// </remarks>
public sealed class RequestObjectSigner
{
    private readonly string? _signingKeyBase64;
    private readonly string _algorithm;
    private readonly ILogger<RequestObjectSigner> _logger;

    public RequestObjectSigner(IConfiguration configuration, ILogger<RequestObjectSigner> logger)
    {
        _signingKeyBase64 = configuration.GetValue<string>("Haip:IssuerSigningKey");
        var configuredAlg = configuration.GetValue<string>("Haip:IssuerSigningAlgorithm") ?? "ES256";
        _algorithm = NormaliseAlgorithm(configuredAlg);
        _logger = logger;
    }

    /// <summary>
    /// Signs <paramref name="payload"/> with the configured issuer key and
    /// returns a compact-serialised JWT ready to be served as
    /// <c>application/oauth-authz-req+jwt</c>.
    /// </summary>
    public string Sign(Dictionary<string, object> payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var header = new Dictionary<string, object>
        {
            ["alg"] = _algorithm,
            ["typ"] = "oauth-authz-req+jwt",
        };

        if (_algorithm == "ES256")
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            if (!string.IsNullOrWhiteSpace(_signingKeyBase64))
            {
                ecdsa.ImportECPrivateKey(Convert.FromBase64String(_signingKeyBase64), out _);
            }
            else
            {
                _logger.LogWarning(
                    "Request Object signing using ephemeral ES256 key — set Haip:IssuerSigningKey for production");
            }

            var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
            header["jwk"] = new Dictionary<string, string>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64Url.EncodeToString(parameters.Q.X!),
                ["y"] = Base64Url.EncodeToString(parameters.Q.Y!),
            };

            var (signingInput, h, p) = BuildSigningInput(header, payload);
            var signature = ecdsa.SignData(
                signingInput, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
            return $"{h}.{p}.{Base64Url.EncodeToString(signature)}";
        }

        // EdDSA / Ed25519
        byte[] edPrivateKey;
        byte[] edPublicKey;
        if (!string.IsNullOrWhiteSpace(_signingKeyBase64))
        {
            var seedOrPrivate = Convert.FromBase64String(_signingKeyBase64);
            if (seedOrPrivate.Length == 32)
            {
                var kp = Sodium.PublicKeyAuth.GenerateKeyPair(seedOrPrivate);
                edPrivateKey = kp.PrivateKey;
                edPublicKey = kp.PublicKey;
            }
            else if (seedOrPrivate.Length == 64)
            {
                edPrivateKey = seedOrPrivate;
                edPublicKey = Sodium.PublicKeyAuth.ExtractEd25519PublicKeyFromEd25519SecretKey(seedOrPrivate);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Haip:IssuerSigningKey for EdDSA must be a 32-byte seed or 64-byte secret key (got {seedOrPrivate.Length})");
            }
        }
        else
        {
            _logger.LogWarning(
                "Request Object signing using ephemeral EdDSA key — set Haip:IssuerSigningKey for production");
            var kp = Sodium.PublicKeyAuth.GenerateKeyPair();
            edPrivateKey = kp.PrivateKey;
            edPublicKey = kp.PublicKey;
        }

        header["jwk"] = new Dictionary<string, string>
        {
            ["kty"] = "OKP",
            ["crv"] = "Ed25519",
            ["x"] = Base64Url.EncodeToString(edPublicKey),
        };

        var (signingInputEd, hEd, pEd) = BuildSigningInput(header, payload);
        var signatureEd = Sodium.PublicKeyAuth.SignDetached(signingInputEd, edPrivateKey);
        return $"{hEd}.{pEd}.{Base64Url.EncodeToString(signatureEd)}";
    }

    private static string NormaliseAlgorithm(string raw) => raw.ToUpperInvariant() switch
    {
        "ES256" or "P-256" or "P256" => "ES256",
        "EDDSA" or "ED25519" => "EdDSA",
        _ => "ES256",
    };

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
