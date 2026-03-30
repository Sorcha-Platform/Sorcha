# Tasks: Register Subscription Sync Pipeline

**Input**: Design documents from `/specs/076-register-subscription-sync/`
**Prerequisites**: plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md

**Tests**: Included — the constitution requires >80% unit test coverage and the spec references a testing strategy.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects needed. This phase prepares shared model and client infrastructure that all user stories depend on.

- [x] T001 [P] Add `SyncState` nullable string property to the Register model in `src/Common/Sorcha.Register.Models/Register.cs`. Default to null. Add XML doc comment explaining the state machine: null (local), "Subscribing", "Syncing", "Synced", "Error".
- [x] T002 [P] Add `SubscribeToRegisterAsync(string registerId, string mode)` and `UnsubscribeFromRegisterAsync(string registerId)` methods to `IPeerServiceClient` interface in `src/Common/Sorcha.ServiceClients/Peer/IPeerServiceClient.cs`. Include XML doc comments referencing the existing Peer Service endpoints.
- [x] T003 [P] Implement `SubscribeToRegisterAsync` and `UnsubscribeFromRegisterAsync` in `PeerServiceClient` in `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs`. `SubscribeToRegisterAsync` sends POST to `/api/registers/{registerId}/subscribe` with `{ mode }` body. `UnsubscribeFromRegisterAsync` sends DELETE to `/api/registers/{registerId}/subscribe`. Follow existing HTTP client patterns (e.g., `AdvertiseRegisterAsync`).
- [x] T004 [P] Add `SubscriptionNotificationRequest` and `SubscriptionNotificationResponse` DTO classes to a new file `src/Common/Sorcha.ServiceClients/Register/SubscriptionNotificationDtos.cs`. Fields per contracts/register-internal-api.yaml: OrganizationId (Guid), RegisterId (string), RegisterName (string?), Description (string?), Action (string). Response: RegisterId, Action, SyncState, Message.
- [x] T005 [P] Add `NotifySubscriptionAsync(SubscriptionNotificationRequest request, CancellationToken ct)` method to `IRegisterServiceClient` interface in `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs`. Returns `SubscriptionNotificationResponse?`.
- [x] T006 Implement `NotifySubscriptionAsync` in `RegisterServiceClient` in `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs`. POST to `/api/internal/register-subscriptions`. This is an internal endpoint so follow the anonymous pattern (no `SetAuthHeaderAsync` call) matching `GetInternalRegistersAsync`.

**Checkpoint**: Shared models and clients compiled. No runtime behaviour changed yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend orchestration logic that must exist before any user story can function end-to-end.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T007 Add `UpdateSyncStateAsync(string registerId, string? syncState, CancellationToken ct)` method to `RegisterManager` in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`. Fetches register by ID, updates `SyncState`, saves, publishes a `register:sync-state-changed` event via `_eventPublisher`. Returns the updated register. Log state transitions with structured logging.
- [x] T008 Add `RegisterSyncStateChangedEvent` class to `src/Services/Sorcha.Register.Service/Events/RegisterEvents.cs` (or the existing events file). Fields: RegisterId (string), SyncState (string), PreviousSyncState (string?). Follow the existing event class pattern (e.g., `RegisterStatusChangedEvent`).
- [x] T009 Subscribe to `register:sync-state-changed` event in `RegisterEventBridgeService` in `src/Services/Sorcha.Register.Service/Services/RegisterEventBridgeService.cs`. When received, broadcast `RegisterSyncStateChanged(registerId, syncState)` to the SignalR group `register:{registerId}`. Add `RegisterSyncStateChanged` method to `IRegisterHubClient` interface.
- [x] T010 Create the `POST /api/internal/register-subscriptions` endpoint in `src/Services/Sorcha.Register.Service/Program.cs`. Use `AllowAnonymous()` and `ExcludeFromDescription()` per the existing `/api/internal/registers` pattern. Accept `SubscriptionNotificationRequest` body. For `action == "subscribe"`: check if register exists locally — if yes, return 200 with current state; if no, create stub register via `RegisterManager.CreateRegisterAsync(name, isFullReplica: false, registerId: request.RegisterId, description: request.Description)`, set `SyncState = "Subscribing"` via `UpdateSyncStateAsync`, then fire-and-forget call to `IPeerServiceClient.SubscribeToRegisterAsync(registerId, "full-replica")`. For `action == "unsubscribe"`: delegate to T021 (placeholder — return 200 for now). Return `SubscriptionNotificationResponse`.
- [ ] T011 Unit test `UpdateSyncStateAsync` in `tests/Sorcha.Register.Core.Tests/`. Test: valid state transitions update the field and publish event. Test: register not found returns null/throws. Test: null syncState clears the field. Use Moq for `IRegisterRepository` and `IEventPublisher`.
- [ ] T012 Unit test the new `POST /api/internal/register-subscriptions` endpoint logic. Create a test class in `tests/Sorcha.Register.Service.Tests/`. Test: subscribe with non-existent register creates stub and returns "Subscribing". Test: subscribe with existing register returns current state (idempotent). Test: invalid request (missing registerId) returns 400. Mock `RegisterManager` and `IPeerServiceClient`.

**Checkpoint**: Register Service can receive subscription notifications, create stubs, and trigger peer sync. SignalR events flow for sync state changes.

---

## Phase 3: User Story 1 — Subscribe to a Remote Register and See It Immediately (Priority: P1) MVP

**Goal**: Clicking Subscribe in the UI creates the subscription, notifies Register Service which creates a stub, and the register appears immediately in the Registers list.

**Independent Test**: Subscribe to a public register from a second peer node. Verify it appears in the Registers list within seconds with a "Syncing" indicator.

### Tests for User Story 1

- [ ] T013 [P] [US1] Unit test the Tenant Service fire-and-forget notification in `tests/Sorcha.Tenant.Service.Tests/`. Test: `SubscribeAsync` saves subscription to DB AND calls `IRegisterServiceClient.NotifySubscriptionAsync` with correct parameters. Test: if `NotifySubscriptionAsync` throws, the subscription still persists (fire-and-forget). Test: `NotifySubscriptionAsync` is called with action="subscribe", correct registerId and registerName. Mock `IRegisterServiceClient`.
- [ ] T014 [P] [US1] Unit test the new Peer Service client methods in `tests/Sorcha.ServiceClients.Tests/`. Test: `SubscribeToRegisterAsync` sends POST to correct URL with correct body. Test: `UnsubscribeFromRegisterAsync` sends DELETE to correct URL. Use `MockHttpMessageHandler` pattern matching existing service client tests.

### Implementation for User Story 1

- [x] T015 [US1] Inject `IRegisterServiceClient` into `RegisterSubscriptionService` constructor in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs`. Add it as a private readonly field. Update `IRegisterSubscriptionService` interface if constructor injection changes are needed for testability.
- [x] T016 [US1] In `RegisterSubscriptionService.SubscribeAsync` in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs`, after the `SaveChangesAsync` call, add a fire-and-forget call to `_registerServiceClient.NotifySubscriptionAsync` with action="subscribe", passing registerId, registerName (from the existing `registerName` parameter), and the orgId. Wrap in `Task.Run` with try/catch that logs warnings on failure. The subscription must already be persisted before this call.
- [x] T017 [P] [US1] Update `SubscribeAsync` in the UI's `RegisterSubscriptionService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterSubscriptionService.cs` to accept and pass `registerName` and `description` parameters. Update `IRegisterSubscriptionService` interface accordingly. The POST body should include `register_name` and `description` fields alongside `register_id`.
- [x] T018 [P] [US1] Add `SyncState` property to `RegisterViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/RegisterViewModel.cs`. Add computed properties: `IsSyncing` (true when SyncState is "Subscribing", "Syncing", or "Error"), `SyncStateText` (human-readable text), `SyncStateColor` (MudBlazor Color for badge display).
- [x] T019 [US1] Update `MapToViewModel` in `RegisterService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterService.cs` to map the `SyncState` field from the backend Register model to the `RegisterViewModel`.
- [x] T020 [US1] Update `SubscribeDialog.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/SubscribeDialog.razor`. In `SubscribeToRegisterAsync`, pass the `register.Name` and `register.Description` from the `AvailableRegisterDto` to the updated `SubscribeAsync` method. Find the matching `AvailableRegisterDto` by registerId to get name and description.
- [x] T021 [US1] Update the Registers `Index.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`. In the register card rendering (foreach loop), when `register.IsSyncing` is true, display a sync state indicator — a small `MudChip` or `MudProgressCircular` next to the register name showing `register.SyncStateText`. Use the existing `subscriptionType` badge pattern as a template.

**Checkpoint**: Full subscribe flow works end-to-end. User clicks Subscribe → Tenant saves subscription → Register Service creates stub → Register appears in UI with sync indicator. Peer sync begins in background.

---

## Phase 4: User Story 2 — Sync Progress Visibility (Priority: P2)

**Goal**: As the Peer Service syncs register data, the UI reflects state changes in real time without page refresh.

**Independent Test**: Subscribe to a register with history. Observe the UI updating sync state in real time as dockets are replicated.

### Tests for User Story 2

- [ ] T022 [P] [US2] Unit test `RegisterEventBridgeService` sync state event handling in `tests/Sorcha.Register.Service.Tests/`. Verify that when a `register:sync-state-changed` event is published, the bridge calls `RegisterSyncStateChanged` on the SignalR hub context for the correct group. Mock `IHubContext<RegisterHub, IRegisterHubClient>`.

### Implementation for User Story 2

- [x] T023 [US2] Add `OnRegisterSyncStateChanged` event to `RegisterHubConnection` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterHubConnection.cs`. Register handler in `StartAsync` that listens for `"RegisterSyncStateChanged"` messages with `(string registerId, string syncState)` parameters. Follow the exact pattern of `OnRegisterStatusChanged`.
- [x] T024 [US2] Subscribe to `HubConnection.OnRegisterSyncStateChanged` in the Registers `Index.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor`. When received, find the matching register in `_registers` by ID and update its `SyncState`. If the new state is "Synced" or null, call `LoadRegistersAsync()` to fetch full register data. Call `StateHasChanged()`. Add/remove the event handler in `OnInitializedAsync` / `DisposeAsync` following the existing `OnRegisterStatusChanged` pattern.
- [x] T025 [US2] Handle the "Error" sync state in the Registers `Index.razor` page. When a register has `SyncState == "Error"`, show an error chip with a "Retry" button. The retry button should call the Register Service internal endpoint to re-trigger sync (or call `SubscriptionService.SubscribeAsync` again which will re-notify). Add an `OnClick:stopPropagation` to prevent navigating to the register detail page.

**Checkpoint**: Sync state changes propagate from Register Service → SignalR → UI in real time. Error states are visible with retry option.

---

## Phase 5: User Story 3 — Unsubscribe Cleans Up Sync State (Priority: P3)

**Goal**: Unsubscribing stops peer replication and removes the local register stub.

**Independent Test**: Subscribe to a register, then unsubscribe. Verify it disappears from the list and sync stops.

### Tests for User Story 3

- [ ] T026 [P] [US3] Unit test unsubscribe notification in `tests/Sorcha.Tenant.Service.Tests/`. Test: `UnsubscribeAsync` revokes subscription in DB AND calls `IRegisterServiceClient.NotifySubscriptionAsync` with action="unsubscribe". Test: if notification fails, subscription is still revoked (fire-and-forget).

### Implementation for User Story 3

- [x] T027 [US3] Implement the unsubscribe handler in the `POST /api/internal/register-subscriptions` endpoint in `src/Services/Sorcha.Register.Service/Program.cs`. For `action == "unsubscribe"`: look up the register by ID. If it exists and has a non-null `SyncState` (remote register), call `IPeerServiceClient.UnsubscribeFromRegisterAsync(registerId)` (fire-and-forget), then delete the register via `RegisterManager.DeleteRegisterAsync` (or equivalent). If the register is locally owned (`SyncState == null`), do NOT delete — just return 200. Log all actions.
- [x] T028 [US3] In `RegisterSubscriptionService.UnsubscribeAsync` in `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs`, after the `SaveChangesAsync` call that revokes the subscription, add a fire-and-forget call to `_registerServiceClient.NotifySubscriptionAsync` with action="unsubscribe". Follow the same pattern as the subscribe notification in T016.
- [ ] T029 [US3] Unit test the unsubscribe handler in `tests/Sorcha.Register.Service.Tests/`. Test: unsubscribe with remote register (SyncState != null) calls Peer unsubscribe and deletes register. Test: unsubscribe with local register (SyncState == null) does NOT delete. Test: unsubscribe with non-existent register returns 200 (idempotent).

**Checkpoint**: Full unsubscribe flow works. Unsubscribe → Tenant revokes → Register Service removes stub → Peer stops sync → UI removes register.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, edge cases, and hardening across all stories.

- [x] T030 [P] Handle edge case: duplicate local register. In the subscribe handler (T010), when a register already exists locally with `SyncState == null` (locally owned), return 200 with `syncState: null` and `message: "Register exists locally"`. No stub creation or sync needed.
- [x] T031 [P] Handle edge case: Peer Service unavailable during subscribe. In the subscribe handler (T010), if `SubscribeToRegisterAsync` throws, set `SyncState = "Error"` on the stub register and log a warning. The existing `RegisterSyncBackgroundService` periodic reconciliation in Peer Service should eventually pick it up, or a manual retry from the UI (T025) can re-trigger.
- [x] T032 [P] Handle edge case: duplicate subscription on same node. In the subscribe handler (T010), if the register already exists with a non-null `SyncState` (already syncing for another org), return 200 with the current sync state. Do not create a duplicate register or start duplicate sync.
- [ ] T033 [P] Update Register Service README in `src/Services/Sorcha.Register.Service/README.md` to document the new internal subscription notification endpoint, its purpose, and the sync state lifecycle.
- [ ] T034 [P] Update `docs/reference/API-DOCUMENTATION.md` to include the new internal endpoint under the Register Service section.
- [x] T035 Run `dotnet build` to verify no compiler warnings across all modified projects. Fix any nullable reference type warnings introduced by the new `SyncState` field.
- [x] T036 Run `dotnet test` to verify all existing tests still pass and new tests pass. Fix any regressions.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — all tasks are parallelisable (different files)
- **Phase 2 (Foundational)**: Depends on Phase 1 completion — T007-T009 need the model and client changes. T10 needs T007-T009.
- **Phase 3 (US1)**: Depends on Phase 2 — needs the internal endpoint and event bridge
- **Phase 4 (US2)**: Depends on Phase 2 (T009 for SignalR bridge) — can run in parallel with US1 on UI side
- **Phase 5 (US3)**: Depends on Phase 2 — can run in parallel with US1/US2
- **Phase 6 (Polish)**: Depends on all user stories being implemented

### User Story Dependencies

- **User Story 1 (P1)**: Depends only on Phase 2. Core subscribe flow — this is the MVP.
- **User Story 2 (P2)**: Depends on Phase 2 (T009). The SignalR event bridge must exist. Can start UI work in parallel with US1 backend work.
- **User Story 3 (P3)**: Depends on Phase 2 (T010 endpoint). Independent of US1 and US2.

### Within Each User Story

- Tests before implementation (TDD)
- Backend (Tenant notification) before frontend (UI display)
- Service client methods before service consumers
- Core implementation before integration

### Parallel Opportunities

Phase 1: All 6 tasks (T001-T006) target different files — fully parallelisable.

Phase 2: T007 and T008 are parallel (different files). T009 depends on T008. T010 depends on T007 + T009.

Phase 3: T013 + T014 parallel (test tasks). T017 + T018 parallel (different files). T015 → T016 → T020 → T021 sequential (same file dependencies).

---

## Parallel Example: Phase 1

```
# Launch all Phase 1 tasks together (6 different files):
Task T001: Add SyncState to Register.cs
Task T002: Add methods to IPeerServiceClient.cs
Task T003: Implement methods in PeerServiceClient.cs  (depends on T002 interface)
Task T004: Create SubscriptionNotificationDtos.cs
Task T005: Add method to IRegisterServiceClient.cs
Task T006: Implement in RegisterServiceClient.cs  (depends on T005 interface)

# Practical parallel grouping:
Group A: T001, T004 (independent model files)
Group B: T002 → T003 (interface then implementation)
Group C: T005 → T006 (interface then implementation)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (shared models and clients)
2. Complete Phase 2: Foundational (endpoint + event bridge)
3. Complete Phase 3: User Story 1 (subscribe → stub → UI display)
4. **STOP and VALIDATE**: Subscribe to a register and verify it appears in the UI
5. Deploy/demo — the core bug is fixed

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Add User Story 1 → Subscribe flow works → **Deploy MVP**
3. Add User Story 2 → Real-time sync visibility → Deploy
4. Add User Story 3 → Unsubscribe cleanup → Deploy
5. Phase 6 → Edge cases, docs, hardening → Final deploy

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Fire-and-forget pattern is critical: subscription MUST persist even if Register Service notification fails
- The Peer Service endpoints already exist — we only add client methods to call them
- No new projects created — all changes extend existing files
- Commit after each task or logical group
- Stop at any checkpoint to validate independently
