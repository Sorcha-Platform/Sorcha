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
    /// Optional section heading rendered above the grouped fields. When
    /// omitted the section acts as an invisible wrapper that only groups
    /// fields — useful in wizard pages where the page title already
    /// labels the grouping and a redundant section heading would just
    /// add visual noise.
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

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
