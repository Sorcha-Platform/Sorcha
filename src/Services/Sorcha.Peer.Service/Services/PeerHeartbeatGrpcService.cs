// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Services;

/// <summary>
/// gRPC service implementation for receiving P2P heartbeats.
/// Handles incoming heartbeats, updates peer health, and responds with local status.
/// </summary>
public class PeerHeartbeatGrpcService : PeerHeartbeat.PeerHeartbeatBase
{
    private readonly ILogger<PeerHeartbeatGrpcService> _logger;
    private readonly PeerListManager _peerListManager;
    private readonly RegisterAdvertisementService _advertisementService;
    private readonly PeerServiceConfiguration _configuration;

    public PeerHeartbeatGrpcService(
        ILogger<PeerHeartbeatGrpcService> logger,
        PeerListManager peerListManager,
        RegisterAdvertisementService advertisementService,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _advertisementService = advertisementService ?? throw new ArgumentNullException(nameof(advertisementService));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Handles a unary heartbeat from a peer.
    /// </summary>
    public override async Task<PeerHeartbeatResponse> SendHeartbeat(
        PeerHeartbeatRequest request,
        ServerCallContext context)
    {
        _logger.LogTrace(
            "Received heartbeat {Seq} from peer {PeerId}",
            request.SequenceNumber, request.PeerId);

        // Update peer's last seen timestamp
        if (!string.IsNullOrEmpty(request.PeerId))
        {
            await _peerListManager.UpdateLastSeenAsync(request.PeerId);
        }

        // Process remote register advertisements
        if (request.AdvertisedRegisters.Count > 0)
        {
            var remoteAds = request.AdvertisedRegisters
                .Select(ad => new PeerRegisterInfo
                {
                    RegisterId = ad.RegisterId,
                    Name = string.IsNullOrEmpty(ad.Name) ? null : ad.Name,
                    Description = string.IsNullOrEmpty(ad.Description) ? null : ad.Description,
                    SyncState = ConvertFromProtoSyncState(ad.SyncState),
                    LatestVersion = ad.LatestVersion,
                    IsPublic = ad.IsPublic
                })
                .ToList();

            await _advertisementService.ProcessRemoteAdvertisementsAsync(
                request.PeerId, remoteAds, context.CancellationToken);
        }

        // Build response with our own status
        var response = new PeerHeartbeatResponse
        {
            Success = true,
            PeerId = _configuration.NodeId ?? Environment.MachineName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        // Add our register versions and advertisements
        foreach (var ad in _advertisementService.GetLocalAdvertisements())
        {
            response.RegisterVersions[ad.RegisterId] = ad.LatestVersion;
            response.AdvertisedRegisters.Add(new RegisterAdvertisement
            {
                RegisterId = ad.RegisterId,
                SyncState = ConvertToProtoSyncState(ad.SyncState),
                LatestVersion = ad.LatestVersion,
                LatestDocketVersion = ad.LatestDocketVersion,
                IsPublic = ad.IsPublic,
                Name = ad.Name ?? "",
                Description = ad.Description ?? ""
            });
        }

        return response;
    }

    /// <summary>
    /// Handles bidirectional heartbeat stream.
    /// </summary>
    public override async Task StreamHeartbeat(
        IAsyncStreamReader<PeerHeartbeatRequest> requestStream,
        IServerStreamWriter<PeerHeartbeatResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug("Bidirectional heartbeat stream started");

        await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
        {
            _logger.LogTrace(
                "Stream heartbeat {Seq} from peer {PeerId}",
                request.SequenceNumber, request.PeerId);

            if (!string.IsNullOrEmpty(request.PeerId))
            {
                await _peerListManager.UpdateLastSeenAsync(request.PeerId);
            }

            var response = new PeerHeartbeatResponse
            {
                Success = true,
                PeerId = _configuration.NodeId ?? Environment.MachineName,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            foreach (var ad in _advertisementService.GetLocalAdvertisements())
            {
                response.RegisterVersions[ad.RegisterId] = ad.LatestVersion;
            }

            await responseStream.WriteAsync(response, context.CancellationToken);
        }

        _logger.LogDebug("Bidirectional heartbeat stream ended");
    }

    private static RegisterSyncState ConvertFromProtoSyncState(SyncStateProto state) => state switch
    {
        SyncStateProto.Subscribing => RegisterSyncState.Subscribing,
        SyncStateProto.Syncing => RegisterSyncState.Syncing,
        SyncStateProto.FullyReplicated => RegisterSyncState.FullyReplicated,
        SyncStateProto.Active => RegisterSyncState.Active,
        SyncStateProto.Error => RegisterSyncState.Error,
        _ => RegisterSyncState.Subscribing
    };

    private static SyncStateProto ConvertToProtoSyncState(RegisterSyncState state) => state switch
    {
        RegisterSyncState.Subscribing => SyncStateProto.Subscribing,
        RegisterSyncState.Syncing => SyncStateProto.Syncing,
        RegisterSyncState.FullyReplicated => SyncStateProto.FullyReplicated,
        RegisterSyncState.Active => SyncStateProto.Active,
        RegisterSyncState.Error => SyncStateProto.Error,
        _ => SyncStateProto.Unknown
    };
}
