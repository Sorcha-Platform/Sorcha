// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services.Admin;

/// <summary>
/// HttpClient implementation for push notification subscription endpoints.
/// </summary>
public class PushNotificationService : IPushNotificationService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(HttpClient httpClient, ILogger<PushNotificationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PushSubscriptionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/push-subscriptions/status", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch push subscription status: {StatusCode}", response.StatusCode);
                return new PushSubscriptionStatus(false);
            }

            return await response.Content.ReadFromJsonAsync<PushSubscriptionStatus>(JsonDefaults.Api, ct)
                   ?? new PushSubscriptionStatus(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error fetching push subscription status");
            return new PushSubscriptionStatus(false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> SubscribeAsync(PushSubscriptionRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/push-subscriptions", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to subscribe to push notifications: {StatusCode}", response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<PushSubscriptionResponse>(JsonDefaults.Api, ct);
            return result?.Subscribed ?? false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error subscribing to push notifications");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnsubscribeAsync(string endpoint, CancellationToken ct = default)
    {
        try
        {
            var encodedEndpoint = Uri.EscapeDataString(endpoint);
            var response = await _httpClient.DeleteAsync($"/api/push-subscriptions?endpoint={encodedEndpoint}", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to unsubscribe from push notifications: {StatusCode}", response.StatusCode);
            }

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error unsubscribing from push notifications");
            return false;
        }
    }
}
