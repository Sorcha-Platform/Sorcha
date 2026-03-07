// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.GrpcServices;

/// <summary>
/// gRPC implementation of PeerHeartbeat for the standalone Peer Router.
/// Processes heartbeats to maintain peer health and register version tracking.
/// </summary>
public sealed class RouterHeartbeatService : PeerHeartbeat.PeerHeartbeatBase
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly ILogger<RouterHeartbeatService> _logger;

    public RouterHeartbeatService(
        RoutingTable routingTable,
        EventBuffer eventBuffer,
        ILogger<RouterHeartbeatService> logger)
    {
        _routingTable = routingTable;
        _eventBuffer = eventBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Processes a single heartbeat: touches the peer, updates register versions, emits event.
    /// </summary>
    public override Task<PeerHeartbeatResponse> SendHeartbeat(
        PeerHeartbeatRequest request,
        ServerCallContext context)
    {
        var response = ProcessHeartbeat(request);
        return Task.FromResult(response);
    }

    /// <summary>
    /// Bidirectional heartbeat stream: processes each incoming heartbeat and responds.
    /// </summary>
    public override async Task StreamHeartbeat(
        IAsyncStreamReader<PeerHeartbeatRequest> requestStream,
        IServerStreamWriter<PeerHeartbeatResponse> responseStream,
        ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            var response = ProcessHeartbeat(request);
            await responseStream.WriteAsync(response, context.CancellationToken);
        }
    }

    /// <summary>
    /// Core heartbeat processing: touch peer, update register versions, emit event, build response.
    /// </summary>
    private PeerHeartbeatResponse ProcessHeartbeat(PeerHeartbeatRequest request)
    {
        var peerId = request.PeerId;
        var found = _routingTable.TouchPeer(peerId);

        if (found && request.RegisterVersions.Count > 0)
        {
            _routingTable.UpdateRegisterVersions(peerId, request.RegisterVersions);
        }

        var detail = new Dictionary<string, object?>
        {
            ["sequence"] = request.SequenceNumber
        };

        if (request.RegisterVersions.Count > 0)
        {
            detail["register_versions"] = request.RegisterVersions.ToDictionary(
                kvp => kvp.Key, kvp => (object?)kvp.Value);
        }

        if (request.Metrics is not null)
        {
            detail["metrics"] = new Dictionary<string, object?>
            {
                ["active_connections"] = request.Metrics.ActiveConnections,
                ["cpu_usage"] = request.Metrics.CpuUsagePercent,
                ["memory_mb"] = request.Metrics.MemoryUsageMb
            };
        }

        _eventBuffer.Add(RouterEvent.Create(
            RouterEventType.PeerHeartbeat,
            peerId,
            "heartbeat",
            0,
            detail: detail));

        _logger.LogDebug(
            "Heartbeat from {PeerId} seq={Sequence}, found={Found}",
            peerId,
            request.SequenceNumber,
            found);

        return new PeerHeartbeatResponse
        {
            Success = found,
            PeerId = "router",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Message = found ? "OK" : "Peer not registered"
        };
    }
}
