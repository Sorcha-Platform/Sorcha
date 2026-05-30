// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Peer.Service.Communication;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Observability;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.GrpcServices;

/// <summary>
/// gRPC service implementation for handling incoming peer-to-peer messages.
/// Dispatches unary relay messages and — when this node is rendezvous-capable (Feature 143) —
/// accepts reverse duplex streams from NAT'd peers and brokers traffic to them.
/// </summary>
public class PeerCommunicationServiceImpl : PeerCommunication.PeerCommunicationBase
{
    private readonly ILogger<PeerCommunicationServiceImpl> _logger;
    private readonly RelayMessageHandler _relayMessageHandler;
    private readonly ReverseStreamManager _reverseStreams;
    private readonly PeerServiceMetrics _metrics;
    private readonly PeerServiceConfiguration _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerCommunicationServiceImpl"/> class.
    /// </summary>
    public PeerCommunicationServiceImpl(
        ILogger<PeerCommunicationServiceImpl> logger,
        RelayMessageHandler relayMessageHandler,
        ReverseStreamManager reverseStreams,
        PeerServiceMetrics metrics,
        IOptions<PeerServiceConfiguration> config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _relayMessageHandler = relayMessageHandler ?? throw new ArgumentNullException(nameof(relayMessageHandler));
        _reverseStreams = reverseStreams ?? throw new ArgumentNullException(nameof(reverseStreams));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
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

    /// <summary>
    /// Bidirectional reverse stream for NAT'd peers (Feature 143). A NAT'd peer dials out and holds
    /// this stream open; this rendezvous-capable node registers it so it can push brokered submit/sync
    /// requests back to the otherwise-unreachable peer, and pumps inbound messages (the peer's own
    /// requests, sync responses, notifications) through the same <see cref="RelayMessageHandler"/>
    /// path as <see cref="SendMessage"/>.
    /// </summary>
    public override async Task Stream(
        IAsyncStreamReader<PeerMessage> requestStream,
        IServerStreamWriter<PeerMessage> responseStream,
        ServerCallContext context)
    {
        if (!_config.IsRendezvousCapable)
        {
            _logger.LogWarning(
                "Reverse stream rejected: this node is not rendezvous-capable (no external address configured).");

            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                "This node is not rendezvous-capable. Configure an external/public address to accept reverse streams."));
        }

        string? peerId = null;

        try
        {
            await foreach (var message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                // The first message identifies the NAT'd peer and registers its reverse stream.
                if (peerId is null)
                {
                    if (string.IsNullOrEmpty(message.SenderPeerId))
                    {
                        throw new RpcException(new Status(
                            StatusCode.InvalidArgument,
                            "First message on a reverse stream must include sender_peer_id."));
                    }

                    peerId = message.SenderPeerId;
                    _reverseStreams.RegisterStream(peerId, responseStream);
                    _metrics.SetReverseStreamsActive(_reverseStreams.ActiveCount);

                    _logger.LogInformation(
                        "Reverse stream established for NAT'd peer {PeerId}", peerId);
                }

                if (_reverseStreams.TryGetStream(peerId, out var entry))
                {
                    entry!.LastActivityAt = DateTimeOffset.UtcNow;
                }

                // Pump the inbound message through the same relay dispatch path as SendMessage.
                // Failures handling one message must not tear down the whole stream.
                try
                {
                    await _relayMessageHandler.HandleAsync(message, context.CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Error handling {MessageType} from reverse-stream peer {PeerId}",
                        message.MessageType, peerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected / stream cancelled — normal teardown.
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            // Client cancelled the stream.
        }
        finally
        {
            if (peerId is not null)
            {
                _reverseStreams.RemoveStream(peerId);
                _metrics.SetReverseStreamsActive(_reverseStreams.ActiveCount);

                _logger.LogInformation(
                    "Reverse stream closed for NAT'd peer {PeerId}", peerId);
            }
        }
    }
}
