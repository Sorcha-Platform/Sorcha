// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Represents a visual grouping of form fields parsed from <c>x-sections</c>.
/// Sections may appear inside a <see cref="BlueprintPageDefinition"/> (wizard page)
/// or directly on the action schema root (standalone sections without wizard).
/// </summary>
public sealed record BlueprintSectionDefinition
{
    /// <summary>
    /// Section heading rendered above the grouped fields. Required; 1-200 characters.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Optional subtitle shown under the section heading.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Optional help text displayed in the contextual help panel when this
    /// section is active and no individual field is focused.
    /// </summary>
    [JsonPropertyName("help")]
    public string? Help { get; init; }

    /// <summary>
    /// Layout mode for fields within the section. Accepts <c>"vertical"</c> (default),
    /// <c>"horizontal"</c>, or <c>"grid"</c>.
    /// </summary>
    [JsonPropertyName("layout")]
    public string? Layout { get; init; } = "vertical";

    /// <summary>
    /// Property names from the parent schema's <c>properties</c> object that
    /// belong in this section, in render order. Required; at least one field.
    /// </summary>
    [JsonPropertyName("fields")]
    public required List<string> Fields { get; init; }
}
