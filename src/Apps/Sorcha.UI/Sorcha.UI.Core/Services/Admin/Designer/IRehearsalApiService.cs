// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.UI.Core.Services.Designer;

/// <summary>
/// Authenticated client for the Feature 142 full-rehearsal HTTP surface on the Blueprint
/// Service (gateway-fronted). A full rehearsal runs the real execution pipeline against the
/// org's private sandbox register; the dry-run counterpart never touches this surface (it runs
/// entirely in-WASM via <see cref="IDryRunHarness"/>).
/// </summary>
/// <remarks>
/// Endpoints (mirroring <c>specs/142-blueprint-lifecycle/contracts/blueprint-lifecycle.openapi.yaml</c>):
/// <list type="bullet">
/// <item><c>POST /api/blueprints/{id}/rehearsals</c> — start (201 <see cref="Rehearsal"/>; 409 when blocking validation errors exist).</item>
/// <item><c>GET /api/blueprints/{id}/rehearsals/{rid}</c> — read the current walk-through state.</item>
/// <item><c>POST /api/blueprints/{id}/rehearsals/{rid}/role</c> — switch the acting participant role.</item>
/// <item><c>POST /api/blueprints/{id}/rehearsals/{rid}/steps</c> — submit the current action as the acting role.</item>
/// <item><c>DELETE /api/blueprints/{id}/rehearsals/{rid}</c> — discard the rehearsal (and its ephemeral wallets/instance) server-side.</item>
/// </list>
/// </remarks>
public interface IRehearsalApiService
{
    /// <summary>
    /// Starts a full rehearsal for <paramref name="blueprintId"/>. Returns the freshly created
    /// <see cref="Rehearsal"/> on success, or <c>null</c> when the server returned a
    /// <c>409</c> blocking-validation soft gate (inspect <paramref name="blockingErrors"/>).
    /// </summary>
    /// <param name="blueprintId">The draft blueprint to rehearse.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="StartRehearsalOutcome"/>: either the started <see cref="Rehearsal"/> or a
    /// signal that blocking validation errors must be fixed first.
    /// </returns>
    Task<StartRehearsalOutcome> StartFullRehearsalAsync(string blueprintId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current state of an in-flight rehearsal.</summary>
    Task<Rehearsal?> GetRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default);

    /// <summary>Switches the acting participant role and returns the refreshed rehearsal state.</summary>
    Task<Rehearsal?> SwitchRoleAsync(string blueprintId, Guid rehearsalId, string role, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the current action as the acting role with <paramref name="payloadJson"/> (a raw
    /// JSON object string) and returns the refreshed rehearsal state. On a <c>422</c> validation
    /// failure the returned rehearsal reflects the unchanged walk-through (step stays current).
    /// </summary>
    Task<Rehearsal?> SubmitStepAsync(string blueprintId, Guid rehearsalId, int actionId, string payloadJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards a rehearsal server-side (releases the sandbox instance + ephemeral wallets) so
    /// the author can re-run a fresh walk-through. Returns <c>true</c> on <c>204</c>.
    /// </summary>
    Task<bool> DeleteRehearsalAsync(string blueprintId, Guid rehearsalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discriminated result of <see cref="IRehearsalApiService.StartFullRehearsalAsync"/>: either the
/// rehearsal started or it was blocked by the <c>409</c> blocking-validation soft gate.
/// </summary>
/// <param name="Rehearsal">The started rehearsal, or <c>null</c> when blocked / on error.</param>
/// <param name="Blocked">True when the start was refused because blocking validation errors exist (HTTP 409).</param>
public sealed record StartRehearsalOutcome(Rehearsal? Rehearsal, bool Blocked)
{
    /// <summary>The rehearsal started successfully.</summary>
    public static StartRehearsalOutcome Started(Rehearsal rehearsal) => new(rehearsal, false);

    /// <summary>The start was refused — fix blocking validation errors first.</summary>
    public static StartRehearsalOutcome BlockedByValidation() => new(null, true);

    /// <summary>The start failed for some other reason (network/server error).</summary>
    public static StartRehearsalOutcome Errored() => new(null, false);
}
