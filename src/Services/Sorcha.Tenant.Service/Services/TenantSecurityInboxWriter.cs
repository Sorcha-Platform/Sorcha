// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Feature 118 — Tenant-side inbox writer for security-relevant account
/// events (2FA enabled / disabled, etc.). Sibling to
/// <see cref="ITenantMembershipInboxWriter"/>; same fail-safe semantics:
/// inbox-write failures are logged but never block the originating
/// security operation.
/// </summary>
public interface ITenantSecurityInboxWriter
{
    /// <summary>Write a "2FA enabled" Category=Security inbox entry.</summary>
    Task WriteTwoFactorEnabledAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Write a "2FA disabled" Category=Security inbox entry.</summary>
    Task WriteTwoFactorDisabledAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Write a "password reset" Category=Security inbox entry. The fold-by-second
    /// SourceEventId means a successful reset always produces a fresh entry — the
    /// "if this wasn't you" copy is the whole point of the surface.
    /// </summary>
    Task WritePasswordResetAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Write a "backup code used" Category=Security entry. Severity is bumped to
    /// <see cref="InboxSeverity.Warning"/> because backup-code consumption is an
    /// account-takeover signal worth flagging. Each consumption produces a fresh
    /// entry (timestamp folded into the SourceEventId).
    /// </summary>
    Task WriteBackupCodeUsedAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>
    /// Write a Feature-150 security-change entry with caller-supplied copy. The single
    /// generic entry point used by <c>ISecurityChangeNotifier</c> so every account-security
    /// mutation (password / social / passkey / 2FA channel) lands in the bell drawer.
    /// </summary>
    Task WriteSecurityChangeAsync(
        Guid platformUserId,
        string eventKey,
        string title,
        string summary,
        InboxSeverity severity = InboxSeverity.Warning,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class TenantSecurityInboxWriter : ITenantSecurityInboxWriter
{
    private readonly IInboxService _inbox;
    private readonly ILogger<TenantSecurityInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="TenantSecurityInboxWriter"/>.</summary>
    public TenantSecurityInboxWriter(
        IInboxService inbox,
        ILogger<TenantSecurityInboxWriter> logger)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task WriteTwoFactorEnabledAsync(Guid platformUserId, CancellationToken ct = default) =>
        WriteAsync(
            platformUserId,
            "two-factor-enabled",
            title: "Two-factor authentication enabled",
            summary: "Your account now requires a code from your authenticator app at sign-in.",
            iconKey: "security.2fa-enabled",
            severity: InboxSeverity.Info,
            ct);

    /// <inheritdoc />
    public Task WriteTwoFactorDisabledAsync(Guid platformUserId, CancellationToken ct = default) =>
        WriteAsync(
            platformUserId,
            "two-factor-disabled",
            title: "Two-factor authentication disabled",
            summary: "Your account is now signing in with password only. Re-enable 2FA from Settings → Security.",
            iconKey: "security.2fa-disabled",
            severity: InboxSeverity.Info,
            ct);

    /// <inheritdoc />
    public Task WritePasswordResetAsync(Guid platformUserId, CancellationToken ct = default) =>
        WriteAsync(
            platformUserId,
            "password-reset",
            title: "Password reset",
            summary: "Your password was reset using the email link. If this wasn't you, contact support immediately.",
            iconKey: "security.password-reset",
            severity: InboxSeverity.Info,
            ct);

    /// <inheritdoc />
    public Task WriteBackupCodeUsedAsync(Guid platformUserId, CancellationToken ct = default) =>
        WriteAsync(
            platformUserId,
            "backup-code-used",
            title: "Backup code used to sign in",
            summary: "A 2FA backup code was just consumed during sign-in. If this wasn't you, change your password and review your backup codes immediately.",
            iconKey: "security.backup-code-used",
            severity: InboxSeverity.Warning,
            ct);

    /// <inheritdoc />
    public Task WriteSecurityChangeAsync(
        Guid platformUserId,
        string eventKey,
        string title,
        string summary,
        InboxSeverity severity = InboxSeverity.Warning,
        CancellationToken ct = default) =>
        WriteAsync(
            platformUserId,
            eventKey,
            title: title,
            summary: summary,
            iconKey: $"security.{eventKey}",
            severity: severity,
            ct);

    private async Task WriteAsync(
        Guid platformUserId,
        string eventKey,
        string title,
        string summary,
        string iconKey,
        InboxSeverity severity,
        CancellationToken ct)
    {
        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            var sourceEventId = DeterministicSourceEventId(platformUserId, eventKey, occurredAt);

            var request = new InboxWriteRequest(
                PlatformUserId: platformUserId,
                Category: InboxCategory.Security,
                Severity: severity,
                CorrelationKey: $"security:{eventKey}:{platformUserId:N}",
                DetailHref: "/security", // Feature 150 — the unified Security home
                SourceEventId: sourceEventId,
                OccurredAt: occurredAt,
                Title: title,
                Summary: summary,
                IconKey: iconKey);

            var result = await _inbox.WriteAsync(request, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for security event {EventKey} — PlatformUserId={UserId} EntryId={EntryId}",
                result.IsIdempotent ? "idempotent" : "created",
                eventKey, platformUserId, result.Entry.Id);
        }
        catch (Exception ex)
        {
            // Inbox-write failure must not block the security operation.
            _logger.LogWarning(ex,
                "Inbox-write failed for security event {EventKey} — PlatformUserId={UserId}",
                eventKey, platformUserId);
        }
    }

    private static Guid DeterministicSourceEventId(Guid platformUserId, string eventKey, DateTimeOffset occurredAt)
    {
        // Each enable/disable event is a fresh occurrence — fold the timestamp
        // (to the second) into the deterministic id so a user can re-enable
        // after a previous enable+disable without the second entry collapsing
        // onto the first via the (PlatformUserId, SourceEventId) unique index.
        var input = $"sorcha.inbox.security.{eventKey}:{platformUserId:N}:{occurredAt.ToUnixTimeSeconds()}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
