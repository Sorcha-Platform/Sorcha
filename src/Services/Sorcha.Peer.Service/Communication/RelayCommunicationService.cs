// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Connection;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Discovery;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Core relay communication primitive. Routes messages through seed node channels
/// when peers are unreachable directly (NAT'd peers with empty Address).
/// Supports fire-and-forget (SendViaRelayAsync) and request/response correlation (SendAndWaitAsync).
/// </summary>
public class RelayCommunicationService
{
    private readonly ILogger<RelayCommunicationService> _logger;
    private readonly PeerConnectionPool _connectionPool;
    private readonly PeerListManager _peerListManager;
    private readonly PeerServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PeerMessage>> _pendingCorrelations = new();
    private AsyncDuplexStreamingCall<PeerMessage, PeerMessage>? _reverseStreamCall;
    private CancellationTokenSource? _reverseStreamCts;
    private Task? _reverseStreamReceiveTask;
    private Task? _reverseStreamKeepaliveTask;
    private int _reverseStreamBackoffMs;
    private readonly Lazy<RelayMessageHandler> _relayMessageHandler;

    /// <summary>
    /// Default timeout for relay request/response correlation (30 seconds)
    /// </summary>
    public static readonly TimeSpan DefaultCorrelationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keepalive interval for reverse stream ping messages.
    /// </summary>
    public static readonly TimeSpan KeepaliveInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum backoff delay for reverse stream reconnection.
    /// </summary>
    public static readonly TimeSpan MaxReconnectBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Whether the reverse stream is currently active and connected.
    /// </summary>
    public bool IsReverseStreamActive =>
        _reverseStreamCall != null &&
        _reverseStreamReceiveTask != null &&
        !_reverseStreamReceiveTask.IsCompleted;

    public RelayCommunicationService(
        ILogger<RelayCommunicationService> logger,
        PeerConnectionPool connectionPool,
        PeerListManager peerListManager,
        IOptions<PeerServiceConfiguration> configuration,
        Lazy<RelayMessageHandler> relayMessageHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
        _relayMessageHandler = relayMessageHandler ?? throw new ArgumentNullException(nameof(relayMessageHandler));
    }

    /// <summary>
    /// Sends a message to a peer via seed node relay (fire-and-forget).
    /// Prefers the active reverse stream if available; falls back to unary SendMessage.
    /// Returns true if the message was successfully forwarded to the seed node.
    /// </summary>
    public async Task<bool> SendViaRelayAsync(
        string targetPeerId,
        MessageType messageType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var peerMessage = CreatePeerMessage(targetPeerId, messageType, payload);

        // Prefer reverse stream if active
        if (IsReverseStreamActive && _reverseStreamCall != null)
        {
            try
            {
                await _reverseStreamCall.RequestStream.WriteAsync(peerMessage, cancellationToken);
                _logger.LogDebug(
                    "Relay message {MessageType} sent to {TargetPeerId} via reverse stream",
                    messageType, targetPeerId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Reverse stream send failed for {MessageType} to {TargetPeerId}, falling back to unary",
                    messageType, targetPeerId);
                // Fall through to unary send
            }
        }

        // Fall back to unary SendMessage
        var seedChannel = GetSeedChannel();
        if (seedChannel == null)
        {
            _logger.LogWarning("No seed node channel available for relay to peer {TargetPeerId}", targetPeerId);
            return false;
        }

        try
        {
            var client = new PeerCommunication.PeerCommunicationClient(seedChannel);
            var ack = await client.SendMessageAsync(peerMessage, cancellationToken: cancellationToken);

            if (ack.Received)
            {
                _logger.LogDebug("Relay message {MessageType} sent to {TargetPeerId} via unary relay",
                    messageType, targetPeerId);
            }

            return ack.Received;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to relay {MessageType} to peer {TargetPeerId}",
                messageType, targetPeerId);
            await _connectionPool.RecordFailureAsync(targetPeerId);
            return false;
        }
    }

    /// <summary>
    /// Sends a request via relay and waits for a correlated response.
    /// Returns the deserialized response, or null on timeout/failure.
    /// </summary>
    public async Task<TResponse?> SendAndWaitAsync<TResponse>(
        string targetPeerId,
        MessageType requestType,
        object request,
        string correlationId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) where TResponse : class
    {
        var tcs = new TaskCompletionSource<PeerMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCorrelations[correlationId] = tcs;

        try
        {
            var sent = await SendViaRelayAsync(targetPeerId, requestType, request, cancellationToken);
            if (!sent)
            {
                _pendingCorrelations.TryRemove(correlationId, out _);
                return null;
            }

            var effectiveTimeout = timeout ?? DefaultCorrelationTimeout;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                var responseMessage = await tcs.Task.WaitAsync(timeoutCts.Token);
                var json = responseMessage.Payload.ToStringUtf8();
                return JsonSerializer.Deserialize<TResponse>(json);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Relay correlation timeout for {CorrelationId} to peer {TargetPeerId} ({Timeout}s)",
                    correlationId, targetPeerId, effectiveTimeout.TotalSeconds);
                return null;
            }
        }
        finally
        {
            _pendingCorrelations.TryRemove(correlationId, out _);
        }
    }

    /// <summary>
    /// Completes a pending correlation with an incoming response message.
    /// Called by RelayMessageHandler when a response arrives via relay.
    /// Returns true if a pending correlation was found and completed.
    /// </summary>
    public bool CompleteCorrelation(string correlationId, PeerMessage responseMessage)
    {
        if (_pendingCorrelations.TryRemove(correlationId, out var tcs))
        {
            tcs.TrySetResult(responseMessage);
            _logger.LogDebug("Completed relay correlation {CorrelationId}", correlationId);
            return true;
        }

        _logger.LogDebug("No pending correlation for {CorrelationId} (expired or already completed)", correlationId);
        return false;
    }

    /// <summary>
    /// Gets the number of pending correlation entries (for diagnostics).
    /// </summary>
    public int PendingCorrelationCount => _pendingCorrelations.Count;

    /// <summary>
    /// Establishes a bidirectional reverse stream to the seed node.
    /// Incoming messages are dispatched to RelayMessageHandler.
    /// Sends keepalive pings every 30 seconds. Reconnects with exponential backoff on disconnect.
    /// </summary>
    public async Task EstablishReverseStreamAsync(CancellationToken cancellationToken = default)
    {
        _reverseStreamBackoffMs = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Create a DEDICATED channel for the reverse stream — do not share with
            // the heartbeat connection pool, which cycles channels on failures and
            // kills any in-flight streams.
            var seedAddress = GetSeedNodeAddress();
            if (seedAddress == null)
            {
                _logger.LogDebug("No seed node address available for reverse stream, retrying after backoff");
                await ApplyBackoffAsync(cancellationToken);
                continue;
            }

            Grpc.Net.Client.GrpcChannel? dedicatedChannel = null;
            try
            {
                dedicatedChannel = Grpc.Net.Client.GrpcChannel.ForAddress(seedAddress, new Grpc.Net.Client.GrpcChannelOptions
                {
                    HttpHandler = new SocketsHttpHandler
                    {
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(15),
                        EnableMultipleHttp2Connections = true
                    }
                });

                _reverseStreamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var client = new PeerCommunication.PeerCommunicationClient(dedicatedChannel);
                _reverseStreamCall = client.Stream(cancellationToken: _reverseStreamCts.Token);

                _logger.LogInformation("Reverse stream established to seed node");
                _reverseStreamBackoffMs = 0; // Reset backoff on successful connection

                // Send initial identification message immediately so the Router
                // registers this stream before the first keepalive tick (30s delay)
                var senderId = _configuration.ResolvedPeerId;
                var hello = new PeerMessage
                {
                    SenderPeerId = senderId,
                    RecipientPeerId = string.Empty,
                    MessageType = MessageType.Heartbeat,
                    Payload = Google.Protobuf.ByteString.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };
                await _reverseStreamCall.RequestStream.WriteAsync(hello, _reverseStreamCts.Token);
                _logger.LogDebug("Reverse stream identification sent as {PeerId}", senderId);

                // Start receive loop and keepalive in parallel
                _reverseStreamReceiveTask = RunReceiveLoopAsync(_reverseStreamCts.Token);
                _reverseStreamKeepaliveTask = RunKeepaliveLoopAsync(_reverseStreamCts.Token);

                // Wait for either to complete (indicates disconnection)
                await Task.WhenAny(_reverseStreamReceiveTask, _reverseStreamKeepaliveTask);

                // Cancel the CTS first to stop both tasks before cleanup
                _reverseStreamCts?.Cancel();
                try { await Task.WhenAll(_reverseStreamReceiveTask, _reverseStreamKeepaliveTask); }
                catch (OperationCanceledException) { }

                _logger.LogWarning("Reverse stream disconnected, will reconnect after backoff");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled &&
                                           cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reverse stream error, will reconnect after backoff");
            }
            finally
            {
                await CleanupReverseStreamAsync();
                dedicatedChannel?.Dispose();
            }

            await ApplyBackoffAsync(cancellationToken);
        }
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_reverseStreamCall == null) return;

        try
        {
            await foreach (var message in _reverseStreamCall.ResponseStream.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await _relayMessageHandler.Value.HandleAsync(message, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error handling reverse stream message {MessageType} from {SenderPeerId}",
                        message.MessageType, message.SenderPeerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Reverse stream receive loop cancelled");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            _logger.LogDebug("Reverse stream receive loop cancelled (gRPC)");
        }
    }

    private async Task RunKeepaliveLoopAsync(CancellationToken cancellationToken)
    {
        if (_reverseStreamCall == null) return;

        var senderId = _configuration.ResolvedPeerId;

        try
        {
            using var timer = new PeriodicTimer(KeepaliveInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var ping = new PeerMessage
                {
                    SenderPeerId = senderId,
                    RecipientPeerId = string.Empty,
                    MessageType = MessageType.Heartbeat,
                    Payload = Google.Protobuf.ByteString.Empty,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                await _reverseStreamCall.RequestStream.WriteAsync(ping, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Reverse stream keepalive loop cancelled");
        }
    }

    private async Task ApplyBackoffAsync(CancellationToken cancellationToken)
    {
        if (_reverseStreamBackoffMs == 0)
        {
            _reverseStreamBackoffMs = 2000; // Start at 2s
        }
        else
        {
            _reverseStreamBackoffMs = Math.Min(
                _reverseStreamBackoffMs * 2,
                (int)MaxReconnectBackoff.TotalMilliseconds);
        }

        _logger.LogDebug("Reverse stream reconnect backoff: {BackoffMs}ms", _reverseStreamBackoffMs);

        try
        {
            await Task.Delay(_reverseStreamBackoffMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
    }

    private async Task CleanupReverseStreamAsync()
    {
        try
        {
            if (_reverseStreamCall != null)
            {
                await _reverseStreamCall.RequestStream.CompleteAsync();
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        _reverseStreamCall?.Dispose();
        _reverseStreamCall = null;

        // CTS is already cancelled in EstablishReverseStreamAsync before cleanup;
        // just dispose here.
        _reverseStreamCts?.Dispose();
        _reverseStreamCts = null;
    }

    private PeerMessage CreatePeerMessage(string targetPeerId, MessageType messageType, object payload)
    {
        var senderId = _configuration.ResolvedPeerId;
        var payloadJson = JsonSerializer.Serialize(payload);

        return new PeerMessage
        {
            SenderPeerId = senderId,
            RecipientPeerId = targetPeerId,
            MessageType = messageType,
            Payload = Google.Protobuf.ByteString.CopyFromUtf8(payloadJson),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    /// <summary>
    /// Gets the gRPC address of the first available seed node for creating a dedicated channel.
    /// </summary>
    private string? GetSeedNodeAddress()
    {
        // Source the address from the SAME seed-endpoint configuration the connection pool uses to
        // reach the seed (PeerConnectionPool.BootstrapFromSeedNodesAsync → seed.GrpcChannelAddress),
        // so the reverse stream dials the identical scheme + gRPC port (e.g. https://host:50051).
        // The previous implementation reconstructed the address from the PeerNode list, which had
        // dropped the seed's TLS flag AND its gRPC port — it prepended "https://" but no port, so the
        // stream hit the seed's web vhost (:443) where PeerCommunication/Stream 404s. That marked the
        // seed "not alive" and silently disabled cross-node transaction fan-out.
        var seed = _configuration.SeedNodes?.SeedNodes?
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Hostname));
        if (seed is not null)
        {
            return seed.GrpcChannelAddress;
        }

        // Legacy fallback: a seed peer discovered into the peer list (carries no TLS flag — assume
        // plaintext h2c with the explicit gRPC port, matching the other outbound peer paths).
        foreach (var peer in _peerListManager.GetAllPeers())
        {
            if (peer.IsSeedNode && !string.IsNullOrEmpty(peer.Address))
            {
                if (peer.Address.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    return peer.Address;
                }
                return peer.Port > 0
                    ? $"http://{peer.Address}:{peer.Port}"
                    : $"http://{peer.Address}";
            }
        }

        // Last resort: the active seed channel from the connection pool.
        var seedChannel = GetSeedChannel();
        return seedChannel?.Target;
    }

    private Grpc.Net.Client.GrpcChannel? GetSeedChannel()
    {
        var activeChannels = _connectionPool.GetAllActiveChannels();

        foreach (var (peerId, channel) in activeChannels)
        {
            var peer = _peerListManager.GetPeer(peerId);
            if (peer?.IsSeedNode == true)
            {
                return channel;
            }
        }

        return null;
    }
}
