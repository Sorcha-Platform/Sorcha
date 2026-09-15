// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Sorcha.ServiceClients.Audit;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Reports a refused publish or amend to the caller's organisation audit log (#1648).
/// </summary>
/// <remarks>
/// The governance refusal reason was logged here and nowhere a refused person could read it. During
/// the 2026-09-15 cold-start run the MCP agent could only reach it over SSH. Best-effort: no client
/// registered, no organisation in the token, or a Tenant outage all leave the 403 unchanged.
/// </remarks>
public static class PublishRefusalAudit
{
    /// <summary>Reports the refusal when the caller belongs to an organisation.</summary>
    /// <param name="http">The refused request.</param>
    /// <param name="action">A <see cref="RefusalAuditActions"/> constant.</param>
    /// <param name="registerId">The register the caller targeted.</param>
    /// <param name="reason">Why, in words the caller can act on.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ReportAsync(
        HttpContext http, string action, string registerId, string reason, CancellationToken ct)
    {
        var client = http.RequestServices?.GetService<IRefusalAuditClient>();
        if (client is null)
        {
            return;
        }

        var org = http.User.FindFirstValue("org_id") ?? http.User.FindFirstValue("organization_id");
        if (!Guid.TryParse(org, out var organizationId))
        {
            return;
        }

        var user = http.User.FindFirstValue("platform_user_id")
                   ?? http.User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? http.User.FindFirstValue("sub");
        Guid? platformUserId = Guid.TryParse(user, out var id) && id != Guid.Empty ? id : null;

        await client.RecordAsync(new RefusalAuditReport
        {
            OrganizationId = organizationId,
            PlatformUserId = platformUserId,
            Action = action,
            ResourceType = "register",
            ResourceId = registerId,
            Reason = reason
        }, ct);
    }
}
