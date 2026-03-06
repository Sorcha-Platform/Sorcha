// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services.Admin;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for PushNotificationService (get status, subscribe, unsubscribe).
/// </summary>
public class PushNotificationServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly PushNotificationService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public PushNotificationServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<PushNotificationService>>();
        _service = new PushNotificationService(_httpClient, logger.Object);
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetupResponse(HttpStatusCode statusCode, object? content = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent("")
        };
        _responses.Add(response);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupNetworkError()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
    }

    // --- GetStatusAsync ---

    [Fact]
    public async Task GetStatusAsync_Active_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.OK, new PushSubscriptionStatus(true));

        var result = await _service.GetStatusAsync();

        result.HasActiveSubscription.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatusAsync_Inactive_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.OK, new PushSubscriptionStatus(false));

        var result = await _service.GetStatusAsync();

        result.HasActiveSubscription.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_NetworkError_ReturnsFalse()
    {
        SetupNetworkError();

        var result = await _service.GetStatusAsync();

        result.HasActiveSubscription.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatusAsync_ServerError_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.GetStatusAsync();

        result.HasActiveSubscription.Should().BeFalse();
    }

    // --- SubscribeAsync ---

    [Fact]
    public async Task SubscribeAsync_Success_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.OK, new PushSubscriptionResponse(true));

        var request = new PushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub/123",
            P256dh = "BNcRdreALRFXTkOOUHK1EtK2wtaz5Ry4YfYCA_0QTpQtUbVlUls0VJXg7A8u-Ts1XbjhazAkj7I99e8p8REfW28=",
            Auth = "tBHItJI5svbpC7i8Q15B2A=="
        };

        var result = await _service.SubscribeAsync(request);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task SubscribeAsync_Failed_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.OK, new PushSubscriptionResponse(false));

        var request = new PushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub/123",
            P256dh = "key",
            Auth = "auth"
        };

        var result = await _service.SubscribeAsync(request);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SubscribeAsync_NetworkError_ReturnsFalse()
    {
        SetupNetworkError();

        var request = new PushSubscriptionRequest
        {
            Endpoint = "https://push.example.com/sub/123",
            P256dh = "key",
            Auth = "auth"
        };

        var result = await _service.SubscribeAsync(request);

        result.Should().BeFalse();
    }

    // --- UnsubscribeAsync ---

    [Fact]
    public async Task UnsubscribeAsync_Success_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.NoContent);

        var result = await _service.UnsubscribeAsync("https://push.example.com/sub/123");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task UnsubscribeAsync_NotFound_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.UnsubscribeAsync("https://push.example.com/sub/missing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UnsubscribeAsync_NetworkError_ReturnsFalse()
    {
        SetupNetworkError();

        var result = await _service.UnsubscribeAsync("https://push.example.com/sub/123");

        result.Should().BeFalse();
    }
}
