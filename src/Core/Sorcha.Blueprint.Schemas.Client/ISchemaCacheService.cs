// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Service for caching schema documents
/// </summary>
public interface ISchemaCacheService
{
    /// <summary>
    /// Get a cached schema by ID
    /// </summary>
    Task<SchemaDocument?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all cached schemas from a specific source
    /// </summary>
    Task<IEnumerable<SchemaDocument>> GetBySourceAsync(SchemaSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all cached schemas
    /// </summary>
    Task<IEnumerable<SchemaDocument>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache a schema document
    /// </summary>
    Task SetAsync(SchemaDocument schema, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache multiple schema documents
    /// </summary>
    Task SetManyAsync(IEnumerable<SchemaDocument> schemas, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove expired cache entries
    /// </summary>
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the entire cache
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cache statistics
    /// </summary>
    Task<SchemaCacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if cache is available
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
