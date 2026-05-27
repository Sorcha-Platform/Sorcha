// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
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
/// Integration tests for token introspection and revocation (US5).
/// Verifies that the ITokenRevocationStore integration in JwtAuthenticationExtensions
/// correctly rejects revoked tokens and allows valid ones.
/// </summary>
public class TokenRevocationTests : IAsyncLifetime
{
    private const string TestSigningKey = "test-signing-key-for-integration-tests-minimum-32-characters-long-enough";
    private const string TestIssuer = "https://test.sorcha.io";
    private const string TestAudience = "sorcha:platform"; // spec 136: human user tokens carry the platform-tier audience

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private InMemoryRevocationStore _revocationStore = null!;

    public async ValueTask InitializeAsync()
    {
        _revocationStore = new InMemoryRevocationStore();

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

        // Register revocation store BEFORE AddJwtAuthentication so OnTokenValidated can find it
        builder.Services.AddSingleton<ITokenRevocationStore>(_revocationStore);
        builder.AddJwtAuthentication();
        builder.Services.AddAuthorization();

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapGet("/api/v1/protected", () => Results.Ok("Protected"))
            .RequireAuthorization();

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
    /// AS-5.1: Valid token introspection returns claims — token with jti is accepted when not revoked.
    /// </summary>
    [Fact]
    public async Task ValidTokenWithJti_IsAccepted()
    {
        var jti = Guid.NewGuid().ToString();
        var token = GenerateToken(jti: jti);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// AS-5.2: Revoked token returns 401 — token's jti is in the revocation store.
    /// </summary>
    [Fact]
    public async Task RevokedToken_ReturnsUnauthorized()
    {
        var jti = Guid.NewGuid().ToString();
        var token = GenerateToken(jti: jti);

        // First request succeeds
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response1 = await _client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoke the token
        _revocationStore.Revoke(jti);

        // Second request fails with 401
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response2 = await _client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AS-5.3: Malformed token returns 401 (standard JWT validation).
    /// </summary>
    [Fact]
    public async Task MalformedToken_ReturnsUnauthorized()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// AS-5.4: Revoked token is rejected immediately (within same request cycle).
    /// </summary>
    [Fact]
    public async Task RevokedToken_RejectedImmediately()
    {
        var jti = Guid.NewGuid().ToString();
        var token = GenerateToken(jti: jti);

        // Revoke before first use
        _revocationStore.Revoke(jti);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Token without jti claim is still accepted (revocation check is skipped).
    /// </summary>
    [Fact]
    public async Task TokenWithoutJti_IsAccepted()
    {
        var token = GenerateToken(jti: null);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Revoking one token's jti does not affect other tokens.
    /// </summary>
    [Fact]
    public async Task RevokingOneToken_DoesNotAffectOthers()
    {
        var jti1 = Guid.NewGuid().ToString();
        var jti2 = Guid.NewGuid().ToString();
        var token1 = GenerateToken(jti: jti1);
        var token2 = GenerateToken(jti: jti2);

        // Revoke only token1
        _revocationStore.Revoke(jti1);

        var request1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token1);
        var response1 = await _client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var request2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/protected");
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token2);
        var response2 = await _client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #region Helpers

    private static string GenerateToken(string? jti = null)
    {
        var keyBytes = Encoding.UTF8.GetBytes(TestSigningKey);
        if (keyBytes.Length < 32) Array.Resize(ref keyBytes, 32);
        var key = new SymmetricSecurityKey(keyBytes);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(TokenClaimConstants.TokenType, TokenClaimConstants.TokenTypeUser),
            new(ClaimTypes.NameIdentifier, "user-123"),
            new(TokenClaimConstants.OrgId, "org-456")
        };

        if (jti is not null)
        {
            claims.Add(new Claim("jti", jti));
        }

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: TestAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// In-memory implementation of ITokenRevocationStore for testing.
    /// </summary>
    private sealed class InMemoryRevocationStore : ITokenRevocationStore
    {
        private readonly ConcurrentDictionary<string, bool> _revokedTokens = new();

        public void Revoke(string jti) => _revokedTokens[jti] = true;

        public Task<bool> IsRevokedAsync(string jti, CancellationToken cancellationToken = default)
            => Task.FromResult(_revokedTokens.ContainsKey(jti));
    }

    #endregion
}
