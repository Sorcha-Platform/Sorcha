// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Sorcha.ServiceClients.Register;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Dtos;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Aggregates admin dashboard statistics — user counts, role distribution,
/// recent logins, invitation stats, and IDP configuration status.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly TenantDbContext _dbContext;
    private readonly IRegisterServiceClient _registerClient;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        TenantDbContext dbContext,
        IRegisterServiceClient registerClient,
        ILogger<DashboardService> logger)
    {
        _dbContext = dbContext;
        _registerClient = registerClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardResponse> GetDashboardAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var users = await _dbContext.UserIdentities
            .Where(u => u.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        var activeCount = users.Count(u => u.Status == IdentityStatus.Active);
        var suspendedCount = users.Count(u => u.Status == IdentityStatus.Suspended);

        // Role breakdown — flatten the Roles array across all active users
        var usersByRole = users
            .Where(u => u.Status == IdentityStatus.Active)
            .SelectMany(u => u.Roles)
            .GroupBy(r => r.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Recent logins — last 10 users who logged in, sorted by most recent
        var recentLogins = users
            .Where(u => u.LastLoginAt.HasValue)
            .OrderByDescending(u => u.LastLoginAt)
            .Take(10)
            .Select(u => new RecentLoginEntry
            {
                UserId = u.Id,
                DisplayName = u.DisplayName,
                Timestamp = u.LastLoginAt!.Value,
                Method = u.ProvisionedVia == ProvisioningMethod.Oidc ? "OIDC" : "Local"
            })
            .ToList();

        // Pending invitations
        var pendingInvitationCount = await _dbContext.OrgInvitations
            .CountAsync(i => i.OrganizationId == organizationId
                          && i.Status == InvitationStatus.Pending
                          && i.ExpiresAt > DateTimeOffset.UtcNow,
                cancellationToken);

        // IDP status
        var idpConfig = await _dbContext.IdentityProviderConfigurations
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId, cancellationToken);

        var lastIdpLogin = users
            .Where(u => u.ProvisionedVia == ProvisioningMethod.Oidc && u.LastLoginAt.HasValue)
            .OrderByDescending(u => u.LastLoginAt)
            .Select(u => u.LastLoginAt)
            .FirstOrDefault();

        var idpStatus = new IdpStatusInfo
        {
            Configured = idpConfig != null,
            Enabled = idpConfig?.IsEnabled ?? false,
            ProviderName = idpConfig?.DisplayName,
            LastLoginViaIdp = lastIdpLogin
        };

        return new DashboardResponse
        {
            ActiveUserCount = activeCount,
            SuspendedUserCount = suspendedCount,
            UsersByRole = usersByRole,
            RecentLogins = recentLogins,
            PendingInvitationCount = pendingInvitationCount,
            IdpStatus = idpStatus
        };
    }

    /// <inheritdoc />
    public async Task<OrgSummaryResponse> GetOrgSummaryAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        // Active users in org.
        var activeUsers = await _dbContext.UserIdentities
            .Where(u => u.OrganizationId == organizationId && u.Status == IdentityStatus.Active)
            .CountAsync(cancellationToken);

        // Pending invitations (unexpired).
        var now = DateTimeOffset.UtcNow;
        var pendingInvitations = await _dbContext.OrgInvitations
            .Where(i => i.OrganizationId == organizationId
                     && i.Status == InvitationStatus.Pending
                     && i.ExpiresAt > now)
            .CountAsync(cancellationToken);

        // Subscribed registers (Status=Active). Capture the ids so we can ask
        // Register Service for the transaction sum in one round-trip.
        var subscribedRegisterIds = await _dbContext.OrganizationRegisterSubscriptions
            .Where(s => s.OrganizationId == organizationId && s.Status == SubscriptionStatus.Active)
            .Select(s => s.RegisterId)
            .ToListAsync(cancellationToken);

        // Recent transactions across the org's subscribed registers. Best-effort —
        // a Register Service outage downgrades to 0 with a warning rather than
        // failing the whole summary (the other three numbers are still useful).
        var recentTransactions = 0;
        if (subscribedRegisterIds.Count > 0)
        {
            try
            {
                var stats = await _registerClient.GetStatsAsync(subscribedRegisterIds, cancellationToken);
                recentTransactions = stats.TransactionCount;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Register stats fetch failed for org {OrgId}; recentTransactions defaults to 0",
                    organizationId);
            }
        }

        return new OrgSummaryResponse
        {
            OrgId = organizationId,
            ActiveUsers = activeUsers,
            PendingInvitations = pendingInvitations,
            SubscribedRegisters = subscribedRegisterIds.Count,
            RecentTransactions = recentTransactions,
            Timestamp = DateTimeOffset.UtcNow
        };
    }
}
