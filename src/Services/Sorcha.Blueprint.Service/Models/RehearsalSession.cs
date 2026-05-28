// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Feature 142 (T028) — transient, in-memory state for one full rehearsal run. Held in a
/// singleton <c>ConcurrentDictionary</c> (Redis is a future swap, not a ledger record). Carries
/// the server-only state the wire <see cref="Rehearsal"/> DTO does NOT expose: the ephemeral
/// per-role sandbox wallet map (D2), the live workflow instance id, and the per-step submitted
/// payloads / outcomes. Discarded on reset (the sandbox register persists — D1).
/// </summary>
public sealed class RehearsalSession
{
    /// <summary>Unique rehearsal identifier.</summary>
    public Guid RehearsalId { get; init; }

    /// <summary>The blueprint being rehearsed.</summary>
    public string BlueprintId { get; init; } = string.Empty;

    /// <summary>Executable-definition hash snapshotted at rehearsal start (D7).</summary>
    public string ExecDefHash { get; init; } = string.Empty;

    /// <summary>Rehearsal mode. This service only ever drives <see cref="RehearsalMode.Full"/>.</summary>
    public RehearsalMode Mode { get; init; } = RehearsalMode.Full;

    /// <summary>The per-org sandbox register this run targets (D1).</summary>
    public string? SandboxRegisterId { get; set; }

    /// <summary>
    /// The live sandbox workflow instance backing the walk-through. Null until the instance is
    /// created at start; cleared on reset.
    /// </summary>
    public string? InstanceId { get; set; }

    /// <summary>
    /// Ephemeral per-role sandbox wallets minted at start (D2): role id → wallet address.
    /// Cleared on reset — the wallets are abandoned, never reused.
    /// </summary>
    public Dictionary<string, string> RoleWallets { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The participant role currently being acted as.</summary>
    public string CurrentActingRole { get; set; } = string.Empty;

    /// <summary>The owning organisation (tenant) of the rehearsal.</summary>
    public string OrganizationId { get; init; } = string.Empty;

    /// <summary>The platform user who started the rehearsal.</summary>
    public Guid StartedByPlatformUserId { get; init; }

    /// <summary>UTC timestamp when the rehearsal started — drives the duration histogram (T058).</summary>
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The ordered walk-through steps.</summary>
    public List<RehearsalSessionStep> Steps { get; } = [];

    /// <summary>The current rehearsal outcome.</summary>
    public RehearsalOutcome Outcome { get; set; } = RehearsalOutcome.InProgress;

    /// <summary>The plain-language activity log, oldest first.</summary>
    public List<RehearsalEvent> Log { get; } = [];

    /// <summary>Synchronisation root for mutating this session's transient state.</summary>
    public object SyncRoot { get; } = new();
}

/// <summary>
/// Feature 142 (T028) — server-side state for a single rehearsal step. Mirrors the data-model
/// <c>RehearsalStep</c> including the transient submitted payload / routing / disclosure outcomes
/// that the wire DTO does not surface.
/// </summary>
public sealed class RehearsalSessionStep
{
    /// <summary>The blueprint action this step exercises.</summary>
    public int ActionId { get; init; }

    /// <summary>The participant role acting on this step.</summary>
    public string ActingRole { get; set; } = string.Empty;

    /// <summary>Walk-through status of this step.</summary>
    public RehearsalStepStatus Status { get; set; } = RehearsalStepStatus.Pending;

    /// <summary>What the acting role submitted (transient — debug/audit only).</summary>
    public IReadOnlyDictionary<string, object>? SubmittedPayload { get; set; }

    /// <summary>The next action(s) chosen by routing.</summary>
    public IReadOnlyList<int>? RoutingOutcome { get; set; }
}
