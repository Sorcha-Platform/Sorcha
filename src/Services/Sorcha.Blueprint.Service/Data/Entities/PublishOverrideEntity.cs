// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Feature 142 (D4/FR-032) — audit record written when an authorised user
/// publishes a blueprint version despite no matching <see cref="RehearsalPassEntity"/>
/// (the rehearsal soft gate was overridden).
/// </summary>
/// <remarks>
/// Lifecycle: append-only audit; never mutated, never deleted.
/// </remarks>
public class PublishOverrideEntity
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// The draft/service identity of the published blueprint.
    /// </summary>
    [Required]
    public string BlueprintId { get; set; } = default!;

    /// <summary>
    /// The published version number.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Identifier of the target live register.
    /// </summary>
    [Required]
    public string RegisterId { get; set; } = default!;

    /// <summary>
    /// Canonical hash of the executable definition (D7) of the published version.
    /// </summary>
    [Required]
    public string ExecDefHash { get; set; } = default!;

    /// <summary>
    /// Identifier of the platform user who performed the override. Must hold
    /// register publish-governance authority.
    /// </summary>
    public Guid OverriddenByPlatformUserId { get; set; }

    /// <summary>
    /// UTC time the override was recorded.
    /// </summary>
    public DateTimeOffset OverriddenAt { get; set; }

    /// <summary>
    /// Optional free-text reason supplied by the overriding user.
    /// </summary>
    public string? Reason { get; set; }
}
