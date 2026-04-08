# Implementation Plan: New Submissions & Action Workspace

**Branch**: `091-new-submissions-workspace` | **Date**: 2026-04-08 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/091-new-submissions-workspace/spec.md`
**Design**: `docs/superpowers/specs/2026-04-08-new-submissions-workspace-design.md`

## Summary

Replace the broken MyWorkflows page (404 on published blueprints) and cramped NewSubmissionDialog with a searchable blueprint catalogue and full-page action workspace. Add JSON Schema vendor extensions (`x-pages`, `x-sections`, `x-width`, `x-introduction`) for wizard pages, field grouping, and contextual help. No new backend endpoints — all changes are UI and model layer.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Blazor WASM, MudBlazor, Sorcha.Blueprint.Models, Sorcha.UI.Core
**Storage**: N/A (client-side state only, no new persistence)
**Testing**: xUnit + FluentAssertions + Moq (unit), Playwright (E2E)
**Target Platform**: Blazor WASM (browser), responsive desktop + tablet
**Project Type**: Web (frontend-only feature with shared model library)
**Performance Goals**: Listing page loads in <3s for 10 registers; help panel updates in <200ms on field focus
**Constraints**: Backwards compatible — existing blueprints without x-extensions must render identically to today
**Scale/Scope**: ~12 new files, ~5 modified files, ~1500 lines of new code + ~500 lines of tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No cross-service changes. All changes in UI layer + shared models. |
| II. Security First | PASS | No new inputs from external boundaries. Form data validated via existing JSON Schema validation. |
| III. API Documentation | N/A | No new API endpoints. |
| IV. Testing Requirements | PASS | Unit tests for parser + NavigationStateService. E2E tests for submission flow. Target >85%. |
| V. Code Quality | PASS | C# 13, async/await, DI, nullable enabled. |
| VI. Blueprint Creation Standards | PASS | x-extensions are JSON Schema vendor extensions stored in blueprint JSON. No C# code generation. |
| VII. Domain-Driven Design | PASS | Uses Sorcha terminology: Blueprint, Action, Participant. |
| VIII. Observability | PASS | Existing structured logging in UI services. No new services to instrument. |

**Gate result**: PASS — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/091-new-submissions-workspace/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Entity definitions and schema extensions
├── quickstart.md        # Developer quickstart guide
├── contracts/
│   └── ui-components.md # Component interfaces and service contracts
└── checklists/
    └── requirements.md  # Specification quality checklist
```

### Source Code (repository root)

```text
src/Common/Sorcha.Blueprint.Models/
├── BlueprintPageDefinition.cs       # NEW: x-pages model
├── BlueprintSectionDefinition.cs    # NEW: x-sections model
├── SchemaLayoutInfo.cs              # NEW: parsed layout result
└── SchemaLayoutParser.cs            # NEW: x-extension parser

src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Services/
│   └── NavigationStateService.cs    # NEW: page-to-page state passing
└── Components/
    └── Workflows/
        ├── ActionWorkspace.razor    # NEW: 2/3 form + 1/3 help layout
        ├── WizardStepper.razor      # NEW: wizard step indicator
        ├── FormSection.razor        # NEW: section renderer with layouts
        └── FieldHelpPanel.razor     # NEW: contextual help panel

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Pages/
│   ├── NewSubmissions.razor         # NEW: blueprint catalogue listing
│   └── NewSubmissionWorkspace.razor # NEW: workspace host page
└── Components/
    └── Layout/
        └── MainLayout.razor         # MODIFY: update nav link

src/Apps/Sorcha.UI/Sorcha.UI.Core/
└── Components/
    └── Forms/
        └── SorchaFormRenderer.razor # MODIFY: add OnFieldFocused, section awareness

tests/
├── Sorcha.Blueprint.Models.Tests/
│   └── SchemaLayoutParserTests.cs   # NEW: parser unit tests
├── Sorcha.UI.Core.Tests/
│   └── NavigationStateServiceTests.cs # NEW: service unit tests
└── Sorcha.UI.E2E.Tests/
    └── NewSubmissionTests.cs        # NEW: E2E tests
```

**Structure Decision**: Frontend-only feature spanning two existing projects (`Sorcha.Blueprint.Models` for shared models, `Sorcha.UI.*` for Blazor components). No new projects created.

## Implementation Phases

### Phase 1: Schema Layout Models + Parser (P2 prerequisite)

**Goal**: Create the model classes and parser that all UI components depend on.

**Files**:
1. `src/Common/Sorcha.Blueprint.Models/BlueprintPageDefinition.cs` — record with Title, Description, Layout, Sections
2. `src/Common/Sorcha.Blueprint.Models/BlueprintSectionDefinition.cs` — record with Title, Description, Help, Layout, Fields
3. `src/Common/Sorcha.Blueprint.Models/SchemaLayoutInfo.cs` — result record with Pages, Sections, Introduction, FieldWidths, HasWizard, HasSections
4. `src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs` — static Parse(JsonElement) and TryGetFieldWidth methods
5. `tests/Sorcha.Blueprint.Models.Tests/SchemaLayoutParserTests.cs` — comprehensive parser tests

**Tests**:
- Parse schema with x-pages + x-sections → returns populated SchemaLayoutInfo
- Parse schema with x-sections only (no x-pages) → sections but no wizard
- Parse schema with no extensions → empty SchemaLayoutInfo, HasWizard=false, HasSections=false
- Parse schema with x-width on properties → FieldWidths dictionary populated
- Parse schema with x-introduction → Introduction populated
- Parse schema with invalid/malformed x-pages → graceful fallback (empty, no exception)
- TryGetFieldWidth with valid values (full, half, third) → returns true + value
- TryGetFieldWidth with missing/invalid → returns false

**Acceptance**: All parser tests pass. Models compile and serialize/deserialize correctly.

### Phase 2: NavigationStateService + Workspace Infrastructure (P1 core)

**Goal**: Create the navigation state service and reusable ActionWorkspace component.

**Files**:
1. `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/NavigationStateService.cs` — scoped service with Set/Get
2. `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/FieldHelpPanel.razor` — contextual help panel
3. `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/ActionWorkspace.razor` — 2/3 + 1/3 layout with form + help
4. `tests/Sorcha.UI.Core.Tests/NavigationStateServiceTests.cs` — unit tests

**Tests**:
- NavigationStateService: Set then Get returns value and removes entry
- NavigationStateService: Get with no Set returns null
- NavigationStateService: Get with wrong type returns null
- NavigationStateService: Set overwrites previous value for same key

**Acceptance**: NavigationStateService tests pass. ActionWorkspace renders with flat form (no wizard).

### Phase 3: New Submissions Listing Page (P1 core)

**Goal**: Replace MyWorkflows with the searchable catalogue.

**Files**:
1. `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmissions.razor` — catalogue page
2. `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmissionWorkspace.razor` — workspace host
3. Modify `MainLayout.razor` — update nav link href and text
4. Remove or redirect `MyWorkflows.razor` → `/new-submissions`

**Tests** (E2E):
- Navigate to /new-submissions → shows blueprints grouped by register
- Search filters blueprint cards by title
- Toggle card/list view, refresh page → view mode persisted
- Click Start → navigates to /new-submission/{registerId}/{blueprintId}
- Workspace loads with form, fill and submit → instance created
- Direct URL to workspace (no nav state) → falls back to API fetch

**Acceptance**: User can browse, search, and start a new submission end-to-end. 404 bug is eliminated.

### Phase 4: Wizard Stepper + Sections (P2)

**Goal**: Add wizard pages and field grouping to the form renderer.

**Files**:
1. `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/WizardStepper.razor` — step indicator
2. `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/FormSection.razor` — section renderer with layout modes
3. Modify `ActionWorkspace.razor` — integrate wizard navigation + sections
4. Modify `SorchaFormRenderer.razor` — add OnFieldFocused callback, section/page subset rendering

**Tests** (E2E):
- Blueprint with x-pages → wizard stepper renders, pages navigate with Next/Back
- Blueprint with x-sections (no pages) → sections render with titles, no wizard
- Per-page validation → invalid fields block Next
- Form data preserved across page navigation
- Section layout modes → horizontal renders side-by-side, grid renders 2-column
- x-width on fields → correct column widths applied
- Catch-all → fields not in any section render at bottom

**Acceptance**: Wizard navigation works. Sections group fields visually. Layout modes apply correctly.

### Phase 5: Introduction Text + Polish (P3)

**Goal**: Add blueprint introduction callout and responsive behaviour.

**Files**:
1. Modify `ActionWorkspace.razor` — x-introduction callout rendering
2. Add responsive CSS/Blazor for help panel collapse on narrow screens

**Tests** (E2E):
- Blueprint with x-introduction → callout displays above form
- Blueprint without x-introduction → falls back to blueprint description
- Neither → no callout shown
- Narrow viewport → help panel collapses to drawer

**Acceptance**: Introduction text displays. Help panel is responsive.

## Phase Dependencies

```
Phase 1 (Models + Parser)
    ↓
Phase 2 (NavigationState + Workspace)
    ↓
Phase 3 (Listing + Workspace Pages) ← P1 complete here
    ↓
Phase 4 (Wizard + Sections) ← P2 complete here
    ↓
Phase 5 (Introduction + Polish) ← P3 complete here
```

## Risk Register

| Risk | Impact | Mitigation |
|------|--------|------------|
| SorchaFormRenderer modifications break existing forms | High | Phase 1 parser is independent. Renderer changes are additive (new parameters, not changed behaviour). Test existing flat form rendering first. |
| Published blueprints endpoint doesn't return full action schemas | Medium | Verified in research — the endpoint returns `BlueprintInfoViewModel` with action schemas. If insufficient, extend the response model (backend change, low effort). |
| Wizard page validation requires schema splitting | Low | JSON Schema's `required` array can be filtered to current page's fields. No schema modification needed. |
