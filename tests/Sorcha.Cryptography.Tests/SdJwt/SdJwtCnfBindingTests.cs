// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// Tests for cnf (confirmation) holder key binding at issuance (FR-001 through FR-006).
/// </summary>
public class SdJwtCnfBindingTests
{
    private readonly SdJwtService _service = new();

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
            ["x"] = Base64UrlEncode(parameters.Q.X!),
            ["y"] = Base64UrlEncode(parameters.Q.Y!)
        };
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task CreateTokenAsync_WithHolderJwk_EmbedsCnfClaim()
    {
        // Arrange
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["licenseType"] = "A"
        };

        // Act
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name"],
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Assert — cnf must be in the payload with jwk sub-claim
        token.Payload.Should().ContainKey("cnf");
        var cnfJson = JsonSerializer.Serialize(token.Payload["cnf"]);
        var cnfElement = JsonSerializer.Deserialize<JsonElement>(cnfJson);
        cnfElement.TryGetProperty("jwk", out var jwkElement).Should().BeTrue();
        jwkElement.TryGetProperty("kty", out var kty).Should().BeTrue();
        kty.GetString().Should().Be("EC");
    }

    [Fact]
    public async Task CreateTokenAsync_WithoutHolderJwk_EmitsNoCnf()
    {
        // Arrange
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        // Act — no holderJwk parameter (legacy path)
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivate,
            algorithm: "ES256");

        // Assert — no cnf claim (backward compat, FR-006)
        token.Payload.Should().NotContainKey("cnf");
    }

    [Fact]
    public async Task CreateTokenAsync_CnfIsNonDisclosable_NotInSdArray()
    {
        // Arrange (FR-003: cnf must NOT be selectively disclosable)
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30
        };

        // Act — all user claims disclosable, but cnf should stay visible
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Assert — cnf in payload directly (non-disclosable), not hidden behind _sd
        token.Payload.Should().ContainKey("cnf");

        // cnf should NOT appear in any disclosure
        foreach (var disclosure in token.Disclosures)
        {
            var decoded = System.Buffers.Text.Base64Url.DecodeFromChars(disclosure);
            var disclosureText = System.Text.Encoding.UTF8.GetString(decoded);
            disclosureText.Should().NotContain("\"cnf\"",
                because: "cnf must be non-disclosable per FR-003");
        }
    }

    [Fact]
    public async Task VerifyTokenAsync_WithCnf_ExtractsCnfClaim()
    {
        // Arrange
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Act
        var result = await _service.VerifyTokenAsync(token.RawToken, issuerPublic, "ES256");

        // Assert — cnf is extracted on the verification result
        result.IsValid.Should().BeTrue();
        result.CnfJwk.Should().NotBeNull("verification must surface the cnf JWK");
        var cnfStr = result.CnfJwk!.Value.GetRawText();
        cnfStr.Should().Contain("P-256");
    }

    [Fact]
    public async Task VerifyTokenAsync_WithoutCnf_CnfJwkIsNull()
    {
        // Arrange — legacy credential with no holder binding
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:org:issuer1",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivate,
            algorithm: "ES256");

        // Act
        var result = await _service.VerifyTokenAsync(token.RawToken, issuerPublic, "ES256");

        // Assert
        result.IsValid.Should().BeTrue();
        result.CnfJwk.Should().BeNull();
    }

    [Fact]
    public async Task CreateTokenAsync_CnfSurvivesRoundTrip_VerifierSeesHolderKey()
    {
        // Full round-trip: issue with cnf → verify → confirm holder key present
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["licenseType"] = "ClassA"
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name", "licenseType"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        var result = await _service.VerifyTokenAsync(token.RawToken, issuerPublic, "ES256");

        result.IsValid.Should().BeTrue();
        result.Claims.Should().ContainKey("name");
        result.Claims.Should().ContainKey("licenseType");
        result.CnfJwk.Should().NotBeNull();

        // The cnf JWK should match the holder public key we provided
        var resultJwk = result.CnfJwk!.Value;
        resultJwk.GetProperty("crv").GetString().Should().Be("P-256");
        resultJwk.GetProperty("kty").GetString().Should().Be("EC");
    }
}
