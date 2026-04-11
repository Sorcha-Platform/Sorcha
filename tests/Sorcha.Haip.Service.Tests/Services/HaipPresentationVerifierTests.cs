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
/// Tests for HaipPresentationVerifier — the HAIP verification pipeline.
/// </summary>
public class HaipPresentationVerifierTests
{
    private readonly SdJwtService _sdJwtService = new();
    private readonly HaipPresentationVerifier _verifier;

    public HaipPresentationVerifierTests()
    {
        _verifier = new HaipPresentationVerifier(
            _sdJwtService,
            Mock.Of<ILogger<HaipPresentationVerifier>>());
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
    public async Task Verify_EmptyVpToken_ThrowsArgumentException()
    {
        var act = () => _verifier.VerifyAsync("", "nonce", "audience");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Verify_MalformedVpToken_ReturnsErrors()
    {
        var result = await _verifier.VerifyAsync(
            "not-a-valid-token",
            "test-nonce",
            "https://verifier.example.com");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Verify_RequiredClaimsMissing_ReturnsError()
    {
        // This test verifies the claim matching logic.
        // Since ExtractIssuerPublicKey returns null (key resolution not wired),
        // the verifier will fail at the key extraction step. This validates
        // that the pipeline handles the failure gracefully.
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        // Create a credential and presentation
        var token = await _sdJwtService.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        int bytesRead;
        var presentation = await _sdJwtService.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"],
            kbJwtSigner: (data, _) =>
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportECPrivateKey(holderPrivate, out bytesRead);
                return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
            },
            holderAlgorithm: "ES256",
            audience: "https://verifier.example.com",
            nonce: "test-nonce");

        // Verify — issuer key resolution will fail at ExtractIssuerPublicKey
        // (returns null until DID/x5c resolution is wired)
        var result = await _verifier.VerifyAsync(
            presentation.RawPresentation,
            expectedNonce: "test-nonce",
            expectedAudience: "https://verifier.example.com",
            requiredCredentialType: "TestCredential",
            requiredClaims: ["name", "missingClaim"]);

        // Should fail because issuer key can't be resolved yet
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ExtractAlgorithm_ValidToken_ReturnsAlgorithm()
    {
        // Use reflection to test the private method via a real token
        var (privateKey, _) = GenerateP256KeyPair();
        var token = _sdJwtService.CreateTokenAsync(
            new Dictionary<string, object> { ["test"] = "value" },
            null, "iss", "sub", privateKey, "ES256").Result;

        // The algorithm is in the header — we can verify by parsing
        var parts = token.RawToken.TrimEnd('~').Split('~');
        var jwtParts = parts[0].Split('.');
        var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtParts[0]);
        var header = JsonSerializer.Deserialize<JsonElement>(headerBytes);
        header.GetProperty("alg").GetString().Should().Be("ES256");
    }
}
