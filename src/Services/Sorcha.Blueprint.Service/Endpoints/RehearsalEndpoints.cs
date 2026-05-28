// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Mvc;

using Sorcha.Blueprint.Service.Extensions;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.ServiceClients.Blueprint.Models;

namespace Sorcha.Blueprint.Service.Endpoints;

/// <summary>
/// Feature 142 (T029 / US2) — REST endpoints for the full-rehearsal walk-through. A rehearsal
/// runs a draft blueprint against the per-org devMode sandbox register, acting as each participant
/// role in turn, and on a successful terminal state records a RehearsalPass that unlocks the
/// publish soft gate. Dry-run is a client-side concern and never reaches this surface — these
/// endpoints are full-mode only.
/// </summary>
/// <remarks>
/// Authorisation mirrors the draft authoring surface (the <c>/api/blueprints</c> group):
/// <c>CanManageBlueprints</c> (org-member or service token) composed with the platform-tier gate
/// <c>RequirePlatformAudience</c> (Feature 136) — rehearsal is an authoring activity, not a
/// citizen/consumer one.
/// </remarks>
public static class RehearsalEndpoints
{
    /// <summary>
    /// Maps the rehearsal endpoints under <c>/api/blueprints/{id}/rehearsals</c>.
    /// </summary>
    /// <param name="app">The web application to map endpoints on.</param>
    public static void MapRehearsalEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/blueprints")
            .WithTags("Rehearsals")
            .RequireAuthorization("CanManageBlueprints", "RequirePlatformAudience");

        group.MapPost("/{id}/rehearsals", StartRehearsal)
            .WithName("StartRehearsal")
            .WithSummary("Start a full rehearsal")
            .WithDescription(
                "Lazily provisions (or reuses) the org's devMode sandbox register, mints ephemeral " +
                "per-role sandbox wallets, publishes the current draft to the sandbox, creates a fresh " +
                "instance, and returns the initial walk-through. Returns 409 when the blueprint has " +
                "blocking validation errors. Dry-run mode is handled client-side and does NOT call this.")
            .Produces<Rehearsal>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        group.MapGet("/{id}/rehearsals/{rehearsalId:guid}", GetRehearsal)
            .WithName("GetRehearsal")
            .WithSummary("Get rehearsal status and log")
            .WithDescription("Retrieves the current state of a rehearsal: outcome, acting role, walk-through steps, and activity log.")
            .Produces<Rehearsal>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapDelete("/{id}/rehearsals/{rehearsalId:guid}", ResetRehearsal)
            .WithName("ResetRehearsal")
            .WithSummary("Reset/discard a rehearsal")
            .WithDescription(
                "Clears the rehearsal instance and discards its ephemeral per-role sandbox wallets. The " +
                "sandbox register itself persists (reused next time). Idempotent — returns 204 even if the " +
                "rehearsal is already gone.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapPost("/{id}/rehearsals/{rehearsalId:guid}/role", SwitchRehearsalRole)
            .WithName("SwitchRehearsalRole")
            .WithSummary("Switch the acting participant role")
            .WithDescription("Changes the participant role under which subsequent step submissions are made.")
            .Produces<Rehearsal>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id}/rehearsals/{rehearsalId:guid}/steps", SubmitRehearsalStep)
            .WithName("SubmitRehearsalStep")
            .WithSummary("Submit the current action as the acting role")
            .WithDescription(
                "Runs the real execution pipeline (sign as acting role server-side, validate, route, seal, " +
                "disclose, issue credentials) against the sandbox register, advances the walk-through, and " +
                "appends to the log. On reaching a successful terminal state, records a RehearsalPass for the " +
                "current executable-definition hash and marks Go live unlockable. Returns 422 when the payload " +
                "fails validation or the submitted action is not the current step.")
            .Produces<Rehearsal>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status422UnprocessableEntity);
    }

    /// <summary>POST /api/blueprints/{id}/rehearsals — start a full rehearsal.</summary>
    private static async Task<IResult> StartRehearsal(
        string id,
        StartFullRehearsalRequest request,
        HttpContext context,
        IRehearsalOrchestrationService orchestration,
        CancellationToken cancellationToken)
    {
        // Only full mode is valid over HTTP (dry-run is client-side).
        if (!string.Equals(request.Mode, "full", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.UnprocessableEntity(new { error = "Only mode 'full' is supported over HTTP. Dry-run is handled client-side." });
        }

        var organizationId = context.GetOrganizationId();
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            // No org context — the caller cannot author/rehearse within an organisation.
            return TypedResults.Forbid();
        }

        var platformUserId = ResolvePlatformUserId(context);

        try
        {
            var rehearsal = await orchestration.StartFullAsync(
                id, organizationId, platformUserId, cancellationToken);
            return TypedResults.Created($"/api/blueprints/{id}/rehearsals/{rehearsal.RehearsalId}", rehearsal);
        }
        catch (RehearsalValidationException ex)
        {
            // Blocking validation errors — cannot rehearse (409).
            return TypedResults.Conflict(new { error = ex.Message, errors = ex.Errors });
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    /// <summary>GET /api/blueprints/{id}/rehearsals/{rehearsalId} — fetch rehearsal state.</summary>
    private static async Task<IResult> GetRehearsal(
        string id,
        Guid rehearsalId,
        IRehearsalOrchestrationService orchestration,
        CancellationToken cancellationToken)
    {
        var rehearsal = await orchestration.GetAsync(rehearsalId, cancellationToken);
        return rehearsal is not null ? TypedResults.Ok(rehearsal) : TypedResults.NotFound();
    }

    /// <summary>DELETE /api/blueprints/{id}/rehearsals/{rehearsalId} — reset/discard (idempotent).</summary>
    private static async Task<IResult> ResetRehearsal(
        string id,
        Guid rehearsalId,
        IRehearsalOrchestrationService orchestration,
        CancellationToken cancellationToken)
    {
        // Idempotent: 204 whether or not a rehearsal was found (ResetAsync returns false when unknown).
        await orchestration.ResetAsync(rehearsalId, cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/role — switch acting role.</summary>
    private static async Task<IResult> SwitchRehearsalRole(
        string id,
        Guid rehearsalId,
        SwitchRehearsalRoleBody request,
        IRehearsalOrchestrationService orchestration,
        CancellationToken cancellationToken)
    {
        try
        {
            var rehearsal = await orchestration.SwitchRoleAsync(rehearsalId, request.Role, cancellationToken);
            return rehearsal is not null ? TypedResults.Ok(rehearsal) : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // e.g. role is not a participant in this rehearsal.
            return TypedResults.UnprocessableEntity(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return TypedResults.UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/steps — submit the current step.</summary>
    private static async Task<IResult> SubmitRehearsalStep(
        string id,
        Guid rehearsalId,
        SubmitRehearsalStepBody request,
        IRehearsalOrchestrationService orchestration,
        CancellationToken cancellationToken)
    {
        // The contract carries payload as a JSON object; the orchestration takes it as a raw JSON string.
        var payloadJson = request.Payload.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : request.Payload.GetRawText();

        try
        {
            var rehearsal = await orchestration.SubmitStepAsync(
                rehearsalId, request.ActionId, payloadJson, cancellationToken);
            return rehearsal is not null ? TypedResults.Ok(rehearsal) : TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // Payload failed validation, step not current, or rehearsal already reset (422).
            return TypedResults.UnprocessableEntity(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Resolves the platform user id from the caller's claims (<c>NameIdentifier</c> then <c>sub</c>).
    /// Returns <see cref="Guid.Empty"/> when absent or non-GUID — the id is only used to attribute the
    /// recorded RehearsalPass and is not a trust boundary.
    /// </summary>
    private static Guid ResolvePlatformUserId(HttpContext context)
    {
        var raw = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}

/// <summary>
/// Request body for starting a full rehearsal (Feature 142). <c>POST /api/blueprints/{id}/rehearsals</c>.
/// </summary>
public sealed record StartFullRehearsalRequest
{
    /// <summary>Rehearsal mode — only <c>full</c> is valid over HTTP.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "full";
}

/// <summary>
/// Request body for switching the acting participant role (Feature 142).
/// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/role</c>.
/// </summary>
public sealed record SwitchRehearsalRoleBody
{
    /// <summary>The participant role to act as.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;
}

/// <summary>
/// Request body for submitting the current rehearsal step (Feature 142).
/// <c>POST /api/blueprints/{id}/rehearsals/{rehearsalId}/steps</c>.
/// </summary>
public sealed record SubmitRehearsalStepBody
{
    /// <summary>The action being submitted (must be the current walk-through step).</summary>
    [JsonPropertyName("actionId")]
    public int ActionId { get; init; }

    /// <summary>The action payload as a JSON object.</summary>
    [JsonPropertyName("payload")]
    public JsonElement Payload { get; init; }
}
