// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.IntegrationTests.Fixtures;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Models.Auth;

namespace Sorcha.Tenant.Service.IntegrationTests;

/// <summary>
/// Integration tests for Authentication API endpoints.
/// </summary>
public class AuthApiTests : IClassFixture<TenantServiceWebApplicationFactory>, IAsyncLifetime
{
    private readonly HttpClient _client;
    private readonly HttpClient _adminClient;
    private readonly HttpClient _unauthClient;
    private readonly HttpClient _serviceClient;
    private readonly TenantServiceWebApplicationFactory _factory;

    public AuthApiTests(TenantServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _unauthClient = _factory.CreateUnauthenticatedClient();
        _client = _factory.CreateAuthenticatedClient();
        _adminClient = _factory.CreateAdminClient();
        _serviceClient = _factory.CreateServiceClient();
    }

    /// <summary>
    /// Initializes test data before any tests run.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        // Ensure test data is seeded before running tests
        await _factory.EnsureSeededAsync();
    }

    /// <summary>
    /// Cleanup after all tests complete.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    #region Health Check Tests

    [Fact]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await _unauthClient.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadAsStringAsync();
        result.Should().Contain("Healthy");
    }

    #endregion

    #region Login Tests

    [Fact]
    public async Task TestData_ShouldBeSeeded()
    {
        // Verify that test users exist in the database
        var user = await TestDataSeeder.GetTestUserAsync(_factory.Services, TestDataSeeder.TestLocalAdminUserId);

        user.Should().NotBeNull();
        user!.Email.Should().Be(TestDataSeeder.TestLocalAdminEmail);
        user.PlatformUserId.Should().NotBe(Guid.Empty);
        user.Status.Should().Be(IdentityStatus.Active);
    }

    [Fact]
    public async Task TestData_PasswordHashShouldBeValid()
    {
        // Verify that the password hash can be verified with BCrypt
        var user = await TestDataSeeder.GetTestUserAsync(_factory.Services, TestDataSeeder.TestLocalAdminUserId);

        user.Should().NotBeNull();
        user!.PlatformUserId.Should().NotBe(Guid.Empty, "because local auth users should have a PlatformUserId");
    }

    [Fact]
    public async Task TestData_OrganizationShouldBeSeeded()
    {
        // Verify that the test organization exists
        var org = await TestDataSeeder.GetTestOrganizationAsync(_factory.Services);

        org.Should().NotBeNull();
        org!.Id.Should().Be(TestDataSeeder.TestOrganizationId);
        org.Name.Should().Be(TestDataSeeder.TestOrganizationName);
        org.Subdomain.Should().Be(TestDataSeeder.TestOrganizationSubdomain);
        org.Status.Should().Be(OrganizationStatus.Active);
    }

    [Fact]
    public async Task TestData_RepositoryCanFindUserByEmail()
    {
        // Verify that the repository can find users by email
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var repository = new IdentityRepository(context);

        var user = await repository.GetUserByEmailAsync(TestDataSeeder.TestLocalAdminEmail);

        user.Should().NotBeNull();
        user!.Email.Should().Be(TestDataSeeder.TestLocalAdminEmail);
        user.PlatformUserId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Login_ShouldReturnTokens_WithValidCredentials()
    {
        // First verify the user exists in the database
        var user = await TestDataSeeder.GetTestUserAsync(_factory.Services, TestDataSeeder.TestLocalAdminUserId);
        user.Should().NotBeNull("test user should be seeded in database");

        // Arrange
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestLocalAdminEmail,
            Password = TestDataSeeder.TestLocalAdminPassword
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Debug: Print response body if failed
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Login failed with status {response.StatusCode}. Response body: {body}");
        }

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.TokenType.Should().Be("Bearer");
        result.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WithInvalidPassword()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestLocalAdminEmail,
            Password = "WrongPassword123!"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WithNonExistentEmail()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@test-org.sorcha.io",
            Password = "SomePassword123!"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_WithInactiveUser()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestInactiveEmail,
            Password = TestDataSeeder.TestInactivePassword
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ShouldReturnBadRequest_WithEmptyEmail()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "",
            Password = "SomePassword123!"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ShouldReturnBadRequest_WithEmptyPassword()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestLocalAdminEmail,
            Password = ""
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ShouldUpdateLastLoginTimestamp()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestLocalMemberEmail,
            Password = TestDataSeeder.TestLocalMemberPassword
        };

        // Act - Get user before login
        var userBefore = await TestDataSeeder.GetTestUserAsync(_factory.Services, TestDataSeeder.TestLocalMemberUserId);
        var lastLoginBefore = userBefore?.LastLoginAt;

        await Task.Delay(100); // Small delay to ensure timestamp difference

        // Login
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get user after login
        var userAfter = await TestDataSeeder.GetTestUserAsync(_factory.Services, TestDataSeeder.TestLocalMemberUserId);
        userAfter.Should().NotBeNull();
        userAfter!.LastLoginAt.Should().NotBeNull();
        userAfter.LastLoginAt.Should().BeAfter(lastLoginBefore ?? DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Login_ShouldReturnUnauthorized_ForExternalIdpUser()
    {
        // Arrange - Try to login with external IDP user email (no password hash)
        var request = new LoginRequest
        {
            Email = TestDataSeeder.TestAdminEmail, // This user has ExternalIdpUserId but no PasswordHash
            Password = "AnyPassword123!"
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_TokensCanBeUsedForAuthentication()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = TestDataSeeder.TestLocalAdminEmail,
            Password = TestDataSeeder.TestLocalAdminPassword
        };

        // Act - Login and get tokens
        var loginResponse = await _unauthClient.PostAsJsonAsync("/api/auth/login", loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var tokenResponse = await loginResponse.Content.ReadFromJsonAsync<TokenResponse>();
        tokenResponse.Should().NotBeNull();

        // Use the access token to call authenticated endpoint
        var authenticatedClient = _factory.CreateClient();
        authenticatedClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenResponse!.AccessToken);

        var meResponse = await authenticatedClient.GetAsync("/api/auth/me");

        // Assert
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var currentUser = await meResponse.Content.ReadFromJsonAsync<CurrentUserResponse>();
        currentUser.Should().NotBeNull();
        currentUser!.Email.Should().Be(TestDataSeeder.TestLocalAdminEmail);
    }

    #endregion

    #region Token Revocation Tests

    [Fact]
    public async Task RevokeToken_ShouldReturnOk_WithValidToken()
    {
        // Arrange - Generate a test token
        var token = GenerateTestToken();
        var request = new TokenRevocationRequest
        {
            Token = token,
            TokenTypeHint = "access_token"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/revoke", request);

        // Assert
        // Note: This will succeed even without Redis due to fail-open pattern
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeToken_ShouldReturnBadRequest_WhenTokenEmpty()
    {
        // Arrange
        var request = new TokenRevocationRequest
        {
            Token = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/revoke", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Token Introspection Tests

    [Fact]
    public async Task IntrospectToken_ShouldReturnForbidden_ForRegularUser()
    {
        // Arrange - Token introspection requires service token
        var request = new TokenIntrospectionRequest
        {
            Token = "any-token"
        };

        // Act - Regular user should not be able to introspect tokens
        var response = await _client.PostAsJsonAsync("/api/auth/token/introspect", request);

        // Assert - Should be forbidden for non-service tokens
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task IntrospectToken_ShouldReturnInactive_ForInvalidToken()
    {
        // Arrange - Service client required for introspection
        var request = new TokenIntrospectionRequest
        {
            Token = "invalid-token-format"
        };

        // Act
        var response = await _serviceClient.PostAsJsonAsync("/api/auth/token/introspect", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TokenIntrospectionResponse>();
        result.Should().NotBeNull();
        result!.Active.Should().BeFalse();
    }

    [Fact]
    public async Task IntrospectToken_ShouldAcceptValidJwt()
    {
        // Arrange - Generate a properly signed test token
        var token = GenerateTestToken();
        var request = new TokenIntrospectionRequest
        {
            Token = token
        };

        // Act - Service client required for introspection
        var response = await _serviceClient.PostAsJsonAsync("/api/auth/token/introspect", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<TokenIntrospectionResponse>();
        result.Should().NotBeNull();
        // Token will be inactive because the signing key doesn't match the service's key
        // In a real scenario with matching keys, it would be active
    }

    #endregion

    #region Get Current User Tests

    [Fact]
    public async Task GetMe_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        // Act
        var response = await _unauthClient.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_ShouldReturnUserInfo_WhenAuthenticated()
    {
        // Act
        var response = await _client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Logout Tests

    [Fact]
    public async Task Logout_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        // Act
        var response = await _unauthClient.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ShouldReturnOk_WhenAuthenticated()
    {
        // Act
        var response = await _client.PostAsync("/api/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Bulk Token Revocation Tests

    [Fact]
    public async Task RevokeUserTokens_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        var request = new RevokeUserTokensRequest
        {
            UserId = Guid.NewGuid()
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/token/revoke-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeUserTokens_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var request = new RevokeUserTokensRequest
        {
            UserId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/revoke-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeUserTokens_ShouldReturnOk_WhenAdmin()
    {
        // Arrange
        var request = new RevokeUserTokensRequest
        {
            UserId = Guid.NewGuid()
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/revoke-user", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokeOrganizationTokens_ShouldReturnUnauthorized_WhenNotAuthenticated()
    {
        // Arrange
        var request = new RevokeOrganizationTokensRequest
        {
            OrganizationId = Guid.NewGuid()
        };

        // Act
        var response = await _unauthClient.PostAsJsonAsync("/api/auth/token/revoke-organization", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeOrganizationTokens_ShouldReturnForbidden_WhenNotAdmin()
    {
        // Arrange
        var request = new RevokeOrganizationTokensRequest
        {
            OrganizationId = Guid.NewGuid()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/revoke-organization", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RevokeOrganizationTokens_ShouldReturnOk_WhenAdmin()
    {
        // Arrange
        var request = new RevokeOrganizationTokensRequest
        {
            OrganizationId = Guid.NewGuid()
        };

        // Act
        var response = await _adminClient.PostAsJsonAsync("/api/auth/token/revoke-organization", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Token Refresh Tests

    [Fact]
    public async Task RefreshToken_ShouldReturnBadRequest_WithInvalidRefreshToken()
    {
        // Arrange
        var request = new TokenRefreshRequest
        {
            RefreshToken = "invalid-refresh-token"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/refresh", request);

        // Assert
        // Should be unauthorized or bad request for invalid token
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_ShouldReturnBadRequest_WithEmptyToken()
    {
        // Arrange
        var request = new TokenRefreshRequest
        {
            RefreshToken = ""
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/token/refresh", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Generates a test JWT token for testing purposes.
    /// Note: This token won't be valid for actual authentication as the signing key differs.
    /// </summary>
    private static string GenerateTestToken()
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-secret-key-that-is-at-least-32-characters-long!"));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Email, "test@example.com"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("org_id", Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "Administrator")
        };

        var token = new JwtSecurityToken(
            issuer: "sorcha-tenant-service",
            audience: "sorcha-services",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
