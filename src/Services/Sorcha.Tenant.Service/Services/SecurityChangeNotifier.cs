// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>The account-security change being notified (Feature 150).</summary>
public enum SecurityChangeKind
{
    /// <summary>A password was set on a previously password-less account.</summary>
    PasswordSet,
    /// <summary>The account password was rotated.</summary>
    PasswordChanged,
    /// <summary>The password sign-in method was removed.</summary>
    PasswordRemoved,
    /// <summary>A social account was linked as a sign-in method.</summary>
    SocialLinked,
    /// <summary>A linked social account was unlinked.</summary>
    SocialUnlinked,
    /// <summary>A new passkey was registered.</summary>
    PasskeyAdded,
    /// <summary>A passkey was revoked.</summary>
    PasskeyRemoved,
    /// <summary>A passkey was renamed.</summary>
    PasskeyRenamed,
    /// <summary>Authenticator (TOTP) two-factor was enabled.</summary>
    TwoFactorEnabled,
    /// <summary>Authenticator (TOTP) two-factor was disabled.</summary>
    TwoFactorDisabled,
    /// <summary>Email one-time-code second factor was enabled (US2).</summary>
    EmailOtpEnabled,
    /// <summary>Email one-time-code second factor was disabled (US2).</summary>
    EmailOtpDisabled,
    /// <summary>SMS one-time-code second factor was enabled (US3).</summary>
    SmsOtpEnabled,
    /// <summary>SMS one-time-code second factor was disabled (US3).</summary>
    SmsOtpDisabled,
    /// <summary>The mobile number used for SMS codes was changed (US3).</summary>
    PhoneChanged
}

/// <summary>
/// Feature 150 always-notify (FR-009): every account-security change writes a durable F118
/// inbox entry AND sends a Sorcha-branded email, so an unexpected-but-authorised change is
/// always visible to the real account owner. Both legs are best-effort (FR-011) — a
/// notification failure is logged and never blocks or rolls back the underlying operation.
/// </summary>
public interface ISecurityChangeNotifier
{
    /// <summary>Notify the user of a security-state change via the inbox + email. Never throws.</summary>
    Task NotifyAsync(Guid platformUserId, SecurityChangeKind kind, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SecurityChangeNotifier : ISecurityChangeNotifier
{
    private readonly ITenantSecurityInboxWriter _inbox;
    private readonly ITransactionalEmailService _email;
    private readonly TenantDbContext _db;
    private readonly EmailSettings _settings;
    private readonly ILogger<SecurityChangeNotifier> _logger;

    /// <summary>Initialises a new <see cref="SecurityChangeNotifier"/>.</summary>
    public SecurityChangeNotifier(
        ITenantSecurityInboxWriter inbox,
        ITransactionalEmailService email,
        TenantDbContext db,
        IOptions<EmailSettings> settings,
        ILogger<SecurityChangeNotifier> logger)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task NotifyAsync(Guid platformUserId, SecurityChangeKind kind, CancellationToken ct = default)
    {
        var (eventKey, title, summary, severity) = Describe(kind);

        // Inbox leg — the writer is already internally fail-safe (try/log/swallow).
        await _inbox.WriteSecurityChangeAsync(platformUserId, eventKey, title, summary, severity, ct)
            .ConfigureAwait(false);

        // Email leg — best-effort; a send failure must never block the security operation (FR-011).
        try
        {
            var user = await _db.PlatformUsers
                .AsNoTracking()
                .Where(u => u.Id == platformUserId)
                .Select(u => new { u.Email, u.DisplayName })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            if (user is null || string.IsNullOrWhiteSpace(user.Email))
                return;

            var manageUrl = $"{_settings.BaseUrl.TrimEnd('/')}/app/security";
            await _email.SendSecurityChangeAsync(
                new SecurityChangeDispatch(user.Email, user.DisplayName, title, summary, manageUrl), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Security-change email failed for PlatformUserId={UserId} Kind={Kind}", platformUserId, kind);
        }
    }

    // Inbox/email copy per change kind. Warning severity for anything that weakens the account
    // (removals, disables, rotations) so it stands out in the bell drawer; Info for additions.
    private static (string eventKey, string title, string summary, InboxSeverity severity) Describe(SecurityChangeKind kind) => kind switch
    {
        SecurityChangeKind.PasswordSet => ("password-set", "Password added", "A password was added to your account as a sign-in method.", InboxSeverity.Info),
        SecurityChangeKind.PasswordChanged => ("password-changed", "Password changed", "Your account password was changed. If this wasn't you, reset it and review your sign-in methods.", InboxSeverity.Warning),
        SecurityChangeKind.PasswordRemoved => ("password-removed", "Password removed", "The password sign-in method was removed from your account.", InboxSeverity.Warning),
        SecurityChangeKind.SocialLinked => ("social-linked", "Social account linked", "A social account was linked to your Sorcha account as a sign-in method.", InboxSeverity.Info),
        SecurityChangeKind.SocialUnlinked => ("social-unlinked", "Social account unlinked", "A linked social account was removed from your sign-in methods.", InboxSeverity.Warning),
        SecurityChangeKind.PasskeyAdded => ("passkey-added", "Passkey added", "A new passkey was registered for signing in to your account.", InboxSeverity.Info),
        SecurityChangeKind.PasskeyRemoved => ("passkey-removed", "Passkey removed", "A passkey was removed from your account. If this wasn't you, review your sign-in methods now.", InboxSeverity.Warning),
        SecurityChangeKind.PasskeyRenamed => ("passkey-renamed", "Passkey renamed", "One of your passkeys was renamed.", InboxSeverity.Info),
        SecurityChangeKind.TwoFactorEnabled => ("two-factor-enabled", "Two-factor authentication enabled", "Your account now requires a second factor at sign-in.", InboxSeverity.Info),
        SecurityChangeKind.TwoFactorDisabled => ("two-factor-disabled", "Two-factor authentication disabled", "A second factor was turned off for your account. Re-enable it from your security settings.", InboxSeverity.Warning),
        SecurityChangeKind.EmailOtpEnabled => ("email-otp-enabled", "Email codes enabled", "Email one-time codes were enabled as a second factor.", InboxSeverity.Info),
        SecurityChangeKind.EmailOtpDisabled => ("email-otp-disabled", "Email codes disabled", "Email one-time codes were disabled as a second factor.", InboxSeverity.Warning),
        SecurityChangeKind.SmsOtpEnabled => ("sms-otp-enabled", "SMS codes enabled", "SMS one-time codes were enabled as a second factor.", InboxSeverity.Info),
        SecurityChangeKind.SmsOtpDisabled => ("sms-otp-disabled", "SMS codes disabled", "SMS one-time codes were disabled as a second factor.", InboxSeverity.Warning),
        SecurityChangeKind.PhoneChanged => ("phone-changed", "Phone number changed", "The mobile number used for SMS codes was changed.", InboxSeverity.Warning),
        _ => ("security-change", "Security update", "A change was made to your account security settings.", InboxSeverity.Warning)
    };
}
