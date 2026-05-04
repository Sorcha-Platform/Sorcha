// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Wallet.Service.Hubs;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DeviceRevocationService"/> (Feature 114, US3 PR2b):
/// FlipAsync on the publisher + DeviceRevoked SignalR broadcast on the citizen's
/// group, ordered with FlipAsync first so a successful broadcast implies the bit
/// is already set.
/// </summary>
public sealed class DeviceRevocationServiceTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private const string SigningWallet = "ws1qsigner";

    private readonly Mock<ICitizenStatusListPublisher> _statusList = new();
    private readonly Mock<IOrgStatusSigningWalletResolver> _resolver = new();
    private readonly Mock<IHubContext<WalletHub>> _hub = new();
    private readonly Mock<IHubClients> _clients = new();
    private readonly Mock<IClientProxy> _clientProxy = new();

    private DeviceRevocationService BuildSut()
    {
        _resolver.Setup(r => r.ResolveAsync(OrgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SigningWallet);

        _hub.SetupGet(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group(It.IsAny<string>())).Returns(_clientProxy.Object);

        return new DeviceRevocationService(
            _statusList.Object,
            _resolver.Object,
            _hub.Object,
            NullLogger<DeviceRevocationService>.Instance);
    }

    [Fact]
    public async Task RevokeAsync_FlipsBitThenBroadcasts_OnPlatformUserGroup()
    {
        var sut = BuildSut();
        var seq = new MockSequence();

        _statusList.InSequence(seq)
            .Setup(s => s.FlipAsync(OrgId, 2, 9876, SigningWallet, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _clientProxy.InSequence(seq)
            .Setup(p => p.SendCoreAsync(
                "DeviceRevoked",
                It.Is<object?[]>(args => args.Length == 1 && (Guid)args[0]! == DeviceId),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await sut.RevokeAsync(OrgId, 2, 9876, DeviceId, PlatformUserId);

        _statusList.Verify();
        _clients.Verify(c => c.Group(WalletHub.GroupNameFor(PlatformUserId)), Times.Once);
        _clientProxy.Verify();
    }
}
