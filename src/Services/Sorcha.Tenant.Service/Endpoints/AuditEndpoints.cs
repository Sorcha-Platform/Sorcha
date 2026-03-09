// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Endpoints for querying the audit log and managing retention configuration.
/// </summary>
public static class AuditEndpoints
{
    /// <summary>
    /// Maps audit endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/audit")
            .WithTags("Audit Log");

        group.MapGet("/", GetAuditEvents)
            .WithName("GetAuditEvents")
            .WithSummary("Query audit log events")
            .WithDescription("Returns paginated audit events with optional filtering by date range, event type, and user. Max page size is 200.")
            .RequireAuthorization("RequireAuditor")
            .Produces<AuditLogResponse>()
            .Produces(404);

        group.MapGet("/retention", GetRetention)
            .WithName("GetAuditRetention")
            .WithSummary("Get audit retention configuration")
            .WithDescription("Returns the current audit log retention period in months for this organization.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<AuditRetentionResponse>()
            .Produces(404);

        group.MapPut("/retention", UpdateRetention)
            .WithName("UpdateAuditRetention")
            .WithSummary("Update audit retention period")
            .WithDescription("Sets the audit log retention period (1-120 months). Events older than this period are automatically purged daily.")
            .RequireAuthorization("RequireAdministrator")
            .Produces<AuditRetentionResponse>()
            .Produces(404)
            .ProducesValidationProblem();

        return app;
    }

    /// <summary>
    /// GET /api/organizations/{organizationId}/audit — query audit events with filtering and pagination.
    /// </summary>
    private static async Task<IResult> GetAuditEvents(
        Guid organizationId,
        TenantDbContext dbContext,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        string? eventType,
        Guid? userId,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Verify org exists
        var orgExists = await dbContext.Organizations
            .AnyAsync(o => o.Id == organizationId, cancellationToken);

        if (!orgExists)
            return TypedResults.NotFound();

        // Clamp page size
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);

        // Build query
        var query = dbContext.AuditLogEntries
            .Where(a => a.OrganizationId == organizationId);

        if (startDate.HasValue)
            query = query.Where(a => a.Timestamp >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(a => a.Timestamp <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(eventType)
            && Enum.TryParse<AuditEventType>(eventType, ignoreCase: true, out var parsedType))
        {
            query = query.Where(a => a.EventType == parsedType);
        }

        if (userId.HasValue)
            query = query.Where(a => a.IdentityId == userId.Value);

        // Get total count before pagination
        var totalCount = await query.CountAsync(cancellationToken);

        // Paginate and project
        var events = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => AuditEventResponse.FromEntity(a))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new AuditLogResponse
        {
            Events = events,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// GET /api/organizations/{organizationId}/audit/retention — get current retention setting.
    /// </summary>
    private static async Task<IResult> GetRetention(
        Guid organizationId,
        TenantDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (org is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new AuditRetentionResponse
        {
            RetentionMonths = org.AuditRetentionMonths
        });
    }

    /// <summary>
    /// PUT /api/organizations/{organizationId}/audit/retention — update retention period.
    /// </summary>
    private static async Task<IResult> UpdateRetention(
        Guid organizationId,
        UpdateAuditRetentionRequest request,
        TenantDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (request.RetentionMonths < 1 || request.RetentionMonths > 120)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["retentionMonths"] = ["Audit retention must be between 1 and 120 months."]
            });
        }

        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (org is null)
            return TypedResults.NotFound();

        var previousRetention = org.AuditRetentionMonths;
        org.AuditRetentionMonths = request.RetentionMonths;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Audit retention updated for org {OrgId}: {Previous}m → {New}m",
            organizationId, previousRetention, request.RetentionMonths);

        return TypedResults.Ok(new AuditRetentionResponse
        {
            RetentionMonths = org.AuditRetentionMonths
        });
    }
}
