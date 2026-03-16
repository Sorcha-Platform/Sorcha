# Tasks: Relay-Aware Peer Communication

**Input**: Design documents from `/specs/060-relay-aware-communication/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/proto-changes.md, quickstart.md

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Proto changes and shared data models that all user stories depend on

- [x] T001 Add 4 new MessageType enum values (REGISTER_SYNC_REQUEST=8, REGISTER_SYNC_RESPONSE=9, TRANSACTION_DATA_REQUEST=10, TRANSACTION_DATA_RESPONSE=11) to `src/Services/Sorcha.Peer.Service/Protos/peer_communication.proto`
- [x] T002 Create relay payload POCOs (RegisterSyncRequest, RegisterSyncResponse, DocketEntry, TransactionDataRequest, TransactionDataResponse, TransactionEntry) in `src/Services/Sorcha.Peer.Service/Communication/Models/RelayMessages.cs`
- [x] T003 Add `RelayPollIntervalSeconds` property (default 60, range 10-300) to `RegisterSyncConfiguration` in `src/Services/Sorcha.Peer.Service/Core/PeerServiceConfiguration.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core relay primitive that ALL user stories depend on — sends messages through seed node relay and manages request/response correlation

**CRITICAL**: No user story work can begin until this phase is complete

### Tests for Foundation

- [x] T004 [P] Create `RelayCommunicationServiceTests` in `tests/Sorcha.Peer.Service.Tests/Communication/RelayCommunicationServiceTests.cs` — test SendViaRelayAsync routes through mocked seed channel, test SendAndWaitAsync generates correlation ID and returns response on CompleteCorrelation, test SendAndWaitAsync returns null on timeout, test no seed node connected returns false/null, test SenderPeerId is populated with NodeId, test stale response for expired/removed correlation ID is silently discarded, test relay failure calls RecordFailureAsync against target peer (not seed node)

### Implementation for Foundation

- [x] T005 Implement `RelayCommunicationService` in `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs` — constructor takes PeerConnectionPool, PeerListManager, PeerServiceConfiguration; implements SendViaRelayAsync (fire-and-forget via seed channel), SendAndWaitAsync<TResponse> (correlation via ConcurrentDictionary<string, TCS<PeerMessage>>), CompleteCorrelation (matches response to pending TCS); populates SenderPeerId from config NodeId; seed selection: GetAllActiveChannels filtered by IsSeedNode, first available; on relay failure, call PeerConnectionPool.RecordFailureAsync(targetPeerId) against the target peer (NOT the seed node)
- [x] T006 Register `RelayCommunicationService` as singleton in `src/Services/Sorcha.Peer.Service/Program.cs` DI container (add to replication services section)
- [x] T007 Verify T004 tests pass — all RelayCommunicationService tests green

**Checkpoint**: Core relay primitive operational — can send messages through seed node and correlate responses

---

## Phase 3: User Story 1 — NAT'd Peer Sends Messages Via Relay (Priority: P1) MVP

**Goal**: When CommunicationProtocolManager sends a message and the target peer has an empty address, route through relay instead of attempting direct connection

**Independent Test**: Deploy two peers behind NAT with a seed node relay, send a message from Peer A to Peer B, verify Peer B receives it

### Tests for User Story 1

- [x] T008 [P] [US1] Add relay fallback tests to `tests/Sorcha.Peer.Service.Tests/Communication/CommunicationProtocolManagerTests.cs` — test SendMessageAsync uses relay when peer.Address is empty, test SendMessageAsync uses direct path when peer.Address is present, test relay returns false when no seed node connected

### Implementation for User Story 1

- [x] T009 [US1] Add `RelayCommunicationService` constructor dependency to `CommunicationProtocolManager` in `src/Services/Sorcha.Peer.Service/Communication/CommunicationProtocolManager.cs`
- [x] T010 [US1] Add relay fallback check at the top of `SendMessageAsync` in `CommunicationProtocolManager` — if `string.IsNullOrEmpty(peer.Address)`, call `_relayCommunication.SendViaRelayAsync(peer.PeerId, MessageType.TransactionNotification, message, ct)` and return result; else continue existing GrpcStream → Grpc → REST chain
- [x] T011 [US1] Verify T008 tests pass — relay fallback triggers correctly for NAT'd peers

**Checkpoint**: NAT'd peers can send messages via relay; direct peers unaffected

---

## Phase 4: User Story 2 — Transaction Distribution Reaches NAT'd Peers (Priority: P1)

**Goal**: Transaction gossip reaches NAT'd peers by falling back to relay in TransactionDistributionService when target peer has no address

**Independent Test**: Create a transaction on Peer A, verify Peer B (NAT'd) receives the transaction notification via relay

### Tests for User Story 2

- [x] T012 [P] [US2] Add relay fallback tests to `tests/Sorcha.Peer.Service.Tests/Distribution/TransactionDistributionServiceTests.cs` — test SendToPeerAsync uses relay when peer.Address is empty, test SendToPeerAsync uses direct gRPC when peer.Address is present

### Implementation for User Story 2

- [x] T013 [US2] Add `RelayCommunicationService` constructor dependency to `TransactionDistributionService` in `src/Services/Sorcha.Peer.Service/Distribution/TransactionDistributionService.cs`
- [x] T014 [US2] Add relay fallback in `SendToPeerAsync` — if `string.IsNullOrEmpty(peer.Address)`, call `_relayCommunication.SendViaRelayAsync(peer.PeerId, MessageType.TransactionNotification, txNotification, ct)` and return result; else continue existing direct gRPC send
- [x] T015 [US2] Verify T012 tests pass — transaction distribution uses relay for NAT'd targets

**Checkpoint**: Transaction gossip now reaches NAT'd peers; notifications handled identically to direct-received ones

---

## Phase 5: User Story 3 — Peer Receives and Dispatches Relayed Messages (Priority: P1)

**Goal**: Peer can receive incoming relayed messages (forwarded by seed node) and dispatch them to appropriate handlers — serve sync requests, complete pending correlations, trigger sync on notifications

**Independent Test**: Send a relayed message to a peer and verify the appropriate handler processes it and responds correctly

### Tests for User Story 3

- [x] T016 [P] [US3] Create `RelayMessageHandlerTests` in `tests/Sorcha.Peer.Service.Tests/Communication/RelayMessageHandlerTests.cs` — test HandleAsync dispatches REGISTER_SYNC_REQUEST to read local RegisterCache and send response via relay, test HandleAsync dispatches REGISTER_SYNC_RESPONSE to CompleteCorrelation, test HandleAsync dispatches TRANSACTION_DATA_REQUEST to read local store and respond, test HandleAsync dispatches TRANSACTION_DATA_RESPONSE to CompleteCorrelation, test HandleTransactionNotificationAsync triggers sync for subscribed register, test HandleTransactionNotificationAsync is no-op for unsubscribed register, test response size limit caps dockets at MaxDockets
- [x] T017 [P] [US3] Create `PeerCommunicationServiceImplTests` in `tests/Sorcha.Peer.Service.Tests/GrpcServices/PeerCommunicationServiceImplTests.cs` — test SendMessage dispatches relay message types to RelayMessageHandler, test SendMessage dispatches TRANSACTION_NOTIFICATION to HandleTransactionNotificationAsync, test SendMessage returns MessageAck with Received=true for unknown types

### Implementation for User Story 3

- [x] T018 [US3] Implement `RelayMessageHandler` in `src/Services/Sorcha.Peer.Service/Communication/RelayMessageHandler.cs` — constructor takes RelayCommunicationService, RegisterCache, RegisterSyncBackgroundService; HandleAsync method dispatches by MessageType: REGISTER_SYNC_REQUEST reads dockets from cache and sends RegisterSyncResponse via relay (cap at MaxDockets, enforce 3MB response limit), REGISTER_SYNC_RESPONSE/TRANSACTION_DATA_RESPONSE calls CompleteCorrelation, TRANSACTION_DATA_REQUEST reads transactions and sends TransactionDataResponse via relay; HandleTransactionNotificationAsync checks subscription via GetSubscription and triggers REGISTER_SYNC_REQUEST if subscribed
- [x] T019 [US3] Implement `PeerCommunicationServiceImpl` in `src/Services/Sorcha.Peer.Service/GrpcServices/PeerCommunicationServiceImpl.cs` — extends PeerCommunication.PeerCommunicationBase; override SendMessage to switch on MessageType and dispatch to RelayMessageHandler; return MessageAck with Received=true
- [x] T020 [US3] Register `RelayMessageHandler` as singleton and `PeerCommunicationServiceImpl` as singleton in `src/Services/Sorcha.Peer.Service/Program.cs`; add `app.MapGrpcService<PeerCommunicationServiceImpl>()` to gRPC service mappings
- [x] T021 [US3] Verify T016 and T017 tests pass — all message types dispatched correctly, responses sent via relay, correlations completed

**Checkpoint**: Peers can receive and process all relay message types; sync requests served, correlations completed, notifications trigger sync

---

## Phase 6: User Story 4 — Register Sync Between NAT'd Peers (Priority: P2)

**Goal**: RegisterReplicationService falls back to batch-based relay sync when source peer has no address and no channel — pulls dockets and transactions via request/response relay messages

**Independent Test**: Write data to a register on Peer A, verify Peer B (NAT'd) can pull and cache all dockets and transactions via relay sync requests

**Depends on**: US1 (sending), US3 (receiving/responding)

### Tests for User Story 4

- [ ] T022 [P] [US4] Add relay batch sync tests to `tests/Sorcha.Peer.Service.Tests/Replication/RegisterReplicationServiceTests.cs` — test PullFullReplicaAsync uses relay batch sync when channel is null AND peer.Address is empty, test relay batch loop processes dockets into RegisterCache, test relay batch loop pulls transactions for each docket, test HasMore=true triggers subsequent requests, test timeout causes fallback to next peer, test relay path skipped when peer has address (even if no channel), test response size failure triggers retry with halved MaxDockets

### Implementation for User Story 4

- [ ] T023 [US4] Add `RelayCommunicationService` constructor dependency to `RegisterReplicationService` in `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs`
- [ ] T024 [US4] Add relay batch sync path in `PullFullReplicaAsync` — after `GetChannel` returns null, check `string.IsNullOrEmpty(sourcePeer.Address)`: if true, enter relay sync loop that sends RegisterSyncRequest via SendAndWaitAsync<RegisterSyncResponse>, processes dockets into RegisterCache (same logic as streaming path), sends TransactionDataRequest for each docket's TransactionIds, processes transactions into RegisterCache, continues loop while HasMore=true; on response size failure, retry with halved MaxDockets (minimum 1); if address not empty, skip peer (existing behavior)
- [ ] T025 [US4] Verify T022 tests pass — relay batch sync processes dockets and transactions correctly

**Checkpoint**: Register data synchronizes between NAT'd peers via relay batches

---

## Phase 7: User Story 5 — Periodic Sync Catches Missed Updates (Priority: P2)

**Goal**: Background poll periodically syncs registers from NAT'd peers as a safety net for missed notifications, with per-register semaphore guards

**Independent Test**: Temporarily disconnect a peer from relay, make register changes, reconnect, verify periodic poll catches up within one poll interval

**Depends on**: US4 (relay sync mechanism)

### Tests for User Story 5

- [ ] T026 [P] [US5] Add periodic relay poll tests to `tests/Sorcha.Peer.Service.Tests/Replication/RegisterSyncBackgroundServiceTests.cs` — test relay poll fires at configured interval, test relay poll sends sync requests to NAT'd peers only, test relay poll skips register when sync already in progress (semaphore), test relay poll stops querying after first successful peer, test per-register SemaphoreSlim prevents concurrent syncs

### Implementation for User Story 5

- [ ] T027 [US5] Add `RelayCommunicationService` and `PeerListManager` dependencies to `RegisterSyncBackgroundService` in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs`
- [ ] T028 [US5] Add per-register sync semaphores — `ConcurrentDictionary<string, SemaphoreSlim>` field on `RegisterSyncBackgroundService`, shared between periodic poll and notification-triggered sync path; acquire before sync, release after
- [ ] T029 [US5] Add periodic relay sync poll loop in `ExecuteAsync` — second `PeriodicTimer` with `RelayPollIntervalSeconds` interval; for each active subscription, find NAT'd peers (empty address) via PeerListManager, attempt relay sync via SendAndWaitAsync<RegisterSyncResponse>, process dockets + pull transactions, stop after first successful peer per register
- [ ] T030 [US5] Wire semaphore guards into RelayMessageHandler.HandleTransactionNotificationAsync (created in T018) — add ISyncGuard parameter or pass the ConcurrentDictionary<string, SemaphoreSlim> from RegisterSyncBackgroundService; acquire per-register semaphore before triggering sync, release after. Note: this modifies RelayMessageHandler.cs from US3
- [ ] T031 [US5] Verify T026 tests pass — periodic poll works with semaphore guards

**Checkpoint**: Periodic poll catches missed notifications; per-register guards prevent concurrent syncs

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, integration validation, and final cleanup

- [ ] T032 [P] Add XML documentation comments to all public methods in RelayCommunicationService, RelayMessageHandler, PeerCommunicationServiceImpl, and RelayMessages POCOs
- [ ] T033 [P] Add structured logging (ILogger) to RelayCommunicationService for relay send/receive/timeout events and RelayMessageHandler for dispatch/response events
- [ ] T034 Update `src/Services/Sorcha.Peer.Service/README.md` with relay communication documentation (relay fallback behavior, configuration, message types)
- [ ] T035 Update `.specify/MASTER-TASKS.md` to mark relay-aware communication tasks complete
- [ ] T036 Run full `dotnet test` suite and verify zero regressions, zero new warnings
- [ ] T037 Run quickstart.md verification checklist (all 10 items)
- [ ] T038 Create relay round-trip integration test in `tests/Sorcha.Peer.Service.Tests/Integration/RelayCommunicationIntegrationTests.cs` — set up two peers with mocked seed node relay channel, Peer A sends RegisterSyncRequest via relay, Peer B receives and responds with RegisterSyncResponse, verify Peer A receives correlated response with docket data

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001-T003) — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (T005-T007) — can start after T007
- **US2 (Phase 4)**: Depends on Foundational (T005-T007) — can run in parallel with US1
- **US3 (Phase 5)**: Depends on Foundational (T005-T007) — can run in parallel with US1/US2
- **US4 (Phase 6)**: Depends on US1 + US3 (needs both sending and receiving)
- **US5 (Phase 7)**: Depends on US4 (extends relay sync with periodic polling)
- **Polish (Phase 8)**: Depends on all user stories complete

### User Story Dependencies

```
Phase 1 (Setup)
    │
Phase 2 (Foundation: RelayCommunicationService)
    │
    ├── Phase 3 (US1: CommunicationProtocolManager relay) ──┐
    ├── Phase 4 (US2: TransactionDistribution relay) ───────┤
    └── Phase 5 (US3: Receive + dispatch relay messages) ───┤
                                                             │
                                              Phase 6 (US4: Register sync via relay)
                                                             │
                                              Phase 7 (US5: Periodic poll backstop)
                                                             │
                                              Phase 8 (Polish)
```

### Within Each User Story

- Tests written FIRST (fail before implementation)
- Implementation follows
- Verify tests pass after implementation

### Parallel Opportunities

- **Phase 1**: T001, T002, T003 can all run in parallel (different files)
- **Phase 2**: T004 (tests) can run in parallel with T005 (impl is sequential)
- **Phases 3-5**: US1, US2, US3 can all start in parallel after Foundation (different files, no cross-dependencies)
- **Within US3**: T016 and T017 (tests) can run in parallel; T018 and T019 (impl) are independent files
- **Phase 8**: T032, T033 can run in parallel (different concerns)

---

## Parallel Example: User Stories 1-3 After Foundation

```bash
# These three stories touch different files and can run simultaneously:

# US1 agent: CommunicationProtocolManager relay fallback
Task: "T008 — Add relay fallback tests to CommunicationProtocolManagerTests.cs"
Task: "T009 — Add RelayCommunicationService dependency to CommunicationProtocolManager"
Task: "T010 — Add relay check in SendMessageAsync"

# US2 agent: TransactionDistributionService relay fallback
Task: "T012 — Add relay fallback tests to TransactionDistributionServiceTests.cs"
Task: "T013 — Add RelayCommunicationService dependency to TransactionDistributionService"
Task: "T014 — Add relay fallback in SendToPeerAsync"

# US3 agent: Receiving side (new files, no conflicts)
Task: "T016 — Create RelayMessageHandlerTests.cs"
Task: "T017 — Create PeerCommunicationServiceImplTests.cs"
Task: "T018 — Implement RelayMessageHandler.cs"
Task: "T019 — Implement PeerCommunicationServiceImpl.cs"
```

---

## Implementation Strategy

### MVP First (User Stories 1-3: Core Relay)

1. Complete Phase 1: Setup (proto + POCOs + config)
2. Complete Phase 2: Foundation (RelayCommunicationService)
3. Complete Phases 3-5: US1 + US2 + US3 in parallel
4. **STOP and VALIDATE**: NAT'd peers can send and receive messages via relay
5. This gives full messaging relay — peers can communicate through seed node

### Incremental Delivery

1. Setup + Foundation → Relay primitive operational
2. Add US1 + US2 + US3 → Full messaging relay (MVP!)
3. Add US4 → Register data syncs between NAT'd peers
4. Add US5 → Periodic poll catches missed updates (safety net)
5. Polish → Documentation, logging, final validation

### Suggested MVP Scope

**US1 + US2 + US3** (all P1) form the natural MVP — they deliver working relay communication for all message types. US4 + US5 (P2) add register sync on top, which is valuable but not critical for basic network participation.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Verify tests fail before implementing
- Commit after each phase completion
- Stop at any checkpoint to validate story independently
- All services registered as Singleton (matches existing Peer Service pattern)
- SenderPeerId MUST be populated on all relay messages (PeerRouter rejects empty)
- Response size capped at 3MB per relay response (50-docket batches default)
- Total tasks: 38 (T001-T038)
