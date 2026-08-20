// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Endpoints;

/// <summary>
/// Platform settings endpoints for system admin configuration.
/// Controls public org availability and org creation limits.
/// </summary>
public static class PlatformSettingsEndpoints
{
    /// <summary>
    /// Maps platform settings endpoints to the application.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/platform/settings")
            .WithTags("Platform Settings")
            .RequireAuthorization("RequireSystemAdmin", "RequirePlatformAudience");

        group.MapGet("/", GetPlatformSettings)
            .WithName("GetPlatformSettings")
            .WithSummary("Get platform settings")
            .WithDescription("Returns platform configuration including public org status and max orgs per user. Requires SystemAdmin role in the system admin organisation.")
            .Produces<PlatformSettingsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/public-org", UpdatePublicOrgSettings)
            .WithName("UpdatePublicOrgSettings")
            .WithSummary("Enable or disable the public organisation")
            .WithDescription("Atomically toggles the public org status and self-registration setting. When enabled, sets Organization.Status = Active and SelfRegistrationEnabled = true. When disabled, sets Organization.Status = Suspended and SelfRegistrationEnabled = false.")
            .Produces<PlatformSettingsResponse>()
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPut("/max-orgs", UpdateMaxOrgsPerUser)
            .WithName("UpdateMaxOrgsPerUser")
            .WithSummary("Update maximum orgs per user setting")
            .WithDescription("Updates the maximum number of private organisations a single user can create. Value must be between 1 and 100.")
            .Produces<PlatformSettingsResponse>()
            .ProducesValidationProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    /// <summary>
    /// Gets the current platform settings including public org status.
    /// </summary>
    private static async Task<Ok<PlatformSettingsResponse>> GetPlatformSettings(
        IPlatformSettingsService settingsService,
        TenantDbContext dbContext,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var publicOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, ct);

        return TypedResults.Ok(MapToResponse(settings, publicOrg));
    }

    /// <summary>
    /// Enables or disables the public organisation (atomically updates org status + self-registration).
    /// </summary>
    private static async Task<Ok<PlatformSettingsResponse>> UpdatePublicOrgSettings(
        [FromBody] UpdatePublicOrgRequest request,
        IPlatformSettingsService settingsService,
        TenantDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var userId = GetUserId(httpContext);
        var result = await settingsService.UpdatePublicOrgEnabledAsync(request.Enabled, userId, ct);
        var publicOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, ct);

        var response = MapToResponse(result.Settings, publicOrg);
        response.PublicOrgWalletAddress = result.WalletAddress;

        // #1530 — present ONLY on the enable that first created the wallet. Shown once, stored
        // nowhere, impossible to reissue: this response is the only moment it exists outside the
        // wallet itself, and the administrator reading it is the only person who will ever hold it.
        response.PublicOrgRecoveryPhrase = result.MnemonicWords;

        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Updates the maximum number of private orgs a user can create.
    /// </summary>
    private static async Task<Results<Ok<PlatformSettingsResponse>, ValidationProblem>> UpdateMaxOrgsPerUser(
        [FromBody] UpdateMaxOrgsRequest request,
        IPlatformSettingsService settingsService,
        TenantDbContext dbContext,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (request.MaxOrgsPerUser is < 1 or > 100)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["maxOrgsPerUser"] = ["MaxOrgsPerUser must be between 1 and 100."]
            });
        }

        var userId = GetUserId(httpContext);
        var settings = await settingsService.UpdateMaxOrgsPerUserAsync(request.MaxOrgsPerUser, userId, ct);
        var publicOrg = await dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, ct);

        return TypedResults.Ok(MapToResponse(settings, publicOrg));
    }

    private static PlatformSettingsResponse MapToResponse(PlatformSettings settings, Organization? publicOrg)
    {
        return new PlatformSettingsResponse
        {
            PublicOrgEnabled = settings.PublicOrgEnabled,
            MaxOrgsPerUser = settings.MaxOrgsPerUser,
            PublicOrgId = WellKnownIds.PublicOrgId,
            PublicOrgStatus = publicOrg?.Status.ToString() ?? "Unknown",
            UpdatedAt = settings.UpdatedAt
        };
    }

    private static Guid GetUserId(HttpContext httpContext)
    {
        var sub = httpContext.User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : Guid.Empty;
    }
}
