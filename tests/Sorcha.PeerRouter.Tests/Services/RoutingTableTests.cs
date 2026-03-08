// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Services;

public class RoutingTableTests
{
    private readonly RouterConfiguration _config = new();
    private readonly EventBuffer _eventBuffer;
    private readonly RoutingTable _sut;

    public RoutingTableTests()
    {
        _eventBuffer = new EventBuffer(_config);
        _sut = new RoutingTable(_eventBuffer, _config);
    }

    private static PeerInfo CreatePeerInfo(string peerId = "peer-1", string address = "192.168.1.10", int port = 5000)
    {
        var info = new PeerInfo
        {
            PeerId = peerId,
            Address = $"{address}:{port}",
            Port = port,
            Capabilities = new PeerCapabilities
            {
                SupportsStreaming = true,
                SupportsTransactionDistribution = true,
                MaxTransactionSize = 10485760
            }
        };
        return info;
    }

    [Fact]
    public void RegisterPeer_NewPeer_ReturnsTrue()
    {
        var result = _sut.RegisterPeer(CreatePeerInfo());

        result.Should().BeTrue();
        _sut.TotalCount.Should().Be(1);
        _sut.HealthyCount.Should().Be(1);
    }

    [Fact]
    public void RegisterPeer_ExistingPeer_ReturnsFalse()
    {
        _sut.RegisterPeer(CreatePeerInfo());
        var result = _sut.RegisterPeer(CreatePeerInfo());

        result.Should().BeFalse();
        _sut.TotalCount.Should().Be(1);
    }

    [Fact]
    public void RegisterPeer_NewPeer_EmitsPeerConnectedEvent()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));

        var events = _eventBuffer.GetSnapshot();
        events.Should().ContainSingle(e => e.Type == RouterEventType.PeerConnected && e.PeerId == "peer-1");
    }

    [Fact]
    public void RegisterPeer_ExistingPeer_UpdatesLastSeen()
    {
        _sut.RegisterPeer(CreatePeerInfo());
        var firstSeen = _sut.GetPeer("peer-1")!.LastSeen;

        Thread.Sleep(10);
        _sut.RegisterPeer(CreatePeerInfo());
        var secondSeen = _sut.GetPeer("peer-1")!.LastSeen;

        secondSeen.Should().BeAfter(firstSeen);
    }

    [Fact]
    public void GetHealthyPeers_ExcludesUnhealthy()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.RegisterPeer(CreatePeerInfo("peer-2", "192.168.1.20"));

        // Mark peer-1 as unhealthy by sweeping with 0 timeout
        _sut.GetPeer("peer-1")!.LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);
        _sut.SweepUnhealthyPeers(TimeSpan.FromSeconds(1));

        var healthy = _sut.GetHealthyPeers();
        healthy.Should().ContainSingle(p => p.PeerId == "peer-2");
    }

    [Fact]
    public void GetHealthyPeers_ExcludesSpecificPeer()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.RegisterPeer(CreatePeerInfo("peer-2", "192.168.1.20"));

        var result = _sut.GetHealthyPeers(excludePeerId: "peer-1");
        result.Should().ContainSingle(p => p.PeerId == "peer-2");
    }

    [Fact]
    public void GetHealthyPeers_RespectsMaxPeers()
    {
        for (var i = 0; i < 10; i++)
            _sut.RegisterPeer(CreatePeerInfo($"peer-{i}", $"192.168.1.{i}"));

        var result = _sut.GetHealthyPeers(maxPeers: 3);
        result.Should().HaveCount(3);
    }

    [Fact]
    public void TouchPeer_UpdatesLastSeenAndHeartbeatCount()
    {
        _sut.RegisterPeer(CreatePeerInfo());
        var entry = _sut.GetPeer("peer-1")!;
        var initialSeen = entry.LastSeen;

        Thread.Sleep(10);
        _sut.TouchPeer("peer-1");

        entry.LastSeen.Should().BeAfter(initialSeen);
        entry.HeartbeatCount.Should().Be(1);
    }

    [Fact]
    public void TouchPeer_UnknownPeer_ReturnsFalse()
    {
        _sut.TouchPeer("unknown").Should().BeFalse();
    }

    [Fact]
    public void UpdateRegisterVersions_StoresVersions()
    {
        _sut.RegisterPeer(CreatePeerInfo());
        var versions = new Dictionary<string, long> { ["reg-1"] = 42, ["reg-2"] = 100 };

        _sut.UpdateRegisterVersions("peer-1", versions);

        var entry = _sut.GetPeer("peer-1")!;
        entry.RegisterVersions.Should().ContainKey("reg-1").WhoseValue.Should().Be(42);
        entry.RegisterVersions.Should().ContainKey("reg-2").WhoseValue.Should().Be(100);
    }

    [Fact]
    public void FindPeersForRegister_FiltersCorrectly()
    {
        var peer1 = CreatePeerInfo("peer-1");
        peer1.AdvertisedRegisters.Add(new PeerRegisterAdvertisement { RegisterId = "reg-1", HasFullReplica = true });
        _sut.RegisterPeer(peer1);

        var peer2 = CreatePeerInfo("peer-2", "192.168.1.20");
        peer2.AdvertisedRegisters.Add(new PeerRegisterAdvertisement { RegisterId = "reg-2", HasFullReplica = true });
        _sut.RegisterPeer(peer2);

        var result = _sut.FindPeersForRegister("reg-1");
        result.Should().ContainSingle(p => p.PeerId == "peer-1");
    }

    [Fact]
    public void FindPeersForRegister_RequireFullReplica_FiltersPartial()
    {
        var peer = CreatePeerInfo("peer-1");
        peer.AdvertisedRegisters.Add(new PeerRegisterAdvertisement { RegisterId = "reg-1", HasFullReplica = false });
        _sut.RegisterPeer(peer);

        var result = _sut.FindPeersForRegister("reg-1", requireFullReplica: true);
        result.Should().BeEmpty();
    }

    [Fact]
    public void SweepUnhealthyPeers_MarksTimedOutPeers()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.GetPeer("peer-1")!.LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);

        var result = _sut.SweepUnhealthyPeers(TimeSpan.FromSeconds(60));

        result.Should().ContainSingle(p => p.PeerId == "peer-1");
        _sut.GetPeer("peer-1")!.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public void SweepUnhealthyPeers_DoesNotDoubleMarkUnhealthy()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.GetPeer("peer-1")!.LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);

        _sut.SweepUnhealthyPeers(TimeSpan.FromSeconds(60));
        var result = _sut.SweepUnhealthyPeers(TimeSpan.FromSeconds(60));

        result.Should().BeEmpty();
    }

    [Fact]
    public void SweepUnhealthyPeers_EmitsPeerDisconnectedEvent()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.GetPeer("peer-1")!.LastSeen = DateTimeOffset.UtcNow.AddMinutes(-5);

        _sut.SweepUnhealthyPeers(TimeSpan.FromSeconds(60));

        var events = _eventBuffer.GetSnapshot();
        events.Should().Contain(e => e.Type == RouterEventType.PeerDisconnected && e.PeerId == "peer-1");
    }

    [Fact]
    public void GetAllPeers_IncludesUnhealthy()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));
        _sut.GetPeer("peer-1")!.IsHealthy = false;

        var result = _sut.GetAllPeers();
        result.Should().ContainSingle(p => p.PeerId == "peer-1");
    }

    [Fact]
    public void RegisterPeer_SelfPeerId_IsRejected()
    {
        var config = new RouterConfiguration { PeerId = "n0" };
        var table = new RoutingTable(new EventBuffer(config), config);

        var result = table.RegisterPeer(CreatePeerInfo("n0", "n0.sorcha.dev", 443));

        result.Should().BeFalse();
        table.TotalCount.Should().Be(0);
    }

    [Fact]
    public void RegisterPeer_SelfPeerId_CaseInsensitive()
    {
        var config = new RouterConfiguration { PeerId = "N0" };
        var table = new RoutingTable(new EventBuffer(config), config);

        var result = table.RegisterPeer(CreatePeerInfo("n0", "n0.sorcha.dev", 443));

        result.Should().BeFalse();
        table.TotalCount.Should().Be(0);
    }

    [Fact]
    public void RegisterPeer_EmptyPeerId_AllowsAll()
    {
        // Default config has empty PeerId — no self-filtering
        var result = _sut.RegisterPeer(CreatePeerInfo("any-peer"));

        result.Should().BeTrue();
        _sut.TotalCount.Should().Be(1);
    }

    [Fact]
    public void RegisterPeer_UnhealthyPeerReregisters_BecomesHealthy()
    {
        _sut.RegisterPeer(CreatePeerInfo());
        _sut.GetPeer("peer-1")!.IsHealthy = false;

        _sut.RegisterPeer(CreatePeerInfo());
        _sut.GetPeer("peer-1")!.IsHealthy.Should().BeTrue();
    }
}
