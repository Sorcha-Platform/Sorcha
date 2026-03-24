// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Represents a blueprint draft stored in the database, containing the
/// editable blueprint content as JSONB alongside ownership and status metadata.
/// </summary>
public class BlueprintDraftEntity
{
    /// <summary>
    /// Unique identifier for the draft.
    /// </summary>
    [Required]
    public string Id { get; set; } = default!;

    /// <summary>
    /// The user who owns this draft.
    /// </summary>
    [Required]
    public string OwnerId { get; set; } = default!;

    /// <summary>
    /// Display name of the draft.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Optional description of the draft.
    /// </summary>
    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Blueprint content stored as JSONB.
    /// </summary>
    [Required]
    public string Content { get; set; } = default!;

    /// <summary>
    /// Optional organization scope for the draft.
    /// </summary>
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Current lifecycle status of the draft.
    /// </summary>
    public DraftStatus Status { get; set; } = DraftStatus.Draft;

    /// <summary>
    /// Timestamp when the draft was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the draft was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Access control entries for shared draft collaboration.
    /// </summary>
    public ICollection<BlueprintDraftAccessEntity> AccessEntries { get; set; } = new List<BlueprintDraftAccessEntity>();
}
