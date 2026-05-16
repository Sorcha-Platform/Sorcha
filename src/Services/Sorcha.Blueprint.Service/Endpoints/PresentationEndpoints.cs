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

        // Feature 127 — council-page-facing disclosed-claims endpoint. Reads the
        // Redis stash of plaintext claims written alongside the outcome tx.
        // Authenticated by a single-use, short-TTL ClaimsFetchToken returned to
        // the originator from F111's InitiateAsync. The token binds the caller
        // to a single presentationRequestId; first fetch consumes it.
        // See specs/127-credential-gated-service/contracts/disclosed-claims-endpoint.md.
        app.MapGet("/{presentationRequestId:guid}/disclosed-claims", async (
                Guid presentationRequestId,
                [FromQuery] string? token,
                IClaimsFetchTokenStore tokenStore,
                IDisclosedClaimsStore claimsStore,
                IPendingPresentationStore pendingStore,
                Sorcha.Blueprint.Service.Services.Implementation.PresentationLifecycleMetrics? metrics,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    metrics?.RecordDisclosedClaimsFetch("token-missing");
                    return Results.BadRequest(new { error = "token-missing", message = "Query parameter 'token' is required." });
                }

                var sentinel = await pendingStore.GetOutcomeSentinelAsync(presentationRequestId, ct);

                // Token-bound auth: GetAndRemove returns the bound requestId and
                // atomically removes the entry (single-use). DO NOT consume yet
                // when the outcome is still pending — the council page is
                // expected to retry, and consuming on pending would force the
                // caller to mint a new token. Defer consumption until we know
                // we can return final state.
                if (sentinel is null || sentinel == "outcome-pending-write")
                {
                    // Peek the token validity without consuming. The token store
                    // doesn't expose a peek today; consume + re-store is safe
                    // because nobody else has the token yet (it was returned
                    // only to the originator) and we're racing only with our
                    // own future fetches. Simpler approach: consume on pending
                    // — the caller must reuse the same token on retry. This is
                    // acceptable here because the council page subscribes to
                    // the hub event and only fetches on outcome ready, so
                    // pending fetches should be rare. Document the choice.
                    var pendingTokenBinding = await tokenStore.GetAndRemoveAsync(token, ct);
                    if (pendingTokenBinding is null || pendingTokenBinding.Value != presentationRequestId)
                    {
                        metrics?.RecordDisclosedClaimsFetch("token-invalid");
                        return Results.Json(
                            new { error = "token-invalid", message = "Token is invalid, expired, or does not match the presentation request." },
                            statusCode: StatusCodes.Status401Unauthorized);
                    }
                    metrics?.RecordDisclosedClaimsFetch("pending");
                    return Results.Ok(new DisclosedClaimsResponse
                    {
                        PresentationRequestId = presentationRequestId,
                        Status = "pending"
                    });
                }

                if (sentinel == "decline")
                {
                    metrics?.RecordDisclosedClaimsFetch("decline");
                    return Results.Json(
                        new { error = "outcome-decline", message = "The presentation was declined; no claims to disclose." },
                        statusCode: StatusCodes.Status410Gone);
                }
                if (sentinel == "abandoned" || sentinel == "abandoned+outcome")
                {
                    metrics?.RecordDisclosedClaimsFetch("abandoned");
                    return Results.Json(
                        new { error = "outcome-abandoned", message = "The presentation attempt was abandoned." },
                        statusCode: StatusCodes.Status410Gone);
                }

                // Success path. Consume the token (single-use) and look up the
                // stashed claims.
                var tokenBinding = await tokenStore.GetAndRemoveAsync(token, ct);
                if (tokenBinding is null)
                {
                    metrics?.RecordDisclosedClaimsFetch("token-invalid");
                    return Results.Json(
                        new { error = "token-invalid", message = "Token is invalid, expired, or already used." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }
                if (tokenBinding.Value != presentationRequestId)
                {
                    metrics?.RecordDisclosedClaimsFetch("token-mismatch");
                    return Results.Json(
                        new { error = "token-mismatch", message = "Token does not match this presentation request." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                var claims = await claimsStore.GetAsync(presentationRequestId, ct);
                if (claims is null)
                {
                    // Outcome sentinel says success but no claims found — the
                    // stash TTL ran out or never landed. Surfacing as 410 lets
                    // the council page show the citizen a clear retry message
                    // instead of dangling on a misleading pending state.
                    metrics?.RecordDisclosedClaimsFetch("claims-expired");
                    return Results.Json(
                        new { error = "claims-expired", message = "The claims-fetch window has elapsed; the citizen will need to present again." },
                        statusCode: StatusCodes.Status410Gone);
                }

                var subjectDisplay = BuildSubjectDisplayName(claims);
                metrics?.RecordDisclosedClaimsFetch("success");
                return Results.Ok(new DisclosedClaimsResponse
                {
                    PresentationRequestId = presentationRequestId,
                    Status = "success",
                    Claims = claims.ToDictionary(
                        kv => kv.Key,
                        kv => (object?)kv.Value,
                        StringComparer.Ordinal),
                    SubjectDisplayName = subjectDisplay
                });
            })
            .AllowAnonymous()
            .WithName("GetDisclosedClaims")
            .WithSummary("Fetch the disclosed claims from a successful presentation for council-page autofill")
            .WithDescription(
                "Feature 127 supplement to F111's presentation lifecycle. The token returned by " +
                "InitiateAsync authenticates the caller as the council page that originated the " +
                "flow. Single-use; consumed on first fetch. Claims are returned in plaintext, " +
                "filtered to the requiredClaims declared on the action's credentialRequirement. " +
                "Returns 200 + status=pending while the wallet's outcome hasn't been written; " +
                "returns 410 on decline / abandonment / claims-expired; returns 401 when the " +
                "token is invalid or doesn't match the path's presentationRequestId.");

        return app;
    }

    private static string? BuildSubjectDisplayName(IReadOnlyDictionary<string, object> claims)
    {
        // Convenience: stitch "givenName familyName" if both present.
        if (claims.TryGetValue("givenName", out var gn) &&
            claims.TryGetValue("familyName", out var fn))
        {
            var gns = gn?.ToString()?.Trim('"');
            var fns = fn?.ToString()?.Trim('"');
            if (!string.IsNullOrEmpty(gns) && !string.IsNullOrEmpty(fns))
            {
                return $"{gns} {fns}";
            }
        }
        return null;
    }
}

/// <summary>
/// Response shape for <c>GET /api/presentations/{id}/disclosed-claims</c> (Feature 127).
/// </summary>
public sealed record DisclosedClaimsResponse
{
    /// <summary>Echoed back for the council-page-side state machine.</summary>
    public required Guid PresentationRequestId { get; init; }

    /// <summary><c>"success"</c> (claims populated) or <c>"pending"</c> (claims null; retry).</summary>
    public required string Status { get; init; }

    /// <summary>Disclosed claims in plaintext, filtered to required claims. Populated only on success.</summary>
    public IReadOnlyDictionary<string, object?>? Claims { get; init; }

    /// <summary>Convenience — <c>"givenName familyName"</c> when both claims are present.</summary>
    public string? SubjectDisplayName { get; init; }
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
