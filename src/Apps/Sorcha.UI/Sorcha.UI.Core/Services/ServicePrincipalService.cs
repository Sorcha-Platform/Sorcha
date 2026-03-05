// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client implementation for service principal management.
/// </summary>
public class ServicePrincipalService : IServicePrincipalService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServicePrincipalService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ServicePrincipalService(HttpClient httpClient, ILogger<ServicePrincipalService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ServicePrincipalListResult> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        try
        {
            var url = includeInactive ? "/api/service-principals?includeInactive=true" : "/api/service-principals";
            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to list service principals: {StatusCode}", response.StatusCode);
                return new ServicePrincipalListResult();
            }
            return await response.Content.ReadFromJsonAsync<ServicePrincipalListResult>(JsonOptions, ct)
                ?? new ServicePrincipalListResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing service principals");
            return new ServicePrincipalListResult();
        }
    }

    public async Task<ServicePrincipalViewModel?> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/service-principals/{id}", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<ServicePrincipalViewModel>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service principal {Id}", id);
            return null;
        }
    }

    public async Task<ServicePrincipalSecretViewModel> CreateAsync(CreateServicePrincipalRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/service-principals", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServicePrincipalSecretViewModel>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to deserialize create response");
    }

    public async Task<ServicePrincipalViewModel?> UpdateScopesAsync(Guid id, string[] scopes, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"/api/service-principals/{id}/scopes", new { scopes }, JsonOptions, ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<ServicePrincipalViewModel>(JsonOptions, ct);
    }

    public async Task<bool> SuspendAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/service-principals/{id}/suspend", null, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/service-principals/{id}/reactivate", null, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"/api/service-principals/{id}", ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<ServicePrincipalSecretViewModel> RotateSecretAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsync($"/api/service-principals/{id}/rotate-secret", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ServicePrincipalSecretViewModel>(JsonOptions, ct)
            ?? throw new InvalidOperationException("Failed to deserialize rotate response");
    }
}
