// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Lightweight index entry for search results.
/// </summary>
public sealed record SchemaIndexEntryDto(
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
public sealed record SchemaIndexEntryDetail(
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
    IReadOnlyList<SchemaIndexEntryDto> Results,
    int TotalCount,
    string? NextCursor,
    string[]? LoadingProviders);

/// <summary>
/// Schema sector DTO.
/// </summary>
public sealed record SchemaSectorDto(
    string Id,
    string DisplayName,
    string Description,
    string Icon);

/// <summary>
/// Organisation sector preferences DTO.
/// </summary>
public sealed record OrganisationSectorPreferencesDto(
    string? OrganizationId,
    string[]? EnabledSectors,
    bool AllSectorsEnabled,
    DateTimeOffset? LastModifiedAt);

/// <summary>
/// Request to update sector preferences.
/// </summary>
public sealed record UpdateSectorPreferencesRequest(
    string[]? EnabledSectors);

/// <summary>
/// Schema provider status DTO.
/// </summary>
public sealed record SchemaProviderStatusDto(
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

/// <summary>
/// Request to create a derived schema (field subset).
/// </summary>
public sealed record CreateDerivedSchemaRequest(
    string ParentSourceProvider,
    string ParentSourceUri,
    string[] IncludedFields);

/// <summary>
/// Derived schema DTO.
/// </summary>
public sealed record DerivedSchemaDto(
    string Id,
    string ParentSourceProvider,
    string ParentSourceUri,
    string ParentTitle,
    string[] IncludedFields,
    JsonDocument Content,
    DateTimeOffset CreatedAt);
