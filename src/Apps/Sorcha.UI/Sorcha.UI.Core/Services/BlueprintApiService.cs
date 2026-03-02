// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Blueprints;
using Sorcha.UI.Core.Models.Common;
using Sorcha.UI.Core.Models.Workflows;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IBlueprintApiService"/> calling the Blueprint Service API.
/// </summary>
public class BlueprintApiService : IBlueprintApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BlueprintApiService> _logger;

    public BlueprintApiService(HttpClient httpClient, ILogger<BlueprintApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PaginatedList<BlueprintListItemViewModel>> GetBlueprintsAsync(int page = 1, int pageSize = 20, string? search = null, string? status = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/api/blueprints?page={page}&pageSize={pageSize}";
            if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrEmpty(status)) url += $"&status={Uri.EscapeDataString(status)}";

            var result = await _httpClient.GetFromJsonAsync<PaginatedList<BlueprintListItemViewModel>>(url, cancellationToken);
            return result ?? new PaginatedList<BlueprintListItemViewModel>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprints");
            return new PaginatedList<BlueprintListItemViewModel>();
        }
    }

    public async Task<BlueprintListItemViewModel?> GetBlueprintAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BlueprintListItemViewModel>($"/api/blueprints/{id}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint {Id}", id);
            return null;
        }
    }

    public async Task<Sorcha.Blueprint.Models.Blueprint?> GetBlueprintDetailAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Sorcha.Blueprint.Models.Blueprint>($"/api/blueprints/{id}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint detail {Id}", id);
            return null;
        }
    }

    public async Task<BlueprintListItemViewModel?> SaveBlueprintAsync(object blueprint, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync("/api/blueprints", blueprint, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<BlueprintListItemViewModel>(cancellationToken: cancellationToken);
            }
            _logger.LogWarning("Failed to save blueprint: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving blueprint");
            return null;
        }
    }

    public async Task<BlueprintListItemViewModel?> UpdateBlueprintAsync(string id, object blueprint, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PutAsJsonAsync($"/api/blueprints/{id}", blueprint, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<BlueprintListItemViewModel>(cancellationToken: cancellationToken);
            }
            _logger.LogWarning("Failed to update blueprint {Id}: {StatusCode}", id, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating blueprint {Id}", id);
            return null;
        }
    }

    public async Task<bool> DeleteBlueprintAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/api/blueprints/{id}", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blueprint {Id}", id);
            return false;
        }
    }

    public async Task<PublishReviewViewModel?> PublishBlueprintAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/blueprints/{id}/publish", null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PublishReviewViewModel>(cancellationToken: cancellationToken);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing blueprint {Id}", id);
            return null;
        }
    }

    public async Task<BlueprintValidationResponse?> ValidateBlueprintAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/blueprints/{id}/validate", null, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<BlueprintValidationResponse>(cancellationToken: cancellationToken);
            }
            _logger.LogWarning("Failed to validate blueprint {Id}: {StatusCode}", id, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating blueprint {Id}", id);
            return null;
        }
    }

    public async Task<PublishReviewViewModel?> PublishBlueprintToRegisterAsync(string id, string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { registerId };
            var response = await _httpClient.PostAsJsonAsync($"/api/blueprints/{id}/publish", body, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PublishReviewViewModel>(cancellationToken: cancellationToken);
            }
            _logger.LogWarning("Failed to publish blueprint {Id} to register {RegisterId}: {StatusCode}", id, registerId, response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing blueprint {Id} to register {RegisterId}", id, registerId);
            return null;
        }
    }

    public async Task<List<BlueprintVersionViewModel>> GetVersionsAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await _httpClient.GetFromJsonAsync<List<BlueprintVersionViewModel>>($"/api/blueprints/{id}/versions", cancellationToken);
            return versions ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching versions for blueprint {Id}", id);
            return [];
        }
    }

    public async Task<BlueprintListItemViewModel?> GetVersionAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BlueprintListItemViewModel>($"/api/blueprints/{id}/versions/{version}", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprint {Id} version {Version}", id, version);
            return null;
        }
    }

    public async Task<AvailableBlueprintsViewModel?> GetAvailableBlueprintsAsync(string walletAddress, string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<AvailableBlueprintsViewModel>(
                $"/api/actions/{Uri.EscapeDataString(walletAddress)}/{Uri.EscapeDataString(registerId)}/blueprints",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available blueprints for wallet {Wallet} on register {Register}", walletAddress, registerId);
            return null;
        }
    }
}
