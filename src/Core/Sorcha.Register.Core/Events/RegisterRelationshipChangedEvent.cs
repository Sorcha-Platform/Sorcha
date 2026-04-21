// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models.LocalRelationship;

namespace Sorcha.Register.Core.Events;

/// <summary>
/// Feature 108. Published on the <c>RegisterEventChannels.RegisterRelationshipChanged</c>
/// Redis channel when the local node's derived role set for a register changes.
/// Triggered by control-transaction seals (governance ops such as AddValidator,
/// RemoveValidator, RotateKey).
/// </summary>
/// <remarks>
/// The event is scoped to *this node's identity* — <see cref="AddedRoles"/> and
/// <see cref="RemovedRoles"/> describe the local node's role diff, not a cross-node
/// broadcast. Each node publishes its own view based on its own local identity.
/// </remarks>
public class RegisterRelationshipChangedEvent
{
    /// <summary>Register whose relationship changed.</summary>
    public required string RegisterId { get; set; }

    /// <summary>Docket number of the control transaction that produced the change.</summary>
    public int ControlRecordVersion { get; set; }

    /// <summary>Roles the local node now holds that it did not hold before.</summary>
    public RegisterRoleSet AddedRoles { get; set; }

    /// <summary>Roles the local node held before that it no longer holds.</summary>
    public RegisterRoleSet RemovedRoles { get; set; }

    /// <summary>When the change was detected locally.</summary>
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
