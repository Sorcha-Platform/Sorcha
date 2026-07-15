// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;

using Sorcha.Cryptography.SdJwt;
using Sorcha.Tenant.Service.Trust;

namespace Sorcha.Haip.Service.Tests;

/// <summary>
/// Feature "citizen OID4VP device-cnf" (#1195) — builds the Phase-1 wallet output: an SD-JWT VC
/// whose <c>cnf.jwk</c> is the <b>device key</b> (a non-extractable P-256 WebCrypto key in Option A),
/// presented with a <b>device-signed KB-JWT</b> (signed by that same P-256 private key). The credential
/// carries an x5c chain to a self-signed test root so issuer trust passes when that root is anchored.
///
/// This is the endpoint-level counterpart of <c>HaipPresentationVerifierTests.CreatePresentationWithX5cAsync</c>
/// — identical device-key + KB-JWT semantics — hoisted to a shared static so the verifier endpoint test can
/// drive the real <c>HandleDirectPost</c> with a genuine device-cnf presentation. There is no delegation
/// artefact anywhere in this shape.
/// </summary>
internal static class DeviceCnfPresentationFactory
{
    /// <summary>The single claim disclosed by <see cref="CreateAsync"/> — request required-claim contract.</summary>
    internal const string DisclosedClaim = "licenseType";

    /// <summary>
    /// Produces a device-cnf SD-JWT VC presentation bound to <paramref name="audience"/> / <paramref name="nonce"/>.
    /// Returns the compact presentation string and the DER of the test root to anchor for trust.
    /// </summary>
    internal static async Task<(string Presentation, byte[] RootCertDer)> CreateAsync(
        ISdJwtService sdJwtService, string audience, string nonce)
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root CA");

        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, issuerPublicKey, "CN=Test Org", "did:sorcha:org:ws1qtest");

        // The DEVICE key: a fresh P-256 key pair. Its public half is the credential's cnf.jwk, and its
        // private half signs the KB-JWT — exactly the Option-A device-bound holder binding.
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var devicePrivate = deviceEcdsa.ExportECPrivateKey();
        var deviceParams = deviceEcdsa.ExportParameters(includePrivateParameters: false);
        var deviceJwk = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = System.Buffers.Text.Base64Url.EncodeToString(deviceParams.Q.X!),
            ["y"] = System.Buffers.Text.Base64Url.EncodeToString(deviceParams.Q.Y!)
        }));

        var token = await sdJwtService.CreateTokenAsync(
            new Dictionary<string, object> { ["licenseType"] = "ClassA", ["holder"] = "Alice" },
            disclosableClaims: ["licenseType", "holder"],
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivateKey,
            algorithm: "ES256",
            holderJwk: deviceJwk);

        // Inject the x5c chain into the header and re-sign (mirrors what the credential endpoint produces).
        var rawParts = token.RawToken.TrimEnd('~').Split('~');
        var jwtSegments = rawParts[0].Split('.');
        var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtSegments[0]);
        var header = JsonSerializer.Deserialize<Dictionary<string, object>>(headerBytes)!;
        header["x5c"] = new[] { Convert.ToBase64String(orgCertDer), Convert.ToBase64String(rootCertDer) };

        var newHeaderB64 = System.Buffers.Text.Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{newHeaderB64}.{jwtSegments[1]}");
        var signature = issuerEcdsa.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = System.Buffers.Text.Base64Url.EncodeToString(signature);
        var newRawToken = $"{newHeaderB64}.{jwtSegments[1]}.{signatureB64}~{string.Join("~", rawParts[1..])}~";

        var presentation = await sdJwtService.CreatePresentationAsync(
            newRawToken,
            claimsToDisclose: [DisclosedClaim],
            kbJwtSigner: (data, _token) =>
            {
                // Sign the KB-JWT with the DEVICE private key — proves holder binding to the cnf key.
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportECPrivateKey(devicePrivate, out _);
                return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
            },
            holderAlgorithm: "ES256",
            audience: audience,
            nonce: nonce);

        return (presentation.RawPresentation, rootCertDer);
    }
}
