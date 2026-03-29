// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

public sealed class RouterHeartbeatAdvertisementTests
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterHeartbeatService _service;

    public RouterHeartbeatAdvertisementTests()
    {
        var config = new RouterConfiguration();
        _eventBuffer = new EventBuffer(config);
        _routingTable = new RoutingTable(_eventBuffer, config);
        _service = new RouterHeartbeatService(
            _routingTable,
            _eventBuffer,
            NullLogger<RouterHeartbeatService>.Instance);
    }

    private void RegisterPeer(string peerId, string address = "10.0.0.1", int port = 5000)
    {
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = peerId,
            Address = address,
            Port = port
        });
    }

    [Fact]
    public async Task SendHeartbeat_WithAdvertisedRegisters_StoresInRoutingTable()
    {
        RegisterPeer("peer-1");

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1,
            AdvertisedRegisters =
            {
                new RegisterAdvertisement
                {
                    RegisterId = "reg-a",
                    SyncState = SyncStateProto.FullyReplicated,
                    LatestVersion = 42,
                    IsPublic = true
                },
                new RegisterAdvertisement
                {
                    RegisterId = "reg-b",
                    SyncState = SyncStateProto.Active,
                    LatestVersion = 10,
                    IsPublic = false
                }
            }
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        var entry = _routingTable.GetPeer("peer-1")!;
        entry.AdvertisedRegisters.Should().HaveCount(2);
        entry.AdvertisedRegisters[0].RegisterId.Should().Be("reg-a");
        entry.AdvertisedRegisters[0].HasFullReplica.Should().BeTrue();
        entry.AdvertisedRegisters[1].RegisterId.Should().Be("reg-b");
        entry.AdvertisedRegisters[1].HasFullReplica.Should().BeFalse();
    }

    [Fact]
    public async Task SendHeartbeat_ResponseIncludesOtherPeersAds()
    {
        RegisterPeer("peer-1", "10.0.0.1");
        RegisterPeer("peer-2", "10.0.0.2");

        // Set up peer-2's advertisements via routing table
        _routingTable.UpdateAdvertisedRegisters("peer-2", [
            new RegisterAdvertisement
            {
                RegisterId = "reg-x",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 100,
                IsPublic = true,
                Name = "Shared Register",
                Description = "A shared register for testing"
            }
        ]);

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.AdvertisedRegisters.Should().ContainSingle();
        response.AdvertisedRegisters[0].RegisterId.Should().Be("reg-x");
        response.AdvertisedRegisters[0].LatestVersion.Should().Be(100);
        response.AdvertisedRegisters[0].IsPublic.Should().BeTrue();
        response.AdvertisedRegisters[0].Name.Should().Be("Shared Register");
        response.AdvertisedRegisters[0].Description.Should().Be("A shared register for testing");
    }

    [Fact]
    public async Task SendHeartbeat_ResponseExcludesSenderAds()
    {
        RegisterPeer("peer-1", "10.0.0.1");

        // Set up peer-1's own advertisements
        _routingTable.UpdateAdvertisedRegisters("peer-1", [
            new RegisterAdvertisement
            {
                RegisterId = "reg-own",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 50
            }
        ]);

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        // Should not contain peer-1's own ads
        response.AdvertisedRegisters.Should().BeEmpty();
    }

    [Fact]
    public async Task SendHeartbeat_ResponseCapsAdsAt100()
    {
        RegisterPeer("peer-1", "10.0.0.1");

        // Create 5 peers each with 30 advertised registers (= 150 total, should be capped to 100)
        for (var i = 2; i <= 6; i++)
        {
            var address = $"10.0.0.{i}";
            RegisterPeer($"peer-{i}", address);

            var ads = Enumerable.Range(0, 30).Select(j => new RegisterAdvertisement
            {
                RegisterId = $"reg-{i}-{j}",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = j
            });

            _routingTable.UpdateAdvertisedRegisters($"peer-{i}", ads);
        }

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.AdvertisedRegisters.Count.Should().BeInRange(1, 100);
    }

    [Fact]
    public async Task SendHeartbeat_NoOtherPeers_EmptyAdsInResponse()
    {
        RegisterPeer("peer-1");

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.AdvertisedRegisters.Should().BeEmpty();
    }

    [Fact]
    public async Task SendHeartbeat_UnregisteredPeer_DoesNotStoreAds()
    {
        // Don't register the peer — heartbeat should be rejected
        var request = new PeerHeartbeatRequest
        {
            PeerId = "unknown-peer",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1,
            AdvertisedRegisters =
            {
                new RegisterAdvertisement
                {
                    RegisterId = "reg-a",
                    SyncState = SyncStateProto.FullyReplicated,
                    LatestVersion = 1
                }
            }
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.Success.Should().BeFalse();
        _routingTable.GetPeer("unknown-peer").Should().BeNull();
    }
}
