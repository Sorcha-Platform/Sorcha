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
    /// The credential type <see cref="CreateAsync"/> mints. Issue #1198 — this factory previously
    /// emitted NO <c>vct</c>, which is malformed for an SD-JWT VC (the profile requires it) and only
    /// went unnoticed because the verifier never gated on credential type.
    /// </summary>
    internal const string LicenceVct = "https://sorcha.example/vc/licence/v1";

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
            new Dictionary<string, object> { ["licenseType"] = "ClassA", ["holder"] = "Alice", ["vct"] = LicenceVct },
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

    // --- #1195 Phase 2: the REAL AIAS credential shape + holder/device binding parity ------------

    /// <summary>
    /// The Assured Identity credential type URI (<c>VctUris.AssuredIdentityV1</c>, case-sensitive).
    /// Carried as a plaintext <c>vct</c> claim, exactly as the live issuance path emits it.
    /// </summary>
    internal const string AiasVct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>The disclosed AIAS assured claim set — givenName / familyName / dateOfBirth.</summary>
    internal static readonly string[] AiasDisclosedClaims = ["givenName", "familyName", "dateOfBirth"];

    /// <summary>
    /// Which of the two independent P-256 keys plays a role in a presentation. The two keys are
    /// cryptographically indistinguishable to the verifier — the names document intent only:
    /// <see cref="Device"/> is a device-bound copy's on-device key; <see cref="Holder"/> is the
    /// web-root's holder key (whose private half the wallet service holds for server-custody signing).
    /// </summary>
    internal enum Binding
    {
        /// <summary>The device-bound copy's on-device P-256 key.</summary>
        Device,

        /// <summary>The web-root's holder P-256 key (server-custody signer).</summary>
        Holder,
    }

    /// <summary>
    /// Builds a REAL AIAS SD-JWT VC presentation (vct <see cref="AiasVct"/> + the assured claim set),
    /// x5c-chained to a test root, with independent control over which key is the credential's
    /// <c>cnf.jwk</c> (<paramref name="cnf"/>) and which key signs the KB-JWT
    /// (<paramref name="kbSigner"/>, defaulting to a correct match with <paramref name="cnf"/>).
    ///
    /// Correct-binding cases (<paramref name="kbSigner"/> == <paramref name="cnf"/>) prove holder /
    /// device custody parity; mismatched cases (device key over a holder-cnf root, or vice versa)
    /// drive the loud, named key-binding failure the whole phase guards against.
    /// </summary>
    internal static async Task<(string Presentation, byte[] RootCertDer)> CreateAiasAsync(
        ISdJwtService sdJwtService, string audience, string nonce,
        Binding cnf = Binding.Device, Binding? kbSigner = null)
    {
        var signerRole = kbSigner ?? cnf;

        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root CA");

        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, issuerPublicKey, "CN=Test Org", "did:sorcha:org:ws1qtest");

        // Two independent P-256 keys — one stands in for the on-device key, one for the web-root
        // holder key. The verifier cannot tell them apart; only which one signed the KB-JWT matters.
        using var deviceEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var holderEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        ECDsa KeyFor(Binding role) => role == Binding.Device ? deviceEcdsa : holderEcdsa;

        var cnfParams = KeyFor(cnf).ExportParameters(includePrivateParameters: false);
        var cnfJwk = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = System.Buffers.Text.Base64Url.EncodeToString(cnfParams.Q.X!),
            ["y"] = System.Buffers.Text.Base64Url.EncodeToString(cnfParams.Q.Y!)
        }));

        // The live AIAS credential: plaintext vct + the assured identity claim set.
        var token = await sdJwtService.CreateTokenAsync(
            new Dictionary<string, object>
            {
                ["vct"] = AiasVct,
                ["givenName"] = "Ada",
                ["familyName"] = "Lovelace",
                ["dateOfBirth"] = "1815-12-10",
            },
            disclosableClaims: AiasDisclosedClaims,
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivateKey,
            algorithm: "ES256",
            holderJwk: cnfJwk);

        // Inject the x5c chain into the header and re-sign (mirrors the credential endpoint output).
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

        // Export the KB-signing private key up front so the delegate captures a byte[] (the ECDsa
        // instances are disposed with the method, but the signing runs synchronously within scope).
        var kbPrivate = KeyFor(signerRole).ExportECPrivateKey();

        var presentation = await sdJwtService.CreatePresentationAsync(
            newRawToken,
            claimsToDisclose: AiasDisclosedClaims,
            kbJwtSigner: (data, _token) =>
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportECPrivateKey(kbPrivate, out _);
                return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
            },
            holderAlgorithm: "ES256",
            audience: audience,
            nonce: nonce);

        return (presentation.RawPresentation, rootCertDer);
    }
}
