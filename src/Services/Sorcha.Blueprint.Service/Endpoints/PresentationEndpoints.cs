// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.ServiceDefaults;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 111 — HTTP endpoints for the Timebound Presentation Lifecycle.
/// Status polling (this phase) and verifier callback relay (Phase 4 US2).
/// </summary>
public static class PresentationEndpoints
{
    public static RouteGroupBuilder MapPresentationEndpoints(this RouteGroupBuilder app)
    {
        app.MapGet("/{presentationRequestId:guid}/status", async (
                Guid presentationRequestId,
                IPendingPresentationStore store,
                CancellationToken ct) =>
            {
                var pending = await store.GetAsync(presentationRequestId, ct);
                var sentinel = await store.GetOutcomeSentinelAsync(presentationRequestId, ct);

                if (pending is null && sentinel is null)
                {
                    return Results.NotFound(new { error = "Presentation request not found or expired." });
                }

                string state = (pending, sentinel) switch
                {
                    (_, "success") => "success",
                    (_, "decline") => "decline",
                    (_, "abandoned") => "abandoned",
                    (_, "abandoned+outcome") => "abandoned-with-late-outcome",
                    ({ } _, null) or ({ } _, "outcome-pending-write") => "awaiting-presentation",
                    (null, _) => "expired",
                    _ => "unknown"
                };

                // The presentationRequestId appears in the QR URI sent to the
                // citizen's wallet app, which polls this endpoint unauthenticated.
                // The response is deliberately scoped to lifecycle state + expiry
                // only — register/instance/action/consumer metadata is never
                // returned here, to prevent anyone holding a leaked QR URI from
                // probing register structure. Full history lives on the register
                // transaction stream under its own authorisation rules.
                return Results.Ok(new PresentationStatusResponse
                {
                    PresentationRequestId = presentationRequestId,
                    State = state,
                    ExpiresAt = pending is not null
                        ? pending.CreatedAt.AddSeconds(pending.ValidityWindowSeconds)
                        : null
                });
            })
            .AllowAnonymous()
            .WithName("GetPresentationStatus")
            .WithSummary("Get the current lifecycle state of a presentation attempt")
            .WithDescription(
                "Returns only the lifecycle state (awaiting-presentation / success / " +
                "decline / abandoned / abandoned-with-late-outcome / expired) and the " +
                "attempt expiry. Wallet-facing and polled unauthenticated by the QR " +
                "scanner; no instance, register, or consumer metadata is included. " +
                "The register's transaction stream is the authoritative history.");

        app.MapPost("/callbacks/{consumerName}/{presentationRequestId:guid}", async (
                string consumerName,
                Guid presentationRequestId,
                [FromBody] JsonElement verifierPayload,
                IPresentationLifecycleService lifecycle,
                CancellationToken ct) =>
            {
                try
                {
                    var result = await lifecycle.HandleOutcomeAsync(
                        consumerName, presentationRequestId, verifierPayload, ct);
                    return Results.Ok(new PresentationCallbackResponse(
                        Kind: result.Kind.ToString(),
                        OutcomeTransactionId: result.OutcomeTransactionId,
                        IdempotentReplay: result.IsIdempotentReplay,
                        LateAfterAbandonment: result.IsLateAfterAbandonment));
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithName("PresentationCallback")
            .WithSummary("Verifier callback for a presentation outcome")
            .WithDescription(
                "Called by a registered IPresentationConsumer (e.g. HAIP Service) after " +
                "verifying a presentation. Writes the PresentationOutcome transaction. " +
                "Idempotent by presentationRequestId. " +
                "(Action advancement on success is deferred to US3.)")
            .RequireAuthorization(AuthorizationPolicies.RequireService);

        return app;
    }
}

/// <summary>
/// Response shape for <c>GET /api/presentations/{id}/status</c>. Deliberately
/// minimal — wallet-facing endpoint returns state + expiry only, no register
/// or instance metadata (see security review on PR #382).
/// </summary>
public sealed record PresentationStatusResponse
{
    public required Guid PresentationRequestId { get; init; }
    public required string State { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

/// <summary>
/// Response shape for <c>POST /api/presentations/callbacks/{consumer}/{id}</c>.
/// </summary>
/// <param name="Kind">Outcome kind from the consumer ("Success" / "Decline").</param>
/// <param name="OutcomeTransactionId">The written outcome transaction id, or empty string on an idempotent replay.</param>
/// <param name="IdempotentReplay">True when the callback was a duplicate and no new tx was written.</param>
/// <param name="LateAfterAbandonment">True when the outcome arrived after the abandonment sweeper already wrote a PresentationAbandoned tx.</param>
public sealed record PresentationCallbackResponse(
    string Kind,
    string OutcomeTransactionId,
    bool IdempotentReplay,
    bool LateAfterAbandonment);
