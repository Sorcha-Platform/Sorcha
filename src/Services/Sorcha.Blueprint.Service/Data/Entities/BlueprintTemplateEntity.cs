// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Represents a reusable blueprint template that can be seeded or user-created,
/// with versioning and usage tracking.
/// </summary>
public class BlueprintTemplateEntity
{
    /// <summary>
    /// Unique identifier for the template.
    /// </summary>
    [Required]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Display name of the template.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Optional description of the template.
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional category for organizing templates.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Template content stored as JSONB.
    /// </summary>
    [Required]
    public string Content { get; set; } = default!;

    /// <summary>
    /// Template version number.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Indicates whether the template was seeded or user-created.
    /// </summary>
    public TemplateSource Source { get; set; }

    /// <summary>
    /// Whether the template is published and visible to users.
    /// </summary>
    public bool Published { get; set; } = true;

    /// <summary>
    /// Number of times this template has been used.
    /// </summary>
    public int UsageCount { get; set; } = 0;

    /// <summary>
    /// Timestamp when the template was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the template was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
