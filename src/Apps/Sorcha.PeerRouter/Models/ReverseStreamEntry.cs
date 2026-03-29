// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Grpc.Core;

using Sorcha.Peer.Service.Protos;

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// Tracks an active bidirectional reverse stream from a NAT'd peer to the Router.
/// </summary>
public sealed class ReverseStreamEntry
{
    /// <summary>Peer identifier (key).</summary>
    public required string PeerId { get; init; }

    /// <summary>gRPC stream writer for pushing messages to this peer.</summary>
    public required IServerStreamWriter<PeerMessage> ResponseStream { get; init; }

    /// <summary>Cancellation token source to signal the stream's read loop to stop.</summary>
    public CancellationTokenSource StreamCts { get; init; } = new();

    /// <summary>When the stream was established.</summary>
    public DateTimeOffset ConnectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last message sent or received on this stream.</summary>
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Whether the stream is still open.</summary>
    public bool IsActive { get; set; } = true;
}
