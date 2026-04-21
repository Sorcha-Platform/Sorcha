// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Core.SyncState;

/// <summary>Tunables for <see cref="RegisterSyncStateResolver"/> (Feature 108).</summary>
public sealed class RegisterSyncStateOptions
{
    /// <summary>
    /// Peer observations older than this window are ignored when computing the sync state.
    /// Default: 60 seconds — matches the existing heartbeat interval tolerance.
    /// </summary>
    public TimeSpan StalenessWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Minimum distinct peers that must agree on the high-water-mark for <c>CaughtUp</c> to be
    /// reported with full confidence. Default: 2. Degrades gracefully to single-peer mode
    /// (state still <c>CaughtUp</c> but <c>SinglePeerMode == true</c>) when only one peer is known.
    /// </summary>
    public int CaughtUpQuorum { get; set; } = 2;
}
