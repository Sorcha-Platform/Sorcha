// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.UI.Core.Models.Templates;

/// <summary>
/// View model for template list items.
/// </summary>
public record TemplateListItemViewModel
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("usageCount")]
    public int UsageCount { get; init; }

    [JsonPropertyName("parameters")]
    public List<TemplateParameter> Parameters { get; init; } = [];
}

/// <summary>
/// A configurable parameter for a template.
/// </summary>
public record TemplateParameter
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Type { get; init; } = "string";
    public string? DefaultValue { get; init; }
    public bool Required { get; init; }
}
