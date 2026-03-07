// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.Services;

public class PeerTimeoutServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SweepsUnhealthyPeers()
    {
        // Use a very short timeout so the sweep interval is minimal (5s floor)
        var config = new RouterConfiguration { PeerTimeoutSeconds = 1 };
        var eventBuffer = new EventBuffer(config);
        var routingTable = new RoutingTable(eventBuffer);

        var peerInfo = new PeerInfo { PeerId = "peer-1", Address = "192.168.1.10:5000", Port = 5000 };
        routingTable.RegisterPeer(peerInfo);
        routingTable.GetPeer("peer-1")!.LastSeen = DateTimeOffset.UtcNow.AddSeconds(-10);

        var sut = new PeerTimeoutService(
            routingTable, config, NullLogger<PeerTimeoutService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Start the service and wait long enough for at least one sweep (5s interval)
        _ = sut.StartAsync(cts.Token);
        await Task.Delay(6500);
        await sut.StopAsync(CancellationToken.None);

        routingTable.GetPeer("peer-1")!.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotSweepHealthyPeers()
    {
        var config = new RouterConfiguration { PeerTimeoutSeconds = 60 };
        var eventBuffer = new EventBuffer(config);
        var routingTable = new RoutingTable(eventBuffer);

        var peerInfo = new PeerInfo { PeerId = "peer-1", Address = "192.168.1.10:5000", Port = 5000 };
        routingTable.RegisterPeer(peerInfo);

        var sut = new PeerTimeoutService(
            routingTable, config, NullLogger<PeerTimeoutService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        _ = sut.StartAsync(cts.Token);
        await Task.Delay(6500);
        await sut.StopAsync(CancellationToken.None);

        routingTable.GetPeer("peer-1")!.IsHealthy.Should().BeTrue();
    }
}
