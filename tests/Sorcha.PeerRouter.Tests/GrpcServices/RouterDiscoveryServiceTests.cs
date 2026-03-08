// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

public sealed class RouterDiscoveryServiceTests
{
    private readonly RouterConfiguration _config;
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterDiscoveryService _service;

    public RouterDiscoveryServiceTests()
    {
        _config = new RouterConfiguration { PeerId = "router-0" };
        _eventBuffer = new EventBuffer(_config);
        _routingTable = new RoutingTable(_eventBuffer, _config);
        _service = new RouterDiscoveryService(
            _routingTable,
            _eventBuffer,
            _config,
            NullLogger<RouterDiscoveryService>.Instance);
    }

    #region RegisterPeer

    [Fact]
    public async Task RegisterPeer_NewPeer_ReturnsSuccessAndRegistered()
    {
        var request = new RegisterPeerRequest
        {
            PeerInfo = CreatePeerInfo("peer-1", "10.0.0.1", 5000)
        };

        var response = await _service.RegisterPeer(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Peer registered");
        _routingTable.TotalCount.Should().Be(1);
        _routingTable.GetPeer("peer-1").Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterPeer_ExistingPeer_ReturnsSuccessAndUpdated()
    {
        var request = new RegisterPeerRequest
        {
            PeerInfo = CreatePeerInfo("peer-1", "10.0.0.1", 5000)
        };
        await _service.RegisterPeer(request, TestServerCallContext.Create());

        // Register again with updated address
        var updateRequest = new RegisterPeerRequest
        {
            PeerInfo = CreatePeerInfo("peer-1", "10.0.0.2", 6000)
        };
        var response = await _service.RegisterPeer(updateRequest, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        response.Message.Should().Be("Peer updated");
        _routingTable.TotalCount.Should().Be(1);
        _routingTable.GetPeer("peer-1")!.Port.Should().Be(6000);
    }

    [Fact]
    public async Task RegisterPeer_NullPeerInfo_ReturnsFailure()
    {
        var request = new RegisterPeerRequest(); // PeerInfo is null

        var response = await _service.RegisterPeer(request, TestServerCallContext.Create());

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("required");
    }

    [Fact]
    public async Task RegisterPeer_SelfPeerId_ReturnsSuccessButIgnored()
    {
        var request = new RegisterPeerRequest
        {
            PeerInfo = CreatePeerInfo("router-0", "n0.sorcha.dev", 443)
        };

        var response = await _service.RegisterPeer(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        response.Message.Should().Contain("ignored");
        _routingTable.TotalCount.Should().Be(0);
    }

    #endregion

    #region GetPeerList

    [Fact]
    public async Task GetPeerList_WithPeers_ReturnsHealthyPeers()
    {
        await RegisterPeerHelper("peer-1", "10.0.0.1", 5001);
        await RegisterPeerHelper("peer-2", "10.0.0.2", 5002);

        var request = new PeerListRequest
        {
            RequestingPeerId = "peer-1",
            MaxPeers = 10
        };

        var response = await _service.GetPeerList(request, TestServerCallContext.Create());

        response.Peers.Should().HaveCount(1, "requesting peer should be excluded");
        response.Peers[0].PeerId.Should().Be("peer-2");
        response.TotalPeers.Should().Be(1);
    }

    [Fact]
    public async Task GetPeerList_NoPeers_ReturnsEmpty()
    {
        var request = new PeerListRequest
        {
            RequestingPeerId = "peer-1",
            MaxPeers = 10
        };

        var response = await _service.GetPeerList(request, TestServerCallContext.Create());

        response.Peers.Should().BeEmpty();
        response.TotalPeers.Should().Be(0);
    }

    [Fact]
    public async Task GetPeerList_RespectsMaxPeers()
    {
        for (var i = 0; i < 5; i++)
        {
            await RegisterPeerHelper($"peer-{i}", $"10.0.0.{i}", 5000 + i);
        }

        var request = new PeerListRequest
        {
            RequestingPeerId = "requester",
            MaxPeers = 3
        };

        var response = await _service.GetPeerList(request, TestServerCallContext.Create());

        response.Peers.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetPeerList_EmitsEvent()
    {
        var initialCount = _eventBuffer.Count;
        var request = new PeerListRequest { RequestingPeerId = "peer-1" };

        await _service.GetPeerList(request, TestServerCallContext.Create());

        _eventBuffer.Count.Should().BeGreaterThan(initialCount);
        var events = _eventBuffer.GetSnapshot();
        events.Last().Type.Should().Be(RouterEventType.PeerListRequested);
    }

    #endregion

    #region Ping

    [Fact]
    public async Task Ping_KnownPeer_ReturnsOnline()
    {
        await RegisterPeerHelper("peer-1", "10.0.0.1", 5000);

        var response = await _service.Ping(
            new PingRequest { PeerId = "peer-1" },
            TestServerCallContext.Create());

        response.PeerId.Should().Be("peer-1");
        response.Status.Should().Be(PeerStatus.Online);
        response.Timestamp.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Ping_UnknownPeer_ReturnsUnknown()
    {
        var response = await _service.Ping(
            new PingRequest { PeerId = "nonexistent" },
            TestServerCallContext.Create());

        response.PeerId.Should().Be("nonexistent");
        response.Status.Should().Be(PeerStatus.Unknown);
    }

    [Fact]
    public async Task Ping_KnownPeer_EmitsHeartbeatEvent()
    {
        await RegisterPeerHelper("peer-1", "10.0.0.1", 5000);
        var initialCount = _eventBuffer.Count;

        await _service.Ping(
            new PingRequest { PeerId = "peer-1" },
            TestServerCallContext.Create());

        _eventBuffer.Count.Should().BeGreaterThan(initialCount);
        var events = _eventBuffer.GetSnapshot();
        events.Last().Type.Should().Be(RouterEventType.PeerHeartbeat);
        events.Last().PeerId.Should().Be("peer-1");
    }

    #endregion

    #region ExchangePeers

    [Fact]
    public async Task ExchangePeers_RegistersIncomingPeers_ReturnsKnownPeers()
    {
        // Pre-register a peer so there's something to return
        await RegisterPeerHelper("existing-peer", "10.0.0.99", 5099);

        var request = new PeerExchangeRequest
        {
            PeerId = "exchanger",
            MaxPeers = 10
        };
        request.KnownPeers.Add(CreatePeerInfo("new-peer-1", "10.0.0.1", 5001));
        request.KnownPeers.Add(CreatePeerInfo("new-peer-2", "10.0.0.2", 5002));

        var response = await _service.ExchangePeers(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        // The incoming peers should now be in routing table
        _routingTable.GetPeer("new-peer-1").Should().NotBeNull();
        _routingTable.GetPeer("new-peer-2").Should().NotBeNull();
        // Response should include existing peer and the newly added peers (excluding exchanger)
        response.KnownPeers.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExchangePeers_EmptyKnownPeers_StillReturnsKnownPeers()
    {
        await RegisterPeerHelper("peer-1", "10.0.0.1", 5001);

        var request = new PeerExchangeRequest
        {
            PeerId = "exchanger",
            MaxPeers = 10
        };

        var response = await _service.ExchangePeers(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        response.KnownPeers.Should().HaveCount(1);
        response.KnownPeers[0].PeerId.Should().Be("peer-1");
    }

    [Fact]
    public async Task ExchangePeers_FiltersSelfFromIncomingPeers()
    {
        var request = new PeerExchangeRequest
        {
            PeerId = "exchanger",
            MaxPeers = 10
        };
        request.KnownPeers.Add(CreatePeerInfo("router-0", "n0.sorcha.dev", 443)); // self
        request.KnownPeers.Add(CreatePeerInfo("real-peer", "10.0.0.1", 5001));

        var response = await _service.ExchangePeers(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        _routingTable.GetPeer("router-0").Should().BeNull("router should not register itself");
        _routingTable.GetPeer("real-peer").Should().NotBeNull();
    }

    [Fact]
    public async Task ExchangePeers_EmitsPeerExchangedEvent()
    {
        var request = new PeerExchangeRequest
        {
            PeerId = "exchanger",
            MaxPeers = 10
        };
        request.KnownPeers.Add(CreatePeerInfo("new-peer", "10.0.0.1", 5001));

        await _service.ExchangePeers(request, TestServerCallContext.Create());

        var events = _eventBuffer.GetSnapshot();
        events.Should().Contain(e => e.Type == RouterEventType.PeerExchanged);
    }

    #endregion

    #region FindPeersForRegister

    [Fact]
    public async Task FindPeersForRegister_MatchingPeers_ReturnsThem()
    {
        var peerInfo = CreatePeerInfo("peer-1", "10.0.0.1", 5001);
        peerInfo.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "register-abc",
            HasFullReplica = true,
            LatestVersion = 42
        });
        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = peerInfo },
            TestServerCallContext.Create());

        var request = new FindPeersForRegisterRequest
        {
            RegisterId = "register-abc",
            RequestingPeerId = "requester",
            MaxPeers = 10
        };

        var response = await _service.FindPeersForRegister(request, TestServerCallContext.Create());

        response.Peers.Should().HaveCount(1);
        response.Peers[0].PeerId.Should().Be("peer-1");
        response.TotalPeers.Should().Be(1);
    }

    [Fact]
    public async Task FindPeersForRegister_NoMatchingPeers_ReturnsEmpty()
    {
        await RegisterPeerHelper("peer-1", "10.0.0.1", 5001);

        var request = new FindPeersForRegisterRequest
        {
            RegisterId = "nonexistent-register",
            RequestingPeerId = "requester",
            MaxPeers = 10
        };

        var response = await _service.FindPeersForRegister(request, TestServerCallContext.Create());

        response.Peers.Should().BeEmpty();
        response.TotalPeers.Should().Be(0);
    }

    [Fact]
    public async Task FindPeersForRegister_RequireFullReplica_FiltersPartialPeers()
    {
        // Peer with full replica
        var fullPeer = CreatePeerInfo("full-peer", "10.0.0.1", 5001);
        fullPeer.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "register-abc",
            HasFullReplica = true
        });
        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = fullPeer },
            TestServerCallContext.Create());

        // Peer with partial replica
        var partialPeer = CreatePeerInfo("partial-peer", "10.0.0.2", 5002);
        partialPeer.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "register-abc",
            HasFullReplica = false
        });
        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = partialPeer },
            TestServerCallContext.Create());

        var request = new FindPeersForRegisterRequest
        {
            RegisterId = "register-abc",
            RequestingPeerId = "requester",
            RequireFullReplica = true,
            MaxPeers = 10
        };

        var response = await _service.FindPeersForRegister(request, TestServerCallContext.Create());

        response.Peers.Should().HaveCount(1);
        response.Peers[0].PeerId.Should().Be("full-peer");
    }

    [Fact]
    public async Task FindPeersForRegister_ExcludesRequestingPeer()
    {
        var peerInfo = CreatePeerInfo("requester", "10.0.0.1", 5001);
        peerInfo.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "register-abc",
            HasFullReplica = true
        });
        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = peerInfo },
            TestServerCallContext.Create());

        var request = new FindPeersForRegisterRequest
        {
            RegisterId = "register-abc",
            RequestingPeerId = "requester",
            MaxPeers = 10
        };

        var response = await _service.FindPeersForRegister(request, TestServerCallContext.Create());

        response.Peers.Should().BeEmpty();
    }

    #endregion

    #region PeerInfo Mapping

    [Fact]
    public async Task GetPeerList_MapsPeerInfoCorrectly()
    {
        var peerInfo = CreatePeerInfo("peer-1", "10.0.0.1", 5001);
        peerInfo.Capabilities = new PeerCapabilities
        {
            SupportsStreaming = true,
            SupportsTransactionDistribution = true,
            MaxTransactionSize = 1024
        };
        peerInfo.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "reg-1",
            HasFullReplica = true,
            LatestVersion = 10
        });

        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = peerInfo },
            TestServerCallContext.Create());

        var response = await _service.GetPeerList(
            new PeerListRequest { RequestingPeerId = "other", MaxPeers = 10 },
            TestServerCallContext.Create());

        var returnedPeer = response.Peers[0];
        returnedPeer.PeerId.Should().Be("peer-1");
        returnedPeer.Address.Should().Be("10.0.0.1");
        returnedPeer.Port.Should().Be(5001);
        returnedPeer.LastSeen.Should().BeGreaterThan(0);
        returnedPeer.Capabilities.Should().NotBeNull();
        returnedPeer.Capabilities.SupportsStreaming.Should().BeTrue();
        returnedPeer.AdvertisedRegisters.Should().HaveCount(1);
        returnedPeer.AdvertisedRegisters[0].RegisterId.Should().Be("reg-1");
    }

    #endregion

    #region Helpers

    private async Task RegisterPeerHelper(string peerId, string address, int port)
    {
        await _service.RegisterPeer(
            new RegisterPeerRequest { PeerInfo = CreatePeerInfo(peerId, address, port) },
            TestServerCallContext.Create());
    }

    private static PeerInfo CreatePeerInfo(string peerId, string address, int port) => new()
    {
        PeerId = peerId,
        Address = address,
        Port = port
    };

    #endregion
}
