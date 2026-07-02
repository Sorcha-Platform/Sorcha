// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Wallet;
using Sorcha.ServiceDefaults;
using Sorcha.Tenant.Models.Persona;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Minimal API endpoints for the authenticated user's persona (self-asserted
/// identity attributes used for form autofill). Routes live under
/// <c>/api/me/persona</c> and require a platform user JWT. See
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
        var group = app.MapGroup("/api/me/persona")
            .WithTags("Persona")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapGet("/", GetMyPersona)
            .WithName("GetMyPersona")
            .WithSummary("Get the signed-in user's persona for a context")
            .WithDescription(
                "Returns the decrypted persona in PersonaReadModelV1 form for the requested " +
                "organisational context (Feature 125). Omit the 'context' query parameter for the " +
                "Personal context. Scalar attributes are wrapped in PersonaAttribute<T> carrying " +
                "provenance (always SelfAsserted in v1). An empty persona (new user or no row) " +
                "returns 200 with all fields null / empty lists — never 404. The optional " +
                "'actingAs' query parameter is reserved for future delegation; only the literal " +
                "value 'self' is accepted in v1. 403 when the caller's JWT lacks an OrgMembership " +
                "for the requested context.")
            .Produces<PersonaReadModelV1>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/", ReplaceMyPersona)
            .WithName("ReplaceMyPersona")
            .WithSummary("Replace the signed-in user's persona for a context (full replace)")
            .WithDescription(
                "Validates the supplied PersonaAttributesV1 against the invariants in data-model.md " +
                "§2 (list cap of 5, exactly one default per multi-value list, RFC 5322 email, E.164 " +
                "phone, ISO 3166-1 alpha-2 country). Encrypts via the Wallet Service under the " +
                "sorcha:persona-vault purpose and upserts the row keyed by " +
                "(PlatformUserId, ContextOrgId). Omit the 'context' query parameter for the " +
                "Personal context (Feature 125). Returns the canonical read model. 403 when the " +
                "caller's JWT lacks an OrgMembership for the requested context.")
            .Produces<PersonaReadModelV1>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/", DeleteMyPersona)
            .WithName("DeleteMyPersona")
            .WithSummary("Wipe the signed-in user's persona for a context")
            .WithDescription("Hard-deletes the persona row for the requested context. Idempotent " +
                "— returns 204 whether or not a row existed. Omit the 'context' query parameter " +
                "for the Personal context (Feature 125). 403 when the caller's JWT lacks an " +
                "OrgMembership for the requested context.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetMyPersona(
        HttpContext context,
        IPersonaService personaService,
        TenantDbContext db,
        CancellationToken ct,
        [FromQuery(Name = "context")] Guid? contextParam = null,
        string? actingAs = null)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        if (!await CallerHasContextAsync(db, userId, contextParam, ct))
            return ForbiddenContextResult();

        try
        {
            var options = new PersonaReadOptions { ActingAs = actingAs ?? "self" };
            var result = await personaService.GetAsync(userId, contextParam, options, ct);
            return TypedResults.Ok(result);
        }
        catch (PersonaValidationException ex)
        {
            return TypedResults.ValidationProblem(ex.Errors);
        }
    }

    private static async Task<IResult> ReplaceMyPersona(
        HttpContext context,
        IPersonaService personaService,
        IWalletServiceClient walletClient,
        TenantDbContext db,
        PersonaAttributesV1 body,
        CancellationToken ct,
        [FromQuery(Name = "context")] Guid? contextParam = null)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        if (!await CallerHasContextAsync(db, userId, contextParam, ct))
            return ForbiddenContextResult();

        var walletAddress = context.User.FindFirst("wallet_address")?.Value;

        // Fallback: resolve the caller's wallet server-side when the token carries no wallet_address
        // claim. This is the norm for a consumer/citizen — Feature 136 strips wallet_address from
        // consumer-tier tokens — and also happens for a brand-new user seeding their persona in the
        // onboarding step right after the wizard (the wallet exists but the token predates it).
        // Without this the persona save fail-closes with 409 and the profile the user just filled is
        // silently lost. Wallet ownership keying mirrors the Wallet Service's own citizen-context
        // resolver: new wallets are keyed by platform_user_id (post-#878), legacy wallets by
        // sub/NameIdentifier — try both, newest-usable first.
        if (string.IsNullOrEmpty(walletAddress))
        {
            var ownerCandidates = new[]
            {
                context.User.FindFirst("platform_user_id")?.Value,
                context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
            };

            foreach (var ownerId in ownerCandidates)
            {
                if (string.IsNullOrEmpty(ownerId)) continue;
                var wallets = await walletClient.GetWalletsByOwnerAsync(ownerId, ct);
                walletAddress = wallets.FirstOrDefault()?.Address;
                if (!string.IsNullOrEmpty(walletAddress)) break;
            }
        }

        try
        {
            var result = await personaService.ReplaceAsync(userId, walletAddress, body, contextParam, ct);
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
        TenantDbContext db,
        CancellationToken ct,
        [FromQuery(Name = "context")] Guid? contextParam = null)
    {
        var userId = GetUserId(context);
        if (userId == Guid.Empty) return TypedResults.Unauthorized();

        if (!await CallerHasContextAsync(db, userId, contextParam, ct))
            return ForbiddenContextResult();

        await personaService.DeleteAsync(userId, contextParam, ct);
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Returns true if the caller is allowed to operate on a persona for the
    /// requested context. The Personal context (null / empty query param) is
    /// always allowed; any non-empty context must match an OrgMembership the
    /// caller holds.
    /// </summary>
    private static async Task<bool> CallerHasContextAsync(
        TenantDbContext db, Guid platformUserId, Guid? contextOrgId, CancellationToken ct)
    {
        if (contextOrgId is null || contextOrgId == Guid.Empty) return true;
        return await db.PlatformUserOrgMemberships
            .AnyAsync(m => m.PlatformUserId == platformUserId && m.OrganizationId == contextOrgId.Value, ct);
    }

    private static IResult ForbiddenContextResult() =>
        TypedResults.Problem(
            title: "Not a member of the requested context",
            detail: "You do not hold an organisation membership for that context.",
            statusCode: StatusCodes.Status403Forbidden,
            type: "https://sorcha.platform/errors/persona_context_forbidden");

    /// <summary>
    /// Resolves the caller's <see cref="PlatformUser"/> id from the
    /// <c>platform_user_id</c> claim. The <c>sub</c> claim carries the
    /// org-scoped <c>UserIdentity.Id</c>, not the cross-org PlatformUser id,
    /// so it cannot be used as the key for the <c>PlatformUserPersonas</c>
    /// table (FK violation). See <c>TokenService.GenerateUserTokenAsync</c>
    /// for where the <c>platform_user_id</c> claim is emitted.
    /// </summary>
    private static Guid GetUserId(HttpContext context)
    {
        var platformUserId = context.User.FindFirst("platform_user_id")?.Value;
        return Guid.TryParse(platformUserId, out var id) ? id : Guid.Empty;
    }
}
