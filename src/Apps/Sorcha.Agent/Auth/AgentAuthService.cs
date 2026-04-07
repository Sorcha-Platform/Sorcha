// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Auth;

/// <summary>
/// Handles authentication with the Tenant Service using email/password credentials.
/// </summary>
public class AgentAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ConnectionConfig _config;
    private readonly ILogger<AgentAuthService> _logger;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;
    private DateTimeOffset _tokenExpiry;

    public AgentAuthService(HttpClient httpClient, ConnectionConfig config, ILogger<AgentAuthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _httpClient.BaseAddress = new Uri(config.GatewayUrl.TrimEnd('/'));
    }

    /// <summary>
    /// Authenticates with the Tenant Service and caches the JWT.
    /// </summary>
    public async Task AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Authenticating as {Email}...", _config.Credentials.Email);

            // Step 1: Login
            var loginResponse = await _httpClient.PostAsJsonAsync("/api/auth/login", new
            {
                email = _config.Credentials.Email,
                password = _config.Credentials.Password
            }, cancellationToken);

            loginResponse.EnsureSuccessStatusCode();
            var loginJson = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
            var loginDoc = JsonDocument.Parse(loginJson);

            // Check if org selection is required
            if (loginDoc.RootElement.TryGetProperty("requires_org_selection", out var reqOrgProp) &&
                reqOrgProp.GetBoolean())
            {
                // Step 2: Select org
                var platformToken = loginDoc.RootElement.GetProperty("platform_login_token").GetString()!;
                var selectResponse = await _httpClient.PostAsJsonAsync("/api/auth/select-org", new
                {
                    platform_login_token = platformToken,
                    organization_id = _config.Credentials.OrganizationId
                }, cancellationToken);

                selectResponse.EnsureSuccessStatusCode();
                loginJson = await selectResponse.Content.ReadAsStringAsync(cancellationToken);
                loginDoc.Dispose();
                loginDoc = JsonDocument.Parse(loginJson);
            }

            // Extract token
            _accessToken = loginDoc.RootElement.GetProperty("access_token").GetString()!;
            _refreshToken = loginDoc.RootElement.TryGetProperty("refresh_token", out var rt)
                ? rt.GetString()
                : null;

            var expiresIn = loginDoc.RootElement.TryGetProperty("expires_in", out var exp)
                ? exp.GetInt32()
                : 3600;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60); // 1-min buffer

            loginDoc.Dispose();
            _logger.LogInformation("Authenticated successfully");
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>
    /// Gets a valid access token, refreshing if needed.
    /// </summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiry)
            return _accessToken;

        // Try refresh first, fall back to full re-auth
        if (_refreshToken is not null)
        {
            try
            {
                await RefreshTokenAsync(cancellationToken);
                return _accessToken!;
            }
            catch
            {
                _logger.LogWarning("Token refresh failed, re-authenticating");
            }
        }

        await AuthenticateAsync(cancellationToken);
        return _accessToken!;
    }

    /// <summary>
    /// Token provider func for SignalR hub connections.
    /// </summary>
    public Task<string?> TokenProviderAsync() => GetTokenAsync().ContinueWith(t => (string?)t.Result);

    private async Task RefreshTokenAsync(CancellationToken cancellationToken)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/auth/token/refresh", new
            {
                refresh_token = _refreshToken
            }, cancellationToken);

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            _accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
            if (doc.RootElement.TryGetProperty("refresh_token", out var rt))
                _refreshToken = rt.GetString();

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60);

            doc.Dispose();
        }
        finally
        {
            _authLock.Release();
        }
    }
}
