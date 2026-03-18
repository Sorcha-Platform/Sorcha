// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Schemas.DTOs;
using Sorcha.Blueprint.Schemas.Models;

namespace Sorcha.Blueprint.Schemas.Services;

/// <summary>
/// Interface for providers that fetch schemas from external sources (e.g., SchemaStore.org).
/// </summary>
public interface IExternalSchemaProvider
{
    /// <summary>
    /// Gets the name of this external schema provider.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Self-declared provider type — determines how the provider is categorised
    /// in the admin dashboard (Static vs LiveApi).
    /// </summary>
    ProviderType ProviderType => ProviderType.LiveApi;

    /// <summary>
    /// Searches for schemas matching the specified query.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Search results from the external source.</returns>
    Task<ExternalSchemaSearchResponse> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full schema content by its URL or identifier.
    /// </summary>
    /// <param name="schemaUrl">The URL of the schema to fetch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The external schema result with full content, or null if not found.</returns>
    Task<ExternalSchemaResult?> GetSchemaAsync(string schemaUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the catalog of all available schemas from this provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of all available schemas.</returns>
    Task<IEnumerable<ExternalSchemaResult>> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this provider is currently available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the provider is available, false otherwise.</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Default sector tags for schemas from this provider.
    /// Used when individual schemas don't specify their own sectors.
    /// </summary>
    string[] DefaultSectorTags => ["general"];
}
