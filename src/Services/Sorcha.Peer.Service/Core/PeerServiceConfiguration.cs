// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Peer.Service.Core;

/// <summary>
/// Configuration for the Peer Service
/// </summary>
public class PeerServiceConfiguration
{
    /// <summary>
    /// Whether the peer service is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Unique node identifier (auto-generated if not specified)
    /// </summary>
    public string? NodeId { get; set; }

    /// <summary>
    /// Port to listen on for peer connections
    /// </summary>
    [Range(1, 65535)]
    public int ListenPort { get; set; } = 5001;

    /// <summary>
    /// Network address configuration
    /// </summary>
    public NetworkAddressConfiguration NetworkAddress { get; set; } = new();

    /// <summary>
    /// Peer discovery configuration
    /// </summary>
    public PeerDiscoveryConfiguration PeerDiscovery { get; set; } = new();

    /// <summary>
    /// Communication configuration
    /// </summary>
    public CommunicationConfiguration Communication { get; set; } = new();

    /// <summary>
    /// Transaction distribution configuration
    /// </summary>
    public TransactionDistributionConfiguration TransactionDistribution { get; set; } = new();

    /// <summary>
    /// Offline mode configuration
    /// </summary>
    public OfflineModeConfiguration OfflineMode { get; set; } = new();

    /// <summary>
    /// Seed node configuration for initial network bootstrap
    /// </summary>
    public SeedNodeConfiguration SeedNodes { get; set; } = new();

    /// <summary>
    /// Register synchronization configuration
    /// </summary>
    public RegisterSyncConfiguration RegisterSync { get; set; } = new();

    /// <summary>
    /// Periodic data cleanup configuration
    /// </summary>
    public DataCleanupConfiguration DataCleanup { get; set; } = new();

    /// <summary>
    /// Whether to enable mutual TLS (mTLS) for peer-to-peer gRPC connections (FR-016).
    /// When enabled, peers must present a valid client certificate to establish connections.
    /// Implementation deferred — this config option is scaffolding only.
    /// </summary>
    public bool EnableMtls { get; set; } = false;
}

public class NetworkAddressConfiguration
{
    /// <summary>
    /// Manually configured external address (overrides auto-detection)
    /// </summary>
    public string? ExternalAddress { get; set; }

    /// <summary>
    /// STUN servers for NAT traversal
    /// </summary>
    public List<string> StunServers { get; set; } = new()
    {
        "stun.l.google.com:19302"
    };

    /// <summary>
    /// HTTP-based address lookup services
    /// </summary>
    public List<string> HttpLookupServices { get; set; } = new()
    {
        "https://api.ipify.org"
    };

    /// <summary>
    /// Preferred IP protocol
    /// </summary>
    public string PreferredProtocol { get; set; } = "IPv4";
}

public class PeerDiscoveryConfiguration
{
    /// <summary>
    /// Bootstrap nodes to connect to initially
    /// </summary>
    public List<string> BootstrapNodes { get; set; } = new();

    /// <summary>
    /// How often to refresh the peer list (in minutes)
    /// </summary>
    [Range(1, 1440)]
    public int RefreshIntervalMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of peers to maintain in the list
    /// </summary>
    [Range(10, 10000)]
    public int MaxPeersInList { get; set; } = 1000;

    /// <summary>
    /// Minimum number of healthy peers required
    /// </summary>
    [Range(1, 100)]
    public int MinHealthyPeers { get; set; } = 5;

    /// <summary>
    /// Timeout for peer connection attempts (in seconds)
    /// </summary>
    [Range(5, 120)]
    public int PeerTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum concurrent peer discoveries
    /// </summary>
    [Range(1, 50)]
    public int MaxConcurrentDiscoveries { get; set; } = 10;
}

public class CommunicationConfiguration
{
    /// <summary>
    /// Preferred communication protocol
    /// </summary>
    public string PreferredProtocol { get; set; } = "GrpcStream";

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    [Range(5, 120)]
    public int ConnectionTimeout { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay between retries in seconds
    /// </summary>
    [Range(1, 60)]
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Circuit breaker failure threshold
    /// </summary>
    [Range(3, 20)]
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Circuit breaker reset interval in minutes
    /// </summary>
    [Range(1, 30)]
    public int CircuitBreakerResetMinutes { get; set; } = 5;
}

public class TransactionDistributionConfiguration
{
    /// <summary>
    /// Gossip protocol fanout factor (how many peers to notify)
    /// </summary>
    [Range(2, 20)]
    public int FanoutFactor { get; set; } = 3;

    /// <summary>
    /// Number of gossip rounds
    /// </summary>
    [Range(1, 10)]
    public int GossipRounds { get; set; } = 3;

    /// <summary>
    /// Transaction cache TTL in seconds
    /// </summary>
    [Range(300, 86400)]
    public int TransactionCacheTTL { get; set; } = 3600;

    /// <summary>
    /// Maximum transaction size in bytes
    /// </summary>
    public int MaxTransactionSize { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Size threshold for streaming (in bytes)
    /// </summary>
    public int StreamingThreshold { get; set; } = 1024 * 1024; // 1 MB

    /// <summary>
    /// Enable compression for transactions
    /// </summary>
    public bool EnableCompression { get; set; } = true;
}

public class OfflineModeConfiguration
{
    /// <summary>
    /// Maximum number of transactions to queue offline
    /// </summary>
    [Range(100, 100000)]
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>
    /// Maximum retry attempts for queued transactions
    /// </summary>
    [Range(1, 10)]
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Whether to persist queue to disk
    /// </summary>
    public bool QueuePersistence { get; set; } = true;

    /// <summary>
    /// Path for queue persistence database
    /// </summary>
    public string PersistencePath { get; set; } = "./data/tx_queue.db";
}
