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
/// Unit tests for PresentationAdminService (create presentation requests, get results).
/// </summary>
public class PresentationAdminServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<PresentationAdminService>> _loggerMock;
    private readonly PresentationAdminService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public PresentationAdminServiceTests()
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

    private static CreatePresentationRequestViewModel CreateValidRequest() => new()
    {
        CredentialType = "VerifiableCredential",
        AcceptedIssuers = ["did:sorcha:issuer-1", "did:sorcha:issuer-2"],
        RequiredClaims = ["name", "email"],
        CallbackUrl = "https://verifier.example.com/callback",
        TargetWalletAddress = "sorcha1abc123",
        TtlSeconds = 600,
        VerifierIdentity = "did:sorcha:verifier-1"
    };

    #endregion

    #region CreatePresentationRequestAsync

    [Fact]
    public async Task CreatePresentationRequestAsync_Success_ReturnsResult()
    {
        // Arrange
        var request = CreateValidRequest();
        var expected = new PresentationRequestResultViewModel
        {
            RequestId = "pres-req-001",
            Status = "Pending",
            QrCodeUrl = "https://api.example.com/qr/pres-req-001",
            RequestUrl = "https://api.example.com/presentations/pres-req-001",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        };
        SetupResponse(HttpMethod.Post, "/api/v1/presentations/request", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.CreatePresentationRequestAsync(request);

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-req-001");
        result.Status.Should().Be("Pending");
        result.QrCodeUrl.Should().Be("https://api.example.com/qr/pres-req-001");
        result.RequestUrl.Should().Be("https://api.example.com/presentations/pres-req-001");
    }

    [Fact]
    public async Task CreatePresentationRequestAsync_ValidationError_ReturnsNull()
    {
        // Arrange
        var request = CreateValidRequest();
        SetupResponse(HttpMethod.Post, "/api/v1/presentations/request", HttpStatusCode.BadRequest);

        // Act
        var result = await _service.CreatePresentationRequestAsync(request);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task CreatePresentationRequestAsync_AuthError_ReturnsNull()
    {
        // Arrange
        var request = CreateValidRequest();
        SetupResponse(HttpMethod.Post, "/api/v1/presentations/request", HttpStatusCode.Forbidden);

        // Act
        var result = await _service.CreatePresentationRequestAsync(request);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region GetPresentationResultAsync

    [Fact]
    public async Task GetPresentationResultAsync_Completed_ReturnsResult()
    {
        // Arrange
        var expected = new PresentationRequestResultViewModel
        {
            RequestId = "pres-req-001",
            Status = "Completed",
            QrCodeUrl = "https://api.example.com/qr/pres-req-001",
            RequestUrl = "https://api.example.com/presentations/pres-req-001",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };
        SetupResponse(HttpMethod.Get, "/api/v1/presentations/pres-req-001/result", HttpStatusCode.OK, expected);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-req-001");

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-req-001");
        result.Status.Should().Be("Completed");
        result.QrCodeUrl.Should().Be("https://api.example.com/qr/pres-req-001");
    }

    [Fact]
    public async Task GetPresentationResultAsync_Pending_ReturnsPendingResult()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/v1/presentations/pres-req-002/result", HttpStatusCode.Accepted);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-req-002");

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-req-002");
        result.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task GetPresentationResultAsync_Expired_ReturnsExpiredResult()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/v1/presentations/pres-req-003/result", HttpStatusCode.Gone);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-req-003");

        // Assert
        result.Should().NotBeNull();
        result!.RequestId.Should().Be("pres-req-003");
        result.Status.Should().Be("Expired");
    }

    [Fact]
    public async Task GetPresentationResultAsync_NotFound_ReturnsNull()
    {
        // Arrange
        SetupResponse(HttpMethod.Get, "/api/v1/presentations/pres-req-404/result", HttpStatusCode.NotFound);

        // Act
        var result = await _service.GetPresentationResultAsync("pres-req-404");

        // Assert
        result.Should().BeNull();
    }

    #endregion
}
