// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.UI.Core.Services.User.Presentation;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Presentation;

public class SorchaWalletLocalPresenterProbeTests
{
    private const string Vct = "https://sorcha.dev/vc/assured-identity/v1";

    /// <summary>
    /// Unsigned request-object JWT with the payload fields the real endpoint serves. The
    /// <c>dcql_query</c> mirrors the exact wire shape <c>DcqlRequestBuilder.BuildCredentialQuery</c>
    /// produces for a required+optional ask (claim ids assigned only once an optional claim
    /// exists; claim_sets = [everything, required-only-floor]) — not a hand-written guess, since
    /// <see cref="Sorcha.Verifier.Engine.Dcql.DcqlCredentialQuery.Validate"/> rejects claim_sets
    /// entries that reference an id no claim declares.
    /// </summary>
    private static string BuildRequestObjectJwt(
        string? nonce = "n-123", string clientId = "did:sorcha:org:ws1qabc",
        string responseUri = "https://unit.test/api/presentations/callbacks/sorcha-wallet/rid-1",
        string state = "rid-1")
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"oauth-authz-req+jwt"}"""));
        var payload = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["response_uri"] = responseUri,
            ["nonce"] = nonce,
            ["state"] = state,
            ["response_mode"] = "direct_post",
            ["dcql_query"] = JsonDocument.Parse($$"""
                {"credentials":[{"id":"credential","format":"dc+sd-jwt",
                  "meta":{"vct_values":["{{Vct}}"]},
                  "claims":[{"id":"c0","path":["givenName"]},{"id":"c1","path":["familyName"]},{"id":"c2","path":["portrait"]}],
                  "claim_sets":[["c0","c1","c2"],["c0","c1"]]}]}
                """).RootElement,
        };
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        return $"{header}.{payloadSeg}.";
    }

    private static (SorchaWalletLocalPresenter Presenter, CapturingHandler Http,
        Mock<IHolderKeyClient> Keys, Mock<ICredentialApiService> Creds)
        Build(string requestObjectJwt, string baseAddress = "https://unit.test/")
    {
        var handler = new CapturingHandler(req =>
            req.RequestUri!.PathAndQuery.Contains("/request-object")
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(requestObjectJwt) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var http = new HttpClient(handler) { BaseAddress = new Uri(baseAddress) };
        var keys = new Mock<IHolderKeyClient>();
        keys.Setup(k => k.GetHolderKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HolderKeysView
            {
                HolderJwk = JsonDocument.Parse("""{"kty":"OKP","crv":"Ed25519","x":"abc"}""").RootElement,
                Algorithm = "ED25519",
                WalletAddress = "ws1qcitizen",
                EncryptionPublicKey = "pk",
            });
        var creds = new Mock<ICredentialApiService>();
        creds.Setup(c => c.MatchCredentialsAsync("ws1qcitizen",
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult
                { RequirementType = Vct, Matched = true, CredentialId = "urn:uuid:c1", IssuerDid = "did:sorcha:org:ws1qabc" }]);
        var presenter = new SorchaWalletLocalPresenter(
            http, keys.Object, creds.Object, TimeProvider.System,
            NullLogger<SorchaWalletLocalPresenter>.Instance);
        return (presenter, handler, keys, creds);
    }

    private static string DeepLink(string requestUri = "https://unit.test/api/presentations/rid-1/request-object")
        => $"openid4vp://authorize?request_uri={Uri.EscapeDataString(requestUri)}";

    [Fact]
    public async Task ProbeAsync_MatchingCredential_ReturnsCandidateWithRequestObjectFields()
    {
        var (presenter, _, _, _) = Build(BuildRequestObjectJwt());
        var candidate = await presenter.ProbeAsync(DeepLink());
        candidate.Should().NotBeNull();
        candidate!.Vct.Should().Be(Vct);
        candidate.Nonce.Should().Be("n-123");
        candidate.ClientId.Should().Be("did:sorcha:org:ws1qabc");
        candidate.ResponseUri.Should().Be("/api/presentations/callbacks/sorcha-wallet/rid-1"); // relative
        candidate.QueryId.Should().Be("credential");
        candidate.RequestState.Should().Be("rid-1");
        candidate.RequiredClaims.Should().BeEquivalentTo(["givenName", "familyName"]);
        candidate.OptionalClaims.Should().BeEquivalentTo(["portrait"]);
        candidate.JoseAlgorithm.Should().Be("EdDSA");
        candidate.WalletAddress.Should().Be("ws1qcitizen");
        candidate.CredentialId.Should().Be("urn:uuid:c1");
    }

    [Fact]
    public async Task ProbeAsync_CrossOriginRequestUri_ReturnsNullWithoutFetching()
    {
        var (presenter, http, _, _) = Build(BuildRequestObjectJwt());
        var candidate = await presenter.ProbeAsync(DeepLink("https://evil.example/api/presentations/x/request-object"));
        candidate.Should().BeNull();
        http.Requests.Should().BeEmpty("a cross-origin request_uri must not be fetched at all");
    }

    [Fact]
    public async Task ProbeAsync_CrossOriginResponseUri_ReturnsNull()
    {
        var jwt = BuildRequestObjectJwt(responseUri: "https://evil.example/collect");
        var (presenter, _, _, _) = Build(jwt);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull(
            "the bearer-carrying direct_post must never target a foreign origin");
    }

    [Fact]
    public async Task ProbeAsync_NoMatchingCredential_ReturnsNull()
    {
        var (presenter, _, _, creds) = Build(BuildRequestObjectJwt());
        creds.Setup(c => c.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CredentialMatchResult { RequirementType = Vct, Matched = false }]);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_HolderKeysUnavailable_ReturnsNull()
    {
        var (presenter, _, keys, _) = Build(BuildRequestObjectJwt());
        keys.Setup(k => k.GetHolderKeysAsync(It.IsAny<CancellationToken>())).ReturnsAsync((HolderKeysView?)null);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_MatchThrows_ReturnsNull()
    {
        // #1324: MatchCredentialsAsync throws on transport failure. The probe swallows it —
        // a probe failure degrades to the QR route, never to a dead end.
        var (presenter, _, _, creds) = Build(BuildRequestObjectJwt());
        creds.Setup(c => c.MatchCredentialsAsync(It.IsAny<string>(),
                It.IsAny<List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("boom"));
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_MissingNonce_ReturnsNull()
    {
        var (presenter, _, _, _) = Build(BuildRequestObjectJwt(nonce: null));
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull();
    }

    /// <summary>
    /// #1330 finding 2 — a two-requirement SorchaWallet action emits a two-credential DCQL
    /// query. Local consent only ever satisfies ONE credential per share, so presenting a single
    /// candidate against a multi-credential ask would silently leave the second requirement
    /// unverified (the very failure #1311 closed loudly on the QR path). The probe must decline
    /// the local route entirely and degrade to QR, not just pick <c>Credentials[0]</c>.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_MultiCredentialQuery_ReturnsNull()
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"oauth-authz-req+jwt"}"""));
        var payload = new Dictionary<string, object?>
        {
            ["client_id"] = "did:sorcha:org:ws1qabc",
            ["response_uri"] = "https://unit.test/api/presentations/callbacks/sorcha-wallet/rid-1",
            ["nonce"] = "n-123",
            ["state"] = "rid-1",
            ["response_mode"] = "direct_post",
            ["dcql_query"] = JsonDocument.Parse($$"""
                {"credentials":[
                    {"id":"credential","format":"dc+sd-jwt","meta":{"vct_values":["{{Vct}}"]},
                      "claims":[{"id":"c0","path":["givenName"]}]},
                    {"id":"credential2","format":"dc+sd-jwt","meta":{"vct_values":["https://sorcha.dev/vc/other/v1"]},
                      "claims":[{"id":"c1","path":["licenceNumber"]}]}
                  ]}
                """).RootElement,
        };
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var jwt = $"{header}.{payloadSeg}.";

        var (presenter, _, _, _) = Build(jwt);
        (await presenter.ProbeAsync(DeepLink())).Should().BeNull(
            "multi-credential local consent is out of scope — degrade to the QR route, which fails loudly by design (#1311)");
    }
}

/// <summary>Records every request and answers via the supplied responder.</summary>
internal sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct));
        return responder(request);
    }
}
