// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// HttpClient implementation for polling HAIP offer and verifier status endpoints.
/// Routes through the API Gateway to the HAIP Service.
/// </summary>
public class HaipOfferService : IHaipOfferService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HaipOfferService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    public HaipOfferService(HttpClient httpClient, ILogger<HaipOfferService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HaipOfferStatus?> GetOfferStatusAsync(Guid offerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/v1/offers/{offerId}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get offer status for {OfferId}: {StatusCode}",
                    offerId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<HaipOfferStatus>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polling offer status for {OfferId}", offerId);
            return null;
        }
    }

    public async Task<HaipVerificationResult?> GetVerificationResultAsync(
        Guid requestId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/verifier/requests/{requestId}/result", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get verification result for {RequestId}: {StatusCode}",
                    requestId, response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<HaipVerificationResult>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error polling verification result for {RequestId}", requestId);
            return null;
        }
    }
}
