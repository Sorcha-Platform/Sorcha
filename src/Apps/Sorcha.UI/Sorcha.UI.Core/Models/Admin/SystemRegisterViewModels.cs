// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

/// <summary>
/// View model for the system register status.
/// </summary>
public record SystemRegisterViewModel
{
    /// <summary>Register identifier.</summary>
    [JsonPropertyName("registerId")]
    public string RegisterId { get; init; } = string.Empty;

    /// <summary>Human-readable display name.</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Whether the register has been initialized.</summary>
    [JsonPropertyName("isInitialized")]
    public bool IsInitialized { get; init; }

    /// <summary>Number of blueprints published to the register.</summary>
    [JsonPropertyName("blueprintCount")]
    public int BlueprintCount { get; init; }

    /// <summary>When the register was created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>
/// Summary view model for a blueprint in the system register.
/// </summary>
public record BlueprintSummaryViewModel
{
    /// <summary>Blueprint identifier.</summary>
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Blueprint version number.</summary>
    [JsonPropertyName("version")]
    public long Version { get; init; }

    /// <summary>When this version was published.</summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Who published this version.</summary>
    [JsonPropertyName("publishedBy")]
    public string PublishedBy { get; init; } = string.Empty;

    /// <summary>Whether this version is currently active.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }
}

/// <summary>
/// Detailed view model for a blueprint including participant and action counts.
/// </summary>
public record BlueprintDetailViewModel
{
    /// <summary>Blueprint identifier.</summary>
    [JsonPropertyName("blueprintId")]
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Blueprint title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Blueprint version number.</summary>
    [JsonPropertyName("version")]
    public long Version { get; init; }

    /// <summary>When this version was published.</summary>
    [JsonPropertyName("publishedAt")]
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Who published this version.</summary>
    [JsonPropertyName("publishedBy")]
    public string PublishedBy { get; init; } = string.Empty;

    /// <summary>Whether this version is currently active.</summary>
    [JsonPropertyName("isActive")]
    public bool IsActive { get; init; }

    /// <summary>Number of participants in the blueprint.</summary>
    [JsonPropertyName("participantCount")]
    public int ParticipantCount { get; init; }

    /// <summary>Number of actions in the blueprint.</summary>
    [JsonPropertyName("actionCount")]
    public int ActionCount { get; init; }

    /// <summary>Blueprint description.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>List of available version numbers for this blueprint.</summary>
    [JsonPropertyName("availableVersions")]
    public List<long> AvailableVersions { get; init; } = [];
}

/// <summary>
/// Paginated result of blueprint summaries.
/// </summary>
public record BlueprintPageResult
{
    /// <summary>Blueprint items for the current page.</summary>
    [JsonPropertyName("items")]
    public List<BlueprintSummaryViewModel> Items { get; init; } = [];

    /// <summary>Total number of blueprints across all pages.</summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>Current page number (1-based).</summary>
    [JsonPropertyName("page")]
    public int Page { get; init; } = 1;

    /// <summary>Number of items per page.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; } = 20;
}
