# Tasks: Resilient System Register Bootstrap

**Input**: Design documents from `/specs/100-resilient-bootstrap/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the constitution requires >80% coverage and the spec explicitly references testability.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Extend configuration model and prepare the bootstrapper for mode-driven logic

- [ ] T001 Add `BootstrapMode` enum (`Auto`, `SyncOnly`, `GenesisFile`) to `src/Common/Sorcha.ServiceDefaults/SystemRegisterOptions.cs`
- [ ] T002 Add retry timing properties (`FastRetryIntervalSeconds`, `FastRetryDurationSeconds`, `BackoffIntervalSeconds`) to `SystemRegisterOptions` in `src/Common/Sorcha.ServiceDefaults/SystemRegisterOptions.cs`
- [ ] T003 Add default configuration values to `src/Services/Sorcha.Register.Service/appsettings.json` under the `SystemRegister` section: `BootstrapMode: Auto`, `FastRetryIntervalSeconds: 5`, `FastRetryDurationSeconds: 120`, `BackoffIntervalSeconds: 300`

**Checkpoint**: Configuration model compiles, options bind from appsettings.json

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Refactor bootstrapper entry point to dispatch by mode, add configuration validation

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 Add startup validation of `BootstrapMode` value in `SystemRegisterBootstrapper.ExecuteAsync()` — fail with `InvalidOperationException` if unrecognised value, in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [ ] T005 Add bootstrap mode announcement log at start of `ExecuteAsync()` — log `BootstrapMode`, `FastRetryInterval`, `BackoffInterval` as structured fields, in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [ ] T006 Refactor `ExecuteAsync()` to dispatch to mode-specific methods: `BootstrapAutoAsync()`, `BootstrapSyncOnlyAsync()`, `BootstrapGenesisFileAsync()` — extract current `BootstrapWithRetryAsync` as `BootstrapAutoAsync` with no behaviour change, in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [ ] T007 Inject `IOptions<SystemRegisterOptions>` into `SystemRegisterBootstrapper` constructor (it currently creates a scope to resolve it — make it a constructor dependency), in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`

**Checkpoint**: Bootstrapper dispatches by mode. `Auto` mode behaves identically to current code. Build succeeds.

---

## Phase 3: User Story 1 — Node Joins Existing Network (Priority: P1) MVP

**Goal**: `SyncOnly` mode retries peer sync indefinitely with two-phase backoff (fast 5s for 2 min, then 5 min polling). Never ingests genesis.

**Independent Test**: Start Register Service with `SystemRegister__BootstrapMode=SyncOnly` and no peers. Observe fast retries for 2 minutes, then transition to 5-minute polling. Bring peers online — node syncs and completes bootstrap.

### Tests for User Story 1

- [ ] T008 [P] [US1] Create test class `SystemRegisterBootstrapperSyncOnlyTests` in `tests/Sorcha.Register.Service.Tests/Services/SystemRegisterBootstrapperTests.cs` with test: `SyncOnly_NoRegisterFound_RetriesIndefinitely` — verify bootstrapper does not throw or complete within fast-retry window when register is never found
- [ ] T009 [P] [US1] Add test: `SyncOnly_RegisterFoundDuringFastRetry_CompletesBootstrap` — mock `RegisterManager.GetRegisterAsync` to return null twice then a valid register on third call, verify bootstrap completes and seeds blueprints
- [ ] T010 [P] [US1] Add test: `SyncOnly_TransitionsToBackoffPhase_AfterFastRetryDuration` — verify that after `FastRetryDurationSeconds` elapses, the retry interval changes from `FastRetryIntervalSeconds` to `BackoffIntervalSeconds`
- [ ] T011 [P] [US1] Add test: `SyncOnly_NeverIngestsGenesisFile_EvenWhenAvailable` — verify `GenesisIngestionService.LoadAndVerifyGenesisAsync` is never called in SyncOnly mode
- [ ] T012 [P] [US1] Add test: `SyncOnly_RespectsShutdownCancellation_DuringPolling` — cancel the token during a delay, verify clean exit without exception

### Implementation for User Story 1

- [ ] T013 [US1] Implement `BootstrapSyncOnlyAsync()` in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` — two-phase retry loop: fast retries every `FastRetryIntervalSeconds` for `FastRetryDurationSeconds`, then backoff to `BackoffIntervalSeconds`. Each iteration checks `RegisterManager.GetRegisterAsync()`. On register found, call `WaitForGenesisDocketAsync` and `SeedBlueprintsIfMissingAsync`
- [ ] T014 [US1] Add structured logging for SyncOnly mode in `BootstrapSyncOnlyAsync()`: log attempt count, elapsed time, next interval at `Information` during fast phase, log phase transition at `Information`, log subsequent backoff attempts at `Debug` level

**Checkpoint**: SyncOnly mode fully functional. Tests pass. Node retries indefinitely, syncs when peer available, never creates local genesis.

---

## Phase 4: User Story 2 — First Node Creates New Network (Priority: P2)

**Goal**: `GenesisFile` mode ingests genesis immediately without any peer sync attempt.

**Independent Test**: Start with `SystemRegister__BootstrapMode=GenesisFile`. Node ingests embedded genesis instantly.

### Tests for User Story 2

- [ ] T015 [P] [US2] Add test: `GenesisFile_ValidGenesis_IngestsImmediately` — verify `GenesisIngestionService.LoadAndVerifyGenesisAsync` is called without any delay or peer check
- [ ] T016 [P] [US2] Add test: `GenesisFile_GenesisFileNotFound_ThrowsWithActionableMessage` — mock `LoadAndVerifyGenesisAsync` to return null, verify `SystemRegisterBootstrapStopException` message includes the configured path
- [ ] T017 [P] [US2] Add test: `GenesisFile_InvalidSignature_ThrowsWithClearError` — mock `LoadAndVerifyGenesisAsync` to throw `InvalidOperationException`, verify it propagates as bootstrap stop

### Implementation for User Story 2

- [ ] T018 [US2] Implement `BootstrapGenesisFileAsync()` in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` — call `GenesisIngestionService.LoadAndVerifyGenesisAsync()` directly. If null, throw `SystemRegisterBootstrapStopException` with message naming the configured path (or "embedded resource"). If valid, call `IngestGenesisAsync`, then `WaitForGenesisDocketAsync` and `SeedBlueprintsIfMissingAsync`
- [ ] T019 [US2] Add structured logging for GenesisFile mode: log "Ingesting genesis file directly (BootstrapMode: GenesisFile)" at `Information` level before ingestion

**Checkpoint**: GenesisFile mode fully functional. Tests pass. Ingests immediately, fails clearly on missing/invalid genesis.

---

## Phase 5: User Story 3 — Developer Local Workflow (Priority: P3)

**Goal**: `Auto` mode (default) preserves current 14-second peer window + genesis fallback behaviour. Log clearly when falling back.

**Independent Test**: `docker-compose up` with default config — system register available within 30 seconds.

### Tests for User Story 3

- [ ] T020 [P] [US3] Add test: `Auto_DefaultBehaviour_TriesPeersThenFallsBackToGenesis` — verify the existing 3-retry exponential backoff flow, then genesis ingestion
- [ ] T021 [P] [US3] Add test: `Auto_FallbackToEmbeddedGenesis_LogsNewNetworkWarning` — verify a `Warning` level log containing "creating a new local network" when Auto mode ingests embedded genesis

### Implementation for User Story 3

- [ ] T022 [US3] Rename existing `BootstrapWithRetryAsync` to `BootstrapAutoAsync` in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` (if not done in T006)
- [ ] T023 [US3] Add warning log to `BootstrapAutoAsync` when falling back to embedded genesis: log at `Warning` level with message "Ingesting embedded genesis — creating a new local network. Set BootstrapMode to SyncOnly to join an existing network instead."

**Checkpoint**: Auto mode identical to current behaviour plus clearer logging. docker-compose workflow unaffected.

---

## Phase 6: User Story 4 — Operator Observability (Priority: P3)

**Goal**: Structured logs with phase transitions, attempt counts, and timing across all modes.

**Independent Test**: Start in SyncOnly mode, observe logs over 5 minutes — verify phase transition logged, frequency decreases.

### Tests for User Story 4

- [ ] T024 [P] [US4] Add test: `AllModes_LogBootstrapModeAtStartup` — verify each mode logs its name and strategy at `Information` level before first action
- [ ] T025 [P] [US4] Add test: `SyncOnly_LogFrequencyDecreases_AfterPhaseTransition` — verify log level drops from `Information` to `Debug` after transitioning to backoff phase

### Implementation for User Story 4

- [ ] T026 [US4] Review and consolidate all log messages across modes in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` — ensure consistent structured fields (`BootstrapMode`, `Phase`, `Attempt`, `ElapsedSeconds`, `NextRetrySeconds`) per the contracts/README.md log event contracts

**Checkpoint**: Operator can observe bootstrap progress, phase transitions, and timing from logs alone.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Configuration for production, documentation, final validation

- [ ] T027 [P] Set `SystemRegister__BootstrapMode=SyncOnly` for the Register Service in `docker-compose.n1.yml`
- [ ] T028 [P] Update `src/Services/Sorcha.Register.Service/README.md` with BootstrapMode configuration documentation
- [ ] T029 Run all tests in `tests/Sorcha.Register.Service.Tests/` and verify passing
- [ ] T030 Run quickstart.md validation — execute the three quick test scenarios (Auto, SyncOnly, GenesisFile) against a local build

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **User Stories (Phases 3-6)**: All depend on Phase 2 completion
  - US1 (SyncOnly): Independent — no dependency on other stories
  - US2 (GenesisFile): Independent — no dependency on other stories
  - US3 (Auto/default): Independent — no dependency on other stories
  - US4 (Observability): Can start after Phase 2, but benefits from US1 being done first (most log messages are in SyncOnly)
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — No dependencies on other stories
- **US2 (P2)**: Can start after Phase 2 — No dependencies on other stories
- **US3 (P3)**: Can start after Phase 2 — No dependencies on other stories (rename in T022 may overlap with T006)
- **US4 (P3)**: Can start after Phase 2 — Consolidation task (T026) benefits from US1-US3 being complete

### Within Each User Story

- Tests written first (FAIL before implementation)
- Implementation tasks in dependency order
- Commit after each task or logical group

### Parallel Opportunities

- T001, T002 can run in parallel (same file but different sections)
- All test tasks within a story (T008-T012, T015-T017, T020-T021, T024-T025) can run in parallel
- US1, US2, US3 can be implemented in parallel after Phase 2 (different code paths in same file — use careful merging)
- T027, T028 can run in parallel

---

## Parallel Example: User Story 1

```bash
# Launch all tests for US1 together:
Task: "T008 - SyncOnly_NoRegisterFound_RetriesIndefinitely"
Task: "T009 - SyncOnly_RegisterFoundDuringFastRetry_CompletesBootstrap"
Task: "T010 - SyncOnly_TransitionsToBackoffPhase_AfterFastRetryDuration"
Task: "T011 - SyncOnly_NeverIngestsGenesisFile_EvenWhenAvailable"
Task: "T012 - SyncOnly_RespectsShutdownCancellation_DuringPolling"

# Then implement:
Task: "T013 - Implement BootstrapSyncOnlyAsync"
Task: "T014 - Add structured logging for SyncOnly mode"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T007)
3. Complete Phase 3: User Story 1 — SyncOnly mode (T008-T014)
4. **STOP and VALIDATE**: Test SyncOnly independently
5. This alone solves the core problem — orphaned local genesis on fresh nodes

### Incremental Delivery

1. Setup + Foundational → Configuration model ready
2. Add US1 (SyncOnly) → Nodes can join existing networks reliably (MVP!)
3. Add US2 (GenesisFile) → Network creation is explicit and clear
4. Add US3 (Auto) → Dev workflow preserved with better logging
5. Add US4 (Observability) → Operators can monitor bootstrap progress
6. Each story adds value without breaking previous stories

---

## Notes

- All changes are in 2 source files (`SystemRegisterOptions.cs`, `SystemRegisterBootstrapper.cs`) + 1 config + 1 test file
- US1-US3 modify the same file (`SystemRegisterBootstrapper.cs`) but different methods — parallel work possible with care
- The test file is new (`SystemRegisterBootstrapperTests.cs`) — all test tasks write to the same file
- Estimated total: ~250 lines new/modified code, ~300 lines tests
