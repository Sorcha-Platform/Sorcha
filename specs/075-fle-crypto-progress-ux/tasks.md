# Tasks: FLE Completion & Crypto Progress UX

**Input**: Design documents from `/specs/075-fle-crypto-progress-ux/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Included — CLAUDE.md mandates >85% coverage; spec US3/US4 are explicitly about closing test gaps.

**Organization**: Tasks grouped by user story (6 stories across P1/P2/P3 priority). US1 and US2 are co-P1 and tightly coupled (UI needs backend events). US3/US4 are independent test-gap work. US5 extends US1. US6 is Docker E2E.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Add new models and interfaces that multiple user stories depend on.

- [x] T001 [P] Add `RecipientProgress` model and `RecipientProgressStatus` enum to `src/Common/Sorcha.TransactionHandler/Encryption/Models/EncryptionModels.cs` — fields: WalletAddress, DisplayName, DisclosedFields, GroupId, Status, ErrorMessage
- [x] T002 [P] Add `DisplayName` nullable string property to `RecipientInfo` in `src/Common/Sorcha.TransactionHandler/Encryption/Models/EncryptionModels.cs`
- [x] T003 [P] Add `RecipientProgress[]` property to `EncryptionResult` in `src/Common/Sorcha.TransactionHandler/Encryption/Models/EncryptionModels.cs`
- [x] T004 [P] Add `RecipientEncryptionNotification` record to `src/Services/Sorcha.Blueprint.Service/Models/EncryptionNotifications.cs` — fields: OperationId, RecipientName, RecipientIndex, TotalRecipients, DisclosedFieldsSummary, Status, PipelineStep, ErrorMessage, Timestamp
- [x] T005 [P] Add `RecipientOperationStatus` record and `Recipients[]` property to `EncryptionOperation` in `src/Services/Sorcha.Blueprint.Service/Models/EncryptionOperationModels.cs`
- [x] T006 Verify solution builds cleanly after model changes: `dotnet build`

**Checkpoint**: All new models compile. No behaviour change yet.

---

## Phase 2: Foundational (Backend Per-Recipient Events)

**Purpose**: Wire per-recipient progress events through the encryption pipeline. US1 (UI) and US5 (errors) both depend on this.

**CRITICAL**: US1 cannot show per-recipient progress without these events.

- [x] T007 Populate `RecipientProgress[]` in `EncryptionPipelineService.EncryptGroupAsync()` per-recipient loop — track each recipient's status during key wrapping at `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- [x] T008 Populate `DisplayName` on `RecipientInfo` from participant data during `ActionExecutionService.ResolveRecipientKeysAsync()` — use instance bindings participant names, fallback to truncated wallet at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [x] T009 Add `NotifyRecipientProgressAsync` method to `INotificationService` and implement in `NotificationService` — send `RecipientEncryptionNotification` to `wallet:{address}` group on ActionsHub at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs`
- [x] T010 Emit per-recipient events from `EncryptionBackgroundService` after each disclosure group is processed — iterate `RecipientProgress[]` from result and call `NotifyRecipientProgressAsync` for each at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [x] T011 Update `InMemoryEncryptionOperationStore` to accept per-recipient status updates — add `UpdateRecipientStatus` method that populates `Recipients[]` on `EncryptionOperation` at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs` (integrated into T010)
- [x] T012 Add `RecipientEncryptionProgress` event handler registration on `ActionsHubConnection` — `OnRecipientProgress` event matching existing pattern (OnEncryptionProgress, etc.) at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs`
- [x] T013 Add `RecipientEncryptionProgressUpdate` model to UI at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EncryptionHubModels.cs` — matching the backend notification contract

**Checkpoint**: Backend emits per-recipient events via SignalR. UI hub connection can receive them. Polling endpoint returns per-recipient status.

---

## Phase 3: User Story 2 — Backend Per-Recipient Events (Priority: P1)

**Goal**: Verify per-recipient events are emitted correctly and the polling endpoint includes recipient status.

**Independent Test**: Submit an action with 3+ recipients, subscribe to ActionsHub, verify 3 individual recipient events received plus existing step events.

### Tests for User Story 2

- [ ] T014 [P] [US2] Unit test for per-recipient progress population in `EncryptionPipelineService` — verify `RecipientProgress[]` populated with correct wallet, name, fields, status after encryption at `tests/Sorcha.TransactionHandler.Tests/Encryption/RecipientProgressTests.cs`
- [ ] T015 [P] [US2] Unit test for `NotifyRecipientProgressAsync` — verify event sent to correct wallet group with correct payload at `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionNotificationTests.cs` (extend existing file)
- [ ] T016 [P] [US2] Unit test for per-recipient status in polling endpoint — verify `GET /api/operations/{id}` returns `Recipients[]` with per-recipient state at `tests/Sorcha.Blueprint.Service.Tests/Endpoints/OperationRecipientStatusTests.cs`

### Implementation for User Story 2

- [ ] T017 [US2] Verify end-to-end: submit action via integration test, assert both step-level and recipient-level events emitted in correct order at `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionBackgroundServiceRecipientTests.cs`

**Checkpoint**: Per-recipient events verified via tests. Polling endpoint returns recipient status. Existing step events unchanged.

---

## Phase 4: User Story 1 — Per-Recipient Progress Popover (Priority: P1)

**Goal**: Floating popover shows per-recipient progress with task-oriented language. Supports expanded/minimised/dismissed states. Persists across navigation.

**Independent Test**: Submit action on non-DevMode register with 3 recipients, verify popover appears with per-recipient status transitions, minimise/dismiss/navigate away — all work.

### Tests for User Story 1

- [ ] T018 [P] [US1] Unit test for `EncryptionOperationTracker` — verify operation lifecycle (track → progress → complete), recipient state transitions, multiple concurrent operations at `tests/Sorcha.UI.Core.Tests/Services/EncryptionOperationTrackerTests.cs`

### Implementation for User Story 1

- [x] T019 [P] [US1] Create `EncryptionOperationState` and `RecipientDisplayState` models at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Encryption/EncryptionOperationState.cs` — per data-model.md
- [x] T020 [P] [US1] Create `PopoverState` enum (Expanded, Minimised, Dismissed) at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Encryption/PopoverState.cs`
- [x] T021 [US1] Create `IEncryptionOperationTracker` interface and `EncryptionOperationTracker` implementation — scoped service subscribing to `ActionsHubConnection` events, tracking active operations, exposing `ActiveOperations` dictionary and change events at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EncryptionOperationTracker.cs`
- [x] T022 [US1] Register `IEncryptionOperationTracker` as scoped in DI at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`
- [x] T023 [US1] Create `CryptoProgressPopover.razor` component — floating panel (bottom-right, ~340px) with three states: expanded (recipient list with status), minimised (compact pill), dismissed (hidden). Uses task-oriented language per FR-007 at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`
- [x] T024 [US1] Implement disclosed fields summary formatting in `CryptoProgressPopover` — `["/*"]` → "all fields", otherwise strip `/` prefix and join with commas at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EncryptionOperationTracker.cs`
- [x] T025 [US1] Implement minimise/dismiss transitions — minimise collapses to pill with "Securing — 2/3 recipients" + mini progress bar; dismiss hides panel and triggers `ISnackbar` toast on completion at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`
- [x] T026 [US1] Implement multi-operation support — badge counter when >1 active operation, click cycles through operations per FR-010 at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`
- [x] T027 [US1] Add `<CryptoProgressPopover />` to MainLayout alongside existing global components at `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`
- [x] T028 [US1] Wire `NewSubmissionDialog` to call `IEncryptionOperationTracker.TrackOperation()` on async submission result (HTTP 202) instead of showing inline `EncryptionProgressIndicator` at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/NewSubmissionDialog.razor`
- [x] T029 [US1] Implement success state: "Submission secured — N recipients can now access their disclosed fields" with "View transaction →" link navigating to transaction explorer at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`

**Checkpoint**: Floating popover shows per-recipient progress. Minimise/dismiss/navigate all work. Success links to transaction explorer.

---

## Phase 5: User Story 5 — Actionable Error Feedback (Priority: P2)

**Goal**: Encryption failures show which recipient failed and why, with a retry action.

**Independent Test**: Simulate key resolution failure for one recipient, verify popover/toast shows the failing recipient's name with retry button.

### Implementation for User Story 5

- [x] T030 [US5] Implement error state in `CryptoProgressPopover` — show failing recipient (red), error message, "Retry" and "Details" links per FR-009 at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`
- [x] T031 [US5] Implement retry action — resubmit original `ActionExecuteRequest` via `IWorkflowService`, start new operation tracking, reset recipient list at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`
- [x] T032 [US5] Implement error toast when panel is dismissed — show "Encryption failed — Could not resolve key for [recipient]" via `ISnackbar` with Retry action at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Encryption/CryptoProgressPopover.razor`

**Checkpoint**: Error states are actionable. Retry works. Error toast appears when dismissed.

---

## Phase 6: User Story 3 — DevMode Unit Tests (Priority: P2)

**Goal**: Close test coverage gaps for DevMode per-register feature (spec 065 US3).

**Independent Test**: Run unit tests for DevMode initiation, toggle, and plaintext path — all pass without Docker.

### Tests for User Story 3

- [x] T033 [P] [US3] Unit test for DevMode register initiation — verify `devMode: true` flows through `InitiateRegisterCreationRequest` → `PendingRegistration` → `Register.DevMode` at `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterInitiateDevModeTests.cs`
- [x] T034 [P] [US3] Unit test for DevMode toggle endpoint — verify `PUT /api/registers/{id}/devmode` updates flag, requires CanManageRegisters, returns 404 for unknown register at `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterDevModeToggleTests.cs`
- [x] T035 [P] [US3] Unit test for plaintext path selection — verify DevMode register skips encryption pipeline, non-DevMode calls encryption, DevMode read path applies disclosure filtering at `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionDevModeTests.cs`

**Checkpoint**: DevMode has >85% test coverage. All 3 test files pass.

---

## Phase 7: User Story 4 — FLE Unit Tests (Priority: P2)

**Goal**: Close test coverage gaps for field-level encryption (spec 065 US4).

**Independent Test**: Run unit tests for disclosure group encryption and recipient key resolution — all pass without Docker.

### Tests for User Story 4

- [x] T036 [P] [US4] Unit test for disclosure group encryption — verify identical fields → 1 group, different fields → N groups, atomic failure identifies failing recipient at `tests/Sorcha.TransactionHandler.Tests/Encryption/DisclosureGroupEncryptionTests.cs`
- [x] T037 [P] [US4] Unit test for recipient key resolution — verify instance binding keys, register published keys, revoked participant failure, mixed sources at `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionEncryptionTests.cs`

**Checkpoint**: FLE encryption pipeline has >85% test coverage.

---

## Phase 8: User Story 6 — Docker E2E Validation (Priority: P3)

**Goal**: Full encryption round-trip validated end-to-end in Docker Compose.

**Independent Test**: Run Docker E2E suite with non-DevMode register, verify encrypted storage and per-participant decryption.

### Implementation for User Story 6

- [ ] T038 [US6] Create Docker E2E test for encrypted payload flow — submit action on non-DevMode register, verify `ContentEncoding: "encrypted"` in MongoDB, query as citizen (only `/decision`), query as ID Department (all fields), query as unauthorised wallet (empty) at `tests/Sorcha.UI.E2E.Tests/Docker/EncryptedPayloadFlowTests.cs`
- [ ] T039 [US6] Create Docker E2E test for DevMode flow — submit action on DevMode register, verify plaintext in MongoDB, verify disclosure filtering at read time at `tests/Sorcha.UI.E2E.Tests/Docker/DevModePayloadFlowTests.cs`
- [ ] T040 [US6] Run E2E suite twice consecutively to verify idempotency: `docker-compose down -v && docker-compose up -d && dotnet test --filter "Category=LongRunning"` (repeat)

**Checkpoint**: Full encrypt/decrypt round-trip proven in Docker. Idempotent.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: GAP-005, documentation, cleanup, regression validation.

- [ ] T041 [P] Complete GAP-005: EncryptionProgress SignalR integration test — verify progress, complete, and failed events delivered via ActionsHub to correct wallet group, including new per-recipient events at `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionNotificationTests.cs`
- [ ] T042 [P] Mark `EncryptionProgressIndicator.razor` as deprecated with comment — keep file but remove all usages from pages at `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor`
- [ ] T043 [P] Update `docs/reference/development-status.md` — mark FLE and DevMode encryption as complete
- [ ] T044 [P] Update `blueprints/README.md` — document that walletAddress is optional on participants
- [ ] T045 [P] Update `.specify/MASTER-TASKS.md` — mark GAP-005 complete, update FLE status
- [ ] T046 [P] Add XML documentation comments and Scalar OpenAPI annotations (WithSummary, WithDescription) to modified endpoints (DevMode toggle, operations polling) per Constitution Principle III
- [ ] T047 Run full test suite: `dotnet test` — verify zero regressions across all 30 test projects (SC-007)
- [ ] T048 Run quickstart.md validation scenarios end-to-end (7 scenarios)
- [ ] T049 [US1] Playwright E2E test for popover states — verify expanded panel appears on submit, minimise collapses to pill, dismiss hides panel, navigate away preserves panel, success toast appears after dismiss (SC-008) at `tests/Sorcha.UI.E2E.Tests/Docker/CryptoProgressPopoverTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ──→ Phase 2 (Foundational) ──BLOCKS──→ All User Stories
                                                          │
                                                          ├── Phase 3 (US2: Backend Events) ──→ Phase 4 (US1: UI Popover)
                                                          │                                          │
                                                          │                                          └── Phase 5 (US5: Errors)
                                                          │
                                                          ├── Phase 6 (US3: DevMode Tests) ── independent
                                                          │
                                                          ├── Phase 7 (US4: FLE Tests) ── independent
                                                          │
                                                          └── Phase 8 (US6: Docker E2E) ── after US3 + US4

Phase 9 (Polish) ── after all desired stories complete
```

### User Story Dependencies

- **US2 (P1)**: Can start after Foundational (Phase 2) — backend events, no UI dependency
- **US1 (P1)**: Depends on US2 (needs per-recipient events to drive the popover)
- **US5 (P2)**: Depends on US1 (extends the popover with error states)
- **US3 (P2)**: Can start after Phase 1 — independent test-gap work, no dependency on US1/US2
- **US4 (P2)**: Can start after Phase 1 — independent test-gap work, no dependency on US1/US2
- **US6 (P3)**: Best after US3 + US4 (tests validate the code that E2E exercises)

### Within Each User Story

- Tests MUST be written and FAIL before implementation (where test tasks precede implementation)
- Models → Services → Components → Integration
- Core implementation before wiring/integration

### Parallel Opportunities

- **Phase 1**: T001-T005 all [P] — different files/projects
- **Phase 2**: T012 + T013 [P] with T007-T011 (UI models while backend wires)
- **Phase 3 (US2)**: T014-T016 all [P] — different test files
- **Phase 4 (US1)**: T019 + T020 [P] — different model files; T018 [P] with implementation
- **Phase 6 (US3)**: T033-T035 all [P] — different test projects
- **Phase 7 (US4)**: T036 + T037 [P] — different test projects
- **Cross-story**: US3 and US4 can run in parallel with US2 and US1 (different services)
- **Phase 9**: T041-T046 all [P] — different files

---

## Parallel Example: After Foundational Phase

```bash
# Launch independent workstreams in parallel:
Agent 1: US2 (Backend Events) — touches TransactionHandler, Blueprint.Service, NotificationService
Agent 2: US3 (DevMode Tests) — touches Register.Service.Tests, Blueprint.Service.Tests
Agent 3: US4 (FLE Tests) — touches TransactionHandler.Tests, Blueprint.Service.Tests

# After US2 completes:
Agent 1: US1 (UI Popover) — touches Sorcha.UI.Core, MainLayout

# After US1 completes:
Agent 1: US5 (Error Feedback) — extends CryptoProgressPopover
```

## Parallel Example: Phase 1 (Setup)

```bash
# Launch all model changes in parallel (different files):
Task: "Add RecipientProgress model to EncryptionModels.cs"
Task: "Add RecipientEncryptionNotification to EncryptionNotifications.cs"
Task: "Add RecipientOperationStatus to EncryptionOperationModels.cs"
```

---

## Implementation Strategy

### MVP First (US2 + US1 Only)

1. Complete Phase 1: Setup (6 tasks)
2. Complete Phase 2: Foundational backend events (7 tasks)
3. Complete Phase 3: US2 tests (4 tasks)
4. Complete Phase 4: US1 popover (12 tasks)
5. **STOP AND VALIDATE**: Submit action, verify per-recipient popover, minimise, dismiss, navigate
6. This alone delivers the core UX improvement — users see who gets what

### Incremental Delivery

1. Setup + Foundational → Backend per-recipient events ready
2. US2 → Backend verified with tests → Events flowing
3. US1 → **MVP: Per-recipient progress popover** — core UX improvement
4. US5 → Actionable error feedback — production-quality error handling
5. US3 → DevMode test coverage — regression safety net
6. US4 → FLE test coverage — encryption pipeline safety net
7. US6 → Docker E2E — full stack validation
8. Polish → Docs, GAP-005, cleanup, regression check

### Parallel Team Strategy

With multiple agents after Phase 2:

1. All complete Setup + Foundational together
2. Once Foundational done:
   - Agent A: US2 (backend events) → US1 (UI) → US5 (errors) — sequential chain
   - Agent B: US3 (DevMode tests) + US4 (FLE tests) — independent test work
3. After all above:
   - Agent A: US6 (Docker E2E)
   - Agent B: Polish
4. Final: Regression validation

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks in the same phase
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Foundational phase
- All new public types MUST have XML doc comments (Constitution III)
- All new endpoints MUST have `.WithSummary()` and `.WithDescription()` (Constitution III)
- License header required on all new files: `// SPDX-License-Identifier: MIT` + `// Copyright (c) 2026 Sorcha Contributors`
- Commit after each task or logical group; reference task IDs in commits
- Total tasks: **49**
- US1: 13 tasks | US2: 4 tasks | US3: 3 tasks | US4: 2 tasks | US5: 3 tasks | US6: 3 tasks
- Setup: 6 tasks | Foundational: 7 tasks | Polish: 9 tasks
