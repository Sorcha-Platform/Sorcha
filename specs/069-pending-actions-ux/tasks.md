# Tasks: Pending Actions UX Overhaul & Instance Reference System

**Input**: Design documents from `/specs/069-pending-actions-ux/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks grouped by user story. US1 and US2 are both P1 but US2 (reference generation) is a prerequisite for US1 (meaningful cards), so US2 is implemented first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects needed. Verify branch, create test scaffolding.

- [x] T001 Verify branch `069-pending-actions-ux` is checked out and solution builds with `dotnet build --force`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Add the `InstanceReferenceTemplate` model to Sorcha.Blueprint.Models — needed by both US2 (generation) and US1 (API enrichment).

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T002 Add `InstanceReferenceTemplate` and `ReferenceComponent` classes to `src/Common/Sorcha.Blueprint.Models/InstanceReferenceTemplate.cs` — prefix (1-5 uppercase alpha), components list (1-5 items), each with field (JSON Pointer), transform (enum: first-word, truncate, uppercase), chars (2-10, default 3). Include DataAnnotation validation and XML docs.
- [x] T003 Add `InstanceReference` property (nullable `InstanceReferenceTemplate`) to `Blueprint` class in `src/Common/Sorcha.Blueprint.Models/Blueprint.cs` with `[JsonPropertyName("instanceReference")]`
- [x] T004 Add `InstanceReference` string field to `PendingActionSummary` DTO in `src/Services/Sorcha.Blueprint.Service/Models/PendingActionSummary.cs`
- [x] T005 [P] Add unit tests for `InstanceReferenceTemplate` validation (valid prefix, invalid prefix, empty components, max components, transform enum values) in `tests/Sorcha.Blueprint.Models.Tests/InstanceReferenceTemplateTests.cs`

**Checkpoint**: Foundation ready — Blueprint model has the reference template, PendingActionSummary has the field.

---

## Phase 3: User Story 2 — Auto-Generated Instance Reference (Priority: P1)

**Goal**: When the first action of an instance completes, auto-generate a human-readable reference (e.g., "CP-RIV-14W-a7k3") and store it in instance metadata.

**Independent Test**: Create an instance, submit Action 1, verify `Instance.Metadata["instanceReference"]` contains the generated reference.

### Tests for User Story 2

- [x] T006 [P] [US2] Unit tests for `InstanceReferenceGenerator` in `tests/Sorcha.Blueprint.Engine.Tests/InstanceReferenceGeneratorTests.cs` — test transforms (first-word, truncate, uppercase), null/empty field fallback ("UNK"), hash uniqueness, fallback when no template, max length, non-ASCII stripping
- [x] T007 [P] [US2] Unit tests for hash generation determinism and uniqueness in `tests/Sorcha.Blueprint.Engine.Tests/InstanceReferenceGeneratorTests.cs` — same instance ID always produces same hash, different instance IDs produce different hashes

### Implementation for User Story 2

- [x] T008 [US2] Create `InstanceReferenceGenerator` class in `src/Core/Sorcha.Blueprint.Engine/Implementation/InstanceReferenceGenerator.cs` — static `Generate(InstanceReferenceTemplate? template, Dictionary<string, object> accumulatedData, string instanceId, string blueprintTitle)` method. Implements transform logic (first-word splits on whitespace takes first token, truncate takes first N chars, uppercase). Generates 4-char base36 hash from instance ID. Returns uppercase hyphen-separated reference. Fallback: first 2 chars of blueprint title + hash.
- [x] T009 [US2] Integrate reference generation into action execution path — in the Blueprint Engine's action completion handler, after `AccumulatedData` is updated for the first action: call `InstanceReferenceGenerator.Generate()`, write result to `Instance.Metadata["instanceReference"]`. Only generate if metadata key doesn't already exist (idempotent).
- [x] T010 [US2] Add `instanceReference` to the Construction Permit blueprint template in `walkthroughs/ConstructionPermit/construction-permit-template.json` — prefix "CP", components: `/projectName` first-word 3 chars, `/siteAddress` first-word 3 chars.

**Checkpoint**: Submitting Action 1 on a blueprint with `instanceReference` generates a reference in instance metadata.

---

## Phase 4: User Story 1 — Meaningful Pending Action Cards (Priority: P1)

**Goal**: Pending actions endpoint returns real action titles and instance references. UI cards show human-readable content.

**Independent Test**: Log in as participant with pending actions, verify cards show blueprint title, action title, and instance reference.

### Tests for User Story 1

- [x] T011 [P] [US1] Unit tests for pending action enrichment in `tests/Sorcha.Blueprint.Service.Tests/PendingActionsEnrichmentTests.cs` — test that action title is populated from blueprint (not placeholder), instance reference is populated from metadata, graceful fallback when blueprint not found (uses "Action {id}"), graceful fallback when no reference in metadata (empty string)
- [x] T012 [P] [US1] Unit tests for UI view model mapping in `tests/Sorcha.UI.Core.Tests/PendingActionViewModelTests.cs` — test that `PendingActionViewModel` maps enriched fields correctly, no raw blueprint IDs shown as primary display text

### Implementation for User Story 1

- [x] T013 [US1] Enrich `EfCoreInstanceStore.GetPendingActionsByWalletAsync()` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` — inject `ActionResolverService`, for each pending action look up the blueprint (cached), extract `Action.Title` for the action ID, read `Instance.Metadata["instanceReference"]`. Populate `ActionTitle` and `InstanceReference` on `PendingActionSummary`.
- [x] T014 [US1] Add `InstanceReference` field to client-side `PendingActionSummaryDto` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/PendingActionService.cs` and to `PendingActionViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Workflows/WorkflowInstanceViewModel.cs`
- [x] T015 [US1] Update `WorkflowService.GetPendingActionsAsync()` mapping in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WorkflowService.cs` — map `ActionTitle`, `BlueprintTitle`, and `InstanceReference` to the view model
- [x] T016 [US1] Update pending action card markup in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — replace blueprint ID heading with blueprint title, replace "Action {id}" with action title, show instance reference as subtitle, keep assigned date and urgency badge
- [x] T017 [US1] Update `PendingActionInbox` sidebar component in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor` — show enriched action title and instance reference consistently with the main page

**Checkpoint**: Pending actions page and sidebar show blueprint title, action title, and instance reference on all cards.

---

## Phase 5: User Story 3 — Execute Action Form Loads Schema (Priority: P2)

**Goal**: Clicking TAKE ACTION fetches the blueprint, extracts the action's data schema, and renders form fields in the dialog.

**Independent Test**: Click TAKE ACTION on a pending action, verify the dialog shows correct form fields.

### Tests for User Story 3

- [x] T018 [P] [US3] Unit tests for schema fetch and mapping in `tests/Sorcha.UI.Core.Tests/ActionFormSchemaFetchTests.cs` — test that blueprint fetch returns action schemas, test graceful error handling on fetch failure, test loading state

### Implementation for User Story 3

- [x] T019 [US3] Add `GetBlueprintAsync(string blueprintId)` method to `IWorkflowService` and `WorkflowService` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WorkflowService.cs` — calls `GET /api/blueprints/{blueprintId}`, returns deserialized blueprint model. Cache in a private dictionary for the session to avoid re-fetching.
- [x] T020 [US3] Update `TakeAction()` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — before opening the dialog, fetch the blueprint via `WorkflowService.GetBlueprintAsync()`, extract the action definition matching the pending action's `ActionId`, set `DataSchema` on the `PendingActionViewModel` from the action's `DataSchemas`. Show loading indicator during fetch. Show error with retry if fetch fails.
- [x] T021 [US3] Verify `ActionForm.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/ActionForm.razor` correctly passes `DataSchema` through to `SorchaFormRenderer` — the existing `OnParametersSet` logic at line 51 should work once `DataSchema` is populated; verify and fix if needed.

**Checkpoint**: TAKE ACTION opens a dialog with correct form fields for any pending action.

---

## Phase 6: User Story 6 — Documentation (Priority: P2)

**Goal**: Blueprint authors and AI assistants can discover and correctly configure `instanceReference` on blueprints.

**Independent Test**: Ask an AI assistant to add an instance reference to a blueprint and verify it produces valid config.

### Implementation for User Story 6

- [x] T022 [P] [US6] Add `instanceReference` documentation section to blueprint schema reference in `docs/reference/blueprint-schema.md` (or create if not exists) — property schema, available transforms with examples, validation rules, fallback behaviour, two complete worked examples (Construction Permit + a simpler one).
- [x] T023 [P] [US6] Update CLAUDE.md Critical Patterns section — add a "6. Instance Reference Configuration" pattern showing the JSON shape, explaining it's public metadata, and noting that blueprint authors should define it for user-facing workflows.
- [x] T024 [P] [US6] Update blueprint-builder skill in `.claude/skills/blueprint-builder.md` — add instruction that when building blueprints with user-facing data entry, the assistant should suggest an `instanceReference` configuration using fields from the first action's schema.
- [x] T025 [US6] Update Construction Permit walkthrough blueprint template in `walkthroughs/ConstructionPermit/construction-permit-template.json` — add `instanceReference` section (same as T010 if not already done) and add a note in `walkthroughs/ConstructionPermit/README.md` explaining the reference.

**Checkpoint**: Documentation covers instanceReference for humans and AI assistants.

---

## Phase 7: User Story 4 — Card/Row View Toggle (Priority: P3)

**Goal**: Participants can switch between card grid and compact table views. Preference persists across sessions.

**Independent Test**: Toggle view, verify both render correctly, log out, log back in, verify preference persisted.

### Implementation for User Story 4

- [ ] T026 [US4] Add view toggle button (card/table icons) to the Pending Actions page header in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — MudToggleIconButton or similar, wired to a `_viewMode` state variable ("cards" or "table").
- [ ] T027 [US4] Implement table row view in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — MudTable with columns: Action Title, Instance Reference, Blueprint Name, Assigned Date, Urgency, TAKE ACTION button. Conditionally render card grid or table based on `_viewMode`.
- [ ] T028 [US4] Add localStorage persistence for view preference using JS interop in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — read preference on `OnAfterRenderAsync(firstRender)`, write on toggle. Key: `sorcha:pendingActions:viewMode`.

**Checkpoint**: Card/table toggle works and persists across login sessions.

---

## Phase 8: User Story 5 — Grouped and Sorted Actions (Priority: P3)

**Goal**: Actions grouped by blueprint type with count badges, sorted by date within groups.

**Independent Test**: Have pending actions from multiple blueprint types, verify grouping and sort order.

### Implementation for User Story 5

- [ ] T029 [US5] Add client-side grouping logic in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` — group `_actions` by `BlueprintName`, produce `Dictionary<string, List<PendingActionViewModel>>` with counts. Sort groups alphabetically, sort actions within groups by `AssignedAt` descending.
- [ ] T030 [US5] Update card view and table view rendering to iterate by group — show group heading with blueprint title and count badge (e.g., "Construction Permit Approval (3)"). Skip grouping chrome when only one group exists.

**Checkpoint**: Pending actions are visually grouped by blueprint type with correct counts and sort order.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, cleanup, and consistency checks.

- [ ] T031 Verify PendingActionInbox sidebar and MyActions page show consistent enriched data — same field names, same formatting, same fallback behaviour
- [ ] T032 Run `dotnet build --force` and `dotnet test` across all affected test projects — verify no regressions
- [ ] T033 Update walkthrough to test instance reference generation end-to-end — run `pwsh walkthroughs/ConstructionPermit/run.ps1 -Scenario A` and verify instance metadata contains generated reference
- [x] T034 Update `Construction-Permit-Walkthrough.md` Known Issues section — remove the "Execute Action form empty" issue (fixed by US3), add any new known issues discovered during implementation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US2 (Phase 3)**: Depends on Phase 2 — reference generation model + logic
- **US1 (Phase 4)**: Depends on Phase 2 + Phase 3 (needs references to exist to display them)
- **US3 (Phase 5)**: Depends on Phase 2 only — independent of US1/US2
- **US6 (Phase 6)**: Depends on Phase 3 (needs reference template finalised to document it)
- **US4 (Phase 7)**: Depends on Phase 4 (card redesign must be done before adding toggle)
- **US5 (Phase 8)**: Depends on Phase 4 (grouping operates on enriched card data)
- **Polish (Phase 9)**: Depends on all desired phases being complete

### User Story Dependencies

```
Phase 2 (Foundation)
    ├── US2 (Phase 3: Reference Generation) ─── P1
    │       ├── US1 (Phase 4: Enriched Cards) ── P1
    │       │       ├── US4 (Phase 7: View Toggle) ── P3
    │       │       └── US5 (Phase 8: Grouping) ──── P3
    │       └── US6 (Phase 6: Documentation) ──── P2
    └── US3 (Phase 5: Form Fix) ──────────────── P2  [INDEPENDENT]
```

### Parallel Opportunities

- **Phase 2**: T002, T003, T004 can be done sequentially (same model); T005 tests [P] in parallel
- **Phase 3**: T006 and T007 tests [P] in parallel; then T008, T009, T010 sequentially
- **Phase 4**: T011 and T012 tests [P] in parallel; then T013→T014→T015→T016→T017
- **Phase 5**: Independent of US1/US2 — can run in parallel with Phase 3/4 after Foundation
- **Phase 6**: T022, T023, T024 all [P] — different files, no dependencies
- **Phase 7 + 8**: Can run in parallel after Phase 4 completes

---

## Parallel Example: After Foundation

```
# These can run simultaneously after Phase 2:

Agent A: US2 (Phase 3) → US1 (Phase 4) → US4 (Phase 7)
Agent B: US3 (Phase 5) → US6 (Phase 6) → US5 (Phase 8)
```

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundation (model changes)
3. Complete Phase 3: US2 — Instance reference generation
4. Complete Phase 4: US1 — Enriched pending action cards
5. **STOP and VALIDATE**: Pending actions page shows meaningful data
6. Deploy/demo

### Incremental Delivery

1. Foundation + US2 + US1 → Meaningful cards (MVP!)
2. + US3 → Users can submit actions through the UI
3. + US6 → Blueprint authors know how to use instanceReference
4. + US4 + US5 → View toggle and grouping (polish)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US2 before US1 because references must exist before cards can display them
- US3 is fully independent — can be done at any point after Foundation
- Constitution requires >85% test coverage for new code — tests included in each phase
- Commit after each phase checkpoint
