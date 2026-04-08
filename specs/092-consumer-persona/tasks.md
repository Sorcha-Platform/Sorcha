---
description: "Task list for Feature 092 — Consumer Persona and Nav Tidy"
---

# Tasks: Consumer Persona and Nav Tidy

**Input**: Design documents from `/specs/092-consumer-persona/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Tests ARE included. The feature spec's §9 Testing section explicitly enumerates required test classes and Playwright E2E suites. Sorcha's constitution IV requires ≥85% coverage for new code.

**Organization**: Tasks are grouped by user story so each P1 story can be delivered as a complete increment, but the three P1 stories together form the MVP.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US5)
- All paths are absolute under `C:\Projects\Sorcha\`

## Path Conventions

- **Shared models**: `src/Common/Sorcha.Tenant.Models/Persona/`
- **Crypto constants**: `src/Common/Sorcha.Cryptography/`
- **HTTP service client**: `src/Common/Sorcha.ServiceClients.Http/`
- **Tenant Service**: `src/Services/Sorcha.Tenant.Service/`
- **Wallet Service**: `src/Services/Sorcha.Wallet.Service/`
- **Blazor Core**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- **Blazor Web Client**: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- **Tests**: `tests/<ProjectName>.Tests/` or `.IntegrationTests/` or `.E2E.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Constants and minimal scaffolding that cross project boundaries

- [X] T001 Add `PersonaVault = "sorcha:persona-vault"` constant to `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` (actual location of the `sorcha:*` derivation constants — BIP44 index 104). Also added `PersonaVaultPath` and extended `ResolvePath`.
- [X] T002 [P] Create `src/Common/Sorcha.Tenant.Models/Persona/` directory — implicitly created by writing T003–T009 files into it.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Storage, crypto, DTOs, Tenant endpoints, Wallet endpoints, and client service. Every P1 user story depends on this phase being complete.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Shared DTOs (parallel)

- [X] T003 [P] Created `PersonaAttributeSource` enum.
- [X] T004 [P] Created `PersonaAttribute<T>` record.
- [X] T005 [P] Created `PersonaEmail`, `PersonaPhone`, `PersonaAddress` records.
- [X] T006 [P] Created `PersonaPhoneKind` enum.
- [X] T007 [P] Created `PersonaAttributesV1` record.
- [X] T008 [P] Created `PersonaReadModelV1` record.
- [X] T009 [P] Created `PersonaReadOptions` record.

### Cryptography tests and derivation (depends on T001)

- [~] T010 **Folded into Wave B** — Persona content key derivation lives inside Wallet Service's `PersonaCryptoService` using stock HKDF-SHA256 with the existing key protection provider. No new surface on `Sorcha.Cryptography` required. Tests moved to `Sorcha.Wallet.Service.Tests` under `PersonaCryptoServiceTests`.
- [~] T011 **Folded into Wave B** — see T010 note. Existing `ISymmetricCrypto.EncryptAsync` / `DecryptAsync` with `XCHACHA20_POLY1305` cover the AEAD primitive without additions.

### Wallet Service — Persona crypto endpoints (depends on T011)

- [X] T012 Created `IPersonaCryptoService` interface + `PersonaCryptoResult` record.
- [X] T013 Created `PersonaCryptoService` implementation — HKDF-SHA256 derivation from wallet private key under `sorcha:persona-vault`, XChaCha20-Poly1305 via existing `ISymmetricCrypto`, zero-memory cleanup. wrappedKeyRef == walletAddress invariant enforced.
- [X] T014 Created `PersonaCryptoEndpoints` with `POST /api/v1/wallets/{address}/persona/encrypt` and `/decrypt`. Protected by `RequirePersonaCrypto` policy, Scalar summaries + XML docs.
- [X] T015 Registered `IPersonaCryptoService` as scoped and mapped endpoints in `Program.cs`.
- [X] T016 Added `RequirePersonaCrypto` authorization policy in `AuthenticationExtensions.cs` — requires a service token (TokenType=service). Reuses existing S2S mechanism rather than inventing a new scope claim.
- [~] T017 **Deferred to Wave H polish** — gateway-config guard test. The YARP config file is untouched by this feature so the route remains internal by default; the explicit assertion test belongs with the rest of the gateway tests and is tracked for Wave H.

### Tenant Service — Entity and EF cascade (depends on nothing; parallel to Wallet block)

- [X] T018 Created `Models/PlatformUserPersona.cs` (placed under existing `Models/` folder to match project convention, not `Data/Entities/`).
- [X] T019 Added `ConfigurePlatformUserPersona(modelBuilder)` inside `TenantDbContext.OnModelCreating` — maps to `public.PlatformUserPersonas` table, PK on `PlatformUserId`, cascade FK to `PlatformUsers`, size limits on Nonce (24) and WrappedKeyRef (256). The project uses inline configuration methods rather than separate `IEntityTypeConfiguration<T>` files.
- [X] T020 Added `DbSet<PlatformUserPersona> PlatformUserPersonas` to `TenantDbContext` and wired the configuration method into `OnModelCreating`.
- [X] T021 Removed the old `20260331082924_InitialCreate` migration via `dotnet ef migrations remove` (which also dropped the DB tables), then regenerated as `20260408160910_InitialCreate` which now includes the `PlatformUserPersonas` table with cascade FK. Applied via `dotnet ef database update` — clean, zero errors. Follows the pre-release squash rule.

### Tenant Service — PersonaService and validators (depends on T011/T014 + T018/T020)

- [ ] T022 Create FluentValidation validators in `src/Services/Sorcha.Tenant.Service/Validators/` for `PersonaAttributesV1`, `PersonaEmail`, `PersonaPhone`, `PersonaAddress` enforcing invariants I-1 through I-5 from `data-model.md` §2 (exactly-one-default, list cap of 5, RFC 5322 email, E.164 phone, ISO 3166-1 alpha-2 country). Use concrete error codes (`multiple_defaults`, `list_cap_exceeded`, `invalid_email`, `invalid_phone`, `invalid_country_code`).
- [ ] T023 Create `src/Services/Sorcha.Tenant.Service/Services/Interfaces/IPersonaService.cs` with `GetAsync(platformUserId, PersonaReadOptions)`, `ReplaceAsync(platformUserId, PersonaAttributesV1)`, `PatchAsync(platformUserId, patch document)`, `DeleteAsync(platformUserId)`.
- [ ] T024 Create `src/Services/Sorcha.Tenant.Service/Services/Implementation/PersonaService.cs` implementing T023. Resolve primary wallet address via the existing `IWalletServiceClient` lookup (`PlatformUserId → wallet address`). Return 409 `wallet_not_provisioned` on write when the user has no wallet. Call `IPersonaCryptoClient` (Wallet Service HTTP client) for encrypt/decrypt. Wrap attributes in `PersonaAttribute<T>` on read. Write an `IActivityLogService` entry on every successful write. Enforce `actingAs == "self"`, reject anything else with 400 `actingAs_not_supported`.
- [ ] T025 Create `src/Common/Sorcha.ServiceClients.Http/IPersonaCryptoClient.cs` — an internal S2S HTTP client interface matching the Wallet persona crypto endpoints. Register in `AddServiceClients` behind an internal flag so it is not surfaced to external callers.

### Tenant Service — Endpoints (depends on T024)

- [ ] T026 Create `src/Services/Sorcha.Tenant.Service/Endpoints/PersonaEndpoints.cs` with `MapGroup("/me/persona")` and routes `GET`, `PUT`, `PATCH` (merge-patch content type), `DELETE` per `contracts/tenant-persona-api.yaml`. Require authenticated `PlatformUser` JWT. Apply `.RequireRateLimiting(RateLimitPolicies.Api)`. Add `.WithSummary()` / `.WithDescription()` on every route and XML comments on every handler method.
- [ ] T027 Register `IPersonaService`, `PersonaEndpoints`, and validators in `src/Services/Sorcha.Tenant.Service/Extensions/ServiceCollectionExtensions.cs` (or the existing equivalent entry point used by `Program.cs`).
- [ ] T028 Hook cascade delete: when `PlatformUser.Delete` is invoked in Tenant Service, ensure the EF cascade from T019 actually fires and the persona row is removed in the same transaction. If an existing `PlatformUserService.DeleteAsync` path uses a manual delete without cascade, wire it through the context so EF handles the cascade.

### Client — IPersonaClient (HTTP) and client IPersonaService

- [ ] T029 [P] Create `src/Common/Sorcha.ServiceClients.Http/IPersonaClient.cs` — public Refit-style HTTP client interface: `GetPersonaAsync(PersonaReadOptions?)`, `PutPersonaAsync(PersonaAttributesV1)`, `PatchPersonaAsync(...)`, `DeletePersonaAsync()`. Register in `AddServiceClients`.
- [ ] T030 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Persona/IPersonaService.cs` with the methods listed in plan.md §6.1: `GetAsync`, `UpdateAsync`, `DeleteAsync`, `GetAutofillEnabledAsync`, `SetAutofillEnabledAsync`, `InvalidateCache`.
- [ ] T031 Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Persona/PersonaServiceClient.cs` implementing T030. Wraps `IPersonaClient`. Holds a session-lifetime in-memory cache keyed by `ActingAs`. Clears on `InvalidateCache`, on logout event, and on org-switch event (subscribe to existing events if present).
- [ ] T032 Wire autofill preference read/write to the existing user settings mechanism used by `Settings.razor`. Investigate `Settings.razor` first to locate the current storage path (browser local storage? Tenant Service user-settings endpoint?) and reuse it. Do not introduce a new settings store.
- [ ] T033 Register `IPersonaService` (client) and its dependencies in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs` (or the equivalent service registration file).

### Contract guard (depends on T030)

- [ ] T034 Create `tests/Sorcha.UI.Core.Tests/PersonaServiceContractTests.cs` using reflection to assert: `IPersonaService.GetAsync` signature accepts `PersonaReadOptions?`; `PersonaAttribute<T>` has `Value`, `Source`, `VerifiedBy`, `LastUpdated` properties in the expected types; `PersonaAttributeSource.SelfAsserted == 0`. These guard the A→C and delegation migration path.

**Checkpoint**: Foundation ready. Persona can be saved, read, decrypted, and the client service is available to every page. User story phases can now begin.

---

## Phase 3: User Story 1 — Fill a form in seconds (Priority: P1) 🎯 Core MVP

**Goal**: When a user opens any blueprint action form containing recognised identity fields, those fields are autofilled from the user's persona with a visible cream tint and a `self` provenance tick. Editing an autofilled field releases the provenance claim.

**Independent Test**: Seed a persona via `IPersonaService.UpdateAsync(...)` in test setup, open a form whose schema contains matching fields, observe the fields are prefilled and the tinted style is applied. Editing a field removes the tint.

### Tests for User Story 1 (write FIRST)

- [ ] T035 [P] [US1] Create `tests/Sorcha.UI.Core.Tests/PersonaAutofillResolverTests.cs` with cases covering: explicit `x-persona` attribute match; explicit `x-persona: false` blocks inference; inference `format: "email"` → `DefaultEmail`; inference `format: "tel"` → `DefaultPhone`; field names `dateOfBirth` / `dob` / `birthDate` case-insensitive → `DateOfBirth`; postal-address schema type → `DefaultAddress`; schema `default` wins over persona match; empty persona returns empty map; nested JSON Pointer paths; array-of-objects fields not autofilled; malformed `x-persona` value skipped with warning. Must fail until T037 exists.

### Implementation for User Story 1

- [ ] T036 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Forms/PersonaFillResult.cs` (record with `FieldPath`, `AttributeName`, `Value`, `Source`, `MatchedBy`) and `PersonaMatchMode` enum (`ExplicitExtension`, `Inference`).
- [ ] T037 [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Forms/PersonaAutofillResolver.cs` as a pure function class. Signature: `Resolve(FormLayout layout, JsonElement schema, PersonaReadModelV1? persona) → IReadOnlyDictionary<string, PersonaFillResult>`. Implement matching rules in the order specified in plan.md §6.2 and research.md Decision 5.
- [ ] T038 [US1] In `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor`, inject `IPersonaService`, `PersonaAutofillResolver`, and `ILogger<SorchaFormRenderer>`. Do not yet apply fills — just inject and build.
- [ ] T039 [US1] In `SorchaFormRenderer.OnInitializedAsync`, after schema hydration, fetch persona (fire-and-forget, do not await before first render), call resolver, and apply fills to the form data bag when persona resolves. Maintain a new private field `_personaFilledPaths : HashSet<string>` that records which JSON pointers were filled from persona.
- [ ] T040 [US1] Add the user-activity precedence check: before applying any fill when persona arrives, skip fields that currently hold focus or already contain a non-empty user-entered value. Fields the user has not touched are filled; touched fields are left alone. Record this behaviour clearly in comments referencing FR-009a.
- [ ] T041 [US1] Add a field-changed hook in `SorchaFormRenderer` that, whenever a bound field value changes, removes its path from `_personaFilledPaths`. This honours FR-019.
- [ ] T042 [US1] Add scoped CSS in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor.css` for the `.sorcha-field.autofilled` state: cream background (`#fff8e1`), `#ffcc80` border, and a `.persona-tick` inline badge with the "self" pill style. Verify contrast meets WCAG AA in both MudBlazor light and dark themes. Adjust colour values if dark-mode contrast fails and document the chosen values.
- [ ] T043 [US1] In the field rendering path of `SorchaFormRenderer.razor`, apply the `autofilled` CSS class to any element whose JSON pointer is in `_personaFilledPaths`, and render a visible `<span class="persona-tick">self</span>` inside that field's container.

**Checkpoint**: A user with a persona saved via the `IPersonaService` client (even without the Profile page yet) can open a form and see autofilled fields with the cream tint and `self` tick. Editing releases the tint. User Story 1 delivers end-to-end.

---

## Phase 4: User Story 2 — Manage my personal profile (Priority: P1)

**Goal**: Users can reach the new `/profile` page from the avatar menu and create/edit/delete each identity attribute, including multi-value lists with defaults. A global autofill toggle lives on this page and defaults to on.

**Independent Test**: A new user can sign in, click the avatar menu → My Profile, land on an empty page, fill identity + contact data, save, reload, and see canonical persisted state.

### Tests for User Story 2

- [ ] T044 [P] [US2] Create `tests/Sorcha.Tenant.Service.Tests/PersonaServiceTests.cs` with cases from spec §9: get returns empty for new user; get decrypts via wallet client and wraps in `PersonaAttribute<T>`; update enforces exactly-one-default per list; update promotes first entry when zero explicit; update rejects multiple explicit defaults (`multiple_defaults`); update rejects list over 5 entries (`list_cap_exceeded`); update rejects malformed email/phone/country; delete wipes row idempotently; `actingAs != "self"` returns 400; user with no primary wallet: read returns empty 200, write returns 409 `wallet_not_provisioned`; audit log entry written on every write, never on read.
- [ ] T045 [P] [US2] Create `tests/Sorcha.Tenant.Service.IntegrationTests/PersonaEndpointsTests.cs` with test server + in-memory or Testcontainers Postgres + fake `IPersonaCryptoClient` for Wallet S2S. Cover full GET → PUT → GET round trip preserves all attribute types; PATCH merges without clobbering untouched attributes; DELETE then GET returns empty, not 404; anonymous returns 401; rate limit policy applies; audit log entries created for writes.

### Implementation for User Story 2

- [ ] T046 [P] [US2] In `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Shared/UserProfileMenu.razor`, add a new `<MudMenuItem Icon="@Icons.Material.Filled.Person" OnClick="@(() => Navigation.NavigateTo("profile"))">` labelled **"My Profile"**, positioned above the existing "View Token" item. Do not change other menu items.
- [ ] T047 [US2] Create `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Profile.razor` with `@page "/profile"`, authentication attribute, and page shell: title, Save button, `IPersonaService` injection, `_persona` state.
- [ ] T048 [US2] Add Identity section to `Profile.razor`: MudTextFields for GivenName, FamilyName, optional FullName fallback, MudDatePicker for DateOfBirth. Wire two-way binding to `_persona.GivenName` etc.
- [ ] T049 [US2] Add Nationalities multi-select to `Profile.razor` with ISO 3166-1 alpha-2 options, default selector, capped at 5 entries.
- [ ] T050 [US2] Add Emails list component to `Profile.razor`: add/remove rows, value + optional label, radio-group for IsDefault, capped at 5 entries with an inline message when cap reached.
- [ ] T051 [US2] Add Phones list component to `Profile.razor`: same pattern as emails plus the `Kind` enum dropdown. Capped at 5.
- [ ] T052 [US2] Add Addresses list component to `Profile.razor`: full address fields (line1/line2/city/region/postalCode/country) with default selector and optional label. Capped at 5.
- [ ] T053 [US2] Add the global **"Autofill forms from my profile"** `MudSwitch` at the top of `Profile.razor`, wired to `IPersonaService.GetAutofillEnabledAsync` / `SetAutofillEnabledAsync`. Defaults to on for users with no setting stored.
- [ ] T054 [US2] Wire Save button to `IPersonaService.UpdateAsync(persona)` and show `MudSnackbar` success. On failure, display the field-level error messages returned by Tenant Service (`invalid_email`, `list_cap_exceeded`, etc.). Invalidate the client cache on success and re-fetch canonical state.
- [ ] T055 [US2] Add empty-state handling: if `GetAsync` returns empty, render the form with all sections empty and no "Last updated" hints. Display the 409 `wallet_not_provisioned` error as a friendly inline message if the user has no wallet.

**Checkpoint**: US2 delivers the profile page end-to-end. US1 and US2 together now form a complete consumer MVP — a user can set up their profile and then open a form to see it autofill.

---

## Phase 5: User Story 3 — Know what I'm disclosing (Priority: P1)

**Goal**: A summary line above any form with autofilled fields tells the user what was filled, offers Review and Clear all actions, and is fully accessible to screen readers. Every autofilled field carries an accessible description announcing its provenance.

**Independent Test**: Open a form with autofilled fields (requires US1). Observe the summary line, open Review to see per-field listing, click a row's clear action to clear one field, click Clear all to clear the rest. Navigate with a screen reader and verify per-field provenance announcements.

### Implementation for User Story 3

- [ ] T056 [US3] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/PersonaFillSummary.razor` and matching scoped CSS. Parameters: `Count : int`, `Fills : IReadOnlyList<PersonaFillResult>`, `OnReview`, `OnClearAll`, `OnClearField`. Renders the one-line summary *"{count} fields filled from your profile"* with Review and Clear all actions. When `Count == 0`, renders nothing.
- [ ] T057 [US3] Add the Review popover inside `PersonaFillSummary.razor` using `MudPopover`. Lists each fill with field label, attribute name, current value, and a per-row clear button. Per-row clear raises `OnClearField(fieldPath)`.
- [ ] T058 [US3] Wire `SorchaFormRenderer` to host `<PersonaFillSummary>` above the form body. Pass `Count = _personaFilledPaths.Count`, the current fill list, and handlers: `OnClearAll` removes every path in `_personaFilledPaths` from the form data bag and clears the set; `OnClearField(path)` clears a single path. Both preserve user-edited fields.
- [ ] T059 [US3] Add per-field accessible descriptions. In `SorchaFormRenderer.razor`, when rendering an autofilled field, set `aria-describedby` to point at a hidden `<span>` containing the text "filled from your profile" (or use MudBlazor's `HelperText` / `aria-label` facility if it routes through to the accessibility tree — verify with the browser dev tools accessibility panel).
- [ ] T060 [US3] Make `PersonaFillSummary` accessible: the summary text is inside a `<div role="status" aria-live="polite">` so screen readers announce the count when it changes. The Review popover uses `MudDialog`-style focus management.
- [ ] T061 [US3] Add unit/component tests for `PersonaFillSummary` in `tests/Sorcha.UI.Core.Tests/PersonaFillSummaryTests.cs` using bUnit if present, or wire into the Playwright suite. Verify count rendering, empty-state hiding, accessible markup, per-row and clear-all event propagation.

**Checkpoint**: User Story 3 delivers the disclosure-transparency layer. All three P1 stories are now complete — the MVP ships.

---

## Phase 6: User Story 4 — Nav tidy (Priority: P2)

**Goal**: Drop the drawer "Navigation" header, remove Settings and Notifications side-nav entries, and merge notification preferences into a Settings tab. My Profile remains reachable only from the avatar menu.

**Independent Test**: Open the side drawer — no "Navigation" header, no Settings or Notifications entries. Open avatar menu — "My Profile" and "Settings" both present. Open Settings — a "Notifications" tab contains the notification preferences content. Bookmark to `/settings/notifications` still resolves.

### Implementation for User Story 4

- [ ] T062 [US4] Edit `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` to remove the `<MudText Typo="Typo.h6">@Loc.T("nav.navigation")</MudText>` block inside `<MudDrawerHeader>` (current lines ~59–62). Leave the `<MudDrawerHeader>` element itself in place if it is load-bearing for the drawer layout, or remove entirely if the drawer renders cleanly without it. Verify in both expanded and mini drawer states.
- [ ] T063 [US4] In the same file, delete the `<MudNavLink Href="settings">` and `<MudNavLink Href="settings/notifications">` entries (current lines ~228–233). Verify the divider above them still looks correct, or remove the divider too if it no longer separates anything meaningful.
- [ ] T064 [US4] In `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings.razor`, wrap the existing page content in a `<MudTabs>` control if it is not already tabbed. The existing content becomes the first tab (e.g. "General" or equivalent label matching the section's current focus).
- [ ] T065 [US4] Locate the current notifications settings page (likely `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Settings/Notifications.razor` or similar) and **move** its body content into a new "Notifications" tab inside `Settings.razor`. Delete the old separate page file. Move any supporting services/bindings it used.
- [ ] T066 [US4] Add a redirect route so `/settings/notifications` resolves to `/settings?tab=notifications`. Options: a `@page "/settings/notifications"` on `Settings.razor` that reads the query string and sets the active tab, or a server-side rewrite in the API Gateway. Prefer the client-side approach for simplicity.
- [ ] T067 [US4] Verify the top app bar "Activity Log" icon (bound to `ToggleActivityLog` in `MainLayout.razor`) is untouched. It is not the same feature as notification preferences and MUST remain as-is per FR-033.
- [ ] T068 [US4] Create `tests/Sorcha.UI.E2E.Tests/NavTidyTests.cs` (Playwright) per spec §9: drawer has no "Navigation" header; side nav has no "Settings" or "Notifications" entries; UserProfileMenu contains "My Profile" and "Settings"; clicking "My Profile" navigates to `/app/profile`; Settings has a "Notifications" tab containing notification preferences; `/app/settings/notifications` deep link resolves to the Notifications tab.

**Checkpoint**: Navigation tidy delivered. The UI matches the design in both discoverability (profile behind the avatar) and cleanliness (no cluttered side nav).

---

## Phase 7: User Story 5 — Global autofill toggle off-state (Priority: P3)

**Goal**: A user who has toggled autofill off sees no automatic fill, but has a one-click "Fill from profile" button on forms with matching fields.

**Independent Test**: In `/profile`, toggle autofill off and save. Open a form with matching fields — no fields are prefilled but a "Fill from profile" button is visible. Click it; the same cream-tinted autofill appears as if the toggle were on.

### Implementation for User Story 5

- [ ] T069 [US5] In `SorchaFormRenderer.OnInitializedAsync`, read the global autofill preference via `IPersonaService.GetAutofillEnabledAsync`. If off: resolve fills into a `_pendingPersonaFills` field **without** applying them to the form data bag. If on: existing US1 behaviour.
- [ ] T070 [US5] In `PersonaFillSummary.razor`, render a **"Fill from profile"** button variant when the component is mounted in the off-state (parent passes `IsAutofillEnabled = false` and a non-empty pending fills list). Clicking the button invokes an `OnFillFromProfile` callback.
- [ ] T071 [US5] Wire `SorchaFormRenderer` to render `<PersonaFillSummary>` in off-state mode when `_pendingPersonaFills` is non-empty. The `OnFillFromProfile` handler applies the pending fills to the form data bag and populates `_personaFilledPaths` so the cream tint + summary line appear exactly as they would in the on-state after the user clicks.
- [ ] T072 [US5] Extend `PersonaAutofillTests.cs` (see T073) with test cases covering the off-state path: toggle off, open form, no autofill, button visible; click button, fields fill with cream tint; toggle back on, open new form, automatic again.

**Checkpoint**: The privacy-conscious flow works. All five user stories complete.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: E2E tests, Wallet crypto integration tests, documentation, Scalar verification, and quickstart validation.

- [ ] T073 [P] Create `tests/Sorcha.UI.E2E.Tests/PersonaAutofillTests.cs` (Playwright, Docker infrastructure per the `sorcha-ui` skill) covering the full happy path from spec §9: fresh user fills persona at `/profile`, saves, opens a blueprint action form with matching fields, sees cream tint + self tick; edits a field and tint disappears; Review popover lists filled fields; Clear all wipes autofilled fields only; toggle off + manual button path; user with three emails has default email filled. Cover the 500 ms latency assertion (SC-006a) by timing the interval between `networkidle` and the autofill appearing.
- [ ] T074 [P] Create `tests/Sorcha.Wallet.Service.IntegrationTests/PersonaCryptoEndpointsTests.cs` covering: encrypt/decrypt round trip yields identical plaintext; tampered ciphertext returns 500 with sanitized error; `persona:crypto` scope required, other scopes return 403; endpoints not reachable through API Gateway (assert via gateway config test).
- [ ] T075 [P] Add XML comments to every new public method/class across Tenant Service, Wallet Service, shared models, and client service so the Release build produces zero warnings. Constitution V.
- [ ] T076 [P] Verify Scalar API docs render the new Tenant Service `/me/persona` endpoints correctly with summaries, descriptions, and request/response examples. Fix any missing metadata. Constitution III.
- [ ] T077 Add a **"## Consumer Persona API"** section to `CLAUDE.md` following the existing convention used by Features 079, 083, 085, and 091. Include: endpoint table (GET/PUT/PATCH/DELETE /me/persona), Key Models list (`PersonaAttributesV1`, `PersonaReadModelV1`, `PersonaAttribute<T>`, `PlatformUserPersona`), and a brief crypto/lifecycle note mentioning `sorcha:persona-vault` and account-delete cascade.
- [ ] T078 Update `docs/reference/API-DOCUMENTATION.md` and `docs/getting-started/PORT-CONFIGURATION.md` if persona-related routes or any new ports are involved (persona adds no new ports but `/me/persona` belongs in the API doc index).
- [ ] T079 Update `.specify/MASTER-TASKS.md` to mark Feature 092 status as appropriate (🚧 during execution → ✅ when merged).
- [ ] T080 Run the full `quickstart.md` script manually or via the Playwright suite to validate every numbered step passes on a fresh Docker Compose stack. Record any deviations as follow-up tasks.
- [ ] T081 Run `dotnet test` at the repo root. Address any regressions introduced by this feature. Known pre-existing failures (`ParticipantTests.Constructor_ShouldInitializeWithDefaults`, `ValidatorRegistryApprovalTests.RejectValidatorAsync`) are not blockers.
- [ ] T082 Run `dotnet build -c Release` at the repo root. Address every new warning — constitution V requires a zero-warning Release build.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)** — no dependencies
- **Phase 2 (Foundational)** — depends on Phase 1; **blocks** all user story phases
- **Phase 3 (US1 autofill)** — depends on Phase 2. Delivers core autofill.
- **Phase 4 (US2 profile page)** — depends on Phase 2. Can run parallel to Phase 3.
- **Phase 5 (US3 disclosure summary)** — depends on Phase 3 (needs `_personaFilledPaths` and the field-render path).
- **Phase 6 (US4 nav tidy)** — depends on Phase 2 only (specifically T029 HTTP client is not required for the nav-only work; only `Profile.razor` existing is — which is Phase 4). In practice, run after Phase 4 so the "My Profile" menu item has a landing page.
- **Phase 7 (US5 off-state)** — depends on Phase 3 and Phase 5.
- **Phase 8 (Polish)** — depends on all user story phases being complete.

### Within Phase 2

Parallel fan-out from T002:
- T003–T009 (DTOs) can all run in parallel
- T010–T011 (crypto) in sequence
- T012–T017 (Wallet Service) in sequence after T011, parallel to the DTO block
- T018–T021 (Tenant entity + EF) in sequence, parallel to Wallet and DTO blocks
- T022 (validators) depends on T007
- T023–T024 (Tenant PersonaService) depend on T018–T021 and T014 and T025
- T025 (IPersonaCryptoClient) parallel to T023 (different project)
- T026–T028 (Tenant endpoints + cascade wiring) depend on T024
- T029–T033 (client IPersonaClient + IPersonaService + registration) depend on T007–T009
- T034 (contract guard) depends on T030

### Within Phase 3 (US1)

- T035 (resolver tests) can be written first in parallel with T036
- T036 (DTO) parallel to T037 since different files
- T037 (resolver implementation) must satisfy T035 tests
- T038 → T039 → T040 → T041 (SorchaFormRenderer integration) sequential on the same file
- T042 (scoped CSS) parallel to renderer integration (different file)
- T043 (apply CSS class in markup) depends on T038–T042

### Within Phase 4 (US2)

- T044 (PersonaServiceTests) and T045 (integration tests) parallel, different files
- T046 (UserProfileMenu edit) parallel with T047 (new Profile.razor)
- T047 → T048–T053 sequential (same file)
- T054 → T055 sequential (same file)

### Within Phase 6 (US4)

- T062–T063 edit the same file, sequential
- T064–T065 edit Settings.razor, sequential
- T066 depends on T064–T065
- T067 is a verification check, no edit
- T068 (Playwright tests) depends on T062–T066

### Within Phase 8

- T073–T076 parallel (different files)
- T077–T078 parallel (different files)
- T080–T082 sequential at the end

---

## Parallel Example: Phase 2 kick-off

```bash
# After T001 and T002 land, fan out all DTOs in parallel:
Task: "T003 PersonaAttributeSource enum"
Task: "T004 PersonaAttribute<T> record"
Task: "T005 PersonaEmail / PersonaPhone / PersonaAddress records"
Task: "T006 PersonaPhoneKind enum"
Task: "T007 PersonaAttributesV1 record"
Task: "T008 PersonaReadModelV1 record"
Task: "T009 PersonaReadOptions record"
Task: "T018 PlatformUserPersona entity"
Task: "T019 PlatformUserPersonaConfiguration"
Task: "T025 IPersonaCryptoClient interface"
Task: "T029 IPersonaClient interface"
Task: "T010 PersonaVault derivation tests"
```

## Parallel Example: Phase 3 (US1) tests + DTO

```bash
# Write tests and DTO in parallel while resolver implementation lands:
Task: "T035 PersonaAutofillResolverTests"
Task: "T036 PersonaFillResult record"
# then serially:
Task: "T037 PersonaAutofillResolver implementation"
```

---

## Implementation Strategy

### MVP First (three P1 stories together)

The three P1 stories (US1 + US2 + US3) jointly form the consumer MVP. A single one alone does not ship a meaningful experience: without US2 there is nothing to autofill from; without US1 the profile has no consumer value; without US3 autofill is invisible and untrustworthy.

1. Complete **Phase 1: Setup** (T001–T002)
2. Complete **Phase 2: Foundational** (T003–T034) — this is the largest phase. Fan out DTOs, entity, validators, Wallet crypto, Tenant PersonaService, and client service in parallel where possible.
3. Complete **Phase 3: US1** (T035–T043) — autofill core.
4. Complete **Phase 4: US2** (T044–T055) — profile page.
5. Complete **Phase 5: US3** (T056–T061) — disclosure summary and a11y.
6. **STOP and VALIDATE**: Run the quickstart through step 6 (autofill on a consumer form). All three P1 stories must be independently functional and, together, deliver the full MVP.
7. Demo. Deploy. This is the feature's core value proposition.

### Incremental delivery

After the MVP ships, the remaining phases add polish and coverage:

8. **Phase 6: US4 (nav tidy)** — ship as a small follow-up PR. No functional change, mostly visual.
9. **Phase 7: US5 (autofill off-state)** — ship as a privacy hardening PR.
10. **Phase 8: Polish** — documentation, E2E coverage, Scalar verification, quickstart validation. Merge as the final PR of the feature.

### Parallel team strategy

With two developers:

- Dev A: Phase 2 backend (Tenant entity, PersonaService, Wallet crypto, endpoints)
- Dev B: Phase 2 frontend/shared (DTOs, client IPersonaService, resolver scaffolding)
- Both sync on T033 (client registration) and T034 (contract guard)
- Dev A then takes Phases 4 (profile page calls real Tenant endpoints) and 6 (nav tidy)
- Dev B then takes Phases 3 (resolver + renderer), 5 (summary + a11y), and 7 (off-state)
- Both collaborate on Phase 8

---

## Notes

- Every new file requires the SPDX license header per the existing project convention: `// SPDX-License-Identifier: MIT` and `// Copyright (c) 2026 Sorcha Contributors`.
- Nullable reference types are enabled project-wide (constitution V). Expect compiler diagnostics on any missing `?` annotations.
- No new projects are added. Every file lands in an existing project per plan.md Project Structure.
- The EF migration is folded into the existing initial setup migration (T021) — do not accept a PR that adds a new incremental migration file.
- Commit atomically per task or per logical group. Reference the task ID in commit messages.
- Stop at the Phase 5 checkpoint to validate the MVP before moving into nav tidy and polish phases.
