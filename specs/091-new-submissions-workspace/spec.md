# Feature Specification: New Submissions & Action Workspace

**Feature Branch**: `091-new-submissions-workspace`  
**Created**: 2026-04-08  
**Status**: Draft  
**Input**: Replace the broken MyWorkflows page and NewSubmissionDialog with a searchable blueprint catalogue, a full-page action workspace with contextual help, and schema layout extensions for wizard pages and field grouping.
**Design Spec**: `docs/superpowers/specs/2026-04-08-new-submissions-workspace-design.md`

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Browse and Start a Blueprint Workflow (Priority: P1)

A platform user navigates to "New Submissions" to see all available blueprints across their subscribed registers. They search or filter to find the right blueprint, then click "Start" to open a full-page form where they fill in the required fields and submit, creating a new workflow instance.

**Why this priority**: This is the core user journey — without it, users cannot initiate new workflows. The current implementation is broken (404 on published blueprints) and uses a cramped dialog.

**Independent Test**: Can be fully tested by navigating to /new-submissions, selecting a published blueprint, filling in a flat form (no wizard), and submitting. Delivers a working new-submission flow that replaces the broken dialog.

**Acceptance Scenarios**:

1. **Given** a user with wallet access and register subscriptions, **When** they navigate to /new-submissions, **Then** they see a catalogue of available blueprints grouped by register with search, sort, and card/list toggle controls.
2. **Given** the catalogue is displayed, **When** the user clicks "Start" on a blueprint card, **Then** they are navigated to /new-submission/{registerId}/{blueprintId} showing a full-page action workspace with the starting action's form.
3. **Given** the action workspace is displayed with a flat schema (no x-pages), **When** the user fills in all required fields and clicks "Submit", **Then** a new workflow instance is created, the starting action is executed, and the user is redirected with a success confirmation.
4. **Given** the user navigates directly to a workspace URL (bookmark/refresh), **When** the page loads, **Then** the published blueprint is fetched from the register's published blueprints (not the draft store) and the form renders correctly.
5. **Given** the user clicks "Cancel" on the workspace, **When** they confirm, **Then** they return to /new-submissions with no instance created.

---

### User Story 2 - Contextual Help While Filling Forms (Priority: P1)

While filling in the action form, the user sees a help panel on the right side of the workspace. When they focus a field, the panel shows that field's description, constraints, and requirements. When no field is focused, it shows section-level or blueprint-level guidance.

**Why this priority**: Field-level help is critical for complex workflows where users need guidance on what to enter. Without it, users submit incorrect data causing rejections and delays.

**Independent Test**: Can be tested by focusing different fields in the action workspace and verifying the help panel updates with relevant contextual information sourced from the JSON Schema descriptions.

**Acceptance Scenarios**:

1. **Given** the action workspace is displayed, **When** the user focuses a form field, **Then** the right panel shows that field's description, type, constraints (min/max length, pattern, required status), sourced from the JSON Schema.
2. **Given** a field is focused within a section that has help text, **When** the user blurs the field without focusing another, **Then** the panel shows the section-level help text.
3. **Given** no field is focused and no section help exists, **When** the workspace is idle, **Then** the panel shows the blueprint description or introduction text as a fallback.
4. **Given** the user is on a mobile/tablet device, **When** the screen is narrow, **Then** the help panel collapses to a toggleable drawer below the form.

---

### User Story 3 - Multi-Step Wizard for Complex Forms (Priority: P2)

Blueprint authors define multi-step wizard flows using `x-pages` in the action schema. Users see a step indicator and navigate through pages with Next/Back buttons. Validation occurs per page — users cannot advance past a page with invalid fields.

**Why this priority**: Many real-world workflows have complex forms that benefit from progressive disclosure. However, the flat form (P1) must work first since most existing blueprints don't have x-pages defined.

**Independent Test**: Can be tested by publishing a blueprint with x-pages defined, starting a new submission, navigating through wizard pages, and verifying per-page validation and final submission.

**Acceptance Scenarios**:

1. **Given** a blueprint with `x-pages` defined in the action schema, **When** the user starts a new submission, **Then** a wizard stepper appears showing page titles with the first page active.
2. **Given** the user is on wizard page 1, **When** they click "Next" with invalid fields, **Then** validation errors appear on the invalid fields and navigation is blocked.
3. **Given** the user is on wizard page 1 with valid fields, **When** they click "Next", **Then** page 2 is displayed and the stepper updates. Form data from page 1 is retained.
4. **Given** the user is on the last wizard page, **When** they click "Submit", **Then** all pages' data is combined and submitted as a single action payload.
5. **Given** the user is on page 3, **When** they click "Back" twice, **Then** they return to page 1 with all previously entered data intact.

---

### User Story 4 - Grouped Field Sections Within Forms (Priority: P2)

Blueprint authors group related fields into visual sections using `x-sections`. Each section has a title, optional description, and configurable layout (vertical, horizontal, or grid). Field width hints allow fine-grained control.

**Why this priority**: Sections improve form usability by grouping related fields (e.g., address fields together). Works with or without wizard pages.

**Independent Test**: Can be tested by publishing a blueprint with x-sections (no x-pages), starting a new submission, and verifying fields are visually grouped with section headers and the configured layout.

**Acceptance Scenarios**:

1. **Given** a blueprint with `x-sections` defined (no x-pages), **When** the user starts a new submission, **Then** fields are grouped into visual sections with titles and optional descriptions. No wizard stepper appears.
2. **Given** a section with `layout: "horizontal"`, **When** the form renders, **Then** the section's fields appear side-by-side in a row.
3. **Given** a section with `layout: "grid"`, **When** the form renders, **Then** the section's fields flow into a 2-column grid.
4. **Given** a field with `x-width: "half"`, **When** the form renders within any section layout, **Then** that field occupies half the available width.
5. **Given** fields exist in `properties` but are not referenced in any section, **When** the form renders, **Then** those fields appear at the bottom of the form as a catch-all (no data loss).

---

### User Story 5 - Blueprint Introduction Text (Priority: P3)

When starting a new submission, the user sees an introduction callout above the form explaining the workflow purpose, requirements, and expected outcomes. This text comes from `x-introduction` on the action schema or falls back to the blueprint description.

**Why this priority**: Helpful but not blocking — users can submit without it. Adds polish and reduces confusion for first-time users of a workflow.

**Independent Test**: Can be tested by publishing a blueprint with x-introduction text, starting a new submission, and verifying the callout appears above the form.

**Acceptance Scenarios**:

1. **Given** a blueprint action with `x-introduction` defined, **When** the user starts a new submission, **Then** a highlighted callout displays the introduction text above the form.
2. **Given** a blueprint action without `x-introduction`, **When** the user starts a new submission, **Then** the callout displays the blueprint's description instead.
3. **Given** neither `x-introduction` nor blueprint description exists, **When** the user starts a new submission, **Then** no callout is shown.

---

### Edge Cases

- What happens when a user starts a submission but the blueprint is unpublished before they submit? The submit call will fail; the workspace shows an error and retains form data.
- What happens when the user's wallet is revoked mid-form? The submit call returns an auth error; the workspace displays the error without losing form data.
- What happens when `x-pages` references a field name that doesn't exist in `properties`? The field is silently skipped (no crash). The catch-all rule ensures no orphaned fields.
- What happens when two sections in `x-sections` reference the same field? The field renders in the first section that references it; subsequent references are ignored.
- What happens when the user has multiple wallets? The workspace shows the default wallet with a "change" option. Wallet selection is persisted across sessions.
- What happens when no blueprints are available across any register? The listing page shows an empty state explaining how to subscribe to registers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST display a searchable, sortable catalogue of published blueprints grouped by register at /new-submissions.
- **FR-002**: System MUST support card view and list/table view with user preference persisted to local storage.
- **FR-003**: System MUST navigate to /new-submission/{registerId}/{blueprintId} when the user starts a blueprint.
- **FR-004**: System MUST render a full-page action workspace with a 2/3 form panel and 1/3 contextual help panel.
- **FR-005**: System MUST create a new workflow instance and execute the starting action on form submission.
- **FR-006**: System MUST fetch published blueprint data from the published blueprints endpoint, never from the draft store.
- **FR-007**: System MUST pass blueprint data between the listing and workspace pages via a navigation state service, falling back to an API fetch on direct navigation.
- **FR-008**: System MUST display field-level help (description, type, constraints) when a form field is focused.
- **FR-009**: System MUST display section-level help when a section is active but no field is focused.
- **FR-010**: System MUST fall back to blueprint description when no field or section help is available.
- **FR-011**: System MUST render a wizard stepper and support multi-page navigation when `x-pages` is defined in the action schema.
- **FR-012**: System MUST validate only the current wizard page's fields before allowing navigation to the next page.
- **FR-013**: System MUST retain form data across wizard page navigation (Next/Back).
- **FR-014**: System MUST render fields in visual sections with titles and descriptions when `x-sections` is defined.
- **FR-015**: System MUST support section layout modes: vertical (default), horizontal (side-by-side), and grid (2-column auto-flow).
- **FR-016**: System MUST support field width hints via `x-width`: full, half, third.
- **FR-017**: System MUST display blueprint introduction text from `x-introduction` (or blueprint description fallback) as a callout above the form for new submissions.
- **FR-018**: System MUST render a flat form in property order when no `x-pages` or `x-sections` are defined (backwards compatibility).
- **FR-019**: System MUST render fields not referenced in any section or page at the bottom of the form as a catch-all.
- **FR-020**: System MUST collapse the help panel to a toggleable drawer on narrow screens (tablet/mobile).

### Key Entities

- **Published Blueprint**: A versioned blueprint published to a specific register, containing action schemas with optional layout extensions. Fetched via the existing published blueprints endpoint.
- **Blueprint Page Definition**: A wizard page with title, description, layout mode, and sections. Defined via `x-pages` on the action schema.
- **Blueprint Section Definition**: A visual field group with title, description, help text, layout mode, and field references. Defined via `x-sections`.
- **Navigation State**: Transient in-memory key-value store (scoped to browser tab lifetime) for passing objects between page navigations without URL serialisation.
- **Workflow Instance**: Created when the user submits the starting action. Contains the instance ID, blueprint reference, and register reference.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can browse available blueprints and start a new submission within 3 clicks from the navigation menu.
- **SC-002**: The 404 error on starting blueprints from non-authoring nodes is eliminated — submissions work on any node with published blueprints.
- **SC-003**: All existing blueprints without layout extensions render identically to the current form behaviour (zero regression).
- **SC-004**: Users can complete a 3-page wizard form and submit successfully, with all page data preserved across navigation.
- **SC-005**: Field-level help appears within 200ms of focusing a form field (perceived as instant).
- **SC-006**: The listing page loads and displays blueprint cards within 3 seconds for a user with up to 10 register subscriptions.
- **SC-007**: The help panel provides relevant contextual guidance for every field that has a JSON Schema description defined.

## Assumptions

- Existing blueprints do not have `x-pages`, `x-sections`, `x-width`, or `x-introduction` defined. The system must render them as flat forms with no wizard (backwards compatible).
- The `x-` prefix convention for vendor extensions is already established in the codebase (see `x-file` in Feature 085).
- The published blueprints endpoint (`GET /api/actions/{wallet}/{register}/blueprints`) returns sufficient data to render the form, including full action schemas. No new backend endpoints are required.
- Blueprint introduction text is optional and most blueprints will initially rely on the description fallback.
- Per-page wizard validation uses the existing JSON Schema validation logic, scoped to the current page's field subset.

## Dependencies

- Existing published blueprints endpoint and view models (no changes needed)
- Existing `SorchaFormRenderer` component (will be extended, not replaced)
- Existing `WalletPreferenceService` for wallet selection defaults
- Existing MudBlazor component library for UI elements
- `Sorcha.Blueprint.Models` for new model classes (page/section definitions)
