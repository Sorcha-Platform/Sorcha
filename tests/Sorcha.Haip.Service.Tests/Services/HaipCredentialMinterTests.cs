// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Tests for HaipCredentialMinter — SD-JWT VC minting with cnf binding.
/// </summary>
public class HaipCredentialMinterTests
{
    private readonly HaipCredentialMinter _minter;
    private readonly SdJwtService _sdJwtService = new();

    public HaipCredentialMinterTests()
    {
        _minter = new HaipCredentialMinter(
            _sdJwtService,
            Mock.Of<ILogger<HaipCredentialMinter>>());
    }

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

    [Fact]
    public async Task MintCredential_ReturnsSdJwtVcWithCnf()
    {
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object>
        {
            ["licenseType"] = "ClassA",
            ["holder"] = "Alice"
        };

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            holderJwk,
            "LicenseCredential",
            claims,
            disclosablePaths: ["licenseType", "holder"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        rawToken.Should().NotBeNullOrWhiteSpace();
        rawToken.Should().Contain("~"); // SD-JWT format

        // Verify the token has cnf
        var (_, issuerPublic) = GenerateP256KeyPair(); // Can't verify with this key, but parse
        // Parse the JWT payload to check cnf
        var parts = rawToken.TrimEnd('~').Split('~');
        var jwtParts = parts[0].Split('.');
        var payloadBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtParts[1]);
        var payload = JsonSerializer.Deserialize<JsonElement>(payloadBytes);

        payload.TryGetProperty("cnf", out var cnf).Should().BeTrue("credential must have cnf");
        cnf.TryGetProperty("jwk", out _).Should().BeTrue("cnf must contain jwk");
    }

    [Fact]
    public async Task MintCredential_WithDisclosables_ProducesDisclosures()
    {
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["license"] = "A",
            ["publicField"] = "visible"
        };

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:issuer1",
            holderJwk,
            "LicenseCredential",
            claims,
            disclosablePaths: ["name", "license"],
            signingKey: issuerPrivate,
            algorithm: "ES256");

        // Verify: should have disclosures
        var result = await _sdJwtService.VerifyTokenAsync(rawToken, issuerPublic, "ES256");
        result.IsValid.Should().BeTrue();
        result.Claims.Should().ContainKey("name");
        result.Claims.Should().ContainKey("license");
        result.Claims.Should().ContainKey("publicField");
        result.CnfJwk.Should().NotBeNull();
    }

    [Fact]
    public async Task MintCredential_FullRoundTrip_IssueAndVerify()
    {
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var rawToken = await _minter.MintCredentialAsync(
            "did:sorcha:org:gov",
            holderJwk,
            "ProfessionalLicense",
            new Dictionary<string, object>
            {
                ["name"] = "Alice O'Brien",
                ["licenseNumber"] = "LIC-12345",
                ["councilArea"] = "Dublin City"
            },
            disclosablePaths: ["name", "licenseNumber", "councilArea"],
            signingKey: issuerPrivate,
            algorithm: "ES256",
            expiresAt: DateTimeOffset.UtcNow.AddYears(1));

        // Verify
        var verifyResult = await _sdJwtService.VerifyTokenAsync(rawToken, issuerPublic, "ES256");
        verifyResult.IsValid.Should().BeTrue();
        verifyResult.Issuer.Should().Be("did:sorcha:org:gov");
        verifyResult.CnfJwk.Should().NotBeNull();
        verifyResult.ExpiresAt.Should().NotBeNull();

        // Create a presentation with KB-JWT
        int bytesRead;
        Func<byte[], CancellationToken, Task<byte[]>> signer = (data, _) =>
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            ecdsa.ImportECPrivateKey(holderPrivate, out bytesRead);
            return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
        };

        var presentation = await _sdJwtService.CreatePresentationAsync(
            rawToken,
            claimsToDisclose: ["licenseNumber", "councilArea"],
            kbJwtSigner: signer,
            holderAlgorithm: "ES256",
            audience: "https://booking-platform.example.com",
            nonce: "verification-nonce-1");

        // Verify the presentation
        var presResult = await _sdJwtService.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://booking-platform.example.com",
            expectedNonce: "verification-nonce-1");

        presResult.IsValid.Should().BeTrue();
        presResult.HolderKeyVerified.Should().BeTrue();
        presResult.Claims.Should().ContainKey("licenseNumber");
        presResult.Claims.Should().ContainKey("councilArea");
        presResult.Claims.Should().NotContainKey("name"); // Not disclosed
    }
}
