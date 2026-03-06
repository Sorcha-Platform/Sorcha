// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Credentials;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for presentation request auto-refresh polling behavior
/// via PresentationAdminService.GetPresentationResultAsync.
/// </summary>
public class PresentationAdminAutoRefreshTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<PresentationAdminService>> _loggerMock;
    private readonly PresentationAdminService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public PresentationAdminAutoRefreshTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<PresentationAdminService>>();
        _service = new PresentationAdminService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        foreach (var response in _responses)
            response.Dispose();
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    private void SetupResponse(HttpStatusCode statusCode, object? content = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent(statusCode.ToString())
        };
        _responses.Add(response);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/v1/presentations/") &&
                    req.RequestUri!.ToString().Contains("/result")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupSequentialResponses(params (HttpStatusCode statusCode, object? content)[] responseSpecs)
    {
        var setupSequence = _httpMessageHandlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/v1/presentations/") &&
                    req.RequestUri!.ToString().Contains("/result")),
                ItExpr.IsAny<CancellationToken>());

        foreach (var (statusCode, content) in responseSpecs)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = content != null ? JsonContent.Create(content) : new StringContent(statusCode.ToString())
            };
            _responses.Add(response);
            setupSequence = setupSequence.ReturnsAsync(response);
        }
    }

    private void SetupNetworkError()
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/v1/presentations/") &&
                    req.RequestUri!.ToString().Contains("/result")),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
    }

    #endregion

    #region Polling: Pending to Completed Transition

    [Fact]
    public async Task GetPresentationResultAsync_PendingThenCompleted_ReturnsUpdatedStatus()
    {
        // Arrange - first poll returns Pending (202), second returns Completed (200)
        var completedResult = new PresentationRequestResultViewModel
        {
            RequestId = "pres-poll-001",
            Status = "Completed",
            QrCodeUrl = "https://api.example.com/qr/pres-poll-001",
            RequestUrl = "https://api.example.com/presentations/pres-poll-001",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            VerificationResult = new { valid = true, claims = new { name = "Alice" } }
        };

        SetupSequentialResponses(
            (HttpStatusCode.Accepted, null),
            (HttpStatusCode.OK, completedResult));

        // Act - first poll (simulates initial check)
        var firstResult = await _service.GetPresentationResultAsync("pres-poll-001");

        // Assert - first poll is Pending
        firstResult.Should().NotBeNull();
        firstResult!.RequestId.Should().Be("pres-poll-001");
        firstResult.Status.Should().Be("Pending");

        // Act - second poll (simulates auto-refresh)
        var secondResult = await _service.GetPresentationResultAsync("pres-poll-001");

        // Assert - second poll is Completed with verification data
        secondResult.Should().NotBeNull();
        secondResult!.RequestId.Should().Be("pres-poll-001");
        secondResult.Status.Should().Be("Completed");
        secondResult.VerificationResult.Should().NotBeNull();
    }

    #endregion

    #region Polling: Pending Status

    [Fact]
    public async Task GetPresentationResultAsync_StillPending_ReturnsPendingStatus()
    {
        // Arrange - server returns 202 Accepted (still pending)
        SetupResponse(HttpStatusCode.Accepted);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-poll-002");

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-poll-002");
        result.Status.Should().Be("Pending");
        result.QrCodeUrl.Should().BeEmpty();
        result.VerificationResult.Should().BeNull();
    }

    #endregion

    #region Polling: Expired Requests

    [Fact]
    public async Task GetPresentationResultAsync_Expired_ReturnsExpiredStatus()
    {
        // Arrange - server returns 410 Gone (expired)
        SetupResponse(HttpStatusCode.Gone);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-poll-003");

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-poll-003");
        result.Status.Should().Be("Expired");
    }

    [Fact]
    public async Task GetPresentationResultAsync_PendingThenExpired_ReturnsExpiredOnSecondPoll()
    {
        // Arrange - first poll returns Pending, second returns Expired (410 Gone)
        SetupSequentialResponses(
            (HttpStatusCode.Accepted, null),
            (HttpStatusCode.Gone, null));

        // Act - first poll
        var firstResult = await _service.GetPresentationResultAsync("pres-poll-003b");

        // Assert
        firstResult.Should().NotBeNull();
        firstResult!.Status.Should().Be("Pending");

        // Act - second poll
        var secondResult = await _service.GetPresentationResultAsync("pres-poll-003b");

        // Assert
        secondResult.Should().NotBeNull();
        secondResult!.RequestId.Should().Be("pres-poll-003b");
        secondResult.Status.Should().Be("Expired");
    }

    #endregion

    #region Polling: Network Error Handling

    [Fact]
    public async Task GetPresentationResultAsync_NetworkError_ReturnsNull()
    {
        // Arrange - network failure (connection refused, timeout, etc.)
        SetupNetworkError();

        // Act
        var result = await _service.GetPresentationResultAsync("pres-poll-004");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPresentationResultAsync_ServerError_ReturnsNull()
    {
        // Arrange - server returns 500
        SetupResponse(HttpStatusCode.InternalServerError);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-poll-004b");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Polling: 404 Not Found

    [Fact]
    public async Task GetPresentationResultAsync_NotFound_ReturnsNull()
    {
        // Arrange - request ID does not exist
        SetupResponse(HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-poll-nonexistent");

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Polling: Multiple Sequential Status Transitions

    [Fact]
    public async Task GetPresentationResultAsync_MultiplePolls_TracksStatusTransitions()
    {
        // Arrange - simulate 3 pending polls then completed
        var completedResult = new PresentationRequestResultViewModel
        {
            RequestId = "pres-poll-005",
            Status = "Completed",
            QrCodeUrl = "https://api.example.com/qr/pres-poll-005",
            RequestUrl = "https://api.example.com/presentations/pres-poll-005",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3)
        };

        SetupSequentialResponses(
            (HttpStatusCode.Accepted, null),
            (HttpStatusCode.Accepted, null),
            (HttpStatusCode.Accepted, null),
            (HttpStatusCode.OK, completedResult));

        var statuses = new List<string>();

        // Act - simulate 4 polling cycles
        for (var i = 0; i < 4; i++)
        {
            var result = await _service.GetPresentationResultAsync("pres-poll-005");
            result.Should().NotBeNull();
            statuses.Add(result!.Status);
        }

        // Assert - three Pending then one Completed
        statuses.Should().HaveCount(4);
        statuses[0].Should().Be("Pending");
        statuses[1].Should().Be("Pending");
        statuses[2].Should().Be("Pending");
        statuses[3].Should().Be("Completed");
    }

    [Fact]
    public async Task GetPresentationResultAsync_PendingThenNetworkErrorThenCompleted_RecoverAfterError()
    {
        // Arrange - Pending, then network error, then Completed
        var completedResult = new PresentationRequestResultViewModel
        {
            RequestId = "pres-poll-006",
            Status = "Completed",
            QrCodeUrl = "https://api.example.com/qr/pres-poll-006",
            RequestUrl = "https://api.example.com/presentations/pres-poll-006",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2)
        };

        var pendingResponse = new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent("Accepted")
        };
        _responses.Add(pendingResponse);

        var completedResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(completedResult)
        };
        _responses.Add(completedResponse);

        _httpMessageHandlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/v1/presentations/") &&
                    req.RequestUri!.ToString().Contains("/result")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(pendingResponse)
            .ThrowsAsync(new HttpRequestException("Temporary network failure"))
            .ReturnsAsync(completedResponse);

        // Act & Assert - Poll 1: Pending
        var result1 = await _service.GetPresentationResultAsync("pres-poll-006");
        result1.Should().NotBeNull();
        result1!.Status.Should().Be("Pending");

        // Act & Assert - Poll 2: Network error returns null
        var result2 = await _service.GetPresentationResultAsync("pres-poll-006");
        result2.Should().BeNull();

        // Act & Assert - Poll 3: Recovery, returns Completed
        var result3 = await _service.GetPresentationResultAsync("pres-poll-006");
        result3.Should().NotBeNull();
        result3!.Status.Should().Be("Completed");
        result3.RequestId.Should().Be("pres-poll-006");
    }

    [Fact]
    public async Task GetPresentationResultAsync_CancellationRequested_ThrowsOperationCanceled()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Operation was canceled"));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => _service.GetPresentationResultAsync("pres-poll-007", cts.Token));
    }

    #endregion
}
