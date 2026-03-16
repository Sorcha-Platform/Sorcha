// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Represents a cached schema entry with expiration metadata
/// </summary>
public class SchemaCacheEntry
{
    /// <summary>
    /// The cached schema document
    /// </summary>
    [JsonPropertyName("schema")]
    public SchemaDocument Schema { get; set; } = new();

    /// <summary>
    /// When this entry was cached
    /// </summary>
    [JsonPropertyName("cachedAt")]
    public DateTimeOffset CachedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When this cache entry expires
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// ETag or version identifier for cache validation
    /// </summary>
    [JsonPropertyName("etag")]
    public string? ETag { get; set; }

    /// <summary>
    /// Check if this cache entry is still valid
    /// </summary>
    public bool IsValid()
    {
        return DateTimeOffset.UtcNow < ExpiresAt;
    }

    /// <summary>
    /// Check if this entry is expired
    /// </summary>
    public bool IsExpired()
    {
        return !IsValid();
    }
}

/// <summary>
/// Cache for schema documents organized by source
/// </summary>
public class SchemaCache
{
    /// <summary>
    /// Cached schemas by source type
    /// </summary>
    [JsonPropertyName("schemasBySource")]
    public Dictionary<SchemaSource, List<SchemaCacheEntry>> SchemasBySource { get; set; } = new();

    /// <summary>
    /// Index of schemas by ID for fast lookup
    /// </summary>
    [JsonPropertyName("schemasById")]
    public Dictionary<string, SchemaCacheEntry> SchemasById { get; set; } = new();

    /// <summary>
    /// When the cache was last updated
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Version of the cache format
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Add or update a schema in the cache
    /// </summary>
    public void AddOrUpdate(SchemaDocument schema, TimeSpan cacheDuration)
    {
        var entry = new SchemaCacheEntry
        {
            Schema = schema,
            CachedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(cacheDuration),
            ETag = schema.Metadata.Version
        };

        // Add to source-based index
        if (!SchemasBySource.ContainsKey(schema.Metadata.Source))
        {
            SchemasBySource[schema.Metadata.Source] = [];
        }

        var sourceList = SchemasBySource[schema.Metadata.Source];
        var existingIndex = sourceList.FindIndex(e => e.Schema.Metadata.Id == schema.Metadata.Id);
        if (existingIndex >= 0)
        {
            sourceList[existingIndex] = entry;
        }
        else
        {
            sourceList.Add(entry);
        }

        // Add to ID-based index
        SchemasById[schema.Metadata.Id] = entry;

        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get a schema by ID if it exists and is valid
    /// </summary>
    public SchemaDocument? GetById(string id)
    {
        if (SchemasById.TryGetValue(id, out var entry) && entry.IsValid())
        {
            return entry.Schema;
        }
        return null;
    }

    /// <summary>
    /// Get all valid schemas from a source
    /// </summary>
    public IEnumerable<SchemaDocument> GetBySource(SchemaSource source)
    {
        if (SchemasBySource.TryGetValue(source, out var entries))
        {
            return entries
                .Where(e => e.IsValid())
                .Select(e => e.Schema);
        }
        return [];
    }

    /// <summary>
    /// Get all valid schemas across all sources
    /// </summary>
    public IEnumerable<SchemaDocument> GetAll()
    {
        return SchemasById.Values
            .Where(e => e.IsValid())
            .Select(e => e.Schema);
    }

    /// <summary>
    /// Remove expired entries
    /// </summary>
    public int PurgeExpired()
    {
        var removedCount = 0;

        // Remove from source-based index
        foreach (var source in SchemasBySource.Keys.ToList())
        {
            var list = SchemasBySource[source];
            var expiredCount = list.RemoveAll(e => e.IsExpired());
            removedCount += expiredCount;

            if (list.Count == 0)
            {
                SchemasBySource.Remove(source);
            }
        }

        // Remove from ID-based index
        var expiredIds = SchemasById
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in expiredIds)
        {
            SchemasById.Remove(id);
        }

        if (removedCount > 0)
        {
            LastUpdated = DateTimeOffset.UtcNow;
        }

        return removedCount;
    }

    /// <summary>
    /// Clear all cached schemas
    /// </summary>
    public void Clear()
    {
        SchemasBySource.Clear();
        SchemasById.Clear();
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get cache statistics
    /// </summary>
    public SchemaCacheStatistics GetStatistics()
    {
        var allEntries = SchemasById.Values.ToList();
        var validEntries = allEntries.Where(e => e.IsValid()).ToList();

        return new SchemaCacheStatistics
        {
            TotalEntries = allEntries.Count,
            ValidEntries = validEntries.Count,
            ExpiredEntries = allEntries.Count - validEntries.Count,
            LastUpdated = LastUpdated,
            CacheVersion = Version,
            EntriesBySource = SchemasBySource.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.Count(e => e.IsValid())
            )
        };
    }
}

/// <summary>
/// Statistics about the schema cache
/// </summary>
public class SchemaCacheStatistics
{
    public int TotalEntries { get; set; }
    public int ValidEntries { get; set; }
    public int ExpiredEntries { get; set; }
    public DateTimeOffset LastUpdated { get; set; }
    public int CacheVersion { get; set; }
    public Dictionary<SchemaSource, int> EntriesBySource { get; set; } = new();
}
