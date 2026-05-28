// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Feature 142 (D4/FR-032) — records that a full rehearsal succeeded for a
/// specific executable definition of a blueprint. Backs the server-side soft
/// gate at publish: a publish checks for a row matching the publishing version's
/// <see cref="ExecDefHash"/>.
/// </summary>
/// <remarks>
/// Lifecycle: created on successful full rehearsal; never mutated. Older passes
/// may be retained for history; the gate checks the latest pass per
/// <c>(BlueprintId, ExecDefHash)</c>.
/// </remarks>
public class RehearsalPassEntity
{
    /// <summary>
    /// Primary key.
    /// </summary>
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// The draft/service identity of the blueprint that was rehearsed.
    /// </summary>
    [Required]
    public string BlueprintId { get; set; } = default!;

    /// <summary>
    /// Canonical hash of the executable definition (D7) at rehearsal time.
    /// </summary>
    [Required]
    public string ExecDefHash { get; set; } = default!;

    /// <summary>
    /// UTC completion time of the rehearsal.
    /// </summary>
    public DateTimeOffset RehearsedAt { get; set; }

    /// <summary>
    /// Identifier of the platform user who ran the rehearsal.
    /// </summary>
    public Guid RehearsedByPlatformUserId { get; set; }

    /// <summary>
    /// Identifier of the sandbox register the rehearsal ran against (audit/debug).
    /// </summary>
    [Required]
    public string SandboxRegisterId { get; set; } = default!;
}
