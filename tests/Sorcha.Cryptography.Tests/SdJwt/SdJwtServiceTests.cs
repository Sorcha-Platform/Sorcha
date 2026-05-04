// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

public class SdJwtServiceTests
{
    private readonly SdJwtService _service = new();

    // Helper: generate P-256 key pair for testing (no native deps required)
    private static (byte[] privateKey, byte[] publicKey) GenerateP256KeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportECPrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        return (privateKey, publicKey);
    }

    [Fact]
    public async Task CreateTokenAsync_ValidClaims_ReturnsTokenWithDisclosures()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["licenseType"] = "A"
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null, // all claims disclosable
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        token.Should().NotBeNull();
        token.RawToken.Should().NotBeNullOrWhiteSpace();
        token.Disclosures.Should().HaveCount(3); // name, age, licenseType
        token.Signature.Should().NotBeNullOrWhiteSpace();
        token.Header.Should().ContainKey("alg");
        token.Header["alg"].Should().Be("ES256");
        token.Payload.Should().ContainKey("iss");
        token.Payload.Should().ContainKey("sub");
        token.Payload.Should().ContainKey("_sd");
        token.Payload.Should().ContainKey("_sd_alg");
    }

    [Fact]
    public async Task CreateTokenAsync_PartialDisclosable_NonDisclosableClaimsInPayload()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["publicField"] = "visible"
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name"], // only name is disclosable
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        token.Disclosures.Should().HaveCount(1); // only name
        token.Payload.Should().ContainKey("publicField");
        token.Payload["publicField"].Should().Be("visible");
    }

    [Fact]
    public async Task CreateTokenAsync_WithExpiry_IncludesExpClaim()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var expiry = DateTimeOffset.UtcNow.AddDays(365);

        var token = await _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256",
            expiresAt: expiry);

        token.Payload.Should().ContainKey("exp");
    }

    [Fact]
    public async Task VerifyTokenAsync_ValidToken_ReturnsValidResult()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var result = await _service.VerifyTokenAsync(
            token.RawToken,
            publicKey,
            "ES256");

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Issuer.Should().Be("did:sorcha:issuer:gov");
        result.Subject.Should().Be("did:sorcha:subject:alice");
        result.Claims.Should().ContainKey("name");
        result.Claims["name"].Should().Be("Alice");
        result.Claims.Should().ContainKey("age");
        result.IssuedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyTokenAsync_WrongPublicKey_ReturnsInvalidSignature()
    {
        var (privateKey, _) = GenerateP256KeyPair();
        var (_, wrongPublicKey) = GenerateP256KeyPair(); // different key pair
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var result = await _service.VerifyTokenAsync(
            token.RawToken,
            wrongPublicKey,
            "ES256");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid signature"));
        // Issue #221 item 2: verify the typed ErrorKind populates in lock-step with the
        // string list. Consumers can branch on Kind without substring-matching Message.
        result.ErrorDetails.Should().ContainSingle(e => e.Kind == SdJwtErrorKind.SignatureInvalid)
            .Which.Message.Should().Contain("Invalid signature");
    }

    [Fact]
    public async Task VerifyTokenAsync_MalformedJwtSegments_ReportsInvalidFormat()
    {
        // Issue #221 item 2: structural-error coverage — token with wrong segment count
        // routes through the InvalidFormat branch.
        var (_, publicKey) = GenerateP256KeyPair();

        var result = await _service.VerifyTokenAsync(
            "not.a.valid.jwt.with.too.many.segments",
            publicKey,
            "ES256");

        result.IsValid.Should().BeFalse();
        result.ErrorDetails.Should().Contain(e => e.Kind == SdJwtErrorKind.InvalidFormat);
    }

    [Fact]
    public async Task VerifyTokenAsync_ErrorDetailsParallelToErrors()
    {
        // Issue #221 item 2: backward-compat invariant — for every entry in Errors there
        // is a corresponding entry in ErrorDetails with the same Message text.
        var (_, publicKey) = GenerateP256KeyPair();

        var result = await _service.VerifyTokenAsync(
            "not.a.valid.jwt.with.too.many.segments",
            publicKey,
            "ES256");

        result.Errors.Should().NotBeEmpty();
        result.ErrorDetails.Should().HaveCount(result.Errors.Count,
            "ErrorDetails and Errors must populate in lock-step for backward compat");
        result.ErrorDetails.Select(e => e.Message)
            .Should().BeEquivalentTo(result.Errors,
                "every Errors entry has a matching ErrorDetails entry with the same Message");
    }

    [Fact]
    public async Task VerifyTokenAsync_TamperedPayload_ReturnsInvalid()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        // Tamper with the payload by modifying the raw token
        var parts = token.RawToken.TrimEnd('~').Split('~');
        var jwtParts = parts[0].Split('.');
        // Replace payload with a different base64url payload
        var tampered = "eyJpc3MiOiJmYWtlIn0"; // {"iss":"fake"}
        var tamperedJwt = $"{jwtParts[0]}.{tampered}.{jwtParts[2]}";
        var tamperedToken = tamperedJwt + "~" + string.Join("~", parts[1..]) + "~";

        var result = await _service.VerifyTokenAsync(
            tamperedToken,
            publicKey,
            "ES256");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyTokenAsync_InvalidFormat_ReturnsError()
    {
        var (_, publicKey) = GenerateP256KeyPair();

        var result = await _service.VerifyTokenAsync(
            "not-a-valid-token",
            publicKey,
            "ES256");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePresentationAsync_SelectSubsetOfClaims_OnlyIncludesSelected()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30,
            ["email"] = "alice@example.com"
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name", "age"]); // exclude email

        presentation.Should().NotBeNull();
        presentation.RawPresentation.Should().NotBeNullOrWhiteSpace();
        presentation.SelectedDisclosures.Should().HaveCount(2); // name and age only

        // Verify the presentation reveals only selected claims
        var verifyResult = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            publicKey,
            "ES256");

        verifyResult.IsValid.Should().BeTrue();
        verifyResult.Claims.Should().ContainKey("name");
        verifyResult.Claims.Should().ContainKey("age");
        verifyResult.Claims.Should().NotContainKey("email"); // not disclosed
    }

    [Fact]
    public async Task CreatePresentationAsync_SelectSingleClaim_OnlyIncludesThatClaim()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30
        };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256");

        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["age"]); // only age

        var verifyResult = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            publicKey,
            "ES256");

        verifyResult.IsValid.Should().BeTrue();
        verifyResult.Claims.Should().NotContainKey("name");
        verifyResult.Claims.Should().ContainKey("age");
    }

    [Fact]
    public async Task RoundTrip_CreateVerifyPresentVerify_FullFlow()
    {
        var (privateKey, publicKey) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["licenseType"] = "ClassA",
            ["issuedCountry"] = "Ireland"
        };

        // Step 1: Create token
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:issuer:gov",
            subject: "did:sorcha:subject:alice",
            signingKey: privateKey,
            algorithm: "ES256",
            expiresAt: DateTimeOffset.UtcNow.AddDays(365));

        // Step 2: Verify full token
        var fullVerify = await _service.VerifyTokenAsync(token.RawToken, publicKey, "ES256");
        fullVerify.IsValid.Should().BeTrue();
        fullVerify.Claims.Should().HaveCount(3);

        // Step 3: Create selective presentation
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["licenseType"]);

        // Step 4: Verify presentation
        var presentVerify = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            publicKey,
            "ES256");

        presentVerify.IsValid.Should().BeTrue();
        presentVerify.Claims.Should().ContainKey("licenseType");
        presentVerify.Claims["licenseType"].Should().Be("ClassA");
        presentVerify.Claims.Should().NotContainKey("name");
        presentVerify.Claims.Should().NotContainKey("issuedCountry");
        presentVerify.Issuer.Should().Be("did:sorcha:issuer:gov");
        presentVerify.ExpiresAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateTokenAsync_NullClaims_ThrowsArgumentNullException()
    {
        var (privateKey, _) = GenerateP256KeyPair();

        var act = () => _service.CreateTokenAsync(
            null!,
            null,
            "did:sorcha:issuer:gov",
            "did:sorcha:subject:alice",
            privateKey,
            "ES256");

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task CreateTokenAsync_EmptyIssuer_ThrowsArgumentException()
    {
        var (privateKey, _) = GenerateP256KeyPair();

        var act = () => _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            null,
            "",
            "did:sorcha:subject:alice",
            privateKey,
            "ES256");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task VerifyTokenAsync_EmptyToken_ThrowsArgumentException()
    {
        var (_, publicKey) = GenerateP256KeyPair();

        var act = () => _service.VerifyTokenAsync("", publicKey, "ES256");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateTokenAsync_UnsupportedAlgorithm_ThrowsNotSupportedException()
    {
        var (privateKey, _) = GenerateP256KeyPair();

        var act = () => _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            null,
            "did:sorcha:issuer:gov",
            "did:sorcha:subject:alice",
            privateKey,
            "UNSUPPORTED-ALG");

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
