// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// Classification of network events captured by the Peer Router.
/// </summary>
public enum RouterEventType
{
    PeerConnected,
    PeerDisconnected,
    PeerHeartbeat,
    RegisterAdvertised,
    PeerListRequested,
    PeerExchanged,
    RelayForwarded,
    Error
}
