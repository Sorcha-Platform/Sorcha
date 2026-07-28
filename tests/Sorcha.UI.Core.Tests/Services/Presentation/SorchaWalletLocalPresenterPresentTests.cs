// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class SorchaWalletLocalPresenterPresentTests
{
    private static string Disclosure(string name, string value)
        => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new object[] { "salt-" + name, name, value }));

    private static readonly string CredentialJwt = "eyJhbGciOiJFZERTQSJ9.eyJ2Y3QiOiJ4In0.c2ln";
    private static readonly string DGiven = Disclosure("givenName", "Ada");
    private static readonly string DFamily = Disclosure("familyName", "Lovelace");
    private static readonly string DPortrait = Disclosure("portrait", "base64...");
    private static string RawToken => $"{CredentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";

    private static LocalPresentationCandidate Candidate() => new()
    {
        CredentialId = "urn:uuid:c1",
        WalletAddress = "ws1qcitizen",
        Vct = "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaims = ["givenName", "familyName"],
        OptionalClaims = ["portrait"],
        Nonce = "n-123",
        ClientId = "did:sorcha:org:ws1qabc",
        ResponseUri = "/api/presentations/callbacks/sorcha-wallet/rid-1",
        QueryId = "credential",
        RequestState = "rid-1",
        JoseAlgorithm = "EdDSA",
        KidThumbprint = "thumb",
    };

    private static string BuildCredentialJwt(object payload)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"EdDSA"}"""));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{payloadSeg}.c2ln";
    }

    private static (SorchaWalletLocalPresenter Presenter, CapturingHandler Http) Build(
        string signKbAlgorithm = "EdDSA",
        string callbackKind = "Success",
        HttpStatusCode callbackStatus = HttpStatusCode.OK,
        string? rawToken = null)
    {
        var handler = new CapturingHandler(req =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("/export"))
                return Json($$"""{"id":"urn:uuid:c1","type":"x","rawToken":"{{rawToken ?? RawToken}}"}""");
            if (path.Contains("/sign-kb"))
                return Json($$"""{"signature":"ZmFrZXNpZw","algorithm":"{{signKbAlgorithm}}"}""");
            if (path.Contains("/callbacks/"))
                return new HttpResponseMessage(callbackStatus)
                    { Content = new StringContent($$"""{"kind":"{{callbackKind}}"}""", Encoding.UTF8, "application/json") };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://unit.test/") };
        var presenter = new SorchaWalletLocalPresenter(
            http, Mock.Of<IHolderKeyClient>(), Mock.Of<ICredentialApiService>(),
            TimeProvider.System, NullLogger<SorchaWalletLocalPresenter>.Instance);
        return (presenter, handler);

        static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task PresentAsync_FullConsent_PostsEnvelopeWithNonceBoundKbJwt()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName", "portrait"]);

        result.Status.Should().Be(LocalPresentStatus.Submitted);

        // The direct_post is the last request; decode what actually went on the wire.
        var form = http.RequestBodies[^1];
        var pairs = form.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
        pairs["state"].Should().Be("rid-1");

        using var envelope = JsonDocument.Parse(pairs["vp_token"]);
        var vp = envelope.RootElement.GetProperty("credential")[0].GetString()!;

        // vp = jwt~d1~d2~d3~kbJwt — all three consented disclosures, then the KB-JWT.
        var segments = vp.Split('~');
        segments[0].Should().Be(CredentialJwt);
        segments.Skip(1).Take(3).Should().BeEquivalentTo([DGiven, DFamily, DPortrait]);
        var kbJwt = segments[^1];
        kbJwt.Count(c => c == '.').Should().Be(2);

        // KB-JWT payload binds aud + nonce and carries the RFC 9901 sd_hash of the exact prefix.
        var kbParts = kbJwt.Split('.');
        using var kbHeader = JsonDocument.Parse(Base64Url.DecodeFromChars(kbParts[0]));
        kbHeader.RootElement.GetProperty("typ").GetString().Should().Be("kb+jwt");
        kbHeader.RootElement.GetProperty("alg").GetString().Should().Be("EdDSA");
        using var kbPayload = JsonDocument.Parse(Base64Url.DecodeFromChars(kbParts[1]));
        kbPayload.RootElement.GetProperty("aud").GetString().Should().Be("did:sorcha:org:ws1qabc");
        kbPayload.RootElement.GetProperty("nonce").GetString().Should().Be("n-123");

        var expectedHashable = $"{CredentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";
        var expectedSdHash = Base64Url.EncodeToString(
            SHA256.HashData(Encoding.ASCII.GetBytes(expectedHashable)));
        kbPayload.RootElement.GetProperty("sd_hash").GetString().Should().Be(expectedSdHash);
    }

    [Fact]
    public async Task PresentAsync_PortraitWithheld_OmitsItsDisclosureAndHashesTheShorterPrefix()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Submitted);

        var form = http.RequestBodies[^1];
        var vpTokenJson = Uri.UnescapeDataString(
            form.Split('&').First(p => p.StartsWith("vp_token=")).Split('=', 2)[1].Replace('+', ' '));
        using var envelope = JsonDocument.Parse(vpTokenJson);
        var vp = envelope.RootElement.GetProperty("credential")[0].GetString()!;
        vp.Should().NotContain(DPortrait);

        var expectedSdHash = Base64Url.EncodeToString(SHA256.HashData(
            Encoding.ASCII.GetBytes($"{CredentialJwt}~{DGiven}~{DFamily}~")));
        var kbPayloadSeg = vp.Split('~')[^1].Split('.')[1];
        using var kbPayload = JsonDocument.Parse(Base64Url.DecodeFromChars(kbPayloadSeg));
        kbPayload.RootElement.GetProperty("sd_hash").GetString().Should().Be(expectedSdHash);
    }

    [Fact]
    public async Task PresentAsync_RequiredClaimNotConsented_FailsWithoutAnyHttpCall()
    {
        var (presenter, http) = Build();
        var result = await presenter.PresentAsync(Candidate(), ["givenName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task PresentAsync_SignKbAlgorithmMismatch_FailsBeforeDirectPost()
    {
        // Mirror of rehearse.ps1:599 — a mismatched signature would fail verification
        // downstream with no local error, so it must be refused here, loudly.
        var (presenter, http) = Build(signKbAlgorithm: "ES256");
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/callbacks/"));
    }

    [Fact]
    public async Task PresentAsync_CallbackDeclines_ReturnsDeclined()
    {
        var (presenter, _) = Build(callbackKind: "Decline");
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Declined);
        result.Detail.Should().Contain("Decline");
    }

    [Fact]
    public async Task PresentAsync_CallbackHttpError_ReturnsFailed()
    {
        var (presenter, _) = Build(callbackStatus: HttpStatusCode.InternalServerError);
        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName"]);
        result.Status.Should().Be(LocalPresentStatus.Failed);
    }

    /// <summary>
    /// #1330 finding 1 — a holder-cnf root and a device-cnf copy can share the same vct, so
    /// <c>/credentials/match</c> can't tell them apart. If the wallet exported the device copy,
    /// signing the KB-JWT with the session's holder key produces a KB-JWT the shared validator
    /// declines server-side — which CONSUMES the presentation request. The pre-check must catch
    /// the cnf-thumbprint mismatch BEFORE sign-kb or the callback are ever called.
    /// </summary>
    [Fact]
    public async Task PresentAsync_CnfThumbprintMismatch_FailsBeforeAnyServerCall()
    {
        var mismatchedJwk = new { kty = "OKP", crv = "Ed25519", x = "deviceKeyX" };
        var credentialJwt = BuildCredentialJwt(new { vct = "x", cnf = new { jwk = mismatchedJwk } });
        var rawToken = $"{credentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";
        var (presenter, http) = Build(rawToken: rawToken);

        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName", "portrait"]);

        result.Status.Should().Be(LocalPresentStatus.Failed);
        result.Detail.Should().Contain("bound to another device");
        http.Requests.Should().ContainSingle(r => r.RequestUri!.PathAndQuery.Contains("/export"));
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/sign-kb"));
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/callbacks/"));
    }

    /// <summary>
    /// A required claim absent from BOTH the disclosures and the JWT body would sign a KB-JWT
    /// over a vp_token that can never satisfy the requirement — a doomed direct_post that burns
    /// the gate on the server side. Mirror of rehearse.ps1:563's guard: fail locally first.
    /// </summary>
    [Fact]
    public async Task PresentAsync_RequiredClaimMissingFromExport_FailsBeforeSignKb()
    {
        // familyName is required but has no disclosure AND isn't a top-level JWT claim.
        var rawToken = $"{CredentialJwt}~{DGiven}~{DPortrait}~";
        var (presenter, http) = Build(rawToken: rawToken);

        var result = await presenter.PresentAsync(Candidate(), ["givenName", "familyName", "portrait"]);

        result.Status.Should().Be(LocalPresentStatus.Failed);
        result.Detail.Should().Contain("familyName");
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/sign-kb"));
        http.Requests.Should().NotContain(r => r.RequestUri!.PathAndQuery.Contains("/callbacks/"));
    }

    /// <summary>A cnf-MATCHING export (this session's holder key) must still proceed normally.</summary>
    [Fact]
    public async Task PresentAsync_CnfMatchingExport_ProceedsToSubmitted()
    {
        var jwk = new { kty = "OKP", crv = "Ed25519", x = "deviceKeyX" };
        var thumbprint = SorchaWalletLocalPresenter.ComputeJwkThumbprint(
            JsonDocument.Parse(JsonSerializer.Serialize(jwk)).RootElement);
        var credentialJwt = BuildCredentialJwt(new { vct = "x", cnf = new { jwk } });
        var rawToken = $"{credentialJwt}~{DGiven}~{DFamily}~{DPortrait}~";
        var (presenter, http) = Build(rawToken: rawToken);

        var candidate = new LocalPresentationCandidate
        {
            CredentialId = "urn:uuid:c1",
            WalletAddress = "ws1qcitizen",
            Vct = "https://sorcha.dev/vc/assured-identity/v1",
            RequiredClaims = ["givenName", "familyName"],
            OptionalClaims = ["portrait"],
            Nonce = "n-123",
            ClientId = "did:sorcha:org:ws1qabc",
            ResponseUri = "/api/presentations/callbacks/sorcha-wallet/rid-1",
            QueryId = "credential",
            RequestState = "rid-1",
            JoseAlgorithm = "EdDSA",
            KidThumbprint = thumbprint,
        };

        var result = await presenter.PresentAsync(candidate, ["givenName", "familyName", "portrait"]);

        result.Status.Should().Be(LocalPresentStatus.Submitted);
        http.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery.Contains("/sign-kb"));
        http.Requests.Should().Contain(r => r.RequestUri!.PathAndQuery.Contains("/callbacks/"));
    }
}
