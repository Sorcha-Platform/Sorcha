// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Grpc.Net.Client;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.GrpcServices;

/// <summary>
/// gRPC implementation of PeerCommunication for relay mode.
/// When relay is enabled, forwards messages between peers that cannot reach each other directly.
/// Supports both direct forwarding and reverse-stream relay for NAT'd peers.
/// </summary>
public sealed class RouterCommunicationService : PeerCommunication.PeerCommunicationBase
{
    private readonly RoutingTable _routingTable;
    private readonly ReverseStreamManager _reverseStreamManager;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _config;
    private readonly ILogger<RouterCommunicationService> _logger;
    private readonly RouterChannelPool _channelPool;

    public RouterCommunicationService(
        RoutingTable routingTable,
        ReverseStreamManager reverseStreamManager,
        EventBuffer eventBuffer,
        RouterConfiguration config,
        ILogger<RouterCommunicationService> logger,
        RouterChannelPool channelPool)
    {
        _routingTable = routingTable;
        _reverseStreamManager = reverseStreamManager;
        _eventBuffer = eventBuffer;
        _config = config;
        _logger = logger;
        _channelPool = channelPool;
    }

    /// <summary>
    /// Receives a message from a sender peer and forwards it to the recipient peer.
    /// First attempts reverse stream relay for NAT'd peers, then falls back to direct forwarding.
    /// Only operational when relay mode is enabled via --enable-relay.
    /// </summary>
    public override async Task<MessageAck> SendMessage(PeerMessage request, ServerCallContext context)
    {
        if (!_config.EnableRelay)
        {
            _logger.LogWarning(
                "Relay request rejected: relay mode is not enabled. Sender={SenderId}, Recipient={RecipientId}",
                request.SenderPeerId, request.RecipientPeerId);

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "Relay mode is not enabled. Start the router with --enable-relay to use this feature."));
        }

        if (string.IsNullOrEmpty(request.RecipientPeerId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Recipient peer ID is required."));
        }

        if (string.IsNullOrEmpty(request.SenderPeerId))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "Sender peer ID is required."));
        }

        var recipient = _routingTable.GetPeer(request.RecipientPeerId);
        if (recipient is null || !recipient.IsHealthy)
        {
            // Check if the recipient has a reverse stream even without a routing entry
            if (_reverseStreamManager.TryGetStream(request.RecipientPeerId, out var streamEntry))
            {
                return await RelayViaReverseStreamAsync(request, streamEntry!);
            }

            _logger.LogWarning(
                "Relay failed: recipient peer {RecipientId} is not registered or unhealthy",
                request.RecipientPeerId);

            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Recipient peer '{request.RecipientPeerId}' is not registered or is unhealthy."));
        }

        // Try reverse stream first for peers with empty addresses (NAT'd peers)
        if (string.IsNullOrEmpty(recipient.Address) &&
            _reverseStreamManager.TryGetStream(request.RecipientPeerId, out var reverseStream))
        {
            return await RelayViaReverseStreamAsync(request, reverseStream!);
        }

        // Fall back to direct channel forwarding
        try
        {
            var recipientAddress = $"http://{recipient.Address}:{recipient.Port}";
            var channel = GetOrCreateChannel(recipientAddress);
            var client = new PeerCommunication.PeerCommunicationClient(channel);

            var ack = await client.SendMessageAsync(request, cancellationToken: context.CancellationToken);

            _eventBuffer.Add(RouterEvent.Create(
                RouterEventType.RelayForwarded,
                request.SenderPeerId,
                recipient.IpAddress,
                recipient.Port,
                detail: new Dictionary<string, object?>
                {
                    ["recipient_peer_id"] = request.RecipientPeerId,
                    ["message_type"] = request.MessageType.ToString(),
                    ["payload_size"] = request.Payload.Length
                }));

            _logger.LogInformation(
                "Relayed message from {SenderId} to {RecipientId} ({MessageType}, {Size} bytes)",
                request.SenderPeerId,
                request.RecipientPeerId,
                request.MessageType,
                request.Payload.Length);

            return ack;
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            _logger.LogWarning(
                ex,
                "Relay failed: could not reach recipient peer {RecipientId} at {Address}:{Port}",
                request.RecipientPeerId,
                recipient.Address,
                recipient.Port);

            throw new RpcException(new Status(
                StatusCode.Unavailable,
                $"Could not reach recipient peer '{request.RecipientPeerId}'."));
        }
    }

    /// <summary>
    /// Bidirectional streaming for NAT'd peers that cannot receive inbound connections.
    /// The peer establishes a long-lived stream; the router uses it to push relay messages.
    /// </summary>
    public override async Task Stream(
        IAsyncStreamReader<PeerMessage> requestStream,
        IServerStreamWriter<PeerMessage> responseStream,
        ServerCallContext context)
    {
        if (!_config.EnableRelay)
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "Relay mode is not enabled. Start the router with --enable-relay to use this feature."));
        }

        string? senderPeerId = null;
        CancellationTokenSource? linkedCts = null;

        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                // Extract sender peer ID from the first message
                if (senderPeerId is null)
                {
                    if (string.IsNullOrEmpty(message.SenderPeerId))
                    {
                        throw new RpcException(new Status(
                            StatusCode.InvalidArgument,
                            "First message must include sender_peer_id."));
                    }

                    senderPeerId = message.SenderPeerId;
                    _reverseStreamManager.RegisterStream(senderPeerId, responseStream);

                    // Create a linked token that cancels when either the context or the entry's CTS fires
                    if (_reverseStreamManager.TryGetStream(senderPeerId, out var registeredEntry))
                    {
                        linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            context.CancellationToken, registeredEntry!.StreamCts.Token);
                    }

                    _eventBuffer.Add(RouterEvent.Create(
                        RouterEventType.StreamConnected,
                        senderPeerId,
                        context.Peer ?? "",
                        0,
                        detail: new Dictionary<string, object?>
                        {
                            ["stream_type"] = "reverse"
                        }));

                    _logger.LogInformation(
                        "Reverse stream established for peer {PeerId}", senderPeerId);
                }

                // Check if this stream has been superseded by a reconnect
                if (linkedCts?.Token.IsCancellationRequested == true)
                    break;

                // Update activity timestamp
                if (_reverseStreamManager.TryGetStream(senderPeerId, out var entry))
                {
                    entry!.LastActivityAt = DateTimeOffset.UtcNow;
                }

                // If the message has a recipient, forward it
                if (!string.IsNullOrEmpty(message.RecipientPeerId))
                {
                    await ForwardStreamMessageAsync(message, senderPeerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected normally or stream was superseded
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Client cancelled the stream
        }
        finally
        {
            linkedCts?.Dispose();

            if (senderPeerId is not null)
            {
                _reverseStreamManager.RemoveStream(senderPeerId);

                _eventBuffer.Add(RouterEvent.Create(
                    RouterEventType.StreamDisconnected,
                    senderPeerId,
                    context.Peer ?? "",
                    0,
                    detail: new Dictionary<string, object?>
                    {
                        ["stream_type"] = "reverse"
                    }));

                _logger.LogInformation(
                    "Reverse stream disconnected for peer {PeerId}", senderPeerId);
            }
        }
    }

    /// <summary>
    /// Relays a message to a recipient via their active reverse stream.
    /// </summary>
    private async Task<MessageAck> RelayViaReverseStreamAsync(PeerMessage request, ReverseStreamEntry streamEntry)
    {
        try
        {
            await streamEntry.ResponseStream.WriteAsync(request);
            streamEntry.LastActivityAt = DateTimeOffset.UtcNow;

            _eventBuffer.Add(RouterEvent.Create(
                RouterEventType.RelayForwarded,
                request.SenderPeerId,
                "",
                0,
                detail: new Dictionary<string, object?>
                {
                    ["recipient_peer_id"] = request.RecipientPeerId,
                    ["message_type"] = request.MessageType.ToString(),
                    ["payload_size"] = request.Payload.Length,
                    ["via"] = "reverse_stream"
                }));

            _logger.LogDebug(
                "Relayed message from {SenderPeerId} to {RecipientPeerId} via reverse stream",
                request.SenderPeerId, request.RecipientPeerId);

            return new MessageAck
            {
                Received = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to relay to {RecipientPeerId}: reverse stream write failed",
                request.RecipientPeerId);

            throw new RpcException(new Status(
                StatusCode.Unavailable,
                $"Could not relay to peer '{request.RecipientPeerId}' via reverse stream."));
        }
    }

    /// <summary>
    /// Forwards a message from a streaming peer to its intended recipient.
    /// </summary>
    private async Task ForwardStreamMessageAsync(PeerMessage message, string senderPeerId)
    {
        // Try reverse stream first
        if (_reverseStreamManager.TryGetStream(message.RecipientPeerId, out var recipientStream))
        {
            try
            {
                await recipientStream!.ResponseStream.WriteAsync(message);
                recipientStream.LastActivityAt = DateTimeOffset.UtcNow;

                _logger.LogDebug(
                    "Relayed message from {SenderPeerId} to {RecipientPeerId} via reverse stream",
                    senderPeerId, message.RecipientPeerId);

                _eventBuffer.Add(RouterEvent.Create(
                    RouterEventType.RelayForwarded,
                    senderPeerId,
                    "",
                    0,
                    detail: new Dictionary<string, object?>
                    {
                        ["recipient_peer_id"] = message.RecipientPeerId,
                        ["message_type"] = message.MessageType.ToString(),
                        ["payload_size"] = message.Payload.Length,
                        ["via"] = "reverse_stream"
                    }));

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to write to reverse stream for {RecipientPeerId}, trying direct channel",
                    message.RecipientPeerId);
            }
        }

        // Try direct channel via routing table
        var recipient = _routingTable.GetPeer(message.RecipientPeerId);
        if (recipient is not null && recipient.IsHealthy && !string.IsNullOrEmpty(recipient.Address))
        {
            try
            {
                var recipientAddress = $"http://{recipient.Address}:{recipient.Port}";
                var channel = GetOrCreateChannel(recipientAddress);
                var client = new PeerCommunication.PeerCommunicationClient(channel);

                await client.SendMessageAsync(message);

                _eventBuffer.Add(RouterEvent.Create(
                    RouterEventType.RelayForwarded,
                    senderPeerId,
                    recipient.IpAddress,
                    recipient.Port,
                    detail: new Dictionary<string, object?>
                    {
                        ["recipient_peer_id"] = message.RecipientPeerId,
                        ["message_type"] = message.MessageType.ToString(),
                        ["payload_size"] = message.Payload.Length,
                        ["via"] = "direct_channel"
                    }));

                _logger.LogDebug(
                    "Relayed message from {SenderPeerId} to {RecipientPeerId} via direct channel",
                    senderPeerId, message.RecipientPeerId);

                return;
            }
            catch (RpcException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to relay to {RecipientPeerId} via direct channel at {Address}:{Port}",
                    message.RecipientPeerId, recipient.Address, recipient.Port);
            }
        }

        _logger.LogWarning(
            "Failed to relay to {RecipientPeerId}: no active stream or address",
            message.RecipientPeerId);
    }

    /// <summary>
    /// Gets or creates a pooled gRPC channel for the given address.
    /// </summary>
    private GrpcChannel GetOrCreateChannel(string address) =>
        _channelPool.GetOrCreateChannel(address);
}
