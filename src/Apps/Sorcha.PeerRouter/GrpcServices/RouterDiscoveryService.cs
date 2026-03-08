// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.GrpcServices;

/// <summary>
/// gRPC implementation of PeerDiscovery for the standalone Peer Router.
/// Manages peer registration, listing, ping, gossip exchange, and register-scoped lookup.
/// </summary>
public sealed class RouterDiscoveryService : PeerDiscovery.PeerDiscoveryBase
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _config;
    private readonly ILogger<RouterDiscoveryService> _logger;

    public RouterDiscoveryService(
        RoutingTable routingTable,
        EventBuffer eventBuffer,
        RouterConfiguration config,
        ILogger<RouterDiscoveryService> logger)
    {
        _routingTable = routingTable;
        _eventBuffer = eventBuffer;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Registers a peer in the routing table.
    /// </summary>
    public override Task<RegisterPeerResponse> RegisterPeer(
        RegisterPeerRequest request,
        ServerCallContext context)
    {
        var peerInfo = request.PeerInfo;
        if (peerInfo is null)
        {
            _logger.LogWarning("RegisterPeer called with null PeerInfo");
            return Task.FromResult(new RegisterPeerResponse
            {
                Success = false,
                Message = "PeerInfo is required"
            });
        }

        if (IsSelf(peerInfo.PeerId))
        {
            _logger.LogDebug("Rejected self-registration for PeerId {PeerId}", peerInfo.PeerId);
            return Task.FromResult(new RegisterPeerResponse
            {
                Success = true,
                Message = "Self-registration ignored"
            });
        }

        var isNew = _routingTable.RegisterPeer(peerInfo);
        _logger.LogInformation(
            "Peer {PeerId} {Action} at {Address}:{Port} from {ClientAddress}",
            peerInfo.PeerId,
            isNew ? "registered" : "updated",
            peerInfo.Address,
            peerInfo.Port,
            context.Peer);

        return Task.FromResult(new RegisterPeerResponse
        {
            Success = true,
            Message = isNew ? "Peer registered" : "Peer updated"
        });
    }

    /// <summary>
    /// Returns a list of healthy peers, excluding the requesting peer.
    /// </summary>
    public override Task<PeerListResponse> GetPeerList(
        PeerListRequest request,
        ServerCallContext context)
    {
        var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : 100;
        var peers = _routingTable.GetHealthyPeers(
            excludePeerId: request.RequestingPeerId,
            maxPeers: maxPeers);

        _eventBuffer.Add(RouterEvent.Create(
            RouterEventType.PeerListRequested,
            request.RequestingPeerId ?? "unknown",
            context.Peer ?? "unknown",
            0));

        var response = new PeerListResponse { TotalPeers = peers.Count };
        response.Peers.AddRange(peers.Select(MapToPeerInfo));

        _logger.LogDebug(
            "GetPeerList for {PeerId}: returning {Count} peers",
            request.RequestingPeerId,
            peers.Count);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Pings a peer to update its LastSeen timestamp.
    /// </summary>
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        var found = _routingTable.TouchPeer(request.PeerId);

        if (found)
        {
            _eventBuffer.Add(RouterEvent.Create(
                RouterEventType.PeerHeartbeat,
                request.PeerId,
                context.Peer ?? "unknown",
                0));
        }

        _logger.LogDebug("Ping from {PeerId}: {Status}", request.PeerId, found ? "ONLINE" : "UNKNOWN");

        return Task.FromResult(new PingResponse
        {
            PeerId = request.PeerId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status = found ? PeerStatus.Online : PeerStatus.Unknown
        });
    }

    /// <summary>
    /// Exchanges peer lists for gossip-style mesh discovery.
    /// Registers all incoming peers, then returns the router's known healthy peers.
    /// </summary>
    public override Task<PeerExchangeResponse> ExchangePeers(
        PeerExchangeRequest request,
        ServerCallContext context)
    {
        var registeredCount = 0;
        foreach (var peerInfo in request.KnownPeers)
        {
            if (IsSelf(peerInfo.PeerId))
                continue;

            _routingTable.RegisterPeer(peerInfo);
            registeredCount++;
        }

        var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : 100;
        var healthyPeers = _routingTable.GetHealthyPeers(
            excludePeerId: request.PeerId,
            maxPeers: maxPeers);

        _eventBuffer.Add(RouterEvent.Create(
            RouterEventType.PeerExchanged,
            request.PeerId ?? "unknown",
            context.Peer ?? "unknown",
            0,
            detail: new Dictionary<string, object?>
            {
                ["received_count"] = registeredCount,
                ["returned_count"] = healthyPeers.Count
            }));

        var response = new PeerExchangeResponse
        {
            Success = true,
            Message = $"Exchanged {registeredCount} peers, returning {healthyPeers.Count}"
        };
        response.KnownPeers.AddRange(healthyPeers.Select(MapToPeerInfo));

        _logger.LogInformation(
            "ExchangePeers with {PeerId} from {ClientAddress}: received {Received}, returning {Returned}",
            request.PeerId,
            context.Peer,
            registeredCount,
            healthyPeers.Count);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Finds peers that advertise a specific register.
    /// </summary>
    public override Task<FindPeersForRegisterResponse> FindPeersForRegister(
        FindPeersForRegisterRequest request,
        ServerCallContext context)
    {
        var maxPeers = request.MaxPeers > 0 ? request.MaxPeers : 100;
        var peers = _routingTable.FindPeersForRegister(
            request.RegisterId,
            request.RequireFullReplica,
            excludePeerId: request.RequestingPeerId,
            maxPeers: maxPeers);

        var response = new FindPeersForRegisterResponse { TotalPeers = peers.Count };
        response.Peers.AddRange(peers.Select(MapToPeerInfo));

        _logger.LogDebug(
            "FindPeersForRegister '{RegisterId}' for {PeerId}: returning {Count} peers",
            request.RegisterId,
            request.RequestingPeerId,
            peers.Count);

        return Task.FromResult(response);
    }

    /// <summary>
    /// Returns true if the given peerId matches this router's own identity.
    /// </summary>
    private bool IsSelf(string peerId) =>
        !string.IsNullOrEmpty(_config.PeerId) &&
        string.Equals(peerId, _config.PeerId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Maps a RoutingEntry to a PeerInfo protobuf message.
    /// </summary>
    private static PeerInfo MapToPeerInfo(RoutingEntry entry)
    {
        var peerInfo = new PeerInfo
        {
            PeerId = entry.PeerId,
            Address = entry.Address,
            Port = entry.Port,
            LastSeen = entry.LastSeen.ToUnixTimeMilliseconds(),
            Capabilities = entry.Capabilities
        };

        peerInfo.AdvertisedRegisters.AddRange(entry.AdvertisedRegisters);

        return peerInfo;
    }
}
