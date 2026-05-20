// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Services;
using Sorcha.ServiceClients.Did;
using Xunit;

namespace Sorcha.Haip.Service.Tests;

/// <summary>
/// Feature 135 / T019 (SC-001) — one trust decision for every path. The same SD-JWT VC, evaluated
/// against the same did-allowlist policy, yields the same accept/reject and the same evidence shape
/// whether it goes through the internal engine <see cref="SdJwtVcFormatHandler"/> path or the HAIP
/// <see cref="HaipPresentationVerifier"/> path — because both consult the one
/// <see cref="ITrustEvaluator"/>. A tampered signature is rejected on both.
/// </summary>
public class CrossPathParityTests
{
    private const string Issuer = "did:sorcha:org:parity";
    private const string Kid = "did:sorcha:org:parity#key-1";
    private readonly SdJwtService _sdJwt = new();

    // --- seams ----------------------------------------------------------------

    private sealed class FixedKeyResolver(byte[] publicKey, string algorithm, string? kid) : IIssuerKeyResolver
    {
        public Task<IssuerKeyResolution?> ResolveAsync(string rawSdJwt, CancellationToken ct = default) =>
            Task.FromResult<IssuerKeyResolution?>(new IssuerKeyResolution { PublicKey = publicKey, Algorithm = algorithm, SigningKeyId = kid });
    }

    private sealed class FakeDirectory : IIssuerDirectory
    {
        public Task<IssuerDirectoryEntry> LookupAsync(string issuerId, CancellationToken ct = default) =>
            Task.FromResult(new IssuerDirectoryEntry { Resolved = false });
    }

    private sealed class FakeAnchors : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default) =>
            Task.FromResult<TrustAnchorSet?>(null);
    }

    private sealed class FakeDidRegistry(string did, DidDocument doc) : IDidResolverRegistry
    {
        public Task<DidDocument?> ResolveAsync(string d, CancellationToken ct = default) =>
            Task.FromResult<DidDocument?>(string.Equals(d, did, StringComparison.Ordinal) ? doc : null);
        public void Register(IDidResolver resolver) { }
        public Task<DidDocument?> ResolveWithAlsoKnownAsAsync(string d, CancellationToken ct = default) => ResolveAsync(d, ct);
    }

    // --- shared policy + helpers ---------------------------------------------

    // Mirrors the policy HAIP synthesises for accepted issuers: x509-tenant (declines without a
    // chain) OR did-allowlist for the pinned issuer, both at Substantial.
    private static TrustPolicy AllowlistPolicy() => new()
    {
        Sources =
        [
            new TrustSourceRef { Kind = TrustSourceKind.X509Tenant, ConfersAssurance = AssuranceLevel.Substantial },
            new TrustSourceRef { Kind = TrustSourceKind.DidAllowlist, AllowedIssuers = [Issuer], ConfersAssurance = AssuranceLevel.Substantial }
        ],
        Combinator = TrustCombinator.AnyOf,
        MinAssuranceLevel = AssuranceLevel.Low
    };

    private static TrustResolverRegistry BuildRegistry()
        => new(new ITrustSourceResolver[]
        {
            new X509TenantTrustSourceResolver(new FakeAnchors()),
            new RegisterTrustSourceResolver(new FakeDirectory()),
            new DidAllowlistTrustSourceResolver(new FakeDirectory())
        });

    private (string raw, byte[] publicKey) MintCredential()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportECPrivateKey();
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();

        var token = _sdJwt.CreateTokenAsync(
            new Dictionary<string, object> { ["vct"] = "ParityCredential", ["license_type"] = "ClassA" },
            disclosableClaims: ["license_type"],
            issuer: Issuer, subject: "did:sorcha:holder:parity",
            signingKey: privateKey, algorithm: "ES256", kid: Kid).GetAwaiter().GetResult();

        return (token.RawToken, publicKey);
    }

    private static DidDocument BuildDidDocument(byte[] publicKey)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ecdsa.ImportSubjectPublicKeyInfo(publicKey, out _);
        var p = ecdsa.ExportParameters(false);
        string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var jwkJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC", ["crv"] = "P-256", ["x"] = B64Url(p.Q.X!), ["y"] = B64Url(p.Q.Y!)
        });
        return new DidDocument
        {
            Id = Issuer,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = Kid, Type = "JsonWebKey2020", Controller = Issuer,
                    PublicKeyJwk = JsonSerializer.Deserialize<JsonElement>(jwkJson)
                }
            ],
            AssertionMethod = [Kid]
        };
    }

    private static string TamperSignature(string raw)
    {
        var parts = raw.Split('~');
        var jwt = parts[0].Split('.');
        jwt[2] = (jwt[2][0] == 'A' ? 'B' : 'A') + jwt[2][1..];
        parts[0] = string.Join('.', jwt);
        return string.Join('~', parts);
    }

    // --- the parity proofs ----------------------------------------------------

    [Fact]
    public async Task SameCredentialAndPolicy_BothPathsTrust_WithEqualEvidenceShape()
    {
        var (raw, publicKey) = MintCredential();

        // Engine path: SdJwtVcFormatHandler -> shared TrustEvaluator.
        var engineHandler = new SdJwtVcFormatHandler(
            _sdJwt, new FixedKeyResolver(publicKey, "ES256", Kid), new TrustEvaluator(BuildRegistry()));
        var engine = await engineHandler.VerifyAsync(
            new PresentedCredential { Raw = raw, Format = CredentialFormat.SdJwtVc },
            new CredentialRequirement { Type = "ParityCredential", TrustPolicy = AllowlistPolicy() });

        // HAIP path: HaipPresentationVerifier -> shared TrustEvaluator (key via DID resolution).
        var haipVerifier = new HaipPresentationVerifier(
            _sdJwt, new TrustEvaluator(BuildRegistry()), Mock.Of<ILogger<HaipPresentationVerifier>>(),
            new FakeDidRegistry(Issuer, BuildDidDocument(publicKey)));
        var haip = await haipVerifier.VerifyAsync(
            raw, expectedNonce: "n", expectedAudience: "a", acceptedIssuers: [Issuer]);

        // Same accept decision.
        engine.IsValid.Should().BeTrue();
        haip.IsValid.Should().BeTrue();

        // Same evidence shape: deciding source, established assurance, and a non-empty policy digest.
        engine.Trust!.Evidence.VouchingSource.Should().Be(TrustSourceKind.DidAllowlist);
        haip.TrustEvidence!.VouchingSource.Should().Be(TrustSourceKind.DidAllowlist);
        engine.Trust.EstablishedAssurance.Should().Be(haip.TrustEvidence.AssuranceLevel);
        engine.Trust.Evidence.PolicyDigest.Should().NotBeNullOrEmpty();
        haip.TrustEvidence.PolicyDigest.Should().NotBeNullOrEmpty();
        engine.IssuerId.Should().Be(Issuer);
        haip.Issuer.Should().Be(Issuer);
    }

    [Fact]
    public async Task TamperedSignature_RejectedOnBothPaths()
    {
        var (raw, publicKey) = MintCredential();
        var tampered = TamperSignature(raw);

        var engineHandler = new SdJwtVcFormatHandler(
            _sdJwt, new FixedKeyResolver(publicKey, "ES256", Kid), new TrustEvaluator(BuildRegistry()));
        var engine = await engineHandler.VerifyAsync(
            new PresentedCredential { Raw = tampered, Format = CredentialFormat.SdJwtVc },
            new CredentialRequirement { Type = "ParityCredential", TrustPolicy = AllowlistPolicy() });

        var haipVerifier = new HaipPresentationVerifier(
            _sdJwt, new TrustEvaluator(BuildRegistry()), Mock.Of<ILogger<HaipPresentationVerifier>>(),
            new FakeDidRegistry(Issuer, BuildDidDocument(publicKey)));
        var haip = await haipVerifier.VerifyAsync(
            tampered, expectedNonce: "n", expectedAudience: "a", acceptedIssuers: [Issuer]);

        engine.IsValid.Should().BeFalse();
        engine.Trust!.FailureReason.Should().Be(TrustFailureReason.SignatureInvalid);
        haip.IsValid.Should().BeFalse();
    }
}
