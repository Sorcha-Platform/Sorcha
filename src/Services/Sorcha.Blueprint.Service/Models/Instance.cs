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
    /// </summary>
    /// <remarks>
    /// <b>Display label only</b> — do not resolve a definition by it. It is assigned from in-memory
    /// insert order and re-derived on recovery, and two of the paths that write it hardcode 1. The
    /// authoritative answer to "which definition is this instance running" is
    /// <see cref="BlueprintExecDefHash"/> (Feature 194).
    /// </remarks>
    public required int BlueprintVersion { get; init; }

    /// <summary>
    /// The executable-definition hash this instance is pinned to — the definition it started on and
    /// will run for its whole life (Feature 194).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Established once, when the instance is created, from the latest definition published to its
    /// register at that moment. <b>Immutable thereafter</b>: republishing the blueprint, restarting
    /// the service, replacing the node, or any submission by any participant leaves it alone. A
    /// folded transaction claiming a different pin is refused rather than applied.
    /// </para>
    /// <para>
    /// Empty means <i>unpinned</i> — an instance whose transactions predate Feature 194. Those fall
    /// back to the latest published definition, identically on every derivation path, and each use
    /// of that fallback is counted so the fallback can eventually be removed on evidence.
    /// </para>
    /// </remarks>
    public string BlueprintExecDefHash { get; set; } = string.Empty;

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
    /// Feature 145: idempotency watermark for the deterministic instance projection — the
    /// id of the most recent sealed action transaction folded into this materialized view.
    /// The <c>InstanceProjector</c> skips re-applying a transaction at or before this point,
    /// so re-observing an already-folded sealed docket is a no-op (FR-004). Null until the
    /// first sealed action is folded.
    /// </summary>
    public string? LastAppliedTxId { get; set; }

    /// <summary>
    /// Feature 186: the id of the route the most recently folded decision took, from the signed
    /// <c>RoutingDecision</c> on the transaction's clear metadata. Null when the last fold carried
    /// no decision, on transactions sealed before Feature 184, and on presentation outcomes.
    /// </summary>
    /// <remarks>
    /// This is what lets a reader find the taken route in the replicated blueprint and discover
    /// whether the outcome was adverse. It is load-bearing rather than merely informative: a
    /// refusal is expressed as a route carrying an <c>x-decision-notice</c>, not as a distinct
    /// instance state, so an application refused on its final step reaches
    /// <see cref="InstanceState.Completed"/> and is otherwise indistinguishable from an approved
    /// one.
    /// </remarks>
    public string? DecisionRouteId { get; set; }

    /// <summary>
    /// Feature 186: the non-sensitive reason code carried on that same decision, resolved to
    /// citizen-facing text at read time through the taken route's
    /// <c>x-decision-notice</c> catalogue. Never shown to a citizen directly.
    /// </summary>
    public string? DecisionReasonCode { get; set; }
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
