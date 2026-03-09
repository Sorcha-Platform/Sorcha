// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Organization settings endpoints for managing org type, self-registration, and audit retention.
/// </summary>
public static class OrgSettingsEndpoints
{
    /// <summary>
    /// Maps organization settings endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapOrgSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{orgId:guid}/settings")
            .WithTags("Organization Settings")
            .RequireAuthorization("RequireAdministrator");

        group.MapGet("/", GetSettings)
            .WithName("GetOrgSettings")
            .WithSummary("Get organization settings")
            .WithDescription("Returns org type, self-registration status, allowed email domains, and audit retention.")
            .Produces<OrgSettingsResponse>();

        group.MapPut("/", UpdateSettings)
            .WithName("UpdateOrgSettings")
            .WithSummary("Update organization settings")
            .WithDescription("Updates self-registration status and audit retention period (1-120 months).")
            .Produces<OrgSettingsResponse>()
            .ProducesValidationProblem();

        return app;
    }

    /// <summary>
    /// GET /api/organizations/{orgId}/settings — returns current org settings.
    /// </summary>
    private static async Task<IResult> GetSettings(
        Guid orgId,
        TenantDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);

        if (org is null)
            return TypedResults.NotFound();

        return TypedResults.Ok(new OrgSettingsResponse
        {
            OrgType = org.OrgType.ToString(),
            SelfRegistrationEnabled = org.SelfRegistrationEnabled,
            AllowedEmailDomains = org.AllowedEmailDomains ?? [],
            AuditRetentionMonths = org.AuditRetentionMonths
        });
    }

    /// <summary>
    /// PUT /api/organizations/{orgId}/settings — updates org settings.
    /// </summary>
    private static async Task<IResult> UpdateSettings(
        Guid orgId,
        OrgSettingsRequest request,
        TenantDbContext dbContext,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        var org = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == orgId, cancellationToken);

        if (org is null)
            return TypedResults.NotFound();

        // Validate audit retention months
        if (request.AuditRetentionMonths.HasValue
            && (request.AuditRetentionMonths < 1 || request.AuditRetentionMonths > 120))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["auditRetentionMonths"] = ["Audit retention must be between 1 and 120 months"]
            });
        }

        if (request.SelfRegistrationEnabled.HasValue)
            org.SelfRegistrationEnabled = request.SelfRegistrationEnabled.Value;

        if (request.AuditRetentionMonths.HasValue)
            org.AuditRetentionMonths = request.AuditRetentionMonths.Value;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Organization settings updated for {OrgId}: SelfRegistration={SelfReg}, AuditRetention={Retention}m",
            orgId, org.SelfRegistrationEnabled, org.AuditRetentionMonths);

        return TypedResults.Ok(new OrgSettingsResponse
        {
            OrgType = org.OrgType.ToString(),
            SelfRegistrationEnabled = org.SelfRegistrationEnabled,
            AllowedEmailDomains = org.AllowedEmailDomains ?? [],
            AuditRetentionMonths = org.AuditRetentionMonths
        });
    }
}
