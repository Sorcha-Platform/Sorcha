// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.ServiceClients.Inbox;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Phase 2c of the Snackbar retirement plan — bridges citizen-wallet device
/// lifecycle events to the durable user inbox owned by Tenant Service. Today
/// only the revoke path is wired; rename is a UX-only event with no inbox
/// surface.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="WalletInboxWriter"/> because the citizen-wallet
/// device path already knows the caller's <c>PlatformUserId</c> from the JWT
/// claims (see <c>CitizenWalletEndpoints.ResolveCitizenContext</c>); there's
/// no participant-lookup step. Keeping it separate also avoids dragging the
/// participant client into a service that doesn't need it.
/// </para>
/// <para>
/// Severity is <see cref="InboxSeverity.Warning"/> — device revocation is a
/// security-relevant event that the citizen may want to confirm later. The
/// surface mirrors <c>TenantSecurityInboxWriter.WriteBackupCodeUsedAsync</c>
/// which uses the same severity for the same audit-trail reason.
/// </para>
/// <para>
/// Fail-safe: empty inputs short-circuit, transport exceptions are caught.
/// Device revocation must never fail because of an inbox-write outage.
/// </para>
/// </remarks>
public interface ICitizenDeviceInboxWriter
{
    /// <summary>Drop a Category=Security entry for a revoked citizen device.</summary>
    /// <param name="platformUserId">The wallet-owner's cross-org platform user id (from JWT claims).</param>
    /// <param name="deviceId">The revoked device's id.</param>
    /// <param name="deviceLabel">Human-readable device label, or <c>null</c> if unavailable.</param>
    Task WriteDeviceRevokedAsync(
        Guid platformUserId,
        Guid deviceId,
        string? deviceLabel,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CitizenDeviceInboxWriter : ICitizenDeviceInboxWriter
{
    private readonly IPlatformInboxClient _inbox;
    private readonly ILogger<CitizenDeviceInboxWriter> _logger;

    /// <summary>Initialises a new <see cref="CitizenDeviceInboxWriter"/>.</summary>
    public CitizenDeviceInboxWriter(
        IPlatformInboxClient inbox,
        ILogger<CitizenDeviceInboxWriter> logger)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteDeviceRevokedAsync(
        Guid platformUserId,
        Guid deviceId,
        string? deviceLabel,
        CancellationToken ct = default)
    {
        if (platformUserId == Guid.Empty || deviceId == Guid.Empty)
        {
            return;
        }

        try
        {
            var displayLabel = string.IsNullOrWhiteSpace(deviceLabel) ? "your device" : deviceLabel;
            var sourceEventId = DeterministicSourceEventId(platformUserId, deviceId);

            var payload = new InboxWritePayload(
                PlatformUserId: platformUserId,
                Category: "Security",
                Severity: "Warning",
                CorrelationKey: $"security:device-revoked:{platformUserId:N}:{deviceId:N}",
                // The revoked device's resource is intentionally still readable
                // (Tenant retains the row with Status=Revoked). Point at the
                // device listing so the citizen can see their remaining devices
                // and confirm which one was removed.
                DetailHref: "/api/v1/me/devices",
                SourceEventId: sourceEventId,
                OccurredAt: DateTimeOffset.UtcNow,
                Title: $"Device revoked: {displayLabel}",
                Summary: "If this wasn't you, change your password and review your remaining devices in Settings → Devices.",
                IconKey: "security.device-revoked");

            var outcome = await _inbox.WriteAsync(payload, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Inbox entry {Outcome} for device-revoked — PlatformUserId={UserId} DeviceId={DeviceId} EntryId={EntryId}",
                outcome.Idempotent ? "idempotent" : "created",
                platformUserId, deviceId, outcome.EntryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Inbox-write failed for device-revoked — PlatformUserId={UserId} DeviceId={DeviceId}",
                platformUserId, deviceId);
        }
    }

    /// <summary>
    /// Deterministic id from <c>(platformUserId, deviceId)</c> so duplicate
    /// revoke attempts (e.g. concurrent web + PWA revoke race) collapse to a
    /// single inbox entry via the <c>(PlatformUserId, SourceEventId)</c>
    /// idempotency key at Tenant Service.
    /// </summary>
    private static Guid DeterministicSourceEventId(Guid platformUserId, Guid deviceId)
    {
        var input = $"sorcha.inbox.security.device-revoked:{platformUserId:N}:{deviceId:N}";
        var bytes = System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}
