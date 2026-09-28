// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Peer;

namespace Sorcha.ServiceClients.Tests.Peer;

/// <summary>
/// #1474: <see cref="IPeerServiceClient.SubscribeToRegisterAsync"/> used to be a fire-and-forget
/// <c>Task</c> that swallowed every non-success HTTP response into a log line the caller could not
/// observe. It now returns whether a subscription actually exists afterwards, so a caller (the
/// system register bootstrapper, the subscribe-notification endpoint, the MCP admin tool) can react
/// to a refusal instead of assuming success.
/// </summary>
public class PeerServiceClientSubscribeTests
{
    private readonly Mock<IServiceAuthClient> _serviceAuthMock;
    private readonly Mock<ILogger<PeerServiceClient>> _loggerMock;
    private readonly IConfiguration _configuration;

    public PeerServiceClientSubscribeTests()
    {
        _serviceAuthMock = new Mock<IServiceAuthClient>();
        _serviceAuthMock.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");

        _loggerMock = new Mock<ILogger<PeerServiceClient>>();

        // No Peer gRPC address configured — the client must skip gRPC channel construction and
        // rely solely on the injected HttpClient for the REST subscribe call under test.
        _configuration = new ConfigurationBuilder().Build();
    }

    private PeerServiceClient CreateClient(Mock<HttpMessageHandler> handlerMock)
    {
        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("http://peer-service.local")
        };
        return new PeerServiceClient(_configuration, _loggerMock.Object, httpClient, _serviceAuthMock.Object);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, string? body = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();

        var response = new HttpResponseMessage(statusCode);
        if (body != null)
        {
            response.Content = new StringContent(body);
        }

        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return handlerMock;
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_Http200_ReturnsTrue()
    {
        var handler = CreateMockHandler(HttpStatusCode.Created);
        var client = CreateClient(handler);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_Http409AlreadySubscribed_ReturnsTrue()
    {
        // The Peer Service's idempotent-check response — this IS the desired end state, not a
        // failure. A caller re-subscribing after a restart must not be told the subscription is
        // broken (see SystemRegisterBootstrapper's remark this fixes: it used to log an unconditional
        // success line regardless of what actually happened here).
        var handler = CreateMockHandler(HttpStatusCode.Conflict, "{\"error\":\"Already subscribed to register 'reg-1'.\"}");
        var client = CreateClient(handler);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_Http500_ReturnsFalse()
    {
        var handler = CreateMockHandler(HttpStatusCode.InternalServerError, "boom");
        var client = CreateClient(handler);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_Http401_ReturnsFalse()
    {
        // The historical bug (#1474): a service-to-service auth failure here produced only a log
        // line — the caller had no way to know the subscription was never created.
        var handler = CreateMockHandler(HttpStatusCode.Unauthorized, "unauthorized");
        var client = CreateClient(handler);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_HttpException_ReturnsFalse()
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = CreateClient(handlerMock);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeToRegisterAsync_NoHttpClientConfigured_ReturnsFalse()
    {
        var client = new PeerServiceClient(_configuration, _loggerMock.Object);

        var result = await client.SubscribeToRegisterAsync("reg-1", "full-replica");

        result.Should().BeFalse();
    }
}
