// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Feature 142 (D4/FR-032) — domain record that a full rehearsal succeeded for
/// a specific executable definition of a blueprint. Backs the server-side soft
/// gate at publish.
/// </summary>
public class RehearsalPass
{
    /// <summary>
    /// Unique identifier of this rehearsal pass.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The draft/service identity of the blueprint that was rehearsed.
    /// </summary>
    public string BlueprintId { get; set; } = default!;

    /// <summary>
    /// Canonical hash of the executable definition (D7) at rehearsal time.
    /// </summary>
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
    public string SandboxRegisterId { get; set; } = default!;
}
