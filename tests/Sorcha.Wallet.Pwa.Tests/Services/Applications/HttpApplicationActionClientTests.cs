// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.UI.Core.Services.HolderKeys;
using Sorcha.Wallet.Pwa.Services.Applications;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Services.Applications;

/// <summary>
/// P0 fix (<c>fix/pwa-p0-claim-and-camera</c>) — <c>HttpApplicationActionClient.LoadFormAsync</c> must
/// read the instance-scoped <c>GET /api/instances/{id}/actions/{actionId}</c> endpoint (not the
/// authoring-only <c>GET /api/blueprints/{id}</c>, which 403s for a consumer-tier citizen token) and
/// must surface a 403 as <see cref="ApplicationFormLoadStatus.Forbidden"/> — never collapse it into a
/// bare failure that the caller could mistake for "offline".
/// </summary>
public sealed class HttpApplicationActionClientTests
{
    private static readonly Guid InstanceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private const string InstanceJson = """
        { "blueprintId": "bp-1", "registerId": "reg-1", "currentActionIds": [1], "title": "Claim your Assured Identity credential" }
        """;

    private const string ActionSchemaJson = """
        { "actionId": 1, "title": "Claim your Assured Identity credential",
          "form": { "type": "Layout", "title": "", "scope": "" },
          "dataSchemas": [ { "type": "object", "properties": { "email": { "type": "string" } } } ] }
        """;

    [Fact]
    public async Task LoadFormAsync_ActionEndpointReturns403_ReturnsForbidden_NotNullOrOffline()
    {
        // This is the exact P0 regression: the server refuses (403) and the caller must be told
        // "forbidden", never asked to guess that it means "you must be offline".
        var client = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                return Ok(InstanceJson);
            }
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.Forbidden);
        result.Context.Should().BeNull();
    }

    [Fact]
    public async Task LoadFormAsync_ActionEndpointReturns401_ReturnsForbidden()
    {
        var client = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                return Ok(InstanceJson);
            }
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.Forbidden);
    }

    [Fact]
    public async Task LoadFormAsync_ServerError500_ReturnsNetworkError_NotForbidden()
    {
        var client = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                return Ok(InstanceJson);
            }
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.NetworkError);
    }

    [Fact]
    public async Task LoadFormAsync_ActionEndpointReturns404_ReturnsNotFound()
    {
        var client = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                return Ok(InstanceJson);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.NotFound);
    }

    [Fact]
    public async Task LoadFormAsync_Success_ReadsFromInstanceScopedActionEndpoint_NotBlueprintEndpoint()
    {
        var hitBlueprintEndpoint = false;
        var client = Create(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("/api/blueprints/", StringComparison.Ordinal))
            {
                hitBlueprintEndpoint = true;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                return Ok(InstanceJson);
            }
            if (req.RequestUri!.AbsolutePath.EndsWith($"/api/instances/{InstanceId:D}/actions/1", StringComparison.Ordinal))
            {
                return Ok(ActionSchemaJson);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        hitBlueprintEndpoint.Should().BeFalse("the authoring-only blueprint endpoint must never be called from this client");
        result.Status.Should().Be(ApplicationFormLoadStatus.Loaded);
        result.Context.Should().NotBeNull();
        result.Context!.Action.Title.Should().Be("Claim your Assured Identity credential");
        result.Context.Action.Id.Should().Be(1);
        result.Context.SenderWallet.Should().Be("ws1qcitizen");
    }

    [Fact]
    public async Task LoadFormAsync_InstanceEndpointReturns403_ReturnsForbidden()
    {
        var client = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden), keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.Forbidden);
    }

    // Regression for the #1195 Phase 2 live bind failure (2026-07-17): the server generates, stores
    // (text column), and matches instance ids in the canonical hyphenated ("D") Guid form
    // (Guid.NewGuid().ToString()); this client formatted the URL Guid as ":N" (no hyphens), so every
    // GET /api/instances/{id} 404'd — surfacing as "The device-binding workflow could not be prepared
    // (NotFound)". The handler here serves ONLY the hyphenated path, reproducing the server, so it fails
    // against the ":N" implementation and passes against ":D".
    [Fact]
    public async Task LoadFormAsync_UsesCanonicalHyphenatedInstanceId_MatchesServerStoredForm()
    {
        string? requestedInstancePath = null;
        var client = Create(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith($"/api/instances/{InstanceId:D}", StringComparison.Ordinal))
            {
                requestedInstancePath = path;
                return Ok(InstanceJson);
            }
            if (path.EndsWith($"/api/instances/{InstanceId:D}/actions/1", StringComparison.Ordinal))
            {
                return Ok(ActionSchemaJson);
            }
            // The server has no route for the no-hyphen ("N") form — it 404s exactly as n1 did.
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }, keys: Keys());

        var result = await client.LoadFormAsync(InstanceId);

        result.Status.Should().Be(ApplicationFormLoadStatus.Loaded);
        requestedInstancePath.Should().NotBeNull("the client must request the hyphenated instance id the server stores");
        requestedInstancePath.Should().Contain("-", "the instance id in the URL must be the canonical hyphenated Guid form");
    }

    private static HolderKeysView Keys() => new() { WalletAddress = "ws1qcitizen" };

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpApplicationActionClient Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        HolderKeysView? keys)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://test.example.com") };
        var holderKeys = new Mock<IHolderKeyClient>();
        holderKeys.Setup(h => h.GetHolderKeysAsync(It.IsAny<CancellationToken>())).ReturnsAsync(keys);
        return new HttpApplicationActionClient(http, holderKeys.Object, NullLogger<HttpApplicationActionClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request));
    }
}
