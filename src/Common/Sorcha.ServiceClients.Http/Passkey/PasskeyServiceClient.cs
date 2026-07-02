// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Serialization;

namespace Sorcha.ServiceClients.Passkey;

/// <summary>
/// HTTP client for retrieving passkey public keys from the Tenant Service.
/// </summary>
public class PasskeyServiceClient : IPasskeyServiceClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PasskeyServiceClient> _logger;


    public PasskeyServiceClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PasskeyServiceClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var serviceAddress = configuration["ServiceClients:TenantService:Address"]
            ?? "https+http://tenant-service";

        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(serviceAddress.TrimEnd('/') + "/");
        }
    }

    /// <inheritdoc/>
    public async Task<PasskeyRecoveryKeyInfo?> GetRecoveryPublicKeyAsync(
        string userId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"api/users/{Uri.EscapeDataString(userId)}/passkeys/recovery-key", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("No passkey registered for user {UserId}", userId);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<PasskeyRecoveryKeyInfo>(SorchaJson.Options, ct);
            _logger.LogDebug("Retrieved passkey public key for user {UserId}", userId);
            return result;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve passkey public key for user {UserId}", userId);
            return null;
        }
    }
}
