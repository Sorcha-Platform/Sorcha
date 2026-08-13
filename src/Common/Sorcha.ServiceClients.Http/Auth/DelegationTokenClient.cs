// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Acquires delegation tokens from the Tenant Service for on-behalf-of-user operations.
/// </summary>
/// <remarks>
/// Delegation tokens are short-lived (5 minutes) and user-specific, so they are NOT cached.
/// Each call acquires a service token via IServiceAuthClient, then POSTs both the service
/// token and the user's access token to /api/internal/service-auth/token/delegated — the
/// internal-only route (not routed by the public API Gateway, per #1397).
/// </remarks>
public class DelegationTokenClient : IDelegationTokenClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuthClient;
    private readonly ILogger<DelegationTokenClient> _logger;

    public DelegationTokenClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuthClient,
        ILogger<DelegationTokenClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuthClient = serviceAuthClient ?? throw new ArgumentNullException(nameof(serviceAuthClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> GetDelegationTokenAsync(
        string userAccessToken,
        string[] scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(userAccessToken);
        ArgumentNullException.ThrowIfNull(scopes);

        try
        {
            // Step 1: Acquire a service token for authentication
            var serviceToken = await _serviceAuthClient.GetTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(serviceToken))
            {
                _logger.LogWarning("Delegation token request failed: could not acquire service token");
                return null;
            }

            _logger.LogInformation(
                "Requesting delegation token with scopes [{Scopes}]",
                string.Join(" ", scopes));

            // Step 2: POST to Tenant Service delegation endpoint
            var request = new DelegationRequest
            {
                ServiceToken = serviceToken,
                UserAccessToken = userAccessToken,
                Scopes = scopes
            };

            var response = await _httpClient.PostAsJsonAsync(
                "/api/internal/service-auth/token/delegated",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Delegation token request failed: HTTP {StatusCode}",
                    (int)response.StatusCode);
                return null;
            }

            var tokenResponse = await response.Content.ReadFromJsonAsync<DelegationResponse>(SorchaJson.Options, cancellationToken);
            if (tokenResponse?.AccessToken is null)
            {
                _logger.LogWarning("Delegation token response was null or missing access_token");
                return null;
            }

            _logger.LogInformation(
                "Delegation token acquired for user {DelegatedUserId}, expires in {ExpiresIn}s",
                tokenResponse.DelegatedUserId, tokenResponse.ExpiresIn);

            return tokenResponse.AccessToken;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex,
                "Delegation token request failed: network error communicating with Tenant Service");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Delegation token request failed: unexpected error");
            return null;
        }
    }

    private sealed class DelegationRequest
    {
        /// <summary>The service token.</summary>
        [JsonPropertyName("service_token")]
        public required string ServiceToken { get; set; }

        /// <summary>The user access token.</summary>
        [JsonPropertyName("user_access_token")]
        public required string UserAccessToken { get; set; }

        /// <summary>OAuth scopes granted or requested.</summary>
        [JsonPropertyName("scopes")]
        public required string[] Scopes { get; set; }
    }

    private sealed class DelegationResponse
    {
        /// <summary>OAuth access token.</summary>
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        /// <summary>The token type.</summary>
        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        /// <summary>Numeric value for expires in.</summary>
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        /// <summary>Identifier of the delegated user.</summary>
        [JsonPropertyName("delegated_user_id")]
        public string? DelegatedUserId { get; set; }

        /// <summary>Identifier of the delegated org.</summary>
        [JsonPropertyName("delegated_org_id")]
        public string? DelegatedOrgId { get; set; }

        /// <summary>The scope.</summary>
        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }
}
