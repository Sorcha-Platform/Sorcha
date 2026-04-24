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

        app.MapPost("/callbacks/{consumerName}", async (
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
                    return Results.Ok(new
                    {
                        kind = result.Kind.ToString(),
                        outcomeTransactionId = result.OutcomeTransactionId,
                        idempotentReplay = result.IsIdempotentReplay,
                        lateAfterAbandonment = result.IsLateAfterAbandonment
                    });
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
                "verifying a presentation. Writes the PresentationOutcome transaction and " +
                "advances the action on success. Idempotent by presentationRequestId.")
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
