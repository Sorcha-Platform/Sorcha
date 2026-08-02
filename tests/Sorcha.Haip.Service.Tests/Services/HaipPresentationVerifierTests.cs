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
        string audience, string nonce, string? vct = null)
    {
        var (rootCertDer, rootPrivateKey, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root CA");

        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootCertDer, rootPrivateKey, issuerPublicKey, "CN=Test Org", "did:sorcha:org:ws1qtest");

        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var claims = new Dictionary<string, object> { ["licenseType"] = "ClassA", ["holder"] = "Alice" };
        if (vct is not null)
        {
            // vct is the SD-JWT VC type identifier: a plain payload claim, never selectively
            // disclosable, so it is always present for the verifier to gate on.
            claims["vct"] = vct;
        }

        var token = await _sdJwtService.CreateTokenAsync(
            claims,
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

    // --- vct gating (issue #1198) ---------------------------------------------
    //
    // VerifyAsync accepted `requiredCredentialType` and never read it. The only real match gates
    // were the object-keyed envelope id, required-CLAIM presence, and issuer trust — so a holder
    // could present a credential of an entirely DIFFERENT type and pass, provided it came from a
    // trusted issuer and disclosed claims with the right NAMES. Claim-name overlap across types
    // (givenName, dateOfBirth, holder, …) makes that reachable, not theoretical: it weakens
    // "prove you hold THIS KIND of credential" to "prove you hold SOME trusted credential with
    // these field names".
    //
    // OpenID4VP/DCQL carries the requirement as `meta.vct_values`, a SET of acceptable URIs; a
    // conformant verifier rejects a presentation whose vct is outside it.

    private const string LicenceVct = "https://sorcha.example/vc/driving-licence/v1";
    private const string IdentityVct = "https://sorcha.example/vc/assured-identity/v1";

    [Fact]
    public async Task Verify_VctMatchesRequirement_Succeeds()
    {
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-1", vct: LicenceVct);
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-1", expectedAudience: "https://verifier.example.com",
            requiredCredentialType: LicenceVct);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_VctDoesNotMatchRequirement_Fails()
    {
        // A perfectly valid, trusted-issuer credential — of the WRONG TYPE.
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-2", vct: IdentityVct);
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-2", expectedAudience: "https://verifier.example.com",
            requiredCredentialType: LicenceVct);

        result.IsValid.Should().BeFalse(
            "a credential whose vct is outside the requested set must be rejected, however trusted its issuer");
        result.Errors.Should().Contain(e => e.Contains("vct", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Verify_VctDiffersOnlyByCase_Fails()
    {
        // vct matching is case-SENSITIVE (Ordinal) platform-wide since #1187 — the vct is a URI and
        // an exact identifier, not a label.
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-3", vct: LicenceVct.ToUpperInvariant());
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-3", expectedAudience: "https://verifier.example.com",
            requiredCredentialType: LicenceVct);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Verify_VctMatchesOneOfSeveralAcceptedValues_Succeeds()
    {
        // DCQL meta.vct_values is a SET. Gating on only the first would falsely reject a holder
        // presenting a legitimate alternative — trading the missing gate for a new bug.
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-4", vct: IdentityVct);
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-4", expectedAudience: "https://verifier.example.com",
            acceptedVctValues: [LicenceVct, IdentityVct]);

        result.IsValid.Should().BeTrue("presenting any one of the accepted vct values satisfies the ask");
    }

    [Fact]
    public async Task Verify_NoVctRequirement_DoesNotGate()
    {
        // Callers that ask for no particular type keep working — the gate is opt-in, so this does
        // not become a breaking change for a request that never declared meta.vct_values.
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-5", vct: IdentityVct);
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-5", expectedAudience: "https://verifier.example.com");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Verify_RequiredVctButCredentialCarriesNone_Fails()
    {
        // Fail closed: a credential with no vct cannot demonstrate it is of the requested type.
        var (presentation, rootCertDer) = await CreatePresentationWithX5cAsync(
            "https://verifier.example.com", "vct-nonce-6", vct: null);
        var verifier = CreateVerifier(trustedRootDer: rootCertDer);

        var result = await verifier.VerifyAsync(
            presentation, expectedNonce: "vct-nonce-6", expectedAudience: "https://verifier.example.com",
            requiredCredentialType: LicenceVct);

        result.IsValid.Should().BeFalse();
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
