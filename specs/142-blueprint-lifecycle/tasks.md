---
description: "Task list for Blueprint Design Lifecycle Overhaul (142)"
---

# Tasks: Blueprint Design Lifecycle Overhaul

**Input**: Design documents from `/specs/142-blueprint-lifecycle/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/ ✓, quickstart.md ✓

**Tests**: INCLUDED — the spec's Testing section and the constitution (>85% new-code coverage, Playwright per `sorcha-ui`) require them. Test tasks precede implementation within each story.

**Organization**: By user story. **P1 = US1+US2+US3 together form the shippable golden-path MVP** (rail + rehearse + governed Go-live). P2 = US4 (guided AI) + US5 (form authoring). P3 = US6 (amend loop).

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: parallelizable (different files, no incomplete-task dependency)
- **[Story]**: US1–US6 (omitted for Setup/Foundational/Polish)

## Path conventions

UI: `src/Apps/Sorcha.UI/{Sorcha.UI.Web.Client,Sorcha.UI.Core,Sorcha.UI.Components.User}`. Engine: `src/Core/Sorcha.Blueprint.Engine`. Blueprint Service: `src/Services/Sorcha.Blueprint.Service`. Register Service: `src/Services/Sorcha.Register.Service`. Clients: `src/Common/Sorcha.ServiceClients.Http`. Tests: `tests/{Sorcha.UI.E2E.Tests,Sorcha.Blueprint.Service.Tests,Sorcha.Blueprint.Engine.Tests}`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Folders, namespaces, and test scaffolding for the feature.

- [ ] T001 Create `Components/Designer/` folder + namespaces in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User` (LifecycleRail, JourneyView, Rehearsal, FormAuthoring) and stage-view placeholders under `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/`
- [ ] T002 [P] Create Playwright page-object + suite folder `tests/Sorcha.UI.E2E.Tests/Docker/Designer/` with `[Category("Designer")]`/`[Category("Lifecycle")]` base wiring
- [ ] T003 [P] Add a `Sorcha.Blueprint.Designer` OpenTelemetry meter registration stub in `src/Services/Sorcha.Blueprint.Service` ServiceDefaults wiring (instruments added in Polish)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: Shared substrate every story depends on. No user-story work begins until this completes.

- [ ] T004 Implement the **executable-definition canonicaliser + hash** in `src/Core/Sorcha.Blueprint.Engine/` (canonical serialise of participants/actions/routes/data-schemas/calculations/disclosures/credential prereq+issuance + behavioural form keywords; exclude presentational keywords) — used by US2/US3/US5
- [ ] T005 [P] Implement the **presentational-vs-behavioural form-keyword classifier** in `src/Common/Sorcha.Blueprint.Models/` (presentational: x-pages/x-sections/x-width/x-introduction/x-review/x-address-lookup/x-persona; behavioural: x-file/x-credential-offer) — single source for T004 and the re-lock logic (D7)
- [ ] T006 Extend `DesignerContext` with `LifecycleState` (CurrentStage, RehearsalPassedForCurrentExecDef, ExecDefHash, AmendContext) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/Designer/DesignerContext.cs`, recomputing ExecDefHash (via T004) on Blueprint change
- [ ] T007 [P] Implement `JourneyViewModel` + mapper (Blueprint → ordered role-labelled steps with Must-prove/Issues badges + per-step detail) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/`
- [ ] T008 Add `RehearsalPass` + `PublishOverride` EF entities + DbContext config + migration in `src/Services/Sorcha.Blueprint.Service/Storage/`; register both via `IStorageRegistrationLog` (F113 pattern)
- [ ] T009 [P] Surface `advertise` (visibility) on the register read response and handle the `sandbox` metadata tag in `src/Services/Sorcha.Register.Service/` + the model in `src/Common/Sorcha.ServiceClients.Http/Register/`
- [ ] T010 [P] Add service-client method signatures for the new endpoints (rehearsals lifecycle, publish-with-override, from-published) in `src/Common/Sorcha.ServiceClients.Http/Blueprint/IBlueprintServiceClient.cs`
- [ ] T011 [P] Unit tests for the exec-def hash (presentational changes preserve hash; behavioural changes alter it) and the classifier in `tests/Sorcha.Blueprint.Engine.Tests/`

**Checkpoint**: substrate ready — stories can begin.

---

## Phase 3: User Story 1 - Staged golden path teaches & gates (Priority: P1) 🎯 MVP

**Goal**: One staged workspace with the lifecycle rail (gating Go live) and journey-first Understand.

**Independent Test**: Open the designer with an existing Blueprint; rail shows 4 stages with correct done/current/available/locked states; Go-live locked tooltip; journey renders with Must-prove/Issues badges; graph toggle; click-step detail.

### Tests for User Story 1

- [ ] T012 [P] [US1] Playwright lifecycle test (rail states, Go-live locked + tooltip, stage navigation, console/network/CSS health) in `tests/Sorcha.UI.E2E.Tests/Docker/Designer/LifecycleRailTests.cs` + page object
- [ ] T013 [P] [US1] bUnit tests for `LifecycleRail` gating states and `JourneyView` badge mapping in `tests/` (UI.Core/Components.User test project)

### Implementation for User Story 1

- [ ] T014 [US1] Implement `LifecycleRail` component (compact single line; done/current/available/locked; hover tooltips) consuming `DesignerContext.LifecycleState` in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Designer/LifecycleRail.razor`
- [ ] T015 [US1] Refactor `DesignerBlueprint.razor` (`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/`): replace the 3-tab `MudTabs` with the rail + stage-driven canvas; keep the AI chat pane persistent (left)
- [ ] T016 [P] [US1] Implement `JourneyView` canvas (journey-first; role badges; Must-prove/Issues chips) using the T007 mapper in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Designer/JourneyView.razor`
- [ ] T017 [US1] Wire "Show technical flow" toggle to the existing `DiagramPane`, and click-step detail reusing `FormPreviewPane`/`SorchaFormRenderer`, inside the Understand stage canvas
- [ ] T018 [P] [US1] First-run dismissible guided overlay on the rail in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Designer/`
- [ ] T019 [US1] Bind the Go-live UI lock to `RehearsalPassedForCurrentExecDef` with hover reason; add a test seam to simulate a pass so US1 is testable before US2 exists

**Checkpoint**: rail + Understand fully functional and independently testable (Go-live lock provable via seam).

---

## Phase 4: User Story 2 - Rehearse safely, unlock Go live (Priority: P1)

**Goal**: Quick dry-run (in-WASM) + full rehearsal on a reusable devMode sandbox register with role-switching; a passing full rehearsal records a server `RehearsalPass` and unlocks Go live.

**Independent Test**: From a validated Blueprint, dry-run steps through all roles (no register); full rehearsal provisions/reuses a sandbox, walks all roles via the switcher, logs real events, completion unlocks Go live, reset discards instance+identities.

### Tests for User Story 2

- [ ] T020 [P] [US2] Engine tests: in-memory store stubs drive validate→calc→route→disclose; **dry-run vs full fidelity** step-sequence equivalence in `tests/Sorcha.Blueprint.Engine.Tests/`
- [ ] T021 [P] [US2] Playwright: full rehearsal provisions sandbox banner, role-switch walk, log shows sealed/routed, completion unlocks Go live, reset discards — `tests/Sorcha.UI.E2E.Tests/Docker/Designer/RehearsalTests.cs`
- [ ] T022 [P] [US2] `Sorcha.Blueprint.Service.Tests`: `RehearsalOrchestrationService` writes a `RehearsalPass` on terminal success; reset discards instance + ephemeral wallets; sandbox isolation (no live-register writes)

### Implementation for User Story 2

- [ ] T023 [P] [US2] In-memory `IInstanceStore`/`IActionStore` stubs (WASM-safe) in `src/Core/Sorcha.Blueprint.Engine/` for the dry-run
- [ ] T024 [US2] Dry-run harness driving `ExecutionEngine` (validate→calc→route→disclose) with per-step model; mark credential steps "checked in full rehearsal" — UI.Components.User services
- [ ] T025 [P] [US2] Shared rehearsal **stepper + role-switcher** UI component (used by dry-run and full) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Designer/`
- [ ] T026 [US2] Sandbox-register provisioning/reuse helper (per-org devMode via initiate/finalize, `sandbox` tag, excluded from listings) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/`
- [ ] T027 [US2] Ephemeral per-role sandbox wallet minting (`CreateWalletAsync`) + role→wallet map + server-side sign-as-acting-role in the rehearsal path
- [ ] T028 [US2] `RehearsalOrchestrationService`: publish-to-sandbox, create instance, execute steps via the real pipeline (`ActionExecutionService`), build plain-language log, write `RehearsalPass` (exec-def hash) on terminal success
- [ ] T029 [US2] Rehearsal endpoints (POST `/rehearsals`, GET, DELETE reset, POST `/role`, POST `/steps`) per contract, with Scalar `.WithSummary/.WithDescription` + XML docs, in `src/Services/Sorcha.Blueprint.Service/Endpoints/`
- [ ] T030 [P] [US2] Implement the rehearsal service-client methods (from T010 signatures) in `src/Common/Sorcha.ServiceClients.Http/Blueprint/`
- [ ] T031 [US2] Rehearse stage UI: mode pills (dry-run/full), sandbox banner, stepper+role-switcher+log; on full pass, set `RehearsalPassedForCurrentExecDef` → unlock Go live
- [ ] T032 [US2] Reset/delete wiring (discard rehearsal instance + ephemeral wallets); quick-iterate by re-running

**Checkpoint**: rehearsal works end-to-end; Go live unlocks for real.

---

## Phase 5: User Story 3 - Governed Go live with register system-info (Priority: P1)

**Goal**: Promote the exact rehearsed executable definition to a chosen live register through governance, with a system-info detail card and a server-side soft gate + audited override.

**Independent Test**: With a passed rehearsal, the register dropdown shows candidates (no-rights blocked, sandbox absent); selecting one shows the system-info card; publish creates a versioned immutable record; a no-rights register is refused; an un-rehearsed publish is blocked (409) unless overridden (audited).

### Tests for User Story 3

- [ ] T033 [P] [US3] Playwright: register dropdown + system-info card populated; no-rights register blocked; publish creates a version; sandbox absent from picker — `tests/Sorcha.UI.E2E.Tests/Docker/Designer/GoLiveTests.cs`
- [ ] T034 [P] [US3] `Sorcha.Blueprint.Service.Tests`: publish gate — governance rights hard (403), rehearsal soft (409 `REHEARSAL_REQUIRED`), override writes `PublishOverride`, exec-def-hash match passes

### Implementation for User Story 3

- [ ] T035 [P] [US3] Register system-info aggregate view-model (fan-out local-relationship + sync-state + governance roster + devMode + published-count + advertise) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Admin/`
- [ ] T036 [US3] Go-live stage UI: register **dropdown** + system-info **detail card** + review summary + permanence/versioning notice (refactor `PublishBlueprintWizard` substance into the stage) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Blueprints/`
- [ ] T037 [US3] Server-side publish gate in the publish path (`src/Services/Sorcha.Blueprint.Service/`): governance rights (hard, via roster) + rehearsal-pass match on exec-def hash (soft) + override-by-authorised + `PublishOverride` audit write
- [ ] T038 [US3] Change the publish endpoint contract (`registerId` + optional `override`; 200/403/409 per contract) + Scalar/OpenAPI + XML docs
- [ ] T039 [P] [US3] Service-client: publish-with-override + 409 `REHEARSAL_REQUIRED` handling in `src/Common/Sorcha.ServiceClients.Http/Blueprint/`
- [ ] T040 [US3] Publish the exact rehearsed version; reflect live + version in UI; ensure sandbox registers are excluded from the picker

**Checkpoint**: 🎯 **MVP COMPLETE** — describe → understand → rehearse → governed go-live works end-to-end.

---

## Phase 6: User Story 4 - Guided AI on-ramp (Priority: P2)

**Goal**: The assistant opens as a guided interviewer (directed-build + sector/purpose/participants/prerequisites), builds the journey live, and translates plain language to constructs.

**Independent Test**: A new service starts with directed-build choices/questions (not a blank box); choosing/answering yields a coherent live journey; "must be certified" produces a credential-gated open starting Action without jargon.

### Tests for User Story 4

- [ ] T041 [P] [US4] Playwright: guided opening (chips/questions), live journey build, plain-language prerequisite → Must-prove badge — `tests/Sorcha.UI.E2E.Tests/Docker/Designer/GuidedOnRampTests.cs`
- [ ] T042 [P] [US4] `Sorcha.Blueprint.Service.Tests`: guided-opening orchestration drives the correct existing tools (require_credential/add_action/issue_credential) from sample answers

### Implementation for User Story 4

- [ ] T043 [US4] Guided interviewer opening behaviour (opening turn + system-prompt) in `src/Services/Sorcha.Blueprint.Service/Services/` (`IChatOrchestrationService`/`AnthropicProviderService`)
- [ ] T044 [P] [US4] Directed-build chips affordance in `AiDesignerPane` (`src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/`)
- [ ] T045 [US4] Live journey render as `BlueprintUpdated` fires (reuse `JourneyView` from T016) in the Describe stage canvas

**Checkpoint**: newcomers get a guided start; path still works without it.

---

## Phase 7: User Story 5 - Form-layout authoring (Priority: P2)

**Goal**: Edit forms in the production renderer (WYSIWYG); layout-less imported schemas render via inference; apply standard `x-*` by direct manipulation or chat.

**Independent Test**: A layout-less schema renders with inferred fields; applying x-sections/x-pages changes the form; persona binding takes; assistant performs the same change and converges.

### Tests for User Story 5

- [ ] T046 [P] [US5] Playwright: layout-less imported schema renders; apply section + wizard-page split visibly changes the form; persona autofill binding — `tests/Sorcha.UI.E2E.Tests/Docker/Designer/FormAuthoringTests.cs`
- [ ] T047 [P] [US5] bUnit: layout tools write the correct `x-*` keywords; direct-manipulation and chat edits converge on the same schema

### Implementation for User Story 5

- [ ] T048 [US5] Add an **edit mode** to `SorchaFormRenderer` (Preview ⇄ Edit toggle) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Forms/` — confirm which `x-*` already render before building writers
- [ ] T049 [US5] `x-*` read/write layer onto Action `dataSchemas` reusing the T005 classifier (so authoring presentational keywords does not trip the exec-def hash)
- [ ] T050 [P] [US5] Layout-tools UI (sections/horizontal, wizard pages, width, introduction, persona-autofill + opt-out, file upload, review page) writing standard `x-*`
- [ ] T051 [US5] Chat layout tools (`set_form_layout`/`set_field_autofill`/`set_review_page`/…) in `BlueprintToolExecutor` + handlers; converge with direct manipulation
- [ ] T052 [US5] Import-schema entry; default render via existing `IFormSchemaService.AutoGenerateForm`; malformed-schema reports clearly

**Checkpoint**: forms (incl. imported) are authorable WYSIWYG by manipulation and chat.

---

## Phase 8: User Story 6 - Amend & re-publish (Priority: P3)

**Goal**: Reopen a live service → new draft version → re-rehearse → re-publish to the same register; prior version stays authoritative until publish.

**Independent Test**: Opening a published service yields a v+1 draft with Go-live re-locked; rehearse+publish increments the version on the same register; the previous version remains authoritative until publish.

### Tests for User Story 6

- [ ] T053 [P] [US6] Playwright: open published → v2 draft → re-rehearse → re-publish; prior version authoritative until publish — `tests/Sorcha.UI.E2E.Tests/Docker/Designer/AmendLoopTests.cs`
- [ ] T054 [P] [US6] `Sorcha.Blueprint.Service.Tests`: clone-published-to-draft carries lineage; `from-published` endpoint behaviour + rights check

### Implementation for User Story 6

- [ ] T055 [US6] Wire the stubbed **Load** dialog in `DesignerToolbar.razor.cs` (`GetBlueprintAsync` → `Context.SetBlueprint`)
- [ ] T056 [US6] `POST /api/blueprints/from-published` (clone published version → new draft with lineage) per contract + Scalar/OpenAPI + XML + service-client method
- [ ] T057 [US6] Amend entry from the services/blueprints list → open v+1 draft with Go-live re-locked pending a fresh rehearsal; re-publish reuses the US3 Go-live path with a version increment

**Checkpoint**: full lifecycle loop closed.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T058 [P] Observability: add `rehearsal_run_total{mode,outcome}`, `rehearsal_duration_seconds`, `publish_override_total`, `sandbox_provision_total` on the `Sorcha.Blueprint.Designer` meter + structured audit logs (override + sandbox lifecycle)
- [ ] T059 [P] Docs: update `src/Apps/Sorcha.UI` README, `docs/reference/API-DOCUMENTATION.md` (new endpoints), the `sorcha-architecture` skill (add an F142 section), and the designer-route note in `CLAUDE.md`
- [ ] T060 [P] Notification compliance: no `ISnackbar` in new code; use `IInlineFeedback`; pass `scripts/check-no-snackbar.ps1`
- [ ] T061 Run `quickstart.md` acceptance walk against Docker; verify all invariants (UI lock, server soft gate via direct API, governance 403, re-lock granularity, sandbox isolation)
- [ ] T062 [P] Coverage check: ensure >85% on new code; fill gaps in `tests/`

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2)** → depends on Setup; **BLOCKS all stories** (exec-def hash, classifier, DesignerContext, JourneyViewModel, stores, register read addition, client signatures).
- **US1, US2, US3 (all P1)** → depend on Foundational. US3's Go-live gate consumes US2's `RehearsalPass`; US1's lock consumes US2's pass signal (US1 ships testable via a seam, then US2 makes it real). Recommended order **US1 → US2 → US3** (they compose the MVP).
- **US4, US5 (P2)** → depend on Foundational; US4 reuses US1's `JourneyView`; US5 reuses the T005 classifier. Independent of each other.
- **US6 (P3)** → depends on Foundational + US3's Go-live path (re-publish).
- **Polish (P9)** → after the desired stories.

### Within each story

Tests → models/services → endpoints → UI → integration. Verify tests fail before implementing. Commit per task or logical group.

### Parallel opportunities

- Foundational: T005, T007, T009, T010, T011 in parallel (different files) after T004/T006 land where noted.
- Within a story, all `[P]` test tasks run together; `[P]` implementation tasks touch different files.
- With capacity, after Foundational: US1/US2 backend (T023–T030) and US4/US5 can progress on separate tracks; US3 waits on US2's pass record.

## Parallel example: User Story 2

```bash
# Tests together:
Task T020  # engine dry-run + fidelity
Task T021  # Playwright full rehearsal
Task T022  # orchestration unit tests
# Then parallel impl on different files:
Task T023  # in-memory stores
Task T025  # stepper+role-switcher UI
Task T030  # rehearsal service-client
```

## Implementation strategy

### MVP first (P1: US1 + US2 + US3)

1. Setup → Foundational (critical).
2. US1 (rail + Understand) → validate via seam.
3. US2 (rehearse) → makes the gate real.
4. US3 (governed Go-live) → **STOP & VALIDATE the full golden path; demo/deploy MVP.**

### Incremental delivery

5. US4 (guided AI) → demo. 6. US5 (form authoring) → demo. 7. US6 (amend loop) → demo. 8. Polish.

## Notes

- `[P]` = different files, no incomplete-task dependency. `[Story]` traces to spec user stories.
- The rehearsal gate's truth is the server `RehearsalPass` (US2/US3); the rail lock mirrors it. Don't let the UI lock become the only enforcement.
- Reuse first: chat agent, `Sorcha.Blueprint.Engine`, `SorchaFormRenderer`, publish/governance APIs, run components — extend, don't reimplement.
- Sandbox registers are reused per org and never appear as Go-live targets; "reset" discards the rehearsal instance + ephemeral wallets, not the register.
