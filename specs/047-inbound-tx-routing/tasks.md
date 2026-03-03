# Tasks: Inbound Transaction Routing & User Notification

**Input**: Design documents from `/specs/047-inbound-tx-routing/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — plan.md targets >85% coverage for new code.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Proto files, enums, gRPC client interfaces, configuration — enables all subsequent phases

- [X] T001 Add register_address.proto from specs/047-inbound-tx-routing/contracts/ to src/Services/Sorcha.Register.Service/Protos/ and configure .csproj for server-side protobuf generation
- [X] T002 [P] Add wallet_notification.proto from specs/047-inbound-tx-routing/contracts/ to src/Services/Sorcha.Wallet.Service/Protos/ and configure .csproj for server-side protobuf generation
- [X] T003 [P] Add docket_sync.proto from specs/047-inbound-tx-routing/contracts/ to src/Services/Sorcha.Peer.Service/Protos/ and configure .csproj for server-side protobuf generation
- [X] T004 [P] Create NotificationMethod.cs (InApp=0, InAppPlusEmail=1, InAppPlusPush=2) and NotificationFrequency.cs (RealTime=0, HourlyDigest=1, DailyDigest=2) enums in src/Services/Sorcha.Tenant.Service/Models/
- [X] T005 [P] Create IRegisterAddressClient.cs, IWalletNotificationClient.cs, and IDocketSyncClient.cs gRPC client interfaces in src/Common/Sorcha.ServiceClients/Grpc/
- [X] T006 Register new gRPC clients (RegisterAddress, WalletNotification, DocketSync) in src/Common/Sorcha.ServiceClients/Extensions/ServiceClientExtensions.cs
- [X] T007 [P] Add BloomFilter and Recovery configuration sections to src/Services/Sorcha.Register.Service/appsettings.json and Notifications section to src/Services/Sorcha.Wallet.Service/appsettings.json per quickstart.md

**Checkpoint**: Proto compilation succeeds, gRPC clients registered, configuration in place

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Database migration and solution-wide build verification — MUST complete before user stories

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T008 Add NotificationMethod and NotificationFrequency properties to UserPreferences entity with defaults (InApp, RealTime) and create EF Core migration in src/Services/Sorcha.Tenant.Service/Data/Migrations/
- [X] T009 Build entire solution to verify proto compilation, gRPC client registration, and migration compatibility

**Checkpoint**: Foundation ready — all proto-generated code compiles, migration applies, user story implementation can begin

---

## Phase 3: User Story 1 — Local Address Registration (Priority: P1) 🎯 MVP

**Goal**: Register local wallet addresses in a Redis bloom filter so inbound transactions can be identified as local. Rebuild on startup and admin command.

**Independent Test**: Create a wallet → verify address in bloom filter → restart node → confirm index rebuilt → trigger admin rebuild via REST → verify rebuild completes

### Implementation for User Story 1

- [X] T010 [US1] Create ILocalAddressIndex.cs interface (Add, MayContain, Rebuild, GetStats) in src/Services/Sorcha.Register.Service/Services/Interfaces/
- [X] T011 [US1] Implement RedisBloomFilterAddressIndex.cs using StackExchange.Redis SETBIT/GETBIT with 10 hash functions (MurmurHash3), sizing for 100K addresses at <0.1% FP rate, Redis key namespace register:bloom:{registerId}. Rebuild MUST be atomic: build new filter under a temp key (register:bloom:{registerId}:rebuild), then RENAME to swap — ensures concurrent MayContain calls see either old or new filter, never empty. In src/Services/Sorcha.Register.Service/Services/Implementation/
- [X] T012 [US1] Implement RegisterAddressGrpcService.cs with RegisterLocalAddress, RemoveLocalAddress, and RebuildAddressIndex RPCs using ILocalAddressIndex and IWalletNotificationClient in src/Services/Sorcha.Register.Service/GrpcServices/
- [X] T013 [P] [US1] Create IAddressRegistrationService.cs interface in src/Services/Sorcha.Wallet.Service/Services/Interfaces/
- [X] T014 [US1] Implement AddressRegistrationService.cs — on wallet create/delete, calls Register Service gRPC RegisterLocalAddress/RemoveLocalAddress via IRegisterAddressClient in src/Services/Sorcha.Wallet.Service/Services/Implementation/
- [X] T015 [US1] Implement GetAllLocalAddresses server-streaming endpoint in WalletNotificationGrpcService.cs — streams all active wallet addresses from IWalletRepository for bloom filter rebuild in src/Services/Sorcha.Wallet.Service/GrpcServices/
- [X] T016 [US1] Register ILocalAddressIndex, RegisterAddressGrpcService in Register Service DI (Program.cs)
- [X] T017 [US1] Register IAddressRegistrationService, WalletNotificationGrpcService in Wallet Service DI (Program.cs)
- [X] T018 [US1] Create admin REST endpoint POST /api/admin/registers/{registerId}/rebuild-index in Register Service Program.cs (follows existing inline endpoint pattern). Add YARP route in API Gateway for /api/admin/registers/* path. Include .WithSummary() and .WithDescription() per constitution
- [X] T019 [P] [US1] Write RedisBloomFilterAddressIndexTests.cs — Add, MayContain, Rebuild (verify atomic swap via RENAME), false positive rate validation, hash distribution, concurrent MayContain during rebuild in tests/Sorcha.Register.Service.Tests/
- [X] T020 [P] [US1] Write AddressRegistrationServiceTests.cs — register on wallet create, remove on wallet delete, gRPC client call verification in tests/Sorcha.Wallet.Service.Tests/

**Checkpoint**: Wallet addresses automatically register in bloom filter. Index rebuilds on startup and admin command (both gRPC and REST). Rebuild is atomic — no missed transactions during rebuild. All US1 tests pass.

---

## Phase 4: User Story 2 — Inbound Action Notification (Priority: P1)

**Goal**: When a stored transaction matches a local address via bloom filter, resolve the owning user and deliver a real-time notification via SignalR EventsHub with blueprint context. Includes batch endpoint for recovery use.

**Independent Test**: Submit a transaction targeting a local address → verify SignalR notification arrives within 5 seconds with correct blueprint name, action description, sender name, and navigation link

**Depends on**: US1 (bloom filter must be operational)

### Implementation for User Story 2

- [X] T021 [US2] Create IInboundTransactionRouter.cs interface in src/Services/Sorcha.Register.Service/Services/Interfaces/
- [X] T022 [US2] Implement InboundTransactionRouter.cs — checks each recipient address against ILocalAddressIndex bloom filter, filters action-type transactions only (skip control/docket/participant types), on match calls Wallet Service NotifyInboundTransaction gRPC via IWalletNotificationClient in src/Services/Sorcha.Register.Service/Services/Implementation/
- [X] T023 [P] [US2] Create INotificationDeliveryService.cs and INotificationRateLimiter.cs interfaces in src/Services/Sorcha.Wallet.Service/Services/Interfaces/
- [X] T024 [US2] Implement NotificationRateLimiter.cs — sliding window rate limiter (10 notifications/min/user) using Redis INCR with TTL, overflow returns rate-limited status for digest routing in src/Services/Sorcha.Wallet.Service/Services/Implementation/
- [X] T025 [US2] Implement NotificationDeliveryService.cs — resolves address→wallet→user via IWalletRepository.GetByAddressAsync, checks NotificationPreference from Tenant Service (if email/push is configured but transport unavailable, deliver in-app and log warning), publishes real-time notifications via Redis pub/sub channel (wallet:notifications) for EventsHub bridge, or queues to Redis sorted set (wallet:digest:{userId}) for digest in src/Services/Sorcha.Wallet.Service/Services/Implementation/
- [X] T026 [US2] Implement NotifyInboundTransaction endpoint in WalletNotificationGrpcService.cs — receives matched transaction data from Register Service, delegates to INotificationDeliveryService, returns NotificationDelivery status in src/Services/Sorcha.Wallet.Service/GrpcServices/
- [X] T027 [US2] Implement NotifyInboundTransactionBatch endpoint in WalletNotificationGrpcService.cs — processes batch of matched transactions (used during recovery mode), delegates each to INotificationDeliveryService, returns aggregate counts (delivered/queued/rate-limited/no-user) in src/Services/Sorcha.Wallet.Service/GrpcServices/
- [X] T028 [US2] Create EventsHubNotificationBridge.cs — IHostedService in Blueprint Service that subscribes to Redis pub/sub channel (wallet:notifications) and pushes InboundActionEvent payloads via IHubContext<EventsHub> to target user's SignalR group. Before pushing, enriches the event: resolves blueprint_id → blueprint name and action_id → action description via IBlueprintStore (owned by Blueprint Service), resolves sender_address → sender display name via IParticipantServiceClient (falls back to raw address if participant not found), and constructs navigation path (/blueprints/{id}/instances/{instanceId}/actions/{actionId}) in src/Services/Sorcha.Blueprint.Service/Services/Implementation/
- [X] T029 [US2] Register IInboundTransactionRouter in Register Service DI, INotificationDeliveryService + INotificationRateLimiter in Wallet Service DI, EventsHubNotificationBridge as hosted service in Blueprint Service DI (Program.cs for each)
- [X] T030 [P] [US2] Write InboundTransactionRouterTests.cs — bloom filter match triggers gRPC call, no match skips, non-action type filtered out, multiple recipients with one local match in tests/Sorcha.Register.Service.Tests/
- [X] T031 [P] [US2] Write NotificationDeliveryServiceTests.cs — real-time delivery path, digest queue routing, rate-limited overflow to digest, no-user-found handling, preference lookup, email/push fallback to in-app in tests/Sorcha.Wallet.Service.Tests/
- [X] T032 [P] [US2] Write NotificationRateLimiterTests.cs — under limit allows, at limit blocks, window slides after TTL, per-user isolation in tests/Sorcha.Wallet.Service.Tests/

**Checkpoint**: Inbound action transactions trigger real-time user notifications via SignalR with enriched content (blueprint name, action description, sender name or address, navigation link). Batch endpoint ready for recovery. Rate limiting caps at 10/min/user with overflow to digest. All US2 tests pass.

---

## Phase 5: User Story 3 — Notification Preferences (Priority: P2)

**Goal**: Users configure notification method (in-app/email/push) and frequency (real-time/hourly/daily) via settings UI. Preferences stored in Tenant Service, respected by notification pipeline.

**Independent Test**: Open settings → change notification frequency to daily digest → save → verify preference persists on page reload → new users default to real-time in-app

**Can run parallel with**: US4

### Implementation for User Story 3

- [X] T033 [US3] Extend UserPreferenceEndpoints.cs PATCH handler to accept and validate NotificationMethod and NotificationFrequency fields with FluentValidation, update GET response to include new fields in src/Services/Sorcha.Tenant.Service/Endpoints/UserPreferenceEndpoints.cs
- [X] T034 [US3] Create NotificationPreferencesPanel.razor component — notification method dropdown (InApp/InApp+Email/InApp+Push) and frequency radio buttons (RealTime/Hourly/Daily) with save/cancel, wired to PATCH /api/users/preferences in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Settings/
- [X] T035 [P] [US3] Add notification preference i18n keys (section title, method labels, frequency labels, descriptions, save confirmation) to en.json, fr.json, de.json, es.json in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/i18n/
- [X] T036 [P] [US3] Write NotificationPreferenceTests.cs — PATCH updates method/frequency, GET returns saved values, defaults for new users (InApp+RealTime), invalid enum rejected in tests/Sorcha.Tenant.Service.Tests/

**Checkpoint**: Users can view and change notification preferences in Settings. Defaults work for new users. All US3 tests pass.

---

## Phase 6: User Story 4 — Register Recovery & Sync (Priority: P2)

**Goal**: On startup, detect docket gaps between local state and network head, stream missing dockets from Peer Service, process bloom filter matches for catch-up notifications, expose health endpoint for sync status monitoring.

**Independent Test**: Stop node → submit transactions to network → restart → verify recovery activates → missing dockets stream in → catch-up notifications delivered → GET /health/sync returns synced

**Depends on**: US1 (bloom filter), US2 (notification delivery pipeline + batch endpoint)

### Implementation for User Story 4

- [X] T037 [US4] Implement DocketSyncGrpcService.cs — GetLatestDocketNumber (unary, queries local MongoDB for latest docket) and SyncDockets (server streaming from from_docket_number+1 to head with max_count flow control) in src/Services/Sorcha.Peer.Service/GrpcServices/
- [X] T038 [US4] Create IRegisterRecoveryService.cs interface in src/Services/Sorcha.Register.Service/Services/Interfaces/
- [X] T039 [US4] Implement RegisterRecoveryService.cs — IHostedService that on startup compares local latest docket vs network head via IDocketSyncClient, enters recovery if gap detected, streams missing dockets, verifies chain integrity (previous_docket_hash linkage), runs bloom filter check on recovered transaction recipients, sends batch notification via IWalletNotificationClient.NotifyInboundTransactionBatch (T027), tracks RecoveryState in Redis hash (register:recovery:{registerId}) in src/Services/Sorcha.Register.Service/Services/Implementation/
- [X] T040 [US4] Create RecoveryHealthEndpoints.cs — GET /health/sync returning JSON with status (recovering/synced/stalled), current docket, target docket, progress percentage, dockets processed, last error, staleness check (<10s). Include .WithSummary() and .WithDescription() per constitution in src/Services/Sorcha.Register.Service/Endpoints/
- [X] T041 [US4] Register RegisterRecoveryService as hosted service and map RecoveryHealthEndpoints in Register Service DI (Program.cs), register DocketSyncGrpcService in Peer Service DI (Program.cs)
- [X] T042 [P] [US4] Write RegisterRecoveryServiceTests.cs — gap detection triggers recovery, no gap skips, streaming processes dockets sequentially, bloom filter match during recovery generates batch notification, state transitions (Synced→Recovering→Synced, Recovering→Stalled), retry on peer failure in tests/Sorcha.Register.Service.Tests/
- [X] T043 [P] [US4] Write DocketSyncGrpcServiceTests.cs — latest docket query returns correct number, streaming returns dockets in range, empty range returns no entries, max count limits stream, network unavailable handling in tests/Sorcha.Peer.Service.Tests/

**Checkpoint**: Recovery mode auto-detects gaps on startup, catches up from peers with chain integrity verification, delivers missed notifications via batch endpoint. Health endpoint reports accurate status. All US4 tests pass.

---

## Phase 7: User Story 5 — Digest Notification Batching (Priority: P3)

**Goal**: Batch accumulated action events per user, deliver consolidated digest notifications at configured intervals (hourly/daily), group by blueprint, skip empty digests.

**Independent Test**: Set hourly digest → send 5 transactions over 30 min → verify single consolidated notification at hour boundary grouped by blueprint → verify no notification if no events in window

**Depends on**: US2 (notification pipeline queues events to digest), US3 (frequency preference determines schedule)

### Implementation for User Story 5

- [X] T044 [US5] Implement NotificationDigestWorker.cs — BackgroundService with configurable timer (DigestCheckIntervalMinutes), scans all users with pending digest items in Redis sorted sets (wallet:digest:{userId}), groups events by blueprint, delivers consolidated notification via Redis pub/sub (wallet:notifications) for EventsHub push, removes processed events atomically (Lua script ZRANGEBYSCORE+ZREMRANGEBYSCORE), skips empty digests in src/Services/Sorcha.Wallet.Service/Services/Implementation/
- [X] T045 [US5] Register NotificationDigestWorker as hosted service in Wallet Service DI (Program.cs)
- [X] T046 [P] [US5] Write NotificationDigestWorkerTests.cs — timer fires and processes pending digests, events grouped by blueprint with counts, empty digest suppressed, atomic dequeue (no double delivery), flush pending on preference change to real-time in tests/Sorcha.Wallet.Service.Tests/

**Checkpoint**: Digest users receive single consolidated notifications at configured intervals. Empty digests suppressed. All US5 tests pass.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Observability, documentation, gateway routing, E2E testing

- [X] T047 Add structured logging (ILogger) for bloom filter operations, notification delivery, recovery progress and OpenTelemetry metrics (Meter) for bloom_filter_hit_rate, notification_delivery_latency_ms, recovery_dockets_per_second across Register and Wallet services
- [X] T048 [P] Add YARP route for /health/sync endpoint in API Gateway configuration (src/Services/Sorcha.ApiGateway/)
- [X] T049 [P] Update Register Service README.md with bloom filter config, recovery endpoints, admin rebuild endpoint, and health check; update Wallet Service README.md with notification pipeline, digest config, and rate limiting
- [X] T050 [P] Write NotificationPreferencesTests.cs Playwright E2E — navigate to Settings, change notification method and frequency, verify save persists, verify defaults for new user in tests/Sorcha.UI.E2E.Tests/
- [X] T051 Run quickstart.md scenarios 1–5 to validate end-to-end integration across all services

**Checkpoint**: All observability in place, documentation updated, E2E tests pass, quickstart scenarios validated.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational — 🎯 MVP target
- **US2 (Phase 4)**: Depends on US1 (bloom filter required for routing)
- **US3 (Phase 5)**: Depends on Foundational only — can run PARALLEL with US2 and US4
- **US4 (Phase 6)**: Depends on US1 + US2 (recovery uses bloom filter + notification pipeline including batch endpoint)
- **US5 (Phase 7)**: Depends on US2 + US3 (digest needs notification pipeline + frequency preference)
- **Polish (Phase 8)**: Depends on all user stories complete

### Dependency Graph

```
Phase 1 (Setup)
    │
    ▼
Phase 2 (Foundational)
    │
    ├───────────────────────────────┐
    ▼                               ▼
Phase 3 (US1) ──────┐        Phase 5 (US3) ─────┐
    │                │              │              │
    ▼                │              │              │
Phase 4 (US2) ──────┼──────────────┘              │
    │                │                             │
    ├────────────────┘                             │
    ▼                                              │
Phase 6 (US4)                                      │
    │                                              │
    │              Phase 7 (US5) ◄─────────────────┘
    │                   │
    ▼                   ▼
         Phase 8 (Polish)
```

### User Story Dependencies

- **US1 (P1)**: After Foundational — no other story dependencies
- **US2 (P1)**: After US1 — needs bloom filter for transaction matching
- **US3 (P2)**: After Foundational — independent of US1/US2, can start early
- **US4 (P2)**: After US2 — needs full notification pipeline (incl. batch endpoint T027) for catch-up notifications
- **US5 (P3)**: After US2 + US3 — needs pipeline to queue events and preferences for schedule

### Within Each User Story

- Interfaces before implementations
- Core implementations before gRPC service endpoints
- Service registrations (DI) after all implementations in that story
- Tests can run parallel with DI registration (different files)

### Parallel Opportunities

- **Phase 1**: T002, T003, T004, T005, T007 all parallel with T001 (different files/services)
- **Phase 3 (US1)**: T013 parallel with T010–T012 (different service); T019, T020 parallel with T016–T018
- **Phase 4 (US2)**: T023 parallel with T021–T022 (different service); T030, T031, T032 all parallel
- **Phase 5 (US3)**: T035, T036 parallel with T033–T034; entire Phase 5 parallel with Phase 4/6
- **Phase 6 (US4)**: T042, T043 parallel (different test projects)
- **Phase 7 (US5)**: T046 parallel with T045
- **Phase 8**: T048, T049, T050 all parallel with T047

---

## Parallel Example: User Story 1

```bash
# After T009 (Foundational checkpoint), launch in parallel:
Agent 1: "Create ILocalAddressIndex.cs interface in Register Service" (T010)
Agent 2: "Create IAddressRegistrationService.cs interface in Wallet Service" (T013)

# After T011-T012 (Register core), launch in parallel:
Agent 1: "Implement AddressRegistrationService.cs in Wallet Service" (T014)
Agent 2: "Implement WalletNotificationGrpcService.cs in Wallet Service" (T015)

# After T016-T018 (DI + admin endpoint), launch tests in parallel:
Agent 1: "Write RedisBloomFilterAddressIndexTests.cs" (T019)
Agent 2: "Write AddressRegistrationServiceTests.cs" (T020)
```

## Parallel Example: User Story 2

```bash
# Launch interface tasks in parallel across services:
Agent 1: "Create IInboundTransactionRouter.cs in Register Service" (T021)
Agent 2: "Create INotificationDeliveryService.cs + INotificationRateLimiter.cs in Wallet Service" (T023)

# After implementations, launch all tests in parallel:
Agent 1: "Write InboundTransactionRouterTests.cs" (T030)
Agent 2: "Write NotificationDeliveryServiceTests.cs" (T031)
Agent 3: "Write NotificationRateLimiterTests.cs" (T032)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (proto files, clients, config)
2. Complete Phase 2: Foundational (EF migration, build verification)
3. Complete Phase 3: User Story 1 — Local Address Registration
4. **STOP and VALIDATE**: Bloom filter works, addresses register/remove, rebuild operates (both gRPC and REST admin endpoint)
5. Delivers: "The system knows which addresses are local"

### Incremental Delivery

1. Setup + Foundational → Infrastructure ready
2. **US1** → Address registration works → **MVP!**
3. **US2** → Real-time notifications for inbound actions (+ batch endpoint for recovery) → **Core value delivered**
4. **US3** (parallel with US4) → User-configurable preferences
5. **US4** (parallel with US3) → Recovery catches up missed notifications
6. **US5** → Digest batching for high-volume users
7. Polish → Observability, docs, E2E tests

### File Conflict Awareness

These files are touched by multiple user stories (must be sequential):
- `WalletNotificationGrpcService.cs`: US1 (T015), US2 (T026, T027)
- `Wallet Service Program.cs`: US1 (T017), US2 (T029), US5 (T045)
- `Register Service Program.cs`: US1 (T016), US2 (T029), US4 (T041)

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable at its checkpoint
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Spec targets >85% test coverage for new code — tests included per user story
- Proto files are copied from specs/contracts/ to service Protos/ directories
- Redis pub/sub (wallet:notifications channel) bridges Wallet Service → Blueprint Service for SignalR push
- Redis sorted sets (wallet:digest:{userId}) store pending digest events
- EventsHubNotificationBridge enriches events before SignalR push: resolves blueprint name, action description, sender display name (with address fallback), and navigation path
