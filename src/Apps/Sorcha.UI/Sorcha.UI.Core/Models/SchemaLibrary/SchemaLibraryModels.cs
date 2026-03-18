// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.UI.Core.Models.SchemaLibrary;

/// <summary>
/// Lightweight schema index entry for search results.
/// </summary>
public sealed record SchemaIndexEntryViewModel(
    string ShortCode,
    string SourceProvider,
    string SourceUri,
    string Title,
    string? Description,
    string[] SectorTags,
    int FieldCount,
    int RequiredFieldCount,
    string SchemaVersion,
    string Status,
    DateTimeOffset LastFetchedAt,
    string[]? FieldNames = null);

/// <summary>
/// Full detail of a schema index entry including content.
/// </summary>
public sealed record SchemaIndexEntryDetailViewModel(
    string ShortCode,
    string SourceProvider,
    string SourceUri,
    string Title,
    string? Description,
    string[] SectorTags,
    int FieldCount,
    int RequiredFieldCount,
    string SchemaVersion,
    string Status,
    DateTimeOffset LastFetchedAt,
    string[]? FieldNames,
    string[]? RequiredFields,
    string[]? Keywords,
    JsonDocument? Content,
    int UsageCount = 0);

/// <summary>
/// Schema index search response with pagination.
/// </summary>
public sealed record SchemaIndexSearchResponse(
    IReadOnlyList<SchemaIndexEntryViewModel> Results,
    int TotalCount,
    string? NextCursor,
    string[]? LoadingProviders);

/// <summary>
/// Schema sector definition.
/// </summary>
public sealed record SchemaSectorViewModel(
    string Id,
    string DisplayName,
    string Description,
    string Icon);

/// <summary>
/// Organisation sector preferences.
/// </summary>
public sealed record OrganisationSectorPreferencesViewModel(
    string? OrganizationId,
    string[]? EnabledSectors,
    bool AllSectorsEnabled,
    DateTimeOffset? LastModifiedAt);

/// <summary>
/// Schema provider status for admin dashboard.
/// </summary>
public sealed record SchemaProviderStatusViewModel(
    string ProviderName,
    bool IsEnabled,
    string? ProviderType,
    double RateLimitPerSecond,
    int RefreshIntervalHours,
    DateTimeOffset? LastSuccessfulFetch,
    string? LastError,
    DateTimeOffset? LastErrorAt,
    int SchemaCount,
    string HealthStatus,
    DateTimeOffset? BackoffUntil);
