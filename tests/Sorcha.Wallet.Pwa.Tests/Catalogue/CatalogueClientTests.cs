// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Catalogue;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Catalogue;

/// <summary>
/// Feature 154 (B) — HttpCatalogueClient maps /api/catalogue and starts a service via /api/instances/.
/// </summary>
public sealed class CatalogueClientTests
{
    [Fact]
    public async Task GetServicesAsync_MapsItems()
    {
        var client = Create((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/catalogue");
            return Ok("""[{"blueprintId":"bp-1","title":"Blue Badge","description":"Apply for a blue badge","registerId":"reg-1"}]""");
        });

        var items = await client.GetServicesAsync();

        items.Should().ContainSingle();
        items[0].BlueprintId.Should().Be("bp-1");
        items[0].Title.Should().Be("Blue Badge");
        items[0].RegisterId.Should().Be("reg-1");
    }

    [Fact]
    public async Task GetServicesAsync_TransientFailure_Throws()
    {
        var client = Create((_, _) => throw new HttpRequestException("offline"));
        await client.Invoking(c => c.GetServicesAsync()).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task StartAsync_PostsCreateInstance_ReturnsNewId()
    {
        var client = Create((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/instances/");
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"11111111-1111-1111-1111-111111111111"}""", Encoding.UTF8, "application/json"),
            };
        });

        var id = await client.StartAsync(new CatalogueItem("bp-1", "Blue Badge", null, "reg-1"));

        id.Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task StartAsync_Failure_ReturnsNull()
    {
        var client = Create((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        (await client.StartAsync(new CatalogueItem("bp-1", "x", null, "reg-1"))).Should().BeNull();
    }

    private static HttpCatalogueClient Create(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://test.example.com") };
        return new HttpCatalogueClient(http);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request, ct));
    }
}
