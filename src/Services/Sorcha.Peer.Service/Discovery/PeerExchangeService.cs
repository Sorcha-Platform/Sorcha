// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Discovery;

/// <summary>
/// Gossip-style peer exchange service. Periodically exchanges peer lists
/// with connected peers to build a mesh topology beyond seed nodes.
/// </summary>
public class PeerExchangeService : IDisposable
{
    private readonly ILogger<PeerExchangeService> _logger;
    private readonly PeerListManager _peerListManager;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerServiceConfiguration _configuration;
    private readonly SemaphoreSlim _exchangeLock = new(1, 1);

    public PeerExchangeService(
        ILogger<PeerExchangeService> logger,
        PeerListManager peerListManager,
        PeerConnectionPool connectionPool,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Performs a peer exchange with a random selection of connected peers.
    /// Called periodically to grow the mesh topology.
    /// </summary>
    public async Task<int> ExchangeWithPeersAsync(CancellationToken cancellationToken = default)
    {
        await _exchangeLock.WaitAsync(cancellationToken);
        try
        {
            var activeChannels = _connectionPool.GetAllActiveChannels();
            if (activeChannels.Count == 0)
            {
                _logger.LogDebug("No active peer connections for exchange");
                return 0;
            }

            // Select random peers for exchange
            var peersToExchange = activeChannels
                .OrderBy(_ => Random.Shared.Next())
                .Take(PeerServiceConstants.GossipExchangePeerCount)
                .ToList();

            var totalDiscovered = 0;

            foreach (var (peerId, channel) in peersToExchange)
            {
                try
                {
                    var discovered = await ExchangeWithPeerAsync(peerId, channel, cancellationToken);
                    totalDiscovered += discovered;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Peer exchange failed with {PeerId}", peerId);
                }
            }

            if (totalDiscovered > 0)
            {
                _logger.LogInformation(
                    "Peer exchange discovered {Count} new peers from {PeerCount} exchanges",
                    totalDiscovered, peersToExchange.Count);
            }

            return totalDiscovered;
        }
        finally
        {
            _exchangeLock.Release();
        }
    }

    /// <summary>
    /// Exchanges peer list with a single peer.
    /// </summary>
    private async Task<int> ExchangeWithPeerAsync(
        string peerId,
        Grpc.Net.Client.GrpcChannel channel,
        CancellationToken cancellationToken)
    {
        var client = new PeerDiscovery.PeerDiscoveryClient(channel);

        // Build our peer list to share
        var ourPeers = _peerListManager.GetHealthyPeers()
            .Take(PeerServiceConstants.MaxPeersInExchangeResponse)
            .Select(ConvertToPeerInfo)
            .ToList();

        var request = new PeerExchangeRequest
        {
            PeerId = _configuration.NodeId ?? Environment.MachineName,
            MaxPeers = PeerServiceConstants.MaxPeersInExchangeResponse
        };
        request.KnownPeers.AddRange(ourPeers);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(PeerServiceConstants.ConnectionTimeoutSeconds));

        var response = await client.ExchangePeersAsync(request, cancellationToken: cts.Token);

        if (!response.Success)
        {
            _logger.LogDebug("Peer exchange rejected by {PeerId}: {Message}",
                peerId, response.Message);
            return 0;
        }

        // Merge received peers into our list
        var newCount = 0;
        foreach (var remotePeer in response.KnownPeers)
        {
            // Don't add ourselves
            if (remotePeer.PeerId == (_configuration.NodeId ?? Environment.MachineName))
                continue;

            var peer = ConvertToPeerNode(remotePeer);
            var added = await _peerListManager.AddOrUpdatePeerAsync(peer, cancellationToken);
            if (added)
            {
                newCount++;

                // Attempt to connect to newly discovered peer
                var address = $"http://{peer.Address}:{peer.Port}";
                await _connectionPool.ConnectToPeerAsync(peer.PeerId, address, cancellationToken);
            }
        }

        return newCount;
    }

    /// <summary>
    /// Finds peers that hold a specific register by querying connected peers.
    /// </summary>
    public async Task<IReadOnlyCollection<PeerNode>> FindPeersForRegisterAsync(
        string registerId,
        bool requireFullReplica = false,
        CancellationToken cancellationToken = default)
    {
        // First check local knowledge
        var localPeers = requireFullReplica
            ? _peerListManager.GetFullReplicaPeersForRegister(registerId)
            : _peerListManager.GetPeersForRegister(registerId);

        if (localPeers.Count > 0)
            return localPeers;

        // Query connected peers
        var activeChannels = _connectionPool.GetAllActiveChannels();
        var results = new List<PeerNode>();

        foreach (var (peerId, channel) in activeChannels)
        {
            try
            {
                var client = new PeerDiscovery.PeerDiscoveryClient(channel);
                var request = new FindPeersForRegisterRequest
                {
                    RegisterId = registerId,
                    RequestingPeerId = _configuration.NodeId ?? Environment.MachineName,
                    RequireFullReplica = requireFullReplica,
                    MaxPeers = 10
                };

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout));

                var response = await client.FindPeersForRegisterAsync(request, cancellationToken: cts.Token);
                foreach (var peerInfo in response.Peers)
                {
                    var peer = ConvertToPeerNode(peerInfo);
                    await _peerListManager.AddOrUpdatePeerAsync(peer, cancellationToken);
                    results.Add(peer);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "FindPeersForRegister failed with peer {PeerId}", peerId);
            }
        }

        return results.AsReadOnly();
    }

    private PeerInfo ConvertToPeerInfo(PeerNode peer)
    {
        var info = new PeerInfo
        {
            PeerId = peer.PeerId,
            Address = peer.Address,
            Port = peer.Port,
            IsSeedNode = peer.IsSeedNode,
            LastSeen = peer.LastSeen.ToUnixTimeSeconds()
        };
        info.SupportedProtocols.AddRange(peer.SupportedProtocols);

        foreach (var reg in peer.AdvertisedRegisters)
        {
            info.AdvertisedRegisters.Add(new PeerRegisterAdvertisement
            {
                RegisterId = reg.RegisterId,
                HasFullReplica = reg.CanServeFullReplica,
                LatestVersion = reg.LatestVersion,
                IsPublic = reg.IsPublic
            });
        }

        return info;
    }

    private PeerNode ConvertToPeerNode(PeerInfo peerInfo)
    {
        var peer = new PeerNode
        {
            PeerId = peerInfo.PeerId,
            Address = peerInfo.Address,
            Port = peerInfo.Port,
            SupportedProtocols = peerInfo.SupportedProtocols.ToList(),
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            IsSeedNode = peerInfo.IsSeedNode,
            AdvertisedRegisters = peerInfo.AdvertisedRegisters
                .Select(r => new PeerRegisterInfo
                {
                    RegisterId = r.RegisterId,
                    SyncState = r.HasFullReplica ? RegisterSyncState.FullyReplicated : RegisterSyncState.Active,
                    LatestVersion = r.LatestVersion,
                    IsPublic = r.IsPublic
                })
                .ToList()
        };

        return peer;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _exchangeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
