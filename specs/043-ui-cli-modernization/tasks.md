# Tasks: UI & CLI Modernization

**Input**: Design documents from `/specs/043-ui-cli-modernization/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution mandates >85% coverage on new backend services and integration tests for all APIs.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in all task descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add dependencies, create shared files, and configure infrastructure needed by multiple stories

- [X] T001 Add OtpNet NuGet package to `src/Services/Sorcha.Tenant.Service/Sorcha.Tenant.Service.csproj`
- [X] T002 [P] Add `qrcode.min.js` to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/js/qrcode.min.js` and create JS interop helper
- [X] T003 [P] Create i18n directory and English base translation file `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/i18n/en.json` with all UI string keys (nav labels, buttons, system messages, page titles)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: UserPreferences backend + client — required by US4 (default wallet), US5 (wizard), US7 (settings)

**⚠️ CRITICAL**: No user story work on US4, US5, or US7 can begin until this phase is complete. US1, US2, US3, US6, US8 have no dependency on this phase and CAN start in parallel.

- [X] T004 Create `UserPreferences` entity model in `src/Services/Sorcha.Tenant.Service/Models/UserPreferences.cs` with fields: Id, UserId (FK), Theme, Language, TimeFormat, DefaultWalletAddress, NotificationsEnabled, TwoFactorEnabled, UpdatedAt — and add `ThemePreference` + `TimeFormatPreference` enums
- [X] T005 Add `UserPreferences` DbSet to `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`, configure entity (unique index on UserId, FK to UserIdentity with cascade delete), and create EF Core migration
- [X] T006 Create `UserPreferenceEndpoints.cs` in `src/Services/Sorcha.Tenant.Service/Endpoints/UserPreferenceEndpoints.cs` implementing: GET `/api/preferences` (lazy-create), PUT `/api/preferences` (partial update), GET `/api/preferences/default-wallet`, PUT `/api/preferences/default-wallet`, DELETE `/api/preferences/default-wallet` — per contract in `contracts/user-preferences-api.md`. Register endpoint group in `src/Services/Sorcha.Tenant.Service/Program.cs`
- [X] T007 [P] Create `UserPreferencesDto` and `UpdateUserPreferencesRequest` DTOs in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/UserPreferencesDto.cs`
- [X] T008 Create `IUserPreferencesService` / `UserPreferencesService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/UserPreferencesService.cs` — HTTP client calling Tenant Service preferences API (GET, PUT, default-wallet endpoints). Register in DI via `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`
- [X] T009 Write unit tests for UserPreference endpoints in `tests/Sorcha.Tenant.Service.Tests/Endpoints/UserPreferenceTests.cs` — test lazy-create, partial update, validation rejection, default-wallet CRUD

**Checkpoint**: UserPreferences infrastructure ready — US4, US5, US7 can now proceed

---

## Phase 3: User Story 1 — Activity Log Replaces Toasts (Priority: P1) 🎯 MVP

**Goal**: Replace transient toast notifications with a persistent, real-time activity log. Bell icon with unread count in app bar, right-side overlay panel, date-grouped events, admin org-wide view.

**Independent Test**: Create a wallet → verify event appears in activity log panel with timestamp and details. Open panel → verify unread count resets. As admin → verify org-wide events visible.

### Implementation for User Story 1

- [X] T010 [P] [US1] Create `ActivityEvent` entity and `EventSeverity` enum in `src/Services/Sorcha.Blueprint.Service/Models/ActivityEvent.cs` per data-model.md (Id, OrganizationId, UserId, EventType, Severity, Title, Message, SourceService, EntityId, EntityType, IsRead, CreatedAt, ExpiresAt)
- [X] T011 [P] [US1] Create `BlueprintEventsDbContext` in `src/Services/Sorcha.Blueprint.Service/Data/BlueprintEventsDbContext.cs` — PostgreSQL context with ActivityEvent DbSet, indexes per data-model.md (UserId+CreatedAt, OrgId+CreatedAt, ExpiresAt, UserId+IsRead partial). Add connection string config and EF Core migration
- [X] T012 [US1] Create `IEventService` interface in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IEventService.cs` and `EventService` implementation in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventService.cs` — methods: GetEventsAsync (paginated, filtered), GetUnreadCountAsync, MarkReadAsync (specific IDs or all), CreateEventAsync, GetAdminEventsAsync (org-wide), DeleteEventAsync. Inject `BlueprintEventsDbContext`
- [X] T013 [US1] Create `EventCleanupService` (IHostedService) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventCleanupService.cs` — daily timer, deletes events where ExpiresAt < UtcNow
- [X] T014 [US1] Create `EventEndpoints.cs` in `src/Services/Sorcha.Blueprint.Service/Endpoints/EventEndpoints.cs` implementing: GET `/api/events` (user's events, paginated), GET `/api/events/unread-count`, POST `/api/events/mark-read`, POST `/api/events` (service-to-service create), GET `/api/events/admin` (admin org-wide), DELETE `/api/events/{id}` — per `contracts/events-api.md`. Register in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [X] T015 [US1] Create `EventsHub` in `src/Services/Sorcha.Blueprint.Service/Hubs/EventsHub.cs` — SignalR hub at `/hubs/events`, JWT auth via query param, methods: Subscribe (join `user:{userId}` group), SubscribeOrg (admin join `org:{orgId}`), Unsubscribe/UnsubscribeOrg. Client method: `EventReceived(ActivityEventDto)`, `UnreadCountUpdated(int count)`. Map hub in Program.cs
- [X] T016 [P] [US1] Create `ActivityEventDto` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/ActivityEventDto.cs` and `IActivityLogService` / `ActivityLogService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActivityLogService.cs` — HTTP client for events REST API + SignalR connection to EventsHub, exposes events stream, unread count, mark-read. Register in DI
- [X] T017 [US1] Create `ActivityLogPanel.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/ActivityLogPanel.razor` — right-side MudDrawer overlay (Anchor.End), shows events newest-first grouped by date (Today, Yesterday, specific dates), severity icon per event, "Mark All Read" button, virtual scroll for large lists. Injects `IActivityLogService`
- [X] T018 [US1] Modify `MainLayout.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — add bell icon (`MudIconButton`) with `MudBadge` unread count in MudAppBar, wire click to open ActivityLogPanel overlay, connect to ActivityLogService for real-time badge updates. Remove or deprecate MudSnackbar usage for events that now go to activity log
- [X] T019 [P] [US1] Write unit tests in `tests/Sorcha.Blueprint.Service.Tests/Services/EventServiceTests.cs` — test GetEventsAsync pagination, GetUnreadCountAsync, MarkReadAsync (specific + all), CreateEventAsync, admin org-wide filter, 90-day expiry setting
- [X] T020 [P] [US1] Write endpoint tests in `tests/Sorcha.Blueprint.Service.Tests/Endpoints/EventEndpointTests.cs` — test each endpoint: GET events returns paginated list, unread count, mark-read, admin-only access, service-to-service create, delete own event only

**Checkpoint**: Activity log fully functional — events created, persisted, pushed via SignalR, displayed in overlay with unread badge

---

## Phase 4: User Story 2 — Sidebar Navigation Consolidation (Priority: P1)

**Goal**: Merge Management and Admin into single alphabetical "Administration" section, add hamburger toggle with icon-only collapse mode, role-based item visibility.

**Independent Test**: Log in as system admin → verify single "Administration" section with all items alphabetical. Log in as org admin → verify only Participants/Registers/Users visible. Toggle hamburger → verify icon-only collapse.

### Implementation for User Story 2

- [X] T021 [US2] Modify sidebar navigation in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` — replace separate MANAGEMENT and ADMIN `MudNavGroup`s with single "Administration" `MudNavGroup`. Items alphabetically: Organisations, Participants, Peer Network, Registers, Schema Providers, Schema Sectors, Service Principals, System Health, Users, Validator. Add role-checking logic using `AuthenticationStateProvider` to show/hide items (SystemAdmin=all, Administrator=Participants+Registers+Users, regular user=none)
- [X] T022 [US2] Implement hamburger toggle with icon-only collapse in `MainLayout.razor` — change `MudDrawer` to use `Variant="DrawerVariant.Mini"` with `ClipMode="DrawerClipMode.Always"`, bind `@bind-Open` to drawer state, add hamburger `MudIconButton` in `MudAppBar`. When collapsed: drawer shows only MudNavLink icons. When expanded: full labels visible
- [X] T023 [US2] Add MudNavLink `Icon` properties to all navigation items in `MainLayout.razor` — each nav item needs an appropriate MudBlazor icon so icon-only mode is usable (e.g., Dashboard=Icons.Material.Filled.Dashboard, Wallets=Icons.Material.Filled.AccountBalanceWallet, Blueprints=Icons.Material.Filled.Architecture, etc.)

**Checkpoint**: Sidebar shows consolidated admin section with role filtering and hamburger collapse

---

## Phase 5: User Story 3 — Status Footer Bar (Priority: P1)

**Goal**: Persistent thin footer showing app version, backend health status (green/red indicator), and pending action count with link.

**Independent Test**: View any page → verify footer visible with version string + green "Connected" indicator. Stop backend → verify "Offline" shown. Check pending action count matches actions page.

### Implementation for User Story 3

- [X] T024 [US3] Create `StatusFooter.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/StatusFooter.razor` — thin bar at bottom of page showing: version string (from assembly), health indicator (green dot + "Connected" or red dot + "Offline"), pending action count as `MudLink` to `/pending-actions`. Injects HttpClient for health check polling (every 30s) and actions count
- [X] T025 [US3] Modify `MainLayout.razor` to add `<StatusFooter />` below `MudMainContent` — position fixed at bottom, full width, styled with thin height (~32px), subtle background. Adjust MudMainContent padding-bottom to prevent content overlap
- [X] T026 [P] [US3] Write unit test for StatusFooter health check logic in `tests/Sorcha.UI.Core.Tests/Services/StatusFooterTests.cs` — test connected state, offline state, pending action count display, link navigation

**Checkpoint**: Footer visible on all pages with live health indicator and pending count

---

## Phase 6: User Story 4 — Wallet Management Improvements (Priority: P2)

**Goal**: Default wallet selection (server-side), list/card view toggle, QR code display per wallet, share/copy mechanism, PQC algorithm support.

**Independent Test**: Create 2 wallets → set one as default → verify it's pre-selected in signing. Toggle list/card view. Click QR icon → verify QR dialog. Click share → verify address copied.

**Depends on**: Phase 2 (UserPreferences API for default wallet)

### Implementation for User Story 4

- [x] T027 [US4] Modify `WalletList.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletList.razor` — add view toggle (MudToggleIconButton for card/list), add "Set as Default" button per wallet (calls UserPreferencesService.SetDefaultWalletAsync), add default indicator (star icon or badge), add QR icon button per wallet (opens WalletQrDialog), add share/copy button (navigator.clipboard.writeText via JS interop). In list mode: render `MudTable` with columns Name, Full Address, Algorithm, Status, Created, Actions
- [x] T028 [US4] Create `WalletQrDialog.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Shared/WalletQrDialog.razor` — MudDialog that receives wallet address as parameter, renders QR code via JS interop to qrcode.js, shows address text below QR, copy button
- [x] T029 [US4] Modify `CreateWallet.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor` — add ML-DSA-65 and ML-KEM-768 to algorithm selection dropdown alongside existing ED25519, NIST P-256, RSA-4096. Update algorithm descriptions and chip colors for new algorithms
- [x] T030 [US4] Migrate existing `WalletPreferenceService` (localStorage-based) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WalletPreferenceService.cs` — on app startup, check localStorage for `sorcha:preferences:defaultWallet`, if found write to server-side UserPreferencesService, clear localStorage key. Update all callers to use `UserPreferencesService` instead
- [x] T031 [US4] Modify signing flow pages to pre-select default wallet — update `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmission.razor` and any action submission pages that include a wallet selector to call `UserPreferencesService.GetDefaultWalletAsync()` on load and pre-populate the wallet dropdown. Fall back to manual selection if no default is set (FR-014)
- [x] T032 [P] [US4] Write unit tests for wallet QR dialog and default wallet flow in `tests/Sorcha.UI.Core.Tests/Wallet/WalletPreferenceMigrationTests.cs` — test localStorage migration to server-side, default wallet set/get via UserPreferencesService

**Checkpoint**: Wallet list has card/list toggle, default selection, QR codes, share, and PQC algorithms available. Default wallet pre-selected in signing flows.

---

## Phase 7: User Story 5 — Dashboard Wizard Conditional Display (Priority: P2)

**Goal**: Show wallet creation wizard only when user has no default wallet. Standard KPI dashboard otherwise.

**Independent Test**: New user (no wallets) → verify wizard shown. Complete wizard → verify default wallet set and KPI dashboard shown. Delete all wallets → verify wizard reappears.

**Depends on**: Phase 2 (UserPreferences API for default wallet check)

### Implementation for User Story 5

- [x] T033 [US5] Modify `Home.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` — on page load, call `UserPreferencesService.GetDefaultWalletAsync()`. If null: show wallet creation wizard (reuse/embed CreateWallet flow). If set: show existing KPI dashboard. After wizard completion: set default wallet via PUT `/api/preferences/default-wallet`, then switch to KPI view
- [x] T034 [US5] Update `CreateWallet.razor` wizard completion flow — after wallet created, if this is the first wallet (wizard context), automatically set it as default via `UserPreferencesService` and navigate to dashboard

**Checkpoint**: Dashboard conditionally shows wizard or KPIs based on default wallet existence

---

## Phase 8: User Story 6 — Real-time Validator Dashboard (Priority: P2)

**Goal**: Validator admin page shows live stats via SignalR — monitored registers, processing status, throughput, queue depth, last-processed timestamps.

**Independent Test**: Navigate to validator page while transactions are processing → verify stats update in real-time without manual refresh.

### Implementation for User Story 6

- [x] T035 [US6] Modify `ValidatorPanel` component (rendered by `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Validator.razor`) — add SignalR connection to validator status endpoint, display real-time: monitored registers table (register name, chain height, last validated block, processing status), dockets/minute throughput, queue depth, last-processed timestamp. Show "Idle" with time-since-last-activity when not processing. Use `MudTable` with auto-refresh rows
- [x] T036 [US6] Create validator stats SignalR integration — either extend existing `RegisterHub` with validator status methods or create minimal validator status endpoint that the UI polls via timer (if dedicated hub is too heavy). Prefer timer-based polling at 3-second intervals hitting GET `/api/admin/validator/status` for simplicity, with `MudProgressLinear` for throughput visualization
- [x] T037 [P] [US6] Write unit test for validator dashboard real-time update logic in `tests/Sorcha.UI.Core.Tests/Admin/ValidatorDashboardTests.cs`

**Checkpoint**: Validator dashboard updates statistics in real-time

---

## Phase 9: User Story 7 — User Settings Expansion (Priority: P3)

**Goal**: Full settings page with tabs: Theme (Light/Dark/System), Time (UTC/Local), Language (en/fr/de/es with browser detection), 2FA (TOTP setup with QR), Push Notifications with Web Push API.

**Independent Test**: Change theme to Dark → verify immediate switch. Change language to French → verify UI text changes. Enable 2FA → verify authenticator QR code shown and login requires code. Enable push notifications → verify browser notification prompt and delivery.

**Depends on**: Phase 2 (UserPreferences API), Phase 1 (OtpNet package, i18n files)

### Implementation for User Story 7

- [x] T038 [P] [US7] Create `TotpConfiguration` entity in `src/Services/Sorcha.Tenant.Service/Models/TotpConfiguration.cs` per data-model.md (Id, UserId FK, EncryptedSecret, BackupCodes, BackupCodesUsed, IsEnabled, IsVerified, CreatedAt, VerifiedAt). Add to `TenantDbContext` with unique index on UserId, create EF Core migration
- [x] T039 [P] [US7] Create `ITotpService` interface in `src/Services/Sorcha.Tenant.Service/Services/Interfaces/ITotpService.cs` and `TotpService` implementation in `src/Services/Sorcha.Tenant.Service/Services/Implementation/TotpService.cs` — methods: SetupAsync (generate secret + backup codes), VerifyAsync (validate TOTP or backup code), DisableAsync (require valid code), GetStatusAsync, RegenerateBackupCodesAsync. Use OtpNet for TOTP, AES-256-GCM for secret encryption, BCrypt for backup codes
- [x] T040 [US7] Create `TotpEndpoints.cs` in `src/Services/Sorcha.Tenant.Service/Endpoints/TotpEndpoints.cs` implementing: POST `/api/totp/setup`, POST `/api/totp/verify`, DELETE `/api/totp`, GET `/api/totp/status`, POST `/api/totp/backup-codes/regenerate` — per `contracts/totp-api.md`. Register in `src/Services/Sorcha.Tenant.Service/Program.cs`. Add rate limiting (5 verify attempts/min, 3 setup calls/hour)
- [x] T041 [US7] Modify `/api/auth/login` endpoint in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` — after password verification, check if user has 2FA enabled. If yes: return `{ requiresTwoFactor: true, loginToken: <short-lived JWT> }` instead of full tokens. Add POST `/api/auth/login/complete` endpoint that accepts loginToken after TOTP verification and issues full tokens
- [x] T042 [P] [US7] Create `ThemeService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ThemeService.cs` — manages dark mode state, persists to UserPreferencesService, detects OS preference via JS interop `window.matchMedia('(prefers-color-scheme: dark)')`, fires state change events for MainLayout binding
- [x] T043 [P] [US7] Create `LocalizationService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/LocalizationService.cs` — loads JSON from `wwwroot/i18n/{locale}.json` via HttpClient, caches strings, exposes `T(key)` method for string lookup, detects browser language via JS interop `navigator.language`, falls back to English. Register as singleton in DI
- [x] T044 [P] [US7] Create `TotpService` (UI client) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TotpService.cs` — HTTP client calling Tenant Service TOTP API (setup, verify, disable, status, regenerate). Register in DI
- [x] T045 [P] [US7] Create `TimeFormatService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TimeFormatService.cs` — reads TimeFormat preference from UserPreferencesService (UTC or Local), exposes `FormatTimestamp(DateTimeOffset)` method that returns formatted string with timezone abbreviation for Local mode or "UTC" suffix for UTC mode. Apply across all pages that display timestamps: activity log events, wallet created dates, transaction timestamps, validator last-processed times (FR-023)
- [x] T046 [US7] Create French, German, Spanish translation files: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/i18n/fr.json`, `de.json`, `es.json` — translate all keys from en.json
- [x] T047 [US7] Apply localization calls to all existing Razor pages — replace hardcoded English strings across `MainLayout.razor`, `Home.razor`, `WalletList.razor`, `CreateWallet.razor`, `Settings.razor`, `Validator.razor`, `NewSubmission.razor`, and all admin pages with `LocalizationService.T("key")` calls. Cover navigation labels, button text, page titles, status messages, and error messages (FR-024)
- [x] T048 [US7] Modify `Settings.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor` — restructure as `MudTabs` with tabs: Appearance (theme toggle Light/Dark/System, time format UTC/Local), Language (selector with flag icons, browser-detected default shown), Security (2FA setup: enable button → show QR + secret + backup codes via TotpService, disable button with code verification, backup code regeneration), Notifications (push notification toggle with subscription management), Connections (existing profile management), About (existing info). Each setting change calls UserPreferencesService PUT
- [x] T049 [US7] Wire ThemeService into `MainLayout.razor` — bind `MudThemeProvider IsDarkMode` to ThemeService.IsDarkMode, listen for System preference changes via JS interop MediaQueryList listener, apply on startup from loaded UserPreferences
- [x] T050 [P] [US7] Create push notification service worker in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/wwwroot/service-worker.published.js` — handle push events, display browser notifications for pending action alerts. Generate VAPID key pair and store in Tenant Service configuration (FR-026)
- [x] T051 [P] [US7] Create push subscription endpoints on Tenant Service — POST `/api/push/subscribe` (store PushSubscription JSON in UserPreferences or new PushSubscription entity), DELETE `/api/push/unsubscribe`, in `src/Services/Sorcha.Tenant.Service/Endpoints/PushSubscriptionEndpoints.cs`. Register in Program.cs (FR-026)
- [x] T052 [US7] Create `PushNotificationService` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PushNotificationService.cs` — when creating an activity event for an offline user (no active SignalR connection), send Web Push notification via stored subscription using `WebPush` NuGet package. Integrate with EventService.CreateEventAsync flow (FR-026)
- [x] T053 [P] [US7] Write unit tests for TOTP service in `tests/Sorcha.Tenant.Service.Tests/Endpoints/TotpEndpointTests.cs` — test setup flow, verify valid/invalid codes, backup code consumption, disable with valid code, rate limiting, status check
- [x] T054 [P] [US7] Write unit tests for ThemeService, LocalizationService, and TimeFormatService in `tests/Sorcha.UI.Core.Tests/Services/ThemeServiceTests.cs`, `tests/Sorcha.UI.Core.Tests/Services/LocalizationServiceTests.cs`, and `tests/Sorcha.UI.Core.Tests/Services/TimeFormatServiceTests.cs`

**Checkpoint**: Settings page fully functional with theme, language, time format, 2FA, and push notifications

---

## Phase 10: User Story 8 — CLI Full API Coverage (Priority: P2)

**Goal**: 5 new CLI command groups covering Blueprint, Participant, Credential, Validator, and Admin APIs. 100% backend API coverage.

**Independent Test**: Run `sorcha blueprint list`, `sorcha participant list`, `sorcha credential list`, `sorcha validator status`, `sorcha admin health` — all return valid output with `--output json`.

### Implementation for User Story 8

- [x] T055 [P] [US8] Create `BlueprintCommand.cs` in `src/Apps/Sorcha.Cli/Commands/BlueprintCommand.cs` — subcommands: list, get, create (from JSON file), publish, delete, versions, instances. Each uses HttpClient to call Blueprint Service API. Register in `src/Apps/Sorcha.Cli/Program.cs`
- [x] T056 [P] [US8] Create `ParticipantCommand.cs` in `src/Apps/Sorcha.Cli/Commands/ParticipantCommand.cs` — subcommands: register, list, get, update, search, wallet-link, wallet-link-verify. Calls Tenant Service Participant Identity API. Register in Program.cs
- [x] T057 [P] [US8] Create `CredentialCommand.cs` in `src/Apps/Sorcha.Cli/Commands/CredentialCommand.cs` — subcommands: list, get, issue, present, verify, revoke, status. Calls credential endpoints via API Gateway. Register in Program.cs
- [x] T058 [P] [US8] Create `ValidatorCommand.cs` in `src/Apps/Sorcha.Cli/Commands/ValidatorCommand.cs` — subcommands: status, start, stop, process, integrity-check. Calls Validator Service admin API. Register in Program.cs
- [x] T059 [P] [US8] Create `AdminCommand.cs` in `src/Apps/Sorcha.Cli/Commands/AdminCommand.cs` — subcommands: health (all services), schema-sectors (list, get), schema-providers (list, get), alerts. Calls various admin endpoints via API Gateway. Register in Program.cs
- [x] T060 [P] [US8] Write CLI command tests in `tests/Sorcha.Cli.Tests/Commands/BlueprintCommandTests.cs`, `ParticipantCommandTests.cs`, `CredentialCommandTests.cs` — test command parsing, option handling, output format switching (table/json/csv), error handling

**Checkpoint**: CLI has 100% backend API coverage — all 18 command groups registered

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Integration testing, documentation, build verification, localization completeness

- [x] T061 Full solution build verification — run `dotnet build` from solution root, fix any compilation errors across all modified projects
- [x] T062 [P] Run all tests — `dotnet test` from solution root, ensure no regressions, >85% coverage on new code
- [x] T063 [P] Update MASTER-TASKS.md in `.specify/MASTER-TASKS.md` — mark 043-ui-cli-modernization tasks as complete
- [x] T064 [P] Update documentation: API Gateway YARP routes for new endpoints (events, preferences, totp, push) in `src/Services/Sorcha.ApiGateway/` config, update `docs/development-status.md` with new feature status, add XML summary docs to all new endpoint methods
- [x] T065 Docker rebuild and smoke test — `docker-compose build --no-cache && docker-compose up -d`, verify all services healthy, run quickstart.md verification checklist
- [x] T066 Run quickstart.md validation — execute all items in the verification checklist from `specs/043-ui-cli-modernization/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (OtpNet package) — BLOCKS US4, US5, US7
- **US1 Activity Log (Phase 3)**: Can start after Phase 1 — NO dependency on Phase 2
- **US2 Sidebar (Phase 4)**: Can start after Phase 1 — NO dependency on Phase 2
- **US3 Footer (Phase 5)**: Can start after Phase 1 — NO dependency on Phase 2
- **US4 Wallet Mgmt (Phase 6)**: Depends on Phase 2 (UserPreferences API)
- **US5 Dashboard (Phase 7)**: Depends on Phase 2 (UserPreferences default wallet)
- **US6 Validator (Phase 8)**: Can start after Phase 1 — NO dependency on Phase 2
- **US7 Settings (Phase 9)**: Depends on Phase 2 (UserPreferences) + Phase 1 (OtpNet, i18n)
- **US8 CLI (Phase 10)**: Can start after Phase 1 — NO dependency on Phase 2
- **Polish (Phase 11)**: Depends on all desired phases complete

### User Story Dependencies

```
Phase 1 (Setup)
    │
    ├──► Phase 2 (Foundational: UserPreferences) ──► US4 (Wallet Mgmt)
    │                                              ──► US5 (Dashboard Wizard)
    │                                              ──► US7 (Settings)
    │
    ├──► US1 (Activity Log) ─── independent ──► can start immediately
    ├──► US2 (Sidebar) ──────── independent ──► can start immediately
    ├──► US3 (Footer) ────────── independent ──► can start immediately
    ├──► US6 (Validator) ─────── independent ──► can start immediately
    └──► US8 (CLI) ───────────── independent ──► can start immediately
```

### Within Each User Story

- Models/Entities before Services
- Services before Endpoints/UI
- Backend before Frontend (where backend is needed)
- Implementation before Tests (tests can be parallel where marked [P])
- Commit after each task or logical group

### Parallel Opportunities

**Immediate parallel starts (after Phase 1)**:
- US1 + US2 + US3 + US6 + US8 — all independent, different files
- Phase 2 (Foundational) — runs alongside the above

**After Phase 2 completes**:
- US4 + US5 + US7 — can all proceed (different files)

**Within stories** (all [P] tasks within same story):
- US1: T010 + T011 in parallel (entity + db context), T019 + T020 in parallel (tests)
- US7: T038 + T039 + T042 + T043 + T044 + T045 + T050 + T051 in parallel (8 different files)
- US8: T055 + T056 + T057 + T058 + T059 all in parallel (5 independent command files)

---

## Parallel Example: User Story 1

```bash
# Step 1: Launch entity + db context in parallel:
Task: T010 "Create ActivityEvent entity in .../Models/ActivityEvent.cs"
Task: T011 "Create BlueprintEventsDbContext in .../Data/BlueprintEventsDbContext.cs"

# Step 2: Service depends on both T010+T011:
Task: T012 "Create EventService in .../Services/Implementation/EventService.cs"

# Step 3: Endpoints + hub + cleanup depend on T012:
Task: T013 "Create EventCleanupService" (parallel with T014)
Task: T014 "Create EventEndpoints" (parallel with T013)
Task: T015 "Create EventsHub" (parallel with T013, T014)

# Step 4: UI client + component:
Task: T016 "Create ActivityLogService in UI.Core"
Task: T017 "Create ActivityLogPanel.razor"
Task: T018 "Modify MainLayout.razor for bell icon"

# Step 5: Tests in parallel:
Task: T019 "EventServiceTests"
Task: T020 "EventEndpointTests"
```

## Parallel Example: User Story 8 (CLI)

```bash
# All 5 command files are independent - launch all in parallel:
Task: T055 "BlueprintCommand.cs"
Task: T056 "ParticipantCommand.cs"
Task: T057 "CredentialCommand.cs"
Task: T058 "ValidatorCommand.cs"
Task: T059 "AdminCommand.cs"

# Then tests:
Task: T060 "CLI command tests"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 3: US1 Activity Log (T010-T020)
3. **STOP and VALIDATE**: Test activity log independently
4. Deploy/demo if ready — this is the single biggest UX improvement

### Incremental Delivery

1. Setup → **Foundation ready**
2. US1 (Activity Log) → Test → **MVP!** (biggest impact)
3. US2 (Sidebar) + US3 (Footer) → Test → **P1 complete**
4. Phase 2 (Foundational) → **Unblocks P2/P3 wallet features**
5. US4 (Wallet) + US5 (Dashboard) + US6 (Validator) + US8 (CLI) → Test → **P2 complete**
6. US7 (Settings/i18n/2FA/Push) → Test → **P3 complete**
7. Polish → **Feature complete**

### Recommended Execution Order (Sequential)

```
T001-T003 → T010-T020 → T021-T023 → T024-T026 → T004-T009 →
T027-T032 → T033-T034 → T035-T037 → T038-T054 → T055-T060 →
T061-T066
```

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- US1 is the recommended MVP — biggest UX impact, no dependency on Phase 2
- US8 (CLI) is fully parallelizable with all other stories
