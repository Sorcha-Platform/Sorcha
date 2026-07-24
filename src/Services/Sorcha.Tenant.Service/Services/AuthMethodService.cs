// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Models.Auth;
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
    private readonly IVerificationChannelRegistry _channels;

    /// <summary>Creates a new <see cref="AuthMethodService"/>.</summary>
    public AuthMethodService(TenantDbContext db, IVerificationChannelRegistry channels)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
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
        //
        // T012 (Feature 150) — the floor rule's second clause ("the user holds an enrolled proof of
        // tier >= RequiredProofTier") is, for the current method set, ALWAYS satisfied and so adds no
        // gating beyond the last-method floor: RequiredProofTier(remove X) == TierOf(X) by the
        // "required = tier of the thing weakened" rule, and removing a method is satisfiable by a
        // proof of that same method (assert-then-delete for a passkey; re-enter for a password;
        // re-OAuth for a social) — or is ungated entirely (a Disabled passkey bypasses step-up). The
        // per-row RequiredProofTier is still surfaced so the dialog requests the right proof, and the
        // server re-checks on verify (403 proof_tier_insufficient). So CanRemove == the floor here is
        // complete, not a simplification.
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

        // SMS 2FA is configuration-gated (US3) — true only when an operator configured a provider
        // (the registry has an SMS channel).
        var smsAvailable = _channels.SmsAvailable;

        // Email/SMS OTP enablement (US2/US3) is account-wide on PlatformUserTwoFactor.
        var twoFactor = await _db.PlatformUserTwoFactors
            .AsNoTracking()
            .Where(t => t.PlatformUserId == platformUserId)
            .Select(t => new { t.EmailOtpEnabled, t.SmsOtpEnabled })
            .FirstOrDefaultAsync(cancellationToken);
        var emailOtpEnabled = twoFactor?.EmailOtpEnabled ?? false;
        var smsOtpEnabled = (twoFactor?.SmsOtpEnabled ?? false) && smsAvailable;

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
                Status: PasskeyStatusWireValue(p.Status),
                DisabledReason: p.DisabledReason,
                CreatedAt: p.CreatedAt,
                LastUsedAt: p.LastUsedAt,
                CanRemove: CanRemovePasskey(p.Status),
                CanRename: p.Status == CredentialStatus.Active,
                AssuranceTier: passkeyTier,
                RequiredProofTier: passkeyRequired)).ToList(),
            SmsAvailable: smsAvailable,
            EmailOtpEnabled: emailOtpEnabled,
            SmsOtpEnabled: smsOtpEnabled);
    }

    /// <summary>
    /// The wire value for a passkey's status, byte-identical to what the serializer produced when
    /// <see cref="AuthMethodsPasskey.Status"/> was typed as <see cref="CredentialStatus"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shared contract in <c>Sorcha.Tenant.Models.Auth</c> models this as a string: hoisting
    /// <see cref="CredentialStatus"/> into that zero-dependency leaf to satisfy one response
    /// property would drag an EF-mapped enum with ~40 service-internal usages along with it.
    /// </para>
    /// <para>
    /// <b>Do not replace this with <c>ToString()</c>.</b> That silently changes the wire value from
    /// <c>"active"</c> to <c>"Active"</c>. The type-level
    /// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> on <see cref="CredentialStatus"/>
    /// never applied here: System.Text.Json ranks the <c>Converters</c> collection ABOVE a
    /// type-level attribute, so <c>SorchaJson</c>'s kebab-case converter has always won. Pinned by
    /// <c>AuthMethodsWireContractTests</c>.
    /// </para>
    /// </remarks>
    private static string PasskeyStatusWireValue(CredentialStatus status) =>
        JsonNamingPolicy.KebabCaseLower.ConvertName(status.ToString());
}
