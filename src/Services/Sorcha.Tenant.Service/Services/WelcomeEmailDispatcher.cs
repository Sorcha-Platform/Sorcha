// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <inheritdoc />
public sealed class WelcomeEmailDispatcher : IWelcomeEmailDispatcher
{
    private readonly TenantDbContext _dbContext;
    private readonly ITransactionalEmailService _transactional;
    private readonly ILogger<WelcomeEmailDispatcher> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="WelcomeEmailDispatcher"/>.
    /// </summary>
    public WelcomeEmailDispatcher(
        TenantDbContext dbContext,
        ITransactionalEmailService transactional,
        ILogger<WelcomeEmailDispatcher> logger)
    {
        _dbContext = dbContext;
        _transactional = transactional;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendIfPendingAsync(PlatformUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        // Fast-path gate: if we can already tell from the in-memory entity that the
        // welcome was sent or the user isn't verified, skip without touching the DB.
        if (user.WelcomeSentAt.HasValue) return;
        if (!user.EmailVerified) return;

        WelcomeDispatchContext context;
        try
        {
            context = await BuildContextAsync(user, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to build welcome context for user {UserId}; welcome not sent",
                user.Id);
            return;
        }

        // Atomic reservation: set WelcomeSentAt only where it's still null and the user
        // is still email-verified. Concurrent triggers race here — the losing writer
        // affects 0 rows and returns without sending. This replaces the previous
        // in-memory check + SaveChangesAsync pattern, which had three problems: a TOCTOU
        // race between the gate and the save, side-effect saves of unrelated dirty
        // entities tracked on the shared scoped DbContext, and data loss when the
        // cancellation token tripped after send but before save.
        var sentAt = DateTimeOffset.UtcNow;
        int rowsReserved;
        try
        {
            rowsReserved = await _dbContext.PlatformUsers
                .Where(u => u.Id == user.Id
                            && u.WelcomeSentAt == null
                            && u.EmailVerified)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(u => u.WelcomeSentAt, sentAt),
                    ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to reserve welcome-send for user {UserId}; welcome not sent",
                user.Id);
            return;
        }

        if (rowsReserved == 0)
        {
            // Another trigger won the race (or the user is no longer verified). No send.
            return;
        }

        // Keep the in-memory entity in sync so the calling scope sees the new value
        // without re-querying.
        user.WelcomeSentAt = sentAt;

        try
        {
            await _transactional.SendWelcomeAsync(context, ct);
        }
        catch (Exception ex)
        {
            // Reservation already committed; the email didn't go out. Log and move on —
            // a stale WelcomeSentAt is preferable to either blocking the caller or
            // double-sending on the next trigger. Operators can reconcile via the
            // structured log line below.
            _logger.LogError(ex,
                "Welcome-send reserved but delivery FAILED for {Email} (user {UserId}, variant {Variant}); " +
                "WelcomeSentAt persisted to prevent double-send. Reconcile if needed.",
                user.Email, user.Id, context.Variant);
            return;
        }

        _logger.LogInformation(
            "Welcome email sent to {Email} (user {UserId}, variant {Variant})",
            user.Email, user.Id, context.Variant);
    }

    private async Task<WelcomeDispatchContext> BuildContextAsync(PlatformUser user, CancellationToken ct)
    {
        // Pull memberships directly — callers aren't required to Include the navigation,
        // and mutating the navigation on a tracked entity is fragile (EF change-tracker
        // side effects). Read-only query, result used locally.
        // Membership count per user is bounded (a user belongs to a handful of orgs at
        // most), so the small in-memory sort after materialisation is trivially fast
        // and sidesteps any provider-specific quirks around ORDER BY on timezone-aware
        // datetime columns.
        var memberships = (await _dbContext.PlatformUserOrgMemberships
            .AsNoTracking()
            .Where(m => m.PlatformUserId == user.Id)
            .ToListAsync(ct))
            .OrderBy(m => m.JoinedAt)
            .ToList();

        // Earliest-joined standard (non-public) org wins. Anything else = public variant.
        var firstStandardOrgMembership = memberships
            .FirstOrDefault(m => m.OrganizationId != WellKnownIds.PublicOrgId);

        if (firstStandardOrgMembership is null)
        {
            return new WelcomeDispatchContext(
                user, WelcomeVariant.Public, InvitingOrganization: null, InvitedRole: null);
        }

        var org = await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == firstStandardOrgMembership.OrganizationId, ct);

        if (org is null)
        {
            // Org disappeared between membership creation and welcome send. Fall back
            // to public variant rather than failing — the user still deserves a greeting.
            _logger.LogWarning(
                "Inviting organisation {OrgId} for user {UserId} not found; falling back to public welcome",
                firstStandardOrgMembership.OrganizationId, user.Id);
            return new WelcomeDispatchContext(
                user, WelcomeVariant.Public, InvitingOrganization: null, InvitedRole: null);
        }

        // Pass the role explicitly — no navigation-collection mutation, no silent fallback
        // to "Member" when the nav isn't loaded.
        return new WelcomeDispatchContext(
            user, WelcomeVariant.Invited, org, firstStandardOrgMembership.Role);
    }
}
