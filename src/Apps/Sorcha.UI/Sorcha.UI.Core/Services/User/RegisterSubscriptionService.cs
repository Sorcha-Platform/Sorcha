// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client for register subscription operations via the Tenant Service API.
/// </summary>
public class RegisterSubscriptionService : IRegisterSubscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<RegisterSubscriptionService> _logger;

    public RegisterSubscriptionService(HttpClient httpClient, ILogger<RegisterSubscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<RegisterSubscriptionDto>> GetMySubscribedRegistersAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/me/subscribed-registers", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch subscribed registers: {StatusCode}", response.StatusCode);
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<RegisterSubscriptionDto>>(JsonDefaults.Api, ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching subscribed registers");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionDto?> SubscribeAsync(Guid orgId, string registerId, string? registerName = null, string? description = null, CancellationToken ct = default)
    {
        try
        {
            var payload = new { register_id = registerId, register_name = registerName, description };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/organizations/{Uri.EscapeDataString(orgId.ToString())}/register-subscriptions",
                payload,
                JsonDefaults.Api,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to subscribe to register {RegisterId}: {StatusCode} - {Error}",
                    registerId, response.StatusCode, error);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterSubscriptionDto>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<RegisterSubscriptionDto?> CreateOwnerSubscriptionAsync(
        Guid orgId, string registerId, string? registerName, CancellationToken ct = default)
    {
        try
        {
            var payload = new { register_id = registerId, register_name = registerName, subscription_type = "Owner" };
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/organizations/{Uri.EscapeDataString(orgId.ToString())}/register-subscriptions",
                payload,
                JsonDefaults.Api,
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Failed to create owner subscription for register {RegisterId}: {StatusCode} - {Error}",
                    registerId, response.StatusCode, error);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<RegisterSubscriptionDto>(JsonDefaults.Api, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating owner subscription for register {RegisterId}", registerId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnsubscribeAsync(Guid orgId, string registerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"/api/organizations/{Uri.EscapeDataString(orgId.ToString())}/register-subscriptions/{Uri.EscapeDataString(registerId)}",
                ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to unsubscribe from register {RegisterId}: {StatusCode}",
                    registerId, response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from register {RegisterId}", registerId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<List<AvailableRegisterDto>> GetAvailableRegistersAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/registers/available", ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch available registers: {StatusCode}", response.StatusCode);
                return [];
            }

            return await response.Content.ReadFromJsonAsync<List<AvailableRegisterDto>>(JsonDefaults.Api, ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available registers");
            return [];
        }
    }
}
