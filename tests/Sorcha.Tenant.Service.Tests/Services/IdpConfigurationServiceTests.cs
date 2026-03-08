// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;
using System.Net;
using System.Text.Json;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

public class IdpConfigurationServiceTests : IDisposable
{
    private readonly TenantDbContext _dbContext;
    private readonly Mock<IOidcDiscoveryService> _discoveryServiceMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<IdpConfigurationService>> _loggerMock;
    private readonly IIdpConfigurationService _service;

    private readonly Guid _testOrgId = Guid.NewGuid();

    public IdpConfigurationServiceTests()
    {
        _dbContext = InMemoryDbContextFactory.Create();
        _discoveryServiceMock = new Mock<IOidcDiscoveryService>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<IdpConfigurationService>>();

        // Seed a test organization
        _dbContext.Organizations.Add(new Organization
        {
            Id = _testOrgId,
            Name = "Test Organization",
            Subdomain = "test-org",
            Status = OrganizationStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow
        });
        _dbContext.SaveChanges();

        _service = new IdpConfigurationService(
            _dbContext,
            _discoveryServiceMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        GC.SuppressFinalize(this);
    }

    #region GetConfigurationAsync Tests

    [Fact]
    public async Task GetConfigurationAsync_ExistingConfig_ReturnsResponse()
    {
        // Arrange
        var config = CreateTestIdpConfig(_testOrgId);
        _dbContext.IdentityProviderConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetConfigurationAsync(_testOrgId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(config.Id);
        result.IssuerUrl.Should().Be(config.IssuerUrl);
        result.ProviderPreset.Should().Be(nameof(IdentityProviderType.MicrosoftEntra));
        result.IsEnabled.Should().Be(config.IsEnabled);
        result.Scopes.Should().BeEquivalentTo(config.Scopes);
        result.AuthorizationEndpoint.Should().Be(config.AuthorizationEndpoint);
        result.TokenEndpoint.Should().Be(config.TokenEndpoint);
        result.UserInfoEndpoint.Should().Be(config.UserInfoEndpoint);
    }

    [Fact]
    public async Task GetConfigurationAsync_NoConfig_ReturnsNull()
    {
        // Arrange
        var nonExistentOrgId = Guid.NewGuid();

        // Act
        var result = await _service.GetConfigurationAsync(nonExistentOrgId);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region CreateOrUpdateAsync Tests

    [Fact]
    public async Task CreateOrUpdateAsync_NewConfig_CreatesAndReturnsResponse()
    {
        // Arrange
        var request = CreateTestIdpRequest();

        // Act
        var result = await _service.CreateOrUpdateAsync(_testOrgId, request);

        // Assert
        result.Should().NotBeNull();
        result.IssuerUrl.Should().Be(request.IssuerUrl);
        result.ProviderPreset.Should().Be(request.ProviderPreset);
        result.Scopes.Should().BeEquivalentTo(request.Scopes);

        // Verify persisted
        var persisted = await _dbContext.IdentityProviderConfigurations
            .FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.OrganizationId.Should().Be(_testOrgId);
        persisted.ClientId.Should().Be(request.ClientId);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_ExistingConfig_UpdatesConfig()
    {
        // Arrange
        var existingConfig = CreateTestIdpConfig(_testOrgId);
        _dbContext.IdentityProviderConfigurations.Add(existingConfig);
        await _dbContext.SaveChangesAsync();

        var updateRequest = new IdpConfigurationRequest
        {
            ProviderPreset = nameof(IdentityProviderType.Google),
            IssuerUrl = "https://accounts.google.com",
            ClientId = "updated-client-id",
            ClientSecret = "updated-secret",
            DisplayName = "Updated Google SSO",
            Scopes = ["openid", "profile", "email"]
        };

        // Act
        var result = await _service.CreateOrUpdateAsync(_testOrgId, updateRequest);

        // Assert
        result.Should().NotBeNull();
        result.IssuerUrl.Should().Be("https://accounts.google.com");
        result.ProviderPreset.Should().Be(nameof(IdentityProviderType.Google));
        result.DisplayName.Should().Be("Updated Google SSO");

        // Verify only one config exists for the org
        _dbContext.IdentityProviderConfigurations
            .Count(c => c.OrganizationId == _testOrgId)
            .Should().Be(1);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_TriggersDiscovery_PopulatesEndpoints()
    {
        // Arrange
        var request = CreateTestIdpRequest();
        var discoveryResponse = CreateTestDiscoveryResponse();

        _discoveryServiceMock
            .Setup(x => x.DiscoverAsync(request.IssuerUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveryResponse);

        // Act
        var result = await _service.CreateOrUpdateAsync(_testOrgId, request);

        // Assert
        result.AuthorizationEndpoint.Should().Be(discoveryResponse.AuthorizationEndpoint);
        result.TokenEndpoint.Should().Be(discoveryResponse.TokenEndpoint);
        result.UserInfoEndpoint.Should().Be(discoveryResponse.UserInfoEndpoint);
        result.DiscoveryFetchedAt.Should().NotBeNull();

        _discoveryServiceMock.Verify(
            x => x.DiscoverAsync(request.IssuerUrl, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateOrUpdateAsync_EncryptsClientSecret()
    {
        // Arrange
        var request = CreateTestIdpRequest();

        // Act
        var result = await _service.CreateOrUpdateAsync(_testOrgId, request);

        // Assert
        var persisted = await _dbContext.IdentityProviderConfigurations
            .FindAsync(result.Id);
        persisted.Should().NotBeNull();

        // Client secret should be encrypted (not stored as plaintext)
        persisted!.ClientSecretEncrypted.Should().NotBeEmpty();

        // Encrypted bytes should NOT be the raw UTF-8 of the plaintext secret
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(request.ClientSecret);
        persisted.ClientSecretEncrypted.Should().NotBeEquivalentTo(plaintextBytes,
            "client secret must be encrypted, not stored as plaintext");
    }

    #endregion

    #region DeleteAsync Tests

    [Fact]
    public async Task DeleteAsync_ExistingConfig_RemovesAndReturnsTrue()
    {
        // Arrange
        var config = CreateTestIdpConfig(_testOrgId);
        _dbContext.IdentityProviderConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.DeleteAsync(_testOrgId);

        // Assert
        result.Should().BeTrue();

        var remaining = _dbContext.IdentityProviderConfigurations
            .Count(c => c.OrganizationId == _testOrgId);
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_NoConfig_ReturnsFalse()
    {
        // Arrange
        var nonExistentOrgId = Guid.NewGuid();

        // Act
        var result = await _service.DeleteAsync(nonExistentOrgId);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region DiscoverAsync Tests

    [Fact]
    public async Task DiscoverAsync_CallsOidcDiscoveryService()
    {
        // Arrange
        var issuerUrl = "https://login.microsoftonline.com/tenant-id/v2.0";
        var expectedResponse = CreateTestDiscoveryResponse();

        _discoveryServiceMock
            .Setup(x => x.DiscoverAsync(issuerUrl, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.DiscoverAsync(_testOrgId, issuerUrl);

        // Assert
        result.Should().NotBeNull();
        result.Issuer.Should().Be(expectedResponse.Issuer);
        result.AuthorizationEndpoint.Should().Be(expectedResponse.AuthorizationEndpoint);
        result.TokenEndpoint.Should().Be(expectedResponse.TokenEndpoint);
        result.UserInfoEndpoint.Should().Be(expectedResponse.UserInfoEndpoint);
        result.JwksUri.Should().Be(expectedResponse.JwksUri);
        result.SupportedScopes.Should().BeEquivalentTo(expectedResponse.SupportedScopes);

        _discoveryServiceMock.Verify(
            x => x.DiscoverAsync(issuerUrl, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region TestConnectionAsync Tests

    [Fact]
    public async Task TestConnectionAsync_ValidCredentials_ReturnsSuccess()
    {
        // Arrange
        var config = CreateTestIdpConfig(_testOrgId);
        config.TokenEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token";
        _dbContext.IdentityProviderConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        var tokenResponse = new { access_token = "test-token", token_type = "Bearer", expires_in = 3600 };
        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(tokenResponse))
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _service.TestConnectionAsync(_testOrgId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidCredentials_ReturnsFailure()
    {
        // Arrange
        var config = CreateTestIdpConfig(_testOrgId);
        config.TokenEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token";
        _dbContext.IdentityProviderConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        var errorResponse = new { error = "invalid_client", error_description = "Invalid client credentials" };
        var httpResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(JsonSerializer.Serialize(errorResponse))
        };

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(httpResponse);

        var httpClient = new HttpClient(handlerMock.Object);
        _httpClientFactoryMock
            .Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        // Act
        var result = await _service.TestConnectionAsync(_testOrgId);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("invalid_client");
    }

    [Fact]
    public async Task TestConnectionAsync_NoConfig_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentOrgId = Guid.NewGuid();

        // Act
        var act = () => _service.TestConnectionAsync(nonExistentOrgId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*configuration*");
    }

    #endregion

    #region ToggleAsync Tests

    [Fact]
    public async Task ToggleAsync_EnablesDisablesIdp()
    {
        // Arrange
        var config = CreateTestIdpConfig(_testOrgId);
        config.IsEnabled = false;
        _dbContext.IdentityProviderConfigurations.Add(config);
        await _dbContext.SaveChangesAsync();

        // Act - Enable
        var enabledResult = await _service.ToggleAsync(_testOrgId, enabled: true);

        // Assert - Enabled
        enabledResult.Should().NotBeNull();
        enabledResult.IsEnabled.Should().BeTrue();

        // Act - Disable
        var disabledResult = await _service.ToggleAsync(_testOrgId, enabled: false);

        // Assert - Disabled
        disabledResult.Should().NotBeNull();
        disabledResult.IsEnabled.Should().BeFalse();

        // Verify persisted state
        var persisted = await _dbContext.IdentityProviderConfigurations
            .FindAsync(config.Id);
        persisted!.IsEnabled.Should().BeFalse();
    }

    #endregion

    #region Test Helpers

    private static IdentityProviderConfiguration CreateTestIdpConfig(Guid organizationId)
    {
        return new IdentityProviderConfiguration
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ProviderPreset = IdentityProviderType.MicrosoftEntra,
            IssuerUrl = "https://login.microsoftonline.com/tenant-id/v2.0",
            ClientId = "test-client-id",
            ClientSecretEncrypted = [0x01, 0x02, 0x03, 0x04], // Dummy encrypted bytes
            Scopes = ["openid", "profile", "email"],
            AuthorizationEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize",
            TokenEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token",
            UserInfoEndpoint = "https://graph.microsoft.com/oidc/userinfo",
            JwksUri = "https://login.microsoftonline.com/tenant-id/discovery/v2.0/keys",
            MetadataUrl = "https://login.microsoftonline.com/tenant-id/v2.0/.well-known/openid-configuration",
            IsEnabled = true,
            DisplayName = "Corporate SSO",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static IdpConfigurationRequest CreateTestIdpRequest()
    {
        return new IdpConfigurationRequest
        {
            ProviderPreset = nameof(IdentityProviderType.MicrosoftEntra),
            IssuerUrl = "https://login.microsoftonline.com/tenant-id/v2.0",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret-value",
            DisplayName = "Corporate SSO",
            Scopes = ["openid", "profile", "email"]
        };
    }

    private static DiscoveryResponse CreateTestDiscoveryResponse()
    {
        return new DiscoveryResponse
        {
            Issuer = "https://login.microsoftonline.com/tenant-id/v2.0",
            AuthorizationEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/authorize",
            TokenEndpoint = "https://login.microsoftonline.com/tenant-id/oauth2/v2.0/token",
            UserInfoEndpoint = "https://graph.microsoft.com/oidc/userinfo",
            JwksUri = "https://login.microsoftonline.com/tenant-id/discovery/v2.0/keys",
            SupportedScopes = ["openid", "profile", "email", "offline_access"]
        };
    }

    #endregion
}
