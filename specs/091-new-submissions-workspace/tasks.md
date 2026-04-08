---

description: "Task list for Feature 091 — New Submissions & Action Workspace"
---

# Tasks: New Submissions & Action Workspace

**Input**: Design documents from `/specs/091-new-submissions-workspace/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Branch**: `091-new-submissions-workspace`
**Tests**: Required per Sorcha constitution (Principle IV — minimum 80% unit test coverage, target >85% for new code, xUnit + FluentAssertions + Moq + Playwright E2E)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- File paths are absolute from repo root

## Path Conventions

- **Shared Models**: `src/Common/Sorcha.Blueprint.Models/`
- **UI Core (shared components)**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/`
- **UI Web Client (Blazor pages)**: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/`
- **Tests**: `tests/Sorcha.Blueprint.Models.Tests/`, `tests/Sorcha.UI.Core.Tests/`, `tests/Sorcha.UI.E2E.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify branch state and prepare workspace. No new project scaffolding required — feature uses existing projects.

- [X] T001 Verify branch `091-new-submissions-workspace` is checked out and clean (`git status`)
- [X] T002 Verify `dotnet build` succeeds on master baseline before adding new files
- [X] T003 Identify nav menu file path — locate `MainLayout.razor` containing the existing `my-workflows` MudNavLink

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema layout models and parser — every UI component depends on these. No user story can begin until this phase is complete.

**⚠️ CRITICAL**: Phase 2 must complete and pass tests before starting Phase 3+.

### Models

- [X] T004 [P] Create `BlueprintPageDefinition` record in `src/Common/Sorcha.Blueprint.Models/BlueprintPageDefinition.cs` with properties: `Title` (string, required), `Description` (string?), `Layout` (string?, default `single-column`), `Sections` (`List<BlueprintSectionDefinition>?`). Include XML doc comments and SPDX license header.

- [X] T005 [P] Create `BlueprintSectionDefinition` record in `src/Common/Sorcha.Blueprint.Models/BlueprintSectionDefinition.cs` with properties: `Title` (string, required), `Description` (string?), `Help` (string?), `Layout` (string?, default `vertical`), `Fields` (`List<string>`, required). Include XML doc comments and JSON property name attributes.

- [X] T006 [P] Create `SchemaLayoutInfo` record in `src/Common/Sorcha.Blueprint.Models/SchemaLayoutInfo.cs` with properties: `Pages` (`List<BlueprintPageDefinition>?`), `Sections` (`List<BlueprintSectionDefinition>?`), `Introduction` (string?), `FieldWidths` (`Dictionary<string, string>?`), computed `HasWizard => Pages?.Count > 0`, computed `HasSections => Sections?.Count > 0 || Pages?.Any(p => p.Sections?.Count > 0) == true`.

### Parser

- [X] T007 Create `SchemaLayoutParser` static class in `src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs` with method `Parse(JsonElement actionSchema)` returning `SchemaLayoutInfo`. Follow the `FileSchemaExtension.TryParseFromSchema` pattern. Read `x-pages`, `x-sections`, `x-introduction` from the root element. Extract `x-width` from each property under `properties`. Catch `JsonException` and return empty `SchemaLayoutInfo` on parse failure. Depends on T004, T005, T006.

- [X] T008 Add `TryGetFieldWidth(JsonElement propertySchema, out string? width)` static helper in `SchemaLayoutParser.cs`. Validates value is one of `full`, `half`, `third` (case-insensitive). Returns false for missing or invalid values.

### Foundational Tests

- [X] T009 [P] Create `tests/Sorcha.Blueprint.Models.Tests/SchemaLayoutParserTests.cs` with the following test methods (xUnit + FluentAssertions, naming pattern `Method_Scenario_ExpectedBehavior`):
  - `Parse_SchemaWithXPagesAndXSections_ReturnsPopulatedLayout`
  - `Parse_SchemaWithXSectionsOnly_ReturnsLayoutWithoutWizard`
  - `Parse_SchemaWithoutExtensions_ReturnsEmptyLayout`
  - `Parse_SchemaWithXIntroduction_PopulatesIntroduction`
  - `Parse_SchemaWithXWidthOnProperties_PopulatesFieldWidths`
  - `Parse_MalformedXPages_ReturnsEmptyLayoutWithoutThrowing`
  - `Parse_PagesContainingNestedXSections_ParsesNestedSections`
  - `Parse_NullJsonElement_ReturnsEmptyLayout`
  - `HasWizard_WithEmptyPages_ReturnsFalse`
  - `HasSections_WithSectionsInsidePages_ReturnsTrue`
  - `TryGetFieldWidth_ValidValue_ReturnsTrueAndValue` (Theory: full, half, third)
  - `TryGetFieldWidth_InvalidValue_ReturnsFalse`
  - `TryGetFieldWidth_MissingProperty_ReturnsFalse`

- [X] T010 Verify `Sorcha.Blueprint.Models.Tests` project references `Sorcha.Blueprint.Models` and includes xUnit, FluentAssertions packages. Run `dotnet test tests/Sorcha.Blueprint.Models.Tests --filter "FullyQualifiedName~SchemaLayoutParserTests"` and confirm all tests pass with >85% coverage of `SchemaLayoutParser.cs`.

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — Browse and Start a Blueprint Workflow (Priority: P1) 🎯 MVP

**Goal**: User can navigate to /new-submissions, browse the catalogue, click Start, fill a flat form (no wizard), and submit to create a new workflow instance. The 404 bug is eliminated.

**Independent Test**: Navigate to /new-submissions in the browser, select any blueprint (with no x-pages defined), submit the form, verify a new instance is created in the register and the user is redirected with a confirmation.

### NavigationStateService (US1 + US2 prerequisite)

- [X] T011 [US1] Create `NavigationStateService` class in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/NavigationStateService.cs` with `Set<T>(string key, T value)` and `Get<T>(string key)` methods. `Get` removes the entry on read. Backed by `Dictionary<string, object>`. Include SPDX header and XML doc comments.

- [X] T012 [P] [US1] Create `tests/Sorcha.UI.Core.Tests/Services/NavigationStateServiceTests.cs` with tests:
  - `Set_ThenGet_ReturnsValueAndRemovesEntry`
  - `Get_WithoutSet_ReturnsNull`
  - `Get_WithWrongType_ReturnsNull`
  - `Set_OverwritesPreviousValueForSameKey`
  - `Get_AfterTwoConsecutiveCalls_FirstReturnsValueSecondReturnsNull`

- [X] T013 [US1] Register `NavigationStateService` as scoped in DI. Locate the existing `Program.cs` for `Sorcha.UI.Web.Client` and add `builder.Services.AddScoped<NavigationStateService>();`.

### Listing Page

- [X] T014 [US1] Create `NewSubmissions.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmissions.razor` at route `/new-submissions`. Inject `IBlueprintApiService`, `IRegisterSubscriptionService`, `IWalletService`, `NavigationStateService`, `NavigationManager`, `IJSRuntime`. State: `_registerGroups` (`List<RegisterBlueprintGroup>`), `_viewMode` (`string`, default `cards`), `_searchQuery` (`string`), `_sortBy` (`string`, default `name`), `_isLoading` (`bool`).

- [X] T015 [US1] Implement `OnInitializedAsync` in `NewSubmissions.razor` to: (1) load wallets via `WalletService`, (2) load subscribed registers, (3) for each register call `BlueprintApiService.GetAvailableBlueprintsAsync(wallet, registerId)`, (4) flatten and group by register into `_registerGroups`, (5) read view mode from localStorage key `sorcha:newSubmissions:viewMode`. Handle errors per-register with `MudAlert` warnings.

- [X] T016 [US1] Implement card view markup in `NewSubmissions.razor`: Use `MudContainer MaxWidth.ExtraLarge`. Header with title "New Submissions" + subtitle. Controls bar: `MudTextField` search, `MudSelect` sort dropdown (Name/Register/Version), `MudToggleIconButton` view toggle. Below: filtered/sorted register groups, each containing `MudGrid` with `MudCard` items showing blueprint title, description (2-line clamp via inline style), version chip, starting action caption, and full-width "Start" `MudButton`.

- [X] T017 [US1] Implement table view markup in `NewSubmissions.razor`: `MudTable` with columns Blueprint | Register | Version | Starting Action | Actions. Hover enabled, dense, elevation 2. "Start" button in actions column.

- [X] T018 [US1] Implement skeleton loading (3 skeleton cards in `MudGrid`) and `EmptyState` component usage (icon `Inbox`, title "No blueprints available", description "Subscribe to a register to see available workflows", action "Manage Subscriptions" navigating to `/registers`).

- [X] T019 [US1] Implement `HandleStart(StartableBlueprintViewModel blueprint)` in `NewSubmissions.razor`: Store the published blueprint via `NavigationStateService.Set("blueprint:{registerId}:{blueprintId}", blueprint)`, then `NavigationManager.NavigateTo($"/new-submission/{blueprint.RegisterId}/{blueprint.BlueprintId}")`.

- [X] T020 [US1] Implement `ToggleViewMode(bool isTable)` in `NewSubmissions.razor`: Set `_viewMode` and persist to localStorage via `JSRuntime.InvokeVoidAsync("localStorage.setItem", "sorcha:newSubmissions:viewMode", _viewMode)`.

- [X] T021 [US1] Implement search and sort filtering as a computed property/method that filters `_registerGroups` by `_searchQuery` (matches blueprint title, description, register name) and sorts by `_sortBy`.

### Workspace Host Page (Flat Form Path)

- [X] T022 [US1] Create `NewSubmissionWorkspace.razor` page in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/NewSubmissionWorkspace.razor` at route `/new-submission/{RegisterId}/{BlueprintId}`. Inject `NavigationStateService`, `IBlueprintApiService`, `IWorkflowService`, `IWalletService`, `WalletPreferenceService`, `NavigationManager`. Parameters: `[Parameter] string RegisterId`, `[Parameter] string BlueprintId`.

- [X] T023 [US1] Implement `OnInitializedAsync` in `NewSubmissionWorkspace.razor`: (1) Try `NavigationStateService.Get<StartableBlueprintViewModel>("blueprint:{RegisterId}:{BlueprintId}")`. (2) If null (direct navigation): resolve default wallet via `WalletPreferenceService.GetSmartDefaultAsync(wallets)`, then call `BlueprintApiService.GetAvailableBlueprintsAsync(wallet, RegisterId)` and find the matching `BlueprintId`. (3) If still not found: show error `MudAlert` with link back to `/new-submissions`. (4) Once blueprint loaded, fetch the full blueprint detail (with action schemas) needed for form rendering — use the published blueprints endpoint, NEVER `GET /api/blueprints/{id}`.

- [X] T024 [US1] Implement workspace markup in `NewSubmissionWorkspace.razor`: Render the new `ActionWorkspace` component with parameters: `Blueprint`, `Action` (action 0), `RegisterId`, `SelectedWallet`, `SelectedWalletChanged` callback, `OnSubmit` callback, `OnCancel` callback, `ShowIntroduction=true`.

- [X] T025 [US1] Implement `HandleSubmit(FormSubmission submission)` in `NewSubmissionWorkspace.razor`: (1) Call `WorkflowService.CreateInstanceAsync(BlueprintId, RegisterId)` to get instance. (2) Build `ActionExecuteRequest` with `BlueprintId`, `ActionId="0"` (or `_action.Id.ToString()`), `InstanceId`, `SenderWallet`, `RegisterAddress=RegisterId`, `PayloadData=submission.Data`. (3) Call `WorkflowService.SubmitActionExecuteAsync(request)`. (4) On success: navigate to `/my-actions` or show success message with instance reference. (5) On failure: show `MudAlert` error, retain form data.

- [X] T026 [US1] Implement `HandleCancel` in `NewSubmissionWorkspace.razor` to confirm via `IDialogService` and navigate back to `/new-submissions`.

### ActionWorkspace Component (Flat Form Mode Only for US1)

- [X] T027 [US1] Create `ActionWorkspace.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/ActionWorkspace.razor`. Parameters: `Blueprint`, `Action`, `RegisterId`, `SelectedWallet`, `SelectedWalletChanged`, `OnSubmit`, `OnCancel`, `ShowIntroduction` (default true), optional `HeaderContent` RenderFragment.

- [X] T028 [US1] Implement workspace layout in `ActionWorkspace.razor`: Use `MudGrid` with `xs="12" md="8"` for form area and `xs="12" md="4"` for help panel. Form area contains: back link, context bar (blueprint name + version chip), wallet selector (compact display with change link), and `SorchaFormRenderer` with current Action. Help area uses sticky positioning with `FieldHelpPanel`. Bottom action bar with Cancel + Submit buttons.

- [X] T029 [US1] Parse the action schema with `SchemaLayoutParser.Parse(...)` in `ActionWorkspace.razor` `OnInitializedAsync`. Store result in `_layout` field. For US1 (flat form), only consume `_layout.Introduction` if `ShowIntroduction=true`. Wizard and section logic deferred to Phase 4.

- [X] T030 [US1] Implement form submission flow: Subscribe to `SorchaFormRenderer.OnSubmit`, forward `FormSubmission` to `OnSubmit` parameter. Show loading overlay (`MudOverlay`) during submission. On error, hide overlay and display `MudAlert`.

### Help Panel (Static Mode for US1)

- [X] T031 [US1] Create initial `FieldHelpPanel.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/FieldHelpPanel.razor`. Parameters: `FocusedField` (string?), `ActiveSection` (string?), `Schema` (JsonElement?), `Layout` (SchemaLayoutInfo?), `BlueprintDescription` (string?). For US1, render only the blueprint description fallback. Field-level help is deferred to US2.

### Navigation Menu Update

- [X] T032 [US1] Modify `MainLayout.razor` (path identified in T003): Change the "My Workflows" / "New Submission" `MudNavLink` href from `my-workflows` to `new-submissions`. Verify localization key still resolves (`@Loc.T("nav.newSubmission")`).

### MyWorkflows Cleanup

- [X] T033 [US1] Replace `MyWorkflows.razor` page contents with a redirect: On `OnInitializedAsync`, call `NavigationManager.NavigateTo("/new-submissions", replace: true)`. Keeps deep links working temporarily. Mark with TODO to remove in a future cleanup PR.

### US1 Tests

- [ ] T034 [P] [US1] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionListingTests.cs` with Playwright tests:
  - `Navigate_ToNewSubmissions_DisplaysBlueprintCatalogue`
  - `Search_ByBlueprintTitle_FiltersResults`
  - `ToggleView_FromCardsToTable_PersistsAcrossRefresh`
  - `Click_StartButton_NavigatesToWorkspace`
  - `EmptyState_NoBlueprints_DisplaysSubscribePrompt`

- [ ] T035 [P] [US1] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionFlatFormTests.cs` with Playwright tests:
  - `Workspace_FlatForm_RendersAllSchemaFields` (no x-pages)
  - `Workspace_FillAndSubmit_CreatesInstance`
  - `Workspace_DirectUrl_FetchesFromPublishedEndpoint` (verifies 404 bug is gone)
  - `Workspace_Cancel_ReturnsToListing`
  - `Workspace_SubmitWithError_RetainsFormData`

**Checkpoint**: User Story 1 complete — users can browse and start blueprints with flat forms. The 404 bug is eliminated. MVP achieved.

---

## Phase 4: User Story 2 — Contextual Help While Filling Forms (Priority: P1)

**Goal**: When the user focuses a form field, the right-side help panel shows that field's description, type, and constraints. Section-level and blueprint-level fallbacks are layered.

**Independent Test**: Open the workspace for any blueprint, focus a form field with a JSON Schema description, verify the help panel updates within 200ms with the field's information.

### SorchaFormRenderer Field Focus Event

- [X] T036 [US2] Modify `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Forms/SorchaFormRenderer.razor` to add a new `[Parameter] EventCallback<string?> OnFieldFocused` parameter. Wire focus/blur events on rendered controls (via the existing `FormContext` cascading value or a new event aggregator) so that field focus changes invoke the callback with the field name (or null on blur with no other focus).

- [ ] T037 [US2] Add unit test `tests/Sorcha.UI.Core.Tests/Components/Forms/SorchaFormRendererFocusTests.cs` (bUnit) verifying `OnFieldFocused` fires with the correct field name when a child input gains focus, and fires with null when focus leaves the form.

### FieldHelpPanel Field-Level Help

- [X] T038 [US2] Extend `FieldHelpPanel.razor` to render field-level help when `FocusedField` is non-null. Look up the field in `Schema.GetProperty("properties").GetProperty(FocusedField)`. Display: field title (use `title` property or humanise the field name), `description`, type, constraints (`minLength`, `maxLength`, `minimum`, `maximum`, `pattern`, `enum`), and required status (check parent schema's `required` array).

- [X] T039 [US2] Add section-level help fallback in `FieldHelpPanel.razor`: When `FocusedField` is null but `ActiveSection` is set, look up the section in `Layout.Sections` (or in the current page's sections if wizard is active) and display its `description`/`help` text.

- [X] T040 [US2] Add blueprint description fallback in `FieldHelpPanel.razor`: When neither field nor section help applies, display `BlueprintDescription` or `Layout.Introduction`.

- [X] T041 [US2] Style `FieldHelpPanel.razor` with sticky positioning, purple left border for active help, muted text for fallback content. Use existing MudBlazor theming variables.

### ActionWorkspace Wires Focus Events

- [X] T042 [US2] Modify `ActionWorkspace.razor` to track `_focusedField` state and pass it to `FieldHelpPanel`. Subscribe to `SorchaFormRenderer.OnFieldFocused` and update `_focusedField` on each invocation. Trigger `StateHasChanged` after updates.

### Responsive Behaviour

- [X] T043 [US2] Modify `ActionWorkspace.razor` markup so that on narrow viewports (`xs="12"` for both panels with `MudBreakpoint.SmAndDown`), the help panel renders below the form as a `MudExpansionPanel` collapsed by default. Use MudBlazor's responsive grid breakpoints.

### US2 Tests

- [ ] T044 [P] [US2] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionHelpPanelTests.cs` with Playwright tests:
  - `FocusField_WithDescription_ShowsFieldHelp`
  - `BlurField_WithSectionHelp_ShowsSectionHelp`
  - `IdleNoFocus_WithBlueprintDescription_ShowsBlueprintFallback`
  - `NarrowViewport_HelpPanelCollapses`

**Checkpoint**: User Story 2 complete — contextual help is layered and reactive. P1 stories all delivered.

---

## Phase 5: User Story 3 — Multi-Step Wizard for Complex Forms (Priority: P2)

**Goal**: Blueprints with `x-pages` defined render as multi-step wizards. Navigation enforces per-page validation. Form data persists across page navigation.

**Independent Test**: Publish a blueprint with `x-pages` defined containing 3 pages, start a new submission, fill page 1 with valid data, navigate to page 2, return to page 1 and verify data is preserved, complete all pages and submit successfully.

### WizardStepper Component

- [X] T045 [US3] Create `WizardStepper.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/WizardStepper.razor`. Parameters: `Pages` (`List<BlueprintPageDefinition>`), `CurrentPage` (int), `OnPageSelected` (`EventCallback<int>`). Render horizontal step indicator showing each page title with completed (check icon), active (filled circle), pending (outlined circle) states. Connecting lines between steps. Use MudBlazor `MudIcon` for step indicators.

- [ ] T046 [P] [US3] Create `tests/Sorcha.UI.Core.Tests/Components/Workflows/WizardStepperTests.cs` (bUnit) with tests:
  - `Render_WithThreePages_ShowsThreeSteps`
  - `Render_WithCurrentPageOne_HighlightsSecondStep`
  - `Click_OnCompletedStep_FiresOnPageSelected` (only completed/current pages clickable)

### ActionWorkspace Wizard Mode

- [X] T047 [US3] Modify `ActionWorkspace.razor` to support wizard mode when `_layout.HasWizard == true`. State: `_currentPage` (int, default 0), `_formData` (Dictionary<string, object?> accumulated across pages). Render `WizardStepper` above the form area. Compute current page's fields from `_layout.Pages[_currentPage].Sections.SelectMany(s => s.Fields)` plus any catch-all fields on the last page.

- [X] T048 [US3] Implement per-page form rendering in `ActionWorkspace.razor`: Pass a filtered schema (only current page's properties) to `SorchaFormRenderer`. The renderer already validates against the schema it receives, so filtering the schema scopes validation to the current page.

- [X] T049 [US3] Implement wizard navigation controls in `ActionWorkspace.razor`: Replace the bottom action bar with conditional buttons:
  - Page 0: Cancel + Next
  - Middle pages: Back + Next
  - Last page: Back + Submit
  Next button text: `"Next: {Pages[currentPage+1].Title} →"`

- [X] T050 [US3] Implement `HandleNext()` in `ActionWorkspace.razor`: Trigger `SorchaFormRenderer` validation for current page. If valid, merge form data into `_formData`, increment `_currentPage`, scroll to top. If invalid, leave `_currentPage` unchanged so errors display.

- [X] T051 [US3] Implement `HandleBack()` in `ActionWorkspace.razor`: Decrement `_currentPage`. Form data is already in `_formData` so re-rendering populates fields.

- [X] T052 [US3] Implement final `HandleSubmit()` in `ActionWorkspace.razor` for wizard mode: Build a final `FormSubmission` from `_formData` and forward to the `OnSubmit` parameter callback.

### US3 Tests

- [ ] T053 [P] [US3] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionWizardTests.cs` with Playwright tests:
  - `WizardBlueprint_RendersStepperAndPages`
  - `Wizard_NextWithInvalidFields_ShowsValidationErrors`
  - `Wizard_NextWithValidFields_AdvancesPage`
  - `Wizard_BackButton_PreservesData`
  - `Wizard_FinalSubmit_CombinesAllPagesData`
  - `Wizard_SkipToCompletedStep_AllowsBackNavigation`

- [ ] T054 [P] [US3] Create test fixture `tests/Sorcha.UI.E2E.Tests/Fixtures/WizardBlueprintFixture.cs` containing a sample blueprint JSON with 3 wizard pages, sections, and varied field types for E2E tests to publish to a test register.

**Checkpoint**: User Story 3 complete — multi-step wizards work end-to-end with per-page validation.

---

## Phase 6: User Story 4 — Grouped Field Sections Within Forms (Priority: P2)

**Goal**: Blueprints with `x-sections` (with or without `x-pages`) render fields in visual groups with configurable layout modes. Field width hints work.

**Independent Test**: Publish a blueprint with `x-sections` defined (no `x-pages`) containing horizontal, vertical, and grid layout sections. Start a new submission and verify each section renders with its title, optional description, and the configured layout. Verify `x-width` hints affect column widths.

### FormSection Component

- [ ] T055 [US4] Create `FormSection.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/FormSection.razor`. Parameters: `Section` (BlueprintSectionDefinition), `Schema` (JsonElement), `FormData` (`Dictionary<string, object?>`), `FieldWidths` (`Dictionary<string, string>?`), `OnFieldFocused` (`EventCallback<string?>`). Render section title (`MudText Typo.h6`), optional description (`MudText Typo.body2`), and a `MudGrid` containing fields.

- [ ] T056 [US4] Implement layout mode rendering in `FormSection.razor`:
  - `vertical` (default): Each field in `MudItem xs="12"`
  - `horizontal`: Fields share a row, each in `MudItem` with `xs="12" sm="{12/fieldCount}"`
  - `grid`: Fields in `MudItem xs="12" sm="6"` (2-column auto-flow)
  Apply field-level `x-width` overrides via `FieldWidths` lookup: full=12, half=6, third=4.

- [ ] T057 [US4] Each field within `FormSection.razor` is rendered by delegating to a single-field variant of `SorchaFormRenderer` or a small dispatcher that builds a per-field schema and invokes the appropriate control. To avoid duplicating renderer logic, prefer extending `SorchaFormRenderer` to accept an optional `FieldFilter` parameter (`HashSet<string>?`) that limits which fields it renders.

- [X] T058 [US4] Modify `SorchaFormRenderer.razor` to accept `[Parameter] HashSet<string>? FieldFilter` and `[Parameter] Dictionary<string, string>? FieldWidths`. When `FieldFilter` is set, only render properties whose names are in the set. When `FieldWidths` is set, override the column width for matching fields. Default behaviour (both null) is unchanged — backwards compatible.

### ActionWorkspace Section Mode

- [ ] T059 [US4] Modify `ActionWorkspace.razor` to support section rendering when `_layout.HasSections == true` but `_layout.HasWizard == false`. Render each section in `_layout.Sections` using `FormSection` component. Pass `FormData` and `OnFieldFocused`.

- [ ] T060 [US4] Modify `ActionWorkspace.razor` wizard mode (Phase 5) to render sections within the current page when `_layout.Pages[_currentPage].Sections` is non-empty. Iterate sections and render via `FormSection`. If a page has no sections, fall back to flat rendering for that page.

### Catch-All Field Rendering

- [ ] T061 [US4] Implement catch-all logic in `ActionWorkspace.razor`: After computing the union of all fields referenced by sections and pages, identify any properties in the schema that are NOT referenced. Render these orphan fields in a final unnamed group at the bottom of the form (or last wizard page). Add a comment in the rendered output indicating they are catch-all fields for diagnostic purposes.

### US4 Tests

- [ ] T062 [P] [US4] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionSectionsTests.cs` with Playwright tests:
  - `SectionsBlueprint_RendersSectionsWithTitles`
  - `HorizontalLayout_FieldsRenderSideBySide`
  - `GridLayout_FieldsRenderInTwoColumns`
  - `XWidthHalf_FieldOccupiesHalfWidth`
  - `OrphanFields_RenderInCatchAll`

- [ ] T063 [P] [US4] Create `tests/Sorcha.UI.Core.Tests/Components/Workflows/FormSectionTests.cs` (bUnit) verifying layout modes apply correct grid breakpoints and `OnFieldFocused` propagates correctly.

**Checkpoint**: User Story 4 complete — sections render with all layout modes. Both P2 stories delivered.

---

## Phase 7: User Story 5 — Blueprint Introduction Text (Priority: P3)

**Goal**: When starting a new submission, an introduction callout displays above the form using `x-introduction` from the action schema, falling back to the blueprint description.

**Independent Test**: Publish a blueprint with `x-introduction` text on its starting action schema. Start a new submission and verify the callout appears above the form with the introduction text. Then test with a blueprint without `x-introduction` and verify the blueprint description is shown.

- [X] T064 [US5] Modify `ActionWorkspace.razor` to render an introduction callout when `ShowIntroduction == true`. Source priority: (1) `_layout.Introduction` from `x-introduction`, (2) `Blueprint.Description`, (3) hide if both empty. Use `MudPaper` with purple left border and "ABOUT THIS WORKFLOW" label. Place above wallet selector and wizard stepper.

- [ ] T065 [P] [US5] Create `tests/Sorcha.UI.E2E.Tests/NewSubmissionIntroductionTests.cs` with Playwright tests:
  - `XIntroduction_DisplaysCalloutAboveForm`
  - `NoXIntroduction_FallsBackToBlueprintDescription`
  - `NeitherDefined_NoCalloutShown`
  - `ShowIntroductionFalse_HidesCalloutEvenIfDefined`

**Checkpoint**: User Story 5 complete — all five user stories delivered.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, integration verification, performance validation, and cleanup.

- [ ] T066 Run full test suite (`dotnet test`) and verify all tests pass. Confirm `Sorcha.Blueprint.Models.Tests` and `Sorcha.UI.Core.Tests` coverage exceeds 85% for new files.

- [ ] T067 Run Playwright E2E tests in headless mode against Docker compose stack (`dotnet test tests/Sorcha.UI.E2E.Tests`). All tests pass.

- [ ] T068 [P] Verify performance: Open the listing page with 10 register subscriptions and confirm load completes within 3 seconds (SC-006). Use browser DevTools Network tab to measure.

- [ ] T069 [P] Verify performance: Focus a form field and confirm help panel updates within 200ms (SC-005). Use browser DevTools Performance tab.

- [ ] T070 [P] Update `CLAUDE.md` to document the new `x-pages`, `x-sections`, `x-width`, `x-introduction` JSON Schema vendor extensions in the "Critical Patterns" section. Reference the design spec.

- [ ] T071 [P] Update `docs/reference/development-status.md` to mark Feature 091 as complete with completion date and PR link.

- [ ] T072 [P] Update `.specify/MASTER-TASKS.md` to mark Feature 091 tasks as completed (📋 → ✅).

- [ ] T073 [P] Update the blueprint authoring guide in `walkthroughs/README.md` (or create a new doc under `docs/guides/`) explaining how to use the layout extensions with examples.

- [ ] T074 Verify backwards compatibility: Run the existing ConstructionPermit walkthrough end-to-end and confirm forms render and submit correctly without changes to its blueprint JSON.

- [ ] T075 Run `dotnet format` to ensure consistent code style across all new files.

- [ ] T076 Create a sample blueprint with all layout extensions in `walkthroughs/` (or extend ConstructionPermit) demonstrating x-pages, x-sections, x-width, and x-introduction in action.

- [ ] T077 Run `quickstart.md` validation: Follow the quickstart steps as a developer would to verify the documentation is accurate and the feature works as described.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational — Models + Parser)**: Depends on Setup. **BLOCKS all user story phases.**
- **Phase 3 (US1 — Browse + Start, P1 MVP)**: Depends on Phase 2
- **Phase 4 (US2 — Contextual Help, P1)**: Depends on Phase 3 (extends ActionWorkspace and FieldHelpPanel created in Phase 3)
- **Phase 5 (US3 — Wizard, P2)**: Depends on Phase 3 (extends ActionWorkspace). Can run in parallel with Phase 4 if a different developer takes it.
- **Phase 6 (US4 — Sections, P2)**: Depends on Phase 3. Can run in parallel with Phase 4 and Phase 5.
- **Phase 7 (US5 — Introduction, P3)**: Depends on Phase 3 (extends ActionWorkspace). Trivial change, can be slotted anywhere after Phase 3.
- **Phase 8 (Polish)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **US1 (P1)**: Depends only on Foundational (Phase 2)
- **US2 (P1)**: Builds on US1 components but tests/code are isolated. Independently testable once US1 ActionWorkspace exists.
- **US3 (P2)**: Builds on US1 ActionWorkspace. Independent of US2.
- **US4 (P2)**: Builds on US1 ActionWorkspace and modifies SorchaFormRenderer. Independent of US2 and US3 (though Phase 5 + Phase 6 share ActionWorkspace edits — coordinate to avoid merge conflicts).
- **US5 (P3)**: Trivial extension to US1 ActionWorkspace. Independent of all P2 stories.

### Within Each User Story

- Models before services
- Services before components
- Components before pages
- Pages before E2E tests
- All unit tests can be written in parallel with implementation (within their files)

### Parallel Opportunities

- **Phase 2 models** (T004, T005, T006) — three separate files, fully parallel
- **Phase 2 tests** (T009) — independent of model implementation order
- **Phase 3 NavigationStateService tests** (T012) — independent of T011 implementation
- **All E2E test tasks** (T034, T035, T044, T053, T062, T065) — different files, parallel
- **Phase 8 documentation tasks** (T070, T071, T072, T073) — different files, parallel
- **Phase 8 performance verification** (T068, T069) — independent measurements, parallel
- **US3, US4, US5** can be developed in parallel by 3 developers after Phase 3 completes (with coordination on ActionWorkspace.razor edits)

---

## Parallel Example: Phase 2 Foundational

```bash
# Three model files can be created in parallel:
Task: "Create BlueprintPageDefinition record in src/Common/Sorcha.Blueprint.Models/BlueprintPageDefinition.cs"
Task: "Create BlueprintSectionDefinition record in src/Common/Sorcha.Blueprint.Models/BlueprintSectionDefinition.cs"
Task: "Create SchemaLayoutInfo record in src/Common/Sorcha.Blueprint.Models/SchemaLayoutInfo.cs"

# Then parser depends on the three above:
Task: "Create SchemaLayoutParser static class in src/Common/Sorcha.Blueprint.Models/SchemaLayoutParser.cs"

# Tests can run in parallel with parser implementation:
Task: "Create tests/Sorcha.Blueprint.Models.Tests/SchemaLayoutParserTests.cs with parser unit tests"
```

## Parallel Example: Phase 3 US1 (after foundational complete)

```bash
# NavigationStateService and its tests in parallel:
Task: "Create NavigationStateService class in src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/NavigationStateService.cs"
Task: "Create tests/Sorcha.UI.Core.Tests/Services/NavigationStateServiceTests.cs"

# E2E tests for listing and form (different test classes):
Task: "Create tests/Sorcha.UI.E2E.Tests/NewSubmissionListingTests.cs"
Task: "Create tests/Sorcha.UI.E2E.Tests/NewSubmissionFlatFormTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 + 2 — both P1)

1. Complete Phase 1: Setup (verify branch state)
2. Complete Phase 2: Foundational (BLOCKS everything)
3. Complete Phase 3: User Story 1 (listing + workspace + flat form)
4. **Validate**: Test User Story 1 — start a flat-form blueprint, fix the 404 bug, instance is created
5. Complete Phase 4: User Story 2 (contextual help)
6. **Validate**: Test User Story 2 — focus fields, help panel updates
7. **Deploy/demo MVP** — both P1 stories complete

### Incremental Delivery After MVP

8. Complete Phase 5: User Story 3 (wizard)
9. **Validate**: Test wizard navigation
10. Complete Phase 6: User Story 4 (sections)
11. **Validate**: Test section layout modes
12. Complete Phase 7: User Story 5 (introduction)
13. **Validate**: Test introduction callout
14. Complete Phase 8: Polish + documentation
15. Final validation and PR

### Parallel Team Strategy

With multiple developers after Phase 3 completes:
- **Developer A**: Phase 4 (US2 — contextual help)
- **Developer B**: Phase 5 (US3 — wizard)
- **Developer C**: Phase 6 (US4 — sections)
- **Developer A** (after Phase 4): Phase 7 (US5 — introduction)

Coordinate ActionWorkspace.razor edits via PR rebase or feature flags.

---

## Notes

- **Constitution compliance**: Tests required (>85% coverage target). All tasks include tests per Sorcha Principle IV.
- **Backwards compatibility**: Existing blueprints without x-extensions must render identically. Verified by T074 and T058's default-null parameters.
- **No backend changes**: All work is in `Sorcha.Blueprint.Models` (shared) and `Sorcha.UI.*` projects. No services, no APIs, no database.
- **The 404 bug**: Eliminated by T023 (NewSubmissionWorkspace fetches from published endpoint, never `/api/blueprints/{id}`).
- **Existing patterns**: Follow `FileSchemaExtension` precedent for parser (T007), `MyActions.razor` for view toggle pattern (T020), `WalletPreferenceService` for wallet defaults (T023).
- **Avoid**: vague tasks, same-file conflicts (especially ActionWorkspace.razor across phases 4-7 — coordinate or sequence), cross-story dependencies that break independence.
