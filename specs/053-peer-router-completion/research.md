# Research: Peer Router App & Peer Service Completion

**Feature**: 053-peer-router-completion
**Date**: 2026-03-07

## Research Findings

### 1. Proto File Sharing Strategy

**Decision**: PeerRouter references Peer Service proto files via relative path with `GrpcServices="Server"`.

**Rationale**: This is the established Sorcha pattern. `Sorcha.ServiceClients` already references Peer Service protos (`peer_discovery.proto`, `peer_communication.proto`, `docket_sync.proto`) via relative `Include` paths with `Link` attributes. The PeerRouter only needs server stubs (it receives gRPC calls from peers, doesn't make outgoing gRPC calls).

**Alternatives considered**:
- Shared proto NuGet package — overkill for 2 consumers, adds build complexity
- Copy protos into PeerRouter — duplication, divergence risk
- Reference via ServiceClients — would pull in all service client dependencies unnecessarily

**Protos needed by PeerRouter**:
- `peer_discovery.proto` (Server) — RegisterPeer, GetPeerList, Ping, ExchangePeers, FindPeersForRegister
- `peer_heartbeat.proto` (Server) — SendHeartbeat, StreamHeartbeat (for peer health tracking)
- `peer_communication.proto` (Server) — SendMessage, Stream (only when relay mode enabled)

### 2. Peer Service Completion — Scope Reduction

**Decision**: The Peer Service is closer to 90-95% complete, not 70%. Three of the four originally scoped items are already implemented.

**Findings from code exploration**:

| Originally Scoped | Actual Status | Action |
|---|---|---|
| Bidirectional heartbeat streaming | COMPLETE — `PeerHeartbeatGrpcService.StreamHeartbeat()` fully implemented | Remove from scope |
| Circuit breaker wiring | PARTIAL — wired into `CommunicationProtocolManager` but NOT into `PeerConnectionPool` | Small fix: add circuit check to pool |
| Persistent offline queue | COMPLETE — `TransactionQueueManager` has SQLite persistence with retry tracking | Migrate from SQLite to in-memory + standard storage |

**Remaining Peer Service gaps**:
1. Wire circuit breaker state check into `PeerConnectionPool.ConnectToPeerAsync()` so it fails fast when a peer's circuit is open, rather than attempting a connection that will timeout.
2. Remove SQLite from `TransactionQueueManager` — SQLite is not part of the standard Sorcha stack. Replace with `ConcurrentQueue` (in-memory) for the hot path, backed by PostgreSQL via `Sorcha.Storage.EFCore` for persistence across restarts. This aligns with the project's storage abstraction pattern (MongoDB/PostgreSQL via `Sorcha.Storage.*`).

### 8. SQLite Removal from Peer Service

**Decision**: Replace `TransactionQueueManager`'s direct SQLite dependency with in-memory queue + PostgreSQL via existing `PeerDbContext`.

**Rationale**: SQLite is not in the Sorcha technology stack. The project uses PostgreSQL (via EF Core) and MongoDB for persistent storage, accessed through `Sorcha.Storage.*` abstractions. The Peer Service already has a `PeerDbContext` with PostgreSQL — the queue table should live there.

**Approach**:
- Add a `QueuedTransaction` entity to `PeerDbContext`
- Replace `SqliteConnection` calls with EF Core operations via `PeerDbContext`
- Remove `Microsoft.Data.Sqlite` package reference
- Keep `ConcurrentQueue` for in-memory hot path (dequeue performance)
- Persist to PostgreSQL on enqueue, remove on successful delivery
- Load from PostgreSQL on startup (existing `LoadFromDatabaseAsync` pattern)

**Alternatives considered**:
- Redis lists — adds coupling to Redis for a queue that needs durability, not speed
- File-based append log — simpler but non-standard, no query capability
- Keep SQLite — violates project standards

### 3. App Integration Patterns

**Decision**: PeerRouter follows the lightweight app pattern (like McpServer/Demo), not the full service pattern.

**Rationale**: PeerRouter has no database, no Redis, no JWT auth. It's a utility app with a web server for the debug page and gRPC for peer protocol.

**Pattern**:
- Use `WebApplication.CreateBuilder` (needs HTTP for debug page + gRPC)
- Reference `Grpc.AspNetCore` directly (not through ServiceDefaults — no Aspire telemetry needed)
- 2-stage Dockerfile (build + runtime, no chiseled image needed)
- Docker: use `profiles: [tools]` so it only starts when explicitly requested
- Aspire: add as project with `WithExternalHttpEndpoints()`

### 4. SSE Event Stream Implementation

**Decision**: Use ASP.NET Core minimal API with `text/event-stream` content type and `IAsyncEnumerable<RouterEvent>`.

**Rationale**: SSE is natively supported by browsers via `EventSource` API, works with `curl`, and is trivially parseable by AI assistants. No SignalR dependency needed.

**Pattern**:
```
GET /events?follow=true  → SSE stream (long-lived)
GET /events              → JSON array of buffered events (snapshot)
GET /peers               → JSON array of current routing table
GET /                    → Static HTML debug page
```

### 5. Event Buffer Strategy

**Decision**: `ConcurrentQueue<RouterEvent>` with configurable max size (default 1000), trimmed on insertion.

**Rationale**: Ring buffer semantics with thread-safe access. No persistence needed — the router is a debugging tool, not an audit system. Events are ephemeral.

### 6. Peer Timeout and Health

**Decision**: Configurable timeout (default 60 seconds). Peers not seen within timeout are marked unhealthy and excluded from `GetPeerList` responses but retained in routing table for debug visibility.

**Rationale**: Aggressive removal would cause flapping in unstable networks. Marking unhealthy but retaining allows the debug page to show "last seen 5 minutes ago" which is more useful for debugging than simply disappearing.

### 7. Relay Mode Architecture

**Decision**: When `--enable-relay` is set, the router implements `PeerCommunication.Stream()` as a message forwarder. It matches `recipient_peer_id` to a connected peer and forwards the message.

**Rationale**: Minimal implementation — just read message, look up recipient in routing table, forward. No queuing, no persistence, no guaranteed delivery. This is a debug convenience, not production infrastructure.

**Limitation**: Both peers must be connected to the same router instance. No multi-hop relay.
