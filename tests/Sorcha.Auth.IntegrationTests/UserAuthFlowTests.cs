// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Auth.IntegrationTests;

/// <summary>
/// Integration tests for the user authentication flow (US2).
/// Verifies that user JWT tokens are accepted/rejected by the JWT middleware,
/// that user claims propagate correctly, and that anonymous endpoints remain accessible.
/// </summary>
public class UserAuthFlowTests : IAsyncLifetime
{
    private const string TestSigningKey = "test-signing-key-for-integration-tests-minimum-32-characters-long-enough";
    private const string TestIssuer = "https://test.sorcha.io";
    private const string TestAudience = "https://test-api.sorcha.io";

    private WebApplication _targetApp = null!;
    private HttpClient _targetClient = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JwtSettings:SigningKey"] = TestSigningKey,
            ["JwtSettings:Issuer"] = TestIssuer,
            ["JwtSettings:Audience:0"] = TestAudience,
            ["JwtSettings:ValidateIssuer"] = "true",
            ["JwtSettings:ValidateAudience"] = "true",
            ["JwtSettings:ValidateIssuerSigningKey"] = "true",
            ["JwtSettings:ValidateLifetime"] = "true",
            ["JwtSettings:ClockSkewMinutes"] = "0"
        });
        builder.WebHost.UseTestServer();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.AddJwtAuthentication();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireAuthenticated", policy =>
                policy.RequireAuthenticatedUser());
            options.AddPolicy("RequireOrganizationMember", policy =>
                policy.RequireAssertion(context =>
                    context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.OrgId &&
                        !string.IsNullOrEmpty(c.Value))));
        });

        _targetApp = builder.Build();
        _targetApp.UseAuthentication();
        _targetApp.UseAuthorization();
        _targetApp.MapGet("/api/test/protected", () => Results.Ok("OK"))
            .RequireAuthorization("RequireAuthenticated");
        _targetApp.MapGet("/api/test/org-protected", () => Results.Ok("OK"))
            .RequireAuthorization("RequireOrganizationMember");
        _targetApp.MapGet("/api/test/anonymous", () => Results.Ok("OK"));
        _targetApp.MapGet("/health", () => Results.Ok("Healthy"));
        _targetApp.MapGet("/alive", () => Results.Ok("Alive"));

        await _targetApp.StartAsync();
        _targetClient = _targetApp.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _targetClient.Dispose();
        await _targetApp.StopAsync();
        await _targetApp.DisposeAsync();
    }

    /// <summary>
    /// AS-2.1: Valid user credentials produce a properly-structured token with user claims.
    /// (Simulated — we generate the token as the Tenant Service would)
    /// </summary>
    [Fact]
    public void ValidUserToken_ContainsExpectedClaims()
    {
        // Arrange & Act
        var token = GenerateUserToken(
            userId: "user-123",
            email: "test@sorcha.io",
            orgId: "org-456",
            role: "Consumer");

        // Assert: decode and verify claims
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.TokenType &&
            c.Value == TokenClaimConstants.TokenTypeUser);
        jwtToken.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.NameIdentifier &&
            c.Value == "user-123");
        jwtToken.Claims.Should().Contain(c =>
            c.Type == ClaimTypes.Email &&
            c.Value == "test@sorcha.io");
        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.OrgId &&
            c.Value == "org-456");
    }

    /// <summary>
    /// AS-2.2: Valid user token accesses a protected endpoint.
    /// </summary>
    [Fact]
    public async Task ValidUserToken_AccessesProtectedEndpoint()
    {
        var token = GenerateUserToken("user-123", "test@sorcha.io", "org-456", "Consumer");
        var response = await SendRequest("/api/test/protected", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-2.2: User with org_id claim accesses org-protected endpoint.
    /// </summary>
    [Fact]
    public async Task UserWithOrgId_AccessesOrgProtectedEndpoint()
    {
        var token = GenerateUserToken("user-123", "test@sorcha.io", "org-456", "Consumer");
        var response = await SendRequest("/api/test/org-protected", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-2.2: User without org_id claim is rejected from org-protected endpoint.
    /// </summary>
    [Fact]
    public async Task UserWithoutOrgId_RejectedFromOrgProtectedEndpoint()
    {
        var token = GenerateUserToken("user-123", "test@sorcha.io", orgId: null, role: "Consumer");
        var response = await SendRequest("/api/test/org-protected", token);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// AS-2.3: Expired user token returns 401.
    /// </summary>
    [Fact]
    public async Task ExpiredUserToken_ReturnsUnauthorized()
    {
        var token = GenerateUserToken("user-123", "test@sorcha.io", "org-456", "Consumer",
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddSeconds(-60));

        var response = await SendRequest("/api/test/protected", token);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AS-2.4: Refresh token flow — mock Tenant Service returns new access+refresh tokens.
    /// </summary>
    [Fact]
    public async Task RefreshToken_ReturnsNewTokens()
    {
        // Arrange: mock Tenant Service with refresh endpoint
        var mockHandler = new MockTenantRefreshHandler(TestSigningKey, TestIssuer, TestAudience);
        using var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://tenant-service") };

        // Act: POST to refresh endpoint with a refresh token
        var refreshRequest = new StringContent(
            JsonSerializer.Serialize(new { refresh_token = "valid-refresh-token" }),
            Encoding.UTF8,
            "application/json");
        var response = await httpClient.PostAsync("/api/auth/token/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(content);

        tokenResponse.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        tokenResponse.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
        tokenResponse.GetProperty("token_type").GetString().Should().Be("Bearer");
    }

    /// <summary>
    /// AS-2.5: Revoked refresh token is rejected.
    /// </summary>
    [Fact]
    public async Task RevokedRefreshToken_IsRejected()
    {
        // Arrange: mock Tenant Service that rejects revoked refresh tokens
        var mockHandler = new MockTenantRefreshHandler(TestSigningKey, TestIssuer, TestAudience)
        {
            RejectRefreshToken = true
        };
        using var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://tenant-service") };

        // Act
        var refreshRequest = new StringContent(
            JsonSerializer.Serialize(new { refresh_token = "revoked-refresh-token" }),
            Encoding.UTF8,
            "application/json");
        var response = await httpClient.PostAsync("/api/auth/token/refresh", refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Protected endpoint returns 401 when no token is provided.
    /// </summary>
    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/protected");
        var response = await _targetClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Health and alive endpoints remain accessible without authentication.
    /// </summary>
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    [InlineData("/api/test/anonymous")]
    public async Task AnonymousEndpoints_AccessibleWithoutToken(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        var response = await _targetClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// User token with wrong signing key is rejected.
    /// </summary>
    [Fact]
    public async Task UserTokenWithWrongKey_ReturnsUnauthorized()
    {
        var token = GenerateUserToken("user-123", "test@sorcha.io", "org-456", "Consumer",
            signingKey: "completely-different-signing-key-that-is-at-least-32-characters-long");

        var response = await SendRequest("/api/test/protected", token);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #region Helpers

    private async Task<HttpResponseMessage> SendRequest(string path, string bearerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await _targetClient.SendAsync(request);
    }

    private static string GenerateUserToken(
        string userId,
        string email,
        string? orgId,
        string role,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string? signingKey = null)
    {
        var keyBytes = Encoding.UTF8.GetBytes(signingKey ?? TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, "Test User"),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Role, role)
        };

        if (orgId is not null)
        {
            claims.Add(new Claim(TokenClaimConstants.OrgId, orgId));
        }

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion

    #region Mock Tenant Refresh Handler

    /// <summary>
    /// Mock HTTP handler that simulates the Tenant Service /api/auth/token/refresh endpoint.
    /// </summary>
    private sealed class MockTenantRefreshHandler : HttpMessageHandler
    {
        private readonly string _signingKey;
        private readonly string _issuer;
        private readonly string _audience;

        public bool RejectRefreshToken { get; set; }

        public MockTenantRefreshHandler(string signingKey, string issuer, string audience)
        {
            _signingKey = signingKey;
            _issuer = issuer;
            _audience = audience;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.PathAndQuery != "/api/auth/token/refresh")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (RejectRefreshToken)
            {
                var errorResponse = new
                {
                    error = "invalid_grant",
                    error_description = "Refresh token has been revoked"
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(errorResponse),
                        Encoding.UTF8,
                        "application/json")
                });
            }

            // Generate new tokens
            var keyBytes = Encoding.UTF8.GetBytes(_signingKey);
            if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
            var key = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
                new Claim(ClaimTypes.NameIdentifier, "user-123"),
                new Claim(ClaimTypes.Email, "test@sorcha.io"),
                new Claim(TokenClaimConstants.OrgId, "org-456"),
                new Claim(ClaimTypes.Role, "Consumer")
            };

            var accessToken = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds);

            var response = new
            {
                access_token = new JwtSecurityTokenHandler().WriteToken(accessToken),
                refresh_token = Guid.NewGuid().ToString(),
                token_type = "Bearer",
                expires_in = 3600
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    #endregion
}
