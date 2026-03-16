// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
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

    /// <summary>
    /// Default timeout for relay request/response correlation (30 seconds)
    /// </summary>
    public static readonly TimeSpan DefaultCorrelationTimeout = TimeSpan.FromSeconds(30);

    public RelayCommunicationService(
        ILogger<RelayCommunicationService> logger,
        PeerConnectionPool connectionPool,
        PeerListManager peerListManager,
        IOptions<PeerServiceConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _peerListManager = peerListManager ?? throw new ArgumentNullException(nameof(peerListManager));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));
    }

    /// <summary>
    /// Sends a message to a peer via seed node relay (fire-and-forget).
    /// Returns true if the message was successfully forwarded to the seed node.
    /// </summary>
    public async Task<bool> SendViaRelayAsync(
        string targetPeerId,
        MessageType messageType,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var seedChannel = GetSeedChannel();
        if (seedChannel == null)
        {
            _logger.LogWarning("No seed node channel available for relay to peer {TargetPeerId}", targetPeerId);
            return false;
        }

        try
        {
            var peerMessage = CreatePeerMessage(targetPeerId, messageType, payload);
            var client = new PeerCommunication.PeerCommunicationClient(seedChannel);
            var ack = await client.SendMessageAsync(peerMessage, cancellationToken: cancellationToken);

            if (ack.Received)
            {
                _logger.LogDebug("Relay message {MessageType} sent to {TargetPeerId} via seed node",
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

    private PeerMessage CreatePeerMessage(string targetPeerId, MessageType messageType, object payload)
    {
        var senderId = _configuration.NodeId ?? Environment.MachineName;
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
