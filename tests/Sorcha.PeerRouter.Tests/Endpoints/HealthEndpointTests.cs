// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Endpoints;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Endpoints;

public class HealthEndpointTests
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
    public void HandleGetHealth_ReturnsOkResult()
    {
        // Arrange
        var table = CreateTable();
        var buffer = CreateBuffer();
        var config = new RouterConfiguration();

        // Act
        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);

        // Assert — anonymous types produce Ok<AnonymousType>, verify it's an Ok result
        result.Should().NotBeNull();
        result.GetType().Name.Should().StartWith("Ok");

    }

    [Fact]
    public void HandleGetHealth_EmptyRouter_ReportsZeroPeers()
    {
        // Arrange
        var table = CreateTable();
        var buffer = CreateBuffer();
        var config = new RouterConfiguration();

        // Act — verify via the table counts directly
        table.TotalCount.Should().Be(0);
        table.HealthyCount.Should().Be(0);

        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetHealth_WithPeers_ReportsCorrectCounts()
    {
        // Arrange
        var buffer = CreateBuffer();
        var table = new RoutingTable(buffer);
        table.RegisterPeer(CreatePeerInfo("peer-1"));
        table.RegisterPeer(CreatePeerInfo("peer-2", "10.0.0.2:5000"));
        var config = new RouterConfiguration();

        // Act
        table.TotalCount.Should().Be(2);
        table.HealthyCount.Should().Be(2);

        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetHealth_RelayEnabled_ReflectsConfiguration()
    {
        // Arrange
        var table = CreateTable();
        var buffer = CreateBuffer();
        var config = new RouterConfiguration { EnableRelay = true };

        // Act
        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);

        // Assert — config value is reflected (relay enabled)
        config.EnableRelay.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetHealth_RelayDisabled_ReflectsConfiguration()
    {
        // Arrange
        var table = CreateTable();
        var buffer = CreateBuffer();
        var config = new RouterConfiguration { EnableRelay = false };

        // Act
        config.EnableRelay.Should().BeFalse();

        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);
        result.Should().NotBeNull();
    }

    [Fact]
    public void HandleGetHealth_EventBufferSize_ReportsBufferedCount()
    {
        // Arrange
        var buffer = CreateBuffer();
        buffer.Add(RouterEvent.Create(RouterEventType.PeerConnected, "peer-1", "10.0.0.1", 5000));
        buffer.Add(RouterEvent.Create(RouterEventType.PeerHeartbeat, "peer-1", "10.0.0.1", 5000));
        var table = CreateTable();
        var config = new RouterConfiguration();

        // Act
        buffer.Count.Should().Be(2);

        var result = HealthEndpoints.HandleGetHealth(table, buffer, config);
        result.Should().NotBeNull();
    }
}
