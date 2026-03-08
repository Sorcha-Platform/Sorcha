// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OidcDiscoveryServiceTests
{
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<IMemoryCache> _cacheMock;
    private readonly Mock<ILogger<OidcDiscoveryService>> _loggerMock;

    private const string ValidIssuerUrl = "https://login.example.com/tenant-id/v2.0";
    private const string WellKnownPath = "/.well-known/openid-configuration";

    private static readonly string ValidDiscoveryJson = JsonSerializer.Serialize(new
    {
        issuer = "https://login.example.com/tenant-id/v2.0",
        authorization_endpoint = "https://login.example.com/tenant-id/oauth2/v2.0/authorize",
        token_endpoint = "https://login.example.com/tenant-id/oauth2/v2.0/token",
        userinfo_endpoint = "https://login.example.com/oidc/userinfo",
        jwks_uri = "https://login.example.com/common/discovery/v2.0/keys",
        scopes_supported = new[] { "openid", "profile", "email", "offline_access" }
    });

    public OidcDiscoveryServiceTests()
    {
        _httpHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpHandlerMock.Object);
        _cacheMock = new Mock<IMemoryCache>();
        _loggerMock = new Mock<ILogger<OidcDiscoveryService>>();

        // Default cache setup: cache miss (TryGetValue returns false)
        object? cacheOutValue = null;
        _cacheMock
            .Setup(x => x.TryGetValue(It.IsAny<object>(), out cacheOutValue))
            .Returns(false);

        // Setup cache CreateEntry to return a mock ICacheEntry
        var cacheEntryMock = new Mock<ICacheEntry>();
        cacheEntryMock.SetupAllProperties();
        _cacheMock
            .Setup(x => x.CreateEntry(It.IsAny<object>()))
            .Returns(cacheEntryMock.Object);
    }

    #region DiscoverAsync Tests

    [Fact]
    public async Task DiscoverAsync_ValidIssuerUrl_ReturnsDiscoveryResponse()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, ValidDiscoveryJson);
        var service = CreateService();

        // Act
        var result = await service.DiscoverAsync(ValidIssuerUrl);

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().Be("https://login.example.com/tenant-id/v2.0");
        result.AuthorizationEndpoint.Should().Be("https://login.example.com/tenant-id/oauth2/v2.0/authorize");
        result.TokenEndpoint.Should().Be("https://login.example.com/tenant-id/oauth2/v2.0/token");
        result.UserInfoEndpoint.Should().Be("https://login.example.com/oidc/userinfo");
        result.JwksUri.Should().Be("https://login.example.com/common/discovery/v2.0/keys");

        // Verify HTTP call was made to the well-known endpoint
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.Method == HttpMethod.Get &&
                req.RequestUri!.ToString().Contains(WellKnownPath)),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DiscoverAsync_CachedResult_ReturnsCachedWithoutHttpCall()
    {
        // Arrange
        var cachedResponse = new DiscoveryResponse
        {
            Issuer = ValidIssuerUrl,
            AuthorizationEndpoint = "https://cached/authorize",
            TokenEndpoint = "https://cached/token"
        };

        object? cacheOutValue = cachedResponse;
        _cacheMock
            .Setup(x => x.TryGetValue(It.IsAny<object>(), out cacheOutValue))
            .Returns(true);

        var service = CreateService();

        // Act
        var result = await service.DiscoverAsync(ValidIssuerUrl);

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().Be(ValidIssuerUrl);
        result.AuthorizationEndpoint.Should().Be("https://cached/authorize");

        // Verify NO HTTP call was made (served from cache)
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DiscoverAsync_EmptyOrWhitespaceUrl_ThrowsArgumentException(string invalidUrl)
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = async () => await service.DiscoverAsync(invalidUrl);

        // Assert — ArgumentException.ThrowIfNullOrWhiteSpace validates input
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DiscoverAsync_UnreachableUrl_ThrowsInvalidOperationException()
    {
        // Arrange
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService();

        // Act
        var act = async () => await service.DiscoverAsync(ValidIssuerUrl);

        // Assert — HttpRequestException is caught and wrapped in InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unable to reach OIDC discovery endpoint*");
    }

    [Fact]
    public async Task DiscoverAsync_InvalidJson_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "this is not valid json {{{");
        var service = CreateService();

        // Act
        var act = async () => await service.DiscoverAsync(ValidIssuerUrl);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not valid JSON*");
    }

    [Fact]
    public async Task DiscoverAsync_ExtractsAllEndpoints_FromValidDocument()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, ValidDiscoveryJson);
        var service = CreateService();

        // Act
        var result = await service.DiscoverAsync(ValidIssuerUrl);

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().NotBeNullOrWhiteSpace();
        result.AuthorizationEndpoint.Should().NotBeNullOrWhiteSpace();
        result.TokenEndpoint.Should().NotBeNullOrWhiteSpace();
        result.UserInfoEndpoint.Should().NotBeNullOrWhiteSpace();
        result.JwksUri.Should().NotBeNullOrWhiteSpace();
        result.SupportedScopes.Should().NotBeEmpty();
        result.SupportedScopes.Should().Contain("openid");
        result.SupportedScopes.Should().Contain("profile");
        result.SupportedScopes.Should().Contain("email");
        result.SupportedScopes.Should().Contain("offline_access");
        result.SupportedScopes.Should().HaveCount(4);
    }

    [Fact]
    public async Task DiscoverAsync_NonSuccessStatusCode_ThrowsInvalidOperationException()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.NotFound, "Not Found");
        var service = CreateService();

        // Act
        var act = async () => await service.DiscoverAsync(ValidIssuerUrl);

        // Assert — EnsureSuccessStatusCode throws HttpRequestException, caught and wrapped
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unable to reach OIDC discovery endpoint*");
    }

    [Fact]
    public async Task DiscoverAsync_TrailingSlashOnIssuerUrl_NormalizesBeforeFetch()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, ValidDiscoveryJson);
        var service = CreateService();

        // Act
        var result = await service.DiscoverAsync(ValidIssuerUrl + "/");

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().NotBeNullOrWhiteSpace();

        // Verify the URL was normalized (no double slashes before .well-known)
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                !req.RequestUri!.ToString().Contains("//" + WellKnownPath.TrimStart('/'))),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task DiscoverAsync_PartialDocument_ReturnsMissingFieldsAsNull()
    {
        // Arrange
        var partialJson = JsonSerializer.Serialize(new
        {
            issuer = "https://minimal.example.com",
            authorization_endpoint = "https://minimal.example.com/authorize",
            token_endpoint = "https://minimal.example.com/token"
            // Missing: userinfo_endpoint, jwks_uri, scopes_supported
        });

        SetupHttpResponse(HttpStatusCode.OK, partialJson);
        var service = CreateService();

        // Act
        var result = await service.DiscoverAsync("https://minimal.example.com");

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().Be("https://minimal.example.com");
        result.AuthorizationEndpoint.Should().Be("https://minimal.example.com/authorize");
        result.TokenEndpoint.Should().Be("https://minimal.example.com/token");
        result.UserInfoEndpoint.Should().BeNull();
        result.JwksUri.Should().BeNull();
        result.SupportedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task DiscoverAsync_CachesResultWith24HourTtl()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, ValidDiscoveryJson);

        var cacheEntryMock = new Mock<ICacheEntry>();
        cacheEntryMock.SetupAllProperties();
        _cacheMock
            .Setup(x => x.CreateEntry(It.IsAny<object>()))
            .Returns(cacheEntryMock.Object);

        var service = CreateService();

        // Act
        await service.DiscoverAsync(ValidIssuerUrl);

        // Assert — verify cache entry was created
        _cacheMock.Verify(x => x.CreateEntry(It.IsAny<object>()), Times.Once);

        // Verify the TTL is set to approximately 24 hours
        cacheEntryMock.VerifySet(x => x.AbsoluteExpirationRelativeToNow = It.Is<TimeSpan?>(
            ts => ts.HasValue && ts.Value.TotalHours >= 23.9 && ts.Value.TotalHours <= 24.1));
    }

    #endregion

    #region InvalidateCache Tests

    [Fact]
    public void InvalidateCache_RemovesCachedEntry()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.InvalidateCache(ValidIssuerUrl);

        // Assert
        _cacheMock.Verify(x => x.Remove(It.Is<object>(key =>
            key.ToString()!.Contains(ValidIssuerUrl))), Times.Once);
    }

    [Fact]
    public void InvalidateCache_NonCachedUrl_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act
        var act = () => service.InvalidateCache("https://not-cached.example.com");

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task DiscoverAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var service = CreateService();

        // Act
        var act = async () => await service.DiscoverAsync(ValidIssuerUrl, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates the service under test.
    /// NOTE: The concrete OidcDiscoveryService class does not exist yet.
    /// When implemented, it should accept:
    ///   - HttpClient (via IHttpClientFactory or direct injection)
    ///   - IMemoryCache
    ///   - ILogger&lt;OidcDiscoveryService&gt;
    /// Update this method once the implementation is created.
    /// </summary>
    private IOidcDiscoveryService CreateService()
    {
        // TODO: Replace with concrete implementation once OidcDiscoveryService is created.
        // Expected constructor signature:
        //   new OidcDiscoveryService(HttpClient httpClient, IMemoryCache cache, ILogger<OidcDiscoveryService> logger)
        //
        // For now, this will cause a compilation error until the implementation exists,
        // which is the expected TDD workflow.
        return new OidcDiscoveryService(
            _httpClient,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _httpHandlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
    }

    #endregion
}
