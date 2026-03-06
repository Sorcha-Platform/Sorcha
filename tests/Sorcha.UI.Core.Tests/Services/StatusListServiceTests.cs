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
/// Unit tests for StatusListService.
/// </summary>
public class StatusListServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<StatusListService>> _loggerMock;
    private readonly StatusListService _service;

    public StatusListServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<StatusListService>>();
        _service = new StatusListService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
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

    #region GetStatusListAsync

    [Fact]
    public async Task GetStatusListAsync_Success_ReturnsViewModel()
    {
        // Arrange
        var expected = new StatusListViewModel
        {
            Id = "sl-001",
            Purpose = "revocation",
            IssuerDid = "did:sorcha:issuer-1",
            ValidFrom = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EncodedList = "H4sIAAAAAAAAA2NgGAWjYBSMglEwCkYBIQAA",
            ContextUrls = ["https://www.w3.org/2018/credentials/v1", "https://w3id.org/vc/status-list/2021/v1"]
        };
        SetupResponse(HttpMethod.Get, "/api/v1/credentials/status-lists/sl-001", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetStatusListAsync("sl-001");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be("sl-001");
        result.Purpose.Should().Be("revocation");
        result.IssuerDid.Should().Be("did:sorcha:issuer-1");
        result.ValidFrom.Should().Be(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        result.EncodedList.Should().Be("H4sIAAAAAAAAA2NgGAWjYBSMglEwCkYBIQAA");
        result.ContextUrls.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStatusListAsync_NotFound_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/v1/credentials/status-lists/nonexistent", HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetStatusListAsync("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetStatusListAsync_NetworkError_ReturnsNull()
    {
        // Arrange
        SetupException("/api/v1/credentials/status-lists/sl-err");

        // Act
        var result = await _service.GetStatusListAsync("sl-err");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
