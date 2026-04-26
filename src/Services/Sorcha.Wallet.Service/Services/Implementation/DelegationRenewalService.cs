// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.ServiceClients.PlatformUserDevice;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>Default <see cref="IDelegationRenewalService"/>.</summary>
public sealed class DelegationRenewalService : IDelegationRenewalService
{
    private readonly IPlatformUserDeviceClient _deviceClient;
    private readonly IDeviceDelegationIssuer _issuer;
    private readonly IOrgStatusSigningWalletResolver _orgWalletResolver;
    private readonly ILogger<DelegationRenewalService> _logger;

    /// <summary>Initialises a new instance.</summary>
    public DelegationRenewalService(
        IPlatformUserDeviceClient deviceClient,
        IDeviceDelegationIssuer issuer,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        ILogger<DelegationRenewalService> logger)
    {
        _deviceClient = deviceClient ?? throw new ArgumentNullException(nameof(deviceClient));
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _orgWalletResolver = orgWalletResolver ?? throw new ArgumentNullException(nameof(orgWalletResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<DelegationRenewalResponse?> RenewAsync(
        Guid deviceId,
        Guid platformUserId,
        string citizenWalletAddress,
        Guid organizationId,
        CancellationToken ct = default)
    {
        var device = await _deviceClient.GetByIdAsync(deviceId, platformUserId, ct);
        if (device is null)
        {
            _logger.LogInformation(
                "Delegation renewal rejected: device {DeviceId} not found for platformUser {PlatformUserId}",
                deviceId, platformUserId);
            return null;
        }

        if (!string.Equals(device.Status, "Active", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Delegation renewal rejected: device {DeviceId} status is {Status} (must be Active)",
                deviceId, device.Status);
            return null;
        }

        // Re-derive the device JWK from its persisted JSON.
        var deviceJwk = ParseJwk(device.DevicePublicJwkJson);

        // Always re-issue: the holder→device delegation is short(ish) and renewals
        // are infrequent. The status-list slot stays the same (it is per-device,
        // not per-delegation) because IDeviceDelegationIssuer allocates a new slot
        // only when there is no existing reservation — but that bookkeeping is
        // owned by the publisher, not us; v1 keeps it simple by always issuing
        // fresh and trusting the publisher to handle slot reuse on subsequent
        // refreshes.
        var orgSigningWallet = await _orgWalletResolver.ResolveAsync(organizationId, ct);
        var fresh = await _issuer.IssueAsync(
            platformUserId,
            citizenWalletAddress,
            organizationId,
            orgSigningWallet,
            deviceJwk,
            device.Label,
            device.Platform,
            ct);

        // Refresh the Tenant device record's delegation fields. The RegisterAsync
        // path is idempotent on (PlatformUserId, JWK thumbprint), so calling it
        // here updates the existing row in place rather than creating a new one.
        await _deviceClient.RegisterAsync(
            platformUserId,
            device.Label,
            device.DevicePublicJwkThumbprint,
            device.DevicePublicJwkJson,
            device.Platform,
            userAgent: string.Empty, // unchanged on renewal
            fresh.ExpiresAt,
            fresh.Jti,
            fresh.StatusListIndex,
            ct);

        _logger.LogInformation(
            "Renewed delegation for device {DeviceId} (platformUser={PlatformUserId}, jti={Jti}, exp={Exp:O})",
            deviceId, platformUserId, fresh.Jti, fresh.ExpiresAt);

        return new DelegationRenewalResponse
        {
            DelegationCredential = fresh.CompactJwt,
            ExpiresAt = fresh.ExpiresAt,
        };
    }

    private static EcP256PublicJwk ParseJwk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new EcP256PublicJwk
        {
            Kty = root.GetProperty("kty").GetString() ?? "EC",
            Crv = root.GetProperty("crv").GetString() ?? "P-256",
            X = root.GetProperty("x").GetString() ?? string.Empty,
            Y = root.TryGetProperty("y", out var y) ? y.GetString() ?? string.Empty : string.Empty,
        };
    }
}
