// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Admin;

public record SystemRegisterViewModel
{
    [JsonPropertyName("registerId")] public string RegisterId { get; init; } = string.Empty;
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = string.Empty;
    [JsonPropertyName("isInitialized")] public bool IsInitialized { get; init; }
    [JsonPropertyName("blueprintCount")] public int BlueprintCount { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset CreatedAt { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
}

public record BlueprintSummaryViewModel
{
    [JsonPropertyName("blueprintId")] public string BlueprintId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public long Version { get; init; }
    [JsonPropertyName("publishedAt")] public DateTime PublishedAt { get; init; }
    [JsonPropertyName("publishedBy")] public string PublishedBy { get; init; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }
}

public record BlueprintDetailViewModel
{
    [JsonPropertyName("blueprintId")] public string BlueprintId { get; init; } = string.Empty;
    [JsonPropertyName("version")] public long Version { get; init; }
    [JsonPropertyName("document")] public string Document { get; init; } = string.Empty;
    [JsonPropertyName("publishedAt")] public DateTime PublishedAt { get; init; }
    [JsonPropertyName("publishedBy")] public string PublishedBy { get; init; } = string.Empty;
    [JsonPropertyName("isActive")] public bool IsActive { get; init; }
}

public record BlueprintPageResult
{
    [JsonPropertyName("items")] public List<BlueprintSummaryViewModel> Items { get; init; } = [];
    [JsonPropertyName("page")] public int Page { get; init; }
    [JsonPropertyName("pageSize")] public int PageSize { get; init; }
    [JsonPropertyName("totalCount")] public int TotalCount { get; init; }
    [JsonPropertyName("totalPages")] public int TotalPages { get; init; }
}
