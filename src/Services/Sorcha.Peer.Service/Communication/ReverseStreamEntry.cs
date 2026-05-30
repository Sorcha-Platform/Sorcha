// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Communication;

/// <summary>
/// Tracks an active bidirectional reverse stream from a NAT'd peer to this (rendezvous-capable)
/// node. The NAT'd peer dials out and holds the stream open; this node reuses it to push brokered
/// submit/sync requests back to the otherwise-unreachable peer. Feature 143 (peer NAT traversal).
/// </summary>
/// <remarks>
/// Ported from <c>Sorcha.PeerRouter.Models.ReverseStreamEntry</c> as part of folding the retired
/// PeerRouter rendezvous capability into the peer service.
/// </remarks>
public sealed class ReverseStreamEntry
{
    /// <summary>Peer identifier of the NAT'd peer that opened this stream (registry key).</summary>
    public required string PeerId { get; init; }

    /// <summary>gRPC stream writer used to push messages to this peer over its reverse stream.</summary>
    public required IServerStreamWriter<PeerMessage> ResponseStream { get; init; }

    /// <summary>Cancellation source signalling this stream's read loop to stop (e.g. on supersede).</summary>
    public CancellationTokenSource StreamCts { get; init; } = new();

    /// <summary>When the stream was established.</summary>
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last message sent or received on this stream (liveness).</summary>
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the stream is still open. Cleared when superseded by a reconnect.</summary>
    public bool IsActive { get; set; } = true;
}
