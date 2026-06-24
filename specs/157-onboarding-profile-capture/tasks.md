# Tasks: Onboarding Profile Capture (Feature 157)

**Input**: Design documents from `/specs/157-onboarding-profile-capture/`

**Prerequisites**: plan.md ✅ · spec.md ✅ · research.md ✅ · data-model.md ✅ · contracts/ ✅ · quickstart.md ✅

**Testing**: Included — xUnit integration tests (Tenant Service) and Playwright E2E (onboarding step and wallet wizard) are explicitly requested in the feature specification and plan.md.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Exact file paths included in each description

---

## Phase 1: Setup

**Purpose**: Discovery tasks that anchor subsequent implementation. No new projects, databases, or migrations required — this feature extends existing surfaces only.

- [X] T001 Read `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` to identify the exact first-run routing logic and the current wallet-creation navigation URL (confirms wiring anchor for T008 and T010)
- [X] T002 [P] Search `src/Services/Sorcha.Tenant.Service` for all JWT claim-assembly sites (login handler, token refresh handler, social callback handler, any `ClaimsIdentity` builders) and list the files that need the `email_verified` claim added (Decision 4 open note — prerequisite for T004)

---

## Phase 2: Foundational

**Purpose**: Single cross-cutting backend change that must land before US3 can be end-to-end tested. Does not block US1 or US2.

- [X] T003 Add `email_verified` claim (sourced from `PlatformUser.EmailVerified`) to every JWT token-mint site identified in T002 within `src/Services/Sorcha.Tenant.Service` (login, refresh, social callback — all must carry the claim so both web and consumer tier tokens reflect email-verification state)

**Checkpoint**: Email-verified claim flows through new tokens — US3 integration tests can now be written and run against live tokens.

---

## Phase 3: User Story 1 — Complete your profile (Priority: P1) 🎯 MVP

**Goal**: A new user finishing first-run onboarding sees a "Complete your profile" step, confirms or enters name and optional contact details, and the values are saved to their self-asserted persona. Pre-fill seeds from existing display name; re-entry updates in place; failures surface inline without silently advancing.

**Independent Test**: Fresh onboarding flow → reach "Complete your profile" → submit name + one optional contact → `GET /api/me/persona` returns submitted values; a second submit (re-entry) updates the existing row; an invalid input (bad email) returns a field-level error and leaves the persona unchanged.

### Tests for US1

- [X] T004 [P] [US1] Create Playwright E2E test file `tests/Sorcha.UI.E2E.Tests/Onboarding/CompleteProfileStepTests.cs` with test stubs for: save (happy path), pre-fill from display name, skip optional fields, re-entry update-in-place, invalid input rejected with field error, 409 (no wallet) surfaces inline error without advancing — **ensure tests fail before T005–T009 are implemented**

### Implementation for US1

- [X] T005 [P] [US1] Create `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Onboarding/CompleteProfileStep.razor` with MudBlazor form skeleton — component parameters: `EventCallback OnContinue`, optional `EventCallback OnSkip`; inject `IPersonaService` and `IInlineFeedback`; include the SPDX license header
- [X] T006 [US1] Implement pre-fill in `CompleteProfileStep.razor` — in `OnInitializedAsync` call `IPersonaService.GetAsync`, seed `GivenName`/`FamilyName`/`FullName` from `PersonaReadModelV1`; fall back to `AuthenticationState` display name when persona is empty (FR-003)
- [X] T007 [US1] Implement form fields and submission in `CompleteProfileStep.razor` — name fields (given/family or full-name toggle) plus one optional email and one optional phone; on submit call `IPersonaService.UpdateAsync(PersonaAttributesV1, ct)`, invalidate persona cache on success, invoke `OnContinue`; on `400` surface field-level errors via `IInlineFeedback` (`autoDismissMs: 0`) and retain entered values; on `409` surface "Wallet not yet created — please retry" inline error without advancing (FR-005, Edge Cases, Pattern #12)
- [X] T008 [US1] Wire `CompleteProfileStep` into the first-run onboarding sequence in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` — add the step after the wallet-creation step (ensuring wallet is provisioned before the persona PUT is attempted, per Decision 2)

**Checkpoint**: User Story 1 fully functional — profile step appears in first-run flow, pre-fills, saves, handles errors, and re-entry updates in place. Run `dotnet test --filter "FullyQualifiedName~Onboarding"`.

---

## Phase 4: User Story 2 — Wallet defaults during onboarding (Priority: P2)

**Goal**: The wallet-creation wizard, when reached via the onboarding first-run path, defaults to 24-word recovery phrase and a sensible wallet name. Both values remain user-editable and survive back-navigation. Standalone wallet creation is unchanged.

**Independent Test**: First-run wallet wizard URL carries `?words=24&name=<default>`; form renders 24-word selector and pre-filled name; override both, navigate back, return — chosen values are preserved; create wallet with overrides. Navigate directly to `wallets/create` (no query string) — word count defaults to 12, name empty.

### Tests for US2

- [X] T009 [P] [US2] Add Playwright E2E tests to `tests/Sorcha.UI.E2E.Tests/Wallets/CreateWalletTests.cs` (or create file) covering: onboarding wizard URL has `words=24` and name pre-filled; selector shows 24 words; user overrides both values and back-navigation preserves them; wallet created with overrides; standalone `wallets/create` (no query string) still defaults to 12 words and empty name

### Implementation for US2

- [X] T010 [US2] Update the first-run wallet-creation navigation in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Home.razor` to pass `?wizard=true&name=<sensible-default>&words=24` when redirecting a new user to `wallets/create` (Decision 3 — seeding via existing `[SupplyParameterFromQuery]` on `DefaultName` and `DefaultWordCount` in `CreateWallet.razor`; no change to the request model global defaults)

**Checkpoint**: User Stories 1 and 2 both independently functional. Run `dotnet test --filter "FullyQualifiedName~CreateWallet"` for standalone-unchanged regression.

---

## Phase 5: User Story 3 — EmailVerified on `/api/auth/me` (Priority: P3)

**Goal**: `GET /api/auth/me` response includes `emailVerified: bool` (non-nullable, default `false`). Verified user tokens → `true`; unverified or absent claim → `false` (unambiguous, FR-011). Client model gains the matching property.

**Independent Test**: Request `GET /api/auth/me` with a verified-user token → `emailVerified == true`; unverified-user token → `emailVerified == false`; token without the `email_verified` claim → `emailVerified == false` without exception.

### Tests for US3

- [X] T011 [P] [US3] Add `EmailVerified` assertions to `tests/Sorcha.Tenant.Service.Tests/Integration/AuthApiTests.cs` — three cases: verified user token → `true`, unverified user token → `false`, token with no `email_verified` claim → `false`; also assert existing fields are unchanged (no regression) — **ensure tests fail before T012–T014 are implemented**

### Implementation for US3

- [X] T012 [P] [US3] Add `EmailVerified` property (`bool`, `/// <summary> Whether the user's email address has been verified. False when unknown. </summary>`) to `CurrentUserResponse` in `src/Services/Sorcha.Tenant.Service/Models/Dtos/AuthDtos.cs` at the end of the existing property block (after T003 lands the claim)
- [X] T013 [US3] Populate `EmailVerified` in `GetCurrentUser` handler in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` — read the `email_verified` claim from `ClaimsPrincipal` using `bool.TryParse`; absent or invalid claim → `false`; no DB call (claims-only, Decision 4)
- [X] T014 [US3] Update `.WithSummary()` and `.WithDescription()` on the `GET /api/auth/me` endpoint registration in `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` to note the response now includes `emailVerified`
- [X] T015 [P] [US3] Find the client-side current-user model in `src/Apps/Sorcha.UI/Sorcha.UI.Core` or `src/Apps/Sorcha.UI/Sorcha.UI.Components.User` (search for class deserializing `/api/auth/me` response) and add `public bool EmailVerified { get; init; }` with a `/// <summary>` comment — **N/A: Blazor UI reads directly from ClaimsPrincipal; no client-side DTO for /api/auth/me exists**

**Checkpoint**: All three user stories independently functional. Run `dotnet test --filter "FullyQualifiedName~AuthApiTests"`.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T016 [P] Run `dotnet build` and confirm zero new warnings (XML doc coverage for new public members, no CS8 nullable warnings) — pre-existing Razor parse errors in TransactionHistoryFeed.razor and RecentActivityFeed.razor fixed as part of this phase
- [X] T017 [P] Run `dotnet test` full suite and confirm all new tests pass with no regressions in wallet creation, persona, or auth flows — 1323 passed, 0 failed
- [X] T018 Update `.specify/MASTER-TASKS.md` to mark Feature 157 tasks complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on T002 (claim-site audit) — only blocks US3 E2E testing
- **US1 (Phase 3)**: Can start after Phase 1 — no dependency on Foundational or US2/US3
- **US2 (Phase 4)**: Can start after Phase 1 — independent of US1 and US3; T010 touches the same `Home.razor` as T008 (US1), so coordinate edits or sequence T010 after T008 to avoid merge conflicts
- **US3 (Phase 5)**: T012/T013 depend on T003 (Foundational claim mint); T015 is independent
- **Polish (Phase 6)**: After all desired stories complete

### User Story Dependencies

- **US1 (P1)**: Independent after Phase 1. `Home.razor` touched by T008.
- **US2 (P2)**: Independent after Phase 1. `Home.razor` touched by T010 — coordinate with T008 if implementing concurrently.
- **US3 (P3)**: T012 + T013 depend on T003 (Foundational). T011 and T015 are independent.

### Within Each User Story

- Tests (T004, T009, T011) written **before** implementation tasks — verify they **fail** first
- T005 before T006 before T007 (US1 component build-up)
- T006 must precede T008 (wire component before inserting into Home.razor)
- T012 must precede T013 (DTO field before handler population)

---

## Parallel Opportunities

### Phase 1 — Run together

```
T001: Read Home.razor routing
T002: Audit token-mint claim sites
```

### Phase 3 (US1) — Start T004 and T005 in parallel

```
T004: Playwright test stubs (CompleteProfileStep)
T005: CompleteProfileStep.razor skeleton
```
Then T006 → T007 → T008 sequentially (each builds on prior).

### Phase 4 (US2) — Can run in parallel with Phase 3

```
T009: Playwright wallet-wizard tests
T010: Home.razor wallet-URL wiring  (after T008 commits to avoid conflict)
```

### Phase 5 (US3) — T011 + T012 + T015 in parallel (after T003)

```
T011: AuthApiTests EmailVerified assertions
T012: CurrentUserResponse DTO field
T015: Client-side current-user model field
```
Then T013 → T014 sequentially.

---

## Implementation Strategy

### MVP Scope (US1 only)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003 — needed only if verifying claim downstream; can defer to US3)
3. Complete Phase 3: US1 (T004–T008)
4. **STOP and VALIDATE**: `dotnet test --filter "FullyQualifiedName~Onboarding"` + manual first-run flow
5. Deploy / demo if ready

### Incremental Delivery

1. Setup (T001–T002) → anchor points confirmed
2. US1 (T004–T008) → profile step live → validate independently → deploy
3. US2 (T009–T010) → wallet defaults → validate independently → deploy
4. Foundational claim mint (T003) + US3 (T011–T015) → emailVerified surfaced → validate → deploy
5. Polish (T016–T018) → clean build, full test pass, docs updated

---

## Notes

- `[P]` tasks touch different files and have no incomplete-task dependencies — safe to run concurrently
- `Home.razor` is touched by both T008 (US1) and T010 (US2) — coordinate if implementing in parallel to avoid edit conflicts
- The `CompleteProfileStep` component must **not** inject `ISnackbar` — use `IInlineFeedback` only (Pattern #12, CI snackbar gate)
- Persona write requires a provisioned wallet (`PUT /api/me/persona` → 409 otherwise) — `CompleteProfileStep` must handle 409 gracefully and the onboarding sequence must place it after wallet creation (T008 enforces this)
- `email_verified` claim mint (T003) must cover **all** token-issue paths — missing one means some sessions never receive a `true` value even after verification
- `EmailVerified` on `CurrentUserResponse` is non-nullable `bool` (not `bool?`) — absence of the claim maps unambiguously to `false` (FR-011)
- No schema migration, no new projects, no new service clients required for this feature
