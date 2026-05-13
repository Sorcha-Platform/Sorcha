// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Designer;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of offline sync queue management.
/// </summary>
public class OfflineSyncService : IOfflineSyncService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorage;
    private readonly ILogger<OfflineSyncService> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private const string SYNC_QUEUE_KEY = "sorcha:sync:queue";
    private const int AUTO_SYNC_INTERVAL_MS = 30000; // 30 seconds

    private CancellationTokenSource? _autoSyncCts;
    private Task? _autoSyncTask;
    private List<SyncQueueItem> _cachedQueue = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int PendingCount => _cachedQueue.Count;
    public bool HasPendingChanges => _cachedQueue.Count > 0;

    public event Action<int>? OnQueueChanged;
    public event EventHandler<SyncCompletedEventArgs>? OnSyncCompleted;
    public event EventHandler<ConflictDetectedEventArgs>? OnConflictDetected;

    public OfflineSyncService(
        HttpClient httpClient,
        ILocalStorageService localStorage,
        ILogger<OfflineSyncService> logger)
    {
        _httpClient = httpClient;
        _localStorage = localStorage;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task QueueOperationAsync(
        SyncOperation operation,
        string blueprintId,
        string? blueprintJson = null)
    {
        await _syncLock.WaitAsync();
        try
        {
            await LoadQueueFromStorageAsync();

            // Check for existing operation on same blueprint
            var existing = _cachedQueue.FirstOrDefault(q => q.BlueprintId == blueprintId);

            if (existing != null)
            {
                // Coalesce operations
                if (operation == SyncOperation.Delete)
                {
                    // Delete supersedes create/update
                    if (existing.Operation == SyncOperation.Create)
                    {
                        // Created then deleted - remove from queue entirely
                        _cachedQueue.Remove(existing);
                        await SaveQueueToStorageAsync();
                        OnQueueChanged?.Invoke(PendingCount);
                        return;
                    }

                    existing.Operation = SyncOperation.Delete;
                    existing.BlueprintJson = null;
                }
                else
                {
                    // Update the existing entry
                    existing.BlueprintJson = blueprintJson;
                    existing.UpdatedAt = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                // Add new queue item
                var item = new SyncQueueItem
                {
                    Operation = operation,
                    BlueprintId = blueprintId,
                    BlueprintJson = blueprintJson,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                _cachedQueue.Add(item);
            }

            await SaveQueueToStorageAsync();
            OnQueueChanged?.Invoke(PendingCount);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncResult>> SyncNowAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<SyncResult>();

        await _syncLock.WaitAsync(cancellationToken);
        try
        {
            await LoadQueueFromStorageAsync();

            if (_cachedQueue.Count == 0)
            {
                return results;
            }

            // Check server availability
            if (!await IsServerHealthyAsync(cancellationToken))
            {
                _logger.LogWarning("Server unavailable, skipping sync");
                return results;
            }

            // Process each item in the queue
            var itemsToProcess = _cachedQueue.ToList();

            foreach (var item in itemsToProcess)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var result = await ProcessSyncItemAsync(item, cancellationToken);
                results.Add(result);

                if (result.Success)
                {
                    _cachedQueue.Remove(item);
                }
                else if (result.HasConflict && result.Conflict != null)
                {
                    OnConflictDetected?.Invoke(this, new ConflictDetectedEventArgs
                    {
                        Conflict = result.Conflict,
                        QueueItem = item
                    });
                }
                else if (!item.CanRetry)
                {
                    // Max retries exceeded, remove from queue
                    _logger.LogWarning(
                        "Removing sync item {ItemId} after {RetryCount} failed attempts",
                        item.Id, item.RetryCount);
                    _cachedQueue.Remove(item);
                }
            }

            await SaveQueueToStorageAsync();
            OnQueueChanged?.Invoke(PendingCount);

            // Raise completion event
            var completedArgs = new SyncCompletedEventArgs
            {
                SuccessCount = results.Count(r => r.Success),
                FailedCount = results.Count(r => !r.Success && !r.HasConflict),
                ConflictCount = results.Count(r => r.HasConflict),
                Results = results
            };

            OnSyncCompleted?.Invoke(this, completedArgs);

            return results;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncQueueItem>> GetQueuedItemsAsync()
    {
        await LoadQueueFromStorageAsync();
        return _cachedQueue.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task RemoveFromQueueAsync(string itemId)
    {
        await _syncLock.WaitAsync();
        try
        {
            await LoadQueueFromStorageAsync();

            var item = _cachedQueue.FirstOrDefault(q => q.Id == itemId);
            if (item != null)
            {
                _cachedQueue.Remove(item);
                await SaveQueueToStorageAsync();
                OnQueueChanged?.Invoke(PendingCount);
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearQueueAsync()
    {
        await _syncLock.WaitAsync();
        try
        {
            _cachedQueue.Clear();
            await SaveQueueToStorageAsync();
            OnQueueChanged?.Invoke(PendingCount);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc />
    public Task StartAutoSyncAsync()
    {
        if (_autoSyncTask != null)
        {
            return Task.CompletedTask;
        }

        _autoSyncCts = new CancellationTokenSource();
        _autoSyncTask = RunAutoSyncLoopAsync(_autoSyncCts.Token);

        _logger.LogInformation("Auto-sync started with {Interval}ms interval", AUTO_SYNC_INTERVAL_MS);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAutoSyncAsync()
    {
        if (_autoSyncCts == null)
        {
            return;
        }

        _autoSyncCts.Cancel();

        if (_autoSyncTask != null)
        {
            try
            {
                await _autoSyncTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _autoSyncCts.Dispose();
        _autoSyncCts = null;
        _autoSyncTask = null;

        _logger.LogInformation("Auto-sync stopped");
    }

    private async Task RunAutoSyncLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(AUTO_SYNC_INTERVAL_MS, cancellationToken);

                if (HasPendingChanges)
                {
                    _logger.LogDebug("Auto-sync triggered with {Count} pending items", PendingCount);
                    await SyncNowAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in auto-sync loop");
            }
        }
    }

    private async Task<SyncResult> ProcessSyncItemAsync(
        SyncQueueItem item,
        CancellationToken cancellationToken)
    {
        item.RetryCount++;
        item.LastAttemptAt = DateTimeOffset.UtcNow;

        try
        {
            return item.Operation switch
            {
                SyncOperation.Create => await ProcessCreateAsync(item, cancellationToken),
                SyncOperation.Update => await ProcessUpdateAsync(item, cancellationToken),
                SyncOperation.Delete => await ProcessDeleteAsync(item, cancellationToken),
                _ => new SyncResult
                {
                    Item = item,
                    Success = false,
                    Error = $"Unknown operation: {item.Operation}"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing sync item {ItemId}", item.Id);
            item.LastError = ex.Message;

            return new SyncResult
            {
                Item = item,
                Success = false,
                Error = ex.Message
            };
        }
    }

    private async Task<SyncResult> ProcessCreateAsync(
        SyncQueueItem item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.BlueprintJson))
        {
            return new SyncResult
            {
                Item = item,
                Success = false,
                Error = "Blueprint JSON is required for create operation"
            };
        }

        var blueprint = JsonSerializer.Deserialize<Blueprint.Models.Blueprint>(
            item.BlueprintJson, JsonOptions);

        var response = await _httpClient.PostAsJsonAsync(
            "/api/blueprints",
            blueprint,
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new SyncResult
            {
                Item = item,
                Success = true,
                BlueprintTitle = blueprint?.Title
            };
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        item.LastError = error;

        return new SyncResult
        {
            Item = item,
            Success = false,
            Error = $"Server returned {response.StatusCode}: {error}"
        };
    }

    private async Task<SyncResult> ProcessUpdateAsync(
        SyncQueueItem item,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(item.BlueprintJson))
        {
            return new SyncResult
            {
                Item = item,
                Success = false,
                Error = "Blueprint JSON is required for update operation"
            };
        }

        var blueprint = JsonSerializer.Deserialize<Blueprint.Models.Blueprint>(
            item.BlueprintJson, JsonOptions);

        // Check for conflicts by getting server version first
        var serverResponse = await _httpClient.GetAsync(
            $"/api/blueprints/{Uri.EscapeDataString(item.BlueprintId)}",
            cancellationToken);

        if (serverResponse.IsSuccessStatusCode)
        {
            var serverBlueprint = await serverResponse.Content.ReadFromJsonAsync<Blueprint.Models.Blueprint>(
                JsonOptions, cancellationToken);

            // Check for version conflict
            if (serverBlueprint != null && blueprint != null &&
                serverBlueprint.Version > blueprint.Version)
            {
                return new SyncResult
                {
                    Item = item,
                    Success = false,
                    HasConflict = true,
                    Conflict = new BlueprintConflict
                    {
                        LocalVersion = blueprint,
                        ServerVersion = serverBlueprint
                    },
                    BlueprintTitle = blueprint.Title
                };
            }
        }
        else if (serverResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Blueprint doesn't exist on server, create it instead
            return await ProcessCreateAsync(item, cancellationToken);
        }

        var response = await _httpClient.PutAsJsonAsync(
            $"/api/blueprints/{Uri.EscapeDataString(item.BlueprintId)}",
            blueprint,
            JsonOptions,
            cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new SyncResult
            {
                Item = item,
                Success = true,
                BlueprintTitle = blueprint?.Title
            };
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        item.LastError = error;

        return new SyncResult
        {
            Item = item,
            Success = false,
            Error = $"Server returned {response.StatusCode}: {error}"
        };
    }

    private async Task<SyncResult> ProcessDeleteAsync(
        SyncQueueItem item,
        CancellationToken cancellationToken)
    {
        var response = await _httpClient.DeleteAsync(
            $"/api/blueprints/{Uri.EscapeDataString(item.BlueprintId)}",
            cancellationToken);

        if (response.IsSuccessStatusCode ||
            response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new SyncResult
            {
                Item = item,
                Success = true
            };
        }

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        item.LastError = error;

        return new SyncResult
        {
            Item = item,
            Success = false,
            Error = $"Server returned {response.StatusCode}: {error}"
        };
    }

    private async Task<bool> IsServerHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, cts.Token);

            var response = await _httpClient.GetAsync("/health", linkedCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadQueueFromStorageAsync()
    {
        try
        {
            var json = await _localStorage.GetItemAsStringAsync(SYNC_QUEUE_KEY);
            if (!string.IsNullOrEmpty(json))
            {
                _cachedQueue = JsonSerializer.Deserialize<List<SyncQueueItem>>(json, JsonOptions) ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading sync queue from storage");
            _cachedQueue = [];
        }
    }

    private async Task SaveQueueToStorageAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(_cachedQueue, JsonOptions);
            await _localStorage.SetItemAsStringAsync(SYNC_QUEUE_KEY, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving sync queue to storage");
        }
    }

    public void Dispose()
    {
        _autoSyncCts?.Cancel();
        _autoSyncCts?.Dispose();
        _syncLock.Dispose();
    }
}
