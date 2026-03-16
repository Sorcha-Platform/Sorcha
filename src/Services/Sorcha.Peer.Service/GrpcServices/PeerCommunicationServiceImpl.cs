// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for handling incoming peer-to-peer messages.
/// Receives messages forwarded by the seed node (PeerRouter) and dispatches
/// relay message types to RelayMessageHandler.
/// </summary>
public class PeerCommunicationServiceImpl : PeerCommunication.PeerCommunicationBase
{
    private readonly ILogger<PeerCommunicationServiceImpl> _logger;
    private readonly RelayMessageHandler _relayMessageHandler;

    public PeerCommunicationServiceImpl(
        ILogger<PeerCommunicationServiceImpl> logger,
        RelayMessageHandler relayMessageHandler)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _relayMessageHandler = relayMessageHandler ?? throw new ArgumentNullException(nameof(relayMessageHandler));
    }

    /// <summary>
    /// Handles an incoming PeerMessage by dispatching to the RelayMessageHandler.
    /// </summary>
    public override async Task<MessageAck> SendMessage(PeerMessage request, ServerCallContext context)
    {
        _logger.LogDebug("Received {MessageType} from {SenderPeerId}",
            request.MessageType, request.SenderPeerId);

        try
        {
            await _relayMessageHandler.HandleAsync(request, context.CancellationToken);

            return new MessageAck
            {
                Received = true,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling {MessageType} from {SenderPeerId}",
                request.MessageType, request.SenderPeerId);

            return new MessageAck
            {
                Received = false,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
    }
}
