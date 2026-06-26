---
description: "Task list for Feature 163: PWA Shared Persona/Profile Editor"
---

# Tasks: PWA Shared Persona/Profile Editor (Feature 163)

**Input**: Design documents from `/specs/163-pwa-shared-persona-editor/`

**Branch**: `163-pwa-shared-persona-editor`

**Summary**: Extract the profile form from the web `MyProfile.razor` into a single `PersonaEditor`
shared component in `Sorcha.UI.Components.User`, host it on both the web and PWA profile pages, and
wire the missing persona DI registrations into the PWA host. No server-side changes; purely
client-side composition and DI wiring. Tests are **required** (FR-013, FR-014).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelisable — targets a different file from concurrent tasks in the same phase
- **[Story]**: User story label (US1, US2, US3)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Extraction Baseline)

**Purpose**: Read the source files to be modified before making any changes, so the extraction is
complete and accurate.

- [ ] T001 Read src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyProfile.razor in full to capture all form markup, mutable state fields, `OnInitializedAsync`, `HydrateFromRead`, `HandleSave`, and `HandleDelete` logic to be extracted into `PersonaEditor`

**Checkpoint**: Full picture of what `PersonaEditor` must contain — proceed to Foundational.

---

## Phase 2: Foundational (Shared `PersonaEditor` Component)

**Purpose**: Create the single shared editor component. Both host pages and all tests depend on this
artifact; nothing else can start until it exists.

**⚠️ CRITICAL**: No user-story or test work can begin until this phase is complete.

- [ ] T002 Create src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Persona/PersonaEditor.razor (namespace `Sorcha.UI.Core.Components.Persona`) by extracting: all form fields (given/family/full name, DOB, and the 5-capped dynamic lists for emails/phones/addresses/nationalities), mutable state, `OnInitializedAsync` (load via `IPersonaService.GetAsync` + `HydrateFromRead`), `HandleSave` (with all three error paths: `PersonaValidationException` → inline field errors; `PersonaWalletNotProvisionedException` → distinct provisioning message; general exception → retry message), `HandleDelete`, and autofill preference read/write
- [ ] T003 Add `/// <summary>` XML documentation to all public `[Parameter]` and `[Inject]` declarations in src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Persona/PersonaEditor.razor per project XML-doc convention

**Checkpoint**: `PersonaEditor` compiles and contains all load/edit/save/delete behaviour. Both
host pages and tests can now proceed.

---

## Phase 3: User Story 1 — Citizen edits and saves their profile from the PWA (Priority: P1) 🎯 MVP

**Goal**: A citizen opens the PWA "My Profile" page, edits a field, saves, reloads, and the change
persists — going from impossible today to fully functional.

**Independent Test**: Open PWA `/profile` as an enrolled citizen, edit a field (e.g. add a phone
number), save, reload — confirm the change persists (SC-001). Then open the web `/profile` as the
same citizen and confirm the same values appear (SC-002).

### Tests for User Story 1

- [ ] T004 [US1] Write bUnit tests `Load_WithExistingPersona_PopulatesFormFields` and `Load_NoPersona_ShowsEmptyEditableForm` in tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs using `ComponentTestFixture` (mock `IPersonaService` + `IInlineFeedback`)
- [ ] T005 [US1] Write bUnit test `Save_ValidInput_CallsUpdateAsync_ShowsSuccessAndRebinds` in tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs
- [ ] T008 [US1] Create tests/Sorcha.Wallet.Pwa.Tests/Services/PersonaDiActivationTests.cs asserting that `IPersonaService` resolves without error and `PersonaEditor` renders under a service collection configured identically to the PWA host (FR-014, SC-005)

### Implementation for User Story 1

- [ ] T006 [P] [US1] Add three PWA DI registrations to src/Apps/Sorcha.Wallet.Pwa/Extensions/ServiceCollectionExtensions.cs inside `AddCitizenWalletServices`: (1) `AddHttpClient<IPersonaClient, PersonaHttpClient>` with `BearerTokenHandler` + `ServerClockHandler` at the gateway base address; (2) `services.AddScoped<IPersonaService, PersonaService>()`; (3) `services.AddBlazoredLocalStorage()`
- [ ] T007 [P] [US1] Replace the placeholder stub in src/Apps/Sorcha.Wallet.Pwa/Pages/Profile.razor with a thin `[Authorize]` shell that renders `<PersonaEditor/>` inside the PWA layout (keep `@page "/profile"`, `PageTitle`, and `[Authorize]`; remove all placeholder markup)

**Checkpoint**: `dotnet test --filter "FullyQualifiedName~PersonaEditorTests"` (load + save tests)
and `dotnet test --filter "FullyQualifiedName~PersonaDiActivationTests"` both pass. User Story 1
delivers a working end-to-end PWA profile save.

---

## Phase 4: User Story 2 — One shared profile editor on both surfaces (Priority: P2)

**Goal**: The web "My Profile" page renders the same `PersonaEditor` component as the PWA. Both
hosts are thin shells; field set and validation behaviour are structurally identical because they
share one definition.

**Independent Test**: Compare the rendered field set and validation messages on web `/profile` and
PWA `/profile` — identical because they are produced by the same component (SC-003). Adding a field
once to `PersonaEditor` appears on both surfaces without per-surface edits.

### Implementation for User Story 2

- [ ] T009 [US2] Reduce src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyProfile.razor to a thin host by removing all extracted form markup, mutable state, and event-handler code, replacing the body with `<PersonaEditor/>` inside the existing page shell (`@page "/profile"`, `PageTitle`, layout, `[Authorize]`)

**Checkpoint**: Web `/profile` renders `PersonaEditor`; the web profile page file no longer contains
duplicated form logic. Both surfaces share one component definition.

---

## Phase 5: User Story 3 — Clear, inline rejection feedback (Priority: P3)

**Goal**: Every save rejection — validation error (400), wallet not provisioned (409), or network /
server failure — produces a specific, inline, recoverable message that preserves all entered data.
No rejection is silent or opaque.

**Independent Test**: Submit a persona with a malformed email — confirm an inline field-relevant
message appears and all other input is preserved (SC-004 / FR-007). Submit as a citizen without a
provisioned wallet — confirm a distinct provisioning-specific message, not a generic error.

### Tests for User Story 3

- [ ] T010 [US3] Write bUnit test `Save_PersonaValidationException_ShowsInlineErrors_PreservesInput` in tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs (mock `IPersonaService.UpdateAsync` throwing `PersonaValidationException`; assert error message shown, form fields retain entered values, and feedback is non-auto-dismissing)
- [ ] T011 [US3] Write bUnit test `Save_WalletNotProvisionedException_ShowsDistinctProvisioningMessage` in tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs (mock throwing `PersonaWalletNotProvisionedException`; assert the provisioning-specific message is shown, distinct from a generic error)
- [ ] T012 [US3] Write bUnit test `Save_NetworkFailure_ShowsRetryMessage_PreservesEnteredData` in tests/Sorcha.UI.Core.Tests/Components/Persona/PersonaEditorTests.cs (mock `UpdateAsync` throwing a generic `HttpRequestException`; assert "save did not complete, retry" feedback and form data preserved)

**Checkpoint**: `dotnet test --filter "FullyQualifiedName~PersonaEditorTests"` — all 5+ component
tests pass (load, save-success, validation-rejection, provisioning-rejection, network-failure). SC-004
and FR-007/008/009 satisfied.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, build verification, and quickstart validation.

- [ ] T013 [P] Update .specify/MASTER-TASKS.md to mark Feature 163 tasks as complete (✅)
- [ ] T014 [P] Run `dotnet build` and `dotnet test --filter "FullyQualifiedName~PersonaEditorTests|PersonaDiActivationTests"` to confirm zero build warnings and all persona tests green
- [ ] T015 Run the SC-001 through SC-005 validation scenarios from specs/163-pwa-shared-persona-editor/quickstart.md against a locally running stack to confirm end-to-end acceptance

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — **BLOCKS** all user stories and all tests
- **US1 (Phase 3)**: Depends on Phase 2 completion
- **US2 (Phase 4)**: Depends on Phase 2; independent of US1 (but US1 tests pass first = safer)
- **US3 (Phase 5)**: Depends on Phase 2; error handling is in `PersonaEditor` (Phase 2) — Phase 5 adds tests for those paths
- **Polish (Phase 6)**: Depends on Phases 3–5

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational only — no dependency on US2 or US3
- **US2 (P2)**: Depends on Foundational only — thin-host reduction is independent of PWA wiring
- **US3 (P3)**: Depends on Foundational (error handling already in `PersonaEditor`) — adds test coverage

### Within Phase 3 (US1)

- T004 and T005 (test writing, same file): T004 before T005
- T006 and T007 (DI wiring + Profile.razor stub): [P] — different files, can proceed together
- T008 (activation test): after T006 (needs the registrations to exist)

### Parallel Opportunities

- T006 (PWA DI) + T007 (Profile.razor stub) — different files, can be written simultaneously
- T013 (MASTER-TASKS update) + T014 (build + test run) — parallel in Polish phase
- US2 (T009) can begin as soon as Phase 2 is done, even while US1 tests are being written

---

## Parallel Example: User Story 1

```
# Once Phase 2 (PersonaEditor) is complete:
Parallel group A — PWA host wiring (different files):
  T006: Register IPersonaClient + IPersonaService + BlazoredLocalStorage in ServiceCollectionExtensions.cs
  T007: Replace Profile.razor stub with <PersonaEditor/>

Sequential after T006:
  T008: PersonaDiActivationTests.cs (needs the DI registrations to exist)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Read MyProfile.razor baseline
2. Complete Phase 2: Create `PersonaEditor` (CRITICAL — blocks everything)
3. Complete Phase 3: Wire PWA DI + replace stub + write load/save tests + activation test
4. **STOP and VALIDATE**: `dotnet test --filter PersonaEditorTests` + `dotnet test --filter PersonaDiActivationTests` pass; manually confirm PWA save works end-to-end (SC-001)
5. Ship MVP — citizen can save their profile from the PWA

### Incremental Delivery

1. Phase 1 + 2 → `PersonaEditor` ready
2. Phase 3 (US1) → Working PWA save + DI activation guard → MVP
3. Phase 4 (US2) → Both surfaces confirmed using one component
4. Phase 5 (US3) → All rejection paths tested
5. Phase 6 → Polish + quickstart validation

---

## Notes

- Tests are **required** by FR-013 (component tests) and FR-014 (PWA activation test)
- `PersonaEditor` lives in `Sorcha.UI.Components.User` (not `Sorcha.UI.Core`) to remain visible to the PWA — see research.md Decision 1
- `AddBlazoredLocalStorage()` is the hidden transitive dependency — missing on the PWA, essential for `PersonaService` to resolve — see research.md Decision 5
- `HandleSave` error paths (`PersonaValidationException` / `PersonaWalletNotProvisionedException` / general) are extracted from `MyProfile.razor` in Phase 2 (Foundational); Phase 5 adds test coverage for them
- The PWA bundle hygiene check (`scripts/check-pwa-bundle.ps1`) should pass without changes — `PersonaEditor` lives in the already-referenced `Sorcha.UI.Components.User` project
- Middle name (`MiddleName`) exists in the model but is not shown on the current web page; keep parity — do not add it
