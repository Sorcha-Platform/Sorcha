// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Sorcha.ServiceClients.Audit;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// #1648 — lets a service record a refusal in the refused caller's organisation audit log.
/// </summary>
/// <remarks>
/// <para>
/// Refusals that matter to a person happen in other services: the Wallet Service refusing a
/// signature, the Blueprint Service refusing a publish. They were only ever logged there, so an MCP
/// agent (which has no log access) could never learn why it was refused. That is how the
/// 2026-09-15 cold-start run ended. Written here, they are readable through the existing
/// <c>GET /api/organizations/{id}/audit</c>.
/// </para>
/// <para>
/// The entry records the writer from the service TOKEN, never from the body, and is always
/// <see cref="AuditEventType.PermissionDenied"/> with <c>Success = false</c>: a writer can add
/// refusals, but cannot forge a record that something was allowed.
/// </para>
/// </remarks>
public static class InternalRefusalAuditEndpoints
{
    /// <summary>Maps <c>POST /api/internal/audit/refusals</c> behind the RequireService policy.</summary>
    public static IEndpointRouteBuilder MapInternalRefusalAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/internal/audit/refusals", RecordAsync)
            .RequireAuthorization("RequireService")
            .WithName("RecordRefusalAudit")
            .WithTags("Internal", "Audit Log")
            .WithSummary("Record a refusal in the refused caller's organisation audit log")
            .WithDescription("Called by services when they refuse a person's request (for example a wallet "
                + "signature or a blueprint publish). Writes a PermissionDenied entry, attributed to the "
                + "calling service from its token, readable via GET /api/organizations/{id}/audit.")
            .ExcludeFromDescription();

        return app;
    }

    private static async Task<IResult> RecordAsync(
        HttpContext context,
        RefusalAuditReport? report,
        TenantDbContext dbContext,
        CancellationToken ct)
    {
        if (report is null)
        {
            return Results.BadRequest(new { error = "request body required" });
        }

        if (Invalid(report) is { } problem)
        {
            return Results.BadRequest(new { error = problem });
        }

        // A best-effort write must stay a caller error when the caller is wrong (#1506's lesson): an
        // unknown organisation is a 404, never a 500 that reads as a platform fault in the writer.
        if (!await dbContext.Organizations.AnyAsync(o => o.Id == report.OrganizationId, ct))
        {
            return Results.NotFound(new { error = "organizationId does not name a known organisation" });
        }

        var writer = context.User.FindFirstValue("client_id")
                     ?? context.User.FindFirstValue("service_name")
                     ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? "unknown-service";

        var details = new Dictionary<string, object>
        {
            ["source"] = "service-refusal",
            ["service"] = writer,
            ["action"] = report.Action.Trim(),
            ["reason"] = report.Reason.Trim()
        };
        if (!string.IsNullOrWhiteSpace(report.ResourceType))
        {
            details["resourceType"] = report.ResourceType.Trim();
        }
        if (!string.IsNullOrWhiteSpace(report.ResourceId))
        {
            details["resourceId"] = report.ResourceId.Trim();
        }
        if (report.PlatformUserId is { } platformUserId)
        {
            details["platformUserId"] = platformUserId.ToString();
        }

        dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            EventType = AuditEventType.PermissionDenied,
            Success = false,
            OrganizationId = report.OrganizationId,
            IdentityId = report.PlatformUserId,
            Timestamp = report.OccurredAt ?? DateTimeOffset.UtcNow,
            UserAgent = writer,
            Details = details
        });
        await dbContext.SaveChangesAsync(ct);

        return Results.Accepted();
    }

    private static string? Invalid(RefusalAuditReport report)
    {
        if (report.OrganizationId == Guid.Empty)
            return "organizationId required";
        if (string.IsNullOrWhiteSpace(report.Action) || report.Action.Length > RefusalAuditReport.MaxActionLength)
            return $"action required, at most {RefusalAuditReport.MaxActionLength} characters";
        if (string.IsNullOrWhiteSpace(report.Reason) || report.Reason.Length > RefusalAuditReport.MaxReasonLength)
            return $"reason required, at most {RefusalAuditReport.MaxReasonLength} characters";
        if (report.ResourceType is { Length: > RefusalAuditReport.MaxResourceTypeLength })
            return $"resourceType at most {RefusalAuditReport.MaxResourceTypeLength} characters";
        if (report.ResourceId is { Length: > RefusalAuditReport.MaxResourceIdLength })
            return $"resourceId at most {RefusalAuditReport.MaxResourceIdLength} characters";
        return null;
    }
}
