// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.AddressLookup;

/// <summary>
/// Default <see cref="IAddressLookupClient"/> — thin wrapper over the
/// Tenant Service <c>/api/address-lookup/*</c> endpoints. Registered in
/// <c>ServiceCollectionExtensions</c> with <c>AuthenticatedHttpMessageHandler</c>
/// so the user's JWT bearer is attached automatically.
/// </summary>
public sealed class AddressLookupHttpClient : IAddressLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AddressLookupHttpClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AddressLookupHttpClient(HttpClient httpClient, ILogger<AddressLookupHttpClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderInfo>?> GetProvidersAsync(CancellationToken ct = default)
    {
        try
        {
            var providers = await _httpClient.GetFromJsonAsync<List<ProviderInfo>>(
                "/api/address-lookup/providers", JsonDefaults.Api, ct);
            return providers;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Address lookup provider discovery failed");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<LookupResult?> LookupPostcodeAsync(string postcode, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "/api/address-lookup/postcode",
                new { postcode },
                JsonOptions,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "POST /api/address-lookup/postcode returned {Status}",
                    (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<LookupResult>(JsonDefaults.Api, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Address lookup request failed");
            return null;
        }
    }
}
