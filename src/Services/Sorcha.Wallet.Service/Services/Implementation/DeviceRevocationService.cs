// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Sorcha.Wallet.Service.Hubs;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>Default <see cref="IDeviceRevocationService"/>.</summary>
public sealed class DeviceRevocationService : IDeviceRevocationService
{
    /// <summary>SignalR client method name for the device-revoked broadcast.</summary>
    public const string DeviceRevokedEvent = "DeviceRevoked";

    private readonly ICitizenStatusListPublisher _statusList;
    private readonly IOrgStatusSigningWalletResolver _orgWalletResolver;
    private readonly IHubContext<WalletHub> _hub;
    private readonly ILogger<DeviceRevocationService> _logger;

    /// <summary>Initialises a new instance of the <see cref="DeviceRevocationService"/> class.</summary>
    public DeviceRevocationService(
        ICitizenStatusListPublisher statusList,
        IOrgStatusSigningWalletResolver orgWalletResolver,
        IHubContext<WalletHub> hub,
        ILogger<DeviceRevocationService> logger)
    {
        _statusList = statusList ?? throw new ArgumentNullException(nameof(statusList));
        _orgWalletResolver = orgWalletResolver ?? throw new ArgumentNullException(nameof(orgWalletResolver));
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        Guid organizationId,
        int listId,
        int indexInList,
        Guid deviceId,
        Guid platformUserId,
        CancellationToken ct = default)
    {
        var signingWallet = await _orgWalletResolver.ResolveAsync(organizationId, ct);

        await _statusList.FlipAsync(organizationId, listId, indexInList, signingWallet, ct);

        var group = WalletHub.GroupNameFor(platformUserId);
        await _hub.Clients.Group(group).SendAsync(DeviceRevokedEvent, deviceId, ct);

        _logger.LogInformation(
            "Citizen device revoked: deviceId={DeviceId} platformUser={PlatformUserId} " +
            "org={OrgId} list={ListId}#{Index} signalrGroup={Group}",
            deviceId, platformUserId, organizationId, listId, indexInList, group);
    }
}
