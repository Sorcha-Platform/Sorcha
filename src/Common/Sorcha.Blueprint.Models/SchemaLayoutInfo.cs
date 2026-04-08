// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Parsed result from <see cref="SchemaLayoutParser.Parse(System.Text.Json.JsonElement)"/>.
/// Carries the complete layout configuration for rendering an action's form,
/// including wizard pages, standalone sections, introduction text, and per-field
/// width hints.
/// </summary>
/// <remarks>
/// All collection properties are nullable to distinguish "extension absent" from
/// "extension present but empty". Consumers should check <see cref="HasWizard"/>
/// and <see cref="HasSections"/> for rendering-mode decisions rather than
/// inspecting <see cref="Pages"/> or <see cref="Sections"/> directly.
/// </remarks>
public sealed record SchemaLayoutInfo
{
    /// <summary>
    /// An empty layout with no pages, sections, introduction, or field widths.
    /// Returned when the source schema has no layout extensions defined or when
    /// parsing fails.
    /// </summary>
    public static readonly SchemaLayoutInfo Empty = new();

    /// <summary>
    /// Wizard pages parsed from <c>x-pages</c>. Null or empty when the schema
    /// does not define a wizard.
    /// </summary>
    public List<BlueprintPageDefinition>? Pages { get; init; }

    /// <summary>
    /// Standalone sections parsed from <c>x-sections</c> on the schema root.
    /// Null or empty when sections are defined only inside wizard pages or
    /// not at all.
    /// </summary>
    public List<BlueprintSectionDefinition>? Sections { get; init; }

    /// <summary>
    /// Introduction text parsed from <c>x-introduction</c>. Shown as a callout
    /// above the form; null when not defined.
    /// </summary>
    public string? Introduction { get; init; }

    /// <summary>
    /// Per-property width hints parsed from <c>x-width</c> on individual schema
    /// properties. Keys are property names; values are one of <c>full</c>,
    /// <c>half</c>, or <c>third</c>.
    /// </summary>
    public Dictionary<string, string>? FieldWidths { get; init; }

    /// <summary>
    /// True when the schema defines one or more wizard pages.
    /// </summary>
    public bool HasWizard => Pages is { Count: > 0 };

    /// <summary>
    /// True when the schema defines any sections — either standalone on the
    /// root or nested inside a wizard page.
    /// </summary>
    public bool HasSections =>
        Sections is { Count: > 0 } ||
        (Pages is not null && Pages.Any(p => p.Sections is { Count: > 0 }));
}
