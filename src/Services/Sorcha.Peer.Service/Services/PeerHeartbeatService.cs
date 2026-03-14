// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;
using Sorcha.Peer.Service.Replication;

namespace Sorcha.Peer.Service.Services;

/// <summary>
/// P2P heartbeat background service that sends periodic heartbeats to all connected peers.
/// Unlike the hub-specific HeartbeatMonitorService, this treats all peers symmetrically.
/// Each heartbeat exchanges per-register version information for targeted sync detection.
/// </summary>
public class PeerHeartbeatBackgroundService : BackgroundService
{
    private readonly ILogger<PeerHeartbeatBackgroundService> _logger;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly RegisterAdvertisementService _advertisementService;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;
    private readonly RegisterSyncConfiguration _syncConfig;
    private long _sequenceNumber;
    private int _heartbeatCycleCount;

    public PeerHeartbeatBackgroundService(
        ILogger<PeerHeartbeatBackgroundService> logger,
        PeerConnectionPool connectionPool,
        PeerListManager peerListManager,
        RegisterAdvertisementService advertisementService,
        PeerServiceMetrics metrics,
        PeerServiceActivitySource activitySource,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _advertisementService = advertisementService ?? throw new ArgumentNullException(nameof(advertisementService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));
        _syncConfig = configuration?.Value?.RegisterSync ?? new RegisterSyncConfiguration();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "P2P heartbeat service started (interval: {Interval}s)",
            _syncConfig.HeartbeatIntervalSeconds);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_syncConfig.HeartbeatIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await SendHeartbeatsToAllPeersAsync(stoppingToken);

                _heartbeatCycleCount++;

                // Reconnect disconnected seed nodes every heartbeat cycle
                await _connectionPool.ReconnectDisconnectedSeedNodesAsync(stoppingToken);

                // Cleanup idle connections every 10th cycle (~5 minutes at 30s interval)
                if (_heartbeatCycleCount % 10 == 0)
                {
                    await _connectionPool.CleanupIdleConnectionsAsync(TimeSpan.FromMinutes(15));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in P2P heartbeat loop");
            }
        }

        _logger.LogInformation("P2P heartbeat service stopped");
    }

    /// <summary>
    /// Sends heartbeats to all connected peers concurrently.
    /// </summary>
    private async Task SendHeartbeatsToAllPeersAsync(CancellationToken cancellationToken)
    {
        var channels = _connectionPool.GetAllActiveChannels();
        if (channels.Count == 0)
        {
            _logger.LogDebug("No active peer connections for heartbeat");
            return;
        }

        var tasks = channels.Select(c =>
            SendHeartbeatToPeerAsync(c.PeerId, c.Channel, cancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a heartbeat to a single peer.
    /// </summary>
    private async Task SendHeartbeatToPeerAsync(
        string peerId,
        GrpcChannel channel,
        CancellationToken cancellationToken)
    {
        var sequenceNumber = Interlocked.Increment(ref _sequenceNumber);
        var startTime = DateTime.UtcNow;

        using var activity = _activitySource.StartHeartbeatActivity(peerId, sequenceNumber);

        try
        {
            using var timeoutCts = new CancellationTokenSource(
                TimeSpan.FromSeconds(_syncConfig.HeartbeatTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token, cancellationToken);

            var request = BuildHeartbeatRequest(sequenceNumber);
            var client = new PeerHeartbeat.PeerHeartbeatClient(channel);
            var response = await client.SendHeartbeatAsync(
                request, cancellationToken: linkedCts.Token);

            var latency = (DateTime.UtcNow - startTime).TotalMilliseconds;

            var latencySpan = TimeSpan.FromMilliseconds(latency);
            _metrics.RecordHeartbeatLatency(latency, peerId);

            // Handle heartbeat rejection — remote peer has dropped our registration
            // (e.g. we timed out of their routing table). Mark for reconnection so the
            // existing reconnect loop re-registers us on the next cycle.
            if (!response.Success)
            {
                _logger.LogWarning(
                    "Heartbeat {Seq} rejected by peer {PeerId}: {Reason} — marking for reconnection",
                    sequenceNumber, peerId, response.Message);
                _metrics.RecordHeartbeatRejection(peerId, response.Message);
                _connectionPool.MarkPeerForReconnection(peerId);
                return;
            }

            // Record success
            _connectionPool.RecordSuccess(peerId);
            _activitySource.RecordSuccess(activity, latencySpan);

            // Update peer's last seen time
            await _peerListManager.UpdateLastSeenAsync(peerId);

            // Process remote register advertisements
            if (response.AdvertisedRegisters.Count > 0)
            {
                var remoteAds = response.AdvertisedRegisters
                    .Select(ConvertToRegisterInfo)
                    .ToList();

                await _advertisementService.ProcessRemoteAdvertisementsAsync(
                    peerId, remoteAds, cancellationToken);
            }

            // Detect version lag
            if (response.RegisterVersions.Count > 0)
            {
                var lagging = _advertisementService.DetectVersionLag(response.RegisterVersions);
                if (lagging.Count > 0)
                {
                    _logger.LogDebug(
                        "Detected version lag for {Count} registers from peer {PeerId}",
                        lagging.Count, peerId);
                }
            }

            _logger.LogTrace(
                "Heartbeat {Seq} to peer {PeerId} succeeded (latency: {Latency}ms)",
                sequenceNumber, peerId, latency);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            _logger.LogWarning(
                "Heartbeat to peer {PeerId} failed — peer unavailable", peerId);
            await _connectionPool.RecordFailureAsync(peerId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Heartbeat {Seq} to peer {PeerId} timed out", sequenceNumber, peerId);
            await _connectionPool.RecordFailureAsync(peerId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Heartbeat {Seq} to peer {PeerId} failed", sequenceNumber, peerId);
            await _connectionPool.RecordFailureAsync(peerId);
        }
    }

    /// <summary>
    /// Builds a heartbeat request including local register versions and advertisements.
    /// </summary>
    private PeerHeartbeatRequest BuildHeartbeatRequest(long sequenceNumber)
    {
        var request = new PeerHeartbeatRequest
        {
            PeerId = _peerListManager.GetLocalPeerStatus()?.PeerId ?? Environment.MachineName,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SequenceNumber = sequenceNumber
        };

        // Add per-register versions
        foreach (var ad in _advertisementService.GetLocalAdvertisements())
        {
            request.RegisterVersions[ad.RegisterId] = ad.LatestVersion;
            request.AdvertisedRegisters.Add(new Protos.RegisterAdvertisement
            {
                RegisterId = ad.RegisterId,
                SyncState = ConvertToProtoSyncState(ad.SyncState),
                LatestVersion = ad.LatestVersion,
                LatestDocketVersion = ad.LatestDocketVersion,
                IsPublic = ad.IsPublic
            });
        }

        return request;
    }

    /// <summary>
    /// Gets the current heartbeat sequence number.
    /// </summary>
    public long GetCurrentSequenceNumber() => Interlocked.Read(ref _sequenceNumber);

    private static PeerRegisterInfo ConvertToRegisterInfo(Protos.RegisterAdvertisement ad) => new()
    {
        RegisterId = ad.RegisterId,
        SyncState = ConvertFromProtoSyncState(ad.SyncState),
        LatestVersion = ad.LatestVersion,
        IsPublic = ad.IsPublic
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

    private static RegisterSyncState ConvertFromProtoSyncState(SyncStateProto state) => state switch
    {
        SyncStateProto.Subscribing => RegisterSyncState.Subscribing,
        SyncStateProto.Syncing => RegisterSyncState.Syncing,
        SyncStateProto.FullyReplicated => RegisterSyncState.FullyReplicated,
        SyncStateProto.Active => RegisterSyncState.Active,
        SyncStateProto.Error => RegisterSyncState.Error,
        _ => RegisterSyncState.Subscribing
    };
}
