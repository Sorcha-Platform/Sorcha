// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Service.Storage.Presentations;

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

                return Results.Ok(new PresentationStatusResponse
                {
                    PresentationRequestId = presentationRequestId,
                    State = state,
                    ConsumerName = pending?.ConsumerName,
                    InstanceId = pending?.InstanceId,
                    ActionId = pending?.ActionId,
                    RegisterId = pending?.RegisterId,
                    ExpiresAt = pending is not null
                        ? pending.CreatedAt.AddSeconds(pending.ValidityWindowSeconds)
                        : null
                });
            })
            .WithName("GetPresentationStatus")
            .WithSummary("Get the current status of a presentation lifecycle")
            .WithDescription(
                "Returns the current lifecycle state of a presentation attempt: " +
                "awaiting-presentation, success, decline, abandoned, abandoned-with-late-outcome, or expired. " +
                "The register's transaction stream is the authoritative history.");

        return app;
    }
}

/// <summary>
/// Response shape for <c>GET /api/presentations/{id}/status</c>.
/// </summary>
public sealed record PresentationStatusResponse
{
    public required Guid PresentationRequestId { get; init; }
    public required string State { get; init; }
    public string? ConsumerName { get; init; }
    public Guid? InstanceId { get; init; }
    public int? ActionId { get; init; }
    public string? RegisterId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}
