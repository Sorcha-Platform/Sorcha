// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Auth;

/// <summary>
/// Introspects tokens via the Tenant Service's POST /api/auth/token/introspect endpoint.
/// Requires a service token for authentication.
/// </summary>
public class TokenIntrospectionClient : ITokenIntrospectionClient
{
    private readonly HttpClient _httpClient;
    private readonly IServiceAuthClient _serviceAuthClient;
    private readonly ILogger<TokenIntrospectionClient> _logger;

    public TokenIntrospectionClient(
        HttpClient httpClient,
        IServiceAuthClient serviceAuthClient,
        ILogger<TokenIntrospectionClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serviceAuthClient = serviceAuthClient ?? throw new ArgumentNullException(nameof(serviceAuthClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TokenIntrospectionResult?> IntrospectAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        try
        {
            // Acquire service token for authentication
            var serviceToken = await _serviceAuthClient.GetTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(serviceToken))
            {
                _logger.LogWarning("Token introspection failed: could not acquire service token");
                return null;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/token/introspect")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token
                })
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Token introspection failed: HTTP {StatusCode}",
                    (int)response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<IntrospectionResponse>(cancellationToken);
            if (result is null)
            {
                _logger.LogWarning("Token introspection returned null response");
                return null;
            }

            return new TokenIntrospectionResult
            {
                Active = result.Active,
                Sub = result.Sub,
                TokenType = result.TokenType,
                Scope = result.Scope,
                ClientId = result.ClientId,
                Exp = result.Exp,
                Iat = result.Iat
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Token introspection failed: network error");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Token introspection failed: unexpected error");
            return null;
        }
    }

    private sealed class IntrospectionResponse
    {
        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("client_id")]
        public string? ClientId { get; set; }

        [JsonPropertyName("exp")]
        public long? Exp { get; set; }

        [JsonPropertyName("iat")]
        public long? Iat { get; set; }
    }
}
