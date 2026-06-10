// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Models.Requests;

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

    /// <inheritdoc />
    public async Task<AuthMethodsResponse?> GetAggregateAsync(Guid platformUserId, CancellationToken cancellationToken = default)
    {
        // Single query for the user header + counts so we can derive
        // CanRemove for each row from the same total used at mutation time.
        var user = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => new
            {
                u.Email,
                u.EmailVerified,
                HasPassword = u.PasswordHash != null,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null) return null;

        var socials = await _db.PlatformSocialLogins
            .AsNoTracking()
            .Where(s => s.PlatformUserId == platformUserId)
            .OrderBy(s => s.LinkedAt)
            .Select(s => new
            {
                s.Id, s.Provider, s.Email, s.DisplayName, s.LinkedAt, s.LastUsedAt,
            })
            .ToListAsync(cancellationToken);

        var passkeys = await _db.PasskeyCredentials
            .AsNoTracking()
            .Where(p => p.PlatformUserId == platformUserId && p.Status != CredentialStatus.Revoked)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new
            {
                p.Id, p.DisplayName, p.DeviceType, p.Status, p.DisabledReason,
                p.CreatedAt, p.LastUsedAt,
            })
            .ToListAsync(cancellationToken);

        // Active count drives the floor — Disabled passkeys are not counted.
        var activePasskeys = passkeys.Count(p => p.Status == CredentialStatus.Active);
        var totalActive = (user.HasPassword ? 1 : 0) + socials.Count + activePasskeys;

        // CanRemove logic is "subtract one only if the targeted method is
        // currently part of the active set" — same as WouldRemovingLeaveZero.
        bool CanRemovePassword() => user.HasPassword && totalActive - 1 > 0;
        bool CanRemoveSocial() => totalActive - 1 > 0;
        bool CanRemovePasskey(CredentialStatus status) =>
            status == CredentialStatus.Active ? totalActive - 1 > 0 : true;

        // Feature 150 — assurance tier (badge) + the floor-rule proof tier each row needs to be
        // removed. These are method-kind constants sourced from AssurancePolicy, the single
        // server-authoritative source; the UI only reflects them, never decides.
        var passwordTier = AssurancePolicy.TierOfMethod(AuthMethodKind.Password);
        var passwordRequired = AssurancePolicy.RequiredProofTier(ScopedOperation.RemovePassword);
        var socialTier = AssurancePolicy.TierOfMethod(AuthMethodKind.Social);
        var socialRequired = AssurancePolicy.RequiredProofTier(ScopedOperation.RemoveAuthMethod, AuthMethodKind.Social);
        var passkeyTier = AssurancePolicy.TierOfMethod(AuthMethodKind.Passkey);
        var passkeyRequired = AssurancePolicy.RequiredProofTier(ScopedOperation.RemoveAuthMethod, AuthMethodKind.Passkey);

        // SMS 2FA is configuration-gated (US3) — false until an ISmsSender provider is wired in.
        const bool smsAvailable = false;

        return new AuthMethodsResponse(
            Email: user.Email,
            EmailVerified: user.EmailVerified,
            Password: new AuthMethodsPassword(
                IsSet: user.HasPassword,
                LastChangedAt: null, // Not currently tracked — future work.
                CanRemove: CanRemovePassword(),
                AssuranceTier: passwordTier,
                RequiredProofTier: passwordRequired),
            Socials: socials.Select(s => new AuthMethodsSocial(
                LinkId: s.Id,
                Provider: s.Provider,
                Email: s.Email,
                DisplayName: s.DisplayName,
                LinkedAt: s.LinkedAt,
                LastUsedAt: s.LastUsedAt,
                CanRemove: CanRemoveSocial(),
                AssuranceTier: socialTier,
                RequiredProofTier: socialRequired)).ToList(),
            Passkeys: passkeys.Select(p => new AuthMethodsPasskey(
                Id: p.Id,
                DisplayName: string.IsNullOrWhiteSpace(p.DisplayName) ? "Unnamed passkey" : p.DisplayName,
                DeviceType: p.DeviceType,
                Status: p.Status,
                DisabledReason: p.DisabledReason,
                CreatedAt: p.CreatedAt,
                LastUsedAt: p.LastUsedAt,
                CanRemove: CanRemovePasskey(p.Status),
                CanRename: p.Status == CredentialStatus.Active,
                AssuranceTier: passkeyTier,
                RequiredProofTier: passkeyRequired)).ToList(),
            SmsAvailable: smsAvailable);
    }
}
