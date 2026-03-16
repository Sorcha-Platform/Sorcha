// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Schemas.Client;

/// <summary>
/// Interface for schema repositories that provide JSON Schema documents
/// </summary>
public interface ISchemaRepository
{
    /// <summary>
    /// Source type for this repository
    /// </summary>
    SchemaSource SourceType { get; }

    /// <summary>
    /// Get all schemas from this repository
    /// </summary>
    Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a schema by its ID
    /// </summary>
    Task<SchemaDocument?> GetSchemaByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search schemas by query
    /// </summary>
    Task<IEnumerable<SchemaDocument>> SearchSchemasAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get schemas by category
    /// </summary>
    Task<IEnumerable<SchemaDocument>> GetSchemasByCategoryAsync(string category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh/reload the repository (e.g., fetch latest from remote)
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
