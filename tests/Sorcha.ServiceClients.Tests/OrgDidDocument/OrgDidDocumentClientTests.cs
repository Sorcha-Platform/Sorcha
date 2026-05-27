// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.OrgDidDocument;

namespace Sorcha.ServiceClients.Tests.OrgDidDocument;

/// <summary>
/// Tests for <see cref="OrgDidDocumentClient.ResolveCanonicalDidAsync"/> (Spec 5 —
/// verifier-DID resolution). The client fetches the org's published W3C DID
/// document and returns the canonical <c>id</c>, degrading to <c>null</c> on any
/// failure so callers can use it as a best-effort display identity.
/// </summary>
public class OrgDidDocumentClientTests
{
    private static readonly Guid OrgId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static OrgDidDocumentClient CreateClient(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://tenant-service:8080") },
            NullLogger<OrgDidDocumentClient>.Instance);

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

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly string _contentType;

        public string? LastRequestPath { get; private set; }

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
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType)
            });
        }
    }
}
