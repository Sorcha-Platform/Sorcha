# Sorcha Peer Service Design

## Overview

The Sorcha Peer Service is a distributed networking component that enables peer-to-peer communication and transaction distribution across a decentralized network of Sorcha nodes. This design document outlines the architecture, components, and implementation strategy for the Peer Service.

**Document Version:** 1.0.0
**Last Updated:** 2025-01-04
**Status:** Design Phase

## Table of Contents

- [Architecture Overview](#architecture-overview)
- [Core Components](#core-components)
- [Communication Protocols](#communication-protocols)
- [Peer Discovery](#peer-discovery)
- [Transaction Distribution](#transaction-distribution)
- [Network Address Discovery](#network-address-discovery)
- [Offline/Online Mode](#offlineonline-mode)
- [Configuration](#configuration)
- [Monitoring and Administration](#monitoring-and-administration)
- [Security Considerations](#security-considerations)
- [Testing Strategy](#testing-strategy)
- [Implementation Roadmap](#implementation-roadmap)

## Architecture Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    Sorcha Peer Service                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────────────────────────────────────────┐   │
│  │           Peer Discovery Service                       │   │
│  │  - Bootstrap Node Connection                           │   │
│  │  - Peer List Management                                │   │
│  │  - Periodic Refresh (15 min default)                   │   │
│  │  - Health Monitoring                                   │   │
│  └────────────────────────────────────────────────────────┘   │
│                          │                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │       Communication Manager                            │   │
│  │  - Protocol Negotiation (gRPC Stream → gRPC → REST)   │   │
│  │  - Connection Pool Management                          │   │
│  │  - Retry Logic with Exponential Backoff               │   │
│  │  - Circuit Breaker Pattern                             │   │
│  └────────────────────────────────────────────────────────┘   │
│                          │                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │      Transaction Distribution Service                  │   │
│  │  - Transaction Queue (online/offline)                  │   │
│  │  - Gossip Protocol Implementation                      │   │
│  │  - Duplicate Detection                                 │   │
│  │  - Bandwidth Optimization                              │   │
│  └────────────────────────────────────────────────────────┘   │
│                          │                                      │
│  ┌────────────────────────────────────────────────────────┐   │
│  │      Network Address Discovery Service                 │   │
│  │  - Internal IP Detection                               │   │
│  │  - NAT Traversal (STUN/TURN)                          │   │
│  │  - External Address Lookup                             │   │
│  │  - Configurable Override                               │   │
│  └────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
         │                      │                      │
         ▼                      ▼                      ▼
┌───────────────┐      ┌───────────────┐      ┌───────────────┐
│  Admin UI     │      │  Event Log    │      │  Metrics      │
│  Dashboard    │      │  Service      │      │  Collector    │
└───────────────┘      └───────────────┘      └───────────────┘
```

### Component Hierarchy

```
Sorcha.Peer.Service/
├── Core/
│   ├── PeerService.cs                    (Main background service)
│   ├── PeerNode.cs                       (Peer node representation)
│   └── PeerServiceConfiguration.cs       (Configuration model)
├── Discovery/
│   ├── IPeerDiscoveryService.cs          (Interface)
│   ├── PeerDiscoveryService.cs           (Implementation)
│   ├── BootstrapNodeProvider.cs          (Bootstrap node management)
│   └── PeerListManager.cs                (Peer list CRUD)
├── Communication/
│   ├── ICommunicationManager.cs          (Interface)
│   ├── CommunicationManager.cs           (Protocol negotiation)
│   ├── GrpcStreamClient.cs               (gRPC streaming)
│   ├── GrpcClient.cs                     (gRPC on-demand)
│   └── RestClient.cs                     (REST fallback)
├── Distribution/
│   ├── ITransactionDistributor.cs        (Interface)
│   ├── TransactionDistributor.cs         (Distribution logic)
│   ├── GossipProtocol.cs                 (Gossip implementation)
│   └── TransactionQueue.cs               (Offline queue)
├── Network/
│   ├── IAddressDiscoveryService.cs       (Interface)
│   ├── AddressDiscoveryService.cs        (Implementation)
│   ├── NatTraversalService.cs            (NAT detection/traversal)
│   └── ExternalAddressLookup.cs          (STUN/external lookup)
├── Monitoring/
│   ├── PeerMetricsCollector.cs           (Metrics collection)
│   └── PeerEventLogger.cs                (Event logging)
└── Protos/
    ├── peer_discovery.proto              (Peer discovery gRPC)
    ├── peer_communication.proto          (Peer-to-peer communication)
    └── transaction_distribution.proto    (Transaction sync)
```

## Core Components

### 1. Peer Service (Background Service)

**Purpose:** Main orchestrator for all peer-to-peer operations

**Responsibilities:**
- Initialize and coordinate all peer service components
- Manage background tasks and threading
- Report operational statistics
- Handle graceful startup and shutdown

**Implementation:**
```csharp
public class PeerService : BackgroundService
{
    private readonly IPeerDiscoveryService _discoveryService;
    private readonly ICommunicationManager _communicationManager;
    private readonly ITransactionDistributor _transactionDistributor;
    private readonly IAddressDiscoveryService _addressDiscoveryService;
    private readonly PeerMetricsCollector _metricsCollector;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Discover own external address
        // 2. Connect to bootstrap node
        // 3. Build peer list
        // 4. Start background tasks:
        //    - Peer discovery refresh
        //    - Transaction distribution
        //    - Health monitoring
        //    - Metrics collection
    }
}
```

### 2. Peer Discovery Service

**Purpose:** Discover and maintain list of active peers

**Key Features:**
- Bootstrap node connection
- Recursive peer discovery
- Periodic refresh (configurable, default 15 minutes)
- Peer health monitoring
- Event-driven updates

**State Machine:**
```
[Initial] → [Bootstrap] → [Discovery] → [Active] → [Refresh]
                ↓                                       ↓
            [Failed] ←──────────────────────────────────┘
                ↓
            [Retry] → [Bootstrap]
```

**Configuration:**
```json
{
  "PeerDiscovery": {
    "BootstrapNodes": [
      "https://peer.sorcha.dev:5001",
      "https://peer2.sorcha.dev:5001"
    ],
    "RefreshIntervalMinutes": 15,
    "MaxPeersInList": 1000,
    "MinHealthyPeers": 5,
    "PeerTimeoutSeconds": 30,
    "MaxConcurrentDiscoveries": 10
  }
}
```

### 3. Communication Manager

**Purpose:** Manage all peer-to-peer communication with protocol fallback

**Protocol Negotiation Flow:**
```
1. Attempt gRPC Streaming Connection
   ├─ Success → Use streaming for all subsequent communication
   └─ Failure → Continue to step 2

2. Attempt gRPC On-Demand Connection
   ├─ Success → Use gRPC request/response pattern
   └─ Failure → Continue to step 3

3. Attempt REST API Connection
   ├─ Success → Use HTTP/REST for communication
   └─ Failure → Mark peer as unreachable
```

**Connection Pooling:**
- Maintain pool of active connections per protocol
- Implement connection reuse
- Automatic cleanup of stale connections
- Circuit breaker pattern for failing peers

### 4. Transaction Distribution Service

**Purpose:** Efficiently distribute transactions across the peer network

**Distribution Algorithm: Gossip Protocol**

```
When receiving new transaction:
1. Store in local transaction pool
2. Select subset of peers (fanout = sqrt(total_peers))
3. Notify selected peers of transaction hash
4. Peers request full transaction if not already known
5. Peers repeat steps 2-4 with their own subset

Benefits:
- O(log N) message complexity
- Redundancy for reliability
- No single point of failure
- Minimal bandwidth usage
```

**Duplicate Detection:**
- Transaction hash-based deduplication
- Bloom filter for efficient lookup
- Configurable TTL for transaction cache

**Bandwidth Optimization:**
- Only send transaction hashes initially
- Full transaction sent on request
- Streaming for large transactions (>1MB)
- Compression (gzip) for all payloads

### 5. Network Address Discovery Service

**Purpose:** Determine peer's externally accessible address

**Discovery Process:**
```
1. Detect Internal IP
   - Query network interfaces
   - Select non-loopback, non-link-local address

2. Check Configuration Override
   - If ExternalAddress configured → Use it
   - If configured address invalid → Log warning, continue

3. NAT Detection
   - Check if internal IP is public (not RFC1918/RFC4193)
   - If public → Use as external address
   - If private → Continue to step 4

4. External Address Lookup
   - Query STUN server (e.g., stun.l.google.com:19302)
   - Parse reflexive address from STUN response
   - Validate address is reachable
   - If STUN fails → Query HTTP service (e.g., api.ipify.org)

5. Verify Address
   - Attempt connection to self through external address
   - If fails → Mark as "behind restrictive NAT"
```

**Configuration:**
```json
{
  "NetworkAddress": {
    "ExternalAddress": null,  // Optional override
    "StunServers": [
      "stun.l.google.com:19302",
      "stun1.l.google.com:19302"
    ],
    "HttpLookupServices": [
      "https://api.ipify.org",
      "https://ifconfig.me/ip"
    ],
    "PreferredProtocol": "IPv4"  // or "IPv6"
  }
}
```

## Communication Protocols

### gRPC Protocol Definitions

#### peer_discovery.proto
```protobuf
syntax = "proto3";

package sorcha.peer.discovery;

service PeerDiscovery {
  // Get list of known peers
  rpc GetPeerList(PeerListRequest) returns (PeerListResponse);

  // Register as a new peer
  rpc RegisterPeer(RegisterPeerRequest) returns (RegisterPeerResponse);

  // Health check
  rpc Ping(PingRequest) returns (PingResponse);
}

message PeerListRequest {
  string requesting_peer_id = 1;
  int32 max_peers = 2;
}

message PeerListResponse {
  repeated PeerInfo peers = 1;
  int32 total_peers = 2;
}

message PeerInfo {
  string peer_id = 1;
  string address = 2;
  int32 port = 3;
  repeated string supported_protocols = 4;
  int64 last_seen = 5;
  PeerCapabilities capabilities = 6;
}

message PeerCapabilities {
  bool supports_streaming = 1;
  bool supports_transaction_distribution = 2;
  int32 max_transaction_size = 3;
}

message RegisterPeerRequest {
  PeerInfo peer_info = 1;
}

message RegisterPeerResponse {
  bool success = 1;
  string message = 2;
}

message PingRequest {
  string peer_id = 1;
}

message PingResponse {
  string peer_id = 1;
  int64 timestamp = 2;
  PeerStatus status = 3;
}

enum PeerStatus {
  UNKNOWN = 0;
  ONLINE = 1;
  OFFLINE = 2;
  BUSY = 3;
}
```

#### transaction_distribution.proto
```protobuf
syntax = "proto3";

package sorcha.peer.transaction;

service TransactionDistribution {
  // Notify peer of new transaction (gossip)
  rpc NotifyTransaction(TransactionNotification) returns (NotificationAck);

  // Request full transaction
  rpc GetTransaction(TransactionRequest) returns (TransactionResponse);

  // Stream large transaction
  rpc StreamTransaction(TransactionRequest) returns (stream TransactionChunk);
}

message TransactionNotification {
  string transaction_hash = 1;
  string sender_peer_id = 2;
  int64 timestamp = 3;
  int64 transaction_size = 4;
}

message NotificationAck {
  bool already_known = 1;
  bool will_request = 2;
}

message TransactionRequest {
  string transaction_hash = 1;
  string requesting_peer_id = 2;
}

message TransactionResponse {
  string transaction_hash = 1;
  bytes transaction_data = 2;
  bool found = 3;
}

message TransactionChunk {
  string transaction_hash = 1;
  int32 chunk_index = 2;
  int32 total_chunks = 3;
  bytes chunk_data = 4;
}
```

### REST API Fallback Endpoints

```
POST   /api/v1/peers/register           Register peer
GET    /api/v1/peers/list                Get peer list
GET    /api/v1/peers/ping                Health check
POST   /api/v1/transactions/notify       Notify transaction
GET    /api/v1/transactions/{hash}       Get transaction
```

## Peer Discovery

### Bootstrap Process

**Phase 1: Initial Connection**
```
1. Load configuration (bootstrap nodes)
2. Attempt connection to each bootstrap node in order
3. On first successful connection:
   - Register self with bootstrap node
   - Request peer list (max 100 peers)
4. If all bootstrap nodes fail:
   - Log error event
   - Enter offline mode
   - Retry after configured interval (default: 5 minutes)
```

**Phase 2: Peer List Building**
```
1. Receive initial peer list from bootstrap node
2. Validate each peer (check address format, reachability)
3. For each valid peer (up to MaxConcurrentDiscoveries):
   - Connect to peer
   - Request their peer list
   - Merge new peers into local list (deduplicate)
4. Continue until:
   - MaxPeersInList reached OR
   - No new peers discovered in last round
```

**Phase 3: Active Monitoring**
```
Every RefreshIntervalMinutes (default: 15):
1. Iterate through peer list
2. Ping each peer (timeout: PeerTimeoutSeconds)
3. If peer responds:
   - Update last_seen timestamp
   - Request peer list for new peers
4. If peer doesn't respond:
   - Increment failure counter
   - If failures > threshold (default: 3):
     - Remove from peer list
     - Log event
5. Calculate network health:
   - Active peers / Total peers
   - If < MinHealthyPeers:
     - Trigger bootstrap re-discovery
```

### Peer List Persistence

**Storage:** SQLite local database or file-based cache

**Schema:**
```sql
CREATE TABLE peers (
  peer_id TEXT PRIMARY KEY,
  address TEXT NOT NULL,
  port INTEGER NOT NULL,
  supported_protocols TEXT,  -- JSON array
  first_seen INTEGER NOT NULL,
  last_seen INTEGER NOT NULL,
  failure_count INTEGER DEFAULT 0,
  capabilities TEXT,  -- JSON object
  is_bootstrap BOOLEAN DEFAULT 0
);

CREATE INDEX idx_last_seen ON peers(last_seen);
CREATE INDEX idx_is_bootstrap ON peers(is_bootstrap);
```

## Transaction Distribution

### Gossip Protocol Implementation

**Parameters:**
```json
{
  "GossipProtocol": {
    "FanoutFactor": 3,           // sqrt(total_peers) as default
    "GossipRounds": 3,            // How many rounds before stopping
    "NotificationTimeout": 5,     // Seconds
    "TransactionCacheTTL": 3600,  // 1 hour
    "MaxTransactionSize": 10485760, // 10 MB
    "StreamingThreshold": 1048576,  // 1 MB
    "EnableCompression": true
  }
}
```

**Algorithm:**
```python
def distribute_transaction(tx_hash, tx_data):
    # 1. Store locally
    local_tx_store.add(tx_hash, tx_data)

    # 2. Select random peers (fanout)
    peers = select_random_peers(fanout_factor)

    # 3. Notify peers (parallel)
    for peer in peers:
        notify_async(peer, tx_hash, tx_data.size)

    # Peers repeat this process with their own fanout

def on_receive_notification(tx_hash, size, sender_peer):
    # 1. Check if already known
    if local_tx_store.contains(tx_hash):
        return NotificationAck(already_known=True)

    # 2. Check bloom filter (probabilistic)
    if bloom_filter.probably_contains(tx_hash):
        return NotificationAck(already_known=True)

    # 3. Request transaction
    if size > streaming_threshold:
        request_transaction_stream(sender_peer, tx_hash)
    else:
        request_transaction(sender_peer, tx_hash)

    return NotificationAck(will_request=True)

def on_receive_transaction(tx_hash, tx_data):
    # 1. Store locally
    local_tx_store.add(tx_hash, tx_data)

    # 2. Add to bloom filter
    bloom_filter.add(tx_hash)

    # 3. Continue gossip (if rounds remaining)
    if current_round < gossip_rounds:
        distribute_transaction(tx_hash, tx_data)
```

### Duplicate Detection

**Bloom Filter:**
- Size: 10,000,000 bits (~1.2 MB)
- Hash functions: 3
- Expected false positive rate: < 0.1%
- Reset interval: 1 hour

**Transaction Hash:**
```csharp
string ComputeTransactionHash(Transaction tx)
{
    using var sha256 = SHA256.Create();
    var json = JsonSerializer.Serialize(tx);
    var bytes = Encoding.UTF8.GetBytes(json);
    var hash = sha256.ComputeHash(bytes);
    return Convert.ToBase64String(hash);
}
```

## Network Address Discovery

### Implementation Details

**STUN Protocol:**
```csharp
public async Task<IPEndPoint?> DiscoverExternalAddress()
{
    var stunClient = new StunClient();

    foreach (var stunServer in _config.StunServers)
    {
        try
        {
            var result = await stunClient.QueryAsync(stunServer, timeout: 5);

            if (result.Success && result.MappedAddress != null)
            {
                _logger.LogInformation(
                    "Discovered external address via STUN: {Address}",
                    result.MappedAddress);

                return result.MappedAddress;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "STUN query failed for server {Server}", stunServer);
        }
    }

    return null;
}
```

**HTTP Lookup Fallback:**
```csharp
public async Task<IPAddress?> LookupViaHttp()
{
    foreach (var lookupService in _config.HttpLookupServices)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(lookupService);

            if (IPAddress.TryParse(response.Trim(), out var address))
            {
                _logger.LogInformation(
                    "Discovered external address via HTTP: {Address}",
                    address);

                return address;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "HTTP lookup failed for service {Service}", lookupService);
        }
    }

    return null;
}
```

## Offline/Online Mode

### State Management

**States:**
- `Online`: Connected to at least MinHealthyPeers
- `Offline`: No peer connections available
- `Degraded`: Some peers available but below threshold

**State Transitions:**
```
[Offline] ──(peers discovered)──> [Online]
[Online] ──(all peers lost)──> [Offline]
[Online] ──(peers < threshold)──> [Degraded]
[Degraded] ──(peers recovered)──> [Online]
[Degraded] ──(all peers lost)──> [Offline]
```

### Transaction Queuing

**Offline Queue:**
```csharp
public class TransactionQueue
{
    private readonly ConcurrentQueue<QueuedTransaction> _queue;
    private readonly SemaphoreSlim _semaphore;

    public async Task EnqueueAsync(Transaction tx)
    {
        var queued = new QueuedTransaction
        {
            Transaction = tx,
            QueuedAt = DateTimeOffset.UtcNow,
            RetryCount = 0
        };

        _queue.Enqueue(queued);

        // Persist to disk
        await PersistQueue();
    }

    public async Task FlushAsync()
    {
        while (_queue.TryDequeue(out var queued))
        {
            try
            {
                await _distributor.DistributeAsync(queued.Transaction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to flush transaction {TxHash}",
                    queued.Transaction.Hash);

                // Re-queue with retry count
                queued.RetryCount++;
                if (queued.RetryCount < MaxRetries)
                {
                    _queue.Enqueue(queued);
                }
            }
        }
    }
}
```

**Persistence:**
- Store queue to disk (SQLite or file)
- Load on service startup
- Automatic flush when transitioning to Online state

## Configuration

### appsettings.json Schema

```json
{
  "PeerService": {
    "Enabled": true,
    "NodeId": null,  // Auto-generated if null
    "ListenPort": 5001,

    "NetworkAddress": {
      "ExternalAddress": null,
      "StunServers": [
        "stun.l.google.com:19302"
      ],
      "HttpLookupServices": [
        "https://api.ipify.org"
      ]
    },

    "PeerDiscovery": {
      "BootstrapNodes": [
        "https://peer.sorcha.dev:5001"
      ],
      "RefreshIntervalMinutes": 15,
      "MaxPeersInList": 1000,
      "MinHealthyPeers": 5,
      "PeerTimeoutSeconds": 30,
      "MaxConcurrentDiscoveries": 10
    },

    "Communication": {
      "PreferredProtocol": "GrpcStream",
      "ConnectionTimeout": 30,
      "MaxRetries": 3,
      "RetryDelaySeconds": 5,
      "CircuitBreakerThreshold": 5,
      "CircuitBreakerResetMinutes": 5
    },

    "TransactionDistribution": {
      "FanoutFactor": 3,
      "GossipRounds": 3,
      "TransactionCacheTTL": 3600,
      "MaxTransactionSize": 10485760,
      "StreamingThreshold": 1048576,
      "EnableCompression": true
    },

    "OfflineMode": {
      "MaxQueueSize": 10000,
      "MaxRetries": 5,
      "QueuePersistence": true,
      "PersistencePath": "./data/tx_queue.db"
    }
  }
}
```

## Monitoring and Administration

### Metrics

**Operational Metrics:**
- `peer_count` - Current number of known peers
- `peer_count_active` - Number of responsive peers
- `transaction_distribution_latency` - Time to distribute transaction
- `transaction_queue_size` - Number of queued transactions
- `bandwidth_sent_bytes` - Total bytes sent
- `bandwidth_received_bytes` - Total bytes received
- `connection_errors_total` - Total connection failures
- `gossip_rounds_total` - Total gossip rounds completed

**Health Metrics:**
- `peer_service_status` - Online/Offline/Degraded
- `last_successful_discovery` - Timestamp
- `network_partition_detected` - Boolean

### Admin UI Dashboard

**Peer Status Page:** `/admin/peers`

**Displays:**
- Network status (Online/Offline/Degraded)
- Connected peers count / Total known peers
- External address (current)
- Last discovery run timestamp
- Transaction queue size
- Bandwidth usage (real-time chart)

**Peer List Table:**
| Peer ID | Address | Last Seen | Latency | Status | Actions |
|---------|---------|-----------|---------|--------|---------|
| abc123  | 1.2.3.4 | 2m ago    | 45ms    | Online | Ping    |
| def456  | 5.6.7.8 | 1h ago    | 120ms   | Slow   | Remove  |

**Event Log:**
- Peer connected events
- Peer disconnected events
- Transaction distribution events
- Discovery round completions
- Error events

## Security Considerations

### Authentication

**Peer Authentication:**
- Mutual TLS (mTLS) for gRPC connections
- Certificate-based peer identity
- Revocation list for banned peers

**Configuration:**
```json
{
  "Security": {
    "RequireMutualTLS": true,
    "CertificatePath": "./certs/peer.pfx",
    "CertificatePassword": "${PEER_CERT_PASSWORD}",
    "TrustedCAs": "./certs/ca.crt"
  }
}
```

### Authorization

**Peer Capabilities:**
- Read-only peers (can request but not distribute)
- Full peers (can distribute transactions)
- Bootstrap peers (can accept registrations)

### Rate Limiting

**Per-Peer Limits:**
- Max requests per minute: 1000
- Max bandwidth per minute: 100 MB
- Max transactions per minute: 100

### DDoS Protection

**Strategies:**
- Connection rate limiting
- Request size limits
- Peer reputation scoring
- Automatic peer banning (temporary)

## Testing Strategy

### Unit Tests

**Test Projects:**
- `Sorcha.Peer.Service.Tests`
- `Sorcha.Peer.Discovery.Tests`
- `Sorcha.Peer.Communication.Tests`
- `Sorcha.Peer.Distribution.Tests`

**Test Coverage:**
- Peer discovery algorithm
- Protocol negotiation logic
- Gossip protocol implementation
- Transaction queuing
- Address discovery
- Bloom filter operations
- State machine transitions

### Integration Tests

**Scenarios:**
- Multi-peer network simulation (3-10 peers)
- Bootstrap node failure recovery
- Transaction distribution across network
- Peer disconnection/reconnection
- Offline mode and queue flush
- Protocol fallback (gRPC → REST)

**Test Infrastructure:**
- Docker Compose for multi-peer setup
- Testcontainers for isolated testing
- Chaos engineering (random peer failures)

### Performance Tests

**Benchmarks:**
- Transaction distribution latency (target: <500ms to 90% of network)
- Bandwidth efficiency (target: <10 KB per transaction notification)
- Peer discovery time (target: <30s for 100 peers)
- Queue flush performance (target: >1000 tx/sec)

## Implementation Roadmap

### Phase 1: Foundation (Sprint 1-2)

**Week 1-2:**
- [ ] Create Sorcha.Peer.Service project
- [ ] Define gRPC proto files
- [ ] Implement PeerNode model
- [ ] Implement configuration system
- [ ] Create unit test projects

**Week 3-4:**
- [ ] Implement AddressDiscoveryService
  - Internal IP detection
  - STUN client
  - HTTP lookup fallback
- [ ] Unit tests for address discovery
- [ ] Integration tests with real STUN servers

### Phase 2: Peer Discovery (Sprint 3-4)

**Week 5-6:**
- [ ] Implement PeerDiscoveryService
  - Bootstrap node connection
  - Peer list management
  - Periodic refresh
- [ ] Implement PeerListManager with persistence
- [ ] Unit tests for discovery logic

**Week 7-8:**
- [ ] Implement health monitoring
- [ ] Implement peer event logging
- [ ] Integration tests with multiple peers
- [ ] Docker Compose test environment

### Phase 3: Communication Layer (Sprint 5-6)

**Week 9-10:**
- [ ] Implement CommunicationManager
  - Protocol negotiation
  - Connection pooling
  - Circuit breaker
- [ ] Implement GrpcStreamClient
- [ ] Implement GrpcClient
- [ ] Implement RestClient

**Week 11-12:**
- [ ] Unit tests for communication layer
- [ ] Integration tests for protocol fallback
- [ ] Performance benchmarks

### Phase 4: Transaction Distribution (Sprint 7-8)

**Week 13-14:**
- [ ] Implement TransactionDistributor
- [ ] Implement GossipProtocol
- [ ] Implement bloom filter for deduplication
- [ ] Implement TransactionQueue

**Week 15-16:**
- [ ] Implement offline/online mode
- [ ] Implement queue persistence
- [ ] Unit tests for distribution
- [ ] Integration tests for gossip protocol

### Phase 5: Admin UI (Sprint 9)

**Week 17-18:**
- [ ] Create Blazor admin components
- [ ] Implement peer status dashboard
- [ ] Implement real-time metrics
- [ ] Implement event log viewer

### Phase 6: Hardening & Documentation (Sprint 10)

**Week 19-20:**
- [ ] Security audit
- [ ] Performance optimization
- [ ] Load testing
- [ ] Complete documentation
- [ ] Deployment guides

## Success Criteria

**Functional Requirements:**
- ✅ Peer discovers external address automatically
- ✅ Bootstrap connection succeeds within 10 seconds
- ✅ Peer list builds to >50 peers within 2 minutes
- ✅ Transaction distributes to 90% of network within 1 minute
- ✅ Offline mode queues transactions correctly
- ✅ Queue flushes successfully when back online
- ✅ Admin dashboard displays real-time metrics

**Non-Functional Requirements:**
- ✅ Transaction distribution latency < 500ms (p95)
- ✅ Bandwidth usage < 10 KB per transaction notification
- ✅ Service uptime > 99.9%
- ✅ Memory usage < 500 MB for 1000 peers
- ✅ Code coverage > 80%

## References

- [Gossip Protocol (Wikipedia)](https://en.wikipedia.org/wiki/Gossip_protocol)
- [STUN RFC 5389](https://tools.ietf.org/html/rfc5389)
- [gRPC Best Practices](https://grpc.io/docs/guides/performance/)
- [Bloom Filter (Wikipedia)](https://en.wikipedia.org/wiki/Bloom_filter)
- [Circuit Breaker Pattern](https://martinfowler.com/bliki/CircuitBreaker.html)

---

**Document Status:** ✅ Ready for Review
**Next Steps:** Review with team → Approval → Begin Phase 1 implementation
