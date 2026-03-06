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
/// Unit tests for EventAdminService (get events, delete event).
/// </summary>
public class EventAdminServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly EventAdminService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public EventAdminServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<EventAdminService>>();
        _service = new EventAdminService(_httpClient, logger.Object);
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

    private void SetupResponseWithRequestCapture(HttpStatusCode statusCode, object? content,
        Action<HttpRequestMessage> captureCallback)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent("")
        };
        _responses.Add(response);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => captureCallback(req))
            .ReturnsAsync(response);
    }

    private void SetupNetworkError()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
    }

    // --- GetEventsAsync ---

    [Fact]
    public async Task GetEventsAsync_Success_ReturnsEvents()
    {
        var expected = new EventListResponse
        {
            Events =
            [
                new SystemEventViewModel
                {
                    Id = "evt-1", Type = "UserLogin", Severity = "Info",
                    Message = "User logged in", Source = "Tenant",
                    Timestamp = DateTimeOffset.UtcNow
                },
                new SystemEventViewModel
                {
                    Id = "evt-2", Type = "WalletCreated", Severity = "Info",
                    Message = "Wallet created", Source = "Wallet",
                    Timestamp = DateTimeOffset.UtcNow
                }
            ],
            TotalCount = 2, Page = 1, PageSize = 20
        };
        SetupResponse(HttpStatusCode.OK, expected);

        var result = await _service.GetEventsAsync();

        result.Events.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
        result.Events[0].Id.Should().Be("evt-1");
    }

    [Fact]
    public async Task GetEventsAsync_WithSeverityFilter_SendsFilterInQuery()
    {
        HttpRequestMessage? capturedRequest = null;
        var expected = new EventListResponse { Events = [], TotalCount = 0, Page = 1, PageSize = 20 };
        SetupResponseWithRequestCapture(HttpStatusCode.OK, expected, req => capturedRequest = req);

        var filter = new EventFilterModel { Severity = "Error", Page = 1, PageSize = 20 };
        await _service.GetEventsAsync(filter);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("severity=Error");
    }

    [Fact]
    public async Task GetEventsAsync_WithPagination_SendsPageParams()
    {
        HttpRequestMessage? capturedRequest = null;
        var expected = new EventListResponse { Events = [], TotalCount = 0, Page = 3, PageSize = 50 };
        SetupResponseWithRequestCapture(HttpStatusCode.OK, expected, req => capturedRequest = req);

        var filter = new EventFilterModel { Page = 3, PageSize = 50 };
        await _service.GetEventsAsync(filter);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("page=3");
        capturedRequest.RequestUri!.Query.Should().Contain("pageSize=50");
    }

    [Fact]
    public async Task GetEventsAsync_NetworkError_ReturnsEmptyResponse()
    {
        SetupNetworkError();

        var result = await _service.GetEventsAsync();

        result.Events.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetEventsAsync_ServerError_ReturnsEmptyResponse()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.GetEventsAsync();

        result.Events.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetEventsAsync_WithSinceFilter_SendsSinceParam()
    {
        HttpRequestMessage? capturedRequest = null;
        var expected = new EventListResponse { Events = [], TotalCount = 0, Page = 1, PageSize = 20 };
        SetupResponseWithRequestCapture(HttpStatusCode.OK, expected, req => capturedRequest = req);

        var since = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var filter = new EventFilterModel { Since = since, Page = 1, PageSize = 20 };
        await _service.GetEventsAsync(filter);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.Query.Should().Contain("since=");
    }

    [Fact]
    public async Task GetEventsAsync_EmptyResponse_ReturnsEmptyList()
    {
        var expected = new EventListResponse { Events = [], TotalCount = 0, Page = 1, PageSize = 20 };
        SetupResponse(HttpStatusCode.OK, expected);

        var result = await _service.GetEventsAsync();

        result.Events.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    // --- DeleteEventAsync ---

    [Fact]
    public async Task DeleteEventAsync_Success_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.NoContent);

        var result = await _service.DeleteEventAsync("evt-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteEventAsync_NotFound_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.DeleteEventAsync("evt-missing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteEventAsync_Forbidden_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.Forbidden);

        var result = await _service.DeleteEventAsync("evt-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteEventAsync_NetworkError_ReturnsFalse()
    {
        SetupNetworkError();

        var result = await _service.DeleteEventAsync("evt-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteEventAsync_ServerError_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.DeleteEventAsync("evt-1");

        result.Should().BeFalse();
    }
}
