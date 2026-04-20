// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Represents a single wizard page parsed from the <c>x-pages</c> array on an
/// action's JSON Schema root. Used by the UI to render a multi-step form with
/// per-page validation and navigation.
/// </summary>
public sealed record BlueprintPageDefinition
{
    /// <summary>
    /// Wizard step label displayed in the stepper and used in Next-button captions.
    /// Required; expected to be 1-200 characters.
    /// </summary>
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    /// <summary>
    /// Optional page overview shown in the help panel when no field is focused.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Page layout mode. Accepts <c>"single-column"</c> (default) or <c>"two-column"</c>.
    /// </summary>
    [JsonPropertyName("layout")]
    public string? Layout { get; init; } = "single-column";

    /// <summary>
    /// Optional sections that group fields within this page. When null or empty,
    /// the page renders its fields flat.
    /// </summary>
    [JsonPropertyName("x-sections")]
    public List<BlueprintSectionDefinition>? Sections { get; init; }

    /// <summary>
    /// Parsed <c>x-review</c> extension when present. A page carrying a
    /// review extension is rendered read-only via
    /// <c>ReviewSummaryRenderer</c>; field rendering is suppressed. Feature
    /// 107 Assured Identity v1. Null when the page is a normal form page.
    /// </summary>
    /// <remarks>
    /// Not exported to JSON — consumed by the renderer only. The parsed
    /// extension is populated by <see cref="SchemaLayoutParser"/>.
    /// </remarks>
    [JsonIgnore]
    public XReviewExtension? ReviewExtension { get; init; }
}
