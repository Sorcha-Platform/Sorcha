// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Feature 142 (T028 / US2) — orchestrates a full rehearsal: a real run of a draft blueprint
/// against the per-org devMode sandbox register, acting as each participant role in turn. Drives
/// the same execution pipeline (<see cref="IActionExecutionService"/>) as production, so a passing
/// rehearsal is meaningful evidence that the blueprint executes. On a successful terminal state it
/// records a <c>RehearsalPass</c> that unlocks the publish soft gate (D4/FR-032).
/// </summary>
/// <remarks>
/// This service handles <see cref="RehearsalMode.Full"/> only. Dry-run is a client-side concern
/// (in-WASM engine) and never reaches this surface.
/// </remarks>
public interface IRehearsalOrchestrationService
{
    /// <summary>
    /// Starts a full rehearsal: rejects (409 → throws <see cref="RehearsalValidationException"/>)
    /// when the blueprint has blocking validation errors; otherwise provisions/reuses the org
    /// sandbox register (T026), mints ephemeral per-role sandbox wallets (T027), publishes the
    /// current draft to the sandbox, creates a fresh instance, snapshots the executable-definition
    /// hash, and returns the initial <see cref="Rehearsal"/> with steps in flow order and the first
    /// step current.
    /// </summary>
    /// <param name="blueprintId">The draft blueprint to rehearse.</param>
    /// <param name="organizationId">The owning organisation / tenant.</param>
    /// <param name="platformUserId">The platform user running the rehearsal.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Rehearsal> StartFullAsync(
        string blueprintId,
        string organizationId,
        Guid platformUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the current state of a rehearsal, or <c>null</c> when unknown.</summary>
    Task<Rehearsal?> GetAsync(Guid rehearsalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the acting participant role for subsequent step submissions. Returns the updated
    /// rehearsal, or <c>null</c> when the rehearsal is unknown.
    /// </summary>
    Task<Rehearsal?> SwitchRoleAsync(
        Guid rehearsalId,
        string role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the current action acting as the current role: runs the REAL pipeline against the
    /// sandbox, advances the walk-through, appends plain-language log events, and — on reaching a
    /// successful terminal state — records exactly one <c>RehearsalPass</c> and sets the outcome to
    /// <see cref="RehearsalOutcome.Passed"/>. Returns the updated rehearsal, or <c>null</c> when the
    /// rehearsal is unknown.
    /// </summary>
    Task<Rehearsal?> SubmitStepAsync(
        Guid rehearsalId,
        int actionId,
        string payloadJson,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the rehearsal instance state and the ephemeral wallet map (idempotent — a second
    /// reset is a no-op). The sandbox register persists (D1). Returns <c>true</c> when a rehearsal
    /// was found and reset, <c>false</c> when unknown.
    /// </summary>
    Task<bool> ResetAsync(Guid rehearsalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Feature 142 — thrown by <see cref="IRehearsalOrchestrationService.StartFullAsync"/> when the
/// blueprint has blocking validation errors and a rehearsal cannot start. The endpoint (T029) maps
/// this to <c>409 Conflict</c>.
/// </summary>
public sealed class RehearsalValidationException : Exception
{
    /// <summary>The blocking validation errors that prevented the rehearsal from starting.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Initialises a new instance of the <see cref="RehearsalValidationException"/> class.</summary>
    public RehearsalValidationException(IReadOnlyList<string> errors)
        : base("Blueprint has blocking validation errors and cannot be rehearsed.")
    {
        Errors = errors ?? [];
    }
}
