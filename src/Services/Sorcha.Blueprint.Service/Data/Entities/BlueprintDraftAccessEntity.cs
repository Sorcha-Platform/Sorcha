// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Schema-only placeholder for draft access control. Defines which users
/// have read or write access to a specific blueprint draft.
/// </summary>
public class BlueprintDraftAccessEntity
{
    /// <summary>
    /// Unique identifier for the access entry.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Foreign key to the associated blueprint draft.
    /// </summary>
    [Required]
    public string DraftId { get; set; } = default!;

    /// <summary>
    /// The user granted access.
    /// </summary>
    [Required]
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Access level granted: "Read" or "Write".
    /// </summary>
    [Required]
    public string AccessLevel { get; set; } = default!;

    /// <summary>
    /// Timestamp when access was granted.
    /// </summary>
    public DateTimeOffset GrantedAt { get; set; }

    /// <summary>
    /// User who granted the access.
    /// </summary>
    [Required]
    public string GrantedBy { get; set; } = default!;

    /// <summary>
    /// Navigation property to the parent draft.
    /// </summary>
    public BlueprintDraftEntity? Draft { get; set; }
}
