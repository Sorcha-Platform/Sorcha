// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Sorcha.ServiceClients.Audit;
using Sorcha.ServiceClients.Auth;

namespace Sorcha.Wallet.Service.Authorization;

/// <summary>
/// Reports a refused wallet request to the caller's organisation audit log (#1648).
/// </summary>
/// <remarks>
/// A refused person cannot read the Wallet Service's logs, and an MCP agent acting for them has no
/// logs at all, so the <c>SEC-AUDIT</c> line alone told nobody who could act on it. That is exactly how
/// the 2026-09-15 cold-start run ended. The report is best-effort: no client registered, no
/// organisation in the token, or a Tenant outage all leave the refusal itself unchanged.
/// </remarks>
public static class RefusalAudit
{
    /// <summary>Reports the refusal when the caller belongs to an organisation.</summary>
    /// <param name="http">The refused request.</param>
    /// <param name="action">A <see cref="RefusalAuditActions"/> constant.</param>
    /// <param name="walletAddress">The wallet the caller tried to act on.</param>
    /// <param name="reason">Why, in words the caller can act on.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ReportAsync(
        HttpContext http, string action, string walletAddress, string reason, CancellationToken ct)
    {
        var client = http.RequestServices?.GetService<IRefusalAuditClient>();
        if (client is null)
        {
            return;
        }

        var org = http.User.FindFirstValue(TokenClaimConstants.OrgId) ?? http.User.FindFirstValue("organization_id");
        if (!Guid.TryParse(org, out var organizationId))
        {
            // Nowhere to record it: the audit log is per organisation.
            return;
        }

        Guid? platformUserId = Guid.TryParse(
            http.User.FindFirstValue(TokenClaimConstants.PlatformUserId), out var id) ? id : null;

        await client.RecordAsync(new RefusalAuditReport
        {
            OrganizationId = organizationId,
            PlatformUserId = platformUserId,
            Action = action,
            ResourceType = "wallet",
            ResourceId = walletAddress,
            Reason = reason
        }, ct);
    }
}
