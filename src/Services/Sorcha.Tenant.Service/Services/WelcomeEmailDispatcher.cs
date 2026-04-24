// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Owns the one-shot welcome-email semantics: sends exactly one welcome per user across
/// the lifetime of the account, regardless of how many times the verify-success or
/// first-login trigger fires. Idempotent and non-throwing — a failed send is logged
/// but MUST NOT block the calling authentication flow (FR-020).
/// </summary>
public sealed class WelcomeEmailDispatcher
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

    /// <summary>
    /// Sends the appropriate welcome email if the user is eligible (email verified AND
    /// welcome not previously sent). On success, sets <see cref="PlatformUser.WelcomeSentAt"/>
    /// and persists. Safe to call from any number of trigger points.
    /// </summary>
    /// <remarks>
    /// Swallows send exceptions after logging — verification or login flows must proceed
    /// regardless of whether the welcome send succeeded. <see cref="PlatformUser.WelcomeSentAt"/>
    /// is set ONLY on successful send, so a failure can be retried by the next trigger.
    /// </remarks>
    public async Task SendIfPendingAsync(PlatformUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

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

        try
        {
            await _transactional.SendWelcomeAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send welcome email to {Email} (user {UserId}, variant {Variant}); flow continues",
                user.Email, user.Id, context.Variant);
            return;
        }

        user.WelcomeSentAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Welcome email sent to {Email} (user {UserId}, variant {Variant})",
            user.Email, user.Id, context.Variant);
    }

    private async Task<WelcomeDispatchContext> BuildContextAsync(PlatformUser user, CancellationToken ct)
    {
        // Pull memberships directly — the navigation collection on the passed-in user
        // may not be loaded (callers aren't required to Include it).
        var memberships = await _dbContext.PlatformUserOrgMemberships
            .Where(m => m.PlatformUserId == user.Id)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync(ct);

        // Earliest-joined standard (non-public) org wins. Anything else = public variant.
        var firstStandardOrgMembership = memberships
            .FirstOrDefault(m => m.OrganizationId != WellKnownIds.PublicOrgId);

        if (firstStandardOrgMembership is null)
        {
            return new WelcomeDispatchContext(user, WelcomeVariant.Public, InvitingOrganization: null);
        }

        var org = await _dbContext.Organizations
            .FirstOrDefaultAsync(o => o.Id == firstStandardOrgMembership.OrganizationId, ct);

        if (org is null)
        {
            // Org disappeared between membership creation and welcome send. Fall back
            // to public variant rather than failing — the user still deserves a greeting.
            _logger.LogWarning(
                "Inviting organisation {OrgId} for user {UserId} not found; falling back to public welcome",
                firstStandardOrgMembership.OrganizationId, user.Id);
            return new WelcomeDispatchContext(user, WelcomeVariant.Public, InvitingOrganization: null);
        }

        // Attach the resolved membership so TransactionalEmailService can read the role.
        // The membership already belongs to the tracked user, but if navigation wasn't
        // loaded we pre-populate OrgMemberships so the facade doesn't re-query.
        if (user.OrgMemberships.Count == 0)
        {
            foreach (var m in memberships) user.OrgMemberships.Add(m);
        }

        return new WelcomeDispatchContext(user, WelcomeVariant.Invited, org);
    }
}
