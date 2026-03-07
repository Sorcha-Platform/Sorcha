# Data Model: Peer Router App & Peer Service Completion

**Feature**: 053-peer-router-completion
**Date**: 2026-03-07

## Entities

### RouterEvent

Represents a network event captured by the Peer Router for debugging and monitoring.

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique event ID (ULID for time-ordered uniqueness) |
| Type | RouterEventType | Event classification |
| Timestamp | DateTimeOffset | When the event occurred (UTC) |
| PeerId | string | Peer that triggered the event |
| NodeName | string? | Human-readable node name (from RegisterPeerRequest, nullable) |
| IpAddress | string | IP address of the peer |
| Port | int | Port of the peer |
| Detail | object | Type-specific payload (serialized as JSON) |

**RouterEventType enum**: PeerConnected, PeerDisconnected, PeerHeartbeat, RegisterAdvertised, PeerListRequested, PeerExchanged, RelayForwarded, Error

### RoutingEntry

Represents a peer in the router's in-memory routing table.

| Field | Type | Description |
|-------|------|-------------|
| PeerId | string | Unique peer identifier |
| NodeName | string? | Human-readable node name |
| Address | string | gRPC address (host:port) |
| IpAddress | string | Resolved IP address |
| Port | int | gRPC port |
| Capabilities | PeerCapabilities | Peer's declared capabilities |
| AdvertisedRegisters | List | Registers this peer holds |
| FirstSeen | DateTimeOffset | When peer first registered |
| LastSeen | DateTimeOffset | Last communication timestamp |
| IsHealthy | bool | Whether peer is within timeout window |
| HeartbeatCount | long | Total heartbeats received |

### RoutingTable

In-memory concurrent dictionary keyed by PeerId, holding RoutingEntry values.

| Operation | Behavior |
|-----------|----------|
| RegisterPeer | Upsert entry, emit PeerConnected if new |
| Ping | Update LastSeen, emit PeerHeartbeat |
| Timeout check | Mark IsHealthy=false when LastSeen > timeout, emit PeerDisconnected |
| GetPeerList | Return only healthy entries |
| FindPeersForRegister | Filter by AdvertisedRegisters |

### EventBuffer

Circular buffer of RouterEvent instances.

| Property | Default | Description |
|----------|---------|-------------|
| MaxSize | 1000 | Maximum events retained |
| Behavior | FIFO eviction | Oldest events dropped when full |
| Thread safety | ConcurrentQueue | Lock-free reads and writes |

## State Transitions

### RoutingEntry Health

```
[New Peer] ──RegisterPeer──> Healthy
                                │
                    ┌───Ping────┘
                    │           │
                    ▼     (timeout expires)
                Healthy ──────> Unhealthy
                    ▲           │
                    │     (peer re-registers)
                    └───────────┘
```

## Existing Entities (Peer Service — Modified)

### PeerConnectionPool (circuit breaker integration)

New behavior added to existing `ConnectToPeerAsync`:
- Before creating a gRPC channel, check circuit breaker state for the target peer
- If circuit is Open, throw `CircuitBreakerOpenException` immediately (fail fast)
- If circuit is HalfOpen, allow one probe connection attempt

No new fields — uses existing `CircuitBreaker` instances from `CommunicationProtocolManager`.

### QueuedTransaction (SQLite → PostgreSQL migration)

New EF Core entity added to existing `PeerDbContext`, replacing the SQLite `transaction_queue` table.

| Field | Type | Description |
|-------|------|-------------|
| Id | string (PK) | Unique queue entry ID |
| TransactionId | string | Transaction hash |
| OriginPeerId | string | Peer that originated the notification |
| RegisterId | string | Target register |
| Timestamp | DateTimeOffset | When the transaction was created |
| DataSize | int | Size of transaction data in bytes |
| DataHash | string | Hash of transaction data |
| GossipRound | int | Current gossip round |
| HopCount | int | Number of hops so far |
| Ttl | int | Time-to-live in seconds |
| HasFullData | bool | Whether full transaction data is included |
| TransactionData | byte[]? | Full transaction payload (nullable) |
| EnqueuedAt | DateTimeOffset | When added to queue |
| RetryCount | int | Number of delivery attempts |
| Status | string | Pending, Processing, Processed, Failed |

**Migration**: Requires new EF Core migration to add `queued_transactions` table to PeerDbContext.
**Removal**: Delete SQLite schema creation, `Microsoft.Data.Sqlite` package reference, and all `SqliteConnection` usage from `TransactionQueueManager`.
