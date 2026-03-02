# Tasks: UI Polish & Blueprint Designer

**Input**: Design documents from `/specs/046-ui-polish-designer/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/blueprint-api-update.md, quickstart.md

**Tests**: bUnit tests included for new code and critical bug fixes (constitution IV compliance). Manual verification steps defined per checkpoint.

**Organization**: Tasks grouped by user story (US1-US7) mapping to spec.md priorities.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1-US7)
- Include exact file paths in descriptions

## Path Conventions

All paths relative to repository root. UI source lives in:
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/` — shared components, services
- `src/Apps/Sorcha.UI/Sorcha.UI.Web/` — web host, static assets
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/` — Blazor WASM pages

---

## Phase 1: Setup

**Purpose**: Verify build baseline before any changes

- [x] T001 Verify all 3 UI projects compile cleanly: `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web/Sorcha.UI.Web.csproj`, `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj`, `dotnet build src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj`

**Checkpoint**: Build passes — safe to begin implementation.

---

## Phase 2: US1 — Dashboard Correctly Welcomes Returning Users (P1) MVP

**Goal**: Fix the 3-bug chain causing the wizard loop and GUID welcome message. Users with wallets see the dashboard with their friendly display name.

**Independent Test**: Login → verify welcome shows display name (not GUID) → verify wizard does not appear when wallet exists → create first wallet → verify it becomes default and wizard stops.

### Implementation

- [x] T002 [P] [US1] Set `_jwtHandler.MapInboundClaims = false` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Authentication/CustomAuthenticationStateProvider.cs` to prevent JWT claim remapping
- [x] T003 [US1] Refactor `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor`: replace `IUserPreferencesService` with `IWalletPreferenceService` + `IWalletApiService`, call `GetSmartDefaultAsync(wallets)`, remove `_stats.TotalWallets == 0` redirect, update welcome text to use `context.User.FindFirst("name")?.Value` with fallback chain (name → preferred_username → email → "Welcome back")
- [x] T004 [P] [US1] Fix `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor`: always call `SetDefaultWalletAsync` on first wallet creation in both `?wizard=true` and `?first-login=true` paths (lines 264-271)
- [ ] T041 [US1] Write bUnit test for Home.razor wizard conditional in `tests/Sorcha.UI.Core.Tests/`: verify dashboard shown (not wizard) when user has ≥1 wallet, verify wizard shown for zero wallets, verify display name rendered (not GUID)

**Checkpoint**: Dashboard shows friendly name, wizard only appears for zero-wallet users, first wallet auto-becomes default. bUnit test passes.

---

## Phase 3: US2 — Notification Panel Stays Hidden When Closed (P1)

**Goal**: Fix layout overflow caused by ActivityLogPanel rendered outside MudLayout.

**Independent Test**: Close notification panel → scroll horizontally on any page → confirm panel is invisible and no horizontal scrollbar appears.

### Implementation

- [x] T005 [P] [US2] Move `<ActivityLogPanel>` from after `</MudLayout>` to inside `</MudLayout>` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/MainLayout.razor` (line 188)
- [x] T006 [P] [US2] Add `overflow-x: hidden` to `html, body` style block in `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html`

**Checkpoint**: No horizontal scroll on any page when notification panel is closed.

---

## Phase 4: US3 — Dark Mode Renders All Pages Readably (P1)

**Goal**: Replace all hardcoded color values with MudBlazor CSS variables. Expand PaletteDark.

**Independent Test**: Enable dark mode → navigate to wallet creation, wallet detail, blueprint designer → confirm all text readable and no white/light backgrounds remain.

### Implementation

- [x] T007 [US3] Expand `PaletteDark` definition in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/MainLayout.razor` (lines 226-239) with AppbarBackground, Surface, Background, DrawerBackground, TextPrimary, TextSecondary
- [x] T008 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor`: `background: linear-gradient(135deg, #fff3e0 0%, #ffe0b2 100%)` at line 108 → `var(--mud-palette-background-grey)`, `background: white` at line 128 → `var(--mud-palette-surface)`
- [x] T009 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor`: `#e8f5e9` at line 266 and `white` at line 274 → `var(--mud-palette-surface)` and `var(--mud-palette-text-primary)`
- [x] T010 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/ActionNodeWidget.razor` embedded `<style>`: replace `white`, `#f8f9fa`, `#555`, `#424242` etc. with `var(--mud-palette-surface)`, `var(--mud-palette-background)`, `var(--mud-palette-text-secondary)`, `var(--mud-palette-text-primary)`
- [x] T011 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/ReadOnlyActionNodeWidget.razor.css`: `background: white` → `var(--mud-palette-surface)` and similar
- [x] T012 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/PropertiesPanel.razor` embedded `<style>`: `white`, `#f5f5f5`, `#e0e0e0` → theme variables
- [x] T013 [P] [US3] Replace hardcoded colors in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/BlueprintViewerDiagram.razor.css`: `#f5f5f5`, `white`, `#424242` → theme variables

**Checkpoint**: All pages readable in both light and dark mode. No hardcoded light backgrounds in modified files.

---

## Phase 5: US4 — Stale "Coming Soon" Labels Removed (P2)

**Goal**: Enable implemented features, replace stale labels with accurate status text.

**Independent Test**: Navigate to wallet creation → confirm PQC algorithms selectable. Open wallet detail → confirm transaction tab shows data. Open Settings → confirm TOTP flow calls backend.

### Implementation

- [x] T014 [P] [US4] Enable ML-DSA-65 and ML-KEM-768 in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/CreateWallet.razor` (lines 51-52): remove `Disabled="true"` and `"(Coming Soon)"` text from dropdown options
- [x] T015 [P] [US4] Replace "Transaction History Coming Soon" placeholder in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor` (lines 295-302) with actual transaction query using Register Service (follow `MyTransactions.razor` pattern)
- [x] T016 [P] [US4] Wire TOTP setup/verify/disable in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor` (lines 513-535) to actual `TotpClientService` endpoints, replace placeholder `Snackbar.Add("not yet connected")` calls
- [x] T017 [P] [US4] Reword "Coming Soon" labels in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OrganizationConfiguration.razor` (lines 19, 51, 62) to specific status text: "External identity provider integration is planned for a future release"
- [x] T018 [P] [US4] Replace "Presentation flow coming soon" in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor` (line 243) with actual presentation initiation or specific status text

**Checkpoint**: PQC algorithms selectable, TOTP flow works, transaction tab loads data, no generic "Coming Soon" labels remain for implemented features.

---

## Phase 6: US5 — Blueprint Visual Designer Load/Save (P2)

**Goal**: Wire designer to Blueprint Service API for persistent save/load, replacing LocalStorage-only behavior.

**Independent Test**: Create blueprint in designer → save → navigate away → return → load → verify all elements (participants, actions, routes, schemas, layout) preserved.

### Implementation

- [x] T019 [P] [US5] Add `UpdateBlueprintAsync(string id, object blueprint, CancellationToken ct)` to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IBlueprintApiService.cs` interface
- [x] T020 [US5] Implement `UpdateBlueprintAsync` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/BlueprintApiService.cs` wrapping `PUT /api/blueprints/{id}`, returning `BlueprintListItemViewModel?`
- [x] T021 [US5] Wire `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer.razor` to `IBlueprintApiService`: add `@inject`, add `[SupplyParameterFromQuery(Name = "id")] string? BlueprintId` parameter, implement `OnParametersSetAsync` to auto-load when `?id=` is provided
- [x] T022 [US5] Replace `SaveBlueprint()` in Designer.razor (lines 489-523): use API `SaveBlueprintAsync` (POST) for new blueprints or `UpdateBlueprintAsync` (PUT) for existing based on `_persistedBlueprintId`
- [x] T023 [US5] Replace `LoadBlueprint(id)` in Designer.razor (lines 525-586): change from LocalStorage read to `GetBlueprintDetailAsync(id)` API call
- [x] T024 [US5] Add `_hasUnsavedChanges` bool tracking and `<NavigationLock OnBeforeInternalNavigation="OnBeforeNav" ConfirmExternalNavigation="_hasUnsavedChanges" />` to Designer.razor for unsaved-changes prompt
- [x] T025 [US5] Add LocalStorage offline draft fallback in Designer.razor: save to both API and LocalStorage, use local copy when offline
- [ ] T042 [US5] Write bUnit test for Designer.razor save/load flow in `tests/Sorcha.UI.Core.Tests/`: verify SaveBlueprint calls API create for new blueprints and API update for existing, verify LoadBlueprint populates diagram from API response

**Checkpoint**: Full save/load cycle through API works. Unsaved changes prompt appears. Blueprints persist across sessions. bUnit test passes.

---

## Phase 7: US6 — Integrated Notification System (P3)

**Goal**: Build EventsHub client and wire to ActivityLogPanel for real-time notifications. Migrate highest-value SignalR snackbars.

**Soft dependency**: T005 (US2 panel placement fix) should be done first — EventsHub wires into ActivityLogPanel which needs correct positioning.

**Independent Test**: Trigger an action event → verify activity log badge increments in real-time → open panel → confirm event appears.

### Implementation

- [x] T026 [US6] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs`: SignalR client following `ActionsHubConnection` pattern — connect to `/hubs/events`, expose `OnEventReceived` (`Action<ActivityEventDto>`) and `OnUnreadCountUpdated` (`Action<int>`) events, implement `ConnectAsync()`/`DisconnectAsync()`. Include structured logging via `ILogger<EventsHubConnection>` for connection state changes (connected/disconnected/reconnecting) and errors (constitution VIII compliance)
- [x] T027 [US6] Register `EventsHubConnection` as scoped service in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs`
- [x] T028 [US6] Wire EventsHubConnection into `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/MainLayout.razor`: inject, connect on `OnInitializedAsync`, subscribe to `OnUnreadCountUpdated` to refresh notification badge count in real-time
- [x] T029 [US6] Wire EventsHubConnection into `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/ActivityLogPanel.razor`: subscribe to `OnEventReceived` to prepend new events when panel is open
- [x] T030 [US6] Migrate 4 SignalR handler `Snackbar.Add` calls in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` to use activity log instead
- [x] T031 [P] [US6] Remove unused `ISnackbar` injections from `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyWallet.razor` and `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyTransactions.razor`
- [ ] T043 [US6] Write bUnit test for EventsHubConnection in `tests/Sorcha.UI.Core.Tests/`: verify ConnectAsync establishes SignalR connection, verify OnEventReceived fires when server sends EventReceived, verify OnUnreadCountUpdated fires on UnreadCountUpdated

**Checkpoint**: EventsHub connects on app load, activity badge updates in real-time, new events appear in panel live, migrated handlers no longer show toasts. bUnit test passes.

---

## Phase 8: US7 — Localization Wiring (P3)

**Goal**: Wire existing ILocalizationService into highest-visibility components so language switching has visible effect.

**Soft dependency**: T003 (US1 welcome text refactor) should be done first — localization replaces the same welcome text strings.

**Independent Test**: Switch language to French in Settings → verify navigation labels, dashboard welcome, and Settings headings display in French.

### Implementation

- [x] T032 [US7] Wire `ILocalizationService` into `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/MainLayout.razor`: `@inject ILocalizationService Loc`, replace navigation labels ("Dashboard", "Wallets", "Designer", etc.) with `Loc.T("nav.dashboard")` etc. Subscribe to LocalizationService language-change event and call `StateHasChanged()` to propagate re-render to all child components without full page reload (satisfies FR-026)
- [ ] T033 [P] [US7] Wire `ILocalizationService` into `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor`: replace welcome text and dashboard labels with `Loc.T()` calls
- [ ] T034 [P] [US7] Wire `ILocalizationService` into `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`: replace tab labels and section headings with `Loc.T()` calls
- [ ] T035 [US7] Add any missing translation keys discovered during wiring to all 4 i18n JSON files: `src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/i18n/en.json`, `fr.json`, `de.json`, `es.json`

**Checkpoint**: Language switch in Settings produces visible changes in navigation, dashboard, and Settings page.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Verify everything works together, update documentation

- [ ] T036 Build verification: `dotnet build` all 3 UI projects clean with no warnings
- [ ] T037 Run existing bUnit tests: `dotnet test tests/Sorcha.UI.Core.Tests/`
- [ ] T038 Update `.specify/MASTER-TASKS.md` with task 046 completion status
- [ ] T039 Update `docs/development-status.md` if UI status changes
- [ ] T040 Run quickstart.md manual verification steps (6 scenarios)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **US1-US4 (Phases 2-5)**: All independent — can execute in parallel after Setup
- **US5 (Phase 6)**: Independent — can parallel with all other stories
- **US6 (Phase 7)**: Soft dependency on US2 (T005 panel placement fix)
- **US7 (Phase 8)**: Soft dependency on US1 (T003 welcome text refactor)
- **Polish (Phase 9)**: After all user stories complete

### Shared File Conflicts (Parallel Execution Warning)

These files are modified by multiple user stories. When executing stories in parallel, coordinate edits to avoid merge conflicts:

| File | Modified By | Conflict Risk |
|------|-------------|---------------|
| `MainLayout.razor` | US2 (T005), US3 (T007), US6 (T028), US7 (T032) | **HIGH** — 4 stories touch this file |
| `CreateWallet.razor` | US1 (T004), US3 (T008), US4 (T014) | **MEDIUM** — different sections |
| `WalletDetail.razor` | US3 (T009), US4 (T015) | **LOW** — different sections |
| `Home.razor` | US1 (T003), US7 (T033) | **MEDIUM** — US7 depends on US1 |
| `Settings.razor` | US4 (T016), US7 (T034) | **LOW** — different sections |

**Recommended sequencing for MainLayout.razor**: US2 (T005) → US3 (T007) → US6 (T028) → US7 (T032)

### Within Each User Story

- Interface before implementation (T019 → T020)
- Service registration before usage (T027 → T028)
- Foundation before details (T007 before T008-T013)
- All [P]-marked tasks within a story can run in parallel

### Parallel Opportunities

```
After Setup (T001):
  ├── US1 (T002-T004, T041) ─── 4 tasks, 2 parallel + test
  ├── US2 (T005-T006) ─── 2 tasks, fully parallel
  ├── US3 (T007-T013) ─── 7 tasks, 6 parallel after T007
  ├── US4 (T014-T018) ─── 5 tasks, fully parallel
  └── US5 (T019-T025, T042) ─── 8 tasks, sequential chain + test
After US2: US6 (T026-T031, T043) ─── 7 tasks, mostly sequential + test
After US1: US7 (T032-T035) ─── 4 tasks, 2 parallel
After all: Polish (T036-T040)
```

---

## Parallel Example: US3 (Dark Mode) + US4 (Coming Soon)

```
# These two stories are fully independent — launch together:

# US3: First do palette expansion, then all file fixes in parallel
Agent 1: T007 MainLayout.razor PaletteDark expansion
         → then T008-T013 all in parallel (6 different files)

# US4: All tasks are independent files — launch all 5 in parallel
Agent 2: T014 CreateWallet.razor PQC enable
Agent 3: T015 WalletDetail.razor transaction tab
Agent 4: T016 Settings.razor TOTP wiring
Agent 5: T017 OrganizationConfiguration.razor labels
Agent 6: T018 MyCredentials.razor presentation
```

---

## Parallel Example: US1 (Dashboard) + US2 (Notification Panel)

```
# Both P1 stories, no shared files — launch together:

# US1: T002 and T004 are parallel (different files), T003 is the main Home.razor work
Agent 1: T002 CustomAuthenticationStateProvider.cs (1-line fix)
Agent 2: T003 Home.razor (multi-part refactor)
Agent 3: T004 CreateWallet.razor (SetDefaultWalletAsync fix)

# US2: Both tasks are different files — fully parallel
Agent 4: T005 MainLayout.razor (move ActivityLogPanel)
Agent 5: T006 index.html (overflow-x: hidden)
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1: Setup (verify build)
2. Complete Phase 2: US1 — Dashboard bug fixes
3. Complete Phase 3: US2 — Notification panel fix
4. **STOP and VALIDATE**: Login, verify no wizard loop, verify no horizontal scroll
5. These two P1 fixes address the most user-visible bugs

### Incremental Delivery

1. **Setup** → Build passes
2. **US1 + US2** (P1 bugs) → Core UX fixed → validate
3. **US3** (P1 dark mode) → Visual quality fixed → validate
4. **US4 + US5** (P2 features) → Feature completeness → validate
5. **US6 + US7** (P3 infrastructure) → Notification and i18n → validate
6. **Polish** → Documentation, build verification → done

### Maximum Parallelization (Subagent Strategy)

With shared file coordination:
1. Setup (T001)
2. Wave 1: US1 + US2 + US4 + US5 in parallel (no MainLayout conflicts between these)
3. Wave 2: US3 (needs MainLayout for PaletteDark) + US6 (needs US2 complete)
4. Wave 3: US7 (needs US1 complete, also touches MainLayout)
5. Polish

---

## Notes

- All changes are UI-layer only — no backend modifications needed
- 43 total tasks across 9 phases (1 setup + 7 user stories + 1 polish)
- MainLayout.razor is the highest-conflict file (4 stories) — coordinate carefully
- [P] tasks within a story = different files, safe for parallel agents
- Commit after each completed user story phase for clean history
- Manual visual verification required for US3 (dark mode) — no automated test catches color issues
