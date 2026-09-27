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
    /// JSON object string).
    /// </summary>
    /// <remarks>
    /// #1724: the server's <c>422</c> validation-failure body is a plain <c>{ "error": "..." }</c>,
    /// never a <see cref="Rehearsal"/> — deserialising it as one used to silently produce a BLANK
    /// rehearsal and wipe the walk-through state. <see cref="SubmitStepOutcome"/> carries the
    /// server's own refusal reason instead, for every non-success response (422, 404, 403, ...).
    /// </remarks>
    Task<SubmitStepOutcome> SubmitStepAsync(string blueprintId, Guid rehearsalId, int actionId, string payloadJson, CancellationToken cancellationToken = default);

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

/// <summary>
/// Outcome of <see cref="IRehearsalApiService.SubmitStepAsync"/> (#1724): either the refreshed
/// <see cref="Rehearsal"/> the server applied the step against, or the reason it refused — e.g.
/// the generated payload failed schema validation, the step was not the current one, or a
/// transport/server error. Exactly one of the two is set.
/// </summary>
/// <param name="Rehearsal">The refreshed rehearsal state, or <c>null</c> when the call was refused.</param>
/// <param name="RefusalReason">The server's own explanation, or a generic message on transport failure — null when <see cref="Rehearsal"/> is set.</param>
public sealed record SubmitStepOutcome(Rehearsal? Rehearsal, string? RefusalReason)
{
    /// <summary>The step was applied; the rehearsal has moved on.</summary>
    public static SubmitStepOutcome Applied(Rehearsal rehearsal) => new(rehearsal, null);

    /// <summary>The server (or the client itself, for a malformed payload) refused the step.</summary>
    public static SubmitStepOutcome Refused(string reason) => new(null, reason);
}
