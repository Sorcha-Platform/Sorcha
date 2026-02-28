// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Protos;
using System.Collections.Concurrent;
using Grpc.Net.Client;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Manages communication protocols with automatic fallback
/// gRPC Streaming -> gRPC -> REST
/// </summary>
public class CommunicationProtocolManager
{
    private readonly ILogger<CommunicationProtocolManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CommunicationConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers;
    private readonly ConcurrentDictionary<string, StreamingCommunicationClient> _streamingClients;

    public CommunicationProtocolManager(
        ILogger<CommunicationProtocolManager> logger,
        ILoggerFactory loggerFactory,
        IOptions<PeerServiceConfiguration> configuration,
        HttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _configuration = configuration?.Value?.Communication ?? throw new ArgumentNullException(nameof(configuration));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _circuitBreakers = new ConcurrentDictionary<string, CircuitBreaker>();
        _streamingClients = new ConcurrentDictionary<string, StreamingCommunicationClient>();
    }

    /// <summary>
    /// Sends a message to a peer with automatic protocol fallback
    /// </summary>
    public async Task<bool> SendMessageAsync(
        PeerNode peer,
        object message,
        CancellationToken cancellationToken = default)
    {
        var breaker = GetOrCreateCircuitBreaker(peer.PeerId);

        try
        {
            return await breaker.ExecuteAsync(
                async () =>
                {
                    // Try protocols in preference order
                    if (peer.SupportedProtocols.Contains("GrpcStream"))
                    {
                        return await SendViaStreamingAsync(peer, message, cancellationToken);
                    }
                    else if (peer.SupportedProtocols.Contains("Grpc"))
                    {
                        return await SendViaGrpcAsync(peer, message, cancellationToken);
                    }
                    else if (peer.SupportedProtocols.Contains("Rest"))
                    {
                        return await SendViaRestAsync(peer, message, cancellationToken);
                    }

                    _logger.LogWarning("No supported protocol for peer {PeerId}", peer.PeerId);
                    return false;
                },
                async () =>
                {
                    // Fallback to REST if circuit is open
                    _logger.LogInformation("Using REST fallback for peer {PeerId}", peer.PeerId);
                    return await SendViaRestAsync(peer, message, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to peer {PeerId}", peer.PeerId);
            return false;
        }
    }

    /// <summary>
    /// Pings a peer with automatic protocol fallback
    /// </summary>
    public async Task<bool> PingPeerAsync(
        PeerNode peer,
        string requestingPeerId,
        CancellationToken cancellationToken = default)
    {
        var breaker = GetOrCreateCircuitBreaker(peer.PeerId);

        try
        {
            return await breaker.ExecuteAsync(
                async () =>
                {
                    // Try gRPC first
                    var address = $"http://{peer.Address}:{peer.Port}";
                    var channel = GrpcChannel.ForAddress(address);
                    var grpcClient = new PeerDiscovery.PeerDiscoveryClient(channel);

                    var request = new PingRequest
                    {
                        PeerId = requestingPeerId
                    };

                    var response = await grpcClient.PingAsync(request, cancellationToken: cancellationToken);
                    return response.Status == PeerStatus.Online;
                },
                async () =>
                {
                    // Fallback to REST
                    var restClient = CreateRestClient(peer);
                    return await restClient.PingAsync(requestingPeerId, cancellationToken);
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ping failed for peer {PeerId}", peer.PeerId);
            return false;
        }
    }

    /// <summary>
    /// Establishes a streaming connection to a peer
    /// </summary>
    public async Task<StreamingCommunicationClient?> EstablishStreamingAsync(
        PeerNode peer,
        CancellationToken cancellationToken = default)
    {
        if (!peer.SupportedProtocols.Contains("GrpcStream"))
        {
            _logger.LogDebug("Peer {PeerId} does not support streaming", peer.PeerId);
            return null;
        }

        var client = _streamingClients.GetOrAdd(peer.PeerId, _ =>
        {
            var address = $"http://{peer.Address}:{peer.Port}";
            var logger = _loggerFactory.CreateLogger<StreamingCommunicationClient>();
            return new StreamingCommunicationClient(logger, peer.PeerId, address);
        });

        if (await client.ConnectAsync(cancellationToken))
        {
            _logger.LogInformation("Established streaming connection to {PeerId}", peer.PeerId);
            return client;
        }

        _logger.LogWarning("Failed to establish streaming connection to {PeerId}", peer.PeerId);
        _streamingClients.TryRemove(peer.PeerId, out _);
        return null;
    }

    /// <summary>
    /// Closes all streaming connections
    /// </summary>
    public async Task CloseAllStreamsAsync()
    {
        var tasks = _streamingClients.Values.Select(client => client.CloseAsync());
        await Task.WhenAll(tasks);
        _streamingClients.Clear();
        _logger.LogInformation("Closed all streaming connections");
    }

    /// <summary>
    /// Gets circuit breaker statistics for all peers
    /// </summary>
    public IReadOnlyDictionary<string, CircuitBreakerStats> GetCircuitBreakerStats()
    {
        return _circuitBreakers.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetStats());
    }

    /// <summary>
    /// Resets circuit breaker for a specific peer
    /// </summary>
    public void ResetCircuitBreaker(string peerId)
    {
        if (_circuitBreakers.TryGetValue(peerId, out var breaker))
        {
            breaker.Reset();
            _logger.LogInformation("Reset circuit breaker for peer {PeerId}", peerId);
        }
    }

    private async Task<bool> SendViaStreamingAsync(
        PeerNode peer,
        object message,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Sending via streaming to {PeerId}", peer.PeerId);

        if (!_streamingClients.TryGetValue(peer.PeerId, out var client))
        {
            client = await EstablishStreamingAsync(peer, cancellationToken);
            if (client == null)
            {
                return false;
            }
        }

        // Convert message to PeerMessage proto
        var peerMessage = new PeerMessage
        {
            SenderPeerId = "",
            RecipientPeerId = peer.PeerId,
            MessageType = MessageType.TransactionNotification,
            Payload = Google.Protobuf.ByteString.CopyFromUtf8(
                System.Text.Json.JsonSerializer.Serialize(message)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        return await client.SendMessageAsync(peerMessage, cancellationToken);
    }

    private async Task<bool> SendViaGrpcAsync(
        PeerNode peer,
        object message,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Sending via gRPC to {PeerId}", peer.PeerId);

        // Use standard gRPC call
        var address = $"http://{peer.Address}:{peer.Port}";
        using var channel = GrpcChannel.ForAddress(address);
        var client = new PeerCommunication.PeerCommunicationClient(channel);

        var peerMessage = new PeerMessage
        {
            SenderPeerId = "",
            RecipientPeerId = peer.PeerId,
            MessageType = MessageType.TransactionNotification,
            Payload = Google.Protobuf.ByteString.CopyFromUtf8(
                System.Text.Json.JsonSerializer.Serialize(message)),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var response = await client.SendMessageAsync(peerMessage, cancellationToken: cancellationToken);
        return response.Received;
    }

    private async Task<bool> SendViaRestAsync(
        PeerNode peer,
        object message,
        CancellationToken cancellationToken)
    {
        _logger.LogTrace("Sending via REST to {PeerId}", peer.PeerId);

        var restClient = CreateRestClient(peer);
        return await restClient.SendMessageAsync(message, cancellationToken);
    }

    private RestFallbackClient CreateRestClient(PeerNode peer)
    {
        var baseUrl = $"http://{peer.Address}:{peer.Port}";
        var logger = _loggerFactory.CreateLogger<RestFallbackClient>();
        return new RestFallbackClient(logger, _httpClient, baseUrl);
    }

    private CircuitBreaker GetOrCreateCircuitBreaker(string peerId)
    {
        return _circuitBreakers.GetOrAdd(peerId, _ =>
        {
            var logger = _loggerFactory.CreateLogger<CircuitBreaker>();
            return new CircuitBreaker(
                logger,
                $"peer-{peerId}",
                _configuration.CircuitBreakerThreshold,
                TimeSpan.FromMinutes(_configuration.CircuitBreakerResetMinutes));
        });
    }
}
