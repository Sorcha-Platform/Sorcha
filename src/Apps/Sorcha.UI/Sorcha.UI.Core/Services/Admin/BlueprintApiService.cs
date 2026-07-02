// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.ServiceClients.Blueprint.Models;
using Sorcha.UI.Core.Extensions;
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

            var result = await _httpClient.GetFromJsonAsync<PaginatedList<BlueprintListItemViewModel>>(url, JsonDefaults.Api, cancellationToken);
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
            return await _httpClient.GetFromJsonAsync<BlueprintListItemViewModel>($"/api/blueprints/{id}", JsonDefaults.Api, cancellationToken);
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
            return await _httpClient.GetFromJsonAsync<Sorcha.Blueprint.Models.Blueprint>($"/api/blueprints/{id}", JsonDefaults.Api, cancellationToken);
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
                return await response.Content.ReadFromJsonAsync<BlueprintListItemViewModel>(JsonDefaults.Api, cancellationToken);
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
                return await response.Content.ReadFromJsonAsync<BlueprintListItemViewModel>(JsonDefaults.Api, cancellationToken);
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
                return await response.Content.ReadFromJsonAsync<PublishReviewViewModel>(JsonDefaults.Api, cancellationToken);
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
                return await response.Content.ReadFromJsonAsync<BlueprintValidationResponse>(JsonDefaults.Api, cancellationToken);
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
                return await response.Content.ReadFromJsonAsync<PublishReviewViewModel>(JsonDefaults.Api, cancellationToken);
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

    public async Task<GoLivePublishOutcome> PublishGoLiveAsync(
        string id,
        string registerId,
        bool confirmOverride = false,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new PublishBlueprintRequest
            {
                RegisterId = registerId,
                Override = confirmOverride
                    ? new PublishOverride { Confirm = true, Reason = overrideReason }
                    : null
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"/api/blueprints/{id}/publish", request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PublishBlueprintResult>(
                    JsonDefaults.Api, cancellationToken);
                return result is not null
                    ? GoLivePublishOutcome.Published(result)
                    : GoLivePublishOutcome.Errored("Publish succeeded but the response could not be read.");
            }

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var error = await response.Content.ReadFromJsonAsync<RehearsalRequiredError>(
                    JsonDefaults.Api, cancellationToken);
                return GoLivePublishOutcome.NeedsRehearsal(
                    error ?? new RehearsalRequiredError
                    {
                        Message = "This version has not passed a full rehearsal."
                    });
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                _logger.LogInformation(
                    "Publish of blueprint {Id} to register {RegisterId} refused (403 — no publish rights)",
                    id, registerId);
                return GoLivePublishOutcome.Refused(
                    "You do not have publishing rights (Owner, Admin, or Designer) on this register.");
            }

            _logger.LogWarning(
                "Go-live publish of blueprint {Id} to register {RegisterId} failed: {StatusCode}",
                id, registerId, response.StatusCode);
            return GoLivePublishOutcome.Errored(
                $"Publishing failed ({(int)response.StatusCode}). Please try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing blueprint {Id} to register {RegisterId}", id, registerId);
            return GoLivePublishOutcome.Errored($"Publishing error: {ex.Message}");
        }
    }

    public async Task<List<BlueprintVersionViewModel>> GetVersionsAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var versions = await _httpClient.GetFromJsonAsync<List<BlueprintVersionViewModel>>($"/api/blueprints/{id}/versions", JsonDefaults.Api, cancellationToken);
            return versions ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching versions for blueprint {Id}", id);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<string?> FromPublishedAsync(
        string registerId,
        string blueprintId,
        int version,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = new { registerId, blueprintId, version };
            var response = await _httpClient.PostAsJsonAsync(
                "/api/blueprints/from-published", body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "FromPublished failed: {StatusCode} (register {RegisterId}, source {BlueprintId} v{Version})",
                    response.StatusCode, registerId, blueprintId, version);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<CloneFromPublishedResponse>(
                JsonDefaults.Api, cancellationToken);
            return result?.DraftBlueprintId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning published blueprint {BlueprintId} v{Version}", blueprintId, version);
            return null;
        }
    }

    private sealed record CloneFromPublishedResponse(
        string DraftBlueprintId,
        int SourceVersion,
        string RegisterId);

    public async Task<BlueprintListItemViewModel?> GetVersionAsync(string id, string version, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BlueprintListItemViewModel>($"/api/blueprints/{id}/versions/{version}", JsonDefaults.Api, cancellationToken);
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
                JsonDefaults.Api,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching available blueprints for wallet {Wallet} on register {Register}", walletAddress, registerId);
            return null;
        }
    }

    public async Task<Sorcha.Blueprint.Models.Blueprint?> GetPublishedBlueprintDetailAsync(string walletAddress, string registerId, string blueprintId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<Sorcha.Blueprint.Models.Blueprint>(
                $"/api/actions/{Uri.EscapeDataString(walletAddress)}/{Uri.EscapeDataString(registerId)}/blueprints/{Uri.EscapeDataString(blueprintId)}",
                JsonDefaults.Api,
                cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Published blueprint {Blueprint} not found in register {Register}", blueprintId, registerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching published blueprint {Blueprint} in register {Register}", blueprintId, registerId);
            return null;
        }
    }
}
