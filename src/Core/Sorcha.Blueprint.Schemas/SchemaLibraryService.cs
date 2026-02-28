// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Sorcha.Blueprint.Schemas;

/// <summary>
/// Unified service for searching and accessing JSON Schemas from multiple sources
/// </summary>
public class SchemaLibraryService
{
    private readonly List<ISchemaRepository> _repositories = [];
    private readonly Dictionary<string, SchemaDocument> _favoriteSchemas = [];
    private readonly ISchemaCacheService? _cacheService;

    public SchemaLibraryService(ISchemaCacheService? cacheService = null, ILoggerFactory? loggerFactory = null)
    {
        _cacheService = cacheService;

        // Initialize with built-in repository by default
        var builtInLogger = loggerFactory?.CreateLogger<BuiltInSchemaRepository>()
            ?? NullLogger<BuiltInSchemaRepository>.Instance;
        AddRepository(new BuiltInSchemaRepository(builtInLogger));
    }

    /// <summary>
    /// Add a schema repository to the library
    /// </summary>
    public void AddRepository(ISchemaRepository repository)
    {
        if (!_repositories.Contains(repository))
        {
            _repositories.Add(repository);
        }
    }

    /// <summary>
    /// Remove a repository from the library
    /// </summary>
    public void RemoveRepository(ISchemaRepository repository)
    {
        _repositories.Remove(repository);
    }

    /// <summary>
    /// Get all schemas from all repositories (with caching)
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync(CancellationToken cancellationToken = default)
    {
        // Try cache first
        if (_cacheService != null)
        {
            var cachedSchemas = await _cacheService.GetAllAsync(cancellationToken);
            var cachedList = cachedSchemas.ToList();

            // If cache has data, return it
            if (cachedList.Count > 0)
            {
                return cachedList;
            }
        }

        // Cache miss or unavailable - fetch from repositories
        var allSchemas = new List<SchemaDocument>();

        foreach (var repository in _repositories)
        {
            var schemas = await repository.GetAllSchemasAsync(cancellationToken);
            allSchemas.AddRange(schemas);
        }

        // Update cache
        if (_cacheService != null && allSchemas.Count > 0)
        {
            await _cacheService.SetManyAsync(allSchemas, cancellationToken: cancellationToken);
        }

        return allSchemas;
    }

    /// <summary>
    /// Search for schemas across all repositories
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var results = new List<SchemaDocument>();

        foreach (var repository in _repositories)
        {
            var schemas = await repository.SearchSchemasAsync(query, cancellationToken);
            results.AddRange(schemas);
        }

        // Remove duplicates based on schema ID
        return results.DistinctBy(s => s.Metadata.Id);
    }

    /// <summary>
    /// Get schemas by category across all repositories
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        var results = new List<SchemaDocument>();

        foreach (var repository in _repositories)
        {
            var schemas = await repository.GetSchemasByCategoryAsync(category, cancellationToken);
            results.AddRange(schemas);
        }

        return results.DistinctBy(s => s.Metadata.Id);
    }

    /// <summary>
    /// Get a schema by ID from any repository
    /// </summary>
    public async Task<SchemaDocument?> GetSchemaByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        foreach (var repository in _repositories)
        {
            var schema = await repository.GetSchemaByIdAsync(id, cancellationToken);
            if (schema != null)
            {
                return schema;
            }
        }

        return null;
    }

    /// <summary>
    /// Get schemas filtered by source type
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> GetBySourceAsync(SchemaSource source, CancellationToken cancellationToken = default)
    {
        var results = new List<SchemaDocument>();

        var matchingRepositories = _repositories.Where(r => r.SourceType == source);
        foreach (var repository in matchingRepositories)
        {
            var schemas = await repository.GetAllSchemasAsync(cancellationToken);
            results.AddRange(schemas);
        }

        return results;
    }

    /// <summary>
    /// Get all unique categories across all repositories
    /// </summary>
    public async Task<IEnumerable<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var allSchemas = await GetAllSchemasAsync(cancellationToken);
        return allSchemas
            .Select(s => s.Metadata.Category)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .OrderBy(c => c);
    }

    /// <summary>
    /// Get favorite schemas
    /// </summary>
    public IEnumerable<SchemaDocument> GetFavorites()
    {
        return _favoriteSchemas.Values;
    }

    /// <summary>
    /// Add a schema to favorites
    /// </summary>
    public void AddToFavorites(SchemaDocument schema)
    {
        if (!_favoriteSchemas.ContainsKey(schema.Metadata.Id))
        {
            schema.Metadata.IsFavorite = true;
            _favoriteSchemas[schema.Metadata.Id] = schema;
        }
    }

    /// <summary>
    /// Remove a schema from favorites
    /// </summary>
    public void RemoveFromFavorites(string schemaId)
    {
        if (_favoriteSchemas.TryGetValue(schemaId, out var schema))
        {
            schema.Metadata.IsFavorite = false;
            _favoriteSchemas.Remove(schemaId);
        }
    }

    /// <summary>
    /// Increment usage count for a schema
    /// </summary>
    public async Task IncrementUsageAsync(string schemaId, CancellationToken cancellationToken = default)
    {
        var schema = await GetSchemaByIdAsync(schemaId, cancellationToken);
        if (schema != null)
        {
            schema.Metadata.UsageCount++;
        }
    }

    /// <summary>
    /// Get most frequently used schemas
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> GetMostUsedAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var allSchemas = await GetAllSchemasAsync(cancellationToken);
        return allSchemas
            .OrderByDescending(s => s.Metadata.UsageCount)
            .Take(count);
    }

    /// <summary>
    /// Get recently added schemas
    /// </summary>
    public async Task<IEnumerable<SchemaDocument>> GetRecentlyAddedAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        var allSchemas = await GetAllSchemasAsync(cancellationToken);
        return allSchemas
            .OrderByDescending(s => s.Metadata.AddedAt)
            .Take(count);
    }

    /// <summary>
    /// Refresh all repositories and update cache
    /// </summary>
    public async Task RefreshAllAsync(CancellationToken cancellationToken = default)
    {
        var refreshTasks = _repositories.Select(r => r.RefreshAsync(cancellationToken));
        await Task.WhenAll(refreshTasks);

        // Clear cache to force reload
        if (_cacheService != null)
        {
            await _cacheService.ClearAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Clear the schema cache
    /// </summary>
    public async Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            await _cacheService.ClearAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public async Task<SchemaCacheStatistics?> GetCacheStatisticsAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            return await _cacheService.GetStatisticsAsync(cancellationToken);
        }
        return null;
    }

    /// <summary>
    /// Purge expired cache entries
    /// </summary>
    public async Task<int> PurgeCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheService != null)
        {
            return await _cacheService.PurgeExpiredAsync(cancellationToken);
        }
        return 0;
    }

    /// <summary>
    /// Get repository statistics
    /// </summary>
    public async Task<SchemaLibraryStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var allSchemas = await GetAllSchemasAsync(cancellationToken);
        var schemasList = allSchemas.ToList();

        return new SchemaLibraryStatistics
        {
            TotalSchemas = schemasList.Count,
            BuiltInSchemas = schemasList.Count(s => s.Metadata.Source == SchemaSource.BuiltIn),
            LocalSchemas = schemasList.Count(s => s.Metadata.Source == SchemaSource.Local),
            SchemaStoreSchemas = schemasList.Count(s => s.Metadata.Source == SchemaSource.SchemaStore),
            BlueprintServiceSchemas = schemasList.Count(s => s.Metadata.Source == SchemaSource.BlueprintService),
            ExternalSchemas = schemasList.Count(s => s.Metadata.Source == SchemaSource.External),
            FavoriteSchemas = _favoriteSchemas.Count,
            Categories = await GetCategoriesAsync(cancellationToken)
        };
    }
}

/// <summary>
/// Statistics about the schema library
/// </summary>
public class SchemaLibraryStatistics
{
    public int TotalSchemas { get; set; }
    public int BuiltInSchemas { get; set; }
    public int LocalSchemas { get; set; }
    public int SchemaStoreSchemas { get; set; }
    public int BlueprintServiceSchemas { get; set; }
    public int ExternalSchemas { get; set; }
    public int FavoriteSchemas { get; set; }
    public IEnumerable<string> Categories { get; set; } = [];
}
