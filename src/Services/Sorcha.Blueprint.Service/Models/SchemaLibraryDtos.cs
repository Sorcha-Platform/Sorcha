// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Lightweight index entry for search results.
/// </summary>
/// <param name="ShortCode">The short code.</param>
/// <param name="SourceProvider">The source provider.</param>
/// <param name="SourceUri">The source uri.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Description">Free-text description of the resource.</param>
/// <param name="SectorTags">The sector tags.</param>
/// <param name="FieldCount">Numeric value for field count.</param>
/// <param name="RequiredFieldCount">Numeric value for required field count.</param>
/// <param name="SchemaVersion">The schema version.</param>
/// <param name="Status">Current status of the resource.</param>
/// <param name="LastFetchedAt">Timestamp at which last fetched occurred (UTC).</param>
/// <param name="FieldNames">The field names.</param>
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
/// <param name="ShortCode">The short code.</param>
/// <param name="SourceProvider">The source provider.</param>
/// <param name="SourceUri">The source uri.</param>
/// <param name="Title">Human-readable title.</param>
/// <param name="Description">Free-text description of the resource.</param>
/// <param name="SectorTags">The sector tags.</param>
/// <param name="FieldCount">Numeric value for field count.</param>
/// <param name="RequiredFieldCount">Numeric value for required field count.</param>
/// <param name="SchemaVersion">The schema version.</param>
/// <param name="Status">Current status of the resource.</param>
/// <param name="LastFetchedAt">Timestamp at which last fetched occurred (UTC).</param>
/// <param name="FieldNames">The field names.</param>
/// <param name="RequiredFields">The required fields.</param>
/// <param name="Keywords">The keywords.</param>
/// <param name="Content">The content.</param>
/// <param name="UsageCount">Numeric value for usage count.</param>
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
/// <param name="Results">Collection of result items.</param>
/// <param name="TotalCount">Total number of items available.</param>
/// <param name="NextCursor">The next cursor.</param>
/// <param name="LoadingProviders">The loading providers.</param>
public sealed record SchemaIndexSearchResponse(
    IReadOnlyList<SchemaIndexEntryDto> Results,
    int TotalCount,
    string? NextCursor,
    string[]? LoadingProviders);

/// <summary>
/// Schema sector DTO.
/// </summary>
/// <param name="Id">Unique identifier for the resource.</param>
/// <param name="DisplayName">Human-readable display name.</param>
/// <param name="Description">Free-text description of the resource.</param>
/// <param name="Icon">The icon.</param>
public sealed record SchemaSectorDto(
    string Id,
    string DisplayName,
    string Description,
    string Icon);

/// <summary>
/// Organisation sector preferences DTO.
/// </summary>
/// <param name="OrganizationId">Identifier of the organization that owns this resource.</param>
/// <param name="EnabledSectors">The enabled sectors.</param>
/// <param name="AllSectorsEnabled">Flag indicating all sectors enabled.</param>
/// <param name="LastModifiedAt">Timestamp at which last modified occurred (UTC).</param>
public sealed record OrganisationSectorPreferencesDto(
    string? OrganizationId,
    string[]? EnabledSectors,
    bool AllSectorsEnabled,
    DateTimeOffset? LastModifiedAt);

/// <summary>
/// Request to update sector preferences.
/// </summary>
/// <param name="EnabledSectors">The enabled sectors.</param>
public sealed record UpdateSectorPreferencesRequest(
    string[]? EnabledSectors);

/// <summary>
/// Schema provider status DTO.
/// </summary>
/// <param name="ProviderName">The provider name.</param>
/// <param name="IsEnabled">Indicates whether the feature is enabled.</param>
/// <param name="ProviderType">The provider type.</param>
/// <param name="RateLimitPerSecond">Numeric value for rate limit per second.</param>
/// <param name="RefreshIntervalHours">Numeric value for refresh interval hours.</param>
/// <param name="LastSuccessfulFetch">Timestamp value (UTC) for last successful fetch.</param>
/// <param name="LastError">The last error.</param>
/// <param name="LastErrorAt">Timestamp at which last error occurred (UTC).</param>
/// <param name="SchemaCount">Numeric value for schema count.</param>
/// <param name="HealthStatus">The health status.</param>
/// <param name="BackoffUntil">Timestamp value (UTC) for backoff until.</param>
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
/// <param name="ParentSourceProvider">The parent source provider.</param>
/// <param name="ParentSourceUri">The parent source uri.</param>
/// <param name="IncludedFields">The included fields.</param>
public sealed record CreateDerivedSchemaRequest(
    string ParentSourceProvider,
    string ParentSourceUri,
    string[] IncludedFields);

/// <summary>
/// Derived schema DTO.
/// </summary>
/// <param name="Id">Unique identifier for the resource.</param>
/// <param name="ParentSourceProvider">The parent source provider.</param>
/// <param name="ParentSourceUri">The parent source uri.</param>
/// <param name="ParentTitle">The parent title.</param>
/// <param name="IncludedFields">The included fields.</param>
/// <param name="Content">The content.</param>
/// <param name="CreatedAt">Server timestamp when the record was created (UTC).</param>
public sealed record DerivedSchemaDto(
    string Id,
    string ParentSourceProvider,
    string ParentSourceUri,
    string ParentTitle,
    string[] IncludedFields,
    JsonDocument Content,
    DateTimeOffset CreatedAt);
