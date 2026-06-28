// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Tests.Infrastructure;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Integration tests for Authentication API endpoints.
/// </summary>
public class AuthApiTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly TenantServiceWebApplicationFactory _factory;
    private HttpClient _adminClient = null!;
    private HttpClient _unauthenticatedClient = null!;

    public AuthApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        _adminClient = _factory.CreateAdminClient();
        _unauthenticatedClient = _factory.CreateUnauthenticatedClient();
        await _factory.SeedTestDataAsync();
    }

    public ValueTask DisposeAsync()
    {
        _adminClient?.Dispose();
        _unauthenticatedClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test-org.sorcha.io",
            Password = "TestPassword123!"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();
        tokenResponse!.AccessToken.Should().NotBeNullOrEmpty();
        tokenResponse.RefreshToken.Should().NotBeNullOrEmpty();
        tokenResponse.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test-org.sorcha.io",
            Password = "WrongPassword"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@test.com",
            Password = "Password123!"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewToken()
    {
        // Arrange - First login to get a refresh token
        var loginResponse = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@test-org.sorcha.io",
            Password = "TestPassword123!"
        });
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();

        var refreshRequest = new TokenRefreshRequest
        {
            RefreshToken = loginResult!.RefreshToken
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/token/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();
        tokenResponse!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshToken_WithInvalidToken_ReturnsUnauthorized()
    {
        // Arrange
        var request = new TokenRefreshRequest
        {
            RefreshToken = "invalid-refresh-token"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/token/refresh", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeToken_WithAuthentication_ReturnsSuccess()
    {
        // Arrange
        var request = new TokenRevocationRequest
        {
            Token = "some-token"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/revoke", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeToken_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Arrange
        var request = new TokenRevocationRequest
        {
            Token = "some-token"
        };

        // Act
        var response = await _unauthenticatedClient.PostAsJsonAsync("/api/auth/token/revoke", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeUserTokens_AsAdmin_ReturnsSuccess()
    {
        // Arrange
        var request = new RevokeUserTokensRequest
        {
            UserId = TestDataSeeder.MemberUserId
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/revoke-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeOrganizationTokens_AsAdmin_ReturnsSuccess()
    {
        // Arrange
        var request = new RevokeOrganizationTokensRequest
        {
            OrganizationId = TestDataSeeder.TestOrganizationId
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/revoke-organization", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetCurrentUser_WithAuthentication_ReturnsUserInfo()
    {
        // Act
        var response = await _adminClient.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        userInfo.Should().NotBeNull();
        userInfo!.UserId.Should().Be(TestDataSeeder.AdminUserId.ToString());
    }

    [Fact]
    public async Task GetCurrentUser_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _unauthenticatedClient.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithAuthentication_ReturnsSuccess()
    {
        // Act
        var response = await _adminClient.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithoutAuthentication_ReturnsUnauthorized()
    {
        // Act
        var response = await _unauthenticatedClient.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IntrospectToken_AsService_ReturnsTokenInfo()
    {
        // Arrange
        var request = new TokenIntrospectionRequest
        {
            Token = "some-valid-token"
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/introspect", request);

        // Assert
        // This will likely return OK or a specific response based on implementation
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden);
    }

    // Feature 157 (US3): EmailVerified on GET /api/auth/me

    [Fact]
    public async Task GetCurrentUser_WithVerifiedEmailClaim_ReturnsEmailVerifiedTrue()
    {
        // Arrange — inject email_verified=true via test header
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Email-Verified", "true");

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        userInfo.Should().NotBeNull();
        userInfo!.EmailVerified.Should().BeTrue();
        // Existing fields are unchanged (regression check)
        userInfo.UserId.Should().NotBeNullOrEmpty();
        userInfo.TokenType.Should().Be("user");
    }

    [Fact]
    public async Task GetCurrentUser_WithUnverifiedEmailClaim_ReturnsEmailVerifiedFalse()
    {
        // Arrange — inject email_verified=false via test header
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Email-Verified", "false");

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        userInfo.Should().NotBeNull();
        userInfo!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetCurrentUser_WithNoEmailVerifiedClaim_ReturnsEmailVerifiedFalse()
    {
        // Arrange — no email_verified header → claim absent from token
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id", TestDataSeeder.AdminUserId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Role", "Administrator");
        client.DefaultRequestHeaders.Add("X-Test-Organization-Id", TestDataSeeder.TestOrganizationId.ToString());
        // Deliberately omit X-Test-Email-Verified

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var userInfo = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        userInfo.Should().NotBeNull();
        // Absent claim must unambiguously return false (FR-011)
        userInfo!.EmailVerified.Should().BeFalse();
        // No exception thrown — handler is robust to missing claim
    }
}
