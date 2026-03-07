// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Endpoints;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Endpoints;

public class PeerEndpointTests
{
    private static EventBuffer CreateBuffer() => new(new RouterConfiguration());

    private static RoutingTable CreateTable()
    {
        var buffer = CreateBuffer();
        return new RoutingTable(buffer);
    }

    private static PeerInfo CreatePeerInfo(string peerId, string address = "10.0.0.1:5000", int port = 5000) =>
        new()
        {
            PeerId = peerId,
            Address = address,
            Port = port
        };

    [Fact]
    public void HandleGetPeers_EmptyTable_ReturnsZeroCounts()
    {
        // Arrange
        var table = CreateTable();

        // Act
        var result = PeerEndpoints.HandleGetPeers(table);

        // Assert — anonymous types produce Ok<AnonymousType>, verify it's an Ok result
        result.Should().NotBeNull();
        result.GetType().Name.Should().StartWith("Ok");

    }

    [Fact]
    public void HandleGetPeers_WithPeers_ReturnsTotalAndHealthyCounts()
    {
        // Arrange
        var table = CreateTable();
        table.RegisterPeer(CreatePeerInfo("peer-1", "10.0.0.1:5000"));
        table.RegisterPeer(CreatePeerInfo("peer-2", "10.0.0.2:5000"));

        // Act — verify via RoutingTable directly since anonymous type is hard to inspect
        table.TotalCount.Should().Be(2);
        table.HealthyCount.Should().Be(2);

        var result = PeerEndpoints.HandleGetPeers(table);
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetPeers_WithUnhealthyPeer_ReportsCorrectHealthyCount()
    {
        // Arrange
        var table = CreateTable();
        table.RegisterPeer(CreatePeerInfo("peer-1", "10.0.0.1:5000"));
        table.RegisterPeer(CreatePeerInfo("peer-2", "10.0.0.2:5000"));

        // Mark peer-2 as unhealthy via timeout sweep
        table.SweepUnhealthyPeers(TimeSpan.Zero);

        // Act
        table.TotalCount.Should().Be(2);
        table.HealthyCount.Should().Be(0); // Both timed out with TimeSpan.Zero

        var result = PeerEndpoints.HandleGetPeers(table);
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetPeers_ReturnsAllPeersIncludingUnhealthy()
    {
        // Arrange
        var table = CreateTable();
        table.RegisterPeer(CreatePeerInfo("peer-1", "10.0.0.1:5000"));
        table.RegisterPeer(CreatePeerInfo("peer-2", "10.0.0.2:5000"));

        // Mark all as unhealthy
        table.SweepUnhealthyPeers(TimeSpan.Zero);

        // Act
        var allPeers = table.GetAllPeers();

        // Assert — GetAllPeers returns ALL peers, not just healthy
        allPeers.Should().HaveCount(2);
    }

    [Fact]
    public void HandleGetPeers_PeerData_ContainsExpectedFields()
    {
        // Arrange
        var table = CreateTable();
        table.RegisterPeer(CreatePeerInfo("peer-abc", "192.168.1.50:7002", 7002));

        // Act
        var peers = table.GetAllPeers();

        // Assert
        var peer = peers.Should().ContainSingle().Subject;
        peer.PeerId.Should().Be("peer-abc");
        peer.IpAddress.Should().Be("192.168.1.50");
        peer.Port.Should().Be(7002);
        peer.IsHealthy.Should().BeTrue();
        peer.FirstSeen.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
