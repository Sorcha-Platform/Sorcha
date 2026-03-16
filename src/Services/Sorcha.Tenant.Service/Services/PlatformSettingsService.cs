// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Implementation of <see cref="IPlatformSettingsService"/> that manages the singleton
/// platform settings record and atomically updates the public organisation state.
/// </summary>
public class PlatformSettingsService : IPlatformSettingsService
{
    private readonly TenantDbContext _dbContext;
    private readonly ILogger<PlatformSettingsService> _logger;

    /// <summary>
    /// Creates a new <see cref="PlatformSettingsService"/> instance.
    /// </summary>
    /// <param name="dbContext">The tenant database context.</param>
    /// <param name="logger">Logger instance.</param>
    public PlatformSettingsService(TenantDbContext dbContext, ILogger<PlatformSettingsService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await _dbContext.PlatformSettings.FirstOrDefaultAsync(ct);

        if (settings is null)
        {
            throw new InvalidOperationException("Platform settings not initialized. Run bootstrap first.");
        }

        return settings;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings> UpdatePublicOrgEnabledAsync(bool enabled, Guid updatedBy, CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);

        settings.PublicOrgEnabled = enabled;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = updatedBy;

        // Atomically update the public organisation status and self-registration flag
        var publicOrg = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == WellKnownIds.PublicOrgId, ct);

        if (publicOrg is not null)
        {
            publicOrg.Status = enabled ? OrganizationStatus.Active : OrganizationStatus.Suspended;
            publicOrg.SelfRegistrationEnabled = enabled;
        }
        else
        {
            _logger.LogWarning("Public organisation {PublicOrgId} not found — skipping org status update", WellKnownIds.PublicOrgId);
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Platform settings updated: PublicOrgEnabled={Enabled} by {UpdatedBy}",
            enabled,
            updatedBy);

        return settings;
    }

    /// <inheritdoc />
    public async Task<PlatformSettings> UpdateMaxOrgsPerUserAsync(int maxOrgs, Guid updatedBy, CancellationToken ct = default)
    {
        if (maxOrgs is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxOrgs), maxOrgs, "MaxOrgsPerUser must be between 1 and 100.");
        }

        var settings = await GetAsync(ct);

        settings.MaxOrgsPerUser = maxOrgs;
        settings.UpdatedAt = DateTimeOffset.UtcNow;
        settings.UpdatedBy = updatedBy;

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Platform settings updated: MaxOrgsPerUser={MaxOrgs} by {UpdatedBy}",
            maxOrgs,
            updatedBy);

        return settings;
    }
}

/// <summary>
/// Well-known identifiers for platform-level entities created during bootstrap.
/// </summary>
public static class WellKnownIds
{
    /// <summary>
    /// ID of the System Admin organisation (created during bootstrap).
    /// </summary>
    public static readonly Guid SystemAdminOrgId = new("00000000-0000-0000-0000-000000000001");

    /// <summary>
    /// ID of the Public organisation (created during bootstrap).
    /// </summary>
    public static readonly Guid PublicOrgId = new("00000000-0000-0000-0000-000000000002");

    /// <summary>
    /// ID of the default system administrator user (created during bootstrap).
    /// </summary>
    public static readonly Guid DefaultAdminUserId = new("00000000-0000-0001-0000-000000000001");
}
