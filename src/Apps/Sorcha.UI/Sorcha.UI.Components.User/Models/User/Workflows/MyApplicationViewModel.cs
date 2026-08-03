// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Workflows;

/// <summary>
/// Feature 186 — one row of the citizen's "My Applications" list. Mirrors the
/// <c>MyApplicationSummary</c> the Blueprint Service projects at
/// <c>GET /api/me/applications</c>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a separate type from <see cref="WorkflowInstanceViewModel"/>, which binds the raw
/// instance model on <c>/api/instances</c> and is consumed by the admin workflow list and detail
/// components.
/// </para>
/// <para>
/// <b>Every field here is resolved server-side</b>, including the decision wording. The client
/// deliberately does no derivation: the wording comes from the blueprint's own catalogue via the same
/// resolution the inbox notice uses, so the page and the notification cannot disagree.
/// </para>
/// </remarks>
public record MyApplicationViewModel
{
    /// <summary>Ledger-derived instance id.</summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>The blueprint (service) this application runs.</summary>
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Human-readable service name; never blank (falls back to the id server-side).</summary>
    public string BlueprintTitle { get; init; } = string.Empty;

    /// <summary>Human-readable reference, absent until the first action seals.</summary>
    public string? InstanceReference { get; init; }

    /// <summary>Lifecycle state by name — <c>Active</c>, <c>Completed</c>, and so on.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// The outcome to show the citizen. Usually the same as <see cref="State"/>;
    /// <c>NotApproved</c> when the application ended on an adversely-flagged decision route.
    /// </summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Decision notice title, when the taken route declared one.</summary>
    public string? DecisionTitle { get; init; }

    /// <summary>
    /// The citizen-facing reason. Null means the route declared no reason — render nothing rather
    /// than an empty line, and never substitute wording of your own.
    /// </summary>
    public string? DecisionReason { get; init; }

    /// <summary>Notice severity (<c>Warning</c>, <c>Error</c>, <c>Success</c>, …).</summary>
    public string? DecisionSeverity { get; init; }

    /// <summary>The action awaiting execution, or null when the application is finished.</summary>
    public int? CurrentActionId { get; init; }

    /// <summary>Title of that action.</summary>
    public string? CurrentActionTitle { get; init; }

    /// <summary>1-based position of the current action.</summary>
    public int? StepNumber { get; init; }

    /// <summary>Number of steps the service declares.</summary>
    public int? TotalSteps { get; init; }

    /// <summary>
    /// True when this application is waiting on the signed-in citizen. Computed server-side and
    /// fail-closed, which is what stops the page offering an action that cannot be taken (#1268).
    /// </summary>
    public bool NeedsYou { get; init; }

    /// <summary>When the application was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>When it finished, if it has.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>Feature 186 — one step of the service, as it stands for this application.</summary>
public record MyApplicationStepViewModel
{
    /// <summary>The blueprint action id.</summary>
    public int ActionId { get; init; }

    /// <summary>The step's title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>One of <c>Completed</c>, <c>Current</c>, <c>Upcoming</c>.</summary>
    public string Status { get; init; } = string.Empty;
}

/// <summary>Feature 186 — a single application with its step timeline.</summary>
public record MyApplicationDetailViewModel
{
    /// <summary>The same fields the list row carries.</summary>
    public MyApplicationViewModel Summary { get; init; } = new();

    /// <summary>The service's steps in order, marked against this application.</summary>
    public List<MyApplicationStepViewModel> Steps { get; init; } = [];
}
