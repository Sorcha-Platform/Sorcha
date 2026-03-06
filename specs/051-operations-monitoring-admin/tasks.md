# Tasks: Operations & Monitoring Admin

**Input**: Design documents from `/specs/051-operations-monitoring-admin/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Included per spec requirement SC-009 (>85% coverage for new code).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects needed. This phase creates shared models and constants that multiple user stories depend on.

- [x] T001 Create `CredentialStatus` constants class with Active, Suspended, Revoked, Expired, Consumed values in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialStatus.cs`
- [x] T002 [P] Replace all credential status magic strings with `CredentialStatus` constants in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialLifecycleDialog.razor`
- [x] T003 [P] Replace credential status magic strings in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`
- [x] T004 [P] Replace credential status magic strings in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/CredentialAdmin.razor` and any other pages referencing status strings
- [x] T005 Extract shared `JsonSerializerOptions` configuration to a static helper in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/JsonSerializerOptionsExtensions.cs` and update services that create their own instances

**Checkpoint**: All credential status values use constants. JSON serializer options are shared. No magic strings remain.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Typed error handling for credential lifecycle — required before US7 and improves reliability for all user stories.

**CRITICAL**: This phase must complete before user story phases begin.

- [x] T006 Create `CredentialOperationResult<T>` result type with typed error info (status code, error message, error type) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Credentials/CredentialOperationResult.cs`
- [x] T007 Update `CredentialApiService.SuspendCredentialAsync` to return `CredentialOperationResult` with typed errors for 403, 404, 409, 500 in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`
- [x] T008 Update `CredentialApiService.ReinstateCredentialAsync` and `RefreshCredentialAsync` to return `CredentialOperationResult` with typed errors in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Credentials/CredentialApiService.cs`
- [x] T009 Update `CredentialLifecycleDialog.razor` to display specific error messages based on `CredentialOperationResult` error type in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Credentials/CredentialLifecycleDialog.razor`
- [x] T010 Write unit tests for typed error handling in `tests/Sorcha.UI.Core.Tests/Services/CredentialApiServiceErrorTests.cs` — test 403 returns permission error, 409 returns conflict error, 404 returns not found, 500 returns server error

**Checkpoint**: Foundation ready — credential lifecycle returns typed errors. User story implementation can now begin in parallel.

---

## Phase 3: User Story 1 — Gateway Dashboard & Alerts (Priority: P1) MVP

**Goal**: Dashboard cards show live statistics with 30-second auto-refresh; alerts panel displays active system alerts with per-user dismissal.

**Independent Test**: Log in as admin, verify dashboard cards show live data that changes when system state changes (e.g., create a register → count increments). Verify alerts appear and can be dismissed. Verify dismissed alerts reappear for a different admin.

### Tests for User Story 1

- [x] T011 [P] [US1] Write unit tests for alert dismissal service (localStorage-based per-user tracking) in `tests/Sorcha.UI.Core.Tests/Services/AlertDismissalServiceTests.cs`
- [x] T012 [P] [US1] Write unit tests for dashboard auto-refresh timer behavior in `tests/Sorcha.UI.Core.Tests/Services/DashboardServiceRefreshTests.cs`

### Implementation for User Story 1

- [x] T013 [US1] Add 30-second auto-refresh timer to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` — use `System.Timers.Timer`, dispose on page leave, show refresh indicator
- [x] T014 [US1] Integrate `AlertsPanel` component into `Home.razor` below the stats cards grid — pass `IAlertService.CurrentAlerts` and wire up dismiss callback
- [x] T015 [US1] Implement per-user alert dismissal using browser localStorage in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/AlertDismissalService.cs` — store dismissed alert IDs per user, filter alerts before display
- [x] T016 [US1] Handle graceful "data unavailable" state in `Home.razor` when `IDashboardService` returns null or throws — show "Data unavailable" text on affected cards instead of zeros
- [x] T017 [US1] Register `AlertDismissalService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`

**Checkpoint**: Dashboard auto-refreshes every 30s, alerts panel visible, per-user dismissal works. Independently testable.

---

## Phase 4: User Story 2 — Wallet Access Delegation (Priority: P1)

**Goal**: Wallet owners can grant, view, and revoke access grants from the wallet detail page Access tab. CLI provides equivalent `wallet access` subcommands.

**Independent Test**: Grant access to a wallet, verify grant appears in list, check access returns true, revoke and verify access returns false. Same via CLI.

### Tests for User Story 2

- [x] T018 [P] [US2] Write unit tests for `WalletAccessService` (grant, list, revoke, check) in `tests/Sorcha.UI.Core.Tests/Services/WalletAccessServiceTests.cs` — mock HttpClient, test success/failure/network error for each operation
- [x] T019 [P] [US2] Write unit tests for `wallet access` CLI commands in `tests/Sorcha.Cli.Tests/Commands/WalletAccessCommandTests.cs` — test grant, list, revoke, check subcommands

### Implementation for User Story 2

- [x] T020 [P] [US2] Create `IWalletAccessService` interface and `WalletAccessService` implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IWalletAccessService.cs` and `WalletAccessService.cs` — grant (POST), list (GET), revoke (DELETE), check (GET) against `/api/v1/wallets/{address}/access/*`
- [x] T021 [P] [US2] Create wallet access view models in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/WalletAccessModels.cs` — `WalletAccessGrantViewModel`, `GrantAccessFormModel` with AccessRight selector
- [x] T022 [US2] Create `WalletAccessTab.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Wallets/WalletAccessTab.razor` — grants table, grant form with subject input + AccessRight dropdown, revoke button with confirmation, empty state message
- [x] T023 [US2] Add Access tab to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor` — insert new MudTabPanel after Transactions tab
- [x] T024 [US2] Add `wallet access grant|list|revoke|check` subcommands to `src/Apps/Sorcha.Cli/Commands/WalletCommands.cs` — use Refit client for `/api/v1/wallets/{address}/access/*`, handle 403 with specific error
- [x] T025 [US2] Register `IWalletAccessService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` using standard `AuthenticatedHttpMessageHandler` pattern

**Checkpoint**: Wallet Access tab functional with grant/list/revoke/check. CLI equivalents work. Independently testable.

---

## Phase 5: User Story 3 — Schema Provider CLI (Priority: P2)

**Goal**: CLI commands expose schema provider health information and manual refresh capability (UI page already exists and is functional). Verify existing UI satisfies FR-010/011/012.

**Independent Test**: Run `schema providers list` and verify tabular output shows provider names, health status, schema count. Run `schema providers refresh <name>` and verify last refresh timestamp updates.

### Tests for User Story 3

- [x] T026 [P] [US3] Write unit tests for `schema providers` CLI commands in `tests/Sorcha.Cli.Tests/Commands/SchemaCommandTests.cs` — test list and refresh subcommands

### Implementation for User Story 3

- [x] T027 [P] [US3] Create Refit interface for schema provider endpoints in `src/Apps/Sorcha.Cli/Services/IBlueprintServiceClient.cs` (added to existing Blueprint client) — `GetSchemaProvidersAsync()`, `RefreshSchemaProviderAsync(providerName)`
- [x] T028 [US3] Create `SchemaCommands.cs` in `src/Apps/Sorcha.Cli/Commands/SchemaCommands.cs` — `schema providers list` (table: Name, Status, SchemaCount, LastFetch) and `schema providers refresh --name <provider>` subcommands
- [x] T029 [US3] Register `SchemaCommands` in CLI root command in `src/Apps/Sorcha.Cli/Program.cs`
- [x] T029a [US3] Verify existing `SchemaProviderHealth.razor` satisfies FR-010 (displays providers with health, refresh time, count), FR-011 (manual refresh button), FR-012 (functional page, not placeholder) — document verification in commit message

**Checkpoint**: Schema provider CLI commands work. Existing UI verified as functional for FR-010/011/012.

---

## Phase 6: User Story 4 — Events Admin (Priority: P2)

**Goal**: Admin Events page shows paginated, filterable system event log with single-event deletion. CLI provides `admin events list|delete`.

**Independent Test**: Trigger system events (publish a blueprint), view them in Events admin page, filter by severity, delete an entry and confirm removal. Same via CLI.

### Tests for User Story 4

- [x] T030 [P] [US4] Write unit tests for `EventAdminService` in `tests/Sorcha.UI.Core.Tests/Services/EventAdminServiceTests.cs` — test list with filters, delete, pagination, network errors
- [x] T031 [P] [US4] Write unit tests for `admin events` CLI commands in `tests/Sorcha.Cli.Tests/Commands/EventAdminCommandTests.cs`

### Implementation for User Story 4

- [x] T032 [P] [US4] Create event admin models in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EventAdminModels.cs` — `SystemEventViewModel`, `EventFilterModel` (severity, since), `EventListResponse` (events, totalCount, page, pageSize)
- [x] T033 [P] [US4] Create `IEventAdminService` interface and `EventAdminService` implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IEventAdminService.cs` and `EventAdminService.cs` — list (GET `/api/events/admin`), delete (DELETE `/api/events/{id}`)
- [x] T034 [US4] Create `EventsAdmin.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/EventsAdmin.razor` — route `/admin/events`, MudTable with server-side pagination, severity filter chips, "since" date picker (no type filter — API does not support it), delete button per row with confirmation, empty state
- [x] T035 [US4] Add navigation menu entry for Events Admin in the admin nav section
- [x] T036 [US4] Add `admin events list|delete` subcommands to `src/Apps/Sorcha.Cli/Commands/AdminCommands.cs` — list with `--severity`, `--since`, `--page` options (no `--type` — API does not support it); delete by event ID
- [x] T037 [US4] Register `IEventAdminService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`

**Checkpoint**: Events admin page shows filtered, paginated events with delete. CLI equivalents work. Independently testable.

---

## Phase 7: User Story 5 — Push Notification Management (Priority: P3)

**Goal**: Users can enable/disable browser push notifications from Settings with clear subscription status feedback.

**Independent Test**: Toggle push notifications on, verify status shows Active, toggle off, verify status shows Inactive. Deny browser permission and verify guidance message.

### Tests for User Story 5

- [x] T038 [P] [US5] Write unit tests for `PushNotificationService` in `tests/Sorcha.UI.Core.Tests/Services/PushNotificationServiceTests.cs` — test subscribe, unsubscribe, get status, network errors

### Implementation for User Story 5

- [x] T039 [P] [US5] Create push notification models in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/PushNotificationModels.cs` — `PushSubscriptionStatus` (hasActiveSubscription), `PushSubscriptionRequest` (endpoint, p256dh, auth)
- [x] T040 [P] [US5] Create `IPushNotificationService` interface and `PushNotificationService` implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IPushNotificationService.cs` and `PushNotificationService.cs` — subscribe (POST), unsubscribe (DELETE), getStatus (GET) against `/api/push-subscriptions`
- [x] T041 [US5] Create `NotificationSettings.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/NotificationSettings.razor` — route `/settings/notifications`, MudSwitch toggle, status chip (Active/Inactive), browser permission request via JS interop, guidance message on permission denial
- [x] T042 [US5] Register `IPushNotificationService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`
- [x] T043 [US5] Add navigation link to Notification Settings in the Settings section of the nav menu

**Checkpoint**: Push notification toggle works end-to-end. Browser permission handling is graceful. Independently testable.

---

## Phase 8: User Story 6 — Encrypted Payload Operation Status (Priority: P3)

**Goal**: Users see encryption progress stages during envelope encryption operations. CLI provides `operation status <id>`.

**Independent Test**: Submit an action that triggers encryption, observe progress indicator advancing through stages until completion. Run `operation status <id>` in CLI.

### Tests for User Story 6

- [x] T044 [P] [US6] Write unit tests for `OperationStatusService` in `tests/Sorcha.UI.Core.Tests/Services/OperationStatusServiceTests.cs` — test get status, polling, network errors
- [x] T045 [P] [US6] Write unit tests for `operation status` CLI command in `tests/Sorcha.Cli.Tests/Commands/OperationCommandTests.cs`

### Implementation for User Story 6

- [x] T046 [P] [US6] Create encryption operation models in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/EncryptionOperationModels.cs` — `EncryptionOperationViewModel` (operationId, stage, percentComplete, recipientCount, isComplete)
- [x] T047 [P] [US6] Create `IOperationStatusService` interface and `OperationStatusService` implementation in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/IOperationStatusService.cs` and `OperationStatusService.cs` — getStatus (GET `/api/operations/{operationId}`)
- [x] T048 [US6] Create `EncryptionProgressIndicator.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor` — MudProgressLinear with stage labels, auto-poll while in progress, stop on completion
- [x] T049 [US6] Create `OperationCommands.cs` in `src/Apps/Sorcha.Cli/Commands/OperationCommands.cs` — `operation status <operationId>` command showing stage, percentage, recipient count
- [x] T050 [US6] Register `IOperationStatusService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` and register `OperationCommands` in CLI root
- [ ] T051 [US6] Integrate `EncryptionProgressIndicator` into the action submission flow where envelope encryption is triggered (BACKLOG — requires Blueprint Service to return OperationId in action execution response; UI plumbing ready once backend supports it)

**Checkpoint**: Encryption progress visible during operations. CLI status command works. Independently testable.

---

## Phase 9: User Story 7 — UI Service Reliability Improvements (Priority: P2)

**Goal**: Structured multi-value inputs replace comma-separated text. Presentation requests auto-refresh. Error messages are specific and actionable.

**Independent Test**: Attempt a credential suspension with wrong permissions — verify specific "Permission denied" message. Create a presentation request with accepted issuers — verify tag/chip input. Verify pending request auto-refreshes without manual reload.

### Tests for User Story 7

- [x] T052 [P] [US7] Write unit tests for presentation request auto-refresh polling in `tests/Sorcha.UI.Core.Tests/Services/PresentationAdminAutoRefreshTests.cs`

### Implementation for User Story 7

- [x] T053 [US7] Replace comma-separated `AcceptedIssuers` input with MudChipSet tag input on `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PresentationAdmin.razor` — allow adding/removing individual issuers
- [x] T054 [US7] Replace comma-separated `RequiredClaims` input with MudChipSet tag input on `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/PresentationAdmin.razor`
- [x] T055 [US7] Add 5-second auto-refresh timer for pending presentation requests on `PresentationAdmin.razor` — poll `GetPresentationResultAsync` for requests with Status=="Pending", stop when status changes
- [x] T056 [US7] Verify all credential lifecycle error messages render correctly in `CredentialLifecycleDialog.razor` for each error type (403, 404, 409, 500) — integration verification of Phase 2 foundation work

**Checkpoint**: Structured inputs replace comma-separated text. Presentation requests auto-refresh. Error messages are specific. Independently testable.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: E2E tests, documentation, and integration verification.

- [x] T057 [P] Write E2E test for dashboard auto-refresh and alerts in `tests/Sorcha.UI.E2E.Tests/Docker/DashboardAlertsTests.cs`
- [x] T058 [P] Write E2E test for wallet access delegation tab in `tests/Sorcha.UI.E2E.Tests/Docker/WalletAccessTests.cs`
- [x] T059 [P] Write E2E test for events admin page in `tests/Sorcha.UI.E2E.Tests/Docker/EventsAdminTests.cs`
- [x] T060 [P] Write E2E test for push notification settings toggle in `tests/Sorcha.UI.E2E.Tests/Docker/PushNotificationTests.cs`
- [x] T061 Verify all new pages/tabs are accessible via nav menu and direct URL routing
- [x] T062 Update `docs/reference/development-status.md` with Feature 051 completion status
- [x] T063 Update `.specify/MASTER-TASKS.md` with Feature 051 task status
- [x] T064 Run full test suite (`dotnet test`) and verify >85% coverage on new code

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (T001 for status constants) — BLOCKS all user stories
- **US1 Dashboard (Phase 3)**: Depends on Phase 2 completion — no dependencies on other stories
- **US2 Wallet Access (Phase 4)**: Depends on Phase 2 completion — no dependencies on other stories
- **US3 Schema CLI (Phase 5)**: Depends on Phase 2 completion — no dependencies on other stories
- **US4 Events Admin (Phase 6)**: Depends on Phase 2 completion — no dependencies on other stories
- **US5 Push Notifications (Phase 7)**: Depends on Phase 2 completion — no dependencies on other stories
- **US6 Encryption Progress (Phase 8)**: Depends on Phase 2 completion — no dependencies on other stories
- **US7 Reliability (Phase 9)**: Depends on Phase 2 completion (uses typed error foundation)
- **Polish (Phase 10)**: Depends on all user story phases

### User Story Independence

All 7 user stories are **fully independent** — they modify different files and can be implemented in any order or in parallel after Phase 2.

### Within Each User Story

- Tests written first (fail before implementation)
- Models before services
- Services before UI components/pages
- Register services in DI after implementation
- CLI commands can be done in parallel with UI work (different files)

---

## Parallel Opportunities

### Phase 1 (after T001)
```
Parallel: T002, T003, T004 — replace magic strings in 3 different files
```

### Phase 3-9 (after Phase 2)
```
All user stories can run in parallel — each touches different files:
  US1: Home.razor, AlertDismissalService
  US2: WalletDetail.razor, WalletAccessService, WalletCommands.cs
  US3: SchemaCommands.cs (new file)
  US4: EventsAdmin.razor (new), EventAdminService, AdminCommands.cs
  US5: NotificationSettings.razor (new), PushNotificationService
  US6: EncryptionProgressIndicator.razor (new), OperationCommands.cs (new)
  US7: PresentationAdmin.razor
```

### Within each user story
```
Models [P] + Service interface [P] can be created simultaneously
CLI commands [P] and UI components [P] can be done in parallel (different projects)
Unit tests [P] for different services can run in parallel
```

### Phase 10
```
All 4 E2E tests (T057-T060) can run in parallel — different test files
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (status constants, shared JSON config)
2. Complete Phase 2: Foundational (typed error handling)
3. Complete Phase 3: User Story 1 — Dashboard & Alerts
4. **STOP and VALIDATE**: Dashboard shows live data with auto-refresh, alerts panel works
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Dashboard) → Test → Demo (MVP!)
3. Add US2 (Wallet Access) → Test → Demo (P1 complete)
4. Add US3 (Schema CLI) + US4 (Events Admin) → Test → Demo (P2 features)
5. Add US5 (Push) + US6 (Encryption) → Test → Demo (P3 features)
6. Add US7 (Reliability) → Test → Polish → Final demo

### Suggested MVP Scope

**Phase 1 + Phase 2 + Phase 3 (US1)** — delivers live dashboard with auto-refresh and alerts, the highest-visibility improvement. Total: 17 tasks.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Schema Provider UI (FR-010, FR-011, FR-012) already exists and is functional — only CLI commands needed
- Presentation request QR polling already exists at 2s interval — US7 adds separate admin-page auto-refresh at 5s
- `AccessRight` enum values (Owner/ReadWrite/ReadOnly) map to spec terms (admin/sign/read)
- All backend endpoints exist — no backend development in any task
- Events admin API supports `severity` and `since` filters only (no `type` filter, no date range end)
- FR-010, FR-011, FR-012 are pre-satisfied by existing SchemaProviderHealth.razor — T029a verifies this
- Total: 65 tasks across 10 phases
