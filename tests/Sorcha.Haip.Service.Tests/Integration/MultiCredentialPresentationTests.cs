// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Sorcha.AtomicCache;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Sorcha.Tenant.Service.Trust;
using Sorcha.Verifier.Engine.Dcql;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Integration;

/// <summary>
/// Feature 181 US2 (T030, SC-003) — end-to-end coverage of the multi-credential DCQL presentation
/// flow through the real request-object → direct_post handler with genuinely-valid SD-JWT
/// presentations (x5c chain to a trusted root, holder-bound KB-JWT). Exercises the three
/// quickstart §US2 scenarios: a two-query AND that completes when both are supplied and blocks when
/// one is missing, a two-option OR that completes with the held option, and a required ask with no
/// valid presentation that fails closed.
/// </summary>
public sealed class MultiCredentialPresentationTests
{
    private const string IssuerUrl = "https://test.example/haip";

    private readonly SdJwtService _sdJwt = new();
    private readonly PresentationRequestStore _store;

    public MultiCredentialPresentationTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PresentationRequestTtlSeconds"] = "600",
                ["Haip:IssuerSigningAlgorithm"] = "ES256",
            })
            .Build();
        _store = new PresentationRequestStore(
            Mock.Of<ILogger<PresentationRequestStore>>(), config, new InMemoryAtomicDistributedCache());
    }

    private static DcqlCredentialQuery Cq(string id, string vct) => new()
    {
        Id = id,
        Format = DcqlFormats.SdJwtVc,
        Meta = new DcqlCredentialMeta { VctValues = [vct] },
    };

    // ── the three quickstart §US2 scenarios ──────────────────────────────────────

    [Fact]
    public async Task TwoQueryAnd_BothPresentationsValid_Verifies200()
    {
        var (root, rootPriv) = BuildRoot();
        var request = await CreateMultiRequestAsync(
            new DcqlQuery { Credentials = [Cq("identity", "IdentityCredential"), Cq("address", "AddressCredential")] });

        var vpToken = Envelope(
            ("identity", await BuildPresentationAsync(root, rootPriv, request.ClientId, request.Nonce)),
            ("address", await BuildPresentationAsync(root, rootPriv, request.ClientId, request.Nonce)));

        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(request.Id, vpToken, verifierRoot: root));

        status.Should().Be(200);
        var stored = await _store.GetAsync(request.Id);
        stored!.Result!.IsValid.Should().BeTrue();
        stored.Result.PerQuery.Should().ContainKeys("identity", "address");
        stored.Result.PerQuery!["identity"].IsValid.Should().BeTrue();
        stored.Result.PerQuery["address"].IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task TwoQueryAnd_OneMissing_Blocks400()
    {
        var (root, rootPriv) = BuildRoot();
        var request = await CreateMultiRequestAsync(
            new DcqlQuery { Credentials = [Cq("identity", "IdentityCredential"), Cq("address", "AddressCredential")] });

        // Only the identity query is supplied — AND semantics ⇒ overall block.
        var vpToken = Envelope(("identity", await BuildPresentationAsync(root, rootPriv, request.ClientId, request.Nonce)));

        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(request.Id, vpToken, verifierRoot: root));

        status.Should().Be(400);
        var stored = await _store.GetAsync(request.Id);
        stored!.Result!.IsValid.Should().BeFalse();
        stored.Result.PerQuery!["identity"].IsValid.Should().BeTrue();
        stored.Result.PerQuery["address"].IsValid.Should().BeFalse();
        stored.Result.PerQuery["address"].Errors.Should().ContainMatch("*No presentation*");
    }

    [Fact]
    public async Task TwoOptionOr_HeldOptionValid_Verifies200()
    {
        var (root, rootPriv) = BuildRoot();
        // credential_sets: either passport OR driving licence satisfies the required set.
        var request = await CreateMultiRequestAsync(new DcqlQuery
        {
            Credentials = [Cq("passport", "PassportCredential"), Cq("licence", "DrivingLicenceCredential")],
            CredentialSets = [new DcqlCredentialSetQuery { Options = [["passport"], ["licence"]], Required = true }],
        });

        // The citizen holds only the driving licence — supply that option alone.
        var vpToken = Envelope(("licence", await BuildPresentationAsync(root, rootPriv, request.ClientId, request.Nonce)));

        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(request.Id, vpToken, verifierRoot: root));

        status.Should().Be(200);
        var stored = await _store.GetAsync(request.Id);
        stored!.Result!.IsValid.Should().BeTrue("one option of the required set is satisfied");
        stored.Result.PerQuery!["licence"].IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RequiredSet_NoValidOption_Blocks400()
    {
        var (root, rootPriv) = BuildRoot();
        var request = await CreateMultiRequestAsync(new DcqlQuery
        {
            Credentials = [Cq("passport", "PassportCredential"), Cq("licence", "DrivingLicenceCredential")],
            CredentialSets = [new DcqlCredentialSetQuery { Options = [["passport"], ["licence"]], Required = true }],
        });

        // The citizen supplies an invalid presentation for one option; neither option verifies.
        var vpToken = Envelope(("passport", "garbage~disclosure~"));

        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(request.Id, vpToken, verifierRoot: root));

        status.Should().Be(400);
        var stored = await _store.GetAsync(request.Id);
        stored!.Result!.IsValid.Should().BeFalse();
        stored.Result.PerQuery!["passport"].IsValid.Should().BeFalse();
    }

    // ── harness ──────────────────────────────────────────────────────────────────

    private Task<PresentationRequest> CreateMultiRequestAsync(DcqlQuery declaredQuery) =>
        _store.CreateAsync(
            clientId: IssuerUrl,
            credentialType: declaredQuery.Credentials[0].Meta.VctValues![0],
            requiredClaims: null,
            acceptedIssuers: null,
            baseUrl: IssuerUrl,
            declaredQuery: declaredQuery);

    private static string Envelope(params (string QueryId, string Presentation)[] entries)
    {
        var obj = entries.ToDictionary(e => e.QueryId, e => new[] { e.Presentation });
        return JsonSerializer.Serialize(obj);
    }

    private static (byte[] RootDer, byte[] RootPriv) BuildRoot()
    {
        var (rootDer, rootPriv, _) = X509CertificateBuilder.BuildSelfSignedRoot("ES256", "CN=Test Root CA");
        return (rootDer, rootPriv);
    }

    /// <summary>
    /// Build a genuinely-valid SD-JWT VC presentation: an org cert issued under <paramref name="rootDer"/>
    /// carried in the x5c header, an issuer-signed token, and a holder-bound KB-JWT over the request's
    /// audience + nonce. Mirrors what the credential endpoint produces (see HaipPresentationVerifierTests).
    /// </summary>
    private async Task<string> BuildPresentationAsync(byte[] rootDer, byte[] rootPriv, string audience, string nonce)
    {
        using var issuerEcdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var issuerPublicKey = issuerEcdsa.ExportSubjectPublicKeyInfo();
        var issuerPrivateKey = issuerEcdsa.ExportECPrivateKey();

        var (orgCertDer, _) = X509CertificateBuilder.BuildOrgCert(
            rootDer, rootPriv, issuerPublicKey, "CN=Test Org", "did:sorcha:org:ws1qtest");

        var (holderPrivate, holderPublic) = GenerateP256KeyPair();
        var holderJwk = CreateHolderJwk(holderPublic);

        var token = await _sdJwt.CreateTokenAsync(
            new Dictionary<string, object> { ["licenseType"] = "ClassA", ["holder"] = "Alice" },
            disclosableClaims: ["licenseType", "holder"],
            issuer: "did:sorcha:org:ws1qtest",
            subject: "did:sorcha:w:holder1",
            signingKey: issuerPrivateKey,
            algorithm: "ES256",
            holderJwk: holderJwk);

        // Inject the x5c chain into the header and re-sign.
        var rawParts = token.RawToken.TrimEnd('~').Split('~');
        var jwtSegments = rawParts[0].Split('.');
        var headerBytes = System.Buffers.Text.Base64Url.DecodeFromChars(jwtSegments[0]);
        var header = JsonSerializer.Deserialize<Dictionary<string, object>>(headerBytes)!;
        header["x5c"] = new[] { Convert.ToBase64String(orgCertDer), Convert.ToBase64String(rootDer) };

        var newHeaderB64 = System.Buffers.Text.Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var signingInput = System.Text.Encoding.UTF8.GetBytes($"{newHeaderB64}.{jwtSegments[1]}");
        var signature = issuerEcdsa.SignData(signingInput, HashAlgorithmName.SHA256);
        var signatureB64 = System.Buffers.Text.Base64Url.EncodeToString(signature);
        var newRawToken = $"{newHeaderB64}.{jwtSegments[1]}.{signatureB64}~{string.Join("~", rawParts[1..])}~";

        var presentation = await _sdJwt.CreatePresentationAsync(
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

        return presentation.RawPresentation;
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
        var p = ecdsa.ExportParameters(includePrivateParameters: false);
        var jwk = new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Convert.ToBase64String(p.Q.X!).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["y"] = Convert.ToBase64String(p.Q.Y!).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
        };
        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(jwk));
    }

    private HaipPresentationVerifier BuildVerifier(byte[]? trustedRootDer)
    {
        var anchors = trustedRootDer is null
            ? null
            : new TrustAnchorSet { Roots = [trustedRootDer], CheckRevocation = false };
        var registry = new TrustResolverRegistry(new ITrustSourceResolver[]
        {
            new X509TenantTrustSourceResolver(new FakeAnchors(anchors)),
        });
        var evaluator = new TrustEvaluator(registry, statusChecker: null);
        return new HaipPresentationVerifier(_sdJwt, evaluator, Mock.Of<ILogger<HaipPresentationVerifier>>());
    }

    private MdocPresentationVerifier BuildMdocVerifier()
    {
        var registry = new TrustResolverRegistry(new ITrustSourceResolver[] { new X509TenantTrustSourceResolver(new FakeAnchors(null)) });
        return new MdocPresentationVerifier(
            new MdocFormatHandler(new Sorcha.Cryptography.Mdoc.MdocService(), new TrustEvaluator(registry, null)),
            Mock.Of<ILogger<MdocPresentationVerifier>>());
    }

    private sealed class FakeAnchors(TrustAnchorSet? set) : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default) => Task.FromResult(set);
    }

    private Task<IResult> InvokeDirectPostAsync(Guid requestId, string vpToken, byte[]? verifierRoot)
    {
        var handler = typeof(VerifierEndpoints).GetMethod("HandleDirectPost", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("HandleDirectPost not found.");
        return (Task<IResult>)handler.Invoke(null,
        [
            requestId,
            vpToken,
            null,               // presentation_submission
            requestId.ToString(),
            _store,
            BuildVerifier(verifierRoot),
            BuildMdocVerifier(),
            null,               // PresentationCallbackRelay
            NullLoggerFactory.Instance,
            CancellationToken.None,
        ])!;
    }

    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }
}
