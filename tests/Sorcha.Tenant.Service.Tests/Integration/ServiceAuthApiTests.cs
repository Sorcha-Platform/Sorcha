// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Integration tests for Service Authentication API endpoints.
/// </summary>
public class ServiceAuthApiTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _adminClient = null!;
    private HttpClient _memberClient = null!;
    private HttpClient _unauthenticatedClient = null!;

    public ServiceAuthApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _adminClient = _factory.CreateAdminClient();
        _memberClient = _factory.CreateMemberClient();
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _adminClient?.Dispose();
        _memberClient?.Dispose();
        _unauthenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task GetOAuth2Token_WithClientCredentials_ReturnsToken()
    {
        // Arrange
        var request = new OAuth2TokenRequest
        {
            GrantType = "client_credentials",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            Scope = "blueprints:read"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();
        tokenResponse!.AccessToken.Should().NotBeNullOrEmpty();
        tokenResponse.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task GetOAuth2Token_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new OAuth2TokenRequest
        {
            GrantType = "client_credentials",
            ClientId = "invalid-client",
            ClientSecret = "invalid-secret"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOAuth2Token_WithPasswordGrant_ReturnsToken()
    {
        // Arrange
        var request = new OAuth2TokenRequest
        {
            GrantType = "password",
            Username = "admin@test-org.sorcha.io",
            Password = "TestPassword123!",
            Scope = "openid profile"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();
        tokenResponse!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetOAuth2Token_WithInvalidGrantType_ReturnsBadRequest()
    {
        // Arrange
        var request = new OAuth2TokenRequest
        {
            GrantType = "invalid_grant_type",
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetDelegatedToken_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var request = new DelegatedTokenRequest
        {
            ClientId = "test-client-id",
            ClientSecret = "test-client-secret",
            DelegatedUserId = TestDataSeeder.AdminUserId
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token/delegated", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();
        tokenResponse!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetDelegatedToken_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new DelegatedTokenRequest
        {
            ClientId = "invalid-client",
            ClientSecret = "invalid-secret",
            DelegatedUserId = TestDataSeeder.AdminUserId
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-auth/token/delegated", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterServicePrincipal_AsAdmin_ReturnsCreated()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = "new-service",
            Scopes = new[] { "blueprints:read", "wallets:write" }
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();
        result.Should().NotBeNull();
        result!.ClientId.Should().NotBeNullOrEmpty();
        result.ClientSecret.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegisterServicePrincipal_Unauthorized_ReturnsUnauthorized()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = "new-service",
            Scopes = new[] { "blueprints:read" }
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterServicePrincipal_AsMember_ReturnsForbidden()
    {
        // Arrange
        var request = new RegisterServicePrincipalRequest
        {
            ServiceName = "new-service",
            Scopes = new[] { "blueprints:read" }
        };

        // Act
        var response = await _memberClient.PostAsJsonAsync("/api/service-principals", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListServicePrincipals_AsAdmin_ReturnsServicePrincipals()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalListResponse>();
        result.Should().NotBeNull();
        result!.ServicePrincipals.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ListServicePrincipals_Unauthorized_ReturnsUnauthorized()
    {
        // Act
        var response = await _unauthenticatedClient.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListServicePrincipals_AsMember_ReturnsForbidden()
    {
        // Act
        var response = await _memberClient.GetAsync("/api/service-principals");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetServicePrincipalByClientId_WithValidId_ReturnsServicePrincipal()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/service-principals/by-client/test-client-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ServicePrincipalResponse>();
        result.Should().NotBeNull();
        result!.ClientId.Should().Be("test-client-id");
    }

    [Fact]
    public async Task GetServicePrincipalByClientId_WithInvalidId_ReturnsNotFound()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/service-principals/by-client/nonexistent");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateServicePrincipalScopes_AsAdmin_ReturnsUpdated()
    {
        // Arrange - First get the service principal ID
        var listResponse = await _adminClient.GetAsync("/api/service-principals");
        var list = await listResponse.Content.ReadFromJsonAsync<ServicePrincipalListResponse>();
        var servicePrincipalId = list!.ServicePrincipals.First().Id;

        var scopes = new[] { "blueprints:write", "registers:read" };

        // Act
        var response = await _adminClient.PutAsJsonAsync(
            $"/api/service-principals/{servicePrincipalId}/scopes",
            scopes);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuspendServicePrincipal_AsAdmin_ReturnsSuccess()
    {
        // Arrange - First get the service principal ID
        var listResponse = await _adminClient.GetAsync("/api/service-principals");
        var list = await listResponse.Content.ReadFromJsonAsync<ServicePrincipalListResponse>();
        var servicePrincipalId = list!.ServicePrincipals.First().Id;

        // Act
        var response = await _adminClient.PostAsync(
            $"/api/service-principals/{servicePrincipalId}/suspend",
            null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ReactivateServicePrincipal_AsAdmin_ReturnsSuccess()
    {
        // Arrange - First suspend it
        var listResponse = await _adminClient.GetAsync("/api/service-principals");
        var list = await listResponse.Content.ReadFromJsonAsync<ServicePrincipalListResponse>();
        var servicePrincipalId = list!.ServicePrincipals.First().Id;

        await _adminClient.PostAsync($"/api/service-principals/{servicePrincipalId}/suspend", null);

        // Act
        var response = await _adminClient.PostAsync(
            $"/api/service-principals/{servicePrincipalId}/reactivate",
            null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeServicePrincipal_AsAdmin_ReturnsSuccess()
    {
        // Arrange - Create a new service principal to revoke
        var createResponse = await _adminClient.PostAsJsonAsync("/api/service-principals",
            new RegisterServicePrincipalRequest
            {
                ServiceName = "to-revoke",
                Scopes = new[] { "test" }
            });
        var created = await createResponse.Content.ReadFromJsonAsync<ServicePrincipalRegistrationResponse>();

        // Act — endpoint is DELETE /api/service-principals/{id} returning 204
        var response = await _adminClient.DeleteAsync(
            $"/api/service-principals/{created!.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
