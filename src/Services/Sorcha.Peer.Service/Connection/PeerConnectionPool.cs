// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;
using System.Collections.Concurrent;

namespace Sorcha.Peer.Service.Connection;

/// <summary>
/// Manages a pool of gRPC connections to multiple peers.
/// Replaces the single-hub HubNodeConnectionManager with a multi-peer
/// connection pool for true P2P topology.
/// </summary>
/// <remarks>
/// Connection strategy:
/// - Maintains connections to multiple peers simultaneously
/// - Seed nodes are always connected (never evicted)
/// - Regular peers evicted on excessive failure (5+ consecutive)
/// - Channels created lazily on first use and cached
/// - Idle channels cleaned up periodically
/// </remarks>
public class PeerConnectionPool : IAsyncDisposable
{
    private readonly ILogger<PeerConnectionPool> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceActivitySource _activitySource;
    private readonly PeerServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, PeerConnection> _connections;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Maximum number of simultaneous peer connections
    /// </summary>
    public int MaxConnections { get; }

    /// <summary>
    /// Number of currently active connections
    /// </summary>
    public int ActiveConnectionCount => _connections.Count(c => c.Value.IsConnected);

    public PeerConnectionPool(
        ILogger<PeerConnectionPool> logger,
        ILoggerFactory loggerFactory,
        PeerListManager peerListManager,
        IOptions<PeerServiceConfiguration> configuration,
        PeerServiceMetrics metrics,
        PeerServiceActivitySource activitySource)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _activitySource = activitySource ?? throw new ArgumentNullException(nameof(activitySource));

        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        MaxConnections = _configuration.PeerDiscovery.MaxPeersInList;
        _connections = new ConcurrentDictionary<string, PeerConnection>();
        _circuitBreakers = new ConcurrentDictionary<string, CircuitBreaker>();
    }

    /// <summary>
    /// Bootstraps connections to seed nodes from configuration.
    /// Called during service startup to establish initial mesh.
    /// </summary>
    public async Task BootstrapFromSeedNodesAsync(CancellationToken cancellationToken = default)
    {
        var seedNodes = _configuration.SeedNodes?.SeedNodes ?? [];

        if (seedNodes.Count == 0)
        {
            _logger.LogWarning("No seed nodes configured — peer will operate in isolated mode");
            _peerListManager.UpdateLocalPeerStatus(null, PeerConnectionStatus.Isolated);
            _metrics.RecordConnectionStatus(PeerConnectionStatus.Isolated);
            return;
        }

        _logger.LogInformation("Bootstrapping from {Count} seed nodes", seedNodes.Count);

        var connectedToAny = false;
        foreach (var seed in seedNodes)
        {
            try
            {
                var peer = new PeerNode
                {
                    PeerId = seed.NodeId,
                    Address = seed.Hostname,
                    Port = seed.Port,
                    IsSeedNode = true,
                    SupportedProtocols = ["GrpcStream"]
                };

                await _peerListManager.AddOrUpdatePeerAsync(peer, cancellationToken);
                var connected = await ConnectToPeerAsync(seed.NodeId, seed.GrpcChannelAddress, cancellationToken);

                if (connected)
                {
                    connectedToAny = true;
                    _logger.LogInformation(
                        "Connected to seed node {NodeId} at {Address}",
                        seed.NodeId, seed.GrpcChannelAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to connect to seed node {NodeId} at {Address}",
                    seed.NodeId, seed.GrpcChannelAddress);
            }
        }

        if (connectedToAny)
        {
            _peerListManager.UpdateLocalPeerStatus(
                seedNodes.First().NodeId,
                PeerConnectionStatus.Connected);
            _metrics.RecordConnectionStatus(PeerConnectionStatus.Connected);
        }
        else
        {
            _logger.LogWarning("Failed to connect to any seed nodes — entering isolated mode");
            _peerListManager.UpdateLocalPeerStatus(null, PeerConnectionStatus.Isolated);
            _metrics.RecordConnectionStatus(PeerConnectionStatus.Isolated);
        }
    }

    /// <summary>
    /// Gets or creates a gRPC channel to the specified peer.
    /// </summary>
    /// <param name="peerId">Target peer identifier</param>
    /// <returns>gRPC channel or null if peer not found/connectable</returns>
    public GrpcChannel? GetChannel(string peerId)
    {
        if (_connections.TryGetValue(peerId, out var connection) && connection.IsConnected)
        {
            connection.LastUsed = DateTimeOffset.UtcNow;
            return connection.Channel;
        }

        return null;
    }

    /// <summary>
    /// Gets channels for all connected peers.
    /// </summary>
    public IReadOnlyList<(string PeerId, GrpcChannel Channel)> GetAllActiveChannels()
    {
        return _connections
            .Where(c => c.Value.IsConnected)
            .Select(c => (c.Key, c.Value.Channel!))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets channels for peers that hold a specific register.
    /// </summary>
    public IReadOnlyList<(string PeerId, GrpcChannel Channel)> GetChannelsForRegister(string registerId)
    {
        var peersForRegister = _peerListManager.GetPeersForRegister(registerId);
        var peerIds = peersForRegister.Select(p => p.PeerId).ToHashSet();

        return _connections
            .Where(c => c.Value.IsConnected && peerIds.Contains(c.Key))
            .Select(c => (c.Key, c.Value.Channel!))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Connects to a specific peer by ID and address.
    /// </summary>
    public async Task<bool> ConnectToPeerAsync(
        string peerId,
        string grpcAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(peerId) || string.IsNullOrEmpty(grpcAddress))
            return false;

        // Check if already connected
        if (_connections.TryGetValue(peerId, out var existing) && existing.IsConnected)
        {
            _logger.LogDebug("Already connected to peer {PeerId}", peerId);
            return true;
        }

        // Check circuit breaker before attempting connection
        var breaker = GetOrCreateCircuitBreaker(peerId);
        var breakerState = breaker.State;

        if (breakerState == CircuitState.Open)
        {
            _logger.LogWarning(
                "Circuit breaker is open for peer {PeerId} — connection attempt rejected",
                peerId);
            throw new CircuitBreakerOpenException(
                $"Circuit breaker is open for peer {peerId}");
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            // Double-check after acquiring lock
            if (_connections.TryGetValue(peerId, out existing) && existing.IsConnected)
                return true;

            // Check connection limit
            if (_connections.Count >= MaxConnections)
            {
                EvictLeastUsedConnection();
            }

            var channel = CreateChannel(grpcAddress);
            var connection = new PeerConnection
            {
                PeerId = peerId,
                Address = grpcAddress,
                Channel = channel,
                IsConnected = true,
                ConnectedAt = DateTimeOffset.UtcNow,
                LastUsed = DateTimeOffset.UtcNow
            };

            _connections.AddOrUpdate(peerId, connection, (_, old) =>
            {
                old.Channel?.Dispose();
                return connection;
            });

            // Record success on the circuit breaker
            RecordCircuitBreakerSuccess(peerId);

            _logger.LogDebug("Connected to peer {PeerId} at {Address}", peerId, grpcAddress);
            return true;
        }
        catch (CircuitBreakerOpenException)
        {
            throw; // Re-throw circuit breaker exceptions
        }
        catch (Exception ex)
        {
            // Record failure on the circuit breaker
            RecordCircuitBreakerFailure(peerId);

            _logger.LogError(ex, "Failed to connect to peer {PeerId} at {Address}", peerId, grpcAddress);
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Disconnects from a specific peer.
    /// </summary>
    public async Task DisconnectPeerAsync(string peerId)
    {
        if (_connections.TryRemove(peerId, out var connection))
        {
            if (connection.Channel != null)
            {
                try
                {
                    await connection.Channel.ShutdownAsync();
                    connection.Channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error shutting down channel to peer {PeerId}", peerId);
                }
            }

            _logger.LogInformation("Disconnected from peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// Records a connection failure for a peer. After threshold failures,
    /// the connection is removed (unless it's a seed node).
    /// </summary>
    public async Task RecordFailureAsync(string peerId)
    {
        if (_connections.TryGetValue(peerId, out var connection))
        {
            connection.ConsecutiveFailures++;
            _logger.LogWarning(
                "Peer {PeerId} failure count: {Count}",
                peerId, connection.ConsecutiveFailures);

            if (connection.ConsecutiveFailures >= PeerServiceConstants.MaxConsecutiveFailuresBeforeDisconnect)
            {
                var peer = _peerListManager.GetPeer(peerId);
                if (peer is { IsSeedNode: true })
                {
                    // Seed nodes: mark disconnected but don't remove
                    connection.IsConnected = false;
                    _logger.LogWarning(
                        "Seed node {PeerId} marked disconnected after {Count} failures (not removed)",
                        peerId, connection.ConsecutiveFailures);
                }
                else
                {
                    await DisconnectPeerAsync(peerId);
                    await _peerListManager.IncrementFailureCountAsync(peerId);
                }
            }
        }
    }

    /// <summary>
    /// Records a successful interaction with a peer, resetting failure count.
    /// </summary>
    public void RecordSuccess(string peerId)
    {
        if (_connections.TryGetValue(peerId, out var connection))
        {
            connection.ConsecutiveFailures = 0;
            connection.LastUsed = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Cleans up idle connections that haven't been used recently.
    /// Seed node connections are never cleaned up.
    /// </summary>
    public async Task CleanupIdleConnectionsAsync(TimeSpan maxIdleTime)
    {
        var cutoff = DateTimeOffset.UtcNow - maxIdleTime;
        var idleConnections = _connections
            .Where(c => c.Value.LastUsed < cutoff)
            .ToList();

        foreach (var (peerId, connection) in idleConnections)
        {
            var peer = _peerListManager.GetPeer(peerId);
            if (peer is { IsSeedNode: true })
                continue; // Never evict seed nodes

            _logger.LogDebug(
                "Cleaning up idle connection to peer {PeerId} (last used: {LastUsed})",
                peerId, connection.LastUsed);

            await DisconnectPeerAsync(peerId);
        }
    }

    /// <summary>
    /// Attempts to reconnect to any disconnected seed nodes.
    /// Seed nodes are critical infrastructure and should be reconnected promptly.
    /// After reconnecting, re-registers with the remote peer so it adds us
    /// back to its routing table (required after heartbeat rejection).
    /// </summary>
    public async Task ReconnectDisconnectedSeedNodesAsync(CancellationToken cancellationToken = default)
    {
        var disconnectedSeeds = _connections
            .Where(c => !c.Value.IsConnected)
            .Where(c => _peerListManager.GetPeer(c.Key) is { IsSeedNode: true })
            .ToList();

        foreach (var (peerId, connection) in disconnectedSeeds)
        {
            try
            {
                var reconnected = await ConnectToPeerAsync(peerId, connection.Address, cancellationToken);
                if (reconnected)
                {
                    _logger.LogInformation("Reconnected to seed node {PeerId}", peerId);
                    connection.ConsecutiveFailures = 0;

                    // Re-register with the remote peer so it adds us to its routing table
                    await RegisterWithRemotePeerAsync(peerId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to reconnect to seed node {PeerId}", peerId);
            }
        }
    }

    /// <summary>
    /// Registers this node with a remote peer using the existing gRPC channel.
    /// Called after reconnection to ensure the remote peer's routing table
    /// includes us (required after heartbeat rejection / timeout eviction).
    /// </summary>
    private async Task RegisterWithRemotePeerAsync(string peerId, CancellationToken cancellationToken)
    {
        var channel = GetChannel(peerId);
        if (channel is null)
        {
            _logger.LogWarning("Cannot re-register with {PeerId} — no active channel", peerId);
            return;
        }

        try
        {
            var client = new PeerDiscovery.PeerDiscoveryClient(channel);
            var localPeer = _peerListManager.GetLocalPeerStatus();
            var request = new RegisterPeerRequest
            {
                PeerInfo = new PeerInfo
                {
                    PeerId = localPeer?.PeerId ?? _configuration.NodeId ?? Environment.MachineName,
                    Address = _configuration.NetworkAddress.ExternalAddress ?? Environment.MachineName,
                    Port = _configuration.ListenPort,
                    SupportedProtocols = { "GrpcStream", "Grpc", "Rest" }
                }
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await client.RegisterPeerAsync(request, cancellationToken: cts.Token);

            if (response.Success)
            {
                _logger.LogInformation(
                    "Re-registered with peer {PeerId}: {Message}", peerId, response.Message);
            }
            else
            {
                _logger.LogWarning(
                    "Re-registration rejected by {PeerId}: {Message}", peerId, response.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to re-register with peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// Marks a peer's connection as disconnected so the reconnection loop
    /// will re-establish the connection and re-register with the remote.
    /// Unlike RecordFailureAsync, this does not increment failure counts —
    /// it handles logical rejections (e.g. "Peer not registered") where
    /// the transport is healthy but the remote has dropped the peer's state.
    /// </summary>
    public void MarkPeerForReconnection(string peerId)
    {
        if (_connections.TryGetValue(peerId, out var connection))
        {
            connection.IsConnected = false;
            connection.ConsecutiveFailures = 0;
            _logger.LogInformation(
                "Peer {PeerId} marked for reconnection (logical rejection)",
                peerId);
        }
    }

    /// <summary>
    /// Gets the connection status for all peers.
    /// </summary>
    public IReadOnlyDictionary<string, bool> GetConnectionStatuses()
    {
        return _connections
            .ToDictionary(c => c.Key, c => c.Value.IsConnected);
    }

    /// <summary>
    /// Gets circuit breaker statistics for all peers in the connection pool.
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerStats> GetCircuitBreakerStats()
    {
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStats());
    }

    /// <summary>
    /// Gets or creates a circuit breaker for the specified peer.
    /// </summary>
    internal CircuitBreaker GetOrCreateCircuitBreaker(string peerId)
    {
        return _circuitBreakers.GetOrAdd(peerId, _ =>
        {
            var logger = _loggerFactory.CreateLogger<CircuitBreaker>();
            return new CircuitBreaker(
                logger,
                $"connection-{peerId}",
                _configuration.Communication.CircuitBreakerThreshold,
                TimeSpan.FromMinutes(_configuration.Communication.CircuitBreakerResetMinutes));
        });
    }

    private void RecordCircuitBreakerSuccess(string peerId)
    {
        if (_circuitBreakers.TryGetValue(peerId, out var breaker))
        {
            breaker.OnSuccess();
        }
    }

    private void RecordCircuitBreakerFailure(string peerId)
    {
        if (_circuitBreakers.TryGetValue(peerId, out var breaker))
        {
            breaker.OnFailure();
        }
    }

    private GrpcChannel CreateChannel(string address)
    {
        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true
            }
        });
    }

    private void EvictLeastUsedConnection()
    {
        // Find the least recently used non-seed connection
        var leastUsed = _connections
            .Where(c =>
            {
                var peer = _peerListManager.GetPeer(c.Key);
                return peer is not { IsSeedNode: true };
            })
            .OrderBy(c => c.Value.LastUsed)
            .FirstOrDefault();

        if (leastUsed.Key != null)
        {
            _logger.LogDebug(
                "Evicting least-used connection to peer {PeerId} (last used: {LastUsed})",
                leastUsed.Key, leastUsed.Value.LastUsed);

            if (_connections.TryRemove(leastUsed.Key, out var connection))
            {
                connection.Channel?.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (peerId, connection) in _connections)
        {
            if (connection.Channel != null)
            {
                try
                {
                    await connection.Channel.ShutdownAsync();
                    connection.Channel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing channel to peer {PeerId}", peerId);
                }
            }
        }

        _connections.Clear();
        _connectLock.Dispose();
    }
}

/// <summary>
/// Represents a connection to a single peer.
/// </summary>
internal class PeerConnection
{
    public required string PeerId { get; init; }
    public required string Address { get; init; }
    public GrpcChannel? Channel { get; set; }
    public bool IsConnected { get; set; }
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset LastUsed { get; set; }
    public int ConsecutiveFailures { get; set; }
}
