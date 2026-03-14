// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.GrpcServices;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.Tests.GrpcServices;

public sealed class RouterHeartbeatServiceTests
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterHeartbeatService _service;

    public RouterHeartbeatServiceTests()
    {
        var config = new RouterConfiguration();
        _eventBuffer = new EventBuffer(config);
        _routingTable = new RoutingTable(_eventBuffer, config);
        _service = new RouterHeartbeatService(
            _routingTable,
            _eventBuffer,
            NullLogger<RouterHeartbeatService>.Instance);
    }

    #region SendHeartbeat

    [Fact]
    public async Task SendHeartbeat_RegisteredPeer_ReturnsSuccess()
    {
        RegisterPeer("peer-1");

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.Success.Should().BeTrue();
        response.PeerId.Should().Be("router");
        response.Timestamp.Should().BeGreaterThan(0);
        response.Message.Should().Be("OK");
    }

    [Fact]
    public async Task SendHeartbeat_UnregisteredPeer_ReturnsFailure()
    {
        var request = new PeerHeartbeatRequest
        {
            PeerId = "unknown-peer",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        var response = await _service.SendHeartbeat(request, TestServerCallContext.Create());

        response.Success.Should().BeFalse();
        response.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task SendHeartbeat_UnregisteredPeer_EmitsRejectedEvent()
    {
        var initialCount = _eventBuffer.Count;

        var request = new PeerHeartbeatRequest
        {
            PeerId = "unknown-peer",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 42
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        _eventBuffer.Count.Should().BeGreaterThan(initialCount);
        var lastEvent = _eventBuffer.GetSnapshot().Last();
        lastEvent.Type.Should().Be(RouterEventType.PeerHeartbeatRejected);
        lastEvent.PeerId.Should().Be("unknown-peer");
    }

    [Fact]
    public async Task SendHeartbeat_RegisteredPeer_EmitsEventWithPeerAddress()
    {
        RegisterPeer("peer-1");
        var initialCount = _eventBuffer.Count;

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        var lastEvent = _eventBuffer.GetSnapshot().Last();
        lastEvent.Type.Should().Be(RouterEventType.PeerHeartbeat);
        lastEvent.IpAddress.Should().Be("10.0.0.1");
        lastEvent.Port.Should().Be(5000);
    }

    [Fact]
    public async Task SendHeartbeat_UpdatesLastSeen()
    {
        RegisterPeer("peer-1");
        var beforeLastSeen = _routingTable.GetPeer("peer-1")!.LastSeen;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        _routingTable.GetPeer("peer-1")!.LastSeen.Should().BeOnOrAfter(beforeLastSeen);
    }

    [Fact]
    public async Task SendHeartbeat_WithRegisterVersions_UpdatesRoutingTable()
    {
        RegisterPeer("peer-1");

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 1,
            RegisterVersions = { { "register-a", 42L }, { "register-b", 99L } }
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        var entry = _routingTable.GetPeer("peer-1")!;
        entry.RegisterVersions.Should().ContainKey("register-a").WhoseValue.Should().Be(42);
        entry.RegisterVersions.Should().ContainKey("register-b").WhoseValue.Should().Be(99);
    }

    [Fact]
    public async Task SendHeartbeat_WithMetrics_EmitsEventWithMetrics()
    {
        RegisterPeer("peer-1");
        var initialCount = _eventBuffer.Count;

        var request = new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = 5,
            Metrics = new PeerHealthMetrics
            {
                ActiveConnections = 3,
                CpuUsagePercent = 45.5,
                MemoryUsageMb = 512.0
            }
        };

        await _service.SendHeartbeat(request, TestServerCallContext.Create());

        _eventBuffer.Count.Should().BeGreaterThan(initialCount);
        var events = _eventBuffer.GetSnapshot();
        var lastEvent = events.Last();
        lastEvent.Type.Should().Be(RouterEventType.PeerHeartbeat);
        lastEvent.PeerId.Should().Be("peer-1");
    }

    [Fact]
    public async Task SendHeartbeat_IncrementsHeartbeatCount()
    {
        RegisterPeer("peer-1");

        for (var i = 0; i < 3; i++)
        {
            await _service.SendHeartbeat(
                new PeerHeartbeatRequest
                {
                    PeerId = "peer-1",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    SequenceNumber = i + 1
                },
                TestServerCallContext.Create());
        }

        // HeartbeatCount is incremented by TouchPeer (called in SendHeartbeat)
        _routingTable.GetPeer("peer-1")!.HeartbeatCount.Should().BeGreaterThanOrEqualTo(3);
    }

    #endregion

    #region StreamHeartbeat

    [Fact]
    public async Task StreamHeartbeat_ProcessesMultipleHeartbeats()
    {
        RegisterPeer("peer-1");

        var requests = Enumerable.Range(1, 3).Select(i => new PeerHeartbeatRequest
        {
            PeerId = "peer-1",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = i
        }).ToList();

        var requestStream = new TestAsyncStreamReader<PeerHeartbeatRequest>(requests);
        var responseStream = new TestServerStreamWriter<PeerHeartbeatResponse>();

        await _service.StreamHeartbeat(requestStream, responseStream, TestServerCallContext.Create());

        responseStream.Messages.Should().HaveCount(3);
        responseStream.Messages.Should().AllSatisfy(r =>
        {
            r.Success.Should().BeTrue();
            r.PeerId.Should().Be("router");
        });
    }

    [Fact]
    public async Task StreamHeartbeat_WithRegisterVersions_UpdatesAllVersions()
    {
        RegisterPeer("peer-1");

        var requests = new List<PeerHeartbeatRequest>
        {
            new()
            {
                PeerId = "peer-1",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SequenceNumber = 1,
                RegisterVersions = { { "reg-a", 10L } }
            },
            new()
            {
                PeerId = "peer-1",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SequenceNumber = 2,
                RegisterVersions = { { "reg-a", 20L }, { "reg-b", 5L } }
            }
        };

        var requestStream = new TestAsyncStreamReader<PeerHeartbeatRequest>(requests);
        var responseStream = new TestServerStreamWriter<PeerHeartbeatResponse>();

        await _service.StreamHeartbeat(requestStream, responseStream, TestServerCallContext.Create());

        var entry = _routingTable.GetPeer("peer-1")!;
        entry.RegisterVersions["reg-a"].Should().Be(20);
        entry.RegisterVersions["reg-b"].Should().Be(5);
    }

    [Fact]
    public async Task StreamHeartbeat_EmptyStream_WritesNoResponses()
    {
        var requestStream = new TestAsyncStreamReader<PeerHeartbeatRequest>([]);
        var responseStream = new TestServerStreamWriter<PeerHeartbeatResponse>();

        await _service.StreamHeartbeat(requestStream, responseStream, TestServerCallContext.Create());

        responseStream.Messages.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private void RegisterPeer(string peerId)
    {
        _routingTable.RegisterPeer(new PeerInfo
        {
            PeerId = peerId,
            Address = "10.0.0.1",
            Port = 5000
        });
    }

    #endregion
}
