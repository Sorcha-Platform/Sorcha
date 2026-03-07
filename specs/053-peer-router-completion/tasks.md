# Tasks: Peer Router App & Peer Service Completion

**Input**: Design documents from `/specs/053-peer-router-completion/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/peer-router-api.md

**Tests**: Included — constitution requires >85% coverage on new code.

**Organization**: Tasks grouped by user story. US4 (Heartbeat Streaming) removed — already fully implemented per research.md. US6/US7 (SQLite migration + removal) consolidated into one story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1-US5)
- Exact file paths included

---

## Phase 1: Setup (Project Scaffolding)

**Purpose**: Create the PeerRouter project, wire into solution, configure proto references

- [x] T001 Create project directory and Sorcha.PeerRouter.csproj with Grpc.AspNetCore dependency in src/Apps/Sorcha.PeerRouter/
- [x] T002 Configure proto file references (peer_discovery.proto, peer_heartbeat.proto, peer_communication.proto) from src/Services/Sorcha.Peer.Service/Protos/ with GrpcServices="Server" in Sorcha.PeerRouter.csproj
- [x] T003 Create Program.cs with WebApplication builder, gRPC, and minimal API setup in src/Apps/Sorcha.PeerRouter/Program.cs
- [x] T004 [P] Create test project Sorcha.PeerRouter.Tests with xUnit, FluentAssertions, Moq in tests/Sorcha.PeerRouter.Tests/
- [x] T005 [P] Add both projects to Sorcha.sln

**Checkpoint**: Solution builds with empty PeerRouter app and test project

---

## Phase 2: Foundational (Router Core — Shared by US1 and US2)

**Purpose**: Core models and services that BOTH bootstrap (US1) and debug stream (US2) depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 [P] Create RouterEventType enum in src/Apps/Sorcha.PeerRouter/Models/RouterEventType.cs
- [x] T007 [P] Create RouterEvent record in src/Apps/Sorcha.PeerRouter/Models/RouterEvent.cs
- [x] T008 [P] Create RoutingEntry class in src/Apps/Sorcha.PeerRouter/Models/RoutingEntry.cs
- [x] T009 [P] Create RouterConfiguration POCO with CLI defaults (port, http-port, enable-relay, event-buffer, peer-timeout) in src/Apps/Sorcha.PeerRouter/Models/RouterConfiguration.cs
- [x] T010 Implement RoutingTable service (ConcurrentDictionary, register/update/remove/query by register) in src/Apps/Sorcha.PeerRouter/Services/RoutingTable.cs
- [x] T011 Implement EventBuffer service (ConcurrentQueue with max size, SSE broadcast via Channel<RouterEvent>) in src/Apps/Sorcha.PeerRouter/Services/EventBuffer.cs
- [x] T012 Implement PeerTimeoutService (IHostedService, periodic sweep marking unhealthy peers, emitting PeerDisconnected events) in src/Apps/Sorcha.PeerRouter/Services/PeerTimeoutService.cs
- [x] T013 [P] Write RoutingTable unit tests in tests/Sorcha.PeerRouter.Tests/Services/RoutingTableTests.cs
- [x] T014 [P] Write EventBuffer unit tests in tests/Sorcha.PeerRouter.Tests/Services/EventBufferTests.cs
- [x] T015 [P] Write PeerTimeoutService unit tests in tests/Sorcha.PeerRouter.Tests/Services/PeerTimeoutServiceTests.cs

**Checkpoint**: Foundation ready — RoutingTable and EventBuffer tested, PeerTimeoutService sweeps unhealthy peers

---

## Phase 3: User Story 1 — Network Bootstrap with Peer Router (Priority: P1) MVP

**Goal**: Peers register with the router and discover each other through the peer list

**Independent Test**: Start router, connect two Peer Service instances as seed nodes, verify both peers discover each other through the router's peer list

### Tests for User Story 1

- [ ] T016 [P] [US1] Write RouterDiscoveryService tests (RegisterPeer, GetPeerList, Ping, ExchangePeers, FindPeersForRegister) in tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterDiscoveryServiceTests.cs
- [ ] T017 [P] [US1] Write RouterHeartbeatService tests (SendHeartbeat, StreamHeartbeat) in tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterHeartbeatServiceTests.cs

### Implementation for User Story 1

- [ ] T018 [US1] Implement RouterDiscoveryService gRPC service (RegisterPeer → upsert routing entry + emit event, GetPeerList → return healthy entries, Ping → update LastSeen + emit event, ExchangePeers → merge peer lists + emit event, FindPeersForRegister → filter by register ID) in src/Apps/Sorcha.PeerRouter/GrpcServices/RouterDiscoveryService.cs
- [ ] T019 [US1] Implement RouterHeartbeatService gRPC service (SendHeartbeat → update LastSeen + register versions, StreamHeartbeat → bidirectional stream updating routing table per heartbeat) in src/Apps/Sorcha.PeerRouter/GrpcServices/RouterHeartbeatService.cs
- [ ] T020 [US1] Wire gRPC services and DI registrations in src/Apps/Sorcha.PeerRouter/Program.cs
- [ ] T021 [US1] Add CLI argument parsing (--port, --peer-timeout) to Program.cs using args[] in src/Apps/Sorcha.PeerRouter/Program.cs

**Checkpoint**: Router accepts peer registrations via gRPC, returns peer lists, handles heartbeats. Two peers can discover each other.

---

## Phase 4: User Story 2 — Real-Time Debug Event Stream (Priority: P1)

**Goal**: Developers see live network events via SSE stream and debug page in browser

**Independent Test**: Connect to /events?follow=true while peers connect/disconnect, verify events appear in real time with correct peer identification

### Tests for User Story 2

- [ ] T022 [P] [US2] Write EventStreamEndpoints tests (GET /events snapshot, GET /events?follow=true SSE format) in tests/Sorcha.PeerRouter.Tests/Endpoints/EventStreamEndpointTests.cs
- [ ] T023 [P] [US2] Write PeerEndpoints tests (GET /peers returns routing table) in tests/Sorcha.PeerRouter.Tests/Endpoints/PeerEndpointTests.cs
- [ ] T024 [P] [US2] Write HealthEndpoints tests (GET /health returns status, uptime, peer counts) in tests/Sorcha.PeerRouter.Tests/Endpoints/HealthEndpointTests.cs

### Implementation for User Story 2

- [ ] T025 [US2] Implement EventStreamEndpoints (GET /events returns JSON array snapshot, GET /events?follow=true returns text/event-stream with IAsyncEnumerable) in src/Apps/Sorcha.PeerRouter/Endpoints/EventStreamEndpoints.cs
- [ ] T026 [P] [US2] Implement PeerEndpoints (GET /peers returns routing table with totalPeers, healthyPeers, peers array) in src/Apps/Sorcha.PeerRouter/Endpoints/PeerEndpoints.cs
- [ ] T027 [P] [US2] Implement HealthEndpoints (GET /health returns status, uptime, peer/event counts, relay flag) in src/Apps/Sorcha.PeerRouter/Endpoints/HealthEndpoints.cs
- [ ] T028 [US2] Create static debug.html page (EventSource connection, live event list, peer table, auto-reconnect) in src/Apps/Sorcha.PeerRouter/wwwroot/debug.html
- [ ] T029 [US2] Wire HTTP endpoints, static files, and CLI args (--http-port, --event-buffer) in src/Apps/Sorcha.PeerRouter/Program.cs

**Checkpoint**: Debug page shows live events in browser. curl to /events?follow=true streams JSON. /peers returns routing table. /health returns status.

---

## Phase 5: User Story 7/6 — Remove SQLite, Migrate Queue to PostgreSQL (Priority: P1)

**Goal**: Transaction queue persists to PostgreSQL via PeerDbContext; SQLite completely removed from Peer Service

**Independent Test**: Enqueue transaction notifications, restart Peer Service, verify queued notifications loaded from PostgreSQL on startup

### Tests for User Story 7/6

- [ ] T030 [P] [US7] Write QueuedTransaction entity tests (validation, defaults) in tests/Sorcha.Peer.Service.Tests/Data/QueuedTransactionTests.cs
- [ ] T031 [P] [US7] Write TransactionQueueManager PostgreSQL tests (enqueue persists, dequeue removes, startup loads, max size enforced) in tests/Sorcha.Peer.Service.Tests/Distribution/TransactionQueueManagerPgTests.cs

### Implementation for User Story 7/6

- [ ] T032 [US7] Create QueuedTransaction entity class in src/Services/Sorcha.Peer.Service/Core/QueuedTransaction.cs
- [ ] T033 [US7] Add QueuedTransaction DbSet and configuration to PeerDbContext in src/Services/Sorcha.Peer.Service/Data/PeerDbContext.cs
- [ ] T034 [US7] Generate EF Core migration for queued_transactions table via dotnet ef migrations add AddQueuedTransactions
- [ ] T035 [US7] Refactor TransactionQueueManager to replace SQLite with PeerDbContext (enqueue → SaveChangesAsync, dequeue → Remove, startup → LoadFromDatabaseAsync, max size → trim oldest) in src/Services/Sorcha.Peer.Service/Distribution/TransactionQueueManager.cs
- [ ] T036 [US7] Remove Microsoft.Data.Sqlite package reference from src/Services/Sorcha.Peer.Service/Sorcha.Peer.Service.csproj
- [ ] T037 [US7] Delete all SQLite connection/schema code from TransactionQueueManager (SqliteConnection, CREATE TABLE, etc.) in src/Services/Sorcha.Peer.Service/Distribution/TransactionQueueManager.cs
- [ ] T038 [US7] Update existing TransactionQueueManager tests to use InMemory EF provider instead of SQLite in tests/Sorcha.Peer.Service.Tests/

**Checkpoint**: Queue persists to PostgreSQL. No SQLite references remain. Service restarts preserve queued notifications.

---

## Phase 6: User Story 5 — Connection Circuit Breaking (Priority: P2)

**Goal**: Failed peers get circuit-broken; communication stops during cooldown and resumes after successful probe

**Independent Test**: Connect to a peer, shut it down, observe communication stops after threshold, restart and observe communication resumes after cooldown

### Tests for User Story 5

- [ ] T039 [P] [US5] Write PeerConnectionPool circuit breaker integration tests (open circuit → fail fast, half-open → probe, close → resume) in tests/Sorcha.Peer.Service.Tests/Connection/PeerConnectionPoolCircuitBreakerTests.cs

### Implementation for User Story 5

- [ ] T040 [US5] Add circuit breaker state check to PeerConnectionPool.ConnectToPeerAsync() — check state before creating channel, throw CircuitBreakerOpenException if open, allow probe if half-open in src/Services/Sorcha.Peer.Service/Connection/PeerConnectionPool.cs
- [ ] T041 [US5] Create CircuitBreakerOpenException class in src/Services/Sorcha.Peer.Service/Communication/CircuitBreakerOpenException.cs
- [ ] T042 [US5] Wire CircuitBreaker instances from CommunicationProtocolManager into PeerConnectionPool via DI in src/Services/Sorcha.Peer.Service/Connection/PeerConnectionPool.cs
- [ ] T043 [US5] Add structured logging for circuit state changes (open/half-open/closed with peer ID and failure count) in src/Services/Sorcha.Peer.Service/Communication/CircuitBreaker.cs

**Checkpoint**: Unreachable peers are circuit-broken. No wasted connection attempts during cooldown. Automatic recovery after cooldown probe succeeds.

---

## Phase 7: User Story 3 — Optional Relay Mode (Priority: P3)

**Goal**: When --enable-relay is set, the router forwards messages between peers that cannot reach each other directly

**Independent Test**: Start router with --enable-relay, connect two peers, send message from A to B through router, verify delivery

### Tests for User Story 3

- [ ] T044 [P] [US3] Write RouterCommunicationService tests (relay enabled → forward, relay disabled → reject, unknown peer → error) in tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterCommunicationServiceTests.cs

### Implementation for User Story 3

- [ ] T045 [US3] Implement RouterCommunicationService gRPC service (SendMessage → lookup recipient in routing table → forward via gRPC client, emit RelayForwarded event; reject if relay disabled or peer unknown) in src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs
- [ ] T046 [US3] Wire relay service conditionally based on --enable-relay flag in src/Apps/Sorcha.PeerRouter/Program.cs

**Checkpoint**: Relay forwards messages when enabled, rejects when disabled. Debug stream shows RelayForwarded events.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Docker, Aspire integration, documentation, final validation

- [ ] T047 [P] Create Dockerfile (2-stage build + runtime) in src/Apps/Sorcha.PeerRouter/Dockerfile
- [ ] T048 [P] Add peer-router service to docker-compose.yml with profiles: [tools] and port 5500 in docker-compose.yml
- [ ] T049 [P] Add PeerRouter project reference to Aspire AppHost with WithExternalHttpEndpoints() in src/Apps/Sorcha.AppHost/AppHost.cs
- [ ] T050 [P] Add launchSettings.json with gRPC and HTTP port profiles in src/Apps/Sorcha.PeerRouter/Properties/launchSettings.json
- [ ] T051 Update Peer Service README with circuit breaker and queue migration changes in src/Services/Sorcha.Peer.Service/README.md
- [ ] T052 Update docs/getting-started/PORT-CONFIGURATION.md with PeerRouter port assignments (gRPC 5500, HTTP 8080)
- [ ] T053 Update docs/reference/development-status.md with Peer Service completion (70% → 95%) and PeerRouter status
- [ ] T054 Update .specify/MASTER-TASKS.md with Feature 053 task status
- [ ] T055 Run quickstart.md validation — start router, connect peers, verify debug page and event stream
- [ ] T056 Verify all tests pass: dotnet test --filter "FullyQualifiedName~PeerRouter or FullyQualifiedName~Peer.Service"

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 Bootstrap)**: Depends on Phase 2 — no cross-story deps
- **Phase 4 (US2 Debug Stream)**: Depends on Phase 2 — parallel with Phase 3
- **Phase 5 (US7 SQLite Removal)**: Depends on Phase 2 — parallel with Phase 3/4 (different project)
- **Phase 6 (US5 Circuit Breaker)**: Depends on Phase 2 — parallel with Phase 3/4 (different project)
- **Phase 7 (US3 Relay)**: Depends on Phase 3 (needs RouterDiscoveryService for peer lookup)
- **Phase 8 (Polish)**: Depends on all desired stories being complete

### User Story Dependencies

- **US1 (Bootstrap)**: Independent after Foundational
- **US2 (Debug Stream)**: Independent after Foundational — can run parallel with US1
- **US7 (SQLite Removal)**: Independent — different project (Peer Service), parallel with US1/US2
- **US5 (Circuit Breaker)**: Independent — different project (Peer Service), parallel with US1/US2
- **US3 (Relay)**: Depends on US1 (needs routing table populated by discovery service)

### Parallel Opportunities

**Wave 1** (after Phase 2):
```
PeerRouter:   US1 (Bootstrap gRPC) ─┐
              US2 (Debug HTTP/SSE) ──┤── all parallel
Peer Service: US7 (SQLite removal) ──┤
              US5 (Circuit breaker) ─┘
```

**Wave 2** (after US1):
```
PeerRouter: US3 (Relay mode) ── depends on US1 routing table
```

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Models — all parallel (different files):
Task: "Create RouterEventType enum in src/Apps/Sorcha.PeerRouter/Models/RouterEventType.cs"
Task: "Create RouterEvent record in src/Apps/Sorcha.PeerRouter/Models/RouterEvent.cs"
Task: "Create RoutingEntry class in src/Apps/Sorcha.PeerRouter/Models/RoutingEntry.cs"
Task: "Create RouterConfiguration in src/Apps/Sorcha.PeerRouter/Models/RouterConfiguration.cs"

# Tests — all parallel (different files, after services):
Task: "Write RoutingTable unit tests in tests/Sorcha.PeerRouter.Tests/Services/RoutingTableTests.cs"
Task: "Write EventBuffer unit tests in tests/Sorcha.PeerRouter.Tests/Services/EventBufferTests.cs"
Task: "Write PeerTimeoutService tests in tests/Sorcha.PeerRouter.Tests/Services/PeerTimeoutServiceTests.cs"
```

## Parallel Example: Wave 1 (US1 + US2 + US7 + US5)

```bash
# All four stories in parallel — different projects/files:
Task: "Implement RouterDiscoveryService in src/Apps/Sorcha.PeerRouter/GrpcServices/RouterDiscoveryService.cs"  # US1
Task: "Implement EventStreamEndpoints in src/Apps/Sorcha.PeerRouter/Endpoints/EventStreamEndpoints.cs"         # US2
Task: "Refactor TransactionQueueManager in src/Services/Sorcha.Peer.Service/Distribution/TransactionQueueManager.cs" # US7
Task: "Wire CircuitBreaker into PeerConnectionPool in src/Services/Sorcha.Peer.Service/Connection/PeerConnectionPool.cs" # US5
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T015)
3. Complete Phase 3: US1 Bootstrap (T016-T021)
4. **STOP and VALIDATE**: Start router, connect two peers, verify discovery
5. This is the minimum viable router — peers can find each other

### Incremental Delivery

1. Setup + Foundational → Project builds, core services tested
2. US1 (Bootstrap) → Peers discover each other via router (MVP!)
3. US2 (Debug Stream) → Developers see live events in browser
4. US7 (SQLite Removal) → Architectural consistency, no SQLite dependency
5. US5 (Circuit Breaker) → Resilient peer connections
6. US3 (Relay) → NAT traversal convenience for development
7. Polish → Docker, Aspire, docs, final validation

### Note on US4 (Heartbeat Streaming)

US4 is **already implemented** — `PeerHeartbeatGrpcService.StreamHeartbeat()` is fully functional per research.md. No tasks generated. The router's `RouterHeartbeatService` (T019) implements the server side for router-to-peer heartbeat streams.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US4 (Heartbeat Streaming) excluded — already complete
- US6/US7 consolidated — same implementation (SQLite→PostgreSQL)
- PeerRouter has no database — all in-memory (RoutingTable, EventBuffer)
- Peer Service modifications are surgical — 3 files changed, 1 entity added, 1 migration
- License headers required on all new .cs files
