// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Cryptography.SdJwt;
using Xunit;

namespace Sorcha.Cryptography.Tests.SdJwt;

/// <summary>
/// Tests for Key Binding JWT (KB-JWT) creation and verification (FR-007 through FR-014).
/// </summary>
public class SdJwtKeyBindingJwtTests
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

    /// <summary>
    /// Creates a signing delegate that signs with the given P-256 private key.
    /// </summary>
    private static Func<byte[], CancellationToken, Task<byte[]>> CreateP256Signer(byte[] privateKey)
    {
        return (data, _) =>
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            int bytesRead;
            ecdsa.ImportECPrivateKey(privateKey, out bytesRead);
            return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
        };
    }

    /// <summary>
    /// Helper to issue a credential with cnf and create a presentation with KB-JWT.
    /// </summary>
    private async Task<(SdJwtToken token, SdJwtPresentation presentation, byte[] issuerPublic)>
        IssueAndPresentAsync(string audience = "https://verifier.example.com", string nonce = "test-nonce-123")
    {
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
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

        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"],
            kbJwtSigner: CreateP256Signer(holderPrivate),
            holderAlgorithm: "ES256",
            audience: audience,
            nonce: nonce);

        return (token, presentation, issuerPublic);
    }

    [Fact]
    public async Task CreatePresentation_WithCnf_AppendsKbJwt()
    {
        var (_, presentation, _) = await IssueAndPresentAsync();

        presentation.KeyBindingJwt.Should().NotBeNullOrWhiteSpace();
        presentation.RawPresentation.Should().EndWith(presentation.KeyBindingJwt);

        // KB-JWT should be a valid JWT structure (3 dot-separated segments)
        var kbSegments = presentation.KeyBindingJwt!.Split('.');
        kbSegments.Should().HaveCount(3);

        // Parse the KB-JWT header — should have typ=kb+jwt
        var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(kbSegments[0]);
        var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(headerBytes)!;
        header["typ"].GetString().Should().Be("kb+jwt");
        header["alg"].GetString().Should().Be("ES256");
    }

    [Fact]
    public async Task CreatePresentation_WithoutCnf_OmitsKbJwt_LegacyPath()
    {
        // Issue without holderJwk (legacy credential)
        var (issuerPrivate, _) = GenerateP256KeyPair();
        var claims = new Dictionary<string, object> { ["name"] = "Alice" };

        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: null,
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256");

        // Use the legacy CreatePresentationAsync (no KB-JWT signer)
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"]);

        presentation.KeyBindingJwt.Should().BeNull();
        presentation.RawPresentation.Should().EndWith("~");
    }

    [Fact]
    public async Task VerifyPresentation_ValidKbJwt_HolderKeyVerifiedTrue()
    {
        var (_, presentation, issuerPublic) = await IssueAndPresentAsync(
            audience: "https://verifier.example.com",
            nonce: "verify-nonce-1");

        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "verify-nonce-1");

        result.IsValid.Should().BeTrue();
        result.HolderKeyVerified.Should().BeTrue();
        result.Claims.Should().ContainKey("name");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyPresentation_KbJwtAudienceMismatch_Fails()
    {
        var (_, presentation, issuerPublic) = await IssueAndPresentAsync(
            audience: "https://verifier-a.example.com",
            nonce: "nonce-1");

        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier-b.example.com", // wrong audience
            expectedNonce: "nonce-1");

        result.IsValid.Should().BeFalse();
        result.HolderKeyVerified.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Audience mismatch"));
    }

    [Fact]
    public async Task VerifyPresentation_KbJwtNonceMismatch_Fails()
    {
        var (_, presentation, issuerPublic) = await IssueAndPresentAsync(
            audience: "https://verifier.example.com",
            nonce: "nonce-1");

        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "wrong-nonce"); // wrong nonce

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Nonce mismatch"));
    }

    [Fact]
    public async Task VerifyPresentation_CnfPresentButNoKbJwt_Fails()
    {
        // Issue a credential with cnf, but present without KB-JWT (legacy path)
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var token = await _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Use legacy CreatePresentationAsync (no KB-JWT)
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"]);

        // Verify with audience/nonce — should fail because cnf present but no KB-JWT
        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "test-nonce");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Missing KB-JWT"));
    }

    [Fact]
    public async Task VerifyPresentation_KbJwtSignedByWrongKey_Fails()
    {
        // Issue credential bound to holderA's key, but sign KB-JWT with holderB's key
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (_, holderAPublic) = GenerateP256KeyPair();
        var (holderBPrivate, _) = GenerateP256KeyPair();
        var holderAJwk = CreateHolderJwk(holderAPublic);

        var token = await _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderAJwk); // bound to holderA

        // Present with holderB's key (wrong key)
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"],
            kbJwtSigner: CreateP256Signer(holderBPrivate), // signed by holderB
            holderAlgorithm: "ES256",
            audience: "https://verifier.example.com",
            nonce: "test-nonce");

        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "test-nonce");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Key binding mismatch"));
    }

    [Fact]
    public async Task VerifyPresentation_SdHashMismatch_Fails()
    {
        var (_, presentation, issuerPublic) = await IssueAndPresentAsync(
            audience: "https://verifier.example.com",
            nonce: "test-nonce");

        // Tamper with the presentation by replacing a disclosure, invalidating sd_hash
        var parts = presentation.RawPresentation.Split('~');
        // The KB-JWT is the last element, find a disclosure and modify it
        // Just prepend a character to the first disclosure
        if (parts.Length > 2)
        {
            parts[1] = "AAAA" + parts[1]; // tamper with a disclosure
        }
        var tamperedPresentation = string.Join("~", parts);

        var result = await _service.VerifyPresentationAsync(
            tamperedPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "test-nonce");

        // Should fail — either issuer signature or sd_hash mismatch
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPresentation_NoCnf_IgnoresTrailingKbJwtIfPresent()
    {
        // Legacy credential without cnf — if a KB-JWT somehow appears, just ignore it
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (holderPrivate, _) = GenerateP256KeyPair();

        var token = await _service.CreateTokenAsync(
            new Dictionary<string, object> { ["name"] = "Alice" },
            disclosableClaims: null,
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256"); // no holderJwk

        // Present with KB-JWT even though credential has no cnf
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["name"],
            kbJwtSigner: CreateP256Signer(holderPrivate),
            holderAlgorithm: "ES256",
            audience: "https://verifier.example.com",
            nonce: "test-nonce");

        // Verify with the new overload — should succeed since no cnf means KB-JWT is optional
        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://verifier.example.com",
            expectedNonce: "test-nonce");

        result.IsValid.Should().BeTrue();
        result.HolderKeyVerified.Should().BeFalse(); // no cnf, so holder not verified
    }

    [Fact]
    public async Task FullRoundTrip_IssueWithCnf_PresentWithKbJwt_VerifySucceeds()
    {
        // End-to-end: issue → present with selective disclosure + KB-JWT → verify
        var (issuerPrivate, issuerPublic) = GenerateP256KeyPair();
        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object>
        {
            ["name"] = "Alice O'Brien",
            ["licenseType"] = "ClassA",
            ["issuedCountry"] = "Ireland",
            ["licenseNumber"] = "LIC-12345"
        };

        // Issue with cnf
        var token = await _service.CreateTokenAsync(
            claims,
            disclosableClaims: ["name", "licenseType", "issuedCountry", "licenseNumber"],
            issuer: "did:sorcha:org:gov",
            subject: "did:sorcha:w:alice",
            signingKey: issuerPrivate,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Present — disclose only licenseType and issuedCountry
        var presentation = await _service.CreatePresentationAsync(
            token.RawToken,
            claimsToDisclose: ["licenseType", "issuedCountry"],
            kbJwtSigner: CreateP256Signer(holderPrivate),
            holderAlgorithm: "ES256",
            audience: "https://booking-platform.example.com",
            nonce: "verification-session-abc");

        // Verify — must see only disclosed claims and holder key verified
        var result = await _service.VerifyPresentationAsync(
            presentation.RawPresentation,
            issuerPublic,
            "ES256",
            expectedAudience: "https://booking-platform.example.com",
            expectedNonce: "verification-session-abc");

        result.IsValid.Should().BeTrue();
        result.HolderKeyVerified.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.Issuer.Should().Be("did:sorcha:org:gov");
        result.Claims.Should().ContainKey("licenseType").WhoseValue.Should().Be("ClassA");
        result.Claims.Should().ContainKey("issuedCountry").WhoseValue.Should().Be("Ireland");
        result.Claims.Should().NotContainKey("name");
        result.Claims.Should().NotContainKey("licenseNumber");
        result.CnfJwk.Should().NotBeNull();
    }
}
