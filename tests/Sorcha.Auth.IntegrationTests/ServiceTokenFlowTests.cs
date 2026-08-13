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
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Auth.IntegrationTests;

/// <summary>
/// Integration tests for the service-to-service authentication flow (US1).
/// Verifies token acquisition via ServiceAuthClient, token validation by JWT middleware,
/// and correct claim propagation for service identity.
/// </summary>
public class ServiceTokenFlowTests : IAsyncLifetime
{
    private const string TestSigningKey = "test-signing-key-for-integration-tests-minimum-32-characters-long-enough";
    private const string TestIssuer = "https://test.sorcha.io";
    // Spec 136: validation derives tier audiences from the installation (default "sorcha"),
    // ignoring any configured raw audience. Service tokens carry the service-tier audience.
    private const string TestAudience = "sorcha:service";

    private WebApplication _targetApp = null!;
    private HttpClient _targetClient = null!;

    public async ValueTask InitializeAsync()
    {
        // Create a minimal target service with real JWT auth middleware
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
        builder.Services.AddAuthorization();

        _targetApp = builder.Build();
        _targetApp.UseAuthentication();
        _targetApp.UseAuthorization();
        _targetApp.MapGet("/api/test/protected", () => Results.Ok("OK")).RequireAuthorization();
        _targetApp.MapGet("/api/test/anonymous", () => Results.Ok("OK"));

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
    /// AS-1.1: Service acquires a token using valid client credentials.
    /// </summary>
    [Fact]
    public async Task ServiceAcquiresToken_WithValidCredentials_ReturnsToken()
    {
        // Arrange: mock Tenant Service that returns a valid JWT
        var mockHandler = new MockTenantServiceHandler(TestSigningKey, TestIssuer, TestAudience);
        using var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://tenant-service") };
        var config = BuildServiceAuthConfig("service-blueprint", "blueprint-service-secret", "wallets:sign registers:write");
        var logger = new LoggerFactory().CreateLogger<ServiceAuthClient>();
        var client = new ServiceAuthClient(httpClient, config, logger);

        // Act
        var token = await client.GetTokenAsync();

        // Assert
        token.Should().NotBeNullOrEmpty("service should receive a valid token");
        mockHandler.RequestCount.Should().Be(1);
    }

    /// <summary>
    /// AS-1.2: Service calls another service endpoint with its acquired token.
    /// </summary>
    [Fact]
    public async Task ServiceCallsAnotherService_WithValidToken_Succeeds()
    {
        // Arrange: generate a valid service token
        var token = GenerateServiceToken("service-blueprint", "wallets:sign registers:write");

        // Act: call the protected endpoint with the token
        var response = await SendProtectedRequest(token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-1.3: Expired service token is rejected with 401.
    /// </summary>
    [Fact]
    public async Task ExpiredToken_ReturnsUnauthorized()
    {
        // Arrange: generate a token that expired 60 seconds ago
        var token = GenerateServiceToken("service-blueprint", "wallets:sign",
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddSeconds(-60));

        // Act
        var response = await SendProtectedRequest(token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AS-1.4: Invalid client credentials are rejected.
    /// </summary>
    [Fact]
    public async Task InvalidCredentials_ReturnsNull()
    {
        // Arrange: mock Tenant Service that rejects all credentials
        var mockHandler = new MockTenantServiceHandler(TestSigningKey, TestIssuer, TestAudience)
        {
            RejectAllCredentials = true
        };
        using var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://tenant-service") };
        var config = BuildServiceAuthConfig("invalid-service", "wrong-secret", "wallets:sign");
        var logger = new LoggerFactory().CreateLogger<ServiceAuthClient>();
        var client = new ServiceAuthClient(httpClient, config, logger);

        // Act
        var token = await client.GetTokenAsync();

        // Assert
        token.Should().BeNull("invalid credentials should not yield a token");
    }

    /// <summary>
    /// AS-1.5: Acquired service token contains correct service identity and scopes.
    /// </summary>
    [Fact]
    public async Task ServiceToken_ContainsServiceIdentityAndScopes()
    {
        // Arrange: mock Tenant Service returning a properly-signed JWT
        var mockHandler = new MockTenantServiceHandler(TestSigningKey, TestIssuer, TestAudience);
        using var httpClient = new HttpClient(mockHandler) { BaseAddress = new Uri("http://tenant-service") };
        var config = BuildServiceAuthConfig("service-blueprint", "blueprint-service-secret", "wallets:sign registers:write");
        var logger = new LoggerFactory().CreateLogger<ServiceAuthClient>();
        var client = new ServiceAuthClient(httpClient, config, logger);

        // Act
        var token = await client.GetTokenAsync();

        // Assert: decode and verify claims
        token.Should().NotBeNull();
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.TokenType &&
            c.Value == TokenClaimConstants.TokenTypeService);
        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.ServiceName &&
            c.Value == "service-blueprint");
        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.Scope &&
            c.Value == "wallets:sign registers:write");
    }

    /// <summary>
    /// Tokens signed with a different key are rejected with 401.
    /// </summary>
    [Fact]
    public async Task TokenWithWrongSigningKey_ReturnsUnauthorized()
    {
        // Arrange: generate a token signed with a different key
        var token = GenerateServiceToken("service-blueprint", "wallets:sign",
            signingKey: "completely-different-signing-key-that-is-at-least-32-characters-long");

        // Act
        var response = await SendProtectedRequest(token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Anonymous endpoints remain accessible without a token.
    /// </summary>
    [Fact]
    public async Task AnonymousEndpoint_WithoutToken_ReturnsOk()
    {
        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/anonymous");
        var response = await _targetClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Protected endpoints return 401 when no token is provided.
    /// </summary>
    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        // Act
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/protected");
        var response = await _targetClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #region Helpers

    private async Task<HttpResponseMessage> SendProtectedRequest(string bearerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/test/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await _targetClient.SendAsync(request);
    }

    private static string GenerateServiceToken(
        string clientId,
        string scopes,
        DateTime? notBefore = null,
        DateTime? expires = null,
        string? signingKey = null)
    {
        var keyBytes = Encoding.UTF8.GetBytes(signingKey ?? TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.ServiceName, clientId),
            new Claim(TokenClaimConstants.Scope, scopes),
            new Claim(ClaimTypes.NameIdentifier, clientId)
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static IConfiguration BuildServiceAuthConfig(
        string clientId,
        string clientSecret,
        string scopes)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceAuth:ClientId"] = clientId,
                ["ServiceAuth:ClientSecret"] = clientSecret,
                ["ServiceAuth:Scopes"] = scopes,
                ["ServiceClients:TenantService:Address"] = "http://tenant-service"
            })
            .Build();
    }

    #endregion

    #region Mock Tenant Service

    /// <summary>
    /// Mock HTTP handler that simulates the Tenant Service /api/internal/service-auth/token
    /// endpoint (the internal-only client_credentials route — #1397 moved client_credentials
    /// off the public /api/service-auth/token path; ServiceAuthClient now posts here).
    /// Returns properly-signed JWTs for valid credentials, or 401 for invalid.
    /// </summary>
    private sealed class MockTenantServiceHandler : HttpMessageHandler
    {
        private readonly string _signingKey;
        private readonly string _issuer;
        private readonly string _audience;

        public bool RejectAllCredentials { get; set; }
        public int RequestCount { get; private set; }

        public MockTenantServiceHandler(string signingKey, string issuer, string audience)
        {
            _signingKey = signingKey;
            _issuer = issuer;
            _audience = audience;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            if (request.RequestUri?.PathAndQuery != "/api/internal/service-auth/token")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (RejectAllCredentials)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { error = "invalid_client", error_description = "Invalid client credentials" }),
                        Encoding.UTF8,
                        "application/json")
                };
            }

            // Parse form data to extract client_id and scope
            var content = await request.Content!.ReadAsStringAsync(cancellationToken);
            var formValues = ParseFormUrlEncoded(content);

            var clientId = formValues.GetValueOrDefault("client_id") ?? "unknown";
            var scope = formValues.GetValueOrDefault("scope") ?? "wallets:sign";

            // Generate a real JWT
            var accessToken = GenerateToken(clientId, scope);

            var response = new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 28800,
                scope
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    Encoding.UTF8,
                    "application/json")
            };
        }

        private string GenerateToken(string clientId, string scopes)
        {
            var keyBytes = Encoding.UTF8.GetBytes(_signingKey);
            if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
            var key = new SymmetricSecurityKey(keyBytes);
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
                new Claim(TokenClaimConstants.ServiceName, clientId),
                new Claim(TokenClaimConstants.Scope, scopes),
                new Claim(ClaimTypes.NameIdentifier, clientId)
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: DateTime.UtcNow.AddMinutes(-1),
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static Dictionary<string, string> ParseFormUrlEncoded(string content)
        {
            return content.Split('&')
                .Select(pair => pair.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .ToDictionary(
                    parts => Uri.UnescapeDataString(parts[0]),
                    parts => Uri.UnescapeDataString(parts[1].Replace('+', ' ')));
        }
    }

    #endregion
}
