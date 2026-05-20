// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Haip.Service.Tests.Services;

/// <summary>
/// Feature 135 — HaipPresentationVerifier now routes its trust decision through the unified
/// ITrustEvaluator. The verifier still owns SD-JWT signature + KB verification; the x5c chain is
/// validated by the x509-tenant trust source against the supplied anchors, and (unlike the prior
/// advisory-only behaviour) an untrusted chain now actually fails the decision.
/// </summary>
public class HaipPresentationVerifierTests
{
    private readonly SdJwtService _sdJwtService = new();

    // --- test seams -----------------------------------------------------------

    private sealed class FakeAnchors(TrustAnchorSet? set) : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default) => Task.FromResult(set);
    }

    private sealed class FakeDirectory(bool resolves) : IIssuerDirectory
    {
        public Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken ct = default) =>
            Task.FromResult(new IssuerDirectoryEntry { Resolved = resolves });
    }

    /// <summary>
    /// Builds a verifier whose evaluator trusts an x5c chain to <paramref name="trustedRootDer"/>
    /// (when supplied) via the x509-tenant source. The register/did-allowlist sources resolve the
    /// issuer only when <paramref name="resolveIssuer"/> is true.
    /// </summary>
    private HaipPresentationVerifier CreateVerifier(byte[]? trustedRootDer = null, bool resolveIssuer = false)
    {
        var anchors = trustedRootDer is null
            ? null
            : new TrustAnchorSet { Roots = [trustedRootDer], CheckRevocation = false };
        var directory = new FakeDirectory(resolveIssuer);
        var registry = new TrustResolverRegistry(new ITrustSourceResolver[]
        {
            new X509TenantTrustSourceResolver(new FakeAnchors(anchors)),
            new RegisterTrustSourceResolver(directory),
            new DidAllowlistTrustSourceResolver(directory)
        });
        var evaluator = new TrustEvaluator(registry, statusChecker: null);
        return new HaipPresentationVerifier(_sdJwtService, evaluator, Mock.Of<ILogger<HaipPresentationVerifier>>());
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

    /// <summary>
    /// Creates an SD-JWT VC presentation with an x5c chain (org cert under a test root) and a KB-JWT,
    /// returning the presentation plus the root cert DER for the trust anchor.
    /// </summary>
    private async Task<(string presentation, byte[] rootCertDer)> CreatePresentationWithX5cAsync(
        string audience, string nonce)
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root CA");

        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, issuerPublicKey, "CN=Test Org", "did:sorcha:org:ws1qtest");

        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var token = await _sdJwtService.CreateTokenAsync(
            new Dictionary<string, object> { ["licenseType"] = "ClassA", ["holder"] = "Alice" },
            disclosableClaims: ["licenseType", "holder"],
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivateKey,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Inject the x5c chain into the header and re-sign (ISdJwtService x5c support is exercised
        // separately; this mirrors what the credential endpoint produces).
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

        var presentation = await _sdJwtService.CreatePresentationAsync(
            newRawToken,
            claimsToDisclose: ["licenseType"],
            kbJwtSigner: (data, _token) =>
            {
                using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                ecdsa.ImportECPrivateKey(holderPrivate, out _);
                return Task.FromResult(ecdsa.SignData(data, HashAlgorithmName.SHA256));
            },
            holderAlgorithm: "ES256",
            audience: audience,
            nonce: nonce);

        return (presentation.RawPresentation, rootCertDer);
    }

    // --- constructor guards ---------------------------------------------------

    [Fact]
    public void Constructor_NullTrustEvaluator_Throws()
    {
        var act = () => new HaipPresentationVerifier(_sdJwtService, null!, Mock.Of<ILogger<HaipPresentationVerifier>>());
        act.Should().Throw<ArgumentNullException>();
    }

    // --- pipeline -------------------------------------------------------------

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
    public async Task Verify_WithX5cChain_ToTrustedRoot_VerifiesAndTrusts()
    {
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-1");
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "test-nonce-1", expectedAudience: "https://verifier.example.com");

        result.IsValid.Should().BeTrue();
        result.HolderKeyVerified.Should().BeTrue();
        result.X5cChainValid.Should().BeTrue();
        result.VerifiedClaims.Should().ContainKey("licenseType");
        result.Issuer.Should().Be("did:sorcha:org:ws1qtest");
        result.TrustEvidence.Should().NotBeNull();
        result.TrustEvidence!.VouchingSource.Should().Be(TrustSourceKind.X509Tenant);
    }

    [Fact]
    public async Task Verify_WithX5cChain_UntrustedRoot_RejectedAndChainInvalid()
    {
        var (presentation, _) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-2");
        var (differentRootDer, _, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Different Root CA");
        // Anchor is a different root; the issuer does not resolve in the directory either.
        var verifier = CreateVerifier(trustedRootDer: differentRootDer, resolveIssuer: false);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "test-nonce-2", expectedAudience: "https://verifier.example.com");

        // The signature is fine (key is the leaf cert), but no trust source vouches.
        result.IsValid.Should().BeFalse();
        result.X5cChainValid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_WithX5cChain_WrongNonce_Fails()
    {
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "correct-nonce");
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "wrong-nonce", expectedAudience: "https://verifier.example.com");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Nonce mismatch"));
    }

    [Fact]
    public async Task Verify_WithX5cChain_RequiredClaimMissing_Fails()
    {
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "test-nonce-3");
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation,
            expectedNonce: "test-nonce-3",
            expectedAudience: "https://verifier.example.com",
            requiredClaims: ["licenseType", "nonExistentClaim"]);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("nonExistentClaim"));
    }
}
