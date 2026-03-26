// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Xunit;

namespace Sorcha.Peer.Service.Tests.Discovery;

public class PeerListManagerBanTests : IDisposable
{
    private readonly PeerListManager _manager;

    public PeerListManagerBanTests()
    {
        var config = new PeerServiceConfiguration
        {
            PeerDiscovery = new PeerDiscoveryConfiguration
            {
                MaxPeersInList = 100,
                MinHealthyPeers = 5,
                RefreshIntervalMinutes = 15
            }
        };

        _manager = new PeerListManager(
            Mock.Of<ILogger<PeerListManager>>(),
            Options.Create(config));
    }

    [Fact]
    public async Task BanPeerAsync_WithDuration_SetsBanExpiresAt()
    {
        // Arrange
        var peer = new PeerNode { PeerId = "peer-1", Address = "10.0.0.1", Port = 5000 };
        await _manager.AddOrUpdatePeerAsync(peer);

        // Act
        var result = await _manager.BanPeerAsync("peer-1", "test", TimeSpan.FromHours(2));

        // Assert
        result.Should().BeTrue();
        var banned = _manager.GetPeer("peer-1");
        banned!.IsBanned.Should().BeTrue();
        banned.BanExpiresAt.Should().NotBeNull();
        banned.BanExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddHours(2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BanPeerAsync_WithoutDuration_LeavesBanExpiresAtNull()
    {
        // Arrange
        var peer = new PeerNode { PeerId = "peer-1", Address = "10.0.0.1", Port = 5000 };
        await _manager.AddOrUpdatePeerAsync(peer);

        // Act
        var result = await _manager.BanPeerAsync("peer-1", "permanent ban");

        // Assert
        result.Should().BeTrue();
        var banned = _manager.GetPeer("peer-1");
        banned!.IsBanned.Should().BeTrue();
        banned.BanExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task EvictStalePeersAsync_RemovesNonSeedPeersBeyondThreshold()
    {
        // Arrange
        var stalePeer = new PeerNode
        {
            PeerId = "stale",
            Address = "10.0.0.1",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-60)
        };
        var freshPeer = new PeerNode
        {
            PeerId = "fresh",
            Address = "10.0.0.2",
            Port = 5000,
            LastSeen = DateTimeOffset.UtcNow
        };
        await _manager.AddOrUpdatePeerAsync(stalePeer);
        await _manager.AddOrUpdatePeerAsync(freshPeer);

        // Act
        var evicted = await _manager.EvictStalePeersAsync(TimeSpan.FromMinutes(30));

        // Assert
        evicted.Should().ContainSingle("stale");
        _manager.GetPeer("stale").Should().BeNull();
        _manager.GetPeer("fresh").Should().NotBeNull();
    }

    [Fact]
    public async Task EvictStalePeersAsync_SkipsSeedNodes()
    {
        // Arrange
        var staleSeed = new PeerNode
        {
            PeerId = "seed",
            Address = "10.0.0.1",
            Port = 5000,
            IsSeedNode = true,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-120)
        };
        await _manager.AddOrUpdatePeerAsync(staleSeed);

        // Act
        var evicted = await _manager.EvictStalePeersAsync(TimeSpan.FromMinutes(30));

        // Assert
        evicted.Should().BeEmpty();
        _manager.GetPeer("seed").Should().NotBeNull();
    }

    [Fact]
    public async Task UnbanExpiredAsync_UnbansPeersWithExpiredBan()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "expired-ban",
            Address = "10.0.0.1",
            Port = 5000,
            IsBanned = true,
            BannedAt = DateTimeOffset.UtcNow.AddHours(-2),
            BanReason = "test",
            BanExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // expired 1 hour ago
        };
        await _manager.AddOrUpdatePeerAsync(peer);

        // Act
        var count = await _manager.UnbanExpiredAsync();

        // Assert
        count.Should().Be(1);
        var result = _manager.GetPeer("expired-ban");
        result!.IsBanned.Should().BeFalse();
        result.BanExpiresAt.Should().BeNull();
        result.BanReason.Should().BeNull();
    }

    [Fact]
    public async Task UnbanExpiredAsync_SkipsPermanentBans()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "perma-ban",
            Address = "10.0.0.1",
            Port = 5000,
            IsBanned = true,
            BannedAt = DateTimeOffset.UtcNow.AddDays(-30),
            BanReason = "permanent",
            BanExpiresAt = null // permanent
        };
        await _manager.AddOrUpdatePeerAsync(peer);

        // Act
        var count = await _manager.UnbanExpiredAsync();

        // Assert
        count.Should().Be(0);
        _manager.GetPeer("perma-ban")!.IsBanned.Should().BeTrue();
    }

    [Fact]
    public async Task UnbanExpiredAsync_SkipsActiveBans()
    {
        // Arrange
        var peer = new PeerNode
        {
            PeerId = "active-ban",
            Address = "10.0.0.1",
            Port = 5000,
            IsBanned = true,
            BannedAt = DateTimeOffset.UtcNow,
            BanReason = "active",
            BanExpiresAt = DateTimeOffset.UtcNow.AddHours(1) // still active
        };
        await _manager.AddOrUpdatePeerAsync(peer);

        // Act
        var count = await _manager.UnbanExpiredAsync();

        // Assert
        count.Should().Be(0);
        _manager.GetPeer("active-ban")!.IsBanned.Should().BeTrue();
    }

    public void Dispose()
    {
        _manager.Dispose();
    }
}
