// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Audit;

namespace Sorcha.Register.Service.Authorization;

/// <summary>
/// Endpoint metadata that opts the system blueprint publish endpoint into refusal auditing of its
/// authorisation-policy 403s. Deliberately system-blueprint specific: the handler hard-codes this endpoint's
/// reason, resource type and <c>blueprintId</c> route key, so it is named for what it serves.
/// Apply with <c>.WithMetadata(new SystemBlueprintPublishAuditMetadata(action))</c>.
/// </summary>
/// <param name="Action">A <see cref="RefusalAuditActions"/> constant naming what was attempted.</param>
public sealed record SystemBlueprintPublishAuditMetadata(string Action);

/// <summary>
/// Reports an authorisation-policy refusal (403) to the caller's organisation audit log for endpoints
/// carrying <see cref="SystemBlueprintPublishAuditMetadata"/>, then defers to the default handler (#1648, SC-004).
/// </summary>
/// <remarks>
/// A policy 403 never reaches the endpoint handler, so the handler cannot audit it itself. Challenges (401)
/// are not audited, and endpoints without the metadata are untouched. Reporting is best-effort and never
/// changes the response.
/// </remarks>
public sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    /// <summary>Reason recorded for a refused system blueprint publish.</summary>
    public const string RefusalReason = "requires a SystemAdmin on a platform-tier session";

    private readonly AuthorizationMiddlewareResultHandler _default = new();

    /// <inheritdoc />
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && context.GetEndpoint()?.Metadata.GetMetadata<SystemBlueprintPublishAuditMetadata>() is { } marker)
        {
            SystemBlueprintMetrics.RecordPublish(PublishOutcomeNames.RefusedAuth);
            await SystemBlueprintRefusalAudit.ReportAsync(
                context, marker.Action, context.GetRouteValue("blueprintId") as string, RefusalReason);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>The one place an operator's id is read from a token: <c>platform_user_id</c>, then <c>sub</c>, then the name identifier.</summary>
public static class OperatorIdentity
{
    /// <summary>Returns the caller's operator id, or null when the token carries none.</summary>
    /// <param name="user">The caller.</param>
    public static string? Resolve(ClaimsPrincipal user)
        => user.FindFirstValue("platform_user_id")
           ?? user.FindFirstValue("sub")
           ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
}

/// <summary>Best-effort refusal reporting for the system blueprint operator endpoints (#1648 pattern).</summary>
public static class SystemBlueprintRefusalAudit
{
    /// <summary>
    /// Reports a refusal when the caller belongs to an organisation. Never throws: no client registered,
    /// no organisation in the token, or a Tenant outage all leave the response unchanged.
    /// </summary>
    /// <param name="http">The refused request.</param>
    /// <param name="action">A <see cref="RefusalAuditActions"/> constant.</param>
    /// <param name="blueprintId">The blueprint the caller targeted.</param>
    /// <param name="reason">Why, in words the caller can act on.</param>
    public static async Task ReportAsync(HttpContext http, string action, string? blueprintId, string reason)
    {
        try
        {
            var client = http.RequestServices.GetService<IRefusalAuditClient>();
            if (client is null)
            {
                return;
            }

            var org = http.User.FindFirstValue("org_id") ?? http.User.FindFirstValue("organization_id");
            if (!Guid.TryParse(org, out var organizationId))
            {
                return;
            }

            Guid? platformUserId = Guid.TryParse(OperatorIdentity.Resolve(http.User), out var id) && id != Guid.Empty ? id : null;

            await client.RecordAsync(new RefusalAuditReport
            {
                OrganizationId = organizationId,
                PlatformUserId = platformUserId,
                Action = action,
                ResourceType = "system-blueprint",
                ResourceId = blueprintId,
                Reason = reason
            }, http.RequestAborted);
        }
        catch (Exception ex)
        {
            http.RequestServices.GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(SystemBlueprintRefusalAudit))
                .LogWarning(ex, "Refusal audit for {Action} could not be reported", action);
        }
    }
}
