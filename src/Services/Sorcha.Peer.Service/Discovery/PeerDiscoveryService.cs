// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Network;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Discovery;

/// <summary>
/// Service for discovering and connecting to peers
/// </summary>
public class PeerDiscoveryService : IDisposable
{
    private readonly ILogger<PeerDiscoveryService> _logger;
    private readonly PeerServiceConfiguration _configuration;
    private readonly PeerListManager _peerListManager;
    private readonly NetworkAddressService _networkAddressService;
    private readonly SemaphoreSlim _discoveryLock = new(1, 1);

    public PeerDiscoveryService(
        ILogger<PeerDiscoveryService> logger,
        IOptions<PeerServiceConfiguration> configuration,
        PeerListManager peerListManager,
        NetworkAddressService networkAddressService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _networkAddressService = networkAddressService ?? throw new ArgumentNullException(nameof(networkAddressService));
    }

    /// <summary>
    /// Performs peer discovery by connecting to bootstrap nodes
    /// </summary>
    public async Task<int> DiscoverPeersAsync(CancellationToken cancellationToken = default)
    {
        await _discoveryLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting peer discovery");

            var discoveredCount = 0;
            var bootstrapNodes = _configuration.PeerDiscovery.BootstrapNodes;

            if (bootstrapNodes.Count == 0)
            {
                _logger.LogWarning("No bootstrap nodes configured");
                return 0;
            }

            // Connect to bootstrap nodes
            foreach (var bootstrapNode in bootstrapNodes)
            {
                try
                {
                    var count = await DiscoverFromNodeAsync(bootstrapNode, cancellationToken);
                    discoveredCount += count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to discover peers from bootstrap node: {Node}", bootstrapNode);
                }
            }

            _logger.LogInformation("Discovered {Count} new peers", discoveredCount);
            return discoveredCount;
        }
        finally
        {
            _discoveryLock.Release();
        }
    }

    /// <summary>
    /// Discovers peers from a specific node
    /// </summary>
    private async Task<int> DiscoverFromNodeAsync(string nodeAddress, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Connecting to node: {Node}", nodeAddress);

        try
        {
            // Parse node address (format: host:port)
            var parts = nodeAddress.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port))
            {
                _logger.LogWarning("Invalid node address format: {Node}", nodeAddress);
                return 0;
            }

            var address = $"http://{nodeAddress}"; // Use http for now, https in production

            using var channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout)
                }
            });

            var client = new PeerDiscovery.PeerDiscoveryClient(channel);

            // Request peer list
            var request = new PeerListRequest
            {
                RequestingPeerId = _configuration.NodeId ?? "unknown",
                MaxPeers = 50
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout));

            var response = await client.GetPeerListAsync(request, cancellationToken: cts.Token);

            _logger.LogInformation("Received {Count} peers from {Node}", response.Peers.Count, nodeAddress);

            // Add peers to the list
            var addedCount = 0;
            foreach (var peerInfo in response.Peers)
            {
                var peer = ConvertToPeerNode(peerInfo);
                var added = await _peerListManager.AddOrUpdatePeerAsync(peer, cancellationToken);
                if (added)
                {
                    addedCount++;
                }
            }

            return addedCount;
        }
        catch (RpcException ex)
        {
            _logger.LogWarning("gRPC error connecting to {Node}: {Status} - {Message}",
                nodeAddress, ex.StatusCode, ex.Message);
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering peers from {Node}", nodeAddress);
            return 0;
        }
    }

    /// <summary>
    /// Registers this node with a bootstrap node
    /// </summary>
    public async Task<bool> RegisterWithBootstrapAsync(string nodeAddress, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Registering with bootstrap node: {Node}", nodeAddress);

        try
        {
            var externalAddress = await _networkAddressService.GetExternalAddressAsync(cancellationToken);
            if (externalAddress == null)
            {
                _logger.LogWarning("Cannot register without external address");
                return false;
            }

            var address = $"http://{nodeAddress}";
            using var channel = GrpcChannel.ForAddress(address);
            var client = new PeerDiscovery.PeerDiscoveryClient(channel);

            var request = new RegisterPeerRequest
            {
                PeerInfo = new PeerInfo
                {
                    PeerId = _configuration.NodeId ?? "unknown",
                    Address = externalAddress,
                    Port = _configuration.ListenPort,
                    SupportedProtocols = { "GrpcStream", "Grpc", "Rest" }
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_configuration.Communication.ConnectionTimeout));

            var response = await client.RegisterPeerAsync(request, cancellationToken: cts.Token);

            if (response.Success)
            {
                _logger.LogInformation("Successfully registered with bootstrap node");
                return true;
            }
            else
            {
                _logger.LogWarning("Bootstrap node rejected registration: {Message}", response.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering with bootstrap node: {Node}", nodeAddress);
            return false;
        }
    }

    /// <summary>
    /// Pings a peer to check if it's alive
    /// </summary>
    public async Task<bool> PingPeerAsync(string nodeAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var address = $"http://{nodeAddress}";
            using var channel = GrpcChannel.ForAddress(address);
            var client = new PeerDiscovery.PeerDiscoveryClient(channel);

            var request = new PingRequest
            {
                PeerId = _configuration.NodeId ?? "unknown"
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await client.PingAsync(request, cancellationToken: cts.Token);

            return response.Status == PeerStatus.Online;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Ping failed for {Node}: {Message}", nodeAddress, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Converts a PeerInfo proto message to a PeerNode
    /// </summary>
    private PeerNode ConvertToPeerNode(PeerInfo peerInfo)
    {
        return new PeerNode
        {
            PeerId = peerInfo.PeerId,
            Address = peerInfo.Address,
            Port = peerInfo.Port,
            SupportedProtocols = peerInfo.SupportedProtocols.ToList(),
            FirstSeen = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow,
            FailureCount = 0,
            IsSeedNode = false,
            AverageLatencyMs = 0,
            Capabilities = new Core.PeerCapabilities
            {
                SupportsStreaming = peerInfo.SupportedProtocols.Contains("GrpcStream"),
                SupportsTransactionDistribution = true,
                MaxTransactionSize = 10 * 1024 * 1024
            }
        };
    }

    /// <summary>
    /// Checks if minimum healthy peer count is met
    /// </summary>
    public bool HasMinimumPeers()
    {
        var healthyCount = _peerListManager.GetHealthyPeerCount();
        var minimum = _configuration.PeerDiscovery.MinHealthyPeers;

        _logger.LogDebug("Healthy peers: {Count}/{Minimum}", healthyCount, minimum);

        return healthyCount >= minimum;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _discoveryLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
