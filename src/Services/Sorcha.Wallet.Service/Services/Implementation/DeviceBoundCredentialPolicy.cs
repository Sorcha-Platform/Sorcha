// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Issuer-side policy enforcing at most <see cref="MaxDevices"/> live device-bound credential
/// copies per <c>(user, credentialType)</c>, keyed on the device key JWK thumbprint (RFC 7638),
/// with least-recently-issued (LRU) eviction (Feature 1195, Phase 2).
/// </summary>
/// <remarks>
/// Pure orchestration over injected seams — no HTTP, no database, no SD-JWT parsing — so it is
/// fully unit-testable. Wiring into the mint path is Task 5.
/// <para>
/// Eviction ordering is by <see cref="DeviceBoundCredentialCopy.IssuedAt"/> (smallest = oldest),
/// so no wall-clock is needed. Revoke happens BEFORE the disposition is returned; a revoke
/// failure propagates so the caller aborts issuance and no partial state is created (no inbox
/// write, no disposition). The inbox notification follows the established non-fatal pattern — a
/// notify failure is logged and swallowed and never aborts issuance.
/// </para>
/// </remarks>
public sealed class DeviceBoundCredentialPolicy : IDeviceBoundCredentialPolicy
{
    /// <summary>Maximum number of live device-bound copies permitted per (user, credentialType).</summary>
    public const int MaxDevices = 3;

    private readonly IDeviceBoundCredentialLookup _lookup;
    private readonly IDeviceBoundCredentialRevoker _revoker;
    private readonly ICitizenDeviceInboxWriter _inbox;
    private readonly ILogger<DeviceBoundCredentialPolicy> _logger;

    /// <summary>Initialises a new <see cref="DeviceBoundCredentialPolicy"/>.</summary>
    public DeviceBoundCredentialPolicy(
        IDeviceBoundCredentialLookup lookup,
        IDeviceBoundCredentialRevoker revoker,
        ICitizenDeviceInboxWriter inbox,
        ILogger<DeviceBoundCredentialPolicy> logger)
    {
        _lookup = lookup ?? throw new ArgumentNullException(nameof(lookup));
        _revoker = revoker ?? throw new ArgumentNullException(nameof(revoker));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DeviceBindDisposition> ReconcileAsync(
        Guid userId, string credentialType, string deviceKeyThumbprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentialType))
            throw new ArgumentException("Credential type is required.", nameof(credentialType));
        if (string.IsNullOrWhiteSpace(deviceKeyThumbprint))
            throw new ArgumentException("Device key thumbprint is required.", nameof(deviceKeyThumbprint));

        var liveCopies = await _lookup.GetLiveCopiesAsync(userId, credentialType, ct).ConfigureAwait(false);

        // Same device re-binding — idempotent replace, no eviction, count unchanged.
        // Thumbprints are base64url (RFC 7638), so compare ordinal (case-sensitive).
        if (liveCopies.Any(c => string.Equals(c.DeviceKeyThumbprint, deviceKeyThumbprint, StringComparison.Ordinal)))
        {
            _logger.LogDebug(
                "Device-bound reconcile: thumbprint match — ReplaceExisting. UserId={UserId} Type={Type}",
                userId, credentialType);
            return new DeviceBindDisposition(DeviceBindKind.ReplaceExisting, EvictedCredentialId: null);
        }

        // New device within the cap — mint freely.
        if (liveCopies.Count < MaxDevices)
        {
            _logger.LogDebug(
                "Device-bound reconcile: {Count}/{Max} — NewWithinCap. UserId={UserId} Type={Type}",
                liveCopies.Count, MaxDevices, userId, credentialType);
            return new DeviceBindDisposition(DeviceBindKind.NewWithinCap, EvictedCredentialId: null);
        }

        // Cap reached — evict the oldest (least-recently-issued) live copy to make room.
        var oldest = liveCopies.OrderBy(c => c.IssuedAt).ThenBy(c => c.CredentialId).First();

        // Revoke BEFORE returning. A revoke failure propagates so issuance aborts — no inbox
        // write, no disposition. This must run before the (non-fatal) inbox notification.
        await _revoker.RevokeAsync(userId, oldest, ct).ConfigureAwait(false);

        // Inbox notify is non-fatal — a notify outage must not abort issuance after the copy
        // has already been revoked. Mirrors the try/log/swallow pattern of the inbox writers.
        try
        {
            await _inbox.WriteDeviceRevokedAsync(userId, oldest.DeviceId, oldest.DeviceLabel, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Device-bound eviction: inbox notify failed (non-fatal). UserId={UserId} CredentialId={CredentialId} DeviceId={DeviceId}",
                userId, oldest.CredentialId, oldest.DeviceId);
        }

        _logger.LogInformation(
            "Device-bound reconcile: cap {Max} reached — evicted oldest copy {CredentialId} (issued {IssuedAt:o}). UserId={UserId} Type={Type}",
            MaxDevices, oldest.CredentialId, oldest.IssuedAt, userId, credentialType);

        return new DeviceBindDisposition(DeviceBindKind.NewWithEviction, oldest.CredentialId);
    }
}
