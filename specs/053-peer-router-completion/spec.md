# Feature Specification: Peer Router App & Peer Service Completion

**Feature Branch**: `053-peer-router-completion`
**Created**: 2026-03-07
**Status**: Draft
**Input**: Lightweight standalone Peer Router app for P2P network bootstrapping and debugging, plus completing the Peer Service from 70% to 100%.

## User Scenarios & Testing

### User Story 1 - Network Bootstrap with Peer Router (Priority: P1)

A network operator deploys the Peer Router as a single Docker container on a known address. When Peer Service nodes start up, they connect to the router as their bootstrap/seed node, register themselves, and receive a list of other peers on the network. The router maintains a live routing table of all connected peers and their advertised registers. Peers then connect directly to each other for data exchange.

**Why this priority**: Without a reliable bootstrap mechanism, peers cannot discover each other. The router is the entry point to the entire P2P network and must work before anything else can be tested.

**Independent Test**: Can be tested by starting a router, connecting two Peer Service instances configured with the router as their seed node, and verifying both peers discover each other through the router's peer list.

**Acceptance Scenarios**:

1. **Given** a Peer Router is running on a known address, **When** a Peer Service starts with the router configured as a seed node, **Then** the peer successfully registers itself with the router and receives an acknowledgement.
2. **Given** two or more peers have registered with the router, **When** any peer requests the peer list, **Then** the router returns all registered peers with their addresses, ports, capabilities, and advertised registers.
3. **Given** a peer has registered, **When** the router receives a ping from that peer, **Then** the router updates the peer's last-seen timestamp and responds with its own status.
4. **Given** peers A and B are both registered, **When** peer A requests peers that hold a specific register, **Then** the router returns only peers advertising that register (e.g. peer B if it holds it).
5. **Given** a registered peer has not sent a heartbeat or ping within the configured timeout period, **Then** the router marks the peer as unhealthy and excludes it from peer list responses.

---

### User Story 2 - Real-Time Debug Event Stream (Priority: P1)

A developer or operator monitoring the P2P network opens the router's debug page in a browser or connects to the event stream endpoint. They see a live feed of network events — peer connections, disconnections, heartbeats, register advertisements, peer list requests — each showing the peer ID, node name, IP address, timestamp, and event details. This enables rapid diagnosis of connectivity issues when bringing up the network.

**Why this priority**: Debugging P2P networks is notoriously difficult without visibility. This is the primary reason for building the router as a separate app rather than just using the existing Peer Service — the debug output is essential for development.

**Independent Test**: Can be tested by connecting to the event stream endpoint while peers connect and disconnect, and verifying events appear in real time with correct peer identification.

**Acceptance Scenarios**:

1. **Given** the router is running, **When** a developer opens the debug page in a browser, **Then** they see a live-updating list of recent network events without requiring manual refresh.
2. **Given** the router is running, **When** a tool or AI assistant connects to the event stream endpoint, **Then** it receives a continuous stream of structured JSON events that can be parsed programmatically.
3. **Given** a peer connects to the router, **Then** a "PeerConnected" event appears in the stream showing the peer's ID, node name (if provided), IP address, port, capabilities, and timestamp.
4. **Given** a peer disconnects or times out, **Then** a "PeerDisconnected" event appears with the peer's identification and the reason (timeout, explicit disconnect, error).
5. **Given** a peer advertises registers, **Then** a "RegisterAdvertised" event appears listing the register IDs, sync states, and the advertising peer's identification.
6. **Given** any event occurs, **Then** the event record includes: event type, timestamp, peer ID, node name (if available), IP address, and a detail object specific to the event type.
7. **Given** the router has been running for some time, **When** a developer opens the debug page, **Then** they see the most recent events (up to a configurable buffer size) immediately, followed by new events as they occur.

---

### User Story 3 - Optional Relay Mode for NAT Traversal (Priority: P3)

When enabled via a command-line flag, the router can forward messages between peers that cannot reach each other directly due to NAT or firewall restrictions. A peer sends a message addressed to another peer through the router, and the router forwards it. This is a development/debugging convenience, not a production-grade relay.

**Why this priority**: Direct peer-to-peer connectivity works in most development scenarios. Relay is a fallback for edge cases (corporate firewalls, complex NAT) and can be deferred without blocking core functionality.

**Independent Test**: Can be tested by starting the router with the relay flag enabled, connecting two peers that cannot reach each other directly, and verifying messages are forwarded through the router.

**Acceptance Scenarios**:

1. **Given** the router is started with the relay flag enabled, **When** peer A sends a message addressed to peer B through the router, **Then** the router forwards the message to peer B and returns an acknowledgement to peer A.
2. **Given** the router is started without the relay flag, **When** peer A attempts to send a relayed message, **Then** the router rejects the request with a clear error indicating relay mode is not enabled.
3. **Given** relay mode is active, **When** a message is forwarded, **Then** a "RelayForwarded" event appears in the debug stream showing source peer, destination peer, message type, and size.
4. **Given** relay mode is active, **When** a message is addressed to an unknown peer, **Then** the router returns an error indicating the destination peer is not registered.

---

### User Story 4 - Reliable Peer Heartbeat Streaming (Priority: P2)

Two connected peers establish a long-lived bidirectional heartbeat stream. Each peer periodically sends its health metrics and per-register version numbers. The receiving peer updates its view of the sender's health and detects register version gaps that trigger synchronization. If the stream breaks, both sides detect the failure and attempt reconnection.

**Why this priority**: The unary heartbeat works but the bidirectional stream is incomplete. Streaming heartbeats are more efficient for sustained connections and enable real-time sync gap detection — a prerequisite for reliable register replication.

**Independent Test**: Can be tested by connecting two Peer Service instances, establishing a heartbeat stream, and verifying that per-register version numbers are exchanged and that stream disconnection is detected within the configured timeout.

**Acceptance Scenarios**:

1. **Given** two peers are connected, **When** they establish a bidirectional heartbeat stream, **Then** both peers periodically send their health metrics (CPU, memory, active connections, uptime) and receive the other peer's metrics.
2. **Given** a heartbeat stream is active, **When** peer A's register version advances (new docket sealed), **Then** peer B detects the version gap in the next heartbeat exchange and can trigger synchronization.
3. **Given** a heartbeat stream is active, **When** one side stops sending for longer than the configured timeout, **Then** the other side detects the failure, marks the peer as unhealthy, and closes the stream.
4. **Given** a heartbeat stream fails, **When** the connection is re-established, **Then** the stream resumes with current state (no stale data from the previous stream).

---

### User Story 5 - Connection Circuit Breaking (Priority: P2)

When a peer repeatedly fails to respond (connection refused, timeouts, errors), the system stops attempting to communicate with that peer for a cooldown period. After the cooldown expires, a single probe request is sent. If it succeeds, normal communication resumes. If it fails, the circuit remains open for another cooldown period. This prevents wasted resources on unreachable peers.

**Why this priority**: Without circuit breaking, the Peer Service wastes CPU and network resources retrying connections to dead peers, potentially causing cascading failures. The configuration already exists — it just needs to be wired in.

**Independent Test**: Can be tested by connecting to a peer, shutting that peer down, observing that communication attempts stop after the configured failure threshold, and then restarting the peer and observing that communication resumes after the cooldown.

**Acceptance Scenarios**:

1. **Given** a peer has failed to respond N times consecutively (where N is the configured threshold), **Then** the circuit opens and no further requests are sent to that peer.
2. **Given** the circuit is open, **When** the configured cooldown period expires, **Then** a single probe request is sent to test if the peer has recovered.
3. **Given** a probe request succeeds, **Then** the circuit closes and normal communication resumes with the peer.
4. **Given** a probe request fails, **Then** the circuit remains open for another cooldown period.
5. **Given** a circuit opens or closes, **Then** the event is logged with the peer's identification and the failure count.

---

### User Story 6 - Migrate Transaction Queue from SQLite to PostgreSQL (Priority: P2)

The Peer Service's transaction queue currently uses SQLite directly, which is not part of the Sorcha technology stack. The queue persistence must be migrated to PostgreSQL via the existing PeerDbContext, aligning with the project's storage abstraction pattern. The in-memory concurrent queue remains the hot path; PostgreSQL provides durability across restarts.

**Why this priority**: SQLite creates an architectural inconsistency. Every other service uses PostgreSQL or MongoDB via the storage abstraction layer. Keeping SQLite adds a maintenance burden and a non-standard dependency.

**Independent Test**: Can be tested by enqueuing transaction notifications, restarting the Peer Service, and verifying queued notifications are loaded from PostgreSQL on startup.

**Acceptance Scenarios**:

1. **Given** a transaction notification is enqueued, **Then** it is persisted to PostgreSQL via the existing PeerDbContext.
2. **Given** the Peer Service restarts, **When** it initializes, **Then** it loads persisted notifications from PostgreSQL.
3. **Given** queued notifications exist and a peer becomes available, **Then** queued notifications are replayed in order.
4. **Given** a queued notification is successfully delivered, **Then** it is removed from the PostgreSQL queue table.
5. **Given** the queue exceeds the configured maximum size, **Then** the oldest notifications are discarded and a warning is logged.
6. **Given** the migration is complete, **Then** the Peer Service has no reference to Microsoft.Data.Sqlite.

---

### User Story 7 - Remove SQLite from Peer Service (Priority: P1)

The Peer Service currently uses SQLite directly for its transaction queue persistence, bypassing the project's standard storage stack (PostgreSQL via EF Core, MongoDB). This must be migrated to PostgreSQL via the existing `PeerDbContext` to maintain architectural consistency. The in-memory queue continues to serve the hot path; PostgreSQL provides durability across restarts.

**Why this priority**: SQLite is not part of the Sorcha technology stack. Keeping it creates a maintenance burden and a deviation from the storage abstraction pattern used everywhere else.

**Independent Test**: Can be tested by enqueuing transactions, restarting the Peer Service, and verifying queued transactions are loaded from PostgreSQL on startup.

**Acceptance Scenarios**:

1. **Given** a transaction notification is enqueued, **Then** it is persisted to PostgreSQL via the existing PeerDbContext, not to a SQLite database.
2. **Given** the Peer Service restarts, **When** it initializes, **Then** it loads persisted notifications from PostgreSQL.
3. **Given** a notification is successfully delivered, **Then** it is removed from the PostgreSQL queue table.
4. **Given** the SQLite package has been removed, **Then** the Peer Service project has no reference to `Microsoft.Data.Sqlite` or any SQLite-related packages.
5. **Given** the queue exceeds the configured maximum size, **Then** the oldest notifications are discarded and a warning is logged.

---

## Functional Requirements

### FR-1: Peer Router Application

**FR-1.1**: The system shall provide a standalone application that serves as a P2P network rendezvous point, deployable as a single container with no external dependencies (no database, no cache, no message broker).

**FR-1.2**: The router shall implement the peer discovery protocol, allowing peers to register themselves, retrieve lists of other peers, exchange peer information, and find peers holding specific registers.

**FR-1.3**: The router shall maintain an in-memory routing table of all registered peers, including their addresses, ports, capabilities, advertised registers, and health status.

**FR-1.4**: The router shall remove peers from the active routing table when they have not communicated within a configurable timeout period.

**FR-1.5**: The router shall expose a machine-readable event stream endpoint that emits structured JSON events via Server-Sent Events (SSE). Each event shall include: event type, ISO 8601 timestamp, peer ID, node name (if available), IP address, and a type-specific detail payload.

**FR-1.6**: The router shall serve a read-only debug page that displays live network events, connected peer count, and routing table contents, consuming the same event stream.

**FR-1.7**: The router shall buffer the most recent events (configurable, default 1000) so that newly connected clients receive recent history.

**FR-1.8**: The router shall accept anonymous connections with no authentication required.

**FR-1.9**: The router shall accept a command-line flag to enable relay mode. When enabled, the router shall forward messages between peers using the peer communication protocol.

**FR-1.10**: The router shall be configurable via command-line arguments (port, relay flag, event buffer size, peer timeout) and optionally via environment variables.

### FR-2: Bidirectional Heartbeat Streaming

**FR-2.1**: The Peer Service shall support bidirectional heartbeat streams where both peers continuously exchange health metrics and per-register version numbers.

**FR-2.2**: Each heartbeat message shall include the peer's current register versions (map of register ID to latest version number), enabling the receiving peer to detect synchronization gaps.

**FR-2.3**: The heartbeat stream shall detect connection failure within the configured timeout period and update the peer's health status accordingly.

**FR-2.4**: When a heartbeat stream reconnects, it shall resume with current state rather than replaying historical data.

### FR-3: Connection Circuit Breaking

**FR-3.1**: The Peer Service shall track consecutive communication failures per peer and open a circuit (stop sending) when the failure count exceeds the configured threshold.

**FR-3.2**: After the configured cooldown period, the system shall send a single probe request. If successful, the circuit closes and normal communication resumes. If unsuccessful, the cooldown restarts.

**FR-3.3**: Circuit state changes (open, half-open, closed) shall be logged with peer identification details.

### FR-4: Transaction Queue SQLite to PostgreSQL Migration

**FR-4.1**: The Peer Service's transaction queue shall persist to PostgreSQL via the existing `PeerDbContext` instead of SQLite.

**FR-4.2**: On startup, the Peer Service shall load persisted notifications from PostgreSQL and add them to the in-memory outbound queue.

**FR-4.3**: Successfully delivered notifications shall be removed from the PostgreSQL queue table.

**FR-4.4**: The queue shall enforce the configured maximum size, discarding the oldest entries when the limit is reached.

**FR-4.5**: All SQLite package references and direct SQLite connection code shall be removed from the Peer Service.

### FR-5: SQLite Removal from Peer Service

**FR-5.1**: The Peer Service's transaction queue persistence shall use PostgreSQL via the existing `PeerDbContext` instead of SQLite.

**FR-5.2**: The in-memory concurrent queue shall remain as the hot path for dequeue operations, with PostgreSQL providing durability across restarts.

**FR-5.3**: On startup, the Peer Service shall load any persisted queue entries from PostgreSQL.

**FR-5.4**: All references to SQLite packages shall be removed from the Peer Service project.

---

## Success Criteria

1. A network operator can deploy the Peer Router as a single container and have two or more Peer Service nodes discover each other through it within 30 seconds of startup.
2. A developer monitoring the debug page or event stream can see every peer connection, disconnection, and register advertisement event in real time, with each event showing the peer's IP address, node name, and peer ID.
3. An AI assistant can connect to the event stream endpoint and parse the structured JSON output to diagnose connectivity issues without human interpretation.
4. Connected peers exchange register version numbers via heartbeat streams and detect version gaps that would trigger synchronization.
5. When a peer becomes unreachable, the system stops attempting communication within the configured failure threshold and automatically resumes when the peer recovers.
6. Transaction notifications queued during a network outage survive a Peer Service restart and are delivered when connectivity is restored.
7. The Peer Router operates with no external infrastructure dependencies — no database, no Redis, no message broker — just the single application process.

---

## Scope & Boundaries

### In Scope

- New standalone Peer Router application (src/Apps/Sorcha.PeerRouter)
- Peer Router Docker image and docker-compose entry
- Peer Router debug page (static HTML) and SSE event stream
- Optional relay mode behind command-line flag
- Completing bidirectional heartbeat streaming in Peer Service
- Wiring circuit breaker into Peer Service connection pool
- Migrating Peer Service transaction queue from SQLite to PostgreSQL via PeerDbContext
- Peer Router integration into Aspire AppHost for local development
- Tests for all new and modified components

### Out of Scope

- Mutual TLS (mTLS) for gRPC connections
- BLS threshold signature aggregation
- Peer reputation scoring
- IPv6 STUN support
- Extracting shared proto definitions into a separate project (will reference Peer Service protos directly)
- Production-grade relay infrastructure (relay mode is a development convenience)
- Changes to the peer discovery protocol itself (router implements existing protocol)

---

## Dependencies

- Existing Peer Service gRPC protocol definitions (6 .proto files)
- Existing Peer Service configuration model (PeerServiceConfiguration)
- Existing CircuitBreaker.cs implementation (needs wiring, not rewriting)
- Existing TransactionQueueManager (needs persistence layer, not rewriting)

---

## Assumptions

- The Peer Router will reference proto files directly from the Peer Service project rather than a shared proto library. This creates a build-time dependency but avoids the overhead of extracting a separate project.
- The persistent offline queue will use a simple append-only file format rather than SQLite, keeping dependencies minimal. If durability requirements increase, this can be upgraded later.
- The debug page will be embedded as a static resource in the application binary, not served from a separate web framework.
- The router's in-memory routing table is acceptable for the expected scale (tens to low hundreds of peers, not thousands).
- Relay mode does not need authentication — it is intended for development and debugging environments only.
- The event buffer default of 1000 events is sufficient for debugging sessions without consuming excessive memory.

---

## Key Entities

- **RouterEvent**: Type, Timestamp, PeerId, NodeName, IpAddress, Port, Detail (type-specific payload)
- **RoutingEntry**: PeerId, NodeName, Address, Port, Capabilities, AdvertisedRegisters, LastSeen, HealthStatus
- **CircuitState**: PeerId, State (Closed/Open/HalfOpen), FailureCount, LastFailure, CooldownExpiry
- **PersistedNotification**: TransactionHash, RegisterId, Timestamp, Payload, DeliveryAttempts
