// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// HttpClient implementation for polling HAIP offer status and the Blueprint BFF presentation status.
/// Result polling routes through Blueprint's user-authenticated <c>GET /api/presentations/{id}/status</c>
/// endpoint rather than the service-only HAIP verifier endpoint.
/// </summary>
public class HaipOfferService : IHaipOfferService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HaipOfferService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    /// <summary>Initializes a new instance of <see cref="HaipOfferService"/>.</summary>
    public HaipOfferService(HttpClient httpClient, ILogger<HaipOfferService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the current status of a HAIP credential offer.</summary>
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

    /// <summary>
    /// Polls <c>GET /api/presentations/{requestId}/status</c> on the Blueprint BFF and returns a
    /// discriminated <see cref="VerificationPollOutcome"/>. Transport errors (401, 403, 5xx, network)
    /// are surfaced as <see cref="VerificationPollOutcome.IsTransportError"/> rather than swallowed.
    /// </summary>
    public async Task<VerificationPollOutcome> GetVerificationResultAsync(
        Guid requestId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/presentations/{requestId}/status", ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Request ID unknown at BFF — treat as expired, not as a transport error.
                return new VerificationPollOutcome
                {
                    Result = new HaipVerificationResult(requestId, HaipVerificationStates.Expired, false, null, null)
                };
            }

            if (response.StatusCode is
                System.Net.HttpStatusCode.Unauthorized or
                System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Verification poll rejected for {RequestId}: {StatusCode}",
                    requestId, (int)response.StatusCode);
                return new VerificationPollOutcome
                {
                    IsTransportError = true,
                    ErrorMessage = $"Authentication error ({(int)response.StatusCode}). Please refresh and try again."
                };
            }

            if ((int)response.StatusCode >= 500)
            {
                _logger.LogWarning(
                    "Verification poll server error for {RequestId}: {StatusCode}",
                    requestId, (int)response.StatusCode);
                return new VerificationPollOutcome
                {
                    IsTransportError = true,
                    ErrorMessage = "A server error occurred. Please try again."
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Unexpected status polling verification for {RequestId}: {StatusCode}",
                    requestId, (int)response.StatusCode);
                return new VerificationPollOutcome
                {
                    IsTransportError = true,
                    ErrorMessage = $"Unexpected error ({(int)response.StatusCode}). Please try again."
                };
            }

            var status = await response.Content.ReadFromJsonAsync<PresentationStatusDto>(JsonOptions, ct);
            if (status is null)
            {
                return new VerificationPollOutcome { Result = null, IsTransportError = false };
            }

            return MapStatusToOutcome(requestId, status.State);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error polling verification for {RequestId}", requestId);
            return new VerificationPollOutcome
            {
                IsTransportError = true,
                ErrorMessage = "Could not reach the server. Check your connection and try again."
            };
        }
    }

    private static VerificationPollOutcome MapStatusToOutcome(Guid requestId, string? state) =>
        state switch
        {
            "success" => new VerificationPollOutcome
            {
                Result = new HaipVerificationResult(requestId, HaipVerificationStates.Verified, true, null, null)
            },
            "decline" => new VerificationPollOutcome
            {
                Result = new HaipVerificationResult(requestId, HaipVerificationStates.Denied, false, null, null)
            },
            "abandoned" or "abandoned-with-late-outcome" => new VerificationPollOutcome
            {
                Result = new HaipVerificationResult(requestId, HaipVerificationStates.Cancelled, false, null, null)
            },
            "expired" => new VerificationPollOutcome
            {
                Result = new HaipVerificationResult(requestId, HaipVerificationStates.Expired, false, null, null)
            },
            // "awaiting-presentation", "unknown", or any unrecognised state: session is live, keep polling.
            _ => new VerificationPollOutcome { Result = null, IsTransportError = false }
        };

    /// <summary>Minimal DTO for the Blueprint <c>GET /api/presentations/{id}/status</c> response.</summary>
    private sealed class PresentationStatusDto
    {
        [JsonPropertyName("state")]
        public string? State { get; init; }
    }
}
