# Tasks: Envelope Encryption Integration

**Input**: Design documents from `/specs/052-encryption-integration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/operations-api.md

**Tests**: Included — the spec targets >85% coverage and the plan explicitly lists test files.

**Organization**: Tasks are grouped by user story (vertical slices) to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Verify existing infrastructure and create shared foundational models needed by multiple stories

- [X] T001 Verify YARP gateway route covers `GET /api/operations` list endpoint in src/Services/Sorcha.ApiGateway/ configuration
- [X] T002 [P] Add `OperationId` (string?) and `IsAsync` (bool) properties to `ActionSubmissionResultViewModel` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Workflows/ActionSubmissionResultViewModel.cs
- [X] T003 [P] Add `TransactionHash` (string?), `BlueprintId` (string?), `ActionTitle` (string?), `CreatedAt` (DateTimeOffset), `CompletedAt` (DateTimeOffset?) properties to `EncryptionOperationViewModel` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EncryptionOperationModels.cs
- [X] T004 [P] Create `OperationHistoryItem` and `OperationHistoryPage` models in src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/OperationHistoryModels.cs

---

## Phase 2: Foundational (Backend List Endpoint)

**Purpose**: Backend changes needed for operations history (US4). Does NOT block US1, US2, US3, US5, or US6.

**Blocks**: US4 (Operations History) only. All other stories can proceed in parallel after Setup.

- [X] T005 Add `GET /api/operations` list endpoint that queries `IActivityEventStore` for completed/failed encryption operations and merges with in-memory active operations from `IEncryptionOperationStore.GetByWalletAddressAsync`, with `wallet`, `page`, `pageSize` query parameters, wallet ownership validation, and Scalar docs in src/Services/Sorcha.Blueprint.Service/Endpoints/OperationsEndpoints.cs
- [X] T006 Write tests for the list endpoint (happy path, pagination, wallet auth, empty results, merge of active + completed operations) in tests/Sorcha.Blueprint.Service.Tests/Endpoints/OperationsEndpointTests.cs

**Checkpoint**: Backend list endpoint functional — US4 (History) can proceed

---

## Phase 3: User Story 1 — Action Submission with Encryption Progress (Priority: P1) MVP

**Goal**: Bridge the core gap — detect async responses from action submission and display the existing EncryptionProgressIndicator inline on MyActions page

**Independent Test**: Submit an action on a multi-recipient blueprint → progress indicator appears → updates through 4 stages → shows transaction reference on completion

### Tests for User Story 1

- [X] T007 [P] [US1] Write tests for `ActionSubmissionResultViewModel` verifying `OperationId`/`IsAsync` properties, computed `HasAsyncOperation` in tests/Sorcha.UI.Core.Tests/Models/ActionSubmissionResultViewModelTests.cs
- [X] T008 [P] [US1] Write tests for `WorkflowService.SubmitActionExecuteAsync` verifying async response maps `OperationId`/`IsAsync` from HTTP 202 response in tests/Sorcha.UI.Core.Tests/Services/WorkflowServiceTests.cs

### Implementation for User Story 1

- [X] T009 [US1] Update `WorkflowService.SubmitActionExecuteAsync` to map `OperationId` and `IsAsync` from the Blueprint Service response into `ActionSubmissionResultViewModel` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WorkflowService.cs
- [X] T010 [US1] Update `EncryptionProgressIndicator.razor` to accept `OperationId` parameter, add `OnCompleted` EventCallback, and add `OnFailed` EventCallback in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor
- [X] T011 [US1] Wire `EncryptionProgressIndicator` into `MyActions.razor` — detect `IsAsync` on submission result, show inline progress indicator with `OperationId`, handle completion (refresh action list) and failure callbacks in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor
- [X] T012 [US1] Write component integration tests for EncryptionProgressIndicator (renders with operationId, shows stages, calls OnCompleted) in tests/Sorcha.UI.Core.Tests/Components/EncryptionProgressIndicatorTests.cs

**Checkpoint**: User Story 1 complete — submitting an async action shows real-time progress inline

---

## Phase 4: User Story 2 — Encryption Failure and Retry (Priority: P2)

**Goal**: When encryption fails, show error details and a retry button that re-submits the original action without re-entering data

**Independent Test**: Simulate encryption failure → error message with affected recipient displayed → click retry → new operation starts and completes

**Depends on**: Phase 3 (US1) — EncryptionProgressIndicator must exist with callbacks

### Tests for User Story 2

- [X] T013 [P] [US2] Write tests for retry flow — EncryptionProgressIndicator shows retry button on failure, retry triggers new submission with original request data, verify failed action remains in pending list in tests/Sorcha.UI.Core.Tests/Components/EncryptionProgressIndicatorTests.cs

### Implementation for User Story 2

- [X] T014 [US2] Add `OriginalRequest` (ActionExecuteRequest?) parameter to `EncryptionProgressIndicator.razor` — when provided and operation fails, render a "Retry" button in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor
- [X] T015 [US2] Implement retry handler in `EncryptionProgressIndicator.razor` — on retry click, call `WorkflowService.SubmitActionExecuteAsync` with original request, reset progress state with new `OperationId` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor
- [X] T016 [US2] Pass `OriginalRequest` from `MyActions.razor` to `EncryptionProgressIndicator` when showing progress after async submission in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor

**Checkpoint**: User Story 2 complete — failed operations show error + retry button, retry works without re-entry

---

## Phase 5: User Story 6 — Real-Time Progress via Push Updates (Priority: P6)

**Goal**: Add SignalR encryption event handlers to ActionsHubConnection so progress updates arrive via push instead of polling, with automatic polling fallback

**Independent Test**: Connect to ActionsHub → submit action → verify WebSocket messages arrive for each stage → disconnect SignalR → verify polling fallback activates

**Depends on**: Phase 3 (US1) — EncryptionProgressIndicator must exist
**Note**: Implemented before US3 because US3 (notifications) depends on SignalR handlers

### Tests for User Story 6

- [ ] T017 [P] [US6] Write tests for ActionsHubConnection encryption event handlers — `EncryptionProgress`, `EncryptionComplete`, `EncryptionFailed` handler registration and callback invocation in tests/Sorcha.UI.Core.Tests/Services/ActionsHubConnectionTests.cs (new or modify existing)
- [ ] T018 [P] [US6] Write tests for EncryptionProgressIndicator SignalR mode — prefers SignalR over polling, falls back to polling when disconnected in tests/Sorcha.UI.Core.Tests/Components/EncryptionProgressIndicatorTests.cs

### Implementation for User Story 6

- [X] T019 [US6] Add `OnEncryptionProgress`, `OnEncryptionComplete`, `OnEncryptionFailed` event handler registration methods to `ActionsHubConnection` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs
- [X] T020 [US6] Update `EncryptionProgressIndicator.razor` to subscribe to ActionsHubConnection encryption events — use SignalR when connected, fall back to polling timer when disconnected, handle reconnection in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor

**Checkpoint**: User Story 6 complete — progress updates arrive via push (<1s latency), polling fallback works

---

## Phase 6: User Story 3 — Cross-Page Completion Notifications (Priority: P3)

**Goal**: Users receive toast notifications when background encryption operations complete, regardless of which page they're on. Navigation warning banner shown when leaving MyActions during encryption.

**Independent Test**: Start encryption → navigate to Dashboard → wait for completion → toast notification appears with transaction reference

**Depends on**: Phase 5 (US6) — SignalR handlers needed for notification listener

### Tests for User Story 3

- [ ] T021 [P] [US3] Write tests for `OperationNotificationListener` — subscribes to EventsHub, shows snackbar on completion/failure, navigates on click in tests/Sorcha.UI.Core.Tests/Components/OperationNotificationListenerTests.cs
- [ ] T022 [P] [US3] Write tests for `NotificationService` EventsHub integration — verify `EncryptionOperationCompleted` event is sent via EventsHub on operation completion and failure in tests/Sorcha.Blueprint.Service.Tests/Services/NotificationServiceTests.cs

### Implementation for User Story 3

- [ ] T023 [US3] Update `NotificationService` in Blueprint Service to send `EncryptionOperationCompleted` event via EventsHub (user-scoped group) when encryption completes or fails, in addition to existing ActionsHub events in src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs
- [ ] T024 [US3] Create `OperationNotificationListener.razor` component — subscribes to EventsHub `EncryptionOperationCompleted` events, shows MudBlazor `ISnackbar` toast with operation result and navigation link in src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OperationNotificationListener.razor
- [ ] T025 [US3] Add `OperationNotificationListener` to `MainLayout.razor` so it is active on all pages in src/Apps/Sorcha.UI/Sorcha.UI.Web/Components/Layout/MainLayout.razor
- [X] T026 [US3] Add navigation warning banner to `MyActions.razor` — when encryption is in progress and user navigates away, show non-blocking `MudAlert` informing them they'll be notified on completion in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor

**Checkpoint**: User Story 3 complete — toast notifications work cross-page, navigation warning displays

---

## Phase 7: User Story 4 — Operations History (Priority: P4)

**Goal**: Paginated operations history page showing all encryption operations for the user's wallet, with status, timestamps, and drill-down

**Independent Test**: Submit several actions (mix success/failure) → navigate to Operations page → see paginated list with correct statuses → click completed → see transaction reference → click failed → see error + retry

**Depends on**: Phase 2 (Foundational — list endpoint) and Phase 3 (US1 — core models)

### Tests for User Story 4

- [ ] T027 [P] [US4] Write tests for `OperationStatusService.ListOperationsAsync` — pagination, mapping, empty results in tests/Sorcha.UI.Core.Tests/Services/OperationStatusServiceTests.cs

### Implementation for User Story 4

- [ ] T028 [US4] Add `ListOperationsAsync(string walletAddress, int page, int pageSize)` to `IOperationStatusService` interface and implement in `OperationStatusService` — call `GET /api/operations` endpoint, return `OperationHistoryPage` in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IOperationStatusService.cs and src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/OperationStatusService.cs
- [ ] T029 [US4] Create `Operations.razor` page — `@page "/operations"`, inject `IOperationStatusService`, display `MudTable` with pagination, status icons (spinner/check/X), timestamps, drill-down to transaction reference or error details with retry button in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Operations.razor
- [ ] T030 [US4] Add "Operations" navigation entry to the sidebar/nav menu in src/Apps/Sorcha.UI/Sorcha.UI.Web/Components/Layout/NavMenu.razor

**Checkpoint**: User Story 4 complete — operations history page shows all operations with pagination and drill-down

---

## Phase 8: User Story 5 — CLI Async Operation Support (Priority: P5)

**Goal**: CLI `action execute` command that submits actions with blocking progress display (default) and `--no-wait` non-blocking mode

**Independent Test**: Run `sorcha action execute` against multi-recipient blueprint → see Spectre.Console progress → get transaction reference on completion. Run with `--no-wait` → get operation ID immediately.

**Depends on**: None (fully independent — can run in parallel with any phase after Setup)

### Tests for User Story 5

- [X] T031 [P] [US5] Write tests for `ActionCommand` — argument parsing (blueprint, action, instance, wallet, register, payload, --no-wait), help text, validation in tests/Sorcha.Cli.Tests/Commands/ActionCommandTests.cs

### Implementation for User Story 5

- [X] T032 [P] [US5] Create CLI action request/response DTOs (`ActionExecuteCliRequest`, `ActionExecuteCliResponse`) in src/Apps/Sorcha.Cli/Models/ActionModels.cs
- [X] T033 [US5] Add `SubmitActionAsync` method to `IBlueprintServiceClient` Refit interface in src/Apps/Sorcha.Cli/Services/IBlueprintServiceClient.cs
- [X] T034 [US5] Create `ActionCommand` with `execute` subcommand — options for `--blueprint`, `--action`, `--instance`, `--wallet`, `--register`, `--payload` (JSON string), `--no-wait` flag in src/Apps/Sorcha.Cli/Commands/ActionCommands.cs
- [X] T035 [US5] Implement blocking mode handler — call `SubmitActionAsync`, if `IsAsync` then poll `GetOperationStatusAsync` with `Spectre.Console` `AnsiConsole.Progress()` showing stages, exit 0 on success / non-zero on failure in src/Apps/Sorcha.Cli/Commands/ActionCommands.cs
- [X] T036 [US5] Implement `--no-wait` mode handler — call `SubmitActionAsync`, print `OperationId`, print hint to use `sorcha operation status <id>`, exit 0 in src/Apps/Sorcha.Cli/Commands/ActionCommands.cs

**Checkpoint**: User Story 5 complete — CLI supports both blocking and non-blocking async action execution

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and quality improvements across all stories

- [ ] T037 Update API documentation for new `GET /api/operations` list endpoint in docs/reference/API-DOCUMENTATION.md
- [ ] T038 [P] Update development status to reflect encryption integration completion in docs/reference/development-status.md
- [ ] T039 [P] Update MASTER-TASKS.md task status for Feature 052 in .specify/MASTER-TASKS.md
- [ ] T040 Run quickstart.md validation scenarios end-to-end (all 7 scenarios from specs/052-encryption-integration/quickstart.md)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup (T004 for history models). Only blocks US4.
- **US1 (Phase 3)**: Depends on Setup (T002-T003) — core flow bridge
- **US2 (Phase 4)**: Depends on US1 (Phase 3) — needs EncryptionProgressIndicator with callbacks
- **US6 (Phase 5)**: Depends on US1 (Phase 3) — needs EncryptionProgressIndicator to add SignalR
- **US3 (Phase 6)**: Depends on US6 (Phase 5) — needs SignalR handlers for notification listener
- **US4 (Phase 7)**: Depends on Foundational (Phase 2) + US1 (Phase 3) — needs list endpoint + models
- **US5 (Phase 8)**: Independent — can run in parallel with ANY phase after Setup
- **Polish (Phase 9)**: Depends on all user stories being complete

### User Story Dependencies

```
Phase 1 (Setup) ──────────────────────────────┐
                                                │
Phase 2 (Foundational) ───────────────┐        │
         (only blocks US4)            │        │
                                      │        │
Phase 3 (US1: Core Flow) ◄───────────┤        │
         │                            │        │
         ├──► Phase 4 (US2: Retry)    │        │
         ├──► Phase 5 (US6: SignalR)  │        │
         │         │                  │        │
         │         └──► Phase 6 (US3) │        │
         └──► Phase 7 (US4) ◄─────────┘        │
                                                │
Phase 8 (US5: CLI) ◄───────────────────────────┘
         (fully independent)
```

### Parallel Opportunities

**After Setup completes**:
- US5 (CLI) can start immediately — fully independent
- US1 (Core Flow) + Foundational can proceed in parallel

**After US1 completes**:
- US2 (Retry), US6 (SignalR), and US4 (History, if Foundational done) can all start in parallel
- US5 (CLI) continues independently

**After US6 completes**:
- US3 (Notifications) can start

---

## Parallel Example: Maximum Parallelism

```
Wave 1 (after Setup):
  Agent A: Phase 2 (Foundational — backend list endpoint)
  Agent B: Phase 3 (US1 — core flow bridge)
  Agent C: Phase 8 (US5 — CLI, fully independent)

Wave 2 (after US1 completes):
  Agent A: Phase 4 (US2 — retry)
  Agent B: Phase 5 (US6 — SignalR push)
  Agent C: Phase 7 (US4 — history, needs Foundational + US1)

Wave 3 (after US6 completes):
  Agent A: Phase 6 (US3 — cross-page notifications)

Wave 4:
  All: Phase 9 (Polish)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T004)
2. Complete Phase 3: US1 Core Flow (T007-T012)
3. **STOP and VALIDATE**: Submit action → see progress → see completion
4. This alone bridges the core gap and delivers primary user value

### Incremental Delivery

1. Setup + US1 → Core encryption visibility (MVP)
2. + US2 → Retry on failure (resilience)
3. + US6 → Real-time push updates (performance)
4. + US3 → Cross-page notifications (UX polish)
5. + US4 → Operations history (audit/troubleshooting)
6. + US5 → CLI support (automation/scripting)
7. Polish → Documentation and validation

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Tests are included per the >85% coverage target in plan.md
- Timer disposal guard pattern must be followed in all components with timers
- Use `JsonDefaults.Api` for all HTTP response deserialization in UI services
- Use `Uri.EscapeDataString()` for path/query segments in raw HttpClient URLs
- FR-014 (idempotency) and FR-015 (size limit fail-fast) are pre-existing backend behaviors per spec Assumptions — no new tasks needed
- Commit after each phase checkpoint
