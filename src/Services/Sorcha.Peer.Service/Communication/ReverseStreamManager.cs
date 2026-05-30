// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Grpc.Core;
using Microsoft.Extensions.Logging;

using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Thread-safe registry of reverse gRPC streams held open by NAT'd peers that dialled this
/// rendezvous-capable node. Lets this node push brokered submit/sync messages to peers that
/// cannot accept inbound connections. Feature 143 (peer NAT traversal).
/// </summary>
/// <remarks>
/// Ported from <c>Sorcha.PeerRouter.Services.ReverseStreamManager</c> and extended with
/// <see cref="DispatchAsync"/> (broker a single message, fail-fast when no live stream) and
/// <see cref="ActiveCount"/> (metrics gauge source).
/// </remarks>
public sealed class ReverseStreamManager
{
    private readonly ConcurrentDictionary<string, ReverseStreamEntry> _streams = new();
    private readonly ILogger<ReverseStreamManager> _logger;

    /// <summary>Initializes a new instance of the <see cref="ReverseStreamManager"/> class.</summary>
    public ReverseStreamManager(ILogger<ReverseStreamManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a reverse stream for a peer. Replaces (and cancels) any existing stream for the
    /// same peer so a reconnect supersedes the stale stream.
    /// </summary>
    public void RegisterStream(string peerId, IServerStreamWriter<PeerMessage> stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(peerId);
        ArgumentNullException.ThrowIfNull(stream);

        var entry = new ReverseStreamEntry
        {
            PeerId = peerId,
            ResponseStream = stream
        };

        _streams.AddOrUpdate(
            peerId,
            entry,
            (_, existing) =>
            {
                existing.IsActive = false;
                try
                {
                    existing.StreamCts.Cancel();
                    existing.StreamCts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed — safe to ignore
                }

                _logger.LogInformation(
                    "Replacing existing reverse stream for peer {PeerId}", peerId);
                return entry;
            });

        _logger.LogInformation(
            "Reverse stream registered for peer {PeerId}", peerId);
    }

    /// <summary>
    /// Removes the reverse stream for a peer and marks it as inactive.
    /// </summary>
    public void RemoveStream(string peerId)
    {
        if (_streams.TryRemove(peerId, out var entry))
        {
            entry.IsActive = false;
            try
            {
                entry.StreamCts.Cancel();
                entry.StreamCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed — safe to ignore
            }

            _logger.LogInformation(
                "Reverse stream removed for peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// Tries to get the active reverse stream entry for a peer.
    /// </summary>
    public bool TryGetStream(string peerId, out ReverseStreamEntry? entry)
    {
        if (_streams.TryGetValue(peerId, out entry) && entry.IsActive)
        {
            return true;
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Brokers a single message to a NAT'd peer over its live reverse stream. Fails fast with
    /// <see cref="StatusCode.Unavailable"/> when the peer has no active stream (so callers fail
    /// over to another anchor rather than hang — Feature 143 FR-010).
    /// </summary>
    public async Task DispatchAsync(string peerId, PeerMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(peerId);
        ArgumentNullException.ThrowIfNull(message);

        if (!TryGetStream(peerId, out var entry))
        {
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                $"No active reverse stream for peer '{peerId}'."));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await entry!.ResponseStream.WriteAsync(message);
            entry.LastActivityAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Failed to dispatch {MessageType} to peer {PeerId} via reverse stream",
                message.MessageType, peerId);

            throw new RpcException(new Status(
                StatusCode.Unavailable,
                $"Failed to write to reverse stream for peer '{peerId}'."));
        }
    }

    /// <summary>
    /// Number of currently active reverse streams (metrics gauge source).
    /// </summary>
    public int ActiveCount => _streams.Values.Count(e => e.IsActive);

    /// <summary>
    /// Returns all active reverse stream entries (for diagnostics).
    /// </summary>
    public IReadOnlyList<ReverseStreamEntry> GetActiveStreams() =>
        _streams.Values.Where(e => e.IsActive).ToList();
}
