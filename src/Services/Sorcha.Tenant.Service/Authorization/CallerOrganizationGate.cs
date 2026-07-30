// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Auth;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Authorization;

/// <summary>
/// Marker metadata stamped on every endpoint carrying the caller-organisation gate, so a test can
/// assert the gate is wired to each org-scoped route. An endpoint filter is invisible to route
/// metadata, which would otherwise make "the new org-scoped group forgot the gate" undetectable —
/// the exact failure mode that produced this defect across six groups at once.
/// </summary>
public sealed class CallerOrganizationRequiredMetadata
{
    /// <summary>Singleton instance; the metadata carries no state.</summary>
    public static readonly CallerOrganizationRequiredMetadata Instance = new();
}

/// <summary>
/// Binds a request to the organisation named in its route.
///
/// <para>B2+ (catch-up security review 2026-07-29): the org-scoped groups were gated on
/// <c>RequireAdministrator</c> + <c>RequirePlatformAudience</c> — a ROLE and TIER check only.
/// <c>RequireAdministrator</c> is literally <c>RequireRole("SystemAdmin", "Administrator")</c> and
/// never inspects <c>org_id</c>, and no handler compared the caller's organisation to the route. So
/// an Administrator of org A could operate on org B: read its audit log, alter its custom domain and
/// domain restrictions, read its dashboard, and manage its invitations — where a resend ROTATES the
/// invitation token and emails the invitee, i.e. a write with an outbound side effect.</para>
///
/// <para>Confirmed empirically before this gate was written: a plain <c>Administrator</c> of one
/// organisation reached four other organisations' routes with HTTP 200.</para>
/// </summary>
public static class CallerOrganizationGate
{
    /// <summary>Route-value names that carry an organisation id, in probe order.</summary>
    private static readonly string[] RouteValueNames = ["organizationId", "orgId"];

    /// <summary>
    /// Decides whether the caller may act on the organisation named in the route.
    /// Returns <c>null</c> to allow the request through, or the <see cref="IResult"/> to
    /// short-circuit with.
    /// </summary>
    public static IResult? Evaluate(HttpContext http)
    {
        if (!TryResolveRouteOrganizationId(http, out var routeOrgId))
        {
            // Applied to a route with no organisation id in its template. Fail closed: silently
            // allowing would make a mis-wiring look like a working control.
            Logger(http).LogError(
                "Caller-organisation gate is applied to {Path} but no organisation-id route value "
                + "was found. Refusing the request — check the route template.", http.Request.Path);

            return Results.Problem(
                title: "Caller organisation could not be determined",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        // Internal service-to-service callers are not org-scoped.
        if (IsServiceToken(http.User))
        {
            return null;
        }

        // Platform-wide administration is a real, intended capability — the seeded
        // admin@sorcha.local reads other organisations deliberately, and this was verified live
        // before the gate was added. Mirrors the RequireSystemAdmin policy's own test
        // (SystemAdmin role AND membership of the system-admin org), so a SystemAdmin role claim
        // alone — in some other org — does NOT buy cross-org access.
        if (IsPlatformSystemAdmin(http.User))
        {
            return null;
        }

        var callerOrgId = http.User.FindFirstValue(TokenClaimConstants.OrgId)
            ?? http.User.FindFirstValue("organization_id");

        if (string.IsNullOrWhiteSpace(callerOrgId))
        {
            return Results.Forbid();
        }

        if (Guid.TryParse(callerOrgId, out var callerOrg) && callerOrg == routeOrgId)
        {
            return null;
        }

        Logger(http).LogWarning(
            "SEC-AUDIT: caller in organisation {CallerOrg} attempted {Method} {Path} scoped to "
            + "organisation {RouteOrg}",
            callerOrgId, http.Request.Method, http.Request.Path, routeOrgId);

        return Results.Forbid();
    }

    private static bool TryResolveRouteOrganizationId(HttpContext http, out Guid organizationId)
    {
        foreach (var name in RouteValueNames)
        {
            if (http.Request.RouteValues.TryGetValue(name, out var value)
                && Guid.TryParse(value?.ToString(), out organizationId))
            {
                return true;
            }
        }

        organizationId = Guid.Empty;
        return false;
    }

    private static bool IsServiceToken(ClaimsPrincipal user) =>
        user.Claims.Any(c =>
            c.Type == TokenClaimConstants.TokenType
            && c.Value == TokenClaimConstants.TokenTypeService);

    private static bool IsPlatformSystemAdmin(ClaimsPrincipal user)
    {
        if (!user.IsInRole("SystemAdmin")) return false;

        var orgId = user.FindFirstValue(TokenClaimConstants.OrgId)
            ?? user.FindFirstValue("organization_id");

        return Guid.TryParse(orgId, out var org) && org == WellKnownIds.SystemAdminOrgId;
    }

    private static ILogger Logger(HttpContext http) =>
        http.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(CallerOrganizationGate).FullName!);
}

/// <summary>Endpoint-builder extensions for the caller-organisation gate.</summary>
public static class CallerOrganizationEndpointExtensions
{
    /// <summary>
    /// Requires that the caller belongs to the organisation named in the route (or is a service
    /// principal, or a platform SystemAdmin). Apply to any route group whose template contains an
    /// organisation id.
    /// </summary>
    /// <remarks>
    /// Composes with — and does not replace — the group's authorization policy.
    /// <c>RequireAdministrator</c> establishes that the caller is an administrator at all; this
    /// establishes <em>whose</em> organisation they may administer.
    /// </remarks>
    public static TBuilder RequireCallerOrganization<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AddEndpointFilter(async (context, next) =>
            CallerOrganizationGate.Evaluate(context.HttpContext) ?? await next(context));

        return builder.WithMetadata(CallerOrganizationRequiredMetadata.Instance);
    }
}
