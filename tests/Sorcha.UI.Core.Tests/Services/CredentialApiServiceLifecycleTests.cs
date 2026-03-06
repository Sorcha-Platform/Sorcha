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
/// Unit tests for CredentialApiService lifecycle methods (suspend, reinstate, refresh).
/// </summary>
public class CredentialApiServiceLifecycleTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<CredentialApiService>> _loggerMock;
    private readonly CredentialApiService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public CredentialApiServiceLifecycleTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<CredentialApiService>>();
        _service = new CredentialApiService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        foreach (var response in _responses)
            response.Dispose();
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Helper Methods

    private void SetupResponse(HttpMethod method, string urlFragment, HttpStatusCode statusCode, object? content = null)
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
                    req.Method == method &&
                    req.RequestUri!.ToString().Contains(urlFragment)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupException(string urlFragment)
    {
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.ToString().Contains(urlFragment)),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));
    }

    #endregion

    #region SuspendCredentialAsync

    [Fact]
    public async Task SuspendCredentialAsync_Success_ReturnsResult()
    {
        // Arrange
        var expected = new CredentialLifecycleResult
        {
            CredentialId = "cred-123",
            NewStatus = "Suspended",
            PerformedBy = "wallet-abc",
            PerformedAt = DateTimeOffset.UtcNow,
            Reason = "Policy violation",
            StatusListUpdated = true,
            NewCredentialId = null
        };
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-123/suspend", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.SuspendCredentialAsync("cred-123", "wallet-abc", "Policy violation");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.CredentialId.Should().Be("cred-123");
        result.Data.NewStatus.Should().Be("Suspended");
        result.Data.PerformedBy.Should().Be("wallet-abc");
        result.Data.Reason.Should().Be("Policy violation");
        result.Data.StatusListUpdated.Should().BeTrue();
        result.Data.NewCredentialId.Should().BeNull();
    }

    [Fact]
    public async Task SuspendCredentialAsync_WrongState_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-123/suspend", HttpStatusCode.Conflict);

        // Act
        var result = await _service.SuspendCredentialAsync("cred-123", "wallet-abc", "Already suspended");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SuspendCredentialAsync_Forbidden_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-123/suspend", HttpStatusCode.Forbidden);

        // Act
        var result = await _service.SuspendCredentialAsync("cred-123", "wallet-abc");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task SuspendCredentialAsync_NetworkError_ReturnsNull()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.SuspendCredentialAsync("cred-123", "wallet-abc");

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region ReinstateCredentialAsync

    [Fact]
    public async Task ReinstateCredentialAsync_Success_ReturnsResult()
    {
        // Arrange
        var expected = new CredentialLifecycleResult
        {
            CredentialId = "cred-456",
            NewStatus = "Active",
            PerformedBy = "wallet-def",
            PerformedAt = DateTimeOffset.UtcNow,
            Reason = "Issue resolved",
            StatusListUpdated = true,
            NewCredentialId = null
        };
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-456/reinstate", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.ReinstateCredentialAsync("cred-456", "wallet-def", "Issue resolved");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.CredentialId.Should().Be("cred-456");
        result.Data.NewStatus.Should().Be("Active");
        result.Data.PerformedBy.Should().Be("wallet-def");
        result.Data.Reason.Should().Be("Issue resolved");
        result.Data.StatusListUpdated.Should().BeTrue();
        result.Data.NewCredentialId.Should().BeNull();
    }

    [Fact]
    public async Task ReinstateCredentialAsync_WrongState_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-456/reinstate", HttpStatusCode.Conflict);

        // Act
        var result = await _service.ReinstateCredentialAsync("cred-456", "wallet-def", "Not suspended");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReinstateCredentialAsync_Forbidden_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-456/reinstate", HttpStatusCode.Forbidden);

        // Act
        var result = await _service.ReinstateCredentialAsync("cred-456", "wallet-def");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ReinstateCredentialAsync_NetworkError_ReturnsNull()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.ReinstateCredentialAsync("cred-456", "wallet-def");

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion

    #region RefreshCredentialAsync

    [Fact]
    public async Task RefreshCredentialAsync_Success_ReturnsResult()
    {
        // Arrange
        var expected = new CredentialLifecycleResult
        {
            CredentialId = "cred-789",
            NewStatus = "Active",
            PerformedBy = "wallet-ghi",
            PerformedAt = DateTimeOffset.UtcNow,
            Reason = null,
            StatusListUpdated = false,
            NewCredentialId = "cred-790"
        };
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-789/refresh", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.RefreshCredentialAsync("cred-789", "wallet-ghi", "P30D");

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.CredentialId.Should().Be("cred-789");
        result.Data.NewStatus.Should().Be("Active");
        result.Data.PerformedBy.Should().Be("wallet-ghi");
        result.Data.Reason.Should().BeNull();
        result.Data.StatusListUpdated.Should().BeFalse();
        result.Data.NewCredentialId.Should().Be("cred-790");
    }

    [Fact]
    public async Task RefreshCredentialAsync_WrongState_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-789/refresh", HttpStatusCode.Conflict);

        // Act
        var result = await _service.RefreshCredentialAsync("cred-789", "wallet-ghi");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshCredentialAsync_Forbidden_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Post, "/api/v1/credentials/cred-789/refresh", HttpStatusCode.Forbidden);

        // Act
        var result = await _service.RefreshCredentialAsync("cred-789", "wallet-ghi");

        // Assert
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task RefreshCredentialAsync_NetworkError_ReturnsNull()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.RefreshCredentialAsync("cred-789", "wallet-ghi");

        // Assert
        result.Success.Should().BeFalse();
    }

    #endregion
}
