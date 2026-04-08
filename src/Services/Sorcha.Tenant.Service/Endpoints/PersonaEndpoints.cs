// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for the authenticated user's persona (self-asserted
/// identity attributes used for form autofill). Routes live under
/// <c>/me/persona</c> and require a platform user JWT. See
/// <c>contracts/tenant-persona-api.yaml</c> for the full contract.
/// </summary>
public static class PersonaEndpoints
{
    /// <summary>
    /// Maps the persona endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPersonaEndpoints(this IEndpointRouteBuilder app)
    {
        // Centralised rate limiting per CLAUDE.md §Critical Patterns #8 —
        // the standard API policy protects persona reads/writes from abuse.
        var group = app.MapGroup("/me/persona")
            .WithTags("Persona")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetMyPersona)
            .WithName("GetMyPersona")
            .WithSummary("Get the signed-in user's persona")
            .WithDescription(
                "Returns the decrypted persona in PersonaReadModelV1 form. Scalar attributes are " +
                "wrapped in PersonaAttribute<T> carrying provenance (always SelfAsserted in v1). " +
                "An empty persona (new user or no row) returns 200 with all fields null / empty " +
                "lists — never 404. The optional 'actingAs' query parameter is reserved for future " +
                "delegation; only the literal value 'self' is accepted in v1.")
            .Produces<PersonaReadModelV1>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPut("/", ReplaceMyPersona)
            .WithName("ReplaceMyPersona")
            .WithSummary("Replace the signed-in user's persona (full replace)")
            .WithDescription(
                "Validates the supplied PersonaAttributesV1 against the invariants in data-model.md " +
                "§2 (list cap of 5, exactly one default per multi-value list, RFC 5322 email, E.164 " +
                "phone, ISO 3166-1 alpha-2 country). Encrypts via the Wallet Service under the " +
                "sorcha:persona-vault purpose and upserts the row. Returns the canonical read model.")
            .Produces<PersonaReadModelV1>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/", DeleteMyPersona)
            .WithName("DeleteMyPersona")
            .WithSummary("Wipe the signed-in user's persona")
            .WithDescription("Hard-deletes the persona row. Idempotent — returns 204 whether or not " +
                "a row existed.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> GetMyPersona(
        HttpContext context,
        IPersonaService personaService,
        CancellationToken ct,
        string? actingAs = null)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        try
        {
            var options = new PersonaReadOptions { ActingAs = actingAs ?? "self" };
            var result = await personaService.GetAsync(userId, options, ct);
            return TypedResults.Ok(result);
        }
        catch (NotSupportedException ex)
        {
            return TypedResults.Problem(
                title: "actingAs not supported",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest,
                type: "https://sorcha.platform/errors/actingAs_not_supported");
        }
    }

    private static async Task<IResult> ReplaceMyPersona(
        HttpContext context,
        IPersonaService personaService,
        PersonaAttributesV1 body,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        try
        {
            var result = await personaService.ReplaceAsync(userId, body, ct);
            return TypedResults.Ok(result);
        }
        catch (PersonaValidationException ex)
        {
            return TypedResults.ValidationProblem(ex.Errors);
        }
        catch (PersonaWalletNotProvisionedException)
        {
            return TypedResults.Problem(
                title: "Wallet not provisioned",
                detail: "Provision a wallet before saving a personal profile.",
                statusCode: StatusCodes.Status409Conflict,
                type: "https://sorcha.platform/errors/wallet_not_provisioned");
        }
    }

    private static async Task<IResult> DeleteMyPersona(
        HttpContext context,
        IPersonaService personaService,
        CancellationToken ct)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        await personaService.DeleteAsync(userId, ct);
        return TypedResults.NoContent();
    }

    private static Guid GetUserId(HttpContext context)
    {
        var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
