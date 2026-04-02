// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Tests.Replication;

public class RegisterAdvertisementServiceTests : IDisposable
{
    private readonly RegisterAdvertisementService _service;
    private readonly PeerListManager _peerListManager;

    public RegisterAdvertisementServiceTests()
    {
        _peerListManager = new PeerListManager(
            new Mock<ILogger<PeerListManager>>().Object,
            Options.Create(new PeerServiceConfiguration
            {
                NodeId = "test-node",
                PeerDiscovery = new PeerDiscoveryConfiguration
                {
                    MaxPeersInList = 100,
                    MinHealthyPeers = 5,
                    RefreshIntervalMinutes = 15
                },
                SeedNodes = new SeedNodeConfiguration()
            }));

        _service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterAdvertisementService(null!, _peerListManager);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullPeerListManager_ThrowsArgumentNullException()
    {
        Action act = () => new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AdvertiseRegister_AddsToLocalAdvertisements()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);

        var ad = _service.GetAdvertisement("reg-1");
        ad.Should().NotBeNull();
        ad!.RegisterId.Should().Be("reg-1");
        ad.SyncState.Should().Be(RegisterSyncState.Active);
        ad.LatestVersion.Should().Be(100);
        ad.LatestDocketVersion.Should().Be(10);
        ad.IsPublic.Should().BeTrue();
    }

    [Fact]
    public void AdvertiseRegister_OverwritesExisting()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Syncing, 50);
        _service.AdvertiseRegister("reg-1", RegisterSyncState.FullyReplicated, 100);

        var ad = _service.GetAdvertisement("reg-1");
        ad!.SyncState.Should().Be(RegisterSyncState.FullyReplicated);
        ad.LatestVersion.Should().Be(100);
    }

    [Fact]
    public void UpdateRegisterVersion_ExistingRegister_UpdatesVersion()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100);

        _service.UpdateRegisterVersion("reg-1", 200, 20);

        var ad = _service.GetAdvertisement("reg-1");
        ad!.LatestVersion.Should().Be(200);
        ad.LatestDocketVersion.Should().Be(20);
    }

    [Fact]
    public void UpdateRegisterVersion_NonExistent_NoOp()
    {
        // Should not throw
        _service.UpdateRegisterVersion("nonexistent", 200, 20);

        _service.GetAdvertisement("nonexistent").Should().BeNull();
    }

    [Fact]
    public void RemoveAdvertisement_ExistingRegister_Removes()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active);

        _service.RemoveAdvertisement("reg-1");

        _service.GetAdvertisement("reg-1").Should().BeNull();
    }

    [Fact]
    public void RemoveAdvertisement_NonExistent_NoOp()
    {
        // Should not throw
        _service.RemoveAdvertisement("nonexistent");
    }

    [Fact]
    public void GetLocalAdvertisements_ReturnsAllAdvertisements()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active);
        _service.AdvertiseRegister("reg-2", RegisterSyncState.FullyReplicated);

        var ads = _service.GetLocalAdvertisements();

        ads.Should().HaveCount(2);
    }

    [Fact]
    public void GetPublicAdvertisements_FiltersNonPublic()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, isPublic: true);
        _service.AdvertiseRegister("reg-2", RegisterSyncState.Active, isPublic: false);
        _service.AdvertiseRegister("reg-3", RegisterSyncState.Active, isPublic: true);

        var ads = _service.GetPublicAdvertisements();

        ads.Should().HaveCount(2);
        ads.Select(a => a.RegisterId).Should().BeEquivalentTo(["reg-1", "reg-3"]);
    }

    [Fact]
    public void GetAdvertisement_NonExistent_ReturnsNull()
    {
        _service.GetAdvertisement("nonexistent").Should().BeNull();
    }

    [Fact]
    public void BuildPeerRegisterInfoList_ConvertsToRegisterInfo()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, isPublic: true);
        _service.AdvertiseRegister("reg-2", RegisterSyncState.FullyReplicated, 200, isPublic: false);

        var infos = _service.BuildPeerRegisterInfoList();

        infos.Should().HaveCount(2);
        var reg1 = infos.First(i => i.RegisterId == "reg-1");
        reg1.SyncState.Should().Be(RegisterSyncState.Active);
        reg1.LatestVersion.Should().Be(100);
        reg1.IsPublic.Should().BeTrue();

        var reg2 = infos.First(i => i.RegisterId == "reg-2");
        reg2.SyncState.Should().Be(RegisterSyncState.FullyReplicated);
        reg2.CanServeFullReplica.Should().BeTrue();
    }

    [Fact]
    public void DetectVersionLag_RemoteAhead_ReturnsLaggingRegisters()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100);
        _service.AdvertiseRegister("reg-2", RegisterSyncState.Active, 200);

        var remoteVersions = new Dictionary<string, long>
        {
            { "reg-1", 150 }, // remote ahead
            { "reg-2", 100 }, // we're ahead
        };

        var lagging = _service.DetectVersionLag(remoteVersions);

        lagging.Should().ContainSingle().Which.Should().Be("reg-1");
    }

    [Fact]
    public void DetectVersionLag_AllUpToDate_ReturnsEmpty()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100);

        var remoteVersions = new Dictionary<string, long>
        {
            { "reg-1", 100 }
        };

        _service.DetectVersionLag(remoteVersions).Should().BeEmpty();
    }

    [Fact]
    public void DetectVersionLag_UnknownRegister_Ignored()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100);

        var remoteVersions = new Dictionary<string, long>
        {
            { "reg-1", 50 },
            { "reg-unknown", 500 } // not in local ads
        };

        _service.DetectVersionLag(remoteVersions).Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessRemoteAdvertisementsAsync_KnownPeer_UpdatesRegisters()
    {
        // Add a peer first
        var peer = new PeerNode
        {
            PeerId = "peer-1",
            Address = "192.168.1.100",
            Port = 5001
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        // Process advertisements from that peer
        var ads = new List<PeerRegisterInfo>
        {
            new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 100 },
            new() { RegisterId = "reg-2", SyncState = RegisterSyncState.FullyReplicated, LatestVersion = 200 }
        };

        await _service.ProcessRemoteAdvertisementsAsync("peer-1", ads);

        var updatedPeer = _peerListManager.GetPeer("peer-1");
        updatedPeer!.AdvertisedRegisters.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessRemoteAdvertisementsAsync_UnknownPeer_Ignored()
    {
        var ads = new List<PeerRegisterInfo>
        {
            new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active }
        };

        // Should not throw
        await _service.ProcessRemoteAdvertisementsAsync("unknown-peer", ads);
    }

    // === GetNetworkAdvertisedRegisters Tests ===

    [Fact]
    public async Task GetNetworkAdvertisedRegisters_AggregatesAcrossPeers()
    {
        // Arrange - add peers with advertised registers
        var peer1 = new PeerNode
        {
            PeerId = "peer-1", Address = "192.168.1.100", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.FullyReplicated, LatestVersion = 100, IsPublic = true },
                new() { RegisterId = "reg-2", SyncState = RegisterSyncState.Active, LatestVersion = 50, IsPublic = true }
            }
        };
        var peer2 = new PeerNode
        {
            PeerId = "peer-2", Address = "192.168.1.101", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.FullyReplicated, LatestVersion = 150, IsPublic = true }
            }
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer1);
        await _peerListManager.AddOrUpdatePeerAsync(peer2);

        // Act
        var result = _service.GetNetworkAdvertisedRegisters();

        // Assert
        result.Should().HaveCount(2);

        var reg1 = result.First(r => r.RegisterId == "reg-1");
        reg1.PeerCount.Should().Be(2);
        reg1.LatestVersion.Should().Be(150); // max version
        reg1.FullReplicaPeerCount.Should().Be(2);
        reg1.IsPublic.Should().BeTrue();

        var reg2 = result.First(r => r.RegisterId == "reg-2");
        reg2.PeerCount.Should().Be(1);
        reg2.LatestVersion.Should().Be(50);
    }

    [Fact]
    public async Task GetNetworkAdvertisedRegisters_ExcludesPrivateRegisters()
    {
        var peer = new PeerNode
        {
            PeerId = "peer-1", Address = "192.168.1.100", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "public-reg", SyncState = RegisterSyncState.Active, LatestVersion = 100, IsPublic = true },
                new() { RegisterId = "private-reg", SyncState = RegisterSyncState.Active, LatestVersion = 200, IsPublic = false }
            }
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var result = _service.GetNetworkAdvertisedRegisters();

        result.Should().ContainSingle().Which.RegisterId.Should().Be("public-reg");
    }

    [Fact]
    public async Task GetNetworkAdvertisedRegisters_ExcludesBannedPeers()
    {
        var peer1 = new PeerNode
        {
            PeerId = "good-peer", Address = "192.168.1.100", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 100, IsPublic = true }
            }
        };
        var peer2 = new PeerNode
        {
            PeerId = "banned-peer", Address = "192.168.1.101", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.FullyReplicated, LatestVersion = 200, IsPublic = true }
            }
        };
        await _peerListManager.AddOrUpdatePeerAsync(peer1);
        await _peerListManager.AddOrUpdatePeerAsync(peer2);
        await _peerListManager.BanPeerAsync("banned-peer");

        var result = _service.GetNetworkAdvertisedRegisters();

        result.Should().ContainSingle();
        result.First().PeerCount.Should().Be(1);
        result.First().LatestVersion.Should().Be(100); // banned peer's 200 excluded
    }

    [Fact]
    public async Task GetNetworkAdvertisedRegisters_ExcludesStaleDisconnectedPeers()
    {
        // Arrange - one healthy peer, one stale peer (last seen 60 min ago)
        var healthyPeer = new PeerNode
        {
            PeerId = "healthy-peer", Address = "192.168.1.100", Port = 5001,
            LastSeen = DateTimeOffset.UtcNow,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 100, IsPublic = true }
            }
        };
        var stalePeer = new PeerNode
        {
            PeerId = "stale-peer", Address = "192.168.1.101", Port = 5001,
            LastSeen = DateTimeOffset.UtcNow.AddMinutes(-60), // Well past healthy threshold
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-2", SyncState = RegisterSyncState.Active, LatestVersion = 200, IsPublic = true }
            }
        };
        await _peerListManager.AddOrUpdatePeerAsync(healthyPeer);
        await _peerListManager.AddOrUpdatePeerAsync(stalePeer);

        // Act
        var result = _service.GetNetworkAdvertisedRegisters();

        // Assert - only the healthy peer's register should appear
        result.Should().ContainSingle();
        result.First().RegisterId.Should().Be("reg-1");
    }

    [Fact]
    public async Task GetNetworkAdvertisedRegisters_ExcludesHighFailureCountPeers()
    {
        // Arrange - one healthy peer, one with many failures
        var healthyPeer = new PeerNode
        {
            PeerId = "healthy-peer", Address = "192.168.1.100", Port = 5001,
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 100, IsPublic = true }
            }
        };
        var failingPeer = new PeerNode
        {
            PeerId = "failing-peer", Address = "192.168.1.101", Port = 5001,
            FailureCount = 4, // Unhealthy: threshold is FailureCount < 3 (max healthy is 2)
            AdvertisedRegisters = new List<PeerRegisterInfo>
            {
                new() { RegisterId = "reg-2", SyncState = RegisterSyncState.Active, LatestVersion = 200, IsPublic = true }
            }
        };
        await _peerListManager.AddOrUpdatePeerAsync(healthyPeer);
        await _peerListManager.AddOrUpdatePeerAsync(failingPeer);

        // Act
        var result = _service.GetNetworkAdvertisedRegisters();

        // Assert - only the healthy peer's register should appear
        result.Should().ContainSingle();
        result.First().RegisterId.Should().Be("reg-1");
    }

    [Fact]
    public void GetNetworkAdvertisedRegisters_EmptyNetwork_ReturnsEmpty()
    {
        var result = _service.GetNetworkAdvertisedRegisters();
        result.Should().BeEmpty();
    }

    // === Idempotency Tests (FR-009) ===

    [Fact]
    public void AdvertiseRegister_Idempotent_SkipsUnchangedWrite()
    {
        // First call — new advertisement
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);
        var firstAd = _service.GetAdvertisement("reg-1");
        var firstTimestamp = firstAd!.LastUpdated;

        // Small delay to ensure timestamp would differ
        Thread.Sleep(10);

        // Second call — same data → should be idempotent
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);
        var secondAd = _service.GetAdvertisement("reg-1");

        // Timestamp unchanged means the write was skipped
        secondAd!.LastUpdated.Should().Be(firstTimestamp);
    }

    [Fact]
    public void AdvertiseRegister_ChangedVersion_WritesThrough()
    {
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);
        var firstAd = _service.GetAdvertisement("reg-1");
        var firstTimestamp = firstAd!.LastUpdated;

        Thread.Sleep(10);

        // Changed version → should update
        _service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 200, 10, true);
        var secondAd = _service.GetAdvertisement("reg-1");

        secondAd!.LatestVersion.Should().Be(200);
        secondAd.LastUpdated.Should().BeAfter(firstTimestamp);
    }

    // === Redis Write-Through Tests ===

    [Fact]
    public void AdvertiseRegister_WithStore_CallsSetLocalAsync()
    {
        var mockStore = new Mock<IRedisAdvertisementStore>();
        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);

        mockStore.Verify(s => s.SetLocalAsync(
            It.Is<LocalRegisterAdvertisement>(a => a.RegisterId == "reg-1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UpdateRegisterVersion_WithStore_CallsSetLocalAsync()
    {
        var mockStore = new Mock<IRedisAdvertisementStore>();
        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        service.AdvertiseRegister("reg-1", RegisterSyncState.Active, 100, 10, true);

        // Reset so we only verify the UpdateRegisterVersion call
        mockStore.Invocations.Clear();

        service.UpdateRegisterVersion("reg-1", 200, 20);

        mockStore.Verify(s => s.SetLocalAsync(
            It.Is<LocalRegisterAdvertisement>(a =>
                a.RegisterId == "reg-1" &&
                a.LatestVersion == 200 &&
                a.LatestDocketVersion == 20),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void RemoveAdvertisement_WithStore_CallsRemoveLocalAsync()
    {
        var mockStore = new Mock<IRedisAdvertisementStore>();
        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        service.AdvertiseRegister("reg-1", RegisterSyncState.Active);
        service.RemoveAdvertisement("reg-1");

        mockStore.Verify(s => s.RemoveLocalAsync(
            "reg-1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadFromRedisAsync_WithStore_PopulatesLocalAdvertisements()
    {
        var mockStore = new Mock<IRedisAdvertisementStore>();
        mockStore.Setup(s => s.GetAllLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalRegisterAdvertisement>
            {
                new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, IsPublic = true },
                new() { RegisterId = "reg-2", SyncState = RegisterSyncState.FullyReplicated, IsPublic = true }
            });
        mockStore.Setup(s => s.GetAllRemoteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, List<PeerRegisterInfo>>());

        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        await service.LoadFromRedisAsync();

        service.GetLocalAdvertisements().Should().HaveCount(2);
        service.GetAdvertisement("reg-1").Should().NotBeNull();
        service.GetAdvertisement("reg-2").Should().NotBeNull();
    }

    // === Remote Advertisement Persistence Tests (US4) ===

    [Fact]
    public async Task ProcessRemoteAdvertisementsAsync_WithStore_CallsSetRemoteAsync()
    {
        var mockStore = new Mock<IRedisAdvertisementStore>();
        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        var peer = new PeerNode { PeerId = "peer-1", Address = "192.168.1.100", Port = 5001 };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var ads = new List<PeerRegisterInfo>
        {
            new() { RegisterId = "reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 100, IsPublic = true },
            new() { RegisterId = "reg-2", SyncState = RegisterSyncState.FullyReplicated, LatestVersion = 200, IsPublic = true }
        };

        await service.ProcessRemoteAdvertisementsAsync("peer-1", ads);

        mockStore.Verify(s => s.SetRemoteAsync(
            "peer-1",
            It.Is<PeerRegisterInfo>(r => r.RegisterId == "reg-1"),
            It.IsAny<CancellationToken>()), Times.Once);
        mockStore.Verify(s => s.SetRemoteAsync(
            "peer-1",
            It.Is<PeerRegisterInfo>(r => r.RegisterId == "reg-2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void GetNetworkAdvertisedRegisters_IncludesLocalPublicAds()
    {
        _service.AdvertiseRegister("local-reg", RegisterSyncState.Active, 100, 10, isPublic: true);

        var result = _service.GetNetworkAdvertisedRegisters();

        result.Should().ContainSingle().Which.RegisterId.Should().Be("local-reg");
    }

    [Fact]
    public void GetNetworkAdvertisedRegisters_ExcludesLocalPrivateAds()
    {
        _service.AdvertiseRegister("private-reg", RegisterSyncState.Active, 100, 10, isPublic: false);

        var result = _service.GetNetworkAdvertisedRegisters();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFromRedisAsync_RestoresRemoteAdsFromRedis()
    {
        var peer = new PeerNode { PeerId = "peer-A", Address = "192.168.1.100", Port = 5001 };
        await _peerListManager.AddOrUpdatePeerAsync(peer);

        var mockStore = new Mock<IRedisAdvertisementStore>();
        mockStore.Setup(s => s.GetAllLocalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LocalRegisterAdvertisement>());
        mockStore.Setup(s => s.GetAllRemoteAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, List<PeerRegisterInfo>>
            {
                ["peer-A"] = new List<PeerRegisterInfo>
                {
                    new() { RegisterId = "remote-reg-1", SyncState = RegisterSyncState.Active, LatestVersion = 50, IsPublic = true }
                }
            });

        var service = new RegisterAdvertisementService(
            new Mock<ILogger<RegisterAdvertisementService>>().Object,
            _peerListManager,
            mockStore.Object);

        await service.LoadFromRedisAsync();

        var loadedPeer = _peerListManager.GetPeer("peer-A");
        loadedPeer!.AdvertisedRegisters.Should().ContainSingle()
            .Which.RegisterId.Should().Be("remote-reg-1");
    }

    public void Dispose()
    {
        _peerListManager.Dispose();
    }
}
