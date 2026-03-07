// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Grpc.Net.Client;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;
using Sorcha.PeerRouter.Services;

namespace Sorcha.PeerRouter.GrpcServices;

/// <summary>
/// gRPC implementation of PeerCommunication for optional relay mode.
/// When relay is enabled, forwards messages between peers that cannot reach each other directly.
/// </summary>
public sealed class RouterCommunicationService : PeerCommunication.PeerCommunicationBase
{
    private readonly RoutingTable _routingTable;
    private readonly EventBuffer _eventBuffer;
    private readonly RouterConfiguration _config;
    private readonly ILogger<RouterCommunicationService> _logger;

    public RouterCommunicationService(
        RoutingTable routingTable,
        EventBuffer eventBuffer,
        RouterConfiguration config,
        ILogger<RouterCommunicationService> logger)
    {
        _routingTable = routingTable;
        _eventBuffer = eventBuffer;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Receives a message from a sender peer and forwards it to the recipient peer.
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
            _logger.LogWarning(
                "Relay failed: recipient peer {RecipientId} is not registered or unhealthy",
                request.RecipientPeerId);

            throw new RpcException(new Status(
                StatusCode.NotFound,
                $"Recipient peer '{request.RecipientPeerId}' is not registered or is unhealthy."));
        }

        try
        {
            var recipientAddress = $"http://{recipient.Address}:{recipient.Port}";
            using var channel = GrpcChannel.ForAddress(recipientAddress);
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
    /// Bidirectional streaming is not supported in relay mode.
    /// </summary>
    public override Task Stream(
        IAsyncStreamReader<PeerMessage> requestStream,
        IServerStreamWriter<PeerMessage> responseStream,
        ServerCallContext context)
    {
        throw new RpcException(new Status(
            StatusCode.Unimplemented,
            "Bidirectional streaming is not supported in relay mode. Use SendMessage for relay forwarding."));
    }
}
