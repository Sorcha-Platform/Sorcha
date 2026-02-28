// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using Sorcha.Peer.Service.Core;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Client for bidirectional streaming communication with peers
/// </summary>
public class StreamingCommunicationClient : IDisposable, IAsyncDisposable
{
    private readonly ILogger<StreamingCommunicationClient> _logger;
    private readonly string _peerId;
    private readonly string _address;
    private GrpcChannel? _channel;
    private AsyncDuplexStreamingCall<PeerMessage, PeerMessage>? _streamingCall;
    private bool _disposed;

    public StreamingCommunicationClient(
        ILogger<StreamingCommunicationClient> logger,
        string peerId,
        string address)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerId = peerId ?? throw new ArgumentNullException(nameof(peerId));
        _address = address ?? throw new ArgumentNullException(nameof(address));
    }

    /// <summary>
    /// Establishes a streaming connection to the peer
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Establishing streaming connection to {PeerId} at {Address}", _peerId, _address);

            _channel = GrpcChannel.ForAddress(_address);
            var client = new PeerCommunication.PeerCommunicationClient(_channel);

            _streamingCall = client.Stream();

            _logger.LogInformation("Streaming connection established to {PeerId}", _peerId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to establish streaming connection to {PeerId}", _peerId);
            return false;
        }
    }

    /// <summary>
    /// Sends a message through the stream
    /// </summary>
    public async Task<bool> SendMessageAsync(PeerMessage message, CancellationToken cancellationToken = default)
    {
        if (_streamingCall == null)
        {
            _logger.LogWarning("Cannot send message - stream not connected");
            return false;
        }

        try
        {
            await _streamingCall.RequestStream.WriteAsync(message, cancellationToken);
            _logger.LogTrace("Sent message to {PeerId}: {Type}", _peerId, message.MessageType);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to {PeerId}", _peerId);
            return false;
        }
    }

    /// <summary>
    /// Receives messages from the stream
    /// </summary>
    public async Task<PeerMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
    {
        if (_streamingCall == null)
        {
            _logger.LogWarning("Cannot receive message - stream not connected");
            return null;
        }

        try
        {
            if (await _streamingCall.ResponseStream.MoveNext(cancellationToken))
            {
                var message = _streamingCall.ResponseStream.Current;
                _logger.LogTrace("Received message from {PeerId}: {Type}", _peerId, message.MessageType);
                return message;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving message from {PeerId}", _peerId);
            return null;
        }
    }

    /// <summary>
    /// Starts a background receive loop
    /// </summary>
    public async Task StartReceiveLoopAsync(
        Func<PeerMessage, Task> messageHandler,
        CancellationToken cancellationToken = default)
    {
        if (_streamingCall == null)
        {
            _logger.LogWarning("Cannot start receive loop - stream not connected");
            return;
        }

        _logger.LogInformation("Starting receive loop for {PeerId}", _peerId);

        try
        {
            await foreach (var message in _streamingCall.ResponseStream.ReadAllAsync(cancellationToken))
            {
                try
                {
                    await messageHandler(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling message from {PeerId}", _peerId);
                }
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
        {
            _logger.LogInformation("Receive loop cancelled for {PeerId}", _peerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in receive loop for {PeerId}", _peerId);
        }

        _logger.LogInformation("Receive loop stopped for {PeerId}", _peerId);
    }

    /// <summary>
    /// Closes the streaming connection
    /// </summary>
    public async Task CloseAsync()
    {
        if (_streamingCall != null)
        {
            await _streamingCall.RequestStream.CompleteAsync();
            _streamingCall.Dispose();
            _streamingCall = null;
        }

        _channel?.Dispose();
        _channel = null;

        _logger.LogInformation("Closed streaming connection to {PeerId}", _peerId);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await CloseAsync();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _streamingCall?.Dispose();
            _streamingCall = null;
            _channel?.Dispose();
            _channel = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
