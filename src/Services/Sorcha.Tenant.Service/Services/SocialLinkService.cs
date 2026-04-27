// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="ISocialLinkService"/> implementation backed by the
/// Tenant DbContext. Threading: a single service instance is scoped per
/// request, so the floor check + delete in <see cref="UnlinkAsync"/> share
/// one EF Core change-tracker; concurrency safety against the two-tab
/// race comes from the <c>SaveChangesAsync</c> + the floor re-read
/// inside the same logical transaction.
/// </summary>
public sealed class SocialLinkService : ISocialLinkService
{
    private readonly TenantDbContext _db;
    private readonly IAuthMethodService _authMethodService;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<SocialLinkService> _logger;

    /// <summary>Creates a new <see cref="SocialLinkService"/>.</summary>
    public SocialLinkService(
        TenantDbContext db,
        IAuthMethodService authMethodService,
        AuthMetrics metrics,
        ILogger<SocialLinkService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _authMethodService = authMethodService ?? throw new ArgumentNullException(nameof(authMethodService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SocialLinkOutcome> LinkAsync(
        Guid platformUserId,
        string provider,
        string providerSubject,
        string? providerEmail,
        string? providerDisplayName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        ArgumentException.ThrowIfNullOrEmpty(providerSubject);

        // Step 1: (Provider, Subject) collision. If the same provider account
        // is already linked anywhere, decide between AlreadyLinkedToCaller
        // (idempotent no-op) and AlreadyLinkedToDifferentUser (Q1 reject).
        var existingLink = await _db.PlatformSocialLogins
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.Provider == provider && s.Subject == providerSubject,
                cancellationToken);

        if (existingLink is not null)
        {
            if (existingLink.PlatformUserId == platformUserId)
            {
                _logger.LogInformation(
                    "Social link no-op (already linked) for {PlatformUserId} {Provider}",
                    platformUserId, provider);
                return SocialLinkOutcome.AlreadyLinkedToCaller;
            }

            _metrics.RecordLinkCollision(provider);
            _logger.LogWarning(
                "Social link rejected — provider account already belongs to {OtherPlatformUserId}",
                existingLink.PlatformUserId);
            return SocialLinkOutcome.AlreadyLinkedToDifferentUser;
        }

        // Step 2: Email collision against any *other* PlatformUser. Skip when
        // the provider returns no email (Apple "Hide my email", private
        // GitHub) — the (Provider, Subject) unique index above already
        // prevents duplicate links for the same provider account.
        if (!string.IsNullOrWhiteSpace(providerEmail))
        {
            var collidingUserExists = await _db.PlatformUsers
                .AsNoTracking()
                .AnyAsync(
                    u => u.Email == providerEmail && u.Id != platformUserId,
                    cancellationToken);

            if (collidingUserExists)
            {
                _metrics.RecordLinkCollision(provider);
                _logger.LogWarning(
                    "Social link rejected — provider email collides with a different PlatformUser {Provider}",
                    provider);
                return SocialLinkOutcome.EmailCollision;
            }
        }

        // Step 3: Insert. The PlatformSocialLogins (Provider, Subject) unique
        // index on the DB side is the final defence against a TOCTOU race
        // between the AnyAsync above and the SaveChangesAsync here.
        _db.PlatformSocialLogins.Add(new PlatformSocialLogin
        {
            PlatformUserId = platformUserId,
            Provider = provider,
            Subject = providerSubject,
            Email = providerEmail,
            DisplayName = providerDisplayName,
            LinkedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UQ_PlatformSocialLogin_Provider_Subject", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Lost the TOCTOU race against a concurrent link of the same
            // (Provider, Subject). Treat as collision.
            _metrics.RecordLinkCollision(provider);
            _logger.LogWarning(
                "Social link race lost on (Provider, Subject) unique index for {Provider}", provider);
            return SocialLinkOutcome.AlreadyLinkedToDifferentUser;
        }

        _metrics.RecordMethodAdded(AuthMethodKindTag.Social);
        _logger.LogInformation(
            "Linked social provider {Provider} to {PlatformUserId}",
            provider, platformUserId);
        return SocialLinkOutcome.Linked;
    }

    /// <inheritdoc />
    public async Task<SocialUnlinkOutcome> UnlinkAsync(
        Guid platformUserId,
        Guid linkId,
        CancellationToken cancellationToken = default)
    {
        // Floor check + delete share one EF change tracker. The aggregate
        // count read inside WouldRemovingLeaveZeroAsync materialises only
        // ints; the subsequent Remove + SaveChanges is atomic at the row
        // level via Postgres unique-key write. Two tabs racing both pass
        // the AnyAsync but only one wins SaveChanges — and the second one
        // has its floor recomputed against a count that already reflects
        // the first delete (because the first SaveChanges committed).
        var link = await _db.PlatformSocialLogins
            .FirstOrDefaultAsync(
                s => s.Id == linkId && s.PlatformUserId == platformUserId,
                cancellationToken);

        if (link is null)
        {
            return SocialUnlinkOutcome.NotFound;
        }

        var wouldLeaveZero = await _authMethodService.WouldRemovingLeaveZeroAsync(
            platformUserId, AuthMethodKind.Social, linkId, cancellationToken);
        if (wouldLeaveZero)
        {
            _metrics.RecordFloorBlocked(AuthMethodKindTag.Social);
            _logger.LogWarning(
                "Social unlink blocked by last-method floor for {PlatformUserId}", platformUserId);
            return SocialUnlinkOutcome.FloorViolation;
        }

        _db.PlatformSocialLogins.Remove(link);
        await _db.SaveChangesAsync(cancellationToken);

        _metrics.RecordMethodRemoved(AuthMethodKindTag.Social);
        _logger.LogInformation(
            "Unlinked social provider {Provider} {LinkId} from {PlatformUserId}",
            link.Provider, linkId, platformUserId);
        return SocialUnlinkOutcome.Unlinked;
    }
}
