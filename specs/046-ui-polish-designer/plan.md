# Implementation Plan: UI Polish & Blueprint Designer

**Branch**: `046-ui-polish-designer` | **Date**: 2026-03-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/046-ui-polish-designer/spec.md`

## Summary

Fix 5 UI bugs (dashboard wizard loop, welcome GUID, notification panel overflow, dark mode hardcoded colors, stale "coming soon" labels), add blueprint designer save/load via existing API, build EventsHub client for real-time notifications, and wire existing localization service into components. All backend infrastructure exists — this is purely UI-layer work across ~40 files in `src/Apps/Sorcha.UI/`.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (Blazor WASM)
**Primary Dependencies**: MudBlazor 9.0, Blazor.Diagrams, Microsoft.AspNetCore.SignalR.Client
**Storage**: Browser localStorage (legacy, being migrated to API), Blueprint Service REST API, User Preferences REST API
**Testing**: xUnit + bUnit for component tests, Playwright for E2E
**Target Platform**: Blazor WebAssembly (browser)
**Project Type**: Web application (Blazor WASM client + ASP.NET host)
**Performance Goals**: All UI changes render in <100ms, no layout shifts
**Constraints**: No new backend endpoints needed, all API infrastructure exists
**Scale/Scope**: ~40 files modified across 3 UI projects (Core, Web, Web.Client)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No service changes, UI-only |
| II. Security First | PASS | No credential handling changes; JWT claim fix improves security |
| III. API Documentation | N/A | No new API endpoints |
| IV. Testing Requirements | PASS | bUnit tests for modified components, Playwright E2E |
| V. Code Quality | PASS | Removing hardcoded values, fixing DI usage |
| VI. Blueprint Standards | PASS | Designer save/load uses existing Blueprint JSON model |
| VII. Domain-Driven Design | PASS | Uses correct terminology throughout |
| VIII. Observability | PASS | EventsHub integration improves event visibility |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/046-ui-polish-designer/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity/interface changes
├── quickstart.md        # Build and verify guide
├── contracts/           # API contract changes
│   └── blueprint-api-update.md
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Core/
│   ├── Services/
│   │   ├── IBlueprintApiService.cs          # Add UpdateBlueprintAsync
│   │   ├── BlueprintApiService.cs           # Implement UpdateBlueprintAsync
│   │   ├── EventsHubConnection.cs           # NEW: SignalR client for /hubs/events
│   │   └── Authentication/
│   │       └── CustomAuthenticationStateProvider.cs  # Fix MapInboundClaims
│   ├── Components/
│   │   ├── Layout/
│   │   │   └── MainLayout.razor             # Fix panel placement, add i18n, EventsHub
│   │   ├── Designer/
│   │   │   ├── ActionNodeWidget.razor        # Dark mode CSS
│   │   │   ├── ReadOnlyActionNodeWidget.razor.css
│   │   │   ├── PropertiesPanel.razor         # Dark mode CSS
│   │   │   └── BlueprintViewerDiagram.razor.css
│   │   └── Admin/
│   │       └── OrganizationConfiguration.razor  # Update labels
│   └── Extensions/
│       └── ServiceCollectionExtensions.cs    # Register EventsHubConnection
├── Sorcha.UI.Web/
│   └── wwwroot/
│       └── app/index.html                   # overflow-x: hidden
└── Sorcha.UI.Web.Client/
    └── Pages/
        ├── Home.razor                        # Fix wizard logic, welcome name
        ├── Designer.razor                    # Save/load via API
        ├── Settings.razor                    # Wire TOTP, update labels
        └── Wallets/
            ├── CreateWallet.razor            # Enable PQC, dark mode, fix default
            └── WalletDetail.razor            # Transaction tab, dark mode

tests/
├── Sorcha.UI.Core.Tests/                    # bUnit tests for modified components
└── Sorcha.UI.E2E.Tests/                     # Playwright tests
```

**Structure Decision**: All changes within existing Sorcha.UI project structure. One new file (`EventsHubConnection.cs`) following established hub connection pattern.

## Implementation Phases

### Phase 1: Bug Fixes (US1, US2, US5 welcome) — P1

**Goal**: Fix the three functional bugs that affect every user session.

| Task | File | Change |
|------|------|--------|
| 1.1 | `CustomAuthenticationStateProvider.cs` | Set `_jwtHandler.MapInboundClaims = false` |
| 1.2 | `Home.razor` | Replace `IUserPreferencesService` with `IWalletPreferenceService` + `IWalletApiService`; use `GetSmartDefaultAsync(wallets)` |
| 1.3 | `Home.razor` | Remove `_stats.TotalWallets == 0` redirect; let `_hasDefaultWallet` gate handle it |
| 1.4 | `Home.razor` | Change welcome text to use `context.User.FindFirst("name")?.Value` with fallback chain |
| 1.5 | `CreateWallet.razor` | Always call `SetDefaultWalletAsync` on first wallet creation (both `?wizard` and `?first-login` paths) |
| 1.6 | `MainLayout.razor` | Move `<ActivityLogPanel>` from after `</MudLayout>` to inside it |
| 1.7 | `app/index.html` | Add `overflow-x: hidden` to `html, body` style block |

**Tests**: bUnit test for Home.razor wizard conditional, manual E2E verification.

### Phase 2: Dark Mode Fixes (US3) — P1

**Goal**: Replace all hardcoded color values with theme-aware CSS variables.

| Task | File | Change |
|------|------|--------|
| 2.1 | `MainLayout.razor` | Expand `PaletteDark` with AppbarBackground, Surface, Background, DrawerBackground, TextPrimary, TextSecondary |
| 2.2 | `CreateWallet.razor:108` | Replace `background: linear-gradient(...)` with `var(--mud-palette-background-grey)` or themed class |
| 2.3 | `CreateWallet.razor:128` | Replace `background: white` with `var(--mud-palette-surface)` |
| 2.4 | `WalletDetail.razor:266,274` | Replace `#e8f5e9` and `white` with theme variables |
| 2.5 | `ActionNodeWidget.razor` embedded `<style>` | Replace all hardcoded `white`, `#f8f9fa`, `#555`, `#424242` etc. with CSS variables |
| 2.6 | `ReadOnlyActionNodeWidget.razor.css` | Same pattern — theme variables |
| 2.7 | `PropertiesPanel.razor` embedded `<style>` | Replace `white`, `#f5f5f5`, `#e0e0e0` |
| 2.8 | `BlueprintViewerDiagram.razor.css` | Replace `#f5f5f5`, `white`, `#424242` |

**Tests**: Manual visual verification in both light and dark mode across all modified pages.

### Phase 3: Coming Soon Labels (US4) — P2

**Goal**: Remove stale labels, enable implemented features.

| Task | File | Change |
|------|------|--------|
| 3.1 | `CreateWallet.razor:51-52` | Remove `Disabled="true"` and "(Coming Soon)" from ML-DSA-65 and ML-KEM-768 options |
| 3.2 | `WalletDetail.razor:295-302` | Replace "Transaction History Coming Soon" placeholder with actual transaction query using Register Service (follow `MyTransactions.razor` pattern) |
| 3.3 | `Settings.razor:513-535` | Wire TOTP setup/verify/disable to actual `TotpClientService` endpoints, remove placeholder strings |
| 3.4 | `OrganizationConfiguration.razor:19,51,62` | Reword "Coming Soon" to specific status text ("External identity provider integration is planned for a future release") |
| 3.5 | `MyCredentials.razor:243` | Replace "Presentation flow coming soon" with actual presentation initiation or specific status |

**Tests**: Manual verification that PQC algorithms are selectable, TOTP flow works, transaction tab loads data.

### Phase 4: Blueprint Designer Save/Load (US5) — P2

**Goal**: Wire designer to Blueprint Service API for persistent save/load.

| Task | File | Change |
|------|------|--------|
| 4.1 | `IBlueprintApiService.cs` | Add `UpdateBlueprintAsync(string id, object blueprint, CancellationToken ct)` |
| 4.2 | `BlueprintApiService.cs` | Implement `UpdateBlueprintAsync` wrapping `PUT /api/blueprints/{id}` |
| 4.3 | `Designer.razor` | Inject `IBlueprintApiService`, add `[SupplyParameterFromQuery(Name = "id")]` |
| 4.4 | `Designer.razor` | Add `OnParametersSetAsync` to auto-load blueprint when `?id=` is provided |
| 4.5 | `Designer.razor` | Replace `SaveBlueprint()` LocalStorage logic with API create (POST) or update (PUT) based on `_persistedBlueprintId` |
| 4.6 | `Designer.razor` | Replace `LoadBlueprint(id)` LocalStorage read with `GetBlueprintDetailAsync(id)` |
| 4.7 | `Designer.razor` | Add `_hasUnsavedChanges` tracking and `NavigationLock` for unsaved-changes prompt |
| 4.8 | `Designer.razor` | Keep LocalStorage as offline draft fallback (save to both API and local) |

**Tests**: bUnit test for save/load flow, manual E2E: create → save → navigate away → load → verify all elements preserved.

### Phase 5: EventsHub Client (US6 — Partial) — P3

**Goal**: Build real-time notification infrastructure. Full snackbar migration is follow-up.

| Task | File | Change |
|------|------|--------|
| 5.1 | `EventsHubConnection.cs` (NEW) | Create SignalR client following `ActionsHubConnection` pattern — connect to `/hubs/events`, expose `OnEventReceived` and `OnUnreadCountUpdated` events |
| 5.2 | `ServiceCollectionExtensions.cs` | Register `EventsHubConnection` as scoped service |
| 5.3 | `MainLayout.razor` | Inject `EventsHubConnection`, connect on init, subscribe to `OnUnreadCountUpdated` to refresh badge in real-time |
| 5.4 | `ActivityLogPanel.razor` | Subscribe to `OnEventReceived` to prepend new events when panel is open |
| 5.5 | `MyActions.razor` | Migrate 4 SignalR handler `Snackbar.Add` calls to activity log (highest value, already SignalR-driven) |
| 5.6 | Remove unused `ISnackbar` injections from `MyWallet.razor` and `MyTransactions.razor` |

**Tests**: bUnit test for EventsHubConnection, manual E2E: trigger event → verify badge increments and panel shows event.

### Phase 6: Localization Wiring (US7 — Partial) — P3

**Goal**: Wire localization into highest-visibility components. Full page coverage is follow-up.

| Task | File | Change |
|------|------|--------|
| 6.1 | `MainLayout.razor` | Inject `ILocalizationService`, replace navigation labels with `Loc.T("nav.dashboard")` etc. |
| 6.2 | `Home.razor` | Replace welcome text and dashboard labels with `Loc.T()` calls |
| 6.3 | `Settings.razor` | Replace tab labels and section headings — this is where users change language so effect should be visible |
| 6.4 | `i18n/*.json` (all 4 files) | Add any missing keys discovered during wiring |

**Tests**: Manual: switch to French → verify nav labels, dashboard, and Settings display in French.

## Dependencies Between Phases

```
Phase 1 (Bug Fixes) ─── no dependencies, do first
Phase 2 (Dark Mode) ─── no dependencies, can parallel with 1
Phase 3 (Coming Soon) ── no dependencies, can parallel with 1-2
Phase 4 (Designer) ───── no dependencies, can parallel with 1-3
Phase 5 (EventsHub) ──── depends on Phase 1.6 (panel placement fix)
Phase 6 (Localization) ─ depends on Phase 1.4 (welcome text refactor)
```

Phases 1-4 are fully independent and can be parallelized. Phases 5-6 have light dependencies on Phase 1 changes.
