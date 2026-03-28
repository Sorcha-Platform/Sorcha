# Tasks: P2P Register Replication — End-to-End Transaction Sync

**Input**: Design documents from `/specs/071-p2p-register-sync/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution mandates >85% coverage on new code.

**Organization**: Tasks grouped by user story. US1–US4 are P1 (core flow); US5–US6 are P2 (live streaming, signature verification).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1–US6)
- Exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Verify prerequisites and prepare projects for new code.

- [x] T001 Verify Peer Service PostgreSQL migration is applied — run `dotnet ef database update` against PeerDbContext in `src/Services/Sorcha.Peer.Service/`
- [x] T002 [P] Verify PeerRouter test project exists at `tests/Sorcha.PeerRouter.Tests/` — create .csproj with xUnit/FluentAssertions/Moq if missing, add project reference to `src/Apps/Sorcha.PeerRouter/`
- [x] T003 [P] Verify `InternalsVisibleTo` is set in `src/Apps/Sorcha.PeerRouter/Sorcha.PeerRouter.csproj` for `Sorcha.PeerRouter.Tests`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared models and infrastructure that multiple user stories depend on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 Create `ReverseStreamEntry` model in `src/Apps/Sorcha.PeerRouter/Models/ReverseStreamEntry.cs` — fields: PeerId, ResponseStream (IServerStreamWriter\<PeerMessage\>), ConnectedAt, LastActivityAt, IsActive
- [x] T005 [P] Create `ReverseStreamManager` in `src/Apps/Sorcha.PeerRouter/Services/ReverseStreamManager.cs` — ConcurrentDictionary\<string, ReverseStreamEntry\>, methods: RegisterStream, RemoveStream, TryGetStream, GetActiveStreamCount
- [x] T006 [P] Create `ValidatorKeyCache` in `src/Services/Sorcha.Peer.Service/Replication/ValidatorKeyCache.cs` — ConcurrentDictionary\<string, ValidatorKeyEntry\> keyed by registerId, methods: TryGetKey, CacheKey, ExtractFromGenesisDocket
- [x] T007 [P] Create `DocketFinalizationRecord` model in `src/Services/Sorcha.Peer.Service/Models/DocketFinalizationRecord.cs` — fields: RegisterId, DocketNumber, DocketHash, Status (enum), AttemptedAt, FinalizedAt, ErrorMessage
- [x] T008 Register new services in `src/Services/Sorcha.Peer.Service/Program.cs` — add DI registrations for DocketFinalizationService, ValidatorKeyCache (singletons)
- [x] T009 Register ReverseStreamManager as singleton in `src/Apps/Sorcha.PeerRouter/Program.cs`

**Checkpoint**: Foundation ready — models and DI in place for all user stories.

---

## Phase 3: User Story 1 — Streaming Relay for NAT'd Peers (Priority: P1) 🎯 MVP

**Goal**: NAT'd peers establish reverse streams to PeerRouter; Router pushes relay messages down those streams.

**Independent Test**: Two peers connect to Router, Peer A sends message targeting Peer B via relay, Peer B receives it on reverse stream.

### Tests for US1

- [x] T010 [P] [US1] Test ReverseStreamManager register/remove/lookup in `tests/Sorcha.PeerRouter.Tests/Services/ReverseStreamManagerTests.cs`
- [x] T011 [P] [US1] Test RouterCommunicationService.Stream — stream establishment, message dispatch, cleanup on disconnect in `tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterCommunicationServiceStreamTests.cs`
- [x] T012 [P] [US1] Test RouterCommunicationService.SendMessage — fallback to reverse stream when recipient has empty address in `tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterCommunicationServiceRelayTests.cs`

### Implementation for US1

- [x] T013 [US1] Implement `RouterCommunicationService.Stream()` in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs` — register peer's response writer in ReverseStreamManager, read loop dispatches incoming messages to recipients via their reverse streams or direct channels
- [x] T014 [US1] Modify `RouterCommunicationService.SendMessage()` in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs` — when recipient has empty address, check ReverseStreamManager for active stream and push via responseStream.WriteAsync instead of creating direct channel
- [x] T015 [US1] Add connection pooling for non-NAT'd recipients in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs` — ConcurrentDictionary\<string, GrpcChannel\> reusing channels instead of creating per-message
- [x] T016 [US1] Add structured logging and event emission for stream lifecycle in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterCommunicationService.cs` — StreamConnected, StreamDisconnected, RelayForwarded events

**Checkpoint**: Router accepts reverse streams and can relay messages between NAT'd peers.

---

## Phase 4: User Story 2 — Register Discovery via Heartbeat Advertisements (Priority: P1)

**Goal**: Router stores register advertisements from heartbeats and relays them to other peers.

**Independent Test**: Peer A advertises register via heartbeat, Peer B's next heartbeat response includes Peer A's advertisement.

### Tests for US2

- [x] T017 [P] [US2] Test RoutingTable.UpdateAdvertisedRegisters in `tests/Sorcha.PeerRouter.Tests/Services/RoutingTableAdvertisementTests.cs`
- [x] T018 [P] [US2] Test RouterHeartbeatService processes and relays advertisements in `tests/Sorcha.PeerRouter.Tests/GrpcServices/RouterHeartbeatAdvertisementTests.cs`

### Implementation for US2

- [x] T019 [US2] Add `UpdateAdvertisedRegisters(peerId, advertisements)` method to `src/Apps/Sorcha.PeerRouter/Services/RoutingTable.cs` — replaces AdvertisedRegisters list for the specified peer entry
- [x] T020 [US2] Modify `RouterHeartbeatService.ProcessHeartbeat()` in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterHeartbeatService.cs` — extract `request.AdvertisedRegisters` and call `RoutingTable.UpdateAdvertisedRegisters()`
- [x] T021 [US2] Modify `RouterHeartbeatService` response builder in `src/Apps/Sorcha.PeerRouter/GrpcServices/RouterHeartbeatService.cs` — aggregate other healthy peers' AdvertisedRegisters from RoutingTable and include in PeerHeartbeatResponse (cap at 100 entries)

**Checkpoint**: Register advertisements flow through the Router — peers discover each other's registers via heartbeats.

---

## Phase 5: User Story 3 — Subscribe and Sync Full Register History (Priority: P1)

**Goal**: Peer Service establishes reverse stream to seed and pulls full register history via streaming relay.

**Independent Test**: Peer B subscribes to Peer A's register, full docket chain + transactions pulled via relay, subscription state reaches FullyReplicated.

### Tests for US3

- [x] T022 [P] [US3] Test RelayCommunicationService reverse stream establishment and reconnection in `tests/Sorcha.Peer.Service.Tests/Communication/ReverseStreamLifecycleTests.cs`
- [x] T023 [P] [US3] Test relay batch sync routes through reverse stream in `tests/Sorcha.Peer.Service.Tests/Replication/RelayBatchSyncTests.cs`

### Implementation for US3

- [x] T024 [US3] Add `EstablishReverseStreamAsync()` to `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs` — initiate `client.Stream()` to seed node, start background receive loop dispatching to RelayMessageHandler, keepalive ping every 30s
- [x] T025 [US3] Add reconnection with exponential backoff in `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs` — 2s, 4s, 8s, 16s, max 60s on disconnect
- [x] T026 [US3] Modify `SendViaRelayAsync()` in `src/Services/Sorcha.Peer.Service/Communication/RelayCommunicationService.cs` — prefer sending via active reverse stream; fall back to unary SendMessage if stream unavailable
- [x] T027 [US3] Modify `RegisterSyncBackgroundService.ExecuteAsync()` in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — call EstablishReverseStreamAsync on startup before processing subscriptions
- [x] T028 [US3] Verify `TryRelayBatchSyncAsync` in `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs` works over reverse stream — the relay is transparent; add logging to confirm relay path used

**Checkpoint**: Peer Service connects to Router via reverse stream and can pull full register history through the relay.

---

## Phase 6: User Story 4 — Docket-Driven Finalization to Register Storage (Priority: P1)

**Goal**: Replicated dockets trigger signature verification and write sealed transactions to Register Service.

**Independent Test**: Docket with valid signature arrives → transactions written to Register Service and queryable. Invalid signature → rejected.

### Tests for US4

- [x] T029 [P] [US4] Test ValidatorKeyCache — extract key from genesis docket, cache hit/miss in `tests/Sorcha.Peer.Service.Tests/Replication/ValidatorKeyCacheTests.cs`
- [x] T030 [P] [US4] Test DocketFinalizationService — valid signature finalized, invalid rejected, chain break detected, idempotent writes, Register Service down retry in `tests/Sorcha.Peer.Service.Tests/Replication/DocketFinalizationServiceTests.cs`

### Implementation for US4

- [x] T031 [US4] Implement `DocketFinalizationService` in `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs` — recompute docket hash via DocketHasher, verify signature via Sorcha.Cryptography, verify chain integrity (PreviousHash), call IRegisterServiceClient.WriteDocketAsync, handle idempotent writes
- [x] T032 [US4] Implement genesis key extraction in `ValidatorKeyCache` in `src/Services/Sorcha.Peer.Service/Replication/ValidatorKeyCache.cs` — on first docket for a register, extract ProposerSignature.PublicKey + Algorithm from genesis docket (DocketNumber 0)
- [x] T033 [US4] Integrate finalization into `RegisterReplicationService.RecoverFromRegisterAsync()` in `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs` — after dockets are cached during PullFullReplicaAsync, call DocketFinalizationService.FinalizeAsync for each docket in order
- [x] T034 [US4] Integrate finalization into `RelayMessageHandler` in `src/Services/Sorcha.Peer.Service/Communication/RelayMessageHandler.cs` — on docket notification receipt for subscribed register, call DocketFinalizationService.FinalizeAsync
- [x] T035 [US4] Handle cache eviction gracefully in `DocketFinalizationService` — if transaction referenced by docket is missing from cache, re-pull from source peer via relay before finalizing

**Checkpoint**: Replicated dockets are verified and finalized to Register Service storage. Transactions are queryable on the subscribing peer.

---

## Phase 7: User Story 5 — Live Transaction Streaming (Priority: P2)

**Goal**: After initial sync, new transactions propagate in near-real-time via the relay and are auto-finalized.

**Independent Test**: Submit action on Peer A → transaction + docket appear on Peer B's Register Service within 15 seconds.

### Tests for US5

- [x] T036 [P] [US5] Test live docket notification triggers finalization in `tests/Sorcha.Peer.Service.Tests/Replication/LiveFinalizationTests.cs`

### Implementation for US5

- [x] T037 [US5] Verify `SubscribeToLiveTransactionsAsync` in `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs` works over reverse stream relay — add integration logging to confirm relay path
- [x] T038 [US5] Modify `RelayMessageHandler.HandleTransactionNotificationAsync` in `src/Services/Sorcha.Peer.Service/Communication/RelayMessageHandler.cs` — on TransactionNotification for subscribed register, check for new docket and trigger finalization if present
- [x] T039 [US5] Handle stream catch-up after disconnect in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — on reverse stream re-establishment, pull dockets with version > last synced version to catch missed transactions

**Checkpoint**: Live replication works — new transactions on Peer A appear finalized on Peer B within 15 seconds.

---

## Phase 8: User Story 6 — Single Validator Signature Verification (Priority: P2)

**Goal**: Subscribing peers verify every replicated docket's signature before finalization.

**Independent Test**: Valid signature → finalized. Tampered docket → rejected with alert logged.

### Tests for US6

- [x] T040 [P] [US6] Test multi-algorithm signature verification (ED25519, P-256, RSA-4096) in `tests/Sorcha.Peer.Service.Tests/Replication/SignatureVerificationTests.cs`

### Implementation for US6

- [x] T041 [US6] Ensure DocketFinalizationService in `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs` supports all three algorithms — ED25519, NISTP256, RSA4096 via Sorcha.Cryptography wallet verification
- [x] T042 [US6] Add structured alert logging in DocketFinalizationService for rejected dockets — log docket hash, register ID, algorithm, failure reason at Warning level
- [x] T043 [US6] Handle unknown validator key — if genesis docket not yet available when a docket arrives, defer finalization and log; resolve key on next sync cycle

**Checkpoint**: All dockets verified before finalization. Invalid signatures are rejected with alerts.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, deployment, integration validation.

- [ ] T044 [P] Update `src/Apps/Sorcha.PeerRouter/README.md` with reverse-stream relay documentation — Stream RPC behavior, configuration, connection lifecycle
- [ ] T045 [P] Update `docs/reference/development-status.md` — Peer Service status to reflect P2P register sync capability
- [ ] T046 Deploy PeerRouter to Azure with `PEERROUTER__ENABLE_RELAY=true` — update Azure Container App environment variable
- [ ] T047 Create dual Docker Compose configuration for two-peer testing in `walkthroughs/DistributedRegister/` — two stacks with different PeerService__NodeId values
- [ ] T048 Run end-to-end walkthrough per `specs/071-p2p-register-sync/quickstart.md` — verify: peer registration → advertisement → discovery → subscribe → sync → finalize → live stream → query
- [ ] T049 Update `.specify/MASTER-TASKS.md` — mark P2P register sync tasks complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — Router streaming relay
- **US2 (Phase 4)**: Depends on Foundational — can run in parallel with US1 (different files)
- **US3 (Phase 5)**: Depends on US1 (needs Router Stream RPC implemented) — Peer Service reverse stream client
- **US4 (Phase 6)**: Depends on US3 (needs reverse stream for relay sync) — Docket finalization
- **US5 (Phase 7)**: Depends on US4 (needs finalization service) — Live streaming
- **US6 (Phase 8)**: Depends on US4 (extends finalization service) — can run in parallel with US5
- **Polish (Phase 9)**: Depends on all user stories

### User Story Dependencies

```
Phase 1: Setup
    ↓
Phase 2: Foundational
    ↓           ↓
Phase 3: US1   Phase 4: US2  (parallel — different projects)
    ↓
Phase 5: US3  (needs Router Stream from US1)
    ↓
Phase 6: US4  (needs relay sync from US3)
    ↓           ↓
Phase 7: US5   Phase 8: US6  (parallel — different concerns)
    ↓           ↓
Phase 9: Polish
```

### Parallel Opportunities

- **T002 + T003**: Setup tasks (different files)
- **T004 + T005 + T006 + T007**: All foundational models/services (different files, different projects)
- **T010 + T011 + T012**: US1 tests (different test files)
- **T017 + T018**: US2 tests (different test files)
- **US1 + US2**: Entirely different projects (PeerRouter vs PeerRouter, but different services)
- **US5 + US6**: Different concerns within Peer Service
- **T044 + T045**: Documentation updates (different files)

---

## Parallel Example: User Story 1

```
# Launch US1 tests in parallel:
T010: ReverseStreamManagerTests.cs
T011: RouterCommunicationServiceStreamTests.cs
T012: RouterCommunicationServiceRelayTests.cs

# Then implement sequentially:
T013: Implement Stream RPC (core)
T014: Modify SendMessage fallback (depends on T013)
T015: Connection pooling (independent)
T016: Logging and events (after T013+T014)
```

---

## Implementation Strategy

### MVP First (US1 + US2 → demonstrate relay works)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: US1 (Router streaming relay)
4. Complete Phase 4: US2 (Advertisement fix)
5. **STOP and VALIDATE**: Two peers can connect via reverse stream and discover each other's registers

### Core Replication (US3 + US4 → data flows end-to-end)

6. Complete Phase 5: US3 (Peer reverse stream client + subscription sync)
7. Complete Phase 6: US4 (Docket finalization to Register Service)
8. **STOP and VALIDATE**: Subscribing peer has finalized, queryable data

### Full Feature (US5 + US6 → live updates + security)

9. Complete Phase 7: US5 (Live streaming)
10. Complete Phase 8: US6 (Signature verification hardening)
11. Complete Phase 9: Polish + deployment + walkthrough
12. **FINAL VALIDATION**: Full quickstart.md walkthrough passes

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Constitution requires >85% test coverage on new code
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Docket contains full Transaction objects — no cache-to-docket matching needed
- Existing `WriteDocketAsync` API is the finalization write path — no new Register Service endpoints
