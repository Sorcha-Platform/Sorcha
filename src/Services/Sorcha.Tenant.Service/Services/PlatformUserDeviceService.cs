// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Default <see cref="IPlatformUserDeviceService"/>. Persists Feature 114 device
/// enrolments with idempotent upsert on <c>(PlatformUserId, DevicePublicJwkThumbprint)</c>.
/// </summary>
public sealed class PlatformUserDeviceService : IPlatformUserDeviceService
{
    private readonly TenantDbContext _db;
    private readonly ILogger<PlatformUserDeviceService> _logger;

    /// <summary>Initialises a new instance of the <see cref="PlatformUserDeviceService"/> class.</summary>
    public PlatformUserDeviceService(TenantDbContext db, ILogger<PlatformUserDeviceService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PlatformUserDevice> RegisterAsync(
        Guid platformUserId,
        string label,
        string devicePublicJwkThumbprint,
        string devicePublicJwkJson,
        string platform,
        string userAgent,
        DateTimeOffset delegationExpiresAt,
        string delegationCredentialJti,
        int statusListIndex,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePublicJwkThumbprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePublicJwkJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(delegationCredentialJti);

        if (label.Length > 120)
        {
            throw new ArgumentException("Label exceeds 120 characters.", nameof(label));
        }

        if (devicePublicJwkThumbprint.Length != 43)
        {
            throw new ArgumentException(
                "DevicePublicJwkThumbprint must be exactly 43 base64url characters (SHA-256).",
                nameof(devicePublicJwkThumbprint));
        }

        if (devicePublicJwkJson.Length > 512)
        {
            throw new ArgumentException("DevicePublicJwkJson exceeds 512 characters.", nameof(devicePublicJwkJson));
        }

        var existing = await _db.PlatformUserDevices
            .FirstOrDefaultAsync(
                d => d.PlatformUserId == platformUserId
                  && d.DevicePublicJwkThumbprint == devicePublicJwkThumbprint,
                ct);

        if (existing is not null)
        {
            // Idempotent re-registration — refresh delegation state but preserve
            // the original Id / EnrolledAt so deviceId stays stable for the wallet.
            existing.Label = label;
            existing.Platform = platform;
            existing.UserAgent = userAgent;
            existing.DelegationExpiresAt = delegationExpiresAt;
            existing.DelegationCredentialJti = delegationCredentialJti;
            existing.StatusListIndex = statusListIndex;
            existing.LastSeenAt = DateTimeOffset.UtcNow;

            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Re-registered existing PlatformUserDevice {DeviceId} (platformUser={PlatformUserId}, " +
                "thumbprint={Thumbprint}) — idempotent enrolment retry",
                existing.Id, platformUserId, devicePublicJwkThumbprint);

            return existing;
        }

        var device = new PlatformUserDevice
        {
            Id = Guid.NewGuid(),
            PlatformUserId = platformUserId,
            Label = label,
            DevicePublicJwkThumbprint = devicePublicJwkThumbprint,
            DevicePublicJwkJson = devicePublicJwkJson,
            Platform = platform,
            UserAgent = userAgent,
            Status = PlatformUserDeviceStatus.Active,
            EnrolledAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow,
            DelegationExpiresAt = delegationExpiresAt,
            DelegationCredentialJti = delegationCredentialJti,
            StatusListIndex = statusListIndex
        };

        _db.PlatformUserDevices.Add(device);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Registered new PlatformUserDevice {DeviceId} (platformUser={PlatformUserId}, " +
            "platform={Platform}, statusListIndex={StatusListIndex}, exp={Exp:O})",
            device.Id, platformUserId, platform, statusListIndex, delegationExpiresAt);

        return device;
    }

    /// <inheritdoc />
    public async Task<PlatformUserDevice?> GetByIdAsync(
        Guid deviceId, Guid platformUserId, CancellationToken ct = default)
    {
        return await _db.PlatformUserDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.PlatformUserId == platformUserId, ct);
    }
}
