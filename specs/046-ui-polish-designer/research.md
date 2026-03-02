# Research: UI Polish & Blueprint Designer

**Feature**: 046-ui-polish-designer
**Date**: 2026-03-02

## R1: Dashboard Wizard Bug — Root Cause

**Decision**: Replace `IUserPreferencesService.GetDefaultWalletAsync()` with `IWalletPreferenceService.GetSmartDefaultAsync()` on `Home.razor`, and unify the `?first-login=true` / `?wizard=true` paths.

**Rationale**: Three connected bugs cause the wizard loop:
1. `Home.razor:208` uses the raw `IUserPreferencesService` which returns `null` unless an explicit default is stored — bypassing the smart fallback in `WalletPreferenceService`
2. `Home.razor:216` auto-redirects with `?first-login=true`, but only the `?wizard=true` path in `CreateWallet.razor:264-267` calls `SetDefaultWalletAsync`
3. The `?first-login=true` path at `CreateWallet.razor:269-271` navigates home without saving the default

**Fix**: Home.razor should inject `IWalletPreferenceService` + `IWalletApiService`, call `GetSmartDefaultAsync(wallets)` with the actual wallet list, and remove the TotalWallets==0 redirect entirely. `CreateWallet.razor` should always set the default wallet on first creation regardless of query parameter.

**Alternatives considered**: Adding a separate "has any wallet" check was rejected — `GetSmartDefaultAsync` already handles this.

---

## R2: Welcome GUID — Root Cause

**Decision**: Use `context.User.FindFirst("name")?.Value` with fallback chain, not `context.User.Identity?.Name`.

**Rationale**: The Tenant Service `TokenService` emits a `"name"` claim containing `user.DisplayName`. The Blazor WASM `CustomAuthenticationStateProvider` uses `JwtSecurityTokenHandler` with `MapInboundClaims = true` (default). The short `"name"` claim gets remapped to `ClaimTypes.Name` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name`). However, the fallback at line 55-57 adds `sub` as `ClaimTypes.Name` when the mapped claim is not found — this races with timing and can produce the GUID.

**Fix**: Two options:
- Option A: Set `_jwtHandler.MapInboundClaims = false` in `CustomAuthenticationStateProvider` (1 line) — cleanest
- Option B: Explicitly look up `FindFirst("name")` in the Razor files

Choose Option A (single fix at the root) plus update the fallback chain to prefer `"name"` > `"preferred_username"` > `"email"` > `"Welcome back"`.

**Alternatives considered**: Configuring `NameClaimType` on the `ClaimsIdentity` constructor — rejected because it only affects one code path while `MapInboundClaims = false` fixes all claim lookups.

---

## R3: Notification Panel CSS — Root Cause

**Decision**: Move `<ActivityLogPanel>` inside `<MudLayout>` and add `overflow-x: hidden` to the body.

**Rationale**: The panel (`MudDrawer` with `Anchor.End`, `Variant.Temporary`) is rendered at `MainLayout.razor:188` **outside** `</MudLayout>`. MudBlazor's temporary drawer uses `transform: translateX(100%)` to hide, but without the layout container's overflow clipping, the 400px drawer extends the document width.

**Fix**: Move `<ActivityLogPanel ... />` from after `</MudLayout>` to just before `</MudLayout>` (inside it). Add `overflow-x: hidden` to `html, body` in `app/index.html` as a safety net.

**Alternatives considered**: Using `position: fixed` on the drawer was rejected — MudBlazor handles positioning when the drawer is inside `MudLayout`.

---

## R4: Dark Mode — Scope and Pattern

**Decision**: Replace all hardcoded inline color values with MudBlazor CSS variables or theme-aware classes. Expand `PaletteDark` definition.

**Rationale**: 10+ components use hardcoded `background: white`, `#f5f5f5`, `#fff3e0` etc. in inline styles. MudBlazor provides CSS variables (`var(--mud-palette-surface)`, `var(--mud-palette-background)`, `var(--mud-palette-text-primary)`) that auto-adapt to theme mode. The `PaletteDark` in MainLayout only sets Primary/Secondary — needs AppbarBackground, Surface, Background, DrawerBackground, TextPrimary, TextSecondary.

**Files requiring changes** (high priority):
- `CreateWallet.razor` — mnemonic gradient and word chips
- `WalletDetail.razor` — signature result panel
- `ActionNodeWidget.razor` — node cards (embedded `<style>`)
- `ReadOnlyActionNodeWidget.razor.css` — node cards (scoped CSS)
- `PropertiesPanel.razor` — panel/header (embedded `<style>`)
- `BlueprintViewerDiagram.razor.css` — container/legend (scoped CSS)

**Pattern**: Replace `Style="background: white"` with `Style="background: var(--mud-palette-surface)"`. Replace `color: #555` with `color: var(--mud-palette-text-secondary)`. For component-specific accent backgrounds (mnemonic area), use `var(--mud-palette-warning-lighten)` or similar theme tokens.

---

## R5: Blueprint Designer Save/Load — Architecture

**Decision**: Wire `IBlueprintApiService` into Designer.razor, add `UpdateBlueprintAsync` to the interface, handle `?id=` query param, and replace LocalStorage save/load with API calls.

**Rationale**: All backend infrastructure exists:
- `POST /api/blueprints` creates, `PUT /api/blueprints/{id}` updates, `GET /api/blueprints/{id}` loads
- `IBlueprintApiService` has `SaveBlueprintAsync` (POST) and `GetBlueprintDetailAsync` (GET) but no update (PUT)
- `LoadBlueprintDialog` already calls `GetBlueprintsAsync` to list API blueprints
- The `Blueprint` model is the same type used by designer and API — no conversion needed
- `Blueprints.razor` links to `designer?id={id}` but Designer ignores the param

**Gaps to fill**:
1. Add `UpdateBlueprintAsync(string id, object blueprint)` to `IBlueprintApiService` wrapping `PUT /api/blueprints/{id}`
2. Inject `IBlueprintApiService` in Designer.razor
3. Add `[SupplyParameterFromQuery(Name = "id")]` to auto-load from URL
4. Replace `SaveBlueprint()` LocalStorage logic with API create-or-update
5. Replace `LoadBlueprint(id)` LocalStorage read with `GetBlueprintDetailAsync(id)`
6. Track `_isPersistedToApi` state to distinguish new vs existing blueprints
7. Add unsaved-changes prompt via `NavigationLock`

**Alternatives considered**: Keeping LocalStorage as a draft layer with separate "Publish to API" was rejected — too confusing for users and creates sync issues.

---

## R6: Snackbar → ActivityLog Migration — Architecture

**Decision**: Phased migration. Phase 1: Build `EventsHubConnection`, wire to `ActivityLogPanel`. Phase 2: Backend services emit events for significant operations. Phase 3: Remove snackbar calls from UI components. Keep snackbar for clipboard copy and inline validation.

**Rationale**: The activity log system is architecturally designed for backend-to-UI push:
- Backend services call `POST /api/events` or `IEventService.CreateEventAsync()`
- `EventsHub` pushes `EventReceived` to connected clients
- UI `ActivityLogPanel` displays events with persistence

Current state: `IActivityLogService` has no `CreateEventAsync`. The EventsHub exists but UI doesn't connect. The `POST /api/events` endpoint requires `OrganizationId` + `UserId` (service-to-service design).

**Migration pattern**:
- **Keep as snackbar**: Clipboard copy (20 calls), inline validation warnings — ephemeral, no persistence value
- **Move to activity log**: Blueprint operations, wallet operations, transaction confirmations, SignalR event handlers — persistent record value
- **Dual display**: Errors — both inline feedback AND activity log entry

**Scope limit for this feature**: Build the `EventsHubConnection` client and wire it to `ActivityLogPanel`. Migrate the ~15 SignalR event handler snackbars first (highest value). Full migration of remaining ~100 calls tracked as follow-up.

---

## R7: Localization Wiring — Approach

**Decision**: Wire `ILocalizationService.T()` into components incrementally, starting with navigation/layout (highest visibility) then expanding to pages.

**Rationale**: The infrastructure is complete — `LocalizationService` registered, 4 language files with 161 genuine translation keys, `T(key)` method with fallback to English. Zero components currently inject or call the service.

**Approach**:
1. Add `@inject ILocalizationService Loc` to `MainLayout.razor` first (navigation, account menu, footer)
2. Then `Home.razor` (welcome message, dashboard labels)
3. Then Settings.razor (where users change language — should show the effect)
4. Remaining pages as follow-up work

**New keys needed**: The current 161 keys cover nav, dashboard, wallet, settings, etc. but many page-specific strings are not keyed. New keys will need to be added to all 4 JSON files simultaneously when wiring new pages.

**Alternatives considered**: Using `IStringLocalizer<T>` with .resx files was rejected — the custom JSON system is already built, tested, and contains genuine translations.

---

## R8: Coming Soon Labels — Verification

**Decision**: Remove disabled state from PQC algorithms, wire TOTP to backend, replace "Coming Soon" with transaction data or accurate status messages.

**Rationale**:
- **ML-DSA-65 / ML-KEM-768** (`CreateWallet.razor:51-52`): Fully implemented in `Sorcha.Cryptography`. The Wallet Service API accepts these algorithms. Only the UI dropdown `Disabled="true"` blocks access.
- **Transaction History** (`WalletDetail.razor:299`): The Register Service provides transaction query by wallet address via OData. The `MyTransactions.razor` page already uses this API. WalletDetail just needs to call the same service.
- **TOTP 2FA** (`Settings.razor:521,535`): Backend endpoints exist at `POST /api/auth/totp/setup`, `POST /api/auth/totp/verify`, `POST /api/auth/totp/disable`. The Settings page has the UI but calls `Snackbar.Add("not yet connected")` instead of the actual endpoints. A `TotpClientService` interface exists but may not be wired.
- **Organization Security Policies** (`OrganizationConfiguration.razor:19,51,62`): Backend enforcement genuinely doesn't exist yet. Keep as informational text but reword from "Coming Soon" to specific status.
