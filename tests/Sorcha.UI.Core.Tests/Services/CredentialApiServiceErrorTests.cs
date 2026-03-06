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
/// Tests for CredentialApiService typed error handling (FR-024).
/// Verifies that lifecycle operations return specific error types for 403, 404, 409, 500.
/// </summary>
public class CredentialApiServiceErrorTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly CredentialApiService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public CredentialApiServiceErrorTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<CredentialApiService>>();
        _service = new CredentialApiService(_httpClient, logger.Object);
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

    // --- Suspend ---

    [Fact]
    public async Task SuspendCredentialAsync_Success_ReturnsOkResult()
    {
        var lifecycleResult = new CredentialLifecycleResult
        {
            CredentialId = "cred-1", NewStatus = "Suspended", PerformedBy = "w1",
            PerformedAt = DateTimeOffset.UtcNow, StatusListUpdated = true
        };
        SetupResponse(HttpStatusCode.OK, lifecycleResult);

        var result = await _service.SuspendCredentialAsync("cred-1", "w1", "test");

        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.NewStatus.Should().Be("Suspended");
        result.ErrorType.Should().Be(CredentialErrorType.None);
    }

    [Fact]
    public async Task SuspendCredentialAsync_403_ReturnsPermissionDenied()
    {
        SetupResponse(HttpStatusCode.Forbidden);

        var result = await _service.SuspendCredentialAsync("cred-1", "w1");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.PermissionDenied);
        result.ErrorMessage.Should().Contain("Permission denied");
    }

    [Fact]
    public async Task SuspendCredentialAsync_404_ReturnsNotFound()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.SuspendCredentialAsync("cred-missing", "w1");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.NotFound);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task SuspendCredentialAsync_409_ReturnsConflict()
    {
        SetupResponse(HttpStatusCode.Conflict);

        var result = await _service.SuspendCredentialAsync("cred-1", "w1");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.Conflict);
        result.ErrorMessage.Should().Contain("already");
    }

    [Fact]
    public async Task SuspendCredentialAsync_500_ReturnsServerError()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.SuspendCredentialAsync("cred-1", "w1");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.ServerError);
        result.ErrorMessage.Should().Contain("unexpected error");
    }

    [Fact]
    public async Task SuspendCredentialAsync_NetworkError_ReturnsNetworkError()
    {
        SetupNetworkError();

        var result = await _service.SuspendCredentialAsync("cred-1", "w1");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.NetworkError);
        result.ErrorMessage.Should().Contain("Network error");
    }

    // --- Reinstate ---

    [Fact]
    public async Task ReinstateCredentialAsync_Success_ReturnsOkResult()
    {
        var lifecycleResult = new CredentialLifecycleResult
        {
            CredentialId = "cred-2", NewStatus = "Active", PerformedBy = "w2",
            PerformedAt = DateTimeOffset.UtcNow, StatusListUpdated = true
        };
        SetupResponse(HttpStatusCode.OK, lifecycleResult);

        var result = await _service.ReinstateCredentialAsync("cred-2", "w2", "resolved");

        result.Success.Should().BeTrue();
        result.Data!.NewStatus.Should().Be("Active");
    }

    [Fact]
    public async Task ReinstateCredentialAsync_403_ReturnsPermissionDenied()
    {
        SetupResponse(HttpStatusCode.Forbidden);

        var result = await _service.ReinstateCredentialAsync("cred-2", "w2");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.PermissionDenied);
    }

    [Fact]
    public async Task ReinstateCredentialAsync_NetworkError_ReturnsNetworkError()
    {
        SetupNetworkError();

        var result = await _service.ReinstateCredentialAsync("cred-2", "w2");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.NetworkError);
    }

    // --- Refresh ---

    [Fact]
    public async Task RefreshCredentialAsync_Success_ReturnsOkResult()
    {
        var lifecycleResult = new CredentialLifecycleResult
        {
            CredentialId = "cred-3", NewStatus = "Active", PerformedBy = "w3",
            PerformedAt = DateTimeOffset.UtcNow, NewCredentialId = "cred-4"
        };
        SetupResponse(HttpStatusCode.OK, lifecycleResult);

        var result = await _service.RefreshCredentialAsync("cred-3", "w3", "P30D");

        result.Success.Should().BeTrue();
        result.Data!.NewCredentialId.Should().Be("cred-4");
    }

    [Fact]
    public async Task RefreshCredentialAsync_409_ReturnsConflict()
    {
        SetupResponse(HttpStatusCode.Conflict);

        var result = await _service.RefreshCredentialAsync("cred-3", "w3");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.Conflict);
    }

    [Fact]
    public async Task RefreshCredentialAsync_NetworkError_ReturnsNetworkError()
    {
        SetupNetworkError();

        var result = await _service.RefreshCredentialAsync("cred-3", "w3");

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(CredentialErrorType.NetworkError);
    }

    // --- CredentialOperationResult.FromStatusCode ---

    [Theory]
    [InlineData(403, CredentialErrorType.PermissionDenied)]
    [InlineData(404, CredentialErrorType.NotFound)]
    [InlineData(409, CredentialErrorType.Conflict)]
    [InlineData(500, CredentialErrorType.ServerError)]
    [InlineData(502, CredentialErrorType.ServerError)]
    public void FromStatusCode_MapsCorrectly(int statusCode, CredentialErrorType expected)
    {
        var result = CredentialOperationResult.FromStatusCode(statusCode);

        result.Success.Should().BeFalse();
        result.ErrorType.Should().Be(expected);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FromStatusCode_409WithCurrentStatus_IncludesStatusInMessage()
    {
        var result = CredentialOperationResult.FromStatusCode(409, "Suspended");

        result.ErrorMessage.Should().Contain("Suspended");
    }
}
