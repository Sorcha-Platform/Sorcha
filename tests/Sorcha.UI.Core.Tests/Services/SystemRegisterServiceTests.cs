// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for SystemRegisterService.
/// Tests verify HTTP calls, response deserialization, and error handling.
/// </summary>
public class SystemRegisterServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<SystemRegisterService>> _loggerMock;
    private readonly SystemRegisterService _service;

    public SystemRegisterServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<SystemRegisterService>>();
        _service = new SystemRegisterService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    // ──────────────────────────────────────────────
    // GetStatusAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetStatusAsync_Success_ReturnsViewModel()
    {
        // Arrange
        var expected = new SystemRegisterViewModel
        {
            RegisterId = "sys-reg-001",
            DisplayName = "System Register",
            IsInitialized = true,
            BlueprintCount = 12,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30)
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetStatusAsync(CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.RegisterId.Should().Be("sys-reg-001");
        result.DisplayName.Should().Be("System Register");
        result.IsInitialized.Should().BeTrue();
        result.BlueprintCount.Should().Be(12);
        result.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStatusAsync_ServerError_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetStatusAsync(CancellationToken.None);

        // Assert
        result.Should().BeNull("server returned 500");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStatusAsync_NetworkError_ReturnsNullAndLogsError()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var result = await _service.GetStatusAsync(CancellationToken.None);

        // Assert
        result.Should().BeNull("exception occurred during API call");

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ──────────────────────────────────────────────
    // GetBlueprintsAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetBlueprintsAsync_Success_ReturnsPageResult()
    {
        // Arrange
        var expected = new BlueprintPageResult
        {
            TotalCount = 2,
            Items =
            [
                new BlueprintSummaryViewModel
                {
                    BlueprintId = "bp-001",
                    Version = 1,
                    PublishedAt = DateTimeOffset.UtcNow,
                    PublishedBy = "admin",
                    IsActive = true
                },
                new BlueprintSummaryViewModel
                {
                    BlueprintId = "bp-002",
                    Version = 3,
                    PublishedAt = DateTimeOffset.UtcNow.AddDays(-5),
                    PublishedBy = "user1",
                    IsActive = false
                }
            ]
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintsAsync(1, 10, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
        result.Items[0].BlueprintId.Should().Be("bp-001");
        result.Items[1].BlueprintId.Should().Be("bp-002");
    }

    [Fact]
    public async Task GetBlueprintsAsync_ServerError_ReturnsEmptyResult()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintsAsync(1, 10, CancellationToken.None);

        // Assert
        result.Should().NotBeNull("service returns empty result on failure, not null");
        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetBlueprintsAsync_VerifiesQueryParameters()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new BlueprintPageResult { TotalCount = 0 })
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("page=2") &&
                    req.RequestUri!.ToString().Contains("pageSize=25")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintsAsync(2, 25, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        _httpMessageHandlerMock.Protected()
            .Verify(
                "SendAsync",
                Times.Once(),
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("page=2") &&
                    req.RequestUri!.ToString().Contains("pageSize=25")),
                ItExpr.IsAny<CancellationToken>());
    }

    // ──────────────────────────────────────────────
    // GetBlueprintAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetBlueprintAsync_Success_ReturnsBlueprintDetail()
    {
        // Arrange
        var expected = new BlueprintDetailViewModel
        {
            BlueprintId = "bp-001",
            Title = "Test Blueprint",
            Version = 2,
            PublishedAt = DateTimeOffset.UtcNow,
            PublishedBy = "admin",
            IsActive = true,
            Description = "A test blueprint",
            ParticipantCount = 2,
            ActionCount = 4,
            AvailableVersions = [1, 2]
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints/bp-001")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintAsync("bp-001", CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.BlueprintId.Should().Be("bp-001");
        result.Title.Should().Be("Test Blueprint");
        result.Version.Should().Be(2);
        result.IsActive.Should().BeTrue();
        result.Description.Should().Be("A test blueprint");
        result.AvailableVersions.Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task GetBlueprintAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints/nonexistent")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintAsync("nonexistent", CancellationToken.None);

        // Assert
        result.Should().BeNull("blueprint does not exist");
    }

    // ──────────────────────────────────────────────
    // GetBlueprintVersionAsync
    // ──────────────────────────────────────────────

    [Fact]
    public async Task GetBlueprintVersionAsync_Success_ReturnsVersionDetail()
    {
        // Arrange
        var expected = new BlueprintDetailViewModel
        {
            BlueprintId = "bp-001",
            Title = "Test Blueprint v1",
            Version = 1,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-10),
            PublishedBy = "admin",
            IsActive = false,
            Description = "A test blueprint version 1",
            ParticipantCount = 3,
            ActionCount = 5,
            AvailableVersions = [1, 2, 3]
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints/bp-001/versions/1")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintVersionAsync("bp-001", 1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.BlueprintId.Should().Be("bp-001");
        result.Version.Should().Be(1);
        result.Title.Should().Be("Test Blueprint v1");
        result.IsActive.Should().BeFalse();
        result.AvailableVersions.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetBlueprintVersionAsync_ServerError_ReturnsNull()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("/api/admin/system-register/blueprints/bp-001/versions/99")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetBlueprintVersionAsync("bp-001", 99, CancellationToken.None);

        // Assert
        result.Should().BeNull("server returned 500");
    }
}
