// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.ServiceClients.Trust;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Trust;

/// <summary>
/// Feature 096 US3 consumer — round-trips the Tenant Service's cert-chain
/// endpoint through <see cref="TrustServiceClient"/>. A fake HTTP handler
/// lets the test assert the exact path + parse the response body without a
/// running Tenant Service, while also verifying the 5-minute cache behaviour.
/// </summary>
public class TrustServiceClientTests
{
    private const string TenantId = "tenant-test-1";
    private const string OrgWallet = "ws1qtestorg";

    [Fact]
    public async Task GetChainForAsync_OkResponse_DecodesBothCerts()
    {
        var handler = new RecordingHandler(BuildOkResponse(orgCertAscii: "ORG_CERT", rootCertAscii: "ROOT_CERT"));
        var sut = Build(handler);

        var chain = await sut.GetChainForAsync(TenantId, OrgWallet);

        chain.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(chain!.OrgCertDer).Should().Be("ORG_CERT");
        System.Text.Encoding.UTF8.GetString(chain.RootCertDer).Should().Be("ROOT_CERT");

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.AbsolutePath
            .Should().Be($"/api/v1/trust/tenants/{TenantId}/orgs/{OrgWallet}/cert-chain");
    }

    [Fact]
    public async Task GetChainForAsync_NotFound_ReturnsNull()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Build(handler);

        var chain = await sut.GetChainForAsync(TenantId, OrgWallet);

        chain.Should().BeNull();
    }

    [Fact]
    public async Task GetChainForAsync_MalformedBase64_ReturnsNull_NoThrow()
    {
        // Attacker-controlled Tenant Service response must not crash the Wallet.
        var body = new { orgCertBase64 = "!!!not-base64!!!", rootCertBase64 = "!!!also-bad!!!" };
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        var handler = new RecordingHandler(resp);
        var sut = Build(handler);

        var chain = await sut.GetChainForAsync(TenantId, OrgWallet);

        chain.Should().BeNull();
    }

    [Fact]
    public async Task GetChainForAsync_CachesWithinTtl_SkipsSecondHttpCall()
    {
        // First call: real HTTP round-trip. Second call within TTL: served
        // from cache. The cert rotation cadence (yearly) means a 5-minute TTL
        // is safe and prevents per-issuance chain fetches.
        var handler = new RecordingHandler(() => BuildOkResponse("A", "B"));
        var sut = Build(handler, cacheTtl: TimeSpan.FromMinutes(5));

        var first = await sut.GetChainForAsync(TenantId, OrgWallet);
        var second = await sut.GetChainForAsync(TenantId, OrgWallet);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        handler.Requests.Should().HaveCount(1, "second call within TTL must be served from cache");
    }

    [Fact]
    public async Task GetChainForAsync_NotFoundIsCached_AvoidsHammeringTenantService()
    {
        // When enrolment is pending, the caller may retry tightly — the short
        // miss cache keeps that from hammering the Tenant Service.
        var handler = new RecordingHandler(() => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Build(handler);

        var first = await sut.GetChainForAsync(TenantId, OrgWallet);
        var second = await sut.GetChainForAsync(TenantId, OrgWallet);

        first.Should().BeNull();
        second.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetChainForAsync_TransientHttpError_ReturnsNull()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("dns-fail"));
        var sut = Build(handler);

        var chain = await sut.GetChainForAsync(TenantId, OrgWallet);

        chain.Should().BeNull();
    }

    [Fact]
    public void AsJwsChain_OrdersLeafThenRoot()
    {
        // RFC 7515 §4.1.6: x5c is leaf-first. The helper must preserve that
        // order or the verifier will treat the root as the issuer key.
        var chain = new OrgCertChain(new byte[] { 1, 2 }, new byte[] { 9, 9 });
        var jws = chain.AsJwsChain();
        jws[0].Should().BeEquivalentTo(new byte[] { 1, 2 });
        jws[1].Should().BeEquivalentTo(new byte[] { 9, 9 });
    }

    // --- helpers ---

    private static TrustServiceClient Build(HttpMessageHandler handler, TimeSpan? cacheTtl = null)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://tenant.test/") };
        return new TrustServiceClient(http, NullLogger<TrustServiceClient>.Instance, cacheTtl);
    }

    private static HttpResponseMessage BuildOkResponse(string orgCertAscii, string rootCertAscii)
    {
        var body = new
        {
            orgCertBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(orgCertAscii)),
            rootCertBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(rootCertAscii)),
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _producer;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(HttpResponseMessage fixedResponse)
            : this(_ => fixedResponse) { }

        public RecordingHandler(Func<HttpResponseMessage> factory)
            : this(_ => factory()) { }

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> producer)
        {
            _producer = producer;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_producer(request));
        }
    }
}
