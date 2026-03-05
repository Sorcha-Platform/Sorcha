// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP implementation of <see cref="IServicePrincipalService"/> that calls the Tenant Service API.
/// </summary>
public class ServicePrincipalService : IServicePrincipalService
{
    private const string BasePath = "/api/service-principals";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ServicePrincipalService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ServicePrincipalService"/>.
    /// </summary>
    public ServicePrincipalService(HttpClient httpClient, ILogger<ServicePrincipalService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalListResult> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        try
        {
            var query = includeInactive ? "?includeInactive=true" : "";
            var result = await _httpClient.GetFromJsonAsync<ServicePrincipalListResult>(
                $"{BasePath}{query}", ct);
            return result ?? new ServicePrincipalListResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing service principals");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalViewModel?> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ServicePrincipalViewModel>(
                $"{BasePath}/{id}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service principal {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalSecretViewModel> CreateAsync(CreateServicePrincipalRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(BasePath, request, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ServicePrincipalSecretViewModel>(cancellationToken: ct);
            return result ?? throw new InvalidOperationException("Empty response from create endpoint");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating service principal {Name}", request.ServiceName);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalViewModel?> UpdateScopesAsync(Guid id, string[] scopes, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"{BasePath}/{id}/scopes", new { scopes }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<ServicePrincipalViewModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scopes for service principal {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SuspendAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{BasePath}/{id}/suspend", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error suspending service principal {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{BasePath}/{id}/reactivate", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reactivating service principal {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{BasePath}/{id}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking service principal {Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ServicePrincipalSecretViewModel> RotateSecretAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"{BasePath}/{id}/rotate-secret", null, ct);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ServicePrincipalSecretViewModel>(cancellationToken: ct);
            return result ?? throw new InvalidOperationException("Empty response from rotate secret endpoint");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating secret for service principal {Id}", id);
            throw;
        }
    }
}
