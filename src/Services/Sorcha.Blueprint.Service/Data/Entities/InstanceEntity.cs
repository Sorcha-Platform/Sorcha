// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Blueprint.Service.Data.Entities;

/// <summary>
/// Represents a running blueprint instance with execution state, participant
/// tracking, accumulated data, and concurrency control via a version token.
/// </summary>
public class InstanceEntity
{
    /// <summary>
    /// Unique identifier for the instance.
    /// </summary>
    [Required]
    public string Id { get; set; } = default!;

    /// <summary>
    /// Identifier of the blueprint this instance executes.
    /// </summary>
    [Required]
    public string BlueprintId { get; set; } = default!;

    /// <summary>
    /// Version of the blueprint at the time of instance creation.
    /// </summary>
    public int BlueprintVersion { get; set; }

    /// <summary>
    /// Identifier of the register this instance operates on.
    /// </summary>
    [Required]
    public string RegisterId { get; set; } = default!;

    /// <summary>
    /// Current execution state.
    /// </summary>
    public Sorcha.Blueprint.Service.Models.InstanceState State { get; set; } = Sorcha.Blueprint.Service.Models.InstanceState.Active;

    /// <summary>
    /// Current action identifiers stored as JSONB.
    /// </summary>
    public string? CurrentActionIds { get; set; }

    /// <summary>
    /// Participant wallet addresses stored as JSONB.
    /// </summary>
    public string? ParticipantWallets { get; set; }

    /// <summary>
    /// Transaction ID of the first transaction in this instance.
    /// </summary>
    public string? FirstTransactionId { get; set; }

    /// <summary>
    /// Transaction ID of the most recent transaction in this instance.
    /// </summary>
    public string? LastTransactionId { get; set; }

    /// <summary>
    /// Number of actions completed in this instance.
    /// </summary>
    public int CompletedActionCount { get; set; } = 0;

    /// <summary>
    /// Accumulated data from completed actions stored as JSONB.
    /// </summary>
    public string? AccumulatedData { get; set; }

    /// <summary>
    /// Prepopulated action payloads (keyed by action ID) seeded by
    /// <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/> on previous
    /// action execution, stored as JSONB. Introduced in Feature 104 wave 14a.
    /// </summary>
    public string? PendingActionPayloads { get; set; }

    /// <summary>
    /// Active execution branches stored as JSONB.
    /// </summary>
    public string? ActiveBranches { get; set; }

    /// <summary>
    /// Additional metadata stored as JSONB.
    /// </summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Concurrency token for optimistic concurrency control.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Timestamp when the instance was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when the instance was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Timestamp when the instance completed, if applicable.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Feature 106: marks this row as a read-only mirror reconstructed from peer-sync
    /// observations rather than a locally-executed instance. When true, the normal
    /// <c>IInstanceStore.UpdateAsync</c> path MUST reject writes — mirror rows are only
    /// mutated via the dedicated <c>UpdateMirrorAsync</c> path invoked by the
    /// <c>InstanceMirrorReconstructor</c> background service. See
    /// <c>specs/106-register-native-credentials/contracts/instance-mirror-reconstructor.md</c>.
    /// </summary>
    public bool IsReadOnlyMirror { get; internal set; }
}
