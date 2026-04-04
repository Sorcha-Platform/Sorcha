// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// OAuth2 client_credentials implementation for service-to-service JWT token acquisition
/// </summary>
/// <remarks>
/// Acquires tokens from the Tenant Service POST /api/service-auth/token endpoint.
/// Tokens are cached in-memory and refreshed when within 5 minutes of expiry.
/// Thread-safe via SemaphoreSlim.
/// </remarks>
public class ServiceAuthClient : IServiceAuthClient, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceAuthClient> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _scopes;

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);
    private const string DefaultScopes = "wallets:sign";

    public ServiceAuthClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ServiceAuthClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _clientId = configuration["ServiceAuth:ClientId"]
            ?? throw new InvalidOperationException("ServiceAuth:ClientId not configured");
        _clientSecret = configuration["ServiceAuth:ClientSecret"]
            ?? throw new InvalidOperationException("ServiceAuth:ClientSecret not configured");
        _scopes = configuration["ServiceAuth:Scopes"] ?? DefaultScopes;

        // Set base address for Tenant Service (JWT issuer)
        if (_httpClient.BaseAddress is null)
        {
            var tenantAddress = configuration["ServiceClients:TenantService:Address"]
                ?? configuration["TenantService:Endpoint"]
                ?? "http://tenant-service";
            _httpClient.BaseAddress = new Uri(tenantAddress);
            _logger.LogInformation("ServiceAuthClient targeting Tenant Service at {Address}", tenantAddress);
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // Fast path: return cached token if still valid
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry - RefreshBuffer)
        {
            _logger.LogDebug(
                "Service token cache hit for {ClientId}, expires at {Expiry}",
                _clientId, _tokenExpiry);
            return _cachedToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiry - RefreshBuffer)
            {
                _logger.LogDebug(
                    "Service token cache hit for {ClientId} after lock acquisition",
                    _clientId);
                return _cachedToken;
            }

            return await RefreshTokenAsync(cancellationToken);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<string?> RefreshTokenAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Service token refresh triggered for {ClientId} with scopes [{Scopes}]",
                _clientId, _scopes);

            var formData = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["scope"] = _scopes
            });

            var response = await _httpClient.PostAsync("/api/service-auth/token", formData, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Service token acquisition failed for {ClientId}: HTTP {StatusCode}",
                    _clientId, (int)response.StatusCode);
                _cachedToken = null;
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (tokenResponse?.AccessToken is null)
            {
                _logger.LogWarning(
                    "Service token response for {ClientId} was null or missing access_token",
                    _clientId);
                _cachedToken = null;
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _logger.LogInformation(
                "Service token acquired for {ClientId}, expires at {Expiry} (in {ExpiresInSeconds}s)",
                _clientId, _tokenExpiry, tokenResponse.ExpiresIn);

            return _cachedToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Service token acquisition failed for {ClientId}: network error communicating with Tenant Service",
                _clientId);
            _cachedToken = null;
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Service token acquisition failed for {ClientId}: unexpected error",
                _clientId);
            _cachedToken = null;
            return null;
        }
    }

    public void Dispose()
    {
        _refreshLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
