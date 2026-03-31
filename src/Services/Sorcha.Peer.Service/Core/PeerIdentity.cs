// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Singleton that provides the canonical peer identity for this node.
/// All components must use <see cref="Id"/> instead of <c>Environment.MachineName</c>
/// or <c>NodeId ?? MachineName</c> to prevent peer identity fragmentation.
/// </summary>
public sealed class PeerIdentity
{
    /// <summary>
    /// The canonical peer ID for this node. Derived from <see cref="PeerServiceConfiguration.NodeId"/>
    /// if configured, otherwise from <see cref="Environment.MachineName"/>.
    /// </summary>
    public string Id { get; }

    public PeerIdentity(PeerServiceConfiguration configuration)
    {
        Id = configuration.NodeId ?? Environment.MachineName;
    }

    public override string ToString() => Id;
}
