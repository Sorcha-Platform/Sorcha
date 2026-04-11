// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for HaipPresentationVerifier — full verification pipeline including
/// x5c chain validation, issuer key resolution, and status checking.
/// </summary>
public class HaipPresentationVerifierTests
{
    private readonly SdJwtService _sdJwtService = new();

    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportECPrivateKey(), ecdsa.ExportSubjectPublicKeyInfo());
    }

    private static JsonElement CreateHolderJwk(byte[] publicKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Convert.ToBase64String(parameters.Q.X!).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["y"] = Convert.ToBase64String(parameters.Q.Y!).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        };
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    private HaipPresentationVerifier CreateVerifier() =>
        new(_sdJwtService, Mock.Of<ILogger<HaipPresentationVerifier>>());

    /// <summary>
    /// Creates an SD-JWT VC with x5c chain in the header, simulating what
    /// a real HAIP issuer would produce. Uses X509CertificateBuilder from spec 096.
    /// </summary>
    private async Task<(string presentation, byte[] rootCertDer)> CreatePresentationWithX5cAsync(
        string audience, string nonce)
    {
        // Generate root CA and org cert using spec 096 infrastructure
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Test Root CA");

        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, issuerPublicKey,
            "CN=Test Org", "did:sorcha:org:ws1qtest");

        // Create holder key
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        // Create the SD-JWT VC with cnf binding
        var token = await _sdJwtService.CreateTokenAsync(
            new Dictionary<string, object>
            {
                ["licenseType"] = "ClassA",
                ["holder"] = "Alice"
            },
            disclosableClaims: ["licenseType", "holder"],
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivateKey,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Manually inject x5c into the header by rebuilding the token
        // (ISdJwtService doesn't support x5c yet — this simulates what the
        // credential endpoint would produce once Task 4 is wired)
        var rawParts = token.RawToken.TrimEnd('~').Split('~');
        var jwtSegments = rawParts[0].Split('.');
        var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtSegments[0]);
        var header = JsonSerializer.Deserialize<Dictionary<string, object>>(headerBytes)!;

        header["x5c"] = new[] {
            Convert.ToBase64String(orgCertDer),
            Convert.ToBase64String(rootCertDer)
        };

        var newHeaderB64 = System.Buffers.Text.Base64Url.EncodeToString(
            JsonSerializer.SerializeToUtf8Bytes(header));
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{newHeaderB64}.{jwtSegments[1]}");
        var signature = issuerEcdsa.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = System.Buffers.Text.Base64Url.EncodeToString(signature);

        var newJwt = $"{newHeaderB64}.{jwtSegments[1]}.{signatureB64}";
        var newRawToken = newJwt + "~" + string.Join("~", rawParts[1..]) + "~";

        // Create presentation with KB-JWT
        int bytesRead;
        var presentation = await _sdJwtService.CreatePresentationAsync(
            newRawToken,
            claimsToDisclose: ["licenseType"],
            kbJwtSigner: (data, _) =>
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportECPrivateKey(holderPrivate, out bytesRead);
                return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
            },
            holderAlgorithm: "ES256",
            audience: audience,
            nonce: nonce);

        return (presentation.RawPresentation, rootCertDer);
    }

    [Fact]
    public async Task Verify_EmptyVpToken_ThrowsArgumentException()
    {
        var verifier = CreateVerifier();
        var act = () => verifier.VerifyAsync("", "nonce", "audience");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Verify_MalformedVpToken_ReturnsErrors()
    {
        var verifier = CreateVerifier();
        var result = await verifier.VerifyAsync("not-a-valid-token", "nonce", "https://verifier.example.com");
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Verify_WithX5cChain_ResolvesIssuerKey_AndVerifies()
    {
        var verifier = CreateVerifier();
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-1");

        // Add the root CA as trusted
        verifier.AddTrustedRoot(X509CertificateLoader.LoadCertificate(rootCertDer));

        var result = await verifier.VerifyAsync(
            presentation,
            expectedNonce: "test-nonce-1",
            expectedAudience: "https://verifier.example.com");

        result.IsValid.Should().BeTrue();
        result.HolderKeyVerified.Should().BeTrue();
        result.X5cChainValid.Should().BeTrue();
        result.VerifiedClaims.Should().ContainKey("licenseType");
        result.Issuer.Should().Be("did:sorcha:org:ws1qtest");
    }

    [Fact]
    public async Task Verify_WithX5cChain_UntrustedRoot_ChainInvalid()
    {
        var verifier = CreateVerifier();
        var (presentation, _) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-2");

        // Add a DIFFERENT root as trusted (not the one that signed the org cert)
        var (differentRootDer, _, _) = X509CertificateBuilder.BuildSelfSignedRoot(
            "ES256", "CN=Different Root CA");
        verifier.AddTrustedRoot(X509CertificateLoader.LoadCertificate(differentRootDer));

        var result = await verifier.VerifyAsync(
            presentation,
            expectedNonce: "test-nonce-2",
            expectedAudience: "https://verifier.example.com");

        // Key resolution succeeds (from leaf cert), but chain validation fails
        result.X5cChainValid.Should().BeFalse();
        // The SD-JWT itself still verifies because the key is correct
        // Chain validity is reported separately from signature validity
    }

    [Fact]
    public async Task Verify_WithX5cChain_WrongNonce_Fails()
    {
        var verifier = CreateVerifier();
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "correct-nonce");

        verifier.AddTrustedRoot(X509CertificateLoader.LoadCertificate(rootCertDer));

        var result = await verifier.VerifyAsync(
            presentation,
            expectedNonce: "wrong-nonce",
            expectedAudience: "https://verifier.example.com");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Nonce mismatch"));
    }

    [Fact]
    public async Task Verify_WithX5cChain_RequiredClaimMissing_Fails()
    {
        var verifier = CreateVerifier();
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-3");

        verifier.AddTrustedRoot(X509CertificateLoader.LoadCertificate(rootCertDer));

        var result = await verifier.VerifyAsync(
            presentation,
            expectedNonce: "test-nonce-3",
            expectedAudience: "https://verifier.example.com",
            requiredClaims: ["licenseType", "nonExistentClaim"]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("nonExistentClaim"));
    }
}
