// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models.Admin;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for ServicePrincipalService HTTP client logic.
/// Tests verify correct HTTP methods, URLs, query strings, and response handling.
/// </summary>
public class ServicePrincipalServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<ServicePrincipalService>> _loggerMock;
    private readonly ServicePrincipalService _service;

    public ServicePrincipalServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _loggerMock = new Mock<ILogger<ServicePrincipalService>>();
        _service = new ServicePrincipalService(_httpClient, _loggerMock.Object);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    #region ListAsync

    [Fact]
    public async Task ListAsync_Success_ReturnsItems()
    {
        // Arrange
        var expected = new ServicePrincipalListResult
        {
            Items =
            [
                new ServicePrincipalViewModel
                {
                    Id = Guid.NewGuid(),
                    ServiceName = "Blueprint Service",
                    ClientId = "bp-client-001",
                    Status = "active",
                    Scopes = ["blueprints:read", "blueprints:write"]
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
                    req.RequestUri!.ToString().Contains("/api/service-principals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.ListAsync(false, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        result.Items[0].ServiceName.Should().Be("Blueprint Service");
        result.Items[0].ClientId.Should().Be("bp-client-001");
        result.Items[0].Status.Should().Be("active");
        result.Items[0].Scopes.Should().Contain("blueprints:read");
    }

    [Fact]
    public async Task ListAsync_IncludeInactiveTrue_SendsCorrectQueryString()
    {
        // Arrange
        var expected = new ServicePrincipalListResult { Items = [] };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains("includeInactive=true")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.ListAsync(true, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().BeEmpty();

        _httpMessageHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.Query.Contains("includeInactive=true", StringComparison.OrdinalIgnoreCase)),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_NetworkError_ThrowsHttpRequestException()
    {
        // Arrange
        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network error"));

        // Act
        var act = () => _service.ListAsync(false, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Network error*");
    }

    #endregion

    #region GetAsync

    [Fact]
    public async Task GetAsync_Success_ReturnsServicePrincipal()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expected = new ServicePrincipalViewModel
        {
            Id = id,
            ServiceName = "Register Service",
            ClientId = "reg-client-001",
            Status = "active",
            Scopes = ["registers:read"]
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
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.GetAsync(id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.ServiceName.Should().Be("Register Service");
        result.ClientId.Should().Be("reg-client-001");
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        // Arrange
        var id = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not Found")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var act = () => _service.GetAsync(id, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task CreateAsync_Success_ReturnsSecret()
    {
        // Arrange
        var request = new CreateServicePrincipalRequest
        {
            ServiceName = "New Service",
            Scopes = ["blueprints:read"],
            ExpirationDuration = ExpirationPreset.NinetyDays
        };

        var expected = new ServicePrincipalSecretViewModel
        {
            ClientId = "new-client-001",
            ClientSecret = "generated-secret-xyz"
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().EndsWith("/api/service-principals")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.CreateAsync(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be("new-client-001");
        result.ClientSecret.Should().Be("generated-secret-xyz");
    }

    [Fact]
    public async Task CreateAsync_Failure_ThrowsHttpRequestException()
    {
        // Arrange
        var request = new CreateServicePrincipalRequest
        {
            ServiceName = "Bad Service",
            Scopes = [],
            ExpirationDuration = ExpirationPreset.ThirtyDays
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().EndsWith("/api/service-principals")),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Server error"));

        // Act
        var act = () => _service.CreateAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    #endregion

    #region UpdateScopesAsync

    [Fact]
    public async Task UpdateScopesAsync_Success_ReturnsUpdatedPrincipal()
    {
        // Arrange
        var id = Guid.NewGuid();
        var newScopes = new[] { "registers:read", "registers:write", "blueprints:read" };
        var expected = new ServicePrincipalViewModel
        {
            Id = id,
            ServiceName = "Updated Service",
            ClientId = "upd-client-001",
            Status = "active",
            Scopes = newScopes
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Put &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}/scopes")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.UpdateScopesAsync(id, newScopes, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(id);
        result.Scopes.Should().HaveCount(3);
        result.Scopes.Should().Contain("registers:write");
    }

    #endregion

    #region SuspendAsync

    [Fact]
    public async Task SuspendAsync_Success_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}/suspend")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.SuspendAsync(id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SuspendAsync_Failure_ReturnsFalse()
    {
        // Arrange
        var id = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Cannot suspend")
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}/suspend")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.SuspendAsync(id, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ReactivateAsync

    [Fact]
    public async Task ReactivateAsync_Success_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}/reactivate")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.ReactivateAsync(id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region RevokeAsync

    [Fact]
    public async Task RevokeAsync_Success_ReturnsTrue()
    {
        // Arrange
        var id = Guid.NewGuid();
        var response = new HttpResponseMessage(HttpStatusCode.OK);

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Delete &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.RevokeAsync(id, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region RotateSecretAsync

    [Fact]
    public async Task RotateSecretAsync_Success_ReturnsNewSecret()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expected = new ServicePrincipalSecretViewModel
        {
            ClientId = "existing-client-001",
            ClientSecret = "rotated-secret-abc"
        };

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };

        _httpMessageHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Post &&
                    req.RequestUri!.ToString().Contains($"/api/service-principals/{id}/rotate-secret")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var result = await _service.RotateSecretAsync(id, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be("existing-client-001");
        result.ClientSecret.Should().Be("rotated-secret-abc");
    }

    #endregion
}
