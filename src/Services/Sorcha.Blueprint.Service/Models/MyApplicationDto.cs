// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Feature 186 — one row of a citizen's "My Applications" list: what they submitted, where it got
/// to, and what was decided.
/// </summary>
/// <remarks>
/// <para>
/// A purpose-built projection, deliberately separate from <see cref="Instance"/>. The raw model is
/// returned by <c>/api/instances</c>, which the citizen wallet app and the CLI both bind — leaving
/// that shape alone is what keeps this feature's blast radius at zero.
/// </para>
/// <para>
/// Optional members are omitted from the wire when absent rather than emitted empty. That matters
/// most for <see cref="DecisionReason"/>: an empty resolution means "this route declares no reason",
/// and rendering it as blank text would read to a citizen as a fault.
/// </para>
/// <para>
/// <see cref="Instance.DecisionReasonCode"/> is deliberately NOT on this record. It is an internal
/// classification, not citizen-facing copy.
/// </para>
/// </remarks>
public sealed record MyApplicationSummary
{
    /// <summary>Ledger-derived instance id.</summary>
    public required string InstanceId { get; init; }

    /// <summary>The blueprint (service) this application runs.</summary>
    public required string BlueprintId { get; init; }

    /// <summary>
    /// Human-readable service name. Falls back to the blueprint id when the blueprint is not
    /// replicated on this node — never blank.
    /// </summary>
    public required string BlueprintTitle { get; init; }

    /// <summary>
    /// Human-readable reference (e.g. <c>CP-RIV-14-A7K3</c>). Absent until the first action seals,
    /// because it is generated from that action's payload.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceReference { get; init; }

    /// <summary>
    /// The instance lifecycle state, by <b>name</b> — <c>Active</c>, <c>Completed</c>,
    /// <c>Rejected</c>, <c>TimedOut</c>, <c>Cancelled</c>. Never the underlying integer.
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// The outcome to show the citizen, derived from the recorded decision where there is one.
    /// Usually the same as <see cref="State"/>; <c>NotApproved</c> when the application ended on a
    /// route whose decision notice carries an adverse severity.
    /// </summary>
    /// <remarks>
    /// This exists because a refusal is a <i>route</i>, not a state. An application refused on its
    /// final step has nothing left to do and so folds to <c>Completed</c> — identical to an approved
    /// one. Without this the page would tell a refused citizen their application completed.
    /// </remarks>
    public required string Outcome { get; init; }

    /// <summary>Title from the taken route's decision notice, when it declares one.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DecisionTitle { get; init; }

    /// <summary>
    /// The citizen-facing reason, resolved from the blueprint's own catalogue. Absent when the taken
    /// route declares no notice, or declares one with neither a matching reason nor a fallback —
    /// never substituted with generic wording.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DecisionReason { get; init; }

    /// <summary>Notice severity, defaulting to <c>Warning</c> exactly as the inbox dispatcher does.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DecisionSeverity { get; init; }

    /// <summary>First current action, or null when the application is terminal.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CurrentActionId { get; init; }

    /// <summary>Title of that action.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentActionTitle { get; init; }

    /// <summary>1-based position of the current action in the service.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StepNumber { get; init; }

    /// <summary>Number of actions the service declares.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TotalSteps { get; init; }

    /// <summary>
    /// True when this application cannot progress without the caller. Fails closed — a terminal
    /// application, an unresolvable blueprint, or an absent binding all yield false, so the page
    /// never offers an action that cannot be taken.
    /// </summary>
    public required bool NeedsYou { get; init; }

    /// <summary>When the application was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>When it finished, if it has.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>
/// Feature 186 — a single application with its step timeline, for the detail view an inbox notice
/// links to.
/// </summary>
public sealed record MyApplicationDetail
{
    /// <summary>The same fields the list row carries.</summary>
    public required MyApplicationSummary Summary { get; init; }

    /// <summary>The service's steps in declared order, each marked against this application.</summary>
    public required IReadOnlyList<MyApplicationStep> Steps { get; init; }
}

/// <summary>Feature 186 — one step of the service, as it stands for this application.</summary>
/// <param name="ActionId">The blueprint action id.</param>
/// <param name="Title">The action's title, falling back to "Step {id}" when unnamed.</param>
/// <param name="Status">One of <c>Completed</c>, <c>Current</c>, <c>Upcoming</c>.</param>
public sealed record MyApplicationStep(int ActionId, string Title, string Status);

/// <summary>Feature 186 — the paged envelope, matching the client's existing PaginatedList shape.</summary>
/// <typeparam name="T">Row type.</typeparam>
/// <param name="Items">Rows on this page.</param>
/// <param name="TotalCount">Total across all pages.</param>
/// <param name="PageNumber">1-based page number.</param>
/// <param name="PageSize">Rows per page.</param>
public sealed record MyApplicationPage<T>(
    IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize);
