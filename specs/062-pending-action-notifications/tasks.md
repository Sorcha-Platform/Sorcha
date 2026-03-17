# Tasks: Pending Action Notifications

**Input**: Design documents from `/specs/062-pending-action-notifications/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Model additions and shared infrastructure needed across stories

- [X] T001 [P] Add NotificationConfig model to src/Common/Sorcha.Blueprint.Models/NotificationConfig.cs — optional class with SummaryTemplate, UrgencyRule, DeadlineField, GroupBy properties
- [X] T002 [P] Add Notification property (NotificationConfig?) to Action class in src/Common/Sorcha.Blueprint.Models/Action.cs
- [X] T003 [P] Add PendingActionNotificationDto model to src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/PendingActionNotificationDto.cs — UI model with Summary, Urgency, Deadline, NavigationPath, SenderDisplayName, BlueprintTitle, ActionTitle, GroupKey

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core enrichment and persistence changes that all user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Extend InboundActionNotification record in src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs — add Summary, Urgency (string), Deadline (DateTimeOffset?), GroupKey (string?) fields
- [X] T005 Add default notification enrichment logic to EventsHubNotificationBridge.EnrichNotificationAsync() in src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs — set Summary to "{BlueprintName} — {ActionTitle}" (default, no template rendering yet), set Urgency to "normal", set Deadline and GroupKey to null. Template-based rendering is deferred to T022 (US3) which replaces these defaults when NotificationConfig is present on the action
- [X] T006 Add ActivityEvent persistence in EventsHubNotificationBridge after enrichment — call Tenant Service POST /api/events to persist each notification as an ActivityEvent with EventType="PendingAction", mapping urgency to EventSeverity, EntityId=InstanceId, EntityType="BlueprintInstance"

**Checkpoint**: Enriched notifications are persisted and contain business-meaningful content

---

## Phase 3: User Story 1 — Real-Time Pending Action Alert (Priority: P1) MVP

**Goal**: Users see meaningful toast notifications within 5 seconds of a pending action arriving

**Independent Test**: Submit action as Participant A in a two-participant blueprint, verify Participant B sees enriched toast notification with blueprint name, action title, sender name, and summary

### Implementation for User Story 1

- [X] T007 [US1] Register InboundActionReceived handler in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs — add _hubConnection.On<PendingActionNotificationDto>("InboundActionReceived", ...) handler that fires new OnPendingActionReceived event, also register DigestNotificationReceived handler
- [X] T008 [US1] Create PendingActionToast.razor component in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionToast.razor — MudBlazor Snackbar/Alert showing: sender display name, action title, summary text, urgency indicator (colour-coded), and "Review" CTA button that navigates to NavigationPath
- [X] T009 [US1] Wire PendingActionToast into MainLayout.razor in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor — subscribe to EventsHubConnection.OnPendingActionReceived, show toast for each real-time notification, queue if multiple arrive simultaneously
- [X] T010 [US1] Add reconnect catch-up in EventsHubConnection in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs — on reconnect, fetch unread events from Tenant Service GET /api/events?unreadOnly=true&eventType=PendingAction to surface missed notifications

**Checkpoint**: User Story 1 is functional — real-time toast notifications appear with business context and navigate to action page

---

## Phase 4: User Story 2 — Pending Action Inbox (Priority: P1)

**Goal**: Users have a dedicated inbox showing all pending actions grouped by blueprint with urgency sorting

**Independent Test**: Create 5 pending actions across 3 blueprints for a user, verify inbox displays them grouped by blueprint and sorted by urgency

### Implementation for User Story 2

- [X] T011 [US2] Create PendingActionService in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/PendingActionService.cs — HTTP client calling GET /api/actions/pending (with pagination, urgency filter) and GET /api/actions/pending/count, plus methods to mark actions as read via Tenant Service POST /api/events/mark-read
- [X] T012 [US2] Add pending actions query method to IInstanceStore in src/Services/Sorcha.Blueprint.Service/Storage/IInstanceStore.cs — GetPendingActionsByWalletAsync(walletAddress, page, pageSize) returning PendingActionSummary list
- [X] T013 [US2] Implement pending actions query in the in-memory instance store implementation in src/Services/Sorcha.Blueprint.Service/Storage/ — filter instances by participant wallet where state=Active, extract current action IDs, build PendingActionSummary with blueprint context
- [X] T014 [US2] Add GET /api/actions/pending and GET /api/actions/pending/count endpoints in src/Services/Sorcha.Blueprint.Service/Endpoints/ActionEndpoints.cs — requires authentication, resolves wallet addresses from JWT claims, calls IInstanceStore.GetPendingActionsByWalletAsync, returns paginated PendingActionSummary list with total count
- [X] T015 [US2] Add API Gateway route for /api/actions/pending in src/Services/Sorcha.ApiGateway/appsettings.json — route to blueprint-cluster with RequireAuthenticated policy
- [X] T016 [US2] Create PendingActionInbox.razor component in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor — MudBlazor drawer/panel showing pending actions grouped by blueprint, sorted by urgency (urgent first), each group expandable with individual action cards showing summary, sender, deadline countdown, and "Review & Sign" CTA navigating to action page
- [X] T017 [US2] Add inbox icon with badge count to navigation in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor — MudBadge on MudIconButton showing unread pending action count from PendingActionService, clicking opens PendingActionInbox drawer
- [X] T018 [US2] Wire real-time updates into inbox — when OnPendingActionReceived fires while inbox is open, prepend new action to appropriate group without page refresh; when action is completed (detected via ActivityEvent with EventType="ActionCompleted"), remove from inbox and show brief "completed by {name}" note; throttle UI renders during burst (batch updates every 500ms when >10 notifications arrive within 2 seconds, show "catching up..." indicator)

**Checkpoint**: User Stories 1 AND 2 work — real-time toasts plus a complete pending action inbox

---

## Phase 5: User Story 3 — Blueprint-Defined Notification Content (Priority: P2)

**Goal**: Blueprint designers configure per-action notification templates that produce meaningful summaries

**Independent Test**: Create a blueprint with NotificationConfig on an action (summary template + deadline field), execute workflow, verify notification displays rendered template values and urgency badge

### Implementation for User Story 3

- [X] T019 [US3] Add NotificationConfig serialization support in src/Common/Sorcha.Blueprint.Models/NotificationConfig.cs — ensure JSON serialization/deserialization works with existing blueprint JSON format, add [JsonPropertyName] attributes matching camelCase convention
- [X] T020 [US3] Create SummaryTemplateRenderer utility in src/Services/Sorcha.Blueprint.Service/Services/Implementation/SummaryTemplateRenderer.cs — takes a template string and JsonElement payload, replaces all {{payload.field}} tokens with resolved values, handles nested paths (e.g., {{payload.order.ref}}), returns action title as fallback if template is null or all tokens unresolved
- [X] T021 [US3] Create UrgencyCalculator utility in src/Services/Sorcha.Blueprint.Service/Services/Implementation/UrgencyCalculator.cs — takes urgency rule + deadline field path + payload, extracts deadline DateTimeOffset, returns "urgent" if <4h, "warning" if <24h, "normal" otherwise, returns "normal" if no config or field missing
- [X] T022 [US3] Integrate SummaryTemplateRenderer and UrgencyCalculator into EventsHubNotificationBridge enrichment pipeline in src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs — when blueprint has NotificationConfig on the action, use renderer for summary and calculator for urgency instead of defaults
- [X] T023 [P] [US3] Add unit tests for SummaryTemplateRenderer in tests/Sorcha.Blueprint.Service.Tests/Services/SummaryTemplateRendererTests.cs — test: valid template renders, missing field falls back, nested path resolves, null template returns default, empty payload returns default, multiple tokens in one template
- [X] T024 [P] [US3] Add unit tests for UrgencyCalculator in tests/Sorcha.Blueprint.Service.Tests/Services/UrgencyCalculatorTests.cs — test: deadline in <4h returns urgent, <24h returns warning, >24h returns normal, missing field returns normal, past deadline returns urgent, null config returns normal

**Checkpoint**: Blueprint-defined templates produce meaningful notifications

---

## Phase 6: User Story 4 — Notification Preferences (Priority: P2)

**Goal**: Users control notification delivery mode (real-time, digest, quiet)

**Independent Test**: Change user preference to digest, trigger actions, verify they arrive as a batched summary instead of individual toasts

### Implementation for User Story 4

- [X] T025 [US4] Create TenantNotificationPreferenceProvider in src/Services/Sorcha.Wallet.Service/Services/Implementation/TenantNotificationPreferenceProvider.cs — implements INotificationPreferenceProvider, calls Tenant Service GET /api/preferences via IServiceClient, maps NotificationFrequency and NotificationMethod to the provider's response model, caches per-user for 5 minutes to avoid per-notification API calls. Map "quiet" mode to NotificationsEnabled=false (existing enum has no Quiet value; quiet means disabled real-time alerts but inbox still accumulates)
- [X] T026 [US4] Replace DefaultNotificationPreferenceProvider registration in src/Services/Sorcha.Wallet.Service/Program.cs — swap AddSingleton<INotificationPreferenceProvider, DefaultNotificationPreferenceProvider> to AddScoped<INotificationPreferenceProvider, TenantNotificationPreferenceProvider>
- [X] T027 [US4] Add notification preferences UI section to user settings in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/ — MudSelect for delivery mode (Real-time / Hourly Digest / Daily Digest), MudSwitch for notifications enabled, calls PUT /api/preferences to save
- [X] T028 [P] [US4] Add unit tests for TenantNotificationPreferenceProvider in tests/Sorcha.Wallet.Service.Tests/Services/TenantNotificationPreferenceProviderTests.cs — test: maps RealTime correctly, maps HourlyDigest, maps DailyDigest, caches results, disabled returns not-enabled, service client failure falls back to defaults

**Checkpoint**: User preferences control notification delivery

---

## Phase 7: User Story 5 — Notification History & Catch-Up (Priority: P3)

**Goal**: Persistent notification history survives restarts, users can browse and catch up

**Independent Test**: Generate notifications while user is offline, restart platform, reconnect user, verify all notifications appear

### Implementation for User Story 5

- [X] T029 [US5] Add notification history browsing to PendingActionInbox in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor — add "History" tab showing past notifications (completed actions) from Tenant Service GET /api/events?eventType=PendingAction with pagination, grouped by date (Today, Yesterday, This Week, Earlier)
- [X] T030 [US5] Add DigestNotificationReceived handler in EventsHubConnection in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs — parse digest payload, fire OnDigestReceived event, inbox shows digest summary with expandable action list

**Checkpoint**: Full notification lifecycle works including history and catch-up

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Quality improvements across all stories

- [X] T031 [P] Update Blueprint Service README at src/Services/Sorcha.Blueprint.Service/README.md — document new GET /api/actions/pending endpoint and NotificationConfig on Action model
- [X] T032 [P] Update Wallet Service README at src/Services/Sorcha.Wallet.Service/README.md — document TenantNotificationPreferenceProvider replacing DefaultNotificationPreferenceProvider
- [X] T033 [P] Add OpenAPI documentation (.WithSummary/.WithDescription) to new pending actions endpoints in src/Services/Sorcha.Blueprint.Service/Endpoints/ActionEndpoints.cs
- [X] T034 Update docs/reference/platform-service-analysis.md with notification pipeline improvements
- [X] T035 Update .specify/MASTER-TASKS.md with Feature 062 completion status

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on T001, T002 (models must exist for enrichment)
- **US1 (Phase 3)**: Depends on Phase 2 (enriched notifications must exist)
- **US2 (Phase 4)**: Depends on Phase 2 (enriched notifications must exist), can run in parallel with US1
- **US3 (Phase 5)**: Depends on Phase 2 (enrichment pipeline must exist)
- **US4 (Phase 6)**: Independent of other user stories (only modifies Wallet Service)
- **US5 (Phase 7)**: Depends on US1 + US2 (inbox must exist)
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (Real-Time Alert)**: Foundational only — independent
- **US2 (Inbox)**: Foundational only — can run parallel with US1
- **US3 (Templates)**: Foundational only — can run parallel with US1/US2
- **US4 (Preferences)**: Fully independent — can run parallel with anything
- **US5 (History)**: Depends on US1 + US2 — sequential after both

### Within Each User Story

- Models before services
- Services before endpoints
- Endpoints before UI components
- Core implementation before integration

### Parallel Opportunities

- T001, T002, T003 can all run in parallel (different files)
- US1 and US2 can run in parallel after Phase 2
- US3 and US4 can run in parallel (different services)
- T023 and T024 can run in parallel (different test files)
- T028 can run in parallel with US3 tasks (different service)

---

## Parallel Example: Phase 1 + Phase 2

```
# Phase 1 — all parallel (different files):
Task T001: "NotificationConfig model"
Task T002: "Add Notification property to Action"
Task T003: "PendingActionNotificationDto UI model"

# Phase 2 — sequential (same file):
Task T004 → T005 → T006 (EventsHubNotificationBridge modifications)
```

## Parallel Example: User Stories 1 + 2

```
# After Phase 2, launch in parallel:

# US1 (UI toast):
Task T007 → T008 → T009 → T010

# US2 (inbox + API):
Task T011, T012, T013 → T014 → T015 → T016 → T017 → T018
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T006)
3. Complete Phase 3: US1 — Real-Time Alerts (T007-T010)
4. Complete Phase 4: US2 — Inbox (T011-T018)
5. **STOP and VALIDATE**: Both stories independently testable
6. Deploy/demo — users can see and act on pending actions

### Incremental Delivery

1. Setup + Foundational → enriched notifications flowing
2. Add US1 → real-time toasts working → demo
3. Add US2 → full inbox working → demo
4. Add US3 → smart templates → demo
5. Add US4 → user-controlled preferences → demo
6. Add US5 → full history → demo

---

## Notes

- Most backend infrastructure already exists (see research.md)
- Primary work is wiring existing components and building UI
- EventsHubNotificationBridge is the key integration point for enrichment
- Test each story by running a two-participant blueprint end-to-end
- Commit after each task or logical group
