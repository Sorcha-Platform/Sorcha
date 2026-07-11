// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Trust;
using Xunit;

namespace Sorcha.ServiceClients.Tests.Trust;

/// <summary>
/// Feature 181 US3 (T035) — the caching HTTP trust-list provider maps the Tenant anchors response to a
/// snapshot, fails closed (null) on 404 TRUSTLIST_UNAVAILABLE, and serves the 15-minute in-process
/// cache before re-hitting the endpoint.
/// </summary>
public sealed class HttpTrustListProviderTests
{
    private sealed class CountingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(responder(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private static (HttpTrustListProvider Provider, CountingHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> responder, FakeTimeProvider clock)
    {
        var handler = new CountingHandler(responder);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(HttpTrustListProvider.HttpClientName))
            .Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://tenant-service:8080") });
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("service-token");

        var provider = new HttpTrustListProvider(
            factory.Object, auth.Object, NullLogger<HttpTrustListProvider>.Instance, clock);
        return (provider, handler);
    }

    [Fact]
    public async Task GetSnapshotAsync_ValidResponse_MapsSnapshot()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-11T00:00:00Z"));
        var body = /*lang=json,strict*/ """
        {"trustListId":"eu-lotl","sequenceNumber":42,"freshness":"Fresh",
         "nextUpdate":"2026-12-31T00:00:00Z","rootsDerBase64":["AQID","BAUG"]}
        """;
        var (provider, _) = Build(_ => Json(body), clock);

        var snapshot = await provider.GetSnapshotAsync("eu-lotl");

        snapshot.Should().NotBeNull();
        snapshot!.Id.Should().Be("eu-lotl");
        snapshot.SequenceNumber.Should().Be(42);
        snapshot.FreshnessState.Should().Be("Fresh");
        snapshot.NextUpdate.Should().Be(DateTimeOffset.Parse("2026-12-31T00:00:00Z"));
        snapshot.Roots.Should().HaveCount(2);
        snapshot.Roots[0].Should().Equal((byte)1, (byte)2, (byte)3);
    }

    [Fact]
    public async Task GetSnapshotAsync_NotFound_ReturnsNull_FailClosed()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-11T00:00:00Z"));
        var (provider, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound), clock);

        (await provider.GetSnapshotAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetSnapshotAsync_CachesWithinTtl_ThenRefetchesAfterExpiry()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-11T00:00:00Z"));
        var body = /*lang=json,strict*/ """{"trustListId":"eu-lotl","sequenceNumber":1,"freshness":"Fresh","rootsDerBase64":["AQID"]}""";
        var (provider, handler) = Build(_ => Json(body), clock);

        await provider.GetSnapshotAsync("eu-lotl");
        clock.Advance(TimeSpan.FromMinutes(10));
        await provider.GetSnapshotAsync("eu-lotl");
        handler.Calls.Should().Be(1, "the 15-minute cache serves the second read");

        clock.Advance(TimeSpan.FromMinutes(10)); // now 20 min > 15 min TTL
        await provider.GetSnapshotAsync("eu-lotl");
        handler.Calls.Should().Be(2, "the cache expired and the provider re-fetched");
    }
}
