// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models;
using Sorcha.UI.Core.Models.Designer;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of blueprint storage with server sync and offline support.
/// </summary>
public class BlueprintStorageService : IBlueprintStorageService
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly IOfflineSyncService _syncService;
    private readonly ILogger<BlueprintStorageService> _logger;

    private const string BLUEPRINTS_CACHE_KEY = "sorcha:blueprints";
    private const string BLUEPRINTS_CACHE_TIMESTAMP_KEY = "sorcha:blueprints:timestamp";
    private const string SERVER_HEALTH_KEY = "sorcha:server:healthy";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public event EventHandler<BlueprintSavedEventArgs>? BlueprintSaved;

    public BlueprintStorageService(
        HttpClient httpClient,
        ILocalStorageService localStorage,
        IOfflineSyncService syncService,
        ILogger<BlueprintStorageService> logger)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _syncService = syncService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Blueprint.Models.Blueprint>> GetBlueprintsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try server first
            if (await IsServerAvailableAsync(cancellationToken))
            {
                var serverBlueprints = await GetBlueprintsFromServerAsync(cancellationToken);
                if (serverBlueprints != null)
                {
                    // Update local cache
                    await UpdateLocalCacheAsync(serverBlueprints);
                    return serverBlueprints;
                }
            }

            // Fall back to local cache
            _logger.LogInformation("Using local cache for blueprints");
            return await GetBlueprintsFromCacheAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blueprints, falling back to cache");
            return await GetBlueprintsFromCacheAsync();
        }
    }

    /// <inheritdoc />
    public async Task<Blueprint.Models.Blueprint?> GetBlueprintAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try server first
            if (await IsServerAvailableAsync(cancellationToken))
            {
                var response = await _httpClient.GetAsync(
                    $"/api/blueprints/{Uri.EscapeDataString(id)}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var blueprint = await response.Content.ReadFromJsonAsync<Blueprint.Models.Blueprint>(
                        JsonOptions, cancellationToken);

                    if (blueprint != null)
                    {
                        // Update in local cache
                        await UpdateBlueprintInCacheAsync(blueprint);
                        return blueprint;
                    }
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
            }

            // Fall back to local cache
            var cached = await GetBlueprintsFromCacheAsync();
            return cached.FirstOrDefault(b => b.Id == id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blueprint {BlueprintId}, falling back to cache", id);
            var cached = await GetBlueprintsFromCacheAsync();
            return cached.FirstOrDefault(b => b.Id == id);
        }
    }

    /// <inheritdoc />
    public async Task<BlueprintSaveResult> SaveBlueprintAsync(
        Blueprint.Models.Blueprint blueprint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Update timestamp
            blueprint.UpdatedAt = DateTimeOffset.UtcNow;

            // Try server first
            if (await IsServerAvailableAsync(cancellationToken))
            {
                var result = await SaveToServerAsync(blueprint, cancellationToken);
                if (result.Success)
                {
                    // Update local cache
                    await UpdateBlueprintInCacheAsync(blueprint);
                    OnBlueprintSaved(blueprint.Id, savedToServer: true);
                    return result;
                }

                // Server save failed but we have connectivity - check if it's a conflict
                if (result.ErrorMessage?.Contains("conflict", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return result; // Return conflict error for UI handling
                }
            }

            // Offline or server error - save locally and queue for sync
            await SaveToLocalStorageAsync(blueprint);
            await _syncService.QueueOperationAsync(
                SyncOperation.Update,
                blueprint.Id,
                JsonSerializer.Serialize(blueprint, JsonOptions));

            OnBlueprintSaved(blueprint.Id, savedToServer: false);
            return BlueprintSaveResult.Queued(blueprint.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving blueprint {BlueprintId}", blueprint.Id);

            // Last resort - try to save locally
            try
            {
                await SaveToLocalStorageAsync(blueprint);
                await _syncService.QueueOperationAsync(
                    SyncOperation.Update,
                    blueprint.Id,
                    JsonSerializer.Serialize(blueprint, JsonOptions));

                return BlueprintSaveResult.Queued(blueprint.Id);
            }
            catch
            {
                return BlueprintSaveResult.Failure($"Failed to save blueprint: {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBlueprintAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try server first
            if (await IsServerAvailableAsync(cancellationToken))
            {
                var response = await _httpClient.DeleteAsync(
                    $"/api/blueprints/{Uri.EscapeDataString(id)}",
                    cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    await RemoveFromLocalCacheAsync(id);
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // Already deleted on server, remove from local
                    await RemoveFromLocalCacheAsync(id);
                    return true;
                }
            }

            // Offline - remove locally and queue for sync
            await RemoveFromLocalCacheAsync(id);
            await _syncService.QueueOperationAsync(SyncOperation.Delete, id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting blueprint {BlueprintId}", id);

            // Try to remove locally and queue
            try
            {
                await RemoveFromLocalCacheAsync(id);
                await _syncService.QueueOperationAsync(SyncOperation.Delete, id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsServerAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, cts.Token);

            var response = await _httpClient.GetAsync(
                "/health",
                linkedCts.Token);

            var isHealthy = response.IsSuccessStatusCode;
            await _localStorage.SetItemAsync(SERVER_HEALTH_KEY, isHealthy, cancellationToken);
            return isHealthy;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Server health check failed");
            await _localStorage.SetItemAsync(SERVER_HEALTH_KEY, false, cancellationToken);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<MigrationResult> MigrateLocalBlueprintsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new MigrationResult();

        if (!await IsServerAvailableAsync(cancellationToken))
        {
            _logger.LogWarning("Cannot migrate blueprints - server unavailable");
            return result;
        }

        var localBlueprints = await GetBlueprintsFromCacheAsync();

        foreach (var blueprint in localBlueprints)
        {
            try
            {
                var saveResult = await SaveToServerAsync(blueprint, cancellationToken);
                if (saveResult.Success)
                {
                    result.MigratedCount++;
                }
                else
                {
                    result.FailedCount++;
                    result.FailedIds.Add(blueprint.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate blueprint {BlueprintId}", blueprint.Id);
                result.FailedCount++;
                result.FailedIds.Add(blueprint.Id);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task ClearLocalCacheAsync(CancellationToken cancellationToken = default)
    {
        await _localStorage.RemoveItemAsync(BLUEPRINTS_CACHE_KEY, cancellationToken);
        await _localStorage.RemoveItemAsync(BLUEPRINTS_CACHE_TIMESTAMP_KEY, cancellationToken);
    }

    private async Task<List<Blueprint.Models.Blueprint>?> GetBlueprintsFromServerAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/blueprints", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch blueprints from server: {StatusCode}",
                    response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<Blueprint.Models.Blueprint>>(
                JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching blueprints from server");
            return null;
        }
    }

    private async Task<List<Blueprint.Models.Blueprint>> GetBlueprintsFromCacheAsync()
    {
        try
        {
            var json = await _localStorage.GetItemAsStringAsync(BLUEPRINTS_CACHE_KEY);
            if (string.IsNullOrEmpty(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<Blueprint.Models.Blueprint>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading blueprints from cache");
            return [];
        }
    }

    private async Task UpdateLocalCacheAsync(IEnumerable<Blueprint.Models.Blueprint> blueprints)
    {
        var json = JsonSerializer.Serialize(blueprints.ToList(), JsonOptions);
        await _localStorage.SetItemAsStringAsync(BLUEPRINTS_CACHE_KEY, json);
        await _localStorage.SetItemAsync(BLUEPRINTS_CACHE_TIMESTAMP_KEY, DateTimeOffset.UtcNow);
    }

    private async Task UpdateBlueprintInCacheAsync(Blueprint.Models.Blueprint blueprint)
    {
        var blueprints = await GetBlueprintsFromCacheAsync();
        var existing = blueprints.FirstOrDefault(b => b.Id == blueprint.Id);

        if (existing != null)
        {
            blueprints.Remove(existing);
        }

        blueprints.Add(blueprint);
        await UpdateLocalCacheAsync(blueprints);
    }

    private async Task SaveToLocalStorageAsync(Blueprint.Models.Blueprint blueprint)
    {
        await UpdateBlueprintInCacheAsync(blueprint);
    }

    private async Task RemoveFromLocalCacheAsync(string id)
    {
        var blueprints = await GetBlueprintsFromCacheAsync();
        var existing = blueprints.FirstOrDefault(b => b.Id == id);

        if (existing != null)
        {
            blueprints.Remove(existing);
            await UpdateLocalCacheAsync(blueprints);
        }
    }

    private async Task<BlueprintSaveResult> SaveToServerAsync(
        Blueprint.Models.Blueprint blueprint,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpResponseMessage response;

            // Check if blueprint exists on server
            var checkResponse = await _httpClient.GetAsync(
                $"/api/blueprints/{Uri.EscapeDataString(blueprint.Id)}",
                cancellationToken);

            if (checkResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Create new blueprint
                response = await _httpClient.PostAsJsonAsync(
                    "/api/blueprints",
                    blueprint,
                    JsonOptions,
                    cancellationToken);
            }
            else
            {
                // Update existing blueprint
                response = await _httpClient.PutAsJsonAsync(
                    $"/api/blueprints/{Uri.EscapeDataString(blueprint.Id)}",
                    blueprint,
                    JsonOptions,
                    cancellationToken);
            }

            if (response.IsSuccessStatusCode)
            {
                return BlueprintSaveResult.ServerSuccess(blueprint.Id);
            }

            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to save blueprint to server: {StatusCode} - {Error}",
                response.StatusCode, error);

            return BlueprintSaveResult.Failure($"Server error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving blueprint to server");
            return BlueprintSaveResult.Failure(ex.Message);
        }
    }

    private void OnBlueprintSaved(string blueprintId, bool savedToServer)
    {
        BlueprintSaved?.Invoke(this, new BlueprintSavedEventArgs
        {
            BlueprintId = blueprintId,
            SavedToServer = savedToServer
        });
    }
}
