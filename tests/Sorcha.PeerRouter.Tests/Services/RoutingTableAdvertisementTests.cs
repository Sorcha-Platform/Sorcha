// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Services;

public sealed class RoutingTableAdvertisementTests
{
    private readonly RouterConfiguration _config = new();
    private readonly EventBuffer _eventBuffer;
    private readonly RoutingTable _sut;

    public RoutingTableAdvertisementTests()
    {
        _eventBuffer = new EventBuffer(_config);
        _sut = new RoutingTable(_eventBuffer, _config);
    }

    private static PeerInfo CreatePeerInfo(string peerId = "peer-1", string address = "192.168.1.10", int port = 5000)
    {
        return new PeerInfo
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
    }

    [Fact]
    public void UpdateAdvertisedRegisters_ExistingPeer_UpdatesRegisters()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));

        var ads = new[]
        {
            new RegisterAdvertisement
            {
                RegisterId = "reg-1",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 42,
                IsPublic = true
            },
            new RegisterAdvertisement
            {
                RegisterId = "reg-2",
                SyncState = SyncStateProto.Active,
                LatestVersion = 10,
                IsPublic = false
            }
        };

        var result = _sut.UpdateAdvertisedRegisters("peer-1", ads);

        result.Should().BeTrue();
        var entry = _sut.GetPeer("peer-1")!;
        entry.AdvertisedRegisters.Should().HaveCount(2);
        entry.AdvertisedRegisters[0].RegisterId.Should().Be("reg-1");
        entry.AdvertisedRegisters[0].HasFullReplica.Should().BeTrue();
        entry.AdvertisedRegisters[0].LatestVersion.Should().Be(42);
        entry.AdvertisedRegisters[0].IsPublic.Should().BeTrue();
        entry.AdvertisedRegisters[1].RegisterId.Should().Be("reg-2");
        entry.AdvertisedRegisters[1].HasFullReplica.Should().BeFalse();
    }

    [Fact]
    public void UpdateAdvertisedRegisters_UnknownPeer_ReturnsFalse()
    {
        var ads = new[]
        {
            new RegisterAdvertisement
            {
                RegisterId = "reg-1",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 1
            }
        };

        var result = _sut.UpdateAdvertisedRegisters("nonexistent", ads);

        result.Should().BeFalse();
    }

    [Fact]
    public void UpdateAdvertisedRegisters_EmptyList_ClearsExistingRegisters()
    {
        var peerInfo = CreatePeerInfo("peer-1");
        peerInfo.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
        {
            RegisterId = "reg-1",
            HasFullReplica = true,
            LatestVersion = 10
        });
        _sut.RegisterPeer(peerInfo);

        _sut.GetPeer("peer-1")!.AdvertisedRegisters.Should().HaveCount(1);

        var result = _sut.UpdateAdvertisedRegisters("peer-1", []);

        result.Should().BeTrue();
        _sut.GetPeer("peer-1")!.AdvertisedRegisters.Should().BeEmpty();
    }

    [Fact]
    public void UpdateAdvertisedRegisters_ReplacesExistingList()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));

        // First update
        _sut.UpdateAdvertisedRegisters("peer-1", [
            new RegisterAdvertisement
            {
                RegisterId = "reg-old",
                SyncState = SyncStateProto.Active,
                LatestVersion = 5
            }
        ]);

        _sut.GetPeer("peer-1")!.AdvertisedRegisters.Should().ContainSingle(r => r.RegisterId == "reg-old");

        // Second update replaces
        _sut.UpdateAdvertisedRegisters("peer-1", [
            new RegisterAdvertisement
            {
                RegisterId = "reg-new",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 99
            }
        ]);

        var entry = _sut.GetPeer("peer-1")!;
        entry.AdvertisedRegisters.Should().ContainSingle(r => r.RegisterId == "reg-new");
        entry.AdvertisedRegisters.Should().NotContain(r => r.RegisterId == "reg-old");
    }

    [Fact]
    public void UpdateAdvertisedRegisters_SyncingState_SetsHasFullReplicaFalse()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));

        _sut.UpdateAdvertisedRegisters("peer-1", [
            new RegisterAdvertisement
            {
                RegisterId = "reg-1",
                SyncState = SyncStateProto.Syncing,
                LatestVersion = 3
            }
        ]);

        var entry = _sut.GetPeer("peer-1")!;
        entry.AdvertisedRegisters[0].HasFullReplica.Should().BeFalse();
    }

    [Fact]
    public void UpdateAdvertisedRegisters_NameAndDescription_SurviveRoundTrip()
    {
        _sut.RegisterPeer(CreatePeerInfo("peer-1"));

        var ads = new[]
        {
            new RegisterAdvertisement
            {
                RegisterId = "reg-named",
                SyncState = SyncStateProto.FullyReplicated,
                LatestVersion = 7,
                IsPublic = true,
                Name = "Test Register",
                Description = "Test desc"
            }
        };

        var result = _sut.UpdateAdvertisedRegisters("peer-1", ads);

        result.Should().BeTrue();
        var entry = _sut.GetPeer("peer-1")!;
        entry.AdvertisedRegisters.Should().ContainSingle();
        entry.AdvertisedRegisters[0].Name.Should().Be("Test Register");
        entry.AdvertisedRegisters[0].Description.Should().Be("Test desc");
    }
}
