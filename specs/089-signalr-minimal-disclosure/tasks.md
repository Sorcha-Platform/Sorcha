# Tasks: SignalR Minimal Disclosure & Notification Fix

**Input**: Design documents from `/specs/089-signalr-minimal-disclosure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/signalr-signals.md

**Tests**: Included — constitution requires >85% coverage on new code.

**Organization**: Tasks grouped by user story. US1/US2/US4 share a foundational phase (server-side contracts). US3 is independent (authz only). US5/US6 depend on foundational completion.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Foundational — Signal Contract Definitions

**Purpose**: Define the two new thin signal record types that all user stories depend on. No behavioral changes yet — just the types.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T001 [P] Define `SignalNotification` record (SignalType, InstanceId, CorrelationId, Timestamp) replacing `ActionNotification` in `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs`
- [x] T002 [P] Define `EncryptionSignal` record (OperationId, PercentComplete, Status, Timestamp) replacing all 4 encryption records in `src/Services/Sorcha.Blueprint.Service/Models/EncryptionNotifications.cs`
- [x] T003 Update `INotificationService` interface — simplify method signatures to use `SignalNotification` and `EncryptionSignal`, remove `NotifyRecipientProgressAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/INotificationService.cs`

**Checkpoint**: New types compile. Interface updated. No behavioral changes yet.

---

## Phase 2: User Story 1 — Agent Receives Real-Time Action Signals (Priority: P1) + User Story 2 — Minimal Disclosure Payloads (Priority: P1) + User Story 4 — Instance Group Removal (Priority: P2)

**Goal**: Server sends thin signals through wallet-only groups. Agents receive and act on them. These three stories are inseparable on the server side — the same code changes deliver all three.

**Independent Test**: Deploy two agents in Docker. Agent A executes an action routing to Agent B. Agent B receives a thin signal (type + instanceId only) within 2 seconds via wallet group, triggers immediate poll. No instance group signals. No metadata in payload.

### Server-Side Implementation

- [x] T004 [US1,US2,US4] Update `NotificationService` — remove all `instance:{id}` group broadcasts, send thin `SignalNotification` to `wallet:{address}` only, add structured logging in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs`
- [x] T005 [US2] Update `EventsHubNotificationBridge` — change `SendAsync("InboundActionReceived", ...)` to send `SignalNotification` instead of rich `InboundActionNotification`, keep enrichment for Tenant persistence in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs`
- [x] T006 [US2] Update `EncryptionBackgroundService` — replace all encryption notification calls to use `EncryptionSignal`, remove `NotifyRecipientProgressAsync` calls in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [x] T007 [US1] Update `ActionExecutionService.NotifyParticipantsAsync` — use simplified interface, log warning when wallet address is null for a participant in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`

### Agent Client Implementation

- [x] T008 [US1] Update `SignalRInboxListener` — handle `SignalNotification` instead of rich payload, extract `instanceId`, trigger immediate instance poll in `src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs`

### Tests

- [ ] T009 [P] [US2] Update `NotificationServiceEventsHubTests` — assert thin payloads, verify no rich metadata in signals in `tests/Sorcha.Blueprint.Service.Tests/Services/NotificationServiceEventsHubTests.cs`
- [ ] T010 [P] [US4] Create `SignalNotificationDeliveryTests` — verify wallet-only delivery, verify no instance group sends, verify structured logging in `tests/Sorcha.Blueprint.Service.Tests/Services/SignalNotificationDeliveryTests.cs`
- [ ] T011 [P] [US1] Update or create agent SignalR tests — verify thin signal deserialization, verify immediate poll trigger in `tests/Sorcha.Agent.Tests/Inbox/SignalRInboxListenerTests.cs`

**Checkpoint**: Server sends thin signals via wallet groups only. Agent receives and polls immediately. No metadata disclosed. No instance groups used.

---

## Phase 3: User Story 3 — Service Token Authorization Enforcement (Priority: P1)

**Goal**: Service tokens without `org_id` claim are rejected when subscribing to wallet groups.

**Independent Test**: Connect with a service token missing `org_id` and attempt wallet subscription. Verify rejection.

- [x] T012 [US3] Enforce `org_id` requirement on service tokens — change `LogWarning` + allow to `throw new HubException` in `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs` (SubscribeToWallet method, lines 98-117)
- [x] T013 [US3] Verify all Sorcha service tokens include `org_id` — check Aspire service defaults token issuance in `src/Common/Sorcha.ServiceDefaults/`
- [ ] T014 [US3] Create `ActionsHubAuthorizationTests` — test 4 scenarios: service token without org_id rejected, with org_id succeeds, user token with valid wallet succeeds, user token with unlinked wallet rejected in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionsHubAuthorizationTests.cs`

**Checkpoint**: Unauthorized wallet subscriptions are rejected 100% of the time.

---

## Phase 4: User Story 5 — Connection Resilience with Polling Fallback (Priority: P2)

**Goal**: On SignalR reconnect, agent immediately polls for missed actions. Polling continues as fallback during disconnection.

**Independent Test**: Disconnect agent's SignalR mid-workflow. Verify polling continues. Reconnect and verify immediate poll fires.

- [x] T015 [US5] Add on-reconnect immediate poll to `SignalRInboxListener` — hook `Reconnected` event, trigger immediate poll of all subscribed wallets in `src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs`
- [ ] T016 [US5] Add reconnection tests — verify reconnect triggers immediate poll, verify polling continues during disconnection in `tests/Sorcha.Agent.Tests/Inbox/SignalRInboxListenerTests.cs`

**Checkpoint**: Agent resilience verified — no missed signals on reconnection.

---

## Phase 5: User Story 6 — UI Receives Thin Signals and Refreshes (Priority: P3)

**Goal**: Blazor UI handles thin signals, shows generic notification messages, pulls detail on demand.

**Independent Test**: Trigger workflow action. Verify notification toast shows generic message. Click loads full details from API.

### UI Model Updates

- [x] T017 [P] [US6] Replace action notification models with thin equivalents (`SignalNotification` shape) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Actions/ActionNotification.cs`
- [x] T018 [P] [US6] Replace encryption hub models with `EncryptionSignal` equivalent in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EncryptionHubModels.cs`

### UI Hub Connection Updates

- [x] T019 [US6] Update `ActionsHubConnection` — change event registrations to use thin types, remove `OnRecipientProgress` event in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs`
- [x] T020 [US6] Update `EventsHubConnection` — change `OnPendingActionReceived` to receive `SignalNotification`, `OnEncryptionOperationCompleted` to receive `EncryptionSignal`, add on-reconnect data refresh in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs`

### UI Component Updates

- [x] T021 [P] [US6] Update `PendingActionToast.razor` — show generic "New action available" snackbar, navigate to instance on click in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionToast.razor`
- [x] T022 [P] [US6] Update `PendingActionInbox.razor` — add placeholder on signal, pull enriched data from activity feed, maintain burst-throttle in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor`
- [x] T023 [P] [US6] Update `OperationNotificationListener.razor` — handle `EncryptionSignal`, show generic success/failure snackbar in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OperationNotificationListener.razor`

### UI Tests

- [x] T024 [P] [US6] Update `ActionsHubConnectionTests` — thin type expectations in `tests/Sorcha.UI.Core.Tests/Services/ActionsHubConnectionTests.cs`
- [x] T025 [P] [US6] Update `EventsHubConnectionTests` — thin type expectations in `tests/Sorcha.UI.Core.Tests/Services/EventsHubConnectionTests.cs`

**Checkpoint**: UI receives thin signals, shows generic toasts, pulls detail on demand. All existing UI tests updated.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Integration verification and documentation

- [ ] T026 Update `SignalRIntegrationTests` — update payload shape expectations in `tests/Sorcha.Blueprint.Service.Tests/Integration/SignalRIntegrationTests.cs`
- [x] T027 Run full test suite (`dotnet test`) and fix any compilation errors from deprecated type references
- [x] T028 Update Blueprint Service README if it documents SignalR notification payloads in `src/Services/Sorcha.Blueprint.Service/README.md`
- [ ] T029 Run quickstart.md validation — Docker integration test with two agents verifying sub-2-second signal delivery

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Foundational)**: No dependencies — start immediately
- **Phase 2 (US1+US2+US4)**: Depends on Phase 1 (needs new types)
- **Phase 3 (US3)**: Can run in parallel with Phase 2 (independent authz change, only needs T001 for the record type)
- **Phase 4 (US5)**: Depends on Phase 2 (T008, agent SignalR handler must exist)
- **Phase 5 (US6)**: Depends on Phase 1 (needs new types); can run in parallel with Phases 2-4
- **Phase 6 (Polish)**: Depends on all previous phases

### User Story Dependencies

- **US1 (Agent signals)**: Foundational → Phase 2 server + agent tasks
- **US2 (Minimal disclosure)**: Foundational → Phase 2 server tasks (same as US1)
- **US3 (Service token authz)**: Foundational T001 only → Phase 3 (independent)
- **US4 (Instance group removal)**: Foundational → Phase 2 T004 (same NotificationService change)
- **US5 (Reconnect resilience)**: Phase 2 T008 → Phase 4
- **US6 (UI thin signals)**: Foundational → Phase 5 (independent of agent/authz work)

### Within Each Phase

- Types/models before services
- Services before callers
- Implementation before tests (tests verify the change)

### Parallel Opportunities

- T001 and T002 can run in parallel (different files)
- T009, T010, T011 can run in parallel (different test files)
- Phase 3 can run in parallel with Phase 2
- Phase 5 can run in parallel with Phases 2-4
- T017 and T018 can run in parallel (different model files)
- T021, T022, T023 can run in parallel (different component files)
- T024 and T025 can run in parallel (different test files)

---

## Parallel Example: Phase 2 (Server + Agent)

```bash
# After Phase 1 (types defined), launch server-side changes in parallel:
Task T004: "Update NotificationService — wallet-only, thin signals"
Task T005: "Update EventsHubNotificationBridge — thin SignalR send"
Task T006: "Update EncryptionBackgroundService — use EncryptionSignal"

# Then sequential (depends on T004):
Task T007: "Update ActionExecutionService.NotifyParticipantsAsync"
Task T008: "Update SignalRInboxListener — thin signal handler"

# Then tests in parallel:
Task T009: "Update NotificationServiceEventsHubTests"
Task T010: "Create SignalNotificationDeliveryTests"
Task T011: "Update agent SignalR tests"
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US4 — Phase 1 + Phase 2)

1. Complete Phase 1: Define thin signal types
2. Complete Phase 2: Server sends thin signals, agent receives and polls
3. **STOP and VALIDATE**: Run Docker integration test — two agents, sub-2-second signal delivery, no metadata in payloads
4. This alone fixes the core bug and the security concern

### Incremental Delivery

1. Phase 1 + Phase 2 → Core fix (MVP)
2. Phase 3 → Security hardening (authz)
3. Phase 4 → Reconnection resilience
4. Phase 5 → UI update
5. Phase 6 → Polish and integration verification

### Parallel Strategy

With multiple agents:
1. Complete Phase 1 together (3 tasks, fast)
2. Agent A: Phase 2 (server + agent)
3. Agent B: Phase 3 (authz) + Phase 5 (UI)
4. Converge at Phase 6

---

## Notes

- [P] tasks = different files, no dependencies
- US1, US2, US4 are merged in Phase 2 because the same NotificationService changes deliver all three
- US3 is fully independent — can be implemented any time after T001
- US6 is fully independent — can be implemented any time after Phase 1
- Commit after each task or logical group
- Stop at any checkpoint to validate independently
- Breaking change: all clients must compile together before merge
