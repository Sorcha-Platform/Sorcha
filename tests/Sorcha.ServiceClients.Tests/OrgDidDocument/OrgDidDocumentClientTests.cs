// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.OrgDidDocument;

namespace Sorcha.ServiceClients.Tests.OrgDidDocument;

/// <summary>
/// Tests for <see cref="OrgDidDocumentClient.ResolveCanonicalDidAsync"/> (Spec 5 —
/// verifier-DID resolution). The client fetches the org's published WC DID
/// document and returns the canonical <c>id</c>, degrading to <c>null</c> on any
/// failure so callers can use it as a best-effort display identity.
/// </summary>
public class OrgDidDocumentClientTests
{
    private static readonly Guid OrgId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static OrgDidDocumentClient CreateClient(
        StubHandler handler, IServiceAuthClient? serviceAuth = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://tenant-service:8080") },
            serviceAuth ?? StubServiceAuth("test-service-token"),
            NullLogger<OrgDidDocumentClient>.Instance);

    private static IServiceAuthClient StubServiceAuth(string? token)
    {
        var mock = new Mock<IServiceAuthClient>();
        mock.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(token);
        return mock.Object;
    }

    [Fact]
    public async Task ResolveCanonicalDidAsync_Returns_IdField_On200()
    {
        const string did = "did:sorcha:org:ws11qstrathcarron";
        var handler = new StubHandler(HttpStatusCode.OK,
            $$"""{"id":"{{did}}","verificationMethod":[]}""", "application/did+json");

        var result = await CreateClient(handler).ResolveCanonicalDidAsync(OrgId);

        result.Should().Be(did);
        handler.LastRequestPath.Should().Be($"/orgs/{OrgId}/did.json");
    }

    [Fact]
    public async Task ResolveCanonicalDidAsync_ReturnsNull_On404()
    {
        // Org has never issued a credential → no published DID document.
        var handler = new StubHandler(HttpStatusCode.NotFound, "", "text/plain");

        var result = await CreateClient(handler).ResolveCanonicalDidAsync(OrgId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCanonicalDidAsync_ReturnsNull_WhenIdMissing()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"verificationMethod":[]}""", "application/did+json");

        var result = await CreateClient(handler).ResolveCanonicalDidAsync(OrgId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCanonicalDidAsync_ReturnsNull_OnMalformedJson()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{not json", "application/did+json");

        var result = await CreateClient(handler).ResolveCanonicalDidAsync(OrgId);

        result.Should().BeNull();
    }

    // ── C1 (catch-up review 2026-07-29): the Tenant regenerate endpoint is now
    //    RequireService. The client MUST present a service token, and MUST NOT send an
    //    anonymous request when it cannot get one — an unauthenticated POST would be a
    //    401 that RegenerateAsync swallows into `false`, silently ending DID publishing.

    [Fact]
    public async Task RegenerateAsync_AttachesServiceBearerToken()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}", "application/json");

        var result = await CreateClient(handler).RegenerateAsync(SnapshotRequest());

        result.Should().BeTrue();
        handler.LastRequestPath.Should().Be($"/orgs/{OrgId}/did-document/regenerate");
        handler.LastAuthorizationScheme.Should().Be("Bearer");
        handler.LastAuthorizationParameter.Should().Be("test-service-token");
    }

    [Fact]
    public async Task RegenerateAsync_NoServiceToken_FailsClosed_AndSendsNothing()
    {
        // Fail closed: an anonymous POST to a RequireService endpoint returns 401, which
        // this client maps to `false` — indistinguishable from a real regeneration failure.
        // Refuse to send at all so the cause is unambiguous.
        var handler = new StubHandler(HttpStatusCode.OK, "{}", "application/json");

        var result = await CreateClient(handler, StubServiceAuth(null)).RegenerateAsync(SnapshotRequest());

        result.Should().BeFalse();
        handler.LastRequestPath.Should().BeNull("no request may be sent without a service token");
    }

    private static OrgDidRegenerateRequest SnapshotRequest() =>
        new(OrgId, "key-derivation", "ws11qstrathcarron",
            [new OrgDidActiveKey(1, "ED25519", "{\"kty\":\"OKP\"}", "thumb-1")]);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _contentType;

        public string? LastRequestPath { get; private set; }
        public string? LastAuthorizationScheme { get; private set; }
        public string? LastAuthorizationParameter { get; private set; }

        public StubHandler(HttpStatusCode status, string body, string contentType)
        {
            _status = status;
            _body = body;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestPath = request.RequestUri?.AbsolutePath;
            // The helper sets DefaultRequestHeaders on the HttpClient, which surfaces here
            // on the outbound request — assert what actually goes on the wire.
            LastAuthorizationScheme = request.Headers.Authorization?.Scheme;
            LastAuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType)
            });
        }
    }
}
