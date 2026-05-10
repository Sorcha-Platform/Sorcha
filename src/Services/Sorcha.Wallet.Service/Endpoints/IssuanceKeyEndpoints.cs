// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Sorcha.ServiceDefaults;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Per-org VC issuance key lifecycle endpoints (Feature 120 US2 / T039).
/// </summary>
/// <remarks>
/// Exposes the lazy-derivation entry point so any service that mints credentials
/// can trigger the key + DID document publish without owning the wallet
/// infrastructure directly. Used by Sorcha.Haip.Service's /credential endpoint
/// to honor FR-004 'no later than first issuance' even when minting via the
/// pre-authorized_code flow.
/// </remarks>
public static class IssuanceKeyEndpoints
{
    /// <summary>Maps the issuance key endpoints into the application.</summary>
    public static IEndpointRouteBuilder MapIssuanceKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orgs/{orgId:guid}/issuance-key")
            .WithTags("IssuanceKey")
            .RequireRateLimiting(RateLimitPolicies.Api);

        group.MapPost("/ensure", EnsureIssuanceKey)
            .WithName("EnsureOrgIssuanceKey")
            .WithSummary("Lazily derive (idempotent) the org's VC issuance key")
            .WithDescription(
                "Returns 200 with the active key's metadata after deriving it on first call " +
                "or returning the existing Active row on retry. Triggers DID document " +
                "regeneration on the Tenant side as a side effect. Designed for callers " +
                "that mint credentials outside the direct /credentials/issue path " +
                "(notably Sorcha.Haip.Service's pre-authorized_code flow).")
            .Produces<EnsureIssuanceKeyResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status500InternalServerError);

        return app;
    }

    private static async Task<IResult> EnsureIssuanceKey(
        [FromRoute] Guid orgId,
        [FromServices] IIssuanceKeyService service,
        CancellationToken ct)
    {
        var state = await service.GetOrDeriveAsync(orgId, ct);
        if (state is null)
        {
            // F120 lazy derivation not applicable for this org (no provisioned master key).
            return Results.Ok(new { provisioned = false, organizationId = orgId });
        }
        return Results.Ok(new EnsureIssuanceKeyResponse(
            OrganizationId: state.OrganizationId,
            RotationIndex: state.RotationIndex,
            Algorithm: state.Algorithm,
            Thumbprint: state.Thumbprint,
            DerivedAt: state.DerivedAt));
    }
}

/// <summary>Response shape for <c>POST /api/v1/orgs/{orgId}/issuance-key/ensure</c>.</summary>
public sealed record EnsureIssuanceKeyResponse(
    Guid OrganizationId,
    int RotationIndex,
    string Algorithm,
    string Thumbprint,
    DateTimeOffset DerivedAt);
