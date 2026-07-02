// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.HolderKeys;

/// <summary>
/// Default <see cref="IHolderKeyClient"/> — thin wrapper over the Wallet Service
/// <c>GET /api/v1/wallet/holder-keys</c> endpoint. Registered with an auth-wrapped
/// <see cref="HttpClient"/> so the citizen's consumer-tier JWT is attached automatically
/// (the endpoint is gated by <c>RequireConsumerAudience</c>). Feature 137.
/// </summary>
public sealed class HolderKeyHttpClient : IHolderKeyClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HolderKeyHttpClient> _logger;

    public HolderKeyHttpClient(HttpClient httpClient, ILogger<HolderKeyHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<HolderKeysView?> GetHolderKeysAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v1/wallet/holder-keys", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "GET /api/v1/wallet/holder-keys returned {Status}", (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<HolderKeysView>(JsonDefaults.Api, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Holder-keys request failed");
            return null;
        }
    }
}
