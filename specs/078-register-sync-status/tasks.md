# Tasks: Register Sync Status Lifecycle & UI Improvements

**Input**: Design documents from `/specs/078-register-sync-status/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — project requires >85% coverage for new code.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects needed. This phase ensures prerequisite understanding.

- [x] T001 Read existing sync state flow: `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs`, `src/Services/Sorcha.Peer.Service/Core/RegisterSyncState.cs`, `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs` (UpdateRegisterStatusAsync + UpdateSyncStateAsync)
- [x] T002 Read existing UI components: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` (notification boxes lines 59-98), `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/RegisterPolicyTab.razor`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Sync state → RegisterStatus mapping infrastructure used by all stories

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 Add sync-status reporting method to Peer Service: create `ReportSyncStatusToRegisterServiceAsync(registerId, syncState, peerConnectionActive)` in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — calls Register Service internal endpoint via IRegisterServiceClient
- [x] T004 Add internal endpoint `POST /api/internal/register-sync-status` in `src/Services/Sorcha.Register.Service/Program.cs` — receives sync state from Peer Service, maps to RegisterStatus (Subscribing→Checking, Syncing→Recovery, FullyReplicated/Active→Online, Error→Offline), calls `RegisterManager.UpdateRegisterStatusAsync()`
- [x] T005 Add `ReportSyncStatusAsync` method to `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` and implement in `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs`
- [x] T006 Wire status reporting into `ProcessSubscriptionAsync` state transitions in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — call `ReportSyncStatusToRegisterServiceAsync` on each state change (Subscribing→Syncing, Syncing→FullyReplicated, errors)
- [x] T007 [P] Write tests for sync state → RegisterStatus mapping in `tests/Sorcha.Register.Core.Tests/` — test all 5 transitions from data-model.md

**Checkpoint**: Peer sync state changes now propagate to Register Service and update RegisterStatus

---

## Phase 3: User Story 1 — Register Status Reflects Sync State (Priority: P1)

**Goal**: Subscribed registers show accurate Checking/Recovery/Online/Offline status driven by peer sync lifecycle

**Independent Test**: Subscribe to a remote register → see Checking → Recovery → Online transitions. Kill source peer → Offline after 30s. Reconnect → Checking → Recovery → Online.

### Tests for User Story 1

- [ ] T008 [P] [US1] Write tests for offline debounce logic (30s grace period, cancellation on reconnect) in `tests/Sorcha.Peer.Service.Tests/Replication/OfflineDebounceTests.cs`
- [x] T009 [P] [US1] Write tests for RegisterStatus lifecycle transitions in `tests/Sorcha.Register.Core.Tests/Managers/RegisterStatusLifecycleTests.cs`

### Implementation for User Story 1

- [x] T010 [US1] Implement offline debounce in Peer Service: add `ConcurrentDictionary<string, CancellationTokenSource>` for per-register debounce timers in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — when all source peers unreachable, start 30s timer; cancel if reconnected within window
- [x] T011 [US1] Report Offline status to Register Service when debounce timer expires in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs`
- [x] T012 [US1] Report Checking status when reconnected peer detected (debounce cancelled) in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs`
- [x] T013 [US1] Update subscription handler in `src/Services/Sorcha.Register.Service/Program.cs` (lines 256-399) — set initial register status to Checking on subscribe action, not just create stub
- [x] T014 [US1] Update register list placeholder in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor` — use RegisterStatus.Recovery (not "Subscribing" text) for subscribed-but-not-synced registers, show Checking for just-subscribed

**Checkpoint**: Register status accurately reflects sync lifecycle including offline debounce

---

## Phase 4: User Story 2 — Real-Time Register Detail Updates (Priority: P2)

**Goal**: Transaction and docket tables auto-update via SignalR, notification boxes removed

**Independent Test**: Open register detail page → submit transaction on source → see it appear in table within 3 seconds without clicking anything

### Implementation for User Story 2

- [x] T015 [US2] Remove notification box markup and counter state from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` — delete lines 59-98 (MudAlert blocks), remove `_newTransactionsCount` and `_newDocketsCount` fields
- [x] T016 [US2] Modify `OnTransactionConfirmedAsync` handler in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` — instead of incrementing counter, fetch transaction details and prepend to `_transactions` list, call `StateHasChanged()`
- [x] T017 [US2] Modify `OnDocketSealedAsync` handler in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` — fetch docket details and prepend to `_dockets` list, call `StateHasChanged()`
- [ ] T018 [US2] Add 100ms batching buffer for rapid updates in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor` — use `Timer` to collect multiple events and apply as single batch to avoid excessive re-renders
- [ ] T019 [US2] Ensure scroll position preservation — prepending to list should not change the user's current scroll offset (verify MudTable behaviour, add JS interop if needed)

**Checkpoint**: Notification boxes removed, tables update in real-time

---

## Phase 5: User Story 3 — Immediate Sync on Subscribe (Priority: P2)

**Goal**: New subscriptions trigger sync within 5 seconds instead of waiting for 5-minute timer

**Independent Test**: Subscribe to a register → observe sync begins within seconds in logs

### Implementation for User Story 3

- [x] T020 [US3] Add `ManualResetEventSlim _immediateSync` signal field to `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs`
- [x] T021 [US3] Modify `ExecuteAsync` loop in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` — wait on both `timer.WaitForNextTickAsync` and `_immediateSync.Wait` using `Task.WhenAny`, reset signal after processing
- [x] T022 [US3] Signal `_immediateSync` from `SubscribeToRegisterAsync` method in `src/Services/Sorcha.Peer.Service/Replication/RegisterSyncBackgroundService.cs` after adding subscription
- [ ] T023 [P] [US3] Write test verifying immediate sync trigger in `tests/Sorcha.Peer.Service.Tests/Replication/ImmediateSyncTriggerTests.cs`

**Checkpoint**: Subscriptions trigger sync immediately, periodic timer still handles retries

---

## Phase 6: User Story 4 — Unencrypted Register Warning (Priority: P3)

**Goal**: Dev-mode registers show warning icon + one-way encryption enable switch

**Independent Test**: View a DevMode=true register → see warning. Toggle encryption on Governance tab → confirm one-way lock.

### Tests for User Story 4

- [x] T024 [P] [US4] Write test for DevMode one-way disable in `tests/Sorcha.Register.Core.Tests/Managers/DevModeDisableTests.cs` — verify DevMode can go true→false but not false→true

### Implementation for User Story 4

- [x] T025 [P] [US4] Add warning icon to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/RegisterCard.razor` — show `Icons.Material.Filled.Warning` (amber) with `MudTooltip` "Unencrypted - update the policy to enable field-level encryption" when register DevMode=true
- [x] T026 [P] [US4] Add DevMode field to `RegisterViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/RegisterViewModel.cs` and populate from register API response
- [x] T027 [US4] Add `POST /api/registers/{registerId}/disable-dev-mode` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — RequireAdministrator auth, call `RegisterManager`, return 409 if already disabled
- [x] T028 [US4] Add `DisableDevModeAsync` to `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs` — set DevMode=false, reject if already false (idempotent), publish governance control-chain transaction
- [x] T029 [US4] Add encryption enable switch to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/RegisterPolicyTab.razor` — `MudSwitch` disabled when DevMode=false, confirmation dialog via `DialogService.ShowMessageBoxAsync` warning one-way nature and that existing transactions remain unencrypted
- [x] T030 [US4] Wire switch to call disable-dev-mode endpoint from `RegisterPolicyTab.razor` — on confirm, call API, refresh policy display, lock switch

**Checkpoint**: Unencrypted registers display warning, encryption can be enabled once (irreversible)

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T031 [P] Update `src/Services/Sorcha.Register.Service/Program.cs` — add OpenAPI `.WithSummary()` and `.WithDescription()` to new disable-dev-mode endpoint
- [x] T032 [P] Update `src/Services/Sorcha.Register.Service/Program.cs` — add OpenAPI docs to internal register-sync-status endpoint
- [x] T033 Add structured logging for all status transitions in Peer Service and Register Service
- [ ] T034 Run `scripts/check-clean-install.sh` to validate deployment after changes
- [ ] T035 Update `docs/reference/API-DOCUMENTATION.md` with new endpoints
- [ ] T036 Docker rebuild and multi-node sync test per quickstart.md test scenarios

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — read existing code
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (T003-T007)
- **US2 (Phase 4)**: Depends on Foundational — independent of US1
- **US3 (Phase 5)**: Depends on Foundational — independent of US1/US2
- **US4 (Phase 6)**: No dependency on Foundational — fully independent (DevMode is separate from sync)
- **Polish (Phase 7)**: Depends on all stories complete

### User Story Dependencies

- **US1 (P1)**: Requires foundational sync→status mapping. Core MVP.
- **US2 (P2)**: Requires only existing SignalR events. Independent of sync status work.
- **US3 (P2)**: Requires Peer Service background service modifications. Touches same file as US1 (RegisterSyncBackgroundService) — implement after US1.
- **US4 (P3)**: Fully independent — touches different files (RegisterCard, RegisterPolicyTab, RegisterManager). Can run in parallel with US1/US2/US3.

### Within Each User Story

- Tests written first (if included), verified to fail
- Backend before frontend
- Service layer before UI layer
- Commit after each task or logical group

### Parallel Opportunities

- T008 + T009 (US1 tests) can run in parallel
- T025 + T026 (US4 UI warning) can run in parallel with any other story
- US4 can be implemented entirely in parallel with US1/US2/US3
- US2 and US3 can run in parallel after US1 completes (US3 touches same file as US1)

---

## Parallel Example: User Story 4

```bash
# These tasks have no dependencies on other stories:
Task: "T024 Write DevMode one-way disable tests"
Task: "T025 Add warning icon to RegisterCard.razor"
Task: "T026 Add DevMode to RegisterViewModel"
# Then sequentially:
Task: "T027 Add disable-dev-mode endpoint"
Task: "T028 Add DisableDevModeAsync to RegisterManager"
Task: "T029 Add encryption switch to RegisterPolicyTab"
Task: "T030 Wire switch to API"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (read code)
2. Complete Phase 2: Foundational (sync→status mapping)
3. Complete Phase 3: User Story 1 (status lifecycle)
4. **STOP and VALIDATE**: Test with multi-node peer setup
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Status reporting infrastructure ready
2. Add US1 → Register statuses reflect sync state → Deploy (MVP!)
3. Add US2 → Tables auto-update in real-time → Deploy
4. Add US3 → Immediate sync on subscribe → Deploy
5. Add US4 → Encryption warnings + enable switch → Deploy
6. Each story adds value without breaking previous stories

---

## Notes

- Total tasks: 36
- US1: 7 tasks (P1 — MVP)
- US2: 5 tasks (P2)
- US3: 4 tasks (P2)
- US4: 7 tasks (P3)
- Foundational: 5 tasks
- Setup: 2 tasks
- Polish: 6 tasks
- US4 is fully parallelizable with all other stories
- RegisterSyncBackgroundService.cs is touched by US1 and US3 — implement sequentially
