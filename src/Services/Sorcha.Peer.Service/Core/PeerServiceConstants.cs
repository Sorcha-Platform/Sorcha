// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Constants for the peer service P2P network
/// </summary>
public static class PeerServiceConstants
{
    /// <summary>
    /// Well-known system register identifier (00000000-0000-0000-0000-000000000000)
    /// </summary>
    public static readonly Guid SystemRegisterId = Guid.Empty;

    /// <summary>
    /// Heartbeat interval in seconds
    /// </summary>
    public const int HeartbeatIntervalSeconds = 30;

    /// <summary>
    /// Heartbeat timeout in seconds
    /// </summary>
    public const int HeartbeatTimeoutSeconds = 30;

    /// <summary>
    /// Maximum missed heartbeats before marking peer as timed out (2 = 60 seconds total)
    /// </summary>
    public const int MaxMissedHeartbeats = 2;

    /// <summary>
    /// Periodic sync interval in minutes
    /// </summary>
    public const int PeriodicSyncIntervalMinutes = 5;

    /// <summary>
    /// Connection retry initial delay in seconds
    /// </summary>
    public const int RetryInitialDelaySeconds = 1;

    /// <summary>
    /// Connection retry multiplier (exponential backoff)
    /// </summary>
    public const double RetryMultiplier = 2.0;

    /// <summary>
    /// Connection retry max delay in seconds (cap at 60s)
    /// </summary>
    public const int RetryMaxDelaySeconds = 60;

    /// <summary>
    /// Maximum retry attempts before trying next peer
    /// </summary>
    public const int MaxRetryAttempts = 10;

    /// <summary>
    /// Connection timeout per attempt in seconds
    /// </summary>
    public const int ConnectionTimeoutSeconds = 30;

    /// <summary>
    /// Push notification delivery target (80% of subscribed peers within 30s)
    /// </summary>
    public const double PushNotificationTargetPercent = 0.80;

    /// <summary>
    /// Push notification delivery timeout in seconds
    /// </summary>
    public const int PushNotificationTimeoutSeconds = 30;

    /// <summary>
    /// Maximum number of peers to return in a peer exchange response
    /// </summary>
    public const int MaxPeersInExchangeResponse = 50;

    /// <summary>
    /// Peer exchange interval in minutes (gossip-style peer list exchange)
    /// </summary>
    public const int PeerExchangeIntervalMinutes = 10;

    /// <summary>
    /// Maximum number of register subscriptions per peer
    /// </summary>
    public const int MaxRegisterSubscriptionsPerPeer = 100;

    /// <summary>
    /// Maximum consecutive failures before disconnecting a non-seed peer
    /// </summary>
    public const int MaxConsecutiveFailuresBeforeDisconnect = 5;

    /// <summary>
    /// Maximum consecutive failures before transitioning subscription to Error state
    /// </summary>
    public const int MaxConsecutiveFailuresBeforeError = 10;

    /// <summary>
    /// Number of peers to select for gossip exchange
    /// </summary>
    public const int GossipExchangePeerCount = 3;

    /// <summary>
    /// Expected retry backoff sequence (in seconds): 1, 2, 4, 8, 16, 32, 60, 60, 60, 60
    /// </summary>
    public static readonly TimeSpan[] RetryBackoffSequence =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(32),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(60)
    ];
}
