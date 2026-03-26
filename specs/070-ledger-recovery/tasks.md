# Tasks: Blueprint Service Ledger Recovery & Register Status Sync

**Input**: Design documents from `/specs/070-ledger-recovery/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: US1 (published blueprints survive restart) and US2 (register status) are both P1 and tightly coupled — recovery queries registers and populates both the blueprint store and status model. They share a phase. US3 (readiness gating) depends on the recovery service existing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Verify branch, ensure build green.

- [ ] T001 Verify branch `070-ledger-recovery` is checked out and solution builds with `dotnet build --force`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add models shared by multiple user stories — RegisterHealthStatus, RecoveryState, and the Register Service endpoint.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T002 [P] Create `RegisterHealthStatus` enum (Unknown, Online, Offline, Degraded) in `src/Services/Sorcha.Blueprint.Service/Models/RegisterHealthStatus.cs` with XML docs and license header
- [ ] T003 [P] Create `RegisterRecoveryState` class in `src/Services/Sorcha.Blueprint.Service/Models/RecoveryState.cs` — fields: RegisterId, RegisterName, Status (RegisterHealthStatus), Height, LastCheckedAt, LastSuccessAt, ConsecutiveFailures, RecoveredBlueprintCount, ErrorMessage. Also add `RecoveryState` class with IsComplete, StartedAt, CompletedAt, RegisterStates dictionary.
- [ ] T004 Create `RecoveryOptions` configuration class in `src/Services/Sorcha.Blueprint.Service/Models/RecoveryOptions.cs` — RefreshIntervalSeconds (default 60), StartupTimeoutSeconds (default 30), MaxRetryAttempts (default 3). Register in DI from appsettings section "Recovery".
- [ ] T005 Add `GET /api/registers/{registerId}/blueprints/published` endpoint in `src/Services/Sorcha.Register.Service/Endpoints/RegisterEndpoints.cs` (or new file) — query all Control transactions where metadata `transactionType == "BlueprintPublish"`, return list with blueprintId, transactionId, publishedBy, publishedAt, blueprintJson, plus registerHeight and queriedAt. Use existing `IRegisterRepository.QueryTransactionsAsync()`.
- [ ] T006 [P] Unit tests for the published blueprints endpoint in `tests/Sorcha.Register.Service.Tests/PublishedBlueprintsEndpointTests.cs` — test: returns published blueprints, empty register returns empty list, non-existent register returns 404

**Checkpoint**: Foundation ready — models, configuration, and Register Service endpoint available.

---

## Phase 3: User Story 1 + 2 — Published Blueprints Survive Restart & Register Status (Priority: P1)

**Goal**: On startup, Blueprint Service queries all known registers for published blueprints and rebuilds InMemoryPublishedBlueprintStore. Register health status is tracked per-register.

**Independent Test**: Publish a blueprint, restart Blueprint Service, verify blueprint appears in available blueprints and register shows as online.

### Tests for User Story 1 + 2

- [ ] T007 [P] [US1] Unit tests for `BlueprintRecoveryService` in `tests/Sorcha.Blueprint.Service.Tests/BlueprintRecoveryServiceTests.cs` — test: recovers blueprints from single register, recovers from multiple registers, handles unreachable register gracefully (marks offline, doesn't throw), idempotent (same transactions produce same state), version ordering (latest version wins), empty register produces no blueprints
- [ ] T008 [P] [US2] Unit tests for register health status transitions in `tests/Sorcha.Blueprint.Service.Tests/RegisterHealthStatusTests.cs` — test: Unknown → Online on success, Unknown → Offline on failure, Online → Offline on failure, Offline → Online on success (recovers blueprints), consecutive failure counter increments/resets

### Implementation for User Story 1 + 2

- [ ] T009 [US1] Create `BlueprintRecoveryService : BackgroundService` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintRecoveryService.cs` — on `ExecuteAsync`: (1) query Register Service `GET /api/registers` for all registers, (2) for each register call `GET /api/registers/{id}/blueprints/published`, (3) deserialize blueprint JSON and add to `InMemoryPublishedBlueprintStore`, (4) update `RegisterRecoveryState` per register, (5) set `RecoveryState.IsComplete = true`. Inject `IRegisterServiceClient`, `IPublishedBlueprintStore`, `IOptions<RecoveryOptions>`, `ILogger`.
- [ ] T010 [US1] Add service client method `GetPublishedBlueprintsAsync(string registerId)` to `IRegisterServiceClient` in `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` and implement in the concrete client — calls the new endpoint from T005.
- [ ] T011 [US2] Expose `RecoveryState` as a singleton in DI — register in `Program.cs`. The recovery service writes to it; health check and other consumers read from it.
- [ ] T012 [US1] Register `BlueprintRecoveryService` as a hosted service in `src/Services/Sorcha.Blueprint.Service/Program.cs` — `builder.Services.AddHostedService<BlueprintRecoveryService>()`. Bind `RecoveryOptions` from configuration.

**Checkpoint**: After restart, published blueprints are recovered from the ledger and register status is tracked.

---

## Phase 4: User Story 3 — Service Readiness Gating (Priority: P2)

**Goal**: Health check returns 503 "recovering" until recovery completes, then 200 "healthy" with register status metrics.

**Independent Test**: Restart Blueprint Service, immediately hit health check — should return 503. Wait for recovery, then verify 200 with register metrics.

### Tests for User Story 3

- [ ] T013 [P] [US3] Unit tests for health check gating in `tests/Sorcha.Blueprint.Service.Tests/HealthCheckGatingTests.cs` — test: returns 503 when recovery incomplete, returns 200 after recovery completes, includes register status in response, partial recovery (some offline) still returns 200

### Implementation for User Story 3

- [ ] T014 [US3] Update health endpoint in `src/Services/Sorcha.Blueprint.Service/Program.cs` (`/api/health`) — inject `RecoveryState` singleton, check `IsComplete`. If false: return 503 with recovery progress (registersTotal, registersRecovered, registersOffline). If true: return 200 with existing metrics plus register summary (total, online, offline, lastRefresh).

**Checkpoint**: Health check gates traffic until recovery completes.

---

## Phase 5: User Story 4 — Graceful Degradation (Priority: P2)

**Goal**: Unreachable registers are skipped, service becomes ready with recovered data from reachable registers, unreachable registers retried on background timer.

**Independent Test**: Start with one register offline, verify service becomes ready from other registers. Bring offline register up, verify its blueprints appear after retry.

### Implementation for User Story 4

- [ ] T015 [US4] Add retry logic to `BlueprintRecoveryService` — after initial recovery completes, schedule retries for registers with `Status == Offline`. Use configurable delay from `RecoveryOptions`. On successful retry, add recovered blueprints and update status to Online. Cap retries at `MaxRetryAttempts` then log warning and stop retrying (will be picked up by periodic refresh).

**Checkpoint**: Service handles partial availability gracefully.

---

## Phase 6: User Story 5 — Background Periodic Refresh (Priority: P3)

**Goal**: During normal operation, periodically re-check register status and discover newly published blueprints.

**Independent Test**: With service running, publish a new blueprint via API. Verify it appears within the refresh interval.

### Implementation for User Story 5

- [ ] T016 [US5] Add periodic timer to `BlueprintRecoveryService.ExecuteAsync()` — after initial recovery, enter a `while (!stoppingToken.IsCancellationRequested)` loop with `Task.Delay(RefreshInterval)`. Each tick: re-query all registers, update status, add any new published blueprints not already in the store. Log changes (new blueprints discovered, status transitions).
- [ ] T017 [P] [US5] Unit test for periodic refresh in `tests/Sorcha.Blueprint.Service.Tests/BlueprintRecoveryServiceTests.cs` — test: discovers newly published blueprint after refresh, detects register going offline during runtime, configurable refresh interval is respected

**Checkpoint**: Newly published blueprints are discovered within the refresh interval.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and cleanup.

- [ ] T018 Run `dotnet build --force` and `dotnet test` across all affected test projects — verify no regressions
- [ ] T019 Integration test: publish a blueprint via walkthrough setup, restart Blueprint Service container (`docker-compose restart blueprint-service`), verify blueprint appears in `GET /api/actions/{wallet}/{register}/blueprints` without re-publishing
- [ ] T020 Update `walkthroughs/ConstructionPermit/README.md` — note that blueprint state now survives container restarts via ledger recovery
- [ ] T021 Add structured logging throughout `BlueprintRecoveryService` — log recovery start/complete, per-register success/failure, blueprint counts, refresh ticks, status transitions

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1+US2 (Phase 3)**: Depends on Phase 2 (models + endpoint)
- **US3 (Phase 4)**: Depends on Phase 3 (needs RecoveryState to exist)
- **US4 (Phase 5)**: Depends on Phase 3 (extends BlueprintRecoveryService)
- **US5 (Phase 6)**: Depends on Phase 3 (extends BlueprintRecoveryService)
- **Polish (Phase 7)**: Depends on all desired phases

### User Story Dependencies

```
Phase 2 (Foundation)
    └── US1+US2 (Phase 3: Recovery + Status) ─── P1
            ├── US3 (Phase 4: Health Gating) ──── P2
            ├── US4 (Phase 5: Degradation) ────── P2
            └── US5 (Phase 6: Refresh) ─────────── P3
```

### Parallel Opportunities

- **Phase 2**: T002, T003 [P] — different files; T006 [P] tests can run alongside
- **Phase 3**: T007, T008 [P] — different test files; then T009→T010→T011→T012 sequentially
- **Phase 4 + 5**: US3 and US4 can run in parallel (health check vs retry logic — different concerns in different files)
- **Phase 6**: T016 and T017 [P] — implementation and test in different files

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundation (models + endpoint)
3. Complete Phase 3: US1+US2 — Recovery + register status
4. **STOP and VALIDATE**: `docker-compose restart blueprint-service` → verify blueprints recovered
5. Deploy/demo

### Incremental Delivery

1. Foundation + US1+US2 → **Blueprints survive restart** (MVP!)
2. + US3 → Health check gates traffic during recovery
3. + US4 → Graceful handling of offline registers
4. + US5 → Runtime discovery of new blueprints

---

## Notes

- US1 and US2 are merged into one phase because the recovery logic naturally produces both (query registers → get blueprints + status)
- The `BlueprintRecoveryService` is a single hosted service that handles startup recovery (US1/US2), retry (US4), and periodic refresh (US5). Tasks build on it incrementally.
- The Register Service endpoint (T005) is the critical path — without it, recovery has no data source.
- Constitution requires >85% test coverage — tests included in each phase.
