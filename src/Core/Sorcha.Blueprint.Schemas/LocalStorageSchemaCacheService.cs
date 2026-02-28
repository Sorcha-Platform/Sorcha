// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Blazored.LocalStorage;

namespace Sorcha.Blueprint.Schemas;

/// <summary>
/// LocalStorage-based implementation of schema caching.
/// Thread-safe: uses SemaphoreSlim to prevent concurrent double-loading.
/// </summary>
public class LocalStorageSchemaCacheService : ISchemaCacheService
{
    private readonly ILocalStorageService _localStorage;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private const string CacheKey = "sorcha:schema-cache";
    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromDays(7); // 7 days default

    private SchemaCache? _memoryCache;
    private bool _isLoaded = false;

    public LocalStorageSchemaCacheService(ILocalStorageService localStorage)
    {
        _localStorage = localStorage ?? throw new ArgumentNullException(nameof(localStorage));
    }

    public async Task<SchemaDocument?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _memoryCache?.GetById(id);
    }

    public async Task<IEnumerable<SchemaDocument>> GetBySourceAsync(SchemaSource source, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _memoryCache?.GetBySource(source) ?? [];
    }

    public async Task<IEnumerable<SchemaDocument>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        return _memoryCache?.GetAll() ?? [];
    }

    public async Task SetAsync(SchemaDocument schema, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (_memoryCache == null)
        {
            _memoryCache = new SchemaCache();
        }

        _memoryCache.AddOrUpdate(schema, cacheDuration ?? DefaultCacheDuration);
        await SaveAsync(cancellationToken);
    }

    public async Task SetManyAsync(IEnumerable<SchemaDocument> schemas, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (_memoryCache == null)
        {
            _memoryCache = new SchemaCache();
        }

        var duration = cacheDuration ?? DefaultCacheDuration;
        foreach (var schema in schemas)
        {
            _memoryCache.AddOrUpdate(schema, duration);
        }

        await SaveAsync(cancellationToken);
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (_memoryCache == null)
        {
            return 0;
        }

        var removedCount = _memoryCache.PurgeExpired();
        if (removedCount > 0)
        {
            await SaveAsync(cancellationToken);
        }

        return removedCount;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _memoryCache = new SchemaCache();
        _isLoaded = true;
        await SaveAsync(cancellationToken);
    }

    public async Task<SchemaCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);

        if (_memoryCache == null)
        {
            return new SchemaCacheStatistics();
        }

        return _memoryCache.GetStatistics();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _localStorage.ContainKeyAsync(CacheKey, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        // Fast path: already loaded
        if (_isLoaded)
        {
            return;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring the lock to prevent double-loading
            if (_isLoaded)
            {
                return;
            }

            try
            {
                var exists = await _localStorage.ContainKeyAsync(CacheKey, cancellationToken);
                if (exists)
                {
                    _memoryCache = await _localStorage.GetItemAsync<SchemaCache>(CacheKey, cancellationToken);
                }

                if (_memoryCache == null)
                {
                    _memoryCache = new SchemaCache();
                }

                // Automatically purge expired entries on load
                _memoryCache.PurgeExpired();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading schema cache: {ex.Message}");
                _memoryCache = new SchemaCache();
            }

            _isLoaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache == null)
        {
            return;
        }

        try
        {
            await _localStorage.SetItemAsync(CacheKey, _memoryCache, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving schema cache: {ex.Message}");
        }
    }
}
