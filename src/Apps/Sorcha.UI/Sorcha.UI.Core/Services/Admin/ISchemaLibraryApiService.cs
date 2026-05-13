// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.UI.Core.Models.SchemaLibrary;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// HTTP client for the Schema Library API — index search, detail, content, sectors, and provider health.
/// </summary>
public interface ISchemaLibraryApiService
{
    /// <summary>
    /// Searches the schema index with optional filters.
    /// </summary>
    Task<SchemaIndexSearchResponse> SearchAsync(
        string? search = null,
        string[]? sectors = null,
        string? provider = null,
        int limit = 25,
        string? cursor = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single schema index entry with full content by short code.
    /// </summary>
    Task<SchemaIndexEntryDetailViewModel?> GetDetailAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the raw JSON Schema content by short code.
    /// </summary>
    Task<JsonDocument?> GetContentAsync(
        string shortCode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all available sectors.
    /// </summary>
    Task<IReadOnlyList<SchemaSectorViewModel>> GetSectorsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets organisation sector preferences.
    /// </summary>
    Task<OrganisationSectorPreferencesViewModel?> GetSectorPreferencesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates organisation sector preferences (admin only).
    /// </summary>
    Task<bool> UpdateSectorPreferencesAsync(
        string[]? enabledSectors,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all provider statuses (admin only).
    /// </summary>
    Task<IReadOnlyList<SchemaProviderStatusViewModel>> GetProviderStatusesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a manual provider refresh (admin only).
    /// </summary>
    Task<bool> RefreshProviderAsync(
        string providerName,
        CancellationToken cancellationToken = default);
}
