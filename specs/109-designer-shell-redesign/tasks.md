---
description: "Dependency-ordered tasks for AI Designer Unified Shell (Feature 109)"
---

# Tasks: AI Designer Unified Shell

**Input**: Design documents from `/specs/109-designer-shell-redesign/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Unit and E2E tests ARE requested — spec §User Stories each define an "Independent Test", spec §Success Criteria lists SC-001..SC-008, plan §Constitution Check commits to >85% coverage on new code, and the engineering design doc at `docs/superpowers/specs/2026-04-21-ai-designer-layout-redesign-design.md` §Testing spells out the E2E cases. Test tasks are included below.

**Organization**: Tasks are grouped by user story. US1 is MVP; US2 and US3 layer on without breaking US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User-story assignment (`[US1]`, `[US2]`, `[US3]`). Setup, Foundational, and Polish phases carry no story label.
- Exact file paths included in every task.

## Path Conventions

- UI client: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- UI shared / testable helpers: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- Unit tests: `tests/Sorcha.UI.Core.Tests/`
- E2E tests: `tests/Sorcha.UI.E2E.Tests/Docker/`

---

## Phase 1: Setup

**Purpose**: Create the directory scaffolding the rest of the work lands in.

- [x] T001 Create new UI directory `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/` (and parent `Designer/`) for the shell and its panes
- [x] T002 [P] Create new UI helper directory `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/` for `DesignerContext` and the three extracted helpers
- [x] T003 [P] Create new test directory `tests/Sorcha.UI.Core.Tests/Services/Designer/` for unit tests that will cover the helpers and context

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Pure-logic files, the DI context, and the empty shell+toolbar that every user story's panes will plug into.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Pure-logic helpers and enum

- [x] T004 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/DesignerTabEnum.cs` defining `public enum DesignerTab { Ai, Diagram, Preview }`
- [x] T005 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/TabRouteParser.cs` implementing `static DesignerTab Parse(string? queryValue)` per data-model.md §4 (null/whitespace → `Ai`, case-insensitive match, unknown → `Ai` with debug log) plus `static string? ToQuery(DesignerTab tab)` that returns `null` for `Ai` and the lowercase enum name otherwise
- [x] T006 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/PreviewPagerLogic.cs` implementing pure static methods `Next(IReadOnlyList<Action> actions, string? currentId)`, `Previous(...)`, `Jump(..., string targetId)`, returning the next action ID or `null` at boundaries; handle unknown `currentId` by falling back to first action
- [x] T007 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/AutoScrollController.cs` with fields `_autoScrollEnabled` (default true) and `_lastScrollTop`; methods `OnContentAppended()` (invokes JS `scrollTo(bottom)` via injected `IJSRuntime` if enabled) and `OnUserScroll(double scrollTop, double scrollHeight, double clientHeight)` (if distance-from-bottom > 40px and delta indicates user action, disable; if ≤ 40px, re-enable); threshold and JS binding documented via XML comments

### DesignerContext

- [x] T008 [P] Create `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/DesignerContext.cs` per data-model.md §1: public mutable fields (`Blueprint`, `Validation`, `ChatSessionId`, `ActiveActionId`, `IsManualCursor`, `IsDirty`), private `_lastAiEditedActionId`, public methods `SetBlueprint`, `ApplyAiUpdate`, `SetActiveActionManual`, `FollowAi`, `MarkDirty`, `MarkClean`, `UpdateValidation`, `event Action? Changed`; enforce invariants 1–4 (single-fire event per mutation, manual cursor sticky, tracking always updated, `IsDirty` ≠ unsaved chat)

### Unit tests for helpers and context

- [x] T009 [P] Write `tests/Sorcha.UI.Core.Tests/Services/Designer/TabRouteParserTests.cs` — 5 xUnit tests covering valid values, case-insensitive match, unknown string → default, missing/null → default, extra query params ignored
- [x] T010 [P] Write `tests/Sorcha.UI.Core.Tests/Services/Designer/PreviewPagerLogicTests.cs` — 5 tests: next happy path, prev happy path, jump, first/last boundary clamping, unknown-ID recovery
- [x] T011 [P] Write `tests/Sorcha.UI.Core.Tests/Services/Designer/AutoScrollControllerTests.cs` — 5 tests: fresh append auto-scrolls, user scroll-up > 40px disables, return-to-bottom re-enables, disposal after scroll is safe, rapid-append coalescing (multiple appends fire a single JS call)
- [x] T012 [P] Write `tests/Sorcha.UI.Core.Tests/Services/Designer/DesignerContextTests.cs` — ~15 tests covering: `SetBlueprint` does not touch `IsDirty`; `ApplyAiUpdate` auto-cursor writes when `IsManualCursor` false; `ApplyAiUpdate` preserves cursor when `IsManualCursor` true but still updates `_lastAiEditedActionId`; `SetActiveActionManual` flips manual flag; `FollowAi` flips back and re-syncs to `_lastAiEditedActionId`; `MarkDirty`/`MarkClean` are idempotent and only fire event on transition; `Changed` fires exactly once per public mutation; initial state

### DI registration and shell scaffolding

- [x] T013 Register `DesignerContext` as scoped in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs` by adding `builder.Services.AddScoped<Sorcha.UI.Core.Services.Designer.DesignerContext>();` alongside the existing service registrations (depends on T008)
- [x] T014 Create shared-toolbar component `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/DesignerToolbar.razor` + `.razor.cs` per spec FR-023: blueprint-title inline edit, dirty indicator, connection chip (hidden when no session), `MudSpacer`, Load button, Save button (enabled iff `Blueprint != null && IsDirty`), Export split-button (JSON/YAML), validation pill (popover-on-click hosting existing `ValidationPanel`), messages-quota counter; inject `DesignerContext` and subscribe to `Changed`
- [x] T015 Create shell page `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/DesignerBlueprint.razor` + `.razor.cs` with route `@page "/designer/blueprint/{BlueprintId?}"`, role attribute `[Authorize(Roles = "Administrator,SystemAdmin,Designer")]`, CSS grid three-row layout (toolbar / tabs / active pane), `MudTabs` with `KeepPanelsAlive="true"` hosting three named tab panels (initially empty placeholders — panes fill in during US1/US2), subscription to `NavigationManager.LocationChanged` for URL↔tab sync via `TabRouteParser`, tab click emits `NavigateTo(..., replace: true)`, shell-level `NavigationLock` wired to `DesignerContext.IsDirty`; inject `DesignerContext` and `NavigationManager` (depends on T013, T014)

**Checkpoint**: Navigating to `/designer/blueprint` renders the shell with an empty body; toolbar shows "Untitled blueprint"; tabs are visible but inactive; no console errors. Foundation ready — user-story implementation can now begin.

---

## Phase 3: User Story 1 — Unified designer shell with fixed chat layout (Priority: P1) 🎯 MVP

**Goal**: AI + Diagram tabs sharing live state, full-width chat with input pinned to the bottom, shared toolbar that works from either tab.

**Independent Test**: Open `/designer/blueprint`, send 40+ messages via AI, verify input stays pinned at bottom. Switch to Diagram tab, verify blueprint renders as graph. Switch back to AI, verify conversation intact. Save from Diagram, verify blueprint persists. Hand-edit a node title in Diagram, switch to AI, ask the AI about it, verify it references the new title.

### Pane extractions

- [ ] T016 [P] [US1] Extract AI chat into `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/AiDesignerPane.razor` + `.razor.cs` — CSS grid `grid-template-rows: 1fr auto`, messages area (max-width ~900px centred) bound to `AutoScrollController`, input pinned at bottom outside scroll region; reuse existing `ChatPanel`/`ChatMessageItem` components; subscribe to `IChatHubConnection.OnBlueprintUpdated` and call `Context.ApplyAiUpdate(bp, validation, editedActionId)`; detect edited action ID from tool-call result (see existing `ChatOrchestrationService` hub payloads); DO NOT include a right-hand preview column, splitter, per-page save/load/export buttons, or the handoff-to-Designer link (all removed for this pane)
- [ ] T017 [P] [US1] Extract Blazor Diagrams canvas into `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/DiagramPane.razor` + `.razor.cs` — canvas initialisation unchanged from existing `Designer.razor`; replace local `CurrentBlueprint` with reads from `Context.Blueprint`; node-drag / rename / add / delete updates write back to `Context.Blueprint` and call `Context.MarkDirty()`; on canvas mutation, call existing `IBlueprintApiService.ValidateAsync` and write result via `Context.UpdateValidation`; node-click on an action writes `Context.SetActiveActionManual(nodeActionId)`; DO NOT include toolbar, `NavigationLock`, or explicit `Save`/`Export`/`Load` handlers (shell owns those)

### Shell composition

- [ ] T018 [US1] Wire `AiDesignerPane` and `DiagramPane` into `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/DesignerBlueprint.razor` — replace the placeholder tab panels created in T015 with real component instances; Diagram tab `Disabled="@(Context.Blueprint == null)"`; Preview tab stays as "(coming soon in US2)" disabled placeholder; on shell `OnParametersSet`, if `BlueprintId` parameter is non-null fetch the blueprint via `IBlueprintApiService.GetAsync(id)` and call `Context.SetBlueprint(bp)` (depends on T016, T017)

### Legacy page shim-down (required for compile-clean solution once panes extract)

- [ ] T019 [US1] Reduce `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` to a redirect shim: keep the `@page "/designer/chat"` and `@page "/designer/chat/{ExistingBlueprintId?}"` routes, remove all other content, in `OnInitialized` call `NavigationManager.NavigateTo($"/designer/blueprint{(ExistingBlueprintId is null ? "" : "/" + ExistingBlueprintId)}?tab=ai", replace: true, forceLoad: false)`
- [ ] T020 [US1] Reduce `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer.razor` to a redirect shim: keep the `@page "/designer"` route, in `OnInitialized` call `NavigationManager.NavigateTo("/designer/blueprint?tab=diagram", replace: true, forceLoad: false)`
- [ ] T021 [P] [US1] Delete `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` (no longer referenced once AiDesignerPane drops the right-hand column); verify no grep hits for `BlueprintPreview` anywhere in `src/` before deleting

### Nav-menu and in-app link updates

- [ ] T022 [US1] Update the left-nav designer entry in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Layout/NavMenu.razor` (or whichever file declares the Designer link) to point at `/designer/blueprint` with no tab param
- [ ] T023 [P] [US1] Grep `src/` for `"/designer/chat"` and `"/designer?"` string literals (HTML `href` values, razor `Href=` attributes, navigation calls) and update each to the new canonical URL; document any call sites kept intentionally (for example inside the shim files themselves)

### E2E tests for US1

- [ ] T024 [P] [US1] Add `DesignerShell_LoadsAtNewRoute_ShowsAiTabFullWidth` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — navigate to `/designer/blueprint`, verify AI tab is active, chat pane has no right-hand preview column, input element's bounding box `y + height` equals viewport `innerHeight` within 2px
- [ ] T025 [P] [US1] Add `DesignerShell_InputPinnedAtBottom_AfterManyMessages` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — inject 50 synthetic chat messages via `page.evaluate` against the test-only `[JSInvokable]` hook, assert input remains at viewport bottom (**closes GAP-011b**)
- [ ] T026 [P] [US1] Add `DesignerShell_TabSwitch_PreservesChatSession` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — send message in AI, switch to Diagram, verify connection chip still green and canvas populated; switch back to AI and assert original message still visible
- [ ] T027 [P] [US1] Add `DesignerShell_SaveFromDiagram_PersistsAiEdits` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — inject a synthetic AI blueprint update; switch to Diagram tab; click Save; reload `/designer/blueprint/{id}` and assert three panes reflect saved state
- [ ] T028 [P] [US1] Add `DesignerShell_ConsoleNoErrors_DuringTabSwitches` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — collect console messages via Playwright listener, round-trip AI → Diagram → AI, assert zero `error` severity entries
- [ ] T029 [P] [US1] Add test-only `[JSInvokable]` hook (under `#if DEBUG || E2E_TEST_HOOKS`) to `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/AiDesignerPane.razor.cs` exposing a method Playwright can call to inject synthetic SignalR events; keep behind preprocessor guard so release builds don't ship it

**Checkpoint**: User Story 1 is fully functional. Chat is usable, tabs are usable, nothing about `Preview` has shipped yet (tab stays disabled).

---

## Phase 4: User Story 2 — Form Preview tab with auto-cursor (Priority: P2)

**Goal**: A Preview tab renders one action at a time through the existing form renderer, auto-following AI activity, with pager controls and a "Follow AI" toggle for manual override.

**Independent Test**: With the shell from US1 already in place, load a blueprint of 3+ actions; open Preview tab and verify the form renders; click Next/Prev/jump; ask the AI to rename a field on Action 1 and verify Preview follows; click Next manually, assert next AI edit does NOT move cursor; click Follow AI, assert cursor snaps to the AI's most-recent action.

### Form renderer preview mode

- [ ] T030 [US2] Add `[Parameter] public bool PreviewMode { get; set; } = false;` to `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` + `.razor.cs`; when `true`, the submit button is rendered but disabled with tooltip "Preview — submission disabled", and submit click handlers are suppressed; fields remain interactable so the designer can see conditional logic fire; no other renderer behaviour changes (additive parameter, default false preserves all existing callers)

### Preview pane

- [ ] T031 [US2] Create `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/FormPreviewPane.razor` + `.razor.cs` — two-row grid, row 1 pager chrome `[◀] Action N of M [▼ jump] [▶]  [🔗 Follow AI toggle]` plus sub-row showing `"As {ParticipantName} · {ActionTitle}"`, row 2 hosts `SorchaFormRenderer` with `PreviewMode="true"` bound to `Context.Blueprint.Actions.Single(a => a.Id == Context.ActiveActionId).Schema / FormLayout`; Prev/Next/jump write via `Context.SetActiveActionManual`; Follow AI toggle calls `Context.FollowAi()`; keyboard handlers `[` / `]` call `PreviewPagerLogic.Previous` / `.Next` when pane has focus; empty states per spec FR-022 (no blueprint / zero actions); depends on T030 and T008
- [ ] T032 [US2] Wire `FormPreviewPane` into the third tab in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/DesignerBlueprint.razor` — replace the US1 placeholder; tab `Disabled="@(Context.Blueprint == null || Context.Blueprint.Actions.Count == 0)"` (depends on T031, T018)

### Integration with AI pane cursor logic

- [ ] T033 [US2] Refine `AiDesignerPane`'s hub handler in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/Panes/AiDesignerPane.razor.cs` to extract the edited action ID from `OnBlueprintUpdated` payloads (inspect the blueprint diff, or read a hint field from the hub event if available) and pass it as `editedActionId` to `Context.ApplyAiUpdate(bp, val, editedActionId)`; depends on T016, T008
- [ ] T034 [US2] Ensure the Diagram pane's node-click also writes `Context.SetActiveActionManual(nodeActionId)` (may already be done in T017; verify and expand if needed) so clicking a node in Diagram then switching to Preview lands on that action

### E2E tests for US2

- [ ] T035 [P] [US2] Add `DesignerShell_PreviewRenders_SingleActionForm` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — load fixture blueprint with 3 actions, open Preview tab, assert the rendered form contains Action 1's field names and the submit button is present but disabled
- [ ] T036 [P] [US2] Add `DesignerShell_PreviewPager_StepsThroughActions` — click Next twice, assert renderer shows Action 3; jump dropdown changes selection; press `]` key to move forward; press `[` to move back
- [ ] T037 [P] [US2] Add `DesignerShell_PreviewFollowAiToggle_AutoCursor` — inject synthetic AI update for Action 2 (auto-cursor active), assert Preview shows Action 2; click Next (manual override engaged); inject another AI update for Action 3, assert Preview stays on the manual selection; click Follow AI, assert cursor jumps to Action 3
- [ ] T038 [P] [US2] Add `DesignerShell_DiagramEdit_VisibleInOtherPanes` — edit an action title in the Diagram canvas, switch to Preview, assert pager shows the new title in the sub-row

**Checkpoint**: User Stories 1 AND 2 both fully functional. Preview tab works with auto-cursor and manual controls.

---

## Phase 5: User Story 3 — Legacy URL compatibility during rollout (Priority: P3)

**Goal**: All three legacy URL shapes redirect cleanly to the equivalent view in the new shell, carrying the blueprint ID where present.

**Independent Test**: After the new shell has shipped, click each of `/designer/chat`, `/designer/chat/{blueprintId}`, `/designer` in a browser; assert each lands on the correct new-shell view with the correct blueprint loaded (when an ID was in the URL).

### Shim correctness

- [ ] T039 [US3] Audit the redirect shims from T019 and T020 for each URL shape: `/designer/chat` → `/designer/blueprint?tab=ai`, `/designer/chat/{id}` → `/designer/blueprint/{id}?tab=ai`, `/designer` → `/designer/blueprint?tab=diagram`; fix any missing query params, wrong tab default, or lost blueprint ID in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` and `Designer.razor`

### E2E tests for US3

- [ ] T040 [P] [US3] Add `DesignerShell_LegacyChatRoute_Redirects` to `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs` — navigate to `/designer/chat`, assert final URL is `/designer/blueprint?tab=ai` and AI tab is active
- [ ] T041 [P] [US3] Add `DesignerShell_LegacyChatWithIdRoute_Redirects` — navigate to `/designer/chat/{id}` using a fixture blueprint ID, assert final URL is `/designer/blueprint/{id}?tab=ai` AND that the named blueprint loaded
- [ ] T042 [P] [US3] Add `DesignerShell_LegacyDesignerRoute_Redirects` — navigate to `/designer`, assert final URL is `/designer/blueprint?tab=diagram` and Diagram tab is active

**Checkpoint**: All three user stories are independently functional. Legacy bookmarks keep working.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final grooming before PR.

- [ ] T043 [P] Run the full designer unit-test filter: `dotnet test tests/Sorcha.UI.Core.Tests/Sorcha.UI.Core.Tests.csproj --filter "FullyQualifiedName~Services.Designer" --no-build -nologo` — assert ~30 tests pass with 0 failed and 0 skipped
- [ ] T044 [P] Run the full designer E2E suite: `dotnet test tests/Sorcha.UI.E2E.Tests/Sorcha.UI.E2E.Tests.csproj --filter "FullyQualifiedName~DesignerShell" --no-build -nologo` — assert all tasks T024–T028, T035–T038, T040–T042 pass
- [ ] T045 [P] Full solution build warning check: `dotnet build Sorcha.sln -nologo 2>&1 | tail -3` — assert warning count is comparable to master baseline (no new warnings from this feature)
- [ ] T046 [P] Walk `specs/109-designer-shell-redesign/quickstart.md` end-to-end in a clean Docker environment — confirm every step from §4 through §6 lands where described
- [ ] T047 Close GAP-011b in `.specify/MASTER-TASKS.md` — flip the status cell to ✅ with a note pointing at PR and T025
- [ ] T048 Grep sweep: `grep -rn "BlueprintPreview\|designer/chat\|/designer\\b" src/ --include="*.razor" --include="*.cs"` — verify only the two redirect shims (`BlueprintChat.razor`, `Designer.razor`) still reference the legacy paths, and that `BlueprintPreview.razor` has no remaining references anywhere
- [ ] T049 Update `CLAUDE.md` under the "Key Services" / "Architecture" section to mention the new `/designer/blueprint` canonical route — one-line insertion, no architectural rewrite

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Depends on Setup. Blocks all user stories.
- **User Story 1 (Phase 3)**: Depends on Foundational. No dependencies on US2 or US3.
- **User Story 2 (Phase 4)**: Depends on Foundational AND on US1's pane extractions being in place (`AiDesignerPane` must exist for T033 to modify; shell must exist for T032 to wire into). Do not start US2 until US1 shell composition (T018) lands.
- **User Story 3 (Phase 5)**: Depends on the redirect shims created in US1 (T019, T020). T039 is a refinement, not a rewrite — US3 is cheap once US1 is done.
- **Polish (Phase 6)**: Depends on all desired user stories being complete.

### Within each user story

- Pane extraction tasks (T016, T017) are `[P]` — different files, no overlap. They both read the `DesignerContext` contract (completed in T008) so must wait for Phase 2.
- Tests within a phase marked `[P]` can run in parallel (they're all new files in `DesignerShellTests.cs` — though actually they share that file, so the `[P]` here means different test methods in the same class can be authored in parallel if split across people; the class itself is one file).
  - **Correction**: Since T024–T028, T035–T038, T040–T042 all land in the same file `DesignerShellTests.cs`, the `[P]` marker there denotes "can be written by different authors in separate PRs or commits" — the methods don't conflict, but the file does. If one person is writing them, author them serially.

### Parallel opportunities

- **Phase 1 setup**: T002 and T003 in parallel (different dirs).
- **Phase 2 foundational**:
  - T004–T008 (enum + helpers + context) in parallel — different files.
  - T009–T012 (unit tests) in parallel with each other AND with T013–T015 (shell wiring) because the tests reference the production classes but don't depend on DI registration or the shell.
  - T013–T015 must be sequential: T013 before T014, T014 before T015.
- **Phase 3 US1**:
  - T016 and T017 in parallel (different pane files).
  - T021 (delete BlueprintPreview) in parallel with T016/T017 (different file).
  - T019, T020 after T016/T017 to avoid compile breakage.
  - T018 depends on T016 + T017 completing.
  - E2E tests (T024–T029) can all start once T018 is done; they share a file so author serially.
- **Phase 4 US2**: T030 (renderer param) is independent; T031 depends on T030 and T008; T032 depends on T018 and T031; T033 depends on T016 and T008; T034 verifies T017.
- **Phase 5 US3**: T040–T042 are separate test methods after T039 shim audit.
- **Phase 6 polish**: T043, T044, T045, T046, T048 in parallel (read-only checks and different concerns). T047 and T049 are single-line file edits.

### Critical path

```
T001 → T002/T003 → T004..T008 (parallel) → T009..T012 (parallel) + T013 → T014 → T015
      → T016/T017 (parallel) → T018 → T019 → T020 → T021 → T022 → T023 → T024..T029
      → T030 → T031 → T032 → T033 → T034 → T035..T038
      → T039 → T040..T042
      → T043..T049
```

---

## Parallel Example: User Story 1

If two engineers are working on US1 simultaneously after Foundational completes:

```
Engineer A:
  T016 Extract AiDesignerPane
  T022 Update NavMenu designer link
  T024–T026 Author AI-pane E2E tests

Engineer B:
  T017 Extract DiagramPane
  T021 Delete BlueprintPreview.razor
  T023 Grep-and-update in-app /designer/chat references
  T027–T028 Author state-preservation E2E tests

Joint merge:
  T018 Compose panes into shell (requires both T016 and T017)
  T019, T020 Replace legacy pages with redirect shims
  T029 Add test-only JSInvokable hook
```

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational) — DesignerContext, helpers, shell scaffolding all ready.
3. Complete Phase 3 (US1) — AI + Diagram tabs functional, redirect shims in place, US1 E2E passing.
4. **STOP and VALIDATE** — run quickstart §4a and §4b manually. This alone is a material UX improvement.
5. Ship.

### Incremental delivery after MVP

- Ship US1 as PR A. Merge. Deploy.
- Ship US2 as PR B (form preview). Merge. Deploy. Now designers can see form previews.
- Ship US3 as PR C (legacy URL tests). Merge. Deploy. Rollout polish complete.
- After one release cycle, ship a cleanup PR deleting the redirect shims and the old page files entirely.

### Single-PR delivery (alternative)

All three stories in one PR if scope can be reviewed atomically. File-level diff is big (~20 new files, 2 shimmed, 1 deleted) but each part is small. Phase-6 polish tasks run at the end regardless.

---

## Notes

- `[P]` tasks touch different files and have no dependencies on other `[P]` tasks in the same phase.
- `[Story]` labels trace each user-story task back to the spec's acceptance scenarios.
- The engineering design doc at `docs/superpowers/specs/2026-04-21-ai-designer-layout-redesign-design.md` is the authoritative reference for architectural detail. Task descriptions point at specific sections of it where needed.
- Verify unit tests FAIL first when TDD-ing (`Arrange-Act-Assert` per constitution §IV) before implementing the logic they cover.
- Commit after each task or logical group; squash-merge at the PR level.
- Avoid: vague tasks, same-file conflicts marked `[P]`, cross-story dependencies that break story independence.
