// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.Cose;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

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
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Mdoc;
using Sorcha.Mdoc.Cbor;
using Sorcha.Cryptography.SdJwt;
using Sorcha.Haip.Service.Endpoints;
using Sorcha.Haip.Service.Models;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Haip.Service.Services;

using Xunit;

namespace Sorcha.Haip.Service.Tests.Endpoints;

/// <summary>
/// Feature 181 / T019 — the HAIP verifier endpoints speak DCQL (OpenID4VP 1.0 final):
/// the Request Object carries <c>dcql_query</c> (no <c>presentation_definition</c>), and
/// direct_post accepts only the object-keyed <c>vp_token</c> envelope, rejecting the
/// retired Presentation Exchange dialect with typed error codes.
/// </summary>
public class VerifierEndpointTests
{
    private const string IssuerUrl = "https://test.example/haip";

    // --- infrastructure -------------------------------------------------------

    private readonly PresentationRequestStore _store;
    private readonly RequestObjectSigner _signer;

    public VerifierEndpointTests()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Haip:PresentationRequestTtlSeconds"] = "600",
                ["Haip:IssuerSigningAlgorithm"] = "ES256"
            })
            .Build();

        _store = new PresentationRequestStore(
            Mock.Of<ILogger<PresentationRequestStore>>(),
            config,
            new InMemoryAtomicDistributedCache());
        _signer = new RequestObjectSigner(Sorcha.Haip.Service.Services.VerifierCertificate.CreateSelfSigned("verifier.test"));
    }

    private sealed class FakeAnchors(byte[]? root) : ITenantTrustAnchorProvider
    {
        public Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default) =>
            Task.FromResult<TrustAnchorSet?>(root is null ? null : new TrustAnchorSet { Roots = [root], CheckRevocation = false });
    }

    private static HaipPresentationVerifier BuildSdJwtVerifier(byte[]? trustedRoot = null)
    {
        var registry = new TrustResolverRegistry([new X509TenantTrustSourceResolver(new FakeAnchors(trustedRoot))]);
        return new HaipPresentationVerifier(
            new SdJwtService(),
            new TrustEvaluator(registry, null),
            Mock.Of<ILogger<HaipPresentationVerifier>>());
    }

    private static MdocPresentationVerifier BuildMdocVerifier(byte[]? trustedRoot = null)
    {
        var registry = new TrustResolverRegistry([new X509TenantTrustSourceResolver(new FakeAnchors(trustedRoot))]);
        var evaluator = new TrustEvaluator(registry, null);
        return new MdocPresentationVerifier(
            new MdocFormatHandler(new MdocService(), evaluator),
            Mock.Of<ILogger<MdocPresentationVerifier>>());
    }

    private Task<PresentationRequest> CreateRequestAsync(List<string>? requiredClaims = null) =>
        _store.CreateAsync(
            clientId: IssuerUrl,
            credentialType: "LicenseCredential",
            requiredClaims: requiredClaims,
            acceptedIssuers: null,
            baseUrl: IssuerUrl);

    // Feature 181 US2 (T028) — a multi-credential request carrying a full declared DCQL query.
    private Task<PresentationRequest> CreateMultiRequestAsync(DcqlQuery declaredQuery) =>
        _store.CreateAsync(
            clientId: IssuerUrl,
            credentialType: declaredQuery.Credentials[0].Meta.VctValues![0],
            requiredClaims: null,
            acceptedIssuers: null,
            baseUrl: IssuerUrl,
            declaredQuery: declaredQuery);

    private static DcqlCredentialQuery Cq(string id, string vct) => new()
    {
        Id = id,
        Format = DcqlFormats.SdJwtVc,
        Meta = new DcqlCredentialMeta { VctValues = [vct] },
    };

    // --- private static handler invocation (established reflection pattern) ----

    private static MethodInfo Handler(string name) =>
        typeof(VerifierEndpoints).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Handler '{name}' not found on VerifierEndpoints.");

    private Task<IResult> InvokeGetRequestObjectAsync(Guid requestId) =>
        (Task<IResult>)Handler("GetRequestObject").Invoke(
            null, [requestId, _store, _signer, CancellationToken.None])!;

    private Task<IResult> InvokeDirectPostAsync(
        Guid requestId,
        string? vpToken,
        string? presentationSubmission = null,
        string? state = null,
        MdocPresentationVerifier? mdocVerifier = null,
        HaipPresentationVerifier? sdJwtVerifier = null) =>
        (Task<IResult>)Handler("HandleDirectPost").Invoke(
            null,
            [
                requestId,
                vpToken,
                presentationSubmission,
                state ?? requestId.ToString(),
                _store,
                sdJwtVerifier ?? BuildSdJwtVerifier(),
                mdocVerifier ?? BuildMdocVerifier(),
                null, // PresentationCallbackRelay — optional, not exercised here
                NullLoggerFactory.Instance,
                CancellationToken.None
            ])!;

    /// <summary>Executes an IResult against a real HttpContext and captures status + body.</summary>
    private static async Task<(int Status, string Body)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        await result.ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static JsonElement ParseJson(string body) =>
        JsonDocument.Parse(body).RootElement.Clone();

    /// <summary>Decodes the payload segment of a compact JWT.</summary>
    internal static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3, "the Request Object is a signed compact JWT");
        return JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement.Clone();
    }

    // ===========================================================================
    // GetRequestObject — DCQL request shape (T010 coverage)
    // ===========================================================================

    [Fact]
    public async Task GetRequestObject_ValidRequest_PayloadCarriesDcqlQueryAndNoPresentationDefinition()
    {
        var request = await CreateRequestAsync(requiredClaims: ["licenseNumber", "locality"]);

        var (status, body) = await ExecuteAsync(await InvokeGetRequestObjectAsync(request.Id));

        status.Should().Be(200);
        var payload = DecodeJwtPayload(body);

        payload.TryGetProperty("presentation_definition", out _)
            .Should().BeFalse("Presentation Exchange is retired (FR-002)");
        payload.GetProperty("response_type").GetString().Should().Be("vp_token");
        payload.GetProperty("response_mode").GetString().Should().Be("direct_post");
        payload.GetProperty("nonce").GetString().Should().Be(request.Nonce);
        payload.GetProperty("state").GetString().Should().Be(request.Id.ToString());

        var dcql = payload.GetProperty("dcql_query");
        var credentials = dcql.GetProperty("credentials");
        credentials.GetArrayLength().Should().Be(1, "the single-ask surface issues exactly one credential query");

        var query = credentials[0];
        query.GetProperty("id").GetString().Should().Be(VerifierEndpoints.DefaultQueryId);
        query.GetProperty("format").GetString().Should().Be("dc+sd-jwt", "final-profile format identifier (FR-004)");
        query.GetProperty("meta").GetProperty("vct_values")[0].GetString().Should().Be("LicenseCredential");
    }

    [Fact]
    public async Task GetRequestObject_RequiredClaims_MappedToDcqlClaimPaths()
    {
        var request = await CreateRequestAsync(requiredClaims: ["licenseNumber", "locality"]);

        var (_, body) = await ExecuteAsync(await InvokeGetRequestObjectAsync(request.Id));
        var payload = DecodeJwtPayload(body);

        var claims = payload.GetProperty("dcql_query").GetProperty("credentials")[0].GetProperty("claims");
        var paths = claims.EnumerateArray()
            .Select(c => string.Join("/", c.GetProperty("path").EnumerateArray().Select(p => p.GetString())))
            .ToList();

        paths.Should().BeEquivalentTo(["licenseNumber", "locality"]);
    }

    [Fact]
    public async Task GetRequestObject_UnknownRequest_Returns404()
    {
        var (status, _) = await ExecuteAsync(await InvokeGetRequestObjectAsync(Guid.NewGuid()));

        status.Should().Be(404);
    }

    // ===========================================================================
    // HandleDirectPost — DCQL envelope + legacy-dialect rejection (T011 coverage)
    // ===========================================================================

    [Fact]
    public async Task HandleDirectPost_PresentationSubmissionPresent_Returns400LegacyDialect()
    {
        var request = await CreateRequestAsync();

        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id,
            vpToken: /*lang=json,strict*/ """{"credential":["a~b~"]}""",
            presentationSubmission: /*lang=json,strict*/ """{"id":"sub-1","definition_id":"pd-1"}"""));

        status.Should().Be(400);
        ParseJson(body).GetProperty("error").GetString().Should().Be("LEGACY_DIALECT");
    }

    [Fact]
    public async Task HandleDirectPost_BareCompactStringVpToken_Returns400LegacyDialect()
    {
        var request = await CreateRequestAsync();

        // The retired pre-DCQL shape: a bare SD-JWT compact string, not an object envelope.
        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: "eyJhbGciOiJFUzI1NiJ9.payload.sig~disclosure~kbjwt"));

        status.Should().Be(400);
        ParseJson(body).GetProperty("error").GetString().Should().Be("LEGACY_DIALECT");
    }

    [Fact]
    public async Task HandleDirectPost_MalformedJsonEnvelope_Returns400DcqlInvalid()
    {
        var request = await CreateRequestAsync();

        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: """{"credential": not-json"""));

        status.Should().Be(400);
        ParseJson(body).GetProperty("error").GetString().Should().Be("DCQL_INVALID");
    }

    [Fact]
    public async Task HandleDirectPost_UnknownQueryId_Returns400DcqlUnknownQueryId()
    {
        var request = await CreateRequestAsync();

        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"other_query":["a~b~"]}"""));

        status.Should().Be(400);
        var json = ParseJson(body);
        json.GetProperty("error").GetString().Should().Be("DCQL_UNKNOWN_QUERY_ID");
        json.GetProperty("error_description").GetString().Should().Contain("other_query");
    }

    // --- Feature 181 US2 (T028): multi-credential per-query verification -----------

    [Fact]
    public async Task HandleDirectPost_MultiQuery_IdOutsideDeclaredSet_Returns400UnknownQueryId()
    {
        var request = await CreateMultiRequestAsync(new DcqlQuery { Credentials = [Cq("idq", "IdCred"), Cq("ageq", "AgeCred")] });

        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"idq":["a~b~"],"ghost":["a~b~"]}"""));

        status.Should().Be(400);
        var json = ParseJson(body);
        json.GetProperty("error").GetString().Should().Be("DCQL_UNKNOWN_QUERY_ID");
        json.GetProperty("error_description").GetString().Should().Contain("ghost");
    }

    [Fact]
    public async Task HandleDirectPost_MultiQuery_MissingRequiredQuery_FailsWithPerQueryDetail()
    {
        var request = await CreateMultiRequestAsync(new DcqlQuery { Credentials = [Cq("idq", "IdCred"), Cq("ageq", "AgeCred")] });

        // Only idq is supplied (garbage → invalid); ageq is absent. AND semantics ⇒ overall invalid.
        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"idq":["garbage~disclosure~"]}"""));

        status.Should().Be(400);

        var stored = await _store.GetAsync(request.Id);
        stored!.Result!.PerQuery.Should().ContainKeys("idq", "ageq");
        stored.Result.PerQuery!["ageq"].IsValid.Should().BeFalse();
        stored.Result.PerQuery["ageq"].Errors.Should().ContainMatch("*No presentation*");
        stored.Result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]   // both queries verified ⇒ AND satisfied
    [InlineData(false)]  // one query missing ⇒ AND not satisfied
    public void AllRequiredSatisfied_AndSemantics(bool bothValid)
    {
        var query = new DcqlQuery { Credentials = [Cq("a", "A"), Cq("b", "B")] };
        var valid = bothValid ? new HashSet<string> { "a", "b" } : new HashSet<string> { "a" };

        VerifierEndpoints.AllRequiredSatisfied(query, valid).Should().Be(bothValid);
    }

    [Fact]
    public void AllRequiredSatisfied_RequiredSet_HonoursOptions()
    {
        var query = new DcqlQuery
        {
            Credentials = [Cq("a", "A"), Cq("b", "B")],
            CredentialSets = [new DcqlCredentialSetQuery { Options = [["a"], ["b"]], Required = true }],
        };

        VerifierEndpoints.AllRequiredSatisfied(query, new HashSet<string> { "b" }).Should().BeTrue();   // one option satisfied
        VerifierEndpoints.AllRequiredSatisfied(query, new HashSet<string>()).Should().BeFalse();         // neither option
    }

    [Fact]
    public void AllRequiredSatisfied_OptionalSet_Unmet_StillSatisfied()
    {
        var query = new DcqlQuery
        {
            Credentials = [Cq("a", "A")],
            CredentialSets = [new DcqlCredentialSetQuery { Options = [["a"]], Required = false }],
        };

        VerifierEndpoints.AllRequiredSatisfied(query, new HashSet<string>()).Should().BeTrue();
    }

    [Fact]
    public async Task HandleDirectPost_MissingState_Returns400()
    {
        var request = await CreateRequestAsync();

        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"credential":["a~b~"]}""", state: ""));

        status.Should().Be(400);
    }

    [Fact]
    public async Task HandleDirectPost_UnknownRequestId_Returns404()
    {
        var (status, _) = await ExecuteAsync(await InvokeDirectPostAsync(
            Guid.NewGuid(), vpToken: /*lang=json,strict*/ """{"credential":["a~b~"]}"""));

        status.Should().Be(404);
    }

    // --- per-entry format dispatch (FR-005) ------------------------------------

    [Fact]
    public async Task HandleDirectPost_TildeDelimitedEntry_RoutesToSdJwtVerifier()
    {
        var request = await CreateRequestAsync();

        // A '~'-containing presentation dispatches to the SD-JWT verifier; the garbage token
        // fails there with the SD-JWT pipeline's own error, proving which verifier ran.
        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"credential":["garbage~disclosure~"]}"""));

        status.Should().Be(400);
        var json = ParseJson(body);
        json.GetProperty("error").GetString().Should().Be("invalid_presentation");
        json.GetProperty("error_description").GetString()
            .Should().Contain("Cannot extract algorithm", "the SD-JWT verifier must have handled the entry")
            .And.NotContain("mdoc");
    }

    [Fact]
    public async Task HandleDirectPost_NonTildeEntry_RoutesToMdocVerifier()
    {
        var request = await CreateRequestAsync();

        // No '~' → mdoc dispatch; the non-CBOR token fails inside the mdoc verifier,
        // whose error text is distinct from the SD-JWT pipeline's.
        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: /*lang=json,strict*/ """{"credential":["bm90LWEtZGV2aWNlLXJlc3BvbnNl"]}"""));

        status.Should().Be(400);
        var json = ParseJson(body);
        json.GetProperty("error").GetString().Should().Be("invalid_presentation");
        json.GetProperty("error_description").GetString().Should().Contain("mdoc");
    }

    [Fact]
    public async Task HandleDirectPost_ValidMdocEnvelope_Returns200AndStoresNullSubmission()
    {
        var request = await CreateRequestAsync();
        var (vpToken, issuerCertDer) = BuildMdocPresentation(request);
        var envelope = JsonSerializer.Serialize(
            new Dictionary<string, string[]> { [VerifierEndpoints.DefaultQueryId] = [vpToken] });

        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: envelope, mdocVerifier: BuildMdocVerifier(trustedRoot: issuerCertDer)));

        status.Should().Be(200, "a valid object-keyed mdoc envelope must verify end-to-end: {0}", body);

        var stored = await _store.GetAsync(request.Id);
        stored.Should().NotBeNull();
        stored!.State.Should().Be(PresentationRequestState.Verified);
        stored.SubmittedVpToken.Should().Be(envelope, "the raw envelope is stored for client re-validation");
        stored.PresentationSubmission.Should().BeNull("presentation_submission is retired with the PE dialect");
    }

    // --- device-cnf SD-JWT via standard OID4VP, NO delegation (#1195 SC-1/SC-2/SC-3) -----------

    [Fact]
    public async Task HandleDirectPost_DeviceCnf_SdJwt_VerifiesWithNoDelegation()
    {
        // Arrange: the citizen wallet's Phase-1 output — an SD-JWT VC whose cnf.jwk is the DEVICE
        // key (P-256), presented with a device-signed KB-JWT and an x5c chain to a test root. The
        // request requires the one claim the presentation discloses (licenseType); vct/credentialType
        // is not gated by the verifier (requiredCredentialType is advisory), so the real match contract
        // is: object-keyed envelope under the DEFAULT query id + required-claim presence + trusted root.
        var request = await CreateRequestAsync(requiredClaims: [DeviceCnfPresentationFactory.DisclosedClaim]);

        var (presentation, rootCertDer) = await DeviceCnfPresentationFactory.CreateAsync(
            new SdJwtService(), audience: request.ClientId, nonce: request.Nonce);

        // OID4VP 1.0 object-keyed vp_token envelope, keyed to the server's default query id ("credential").
        var envelope = JsonSerializer.Serialize(
            new Dictionary<string, string[]> { [VerifierEndpoints.DefaultQueryId] = [presentation] });

        // Act: drive the REAL HandleDirectPost with ONLY vp_token + state (no delegation parameter
        // exists on the handler) via a root-trusting SD-JWT verifier. #1195 SC-3: a verified result
        // obtained here inherently proves delegation is absent from the standard OID4VP path.
        var (status, body) = await ExecuteAsync(await InvokeDirectPostAsync(
            request.Id, vpToken: envelope, state: request.Id.ToString(),
            sdJwtVerifier: BuildSdJwtVerifier(trustedRoot: rootCertDer)));

        // Assert: the device-cnf presentation verifies end-to-end through the standard HAIP path.
        status.Should().Be(200, "a device-cnf SD-JWT with a device-signed KB-JWT must verify: {0}", body);

        var stored = await _store.GetAsync(request.Id);
        stored.Should().NotBeNull();
        stored!.State.Should().Be(PresentationRequestState.Verified);
        stored.Result.Should().NotBeNull();
        stored.Result!.IsValid.Should().BeTrue();
        stored.Result.HolderKeyVerified.Should().BeTrue(
            "the KB-JWT was signed by the private half of the device cnf key");
        stored.Result.X5cChainValid.Should().BeTrue();
        stored.Result.VerifiedClaims.Should().ContainKey(DeviceCnfPresentationFactory.DisclosedClaim);
        stored.PresentationSubmission.Should().BeNull("no PE dialect, and no delegation artefact, on the wire");
    }

    /// <summary>
    /// Mints a self-signed mdoc DeviceResponse bound to the request's OpenID4VP session
    /// (client_id / nonce / response_uri) so the endpoint's mdoc path verifies for real.
    /// </summary>
    private static (string VpToken, byte[] IssuerCertDer) BuildMdocPresentation(PresentationRequest request)
    {
        const string docType = "eu.europa.ec.eudi.pid.1";
        var now = DateTimeOffset.UtcNow;

        using var issuerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var issuerCert = new CertificateRequest("CN=Test PID Issuer", issuerKey, HashAlgorithmName.SHA256)
            .CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
        var issuerCertDer = issuerCert.Export(X509ContentType.Cert);

        using var holderKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = holderKey.ExportParameters(false);
        string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var holderJwk = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = B64Url(p.Q.X!),
            ["y"] = B64Url(p.Q.Y!)
        }));
        var holderCose = CredentialEndpoints.BuildEc2CoseKeyFromJwk(holderJwk);

        var issuerSigned = MdocIssuer.IssueIssuerSigned(
            docType, new Dictionary<string, object> { ["family_name"] = "Andersson" },
            issuerKey.ExportECPrivateKey(), "ES256", holderCose, now, now.AddYears(1), x5cChain: [issuerCertDer]);

        var deviceNameSpaces = MdocCbor.WrapTag24(MdocCbor.Encode(w => { w.WriteStartMap(0); w.WriteEndMap(); }));
        var transcript = MdocCodec.BuildOpenId4VpSessionTranscript(
            request.ClientId, request.Nonce, null, request.ResponseUri);
        var deviceAuthn = MdocCodec.BuildDeviceAuthentication(transcript, docType, deviceNameSpaces);
        var deviceSig = CoseMessage.DecodeSign1(
            CoseSign1Message.SignDetached(deviceAuthn, new CoseSigner(holderKey, HashAlgorithmName.SHA256)));

        var response = new DeviceResponse
        {
            Documents =
            [
                new Document
                {
                    DocType = docType,
                    IssuerSigned = issuerSigned,
                    DeviceSigned = new DeviceSigned
                    {
                        NameSpacesBytes = deviceNameSpaces,
                        DeviceAuth = new DeviceAuth { DeviceSignature = deviceSig }
                    }
                }
            ]
        };

        return (Base64Url.EncodeToString(MdocCodec.EncodeDeviceResponse(response)), issuerCertDer);
    }
}
