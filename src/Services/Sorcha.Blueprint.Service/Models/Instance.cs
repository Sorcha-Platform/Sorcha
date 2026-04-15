// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Represents a running workflow instance.
/// Tracks the execution state of a blueprint.
/// </summary>
public class Instance
{
    /// <summary>
    /// Unique identifier for the instance (UUID)
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The blueprint being executed
    /// </summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// Blueprint version at the time of instance creation.
    /// Instance continues with this version even if blueprint is updated.
    /// </summary>
    public required int BlueprintVersion { get; init; }

    /// <summary>
    /// The register where transactions are stored
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Current workflow state
    /// </summary>
    public InstanceState State { get; set; } = InstanceState.Active;

    /// <summary>
    /// Current action ID(s) awaiting execution.
    /// Multiple IDs indicate parallel branches.
    /// </summary>
    public List<int> CurrentActionIds { get; set; } = [];

    /// <summary>
    /// Participant to wallet address bindings for this instance.
    /// Key is participant ID from blueprint, value is wallet address.
    /// </summary>
    public Dictionary<string, string> ParticipantWallets { get; init; } = new();

    /// <summary>
    /// Active parallel branches in this instance.
    /// Empty for sequential workflows.
    /// </summary>
    public List<Branch> ActiveBranches { get; set; } = [];

    /// <summary>
    /// ID of the first transaction in this instance.
    /// Used to link all transactions in the workflow.
    /// </summary>
    public string? FirstTransactionId { get; set; }

    /// <summary>
    /// ID of the most recent transaction in this instance.
    /// Used as PreviousTxId for the next transaction.
    /// </summary>
    public string? LastTransactionId { get; set; }

    /// <summary>
    /// Total number of actions completed
    /// </summary>
    public int CompletedActionCount { get; set; }

    /// <summary>
    /// Timestamp when the instance was created
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp when the instance was last updated
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optimistic concurrency version. Incremented on every update.
    /// Used to detect concurrent modification (compare-and-swap).
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Timestamp when the instance was completed (if completed)
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Tenant ID for isolation
    /// </summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Optional metadata for the instance
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// Accumulated data from all completed actions (payload + calculated values).
    /// Used as fallback when Register-based state reconstruction is unavailable.
    /// Keys are flattened field names; later actions override earlier ones.
    /// </summary>
    public Dictionary<string, object> AccumulatedData { get; set; } = new();

    /// <summary>
    /// Prepopulated payload data seeded per pending action ID when a previous
    /// action's <see cref="Sorcha.Blueprint.Models.Route.OutputMapping"/> carried
    /// data forward. Keyed by action ID; value is the JSON object to merge with
    /// the action submission before validation (submission wins on key collision).
    /// Entries are removed atomically with the action's resolution (complete,
    /// reject, or expire). Empty for actions that receive no carry-forward data.
    /// </summary>
    /// <remarks>
    /// Persisted alongside <see cref="AccumulatedData"/> as a plaintext
    /// <c>jsonb</c> column in PostgreSQL via <c>EfCoreInstanceStore</c> —
    /// see wave 14a research decision 1 for the rationale.
    /// <para>
    /// <b>Wave 14b prerequisite:</b> the credential claim flow will persist
    /// an OpenID4VCI <c>pre_authorized_code</c> in this field. That code is a
    /// short-lived bearer token and MUST be encrypted at rest before wave 14b
    /// ships — either by wrapping it through the existing disclosure pipeline
    /// or by adding an at-rest encryption layer to the instance state columns.
    /// Tracked as an open planning question in specs/104-credential-claim-action/plan.md.
    /// </para>
    /// Introduced in Feature 104.
    /// </remarks>
    public Dictionary<int, JsonObject> PendingActionPayloads { get; set; } = new();

    /// <summary>
    /// Feature 106: marks this instance as a read-only mirror reconstructed by the
    /// <c>InstanceMirrorReconstructor</c> from peer-synced transactions rather than
    /// a locally-executed authoritative instance. The normal
    /// <c>IInstanceStore.UpdateAsync</c> path MUST reject writes when this flag is
    /// set; mirror mutations go through the dedicated mirror-write path.
    /// </summary>
    public bool IsReadOnlyMirror { get; set; }
}

/// <summary>
/// State of a workflow instance
/// </summary>
public enum InstanceState
{
    /// <summary>Workflow is in progress</summary>
    Active,

    /// <summary>All actions completed successfully</summary>
    Completed,

    /// <summary>Workflow was rejected (terminal rejection)</summary>
    Rejected,

    /// <summary>Workflow timed out (e.g., parallel branch deadline)</summary>
    TimedOut,

    /// <summary>Workflow was manually cancelled</summary>
    Cancelled
}
