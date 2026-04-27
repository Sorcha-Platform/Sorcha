// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IAuthMethodService"/> implementation. Computes the
/// last-method floor via a single aggregating query against
/// <see cref="PlatformUser"/>, <see cref="PlatformSocialLogin"/>, and
/// active <see cref="PasskeyCredential"/> rows.
/// </summary>
public sealed class AuthMethodService : IAuthMethodService
{
    private readonly TenantDbContext _db;

    /// <summary>Creates a new <see cref="AuthMethodService"/>.</summary>
    public AuthMethodService(TenantDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<AuthMethodCounts> GetCountsAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        // Single round-trip: project from PlatformUsers and join-aggregate
        // over the two related collections. Returns zeros when the user is
        // not found (caller's auth filter should already have rejected).
        var snapshot = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => new
            {
                HasPassword = u.PasswordHash != null,
                SocialCount = _db.PlatformSocialLogins.Count(s => s.PlatformUserId == platformUserId),
                ActivePasskeyCount = _db.PasskeyCredentials
                    .Count(p => p.PlatformUserId == platformUserId && p.Status == CredentialStatus.Active)
            })
            .FirstOrDefaultAsync(cancellationToken);

        return snapshot is null
            ? new AuthMethodCounts(false, 0, 0)
            : new AuthMethodCounts(snapshot.HasPassword, snapshot.SocialCount, snapshot.ActivePasskeyCount);
    }

    /// <inheritdoc />
    public async Task<bool> WouldRemovingLeaveZeroAsync(
        Guid platformUserId,
        AuthMethodKind kind,
        Guid? methodId,
        CancellationToken cancellationToken = default)
    {
        var counts = await GetCountsAsync(platformUserId, cancellationToken);

        // Subtract one only if the targeted method is currently part of the
        // active set. This guards against UI optimism: if the row was already
        // removed by a concurrent request the count is unaffected.
        var subtract = kind switch
        {
            AuthMethodKind.Password => counts.HasPassword ? 1 : 0,
            AuthMethodKind.Social => methodId.HasValue
                && await _db.PlatformSocialLogins
                    .AnyAsync(s => s.Id == methodId.Value && s.PlatformUserId == platformUserId, cancellationToken)
                ? 1 : 0,
            AuthMethodKind.Passkey => methodId.HasValue
                && await _db.PasskeyCredentials
                    .AnyAsync(p => p.Id == methodId.Value
                                && p.PlatformUserId == platformUserId
                                && p.Status == CredentialStatus.Active, cancellationToken)
                ? 1 : 0,
            _ => 0
        };

        return counts.Total - subtract <= 0;
    }
}
