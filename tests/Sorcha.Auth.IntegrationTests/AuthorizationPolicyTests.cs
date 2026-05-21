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
/// Integration tests for authorization policies (US4).
/// Verifies that different actor types (user, service, delegation) have correct access
/// to endpoints protected by various authorization policies.
/// </summary>
public class AuthorizationPolicyTests : IAsyncLifetime
{
    private const string TestSigningKey = "test-signing-key-for-integration-tests-minimum-32-characters-long-enough";
    private const string TestIssuer = "https://test.sorcha.io";
    private const string TestAudience = "sorcha:platform"; // spec 136: human user tokens carry the platform-tier audience

    private WebApplication _app = null!;
    private HttpClient _client = null!;

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
            // Org member policy — requires org_id claim
            options.AddPolicy("RequireOrganizationMember", policy =>
                policy.RequireAssertion(context =>
                    context.User.Claims.Any(c => c.Type == "org_id" && !string.IsNullOrEmpty(c.Value))));

            // Service policy
            options.AddPolicy("RequireService", policy =>
                policy.RequireClaim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService));

            // Delegation policy — service + delegated user
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

            // Wallet management — org member or service
            options.AddPolicy("CanManageWallets", policy =>
                policy.RequireAssertion(context =>
                {
                    var hasOrgId = context.User.Claims.Any(c => c.Type == "org_id" && !string.IsNullOrEmpty(c.Value));
                    var isService = context.User.Claims.Any(c => c.Type == TokenClaimConstants.TokenType && c.Value == TokenClaimConstants.TokenTypeService);
                    return hasOrgId || isService;
                }));

            // Register write — requires register:write scope
            options.AddPolicy("CanWriteRegisters", policy =>
                policy.RequireAssertion(context =>
                {
                    var scopes = context.User.Claims
                        .Where(c => c.Type == TokenClaimConstants.Scope)
                        .SelectMany(c => c.Value.Split(' '));
                    return scopes.Contains("registers:write");
                }));
        });

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        // Wallet owner endpoint — user with org_id can access
        _app.MapGet("/api/v1/wallets/my-wallet", () => Results.Ok("Wallet accessed"))
            .RequireAuthorization("CanManageWallets");

        // Delegation-protected endpoint — requires service + delegated user
        _app.MapPost("/api/v1/wallets/my-wallet/sign", () => Results.Ok("Signed"))
            .RequireAuthorization("RequireDelegatedAuthority");

        // Register write endpoint — requires registers:write scope
        _app.MapPost("/api/v1/registers/tx/submit", () => Results.Ok("Submitted"))
            .RequireAuthorization("CanWriteRegisters");

        // Service-only endpoint
        _app.MapGet("/api/v1/internal/status", () => Results.Ok("Status"))
            .RequireAuthorization("RequireService");

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>
    /// AS-4.1: Wallet owner (user with org_id) accesses their own wallet endpoint.
    /// </summary>
    [Fact]
    public async Task WalletOwner_AccessesOwnWallet()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(TokenClaimConstants.OrgId, "org-456")
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/wallets/my-wallet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-4.2: Delegate (service with delegated user) accesses wallet signing endpoint.
    /// </summary>
    [Fact]
    public async Task Delegate_AccessesDelegatedWalletSigning()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.ServiceName, "service-blueprint"),
            new Claim(TokenClaimConstants.DelegatedUserId, "user-123"),
            new Claim(TokenClaimConstants.DelegatedOrgId, "org-456"),
            new Claim(TokenClaimConstants.Scope, "wallets:sign"),
            new Claim(ClaimTypes.NameIdentifier, "service-blueprint")
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/wallets/my-wallet/sign");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-4.3: User without org_id gets 403 on wallet endpoint.
    /// </summary>
    [Fact]
    public async Task UserWithoutOrg_GetsForbiddenOnWallet()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.NameIdentifier, "user-no-org")
            // No org_id claim
        });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/wallets/my-wallet");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// AS-4.4: Service token without delegation gets 403 on user-scoped operation (signing).
    /// </summary>
    [Fact]
    public async Task ServiceWithoutDelegation_GetsForbiddenOnUserScopedOp()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeService),
            new Claim(TokenClaimConstants.ServiceName, "service-blueprint"),
            new Claim(TokenClaimConstants.Scope, "wallets:sign"),
            new Claim(ClaimTypes.NameIdentifier, "service-blueprint")
            // No delegated_user_id
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/wallets/my-wallet/sign");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// AS-4.5: User with registers:write scope can submit a transaction.
    /// </summary>
    [Fact]
    public async Task UserWithRegisterWriteScope_CanSubmitTransaction()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(TokenClaimConstants.OrgId, "org-456"),
            new Claim(TokenClaimConstants.Scope, "registers:write registers:read")
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/registers/tx/submit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-4.6: User without registers:write scope gets 403 on transaction submit.
    /// </summary>
    [Fact]
    public async Task UserWithoutScope_GetsForbiddenOnTxSubmit()
    {
        var token = GenerateToken(new[]
        {
            new Claim(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new Claim(ClaimTypes.NameIdentifier, "user-123"),
            new Claim(TokenClaimConstants.OrgId, "org-456")
            // No scope claim
        });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/registers/tx/submit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    #region Token Generator

    private static string GenerateToken(Claim[] claims)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
