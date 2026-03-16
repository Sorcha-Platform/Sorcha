# Tasks: Designer & Blueprint Instructions Upgrade

**Input**: Design documents from `/specs/059-designer-blueprint-upgrade/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included per constitution requirement (>85% coverage for new code).

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Project initialization — no functional changes, just scaffolding.

- [x] T001 Create `BlueprintInstructions.cs` model in `src/Common/Sorcha.Blueprint.Models/BlueprintInstructions.cs` with Overview, Locale, ActionInstructions, ParticipantInstructions, InstructionSets, GovernanceRoles properties per data-model.md
- [x] T002 [P] Create `InstructionSet.cs` model in `src/Common/Sorcha.Blueprint.Models/InstructionSet.cs` with Locale, Source, Version properties per data-model.md
- [x] T003 [P] Create `BlueprintVersion.cs` model in `src/Common/Sorcha.Blueprint.Models/BlueprintVersion.cs` with Major, Minor, ChangeType, StructuralHash, PublishedAt, PublishedBy, TransactionId properties per data-model.md
- [x] T004 Add `Instructions` (BlueprintInstructions?, JsonIgnore WhenWritingNull), `VersionMajor` (int, default 1), `VersionMinor` (int, default 0) properties to `src/Common/Sorcha.Blueprint.Models/Blueprint.cs`
- [x] T005 [P] Add `Instructions` (string?, max 5000) property to `src/Common/Sorcha.Blueprint.Models/Action.cs`
- [x] T006 [P] Add `Instructions` (string?, max 500) property to `src/Common/Sorcha.Blueprint.Models/Control.cs`
- [x] T007 [P] Add `Instructions` (string?, max 2000) property to `src/Common/Sorcha.Blueprint.Models/Participant.cs`
- [x] T008 Write unit tests for new model properties and JSON serialization backwards-compatibility in `tests/Sorcha.Blueprint.Models.Tests/BlueprintInstructionsTests.cs` — verify null instructions serialize/deserialize cleanly, verify existing blueprints without instructions still load

**Checkpoint**: Model layer complete. All existing tests must still pass (backwards-compatible changes only).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Backend services that multiple user stories depend on. MUST complete before user story work.

- [x] T009 Create `StructuralDiffService.cs` in `src/Services/Sorcha.Register.Service/Services/StructuralDiffService.cs` — deep-clone blueprint JSON, strip `instructions` at all levels (blueprint, action, control, participant), serialize to canonical JSON (sorted keys), SHA-256 hash, compare hashes to classify change as structural or documentation-only
- [x] T010 [P] Create `SchemaFieldResolver.cs` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/SchemaFieldResolver.cs` — parse JsonDocument data schemas to extract property names, types, descriptions, required flags; returns `List<SchemaField>` per data-model.md
- [x] T011 [P] Create `SchemaDescriptionExtractor.cs` in `src/Core/Sorcha.Blueprint.Schemas/SchemaDescriptionExtractor.cs` — given a Control.Scope and action's DataSchemas, resolve the matching schema property and return its `description` field (for fallback help text)
- [x] T012 Write unit tests for StructuralDiffService in `tests/Sorcha.Register.Service.Tests/Services/StructuralDiffServiceTests.cs` — test identical blueprints produce same hash, instruction-only change produces same structural hash, action change produces different hash, control/participant instruction changes are documentation-only
- [x] T013 [P] Write unit tests for SchemaFieldResolver in `tests/Sorcha.UI.Core.Tests/Services/SchemaFieldResolverTests.cs` — test extraction from schema with descriptions, schema without descriptions, nested properties, empty schema
- [x] T014 [P] Write unit tests for SchemaDescriptionExtractor in `tests/Sorcha.Blueprint.Schemas.Tests/SchemaDescriptionExtractorTests.cs` — test scope resolution to property description, missing scope, schema without description

**Checkpoint**: Foundation ready — structural diff, schema field resolution, and description extraction all tested. User story work can begin.

---

## Phase 3: User Story 1 — Blueprint Author Creates and Publishes with Instructions (Priority: P1) 🎯 MVP

**Goal**: Authors can add instructions to blueprints and publish them with semantic versioning to the system register.

**Independent Test**: Create a blueprint with instructions in the designer, verify instructions render in preview, submit through publishing API with version metadata, verify it appears in system register with correct version.

### Tests for User Story 1

- [x] T015 [P] [US1] Write unit tests for versioned publish in `tests/Sorcha.Register.Service.Tests/Services/VersionedPublishTests.cs` — test first publish = v1.0, documentation-only = minor bump, structural = major bump with minor reset, version metadata stored in transaction
- [x] T016 [P] [US1] Write unit tests for BlueprintVersion in `tests/Sorcha.Blueprint.Models.Tests/BlueprintVersionTests.cs` — test version increment logic, display formatting, major/minor reset rules

### Implementation for User Story 1

- [x] T017 [US1] Extend `SystemRegisterService.PublishBlueprintAsync` in `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs` — accept version metadata (changeType, structuralHash, previousVersion) in publish request, compute next version (major+1/minor+1), store version info in transaction metadata. Include structured logging (ILogger) for publish events: log blueprint ID, version (major.minor), changeType, signer address, and transaction ID at Information level; log structural hash comparison at Debug level
- [x] T018 [US1] Add `/api/system-register/blueprints/{blueprintId}/versions` endpoint to `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs` — query all versions of a blueprint with change type labels, signer identity, publish timestamps per contracts/system-register-versioning.md
- [x] T019 [US1] Add `/api/system-register/blueprints/{blueprintId}/classify-change` endpoint to `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs` — accept new blueprint JSON, compare structural hash with latest published version, return changeType and proposedVersion per contracts/system-register-versioning.md
- [x] T020 [US1] Create `InstructionsTab.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/InstructionsTab.razor` — editable text fields for blueprint overview (Markdown), per-action instructions (one editor per action), per-field instructions (pre-populated from SchemaDescriptionExtractor where available), per-participant instructions; save updates to blueprint model
- [x] T021 [US1] Create `InstructionsPreview.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/InstructionsPreview.razor` — read-only preview showing instructions as participants would see them: help icons next to fields, expandable action instruction panels, Markdown rendered via Markdig
- [x] T022 [US1] Add Instructions tab to `PropertiesPanel.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/PropertiesPanel.razor` — new MudTab containing InstructionsTab component, toggle button for InstructionsPreview mode
- [x] T022a [US1] Add participant-scoped instruction filtering to `InstructionsPreview.razor` — when rendering in participant context (workflow form UI), resolve the current user's participant identity and show only: (1) blueprint overview instructions (visible to all), (2) action instructions for actions where the participant is the sender, (3) participant-specific instructions matching the current participant name, (4) field instructions for fields in the participant's current action. Hide other participants' role-specific instructions.

**Checkpoint**: US1 complete — blueprints have instructions, versioned publishing works, instructions editor and preview functional in designer with participant-scoped visibility.

---

## Phase 4: User Story 2 — Unified Blueprint Visualisation (Priority: P1)

**Goal**: Single diagram component renders consistently across all 4 contexts (designer, chat preview, catalogue, viewer dialog).

**Independent Test**: Load the same blueprint in visual designer, AI chat preview, catalogue detail, and viewer dialog — verify identical layout.

### Implementation for User Story 2

- [x] T023 [US2] Create `BlueprintDiagram.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/BlueprintDiagram.razor` — unified component with `Mode` parameter (Edit, Preview, Compact); Edit mode uses ActionNodeWidget (unlocked), Preview mode uses ReadOnlyActionNodeWidget (locked, auto-layout), Compact mode uses simplified title-only nodes with smaller spacing; all modes use BlueprintLayoutService for positioning
- [x] T024 [US2] Extend `BlueprintLayoutService.cs` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/BlueprintLayoutService.cs` — add swimlane column assignment by sender participant, increase spacing (generous padding around actions), add participant header labels above each column, improve edge routing for divergent/convergent paths
- [x] T025 [US2] Add decision diamond rendering for divergent paths in `BlueprintDiagram.razor` — where an action has multiple routes, render a diamond-shaped decision indicator with labelled condition arrows for each route
- [x] T026 [US2] Add convergent path merge indicators in `BlueprintDiagram.razor` — where multiple routes target the same action, render a merge indicator at the target node
- [x] T027 [US2] Improve back-edge rendering for cycles — curved arcs (not straight lines) with distinct styling (purple dashed), cycle target badge already exists in ReadOnlyActionNodeWidget
- [x] T028 [US2] Refactor `BlueprintViewerDiagram.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/BlueprintViewerDiagram.razor` — delegate to `BlueprintDiagram` in Preview mode, remove duplicated layout and node creation logic
- [x] T029 [US2] Replace `BlueprintPreview.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` — replace flat timeline list with `BlueprintDiagram` in Preview mode (or Compact mode for narrow chat pane)
- [x] T030 [US2] Write unit tests for swimlane layout in `tests/Sorcha.UI.Core.Tests/Services/BlueprintLayoutServiceTests.cs` — test multi-participant blueprint assigns correct columns, single-participant stays single column, edge routing crosses lanes correctly

**Checkpoint**: US2 complete — same diagram renders in all contexts with swimlanes, directional arrows, and cycle arcs.

---

## Phase 5: User Story 3 — Blueprint Publishing Governance Workflow (Priority: P1)

**Goal**: Self-hosted Blueprint Publishing Blueprint that governs how blueprints are published to the system register.

**Independent Test**: Run the publishing workflow end-to-end: author submits, reviewer approves/rejects, publisher signs, blueprint appears in register.

### Implementation for User Story 3

- [ ] T031 [US3] Create `blueprint-publishing-v1.json` in `blueprints/templates/blueprint-publishing-v1.json` — 3 participants (Author, Reviewer, Publisher), 5 actions (Submit Draft, Classify Change, Full Review, Documentation Review, Sign & Publish), routing: classify routes to full or doc review based on changeType calculation, reject cycles back to Submit, approve routes to Sign & Publish; per research.md R6. Include validation warning in template metadata that single-person governance (reviewer == publisher) should generate an audit log entry at Warning level during workflow execution
- [ ] T032 [US3] Add data schemas for the publishing workflow — blueprint submission schema (blueprint JSON, changeType, proposedVersion) and review response schema (approved boolean, comments string, reviewer address)
- [ ] T033 [US3] Add disclosure rules to publishing blueprint — Author discloses full blueprint to Reviewer, Reviewer discloses review decision to Publisher, Publisher discloses signed transaction to all
- [ ] T034 [US3] Add "Publish Blueprint" button to `Designer.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer.razor` — initiates the Blueprint Publishing workflow with the current blueprint as submission data
- [ ] T035 [US3] Add "Publish" action to catalogue (Templates.razor) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Templates.razor` — for blueprints loaded from templates, offer to publish to system register via the publishing workflow
- [ ] T036 [US3] Write integration test for the publishing blueprint structure in `tests/Sorcha.Blueprint.Models.Tests/PublishingBlueprintTests.cs` — load blueprint-publishing-v1.json, validate structure (3 participants, 5 actions, correct routes, cycles, disclosures)
- [ ] T036a [US3] Add governance role resolution logic to publishing workflow initiation in `Designer.razor` and `Templates.razor` — when initiating "Publish Blueprint", check if the blueprint's `Instructions.GovernanceRoles` defines Reviewer/Publisher DIDs; if so, pre-populate the workflow participant assignments from those definitions rather than org-admin role assignments
- [ ] T036b [US3] Add governance conflict notification — when an org admin views a blueprint that defines its own governance roles (via GovernanceRoles dictionary), display an info banner showing "This blueprint defines its own governance model" with the defined roles listed; if the admin attempts to override (e.g., reassign reviewer), show a warning dialog explaining that blueprint-defined roles take precedence and the override will not apply to this blueprint's publishing workflow

**Checkpoint**: US3 complete — governance workflow template installed and structurally validated. UI publish buttons initiate the workflow with governance role precedence enforced.

---

## Phase 6: User Story 4 — Seamless Designer Context Handoff (Priority: P2)

**Goal**: Blueprint state preserved when switching between visual designer and AI chat designer.

**Independent Test**: Create blueprint in AI chat, click "Open in Visual Designer", verify it loads, edit, click "Open in AI Chat", verify edit present.

### Implementation for User Story 4

- [ ] T037 [US4] Add `[SupplyParameterFromQuery]` for `blueprint` parameter to `Designer.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer.razor` — on init, if query param present, load blueprint via `BlueprintApiService.GetBlueprintDetailAsync(id)` and populate the diagram
- [ ] T038 [US4] Update "Open in Visual Designer" button in `BlueprintChat.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` — change navigation from `/designer` to `/designer?blueprint={blueprintId}` so the visual designer loads the blueprint
- [ ] T039 [US4] Add "Open in AI Chat" button to `Designer.razor` toolbar — navigates to `/designer/chat/{blueprintId}` (AI chat already supports loading via route param)
- [ ] T040 [US4] Ensure both designers save to same Blueprint API record — verify that saving from visual designer after handoff from chat targets the same blueprint ID (no duplicate creation)

**Checkpoint**: US4 complete — round-trip handoff works between both designers.

---

## Phase 7: User Story 5 — Catalogue Dual-Source (Priority: P2)

**Goal**: Catalogue shows both auto-seeded templates and published blueprints from the system register.

**Independent Test**: Start platform, open Catalogue, verify templates appear (auto-seeded) and published blueprints appear (from register).

### Implementation for User Story 5

- [ ] T041 [US5] Create `TemplateSeedService.cs` as IHostedService in `src/Services/Sorcha.Blueprint.Service/Services/TemplateSeedService.cs` — on startup, scan `{AppContext.BaseDirectory}/blueprints/templates/*.json`, parse each as BlueprintTemplate, upsert into InMemoryDocumentStore (idempotent, skip if same version exists)
- [ ] T042 [US5] Register `TemplateSeedService` in `src/Services/Sorcha.Blueprint.Service/Program.cs` — add `builder.Services.AddHostedService<TemplateSeedService>()`
- [ ] T043 [US5] Create `PublishedBlueprintList.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Templates/PublishedBlueprintList.razor` — query `/api/system-register/blueprints` endpoint, display cards with title, version (major.minor), author, publish date, signed provenance badge
- [ ] T044 [US5] Add version history detail to `PublishedBlueprintList.razor` — on card click, show version history from `/api/system-register/blueprints/{id}/versions` with change type labels (structural vs documentation)
- [ ] T045 [US5] Update `Templates.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Templates.razor` — add MudTabs with two sections: "Templates" (existing template list) and "Published Blueprints" (new PublishedBlueprintList)
- [ ] T046 [US5] Add "Use" button for published blueprints — create new blueprint instance from published version and open in designer
- [ ] T047 [US5] Write unit tests for TemplateSeedService in `tests/Sorcha.Blueprint.Service.Tests/Services/TemplateSeedServiceTests.cs` — test seeds from files, idempotent on restart, skips invalid JSON

**Checkpoint**: US5 complete — catalogue is dual-source with auto-seeded templates and published blueprint browser.

---

## Phase 8: User Story 6 — Instructions Editing and Translation (Priority: P2)

**Goal**: Dedicated instructions editing workflow with export/import for translations.

**Independent Test**: Open Instructions tab, edit action/field instructions, toggle preview, export strings, import translated file.

### Implementation for User Story 6

- [ ] T048 [US6] Create `InstructionExportService.cs` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/InstructionExportService.cs` — export all instruction strings as flat key-value JSON per research.md R10 format (blueprint.overview, action.N.instructions, control.actionN./scope.instructions, participant.name.instructions)
- [ ] T049 [US6] Add import functionality to `InstructionExportService.cs` — parse imported JSON, validate locale tag, if primary locale update inline text, if different locale create/update InstructionSet entry
- [ ] T050 [US6] Add "Export Strings" and "Import Translations" buttons to `InstructionsTab.razor` — export triggers file download via JSInterop, import opens file upload dialog and calls InstructionExportService
- [ ] T051 [US6] Add stale instruction detection to `InstructionsTab.razor` — compare instruction keys against current blueprint structure (action IDs, control scopes, participant names), highlight orphaned instructions with warning icon and message
- [ ] T052 [US6] Write unit tests for InstructionExportService in `tests/Sorcha.UI.Core.Tests/Services/InstructionExportServiceTests.cs` — test export produces correct keys, import updates primary locale, import creates InstructionSet for foreign locale, round-trip export/import preserves content

**Checkpoint**: US6 complete — instructions can be edited, exported for translation, and re-imported with locale tags.

---

## Phase 9: User Story 7 — Fix Existing Designer Stubs (Priority: P3)

**Goal**: Complete all stubbed features in the designer (export, clipboard, field resolution, routes, disclosures).

**Independent Test**: Each stub fix tested individually with before/after verification.

### Implementation for User Story 7

- [ ] T053 [P] [US7] Fix AI chat export in `BlueprintChat.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` — replace modal display with JSInterop file download using same `downloadFile` helper as visual designer's ExportDialog
- [ ] T054 [P] [US7] Fix clipboard copy in `BlueprintJsonView.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/BlueprintJsonView.razor` — implement `navigator.clipboard.writeText` via JSInterop, replace TODO comment and snackbar message
- [ ] T055 [P] [US7] Replace hardcoded fields in condition editor — update `GetAvailableFieldsForCondition()` in `Designer.razor` (or PropertiesPanel) to call `SchemaFieldResolver.ResolveFieldsAsync(action.DataSchemas)` instead of returning `["amount", "status", "approved"]`
- [ ] T056 [P] [US7] Replace hardcoded fields in calculation editor — update `GetAvailableFieldsForCalculation()` similarly to use SchemaFieldResolver
- [ ] T057 [US7] Create `RouteEditor.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/RouteEditor.razor` — display and edit action.Routes in properties panel: list routes with condition, nextActionIds, isDefault flag; add/edit/remove routes with condition editor integration
- [ ] T058 [US7] Create `DisclosureEditor.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Designer/DisclosureEditor.razor` — display and edit action.Disclosures in properties panel: list participant-field mappings; add/edit/remove disclosures with participant dropdown and field selector
- [ ] T059 [US7] Integrate RouteEditor and DisclosureEditor into `PropertiesPanel.razor` — add as sections in the action detail view, replacing read-only displays

**Checkpoint**: US7 complete — all previously stubbed features now functional.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and integration testing across all user stories.

- [ ] T060 Update `CLAUDE.md` with new API endpoints for versioned publishing, instructions model description
- [ ] T061 [P] Update `docs/reference/API-DOCUMENTATION.md` with system register version endpoints and classify-change endpoint
- [ ] T062 [P] Update `docs/reference/development-status.md` with designer upgrade completion status
- [ ] T063 [P] Add XML documentation comments to all new public classes and methods (BlueprintInstructions, InstructionSet, BlueprintVersion, StructuralDiffService, SchemaFieldResolver, InstructionExportService)
- [ ] T064 [P] Add `.WithSummary()` and `.WithDescription()` to all new Minimal API endpoints (versions, classify-change)
- [ ] T065 Update `.specify/MASTER-TASKS.md` with feature 059 completion status
- [ ] T066 Run full `dotnet build && dotnet test` validation across all affected projects

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 (model properties must exist)
- **Phase 3 (US1)**: Depends on Phase 2 (needs StructuralDiffService, SchemaDescriptionExtractor)
- **Phase 4 (US2)**: Depends on Phase 1 only (model changes for diagram rendering)
- **Phase 5 (US3)**: Depends on Phase 1 + Phase 3 (needs versioned publishing endpoints)
- **Phase 6 (US4)**: Depends on Phase 1 only (just query param handling)
- **Phase 7 (US5)**: Depends on Phase 2 + Phase 3 (needs version endpoints for published blueprint display)
- **Phase 8 (US6)**: Depends on Phase 1 + Phase 3 (needs InstructionsTab from US1)
- **Phase 9 (US7)**: Depends on Phase 2 only (needs SchemaFieldResolver)
- **Phase 10 (Polish)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Foundation → US1 (no cross-story deps)
- **US2 (P1)**: Setup → US2 (independent of US1)
- **US3 (P1)**: US1 → US3 (needs versioned publish endpoints)
- **US4 (P2)**: Setup → US4 (independent)
- **US5 (P2)**: US1 → US5 (needs version history endpoints)
- **US6 (P2)**: US1 → US6 (needs InstructionsTab)
- **US7 (P3)**: Foundation → US7 (independent of other stories)

### Parallel Opportunities

After Phase 2 (Foundation) completes:
- **US1** and **US2** can run in parallel (different files/services)
- **US4** and **US7** can run in parallel with US1/US2 (different files)
- **US3**, **US5**, **US6** must wait for US1 completion

---

## Parallel Example: Phase 1 Setup

```
# All model changes are in different files — run in parallel:
T001: BlueprintInstructions.cs (new file)
T002: InstructionSet.cs (new file)
T003: BlueprintVersion.cs (new file)
T005: Action.cs (add property)
T006: Control.cs (add property)
T007: Participant.cs (add property)

# Then sequentially:
T004: Blueprint.cs (depends on T001 for type reference)
T008: Tests (depends on all models)
```

## Parallel Example: Phase 2 Foundation

```
# Independent services — run in parallel:
T009: StructuralDiffService.cs (Register Service)
T010: SchemaFieldResolver.cs (UI Core)
T011: SchemaDescriptionExtractor.cs (Blueprint Schemas)

# Then tests in parallel:
T012: StructuralDiffServiceTests.cs
T013: SchemaFieldResolverTests.cs
T014: SchemaDescriptionExtractorTests.cs
```

---

## Implementation Strategy

### MVP First (US1 + US2)

1. Complete Phase 1: Setup (model changes)
2. Complete Phase 2: Foundation (diff service, field resolver)
3. Complete Phase 3: US1 (instructions + versioned publishing)
4. Complete Phase 4: US2 (unified diagram)
5. **STOP and VALIDATE**: Instructions editable, diagram consistent, versioned publishing works
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundation → Model layer ready
2. US1 (instructions + versioning) → Core value delivered
3. US2 (unified diagram) → Visual consistency
4. US3 (publishing blueprint) → Self-governance
5. US4 (handoff) → Designer integration
6. US5 (catalogue) → Discovery
7. US6 (translation) → i18n support
8. US7 (stub fixes) → Polish

Each increment adds value without breaking previous stories.

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Constitution requires >85% test coverage for new code — test tasks included
- All new endpoints need `.WithSummary()` and XML docs per constitution
- Markdown instruction content must be XSS-sanitized before rendering
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
