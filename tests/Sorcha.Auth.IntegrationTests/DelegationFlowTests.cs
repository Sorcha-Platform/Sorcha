// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
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
/// Integration tests for the delegation token flow (US3).
/// Verifies end-to-end: user action → service acquires delegation → target service
/// validates delegation token with correct user identity and scopes.
/// </summary>
public class DelegationFlowTests : IAsyncLifetime
{
    private const string TestSigningKey = "test-signing-key-for-integration-tests-minimum-32-characters-long-enough";
    private const string TestIssuer = "https://test.sorcha.io";
    private const string TestAudience = "sorcha:service"; // spec 136: service-tier audience (delegation rides a service token)

    private WebApplication _targetApp = null!;
    private HttpClient _targetClient = null!;

    public async ValueTask InitializeAsync()
    {
        // Create a minimal target service (simulating Wallet Service) with JWT auth
        // and a RequireDelegatedAuthority policy
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
            options.AddPolicy("RequireDelegatedAuthority", policy =>
                policy.RequireAssertion(context =>
                {
                    var isService = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.TokenType &&
                        c.Value == TokenClaimConstants.TokenTypeService);
                    var hasDelegatedUser = context.User.Claims.Any(c =>
                        c.Type == TokenClaimConstants.DelegatedUserId &&
                        !string.IsNullOrEmpty(c.Value));
                    return isService && hasDelegatedUser;
                }));

            options.AddPolicy("RequireService", policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));
        });

        _targetApp = builder.Build();
        _targetApp.UseAuthentication();
        _targetApp.UseAuthorization();

        // Delegation-protected endpoint (requires both service identity and delegated user)
        _targetApp.MapPost("/api/v1/wallets/{address}/sign", () => Results.Ok("Signed"))
            .RequireAuthorization("RequireDelegatedAuthority");

        // Service-only endpoint (requires service token, no delegation)
        _targetApp.MapGet("/api/v1/wallets/list", () => Results.Ok("Listed"))
            .RequireAuthorization("RequireService");

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
    /// Delegation token with both service and user identity accesses delegation-protected endpoint.
    /// </summary>
    [Fact]
    public async Task DelegationToken_AccessesDelegationProtectedEndpoint()
    {
        var token = GenerateDelegationToken("service-blueprint", "user-123", "org-456", "wallets:sign");
        var response = await SendSignRequest(token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Service token WITHOUT delegation claims is rejected from delegation-protected endpoint.
    /// </summary>
    [Fact]
    public async Task ServiceTokenWithoutDelegation_RejectedFromDelegationEndpoint()
    {
        var token = GenerateServiceToken("service-blueprint", "wallets:sign");
        var response = await SendSignRequest(token);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// User token is rejected from delegation-protected endpoint (requires service token_type).
    /// </summary>
    [Fact]
    public async Task UserToken_RejectedFromDelegationEndpoint()
    {
        var token = GenerateUserToken("user-123", "org-456");
        var response = await SendSignRequest(token);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Delegation token with service identity can also access service-only endpoints.
    /// </summary>
    [Fact]
    public async Task DelegationToken_AccessesServiceOnlyEndpoint()
    {
        var token = GenerateDelegationToken("service-blueprint", "user-123", "org-456", "wallets:sign");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/wallets/list");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _targetClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Delegation token contains correct delegated user ID in claims.
    /// </summary>
    [Fact]
    public void DelegationToken_ContainsCorrectDelegatedUserId()
    {
        var token = GenerateDelegationToken("service-blueprint", "user-abc", "org-xyz", "wallets:sign");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(token);

        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.DelegatedUserId &&
            c.Value == "user-abc");
        jwtToken.Claims.Should().Contain(c =>
            c.Type == TokenClaimConstants.DelegatedOrgId &&
            c.Value == "org-xyz");
    }

    /// <summary>
    /// Expired delegation token is rejected with 401.
    /// </summary>
    [Fact]
    public async Task ExpiredDelegationToken_ReturnsUnauthorized()
    {
        var token = GenerateDelegationToken("service-blueprint", "user-123", "org-456", "wallets:sign",
            notBefore: DateTime.UtcNow.AddMinutes(-10),
            expires: DateTime.UtcNow.AddSeconds(-60));

        var response = await SendSignRequest(token);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// No token at all returns 401.
    /// </summary>
    [Fact]
    public async Task NoToken_ReturnsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/wallets/ws1test/sign");
        var response = await _targetClient.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #region Token Generators

    private async Task<HttpResponseMessage> SendSignRequest(string bearerToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/wallets/ws1test/sign");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return await _targetClient.SendAsync(request);
    }

    private static string GenerateDelegationToken(
        string serviceId, string userId, string orgId, string scopes,
        DateTime? notBefore = null, DateTime? expires = null)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.ServiceName, serviceId),
            new Claim(TokenClaimConstants.DelegatedUserId, userId),
            new Claim(TokenClaimConstants.DelegatedOrgId, orgId),
            new Claim(TokenClaimConstants.Scope, scopes),
            new Claim(ClaimTypes.NameIdentifier, serviceId)
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateServiceToken(string serviceId, string scopes)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.ServiceName, serviceId),
            new Claim(TokenClaimConstants.Scope, scopes),
            new Claim(ClaimTypes.NameIdentifier, serviceId)
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateUserToken(string userId, string orgId)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(TokenClaimConstants.OrgId, orgId)
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(60),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
