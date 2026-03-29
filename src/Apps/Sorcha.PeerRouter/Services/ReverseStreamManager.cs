// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

using Grpc.Core;

using Sorcha.Peer.Service.Protos;
using Sorcha.PeerRouter.Models;

namespace Sorcha.PeerRouter.Services;

/// <summary>
/// Thread-safe manager for reverse gRPC streams from NAT'd peers.
/// Allows the router to push messages to peers that maintain a long-lived stream connection.
/// </summary>
public sealed class ReverseStreamManager
{
    private readonly ConcurrentDictionary<string, ReverseStreamEntry> _streams = new();
    private readonly ILogger<ReverseStreamManager> _logger;

    public ReverseStreamManager(ILogger<ReverseStreamManager> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Registers a reverse stream for a peer. Replaces any existing stream for the same peer.
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
                // Cancel the old stream's read loop so it stops processing
                try { existing.StreamCts.Cancel(); }
                catch (ObjectDisposedException) { }
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
    /// Returns the number of currently active reverse streams.
    /// </summary>
    public int GetActiveStreamCount() =>
        _streams.Values.Count(e => e.IsActive);

    /// <summary>
    /// Returns all active reverse stream entries (for diagnostics).
    /// </summary>
    public IReadOnlyList<ReverseStreamEntry> GetActiveStreams() =>
        _streams.Values.Where(e => e.IsActive).ToList();
}
