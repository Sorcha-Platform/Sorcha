# New Submissions & Action Workspace Design

**Date:** 2026-04-08
**Status:** Draft
**Scope:** Replace the broken MyWorkflows page and NewSubmissionDialog with a proper blueprint catalogue and full-page action workspace

---

## Problem Statement

The current "New Submissions" experience has two critical issues:

1. **Broken blueprint fetch:** The `NewSubmissionDialog` calls `GET /api/blueprints/{id}` which queries the draft store (`IBlueprintStore`). Published blueprints live in `IPublishedBlueprintStore` — a different store with different IDs. This causes a 404 on any node that didn't author the blueprint.

2. **Poor UX for form-heavy workflows:** A modal dialog is too cramped for complex multi-field action schemas. Blueprint introduction text and field-level help have nowhere to live. There's no concept of multi-step forms or field grouping.

## Solution Overview

Three new components:

1. **New Submissions listing page** — searchable/sortable blueprint catalogue grouped by register
2. **Action Workspace page** — full-page 2/3 form + 1/3 help panel, replacing the dialog
3. **Schema layout extensions** — `x-pages`, `x-sections`, `x-width` for wizard pages, field grouping, and layout control

---

## 1. New Submissions Listing Page

### Route

`/new-submissions` — replaces `/my-workflows` in the navigation sidebar.

### Data Flow

1. Fetch user's wallets via existing wallet service
2. Fetch subscribed registers via existing subscription service
3. For each register, call `GET /api/actions/{wallet}/{register}/blueprints` (existing endpoint)
4. Flatten results into a searchable list of startable blueprints
5. Group by register (default view)

### Layout

**Header:** Title ("New Submissions") + subtitle + connection status indicator

**Controls bar:**
- Search input — filters by blueprint title, description, register name
- Sort dropdown — name (A-Z), register, version (newest first)
- Card/list view toggle — `MudToggleIconButton`, persisted to localStorage (`sorcha:newSubmissions:viewMode`)

**Card view:**
- `MudGrid` with `xs="12" sm="6" md="4"` responsive columns
- Grouped by register with collapsible headers (register name + icon + blueprint count chip)
- Each card:
  - Blueprint title (`MudText Typo.h6`)
  - Description (body2, 2-line CSS clamp)
  - Version chip + starting action name (caption)
  - Full-width "Start" button → navigates to `/new-submission/{registerId}/{blueprintId}`

**Table view:**
- `MudTable` with columns: Blueprint | Register | Version | Starting Action | Actions
- Hover enabled, dense, elevation 2
- "Start" button in actions column

**Empty state:** `EmptyState` component — "No blueprints available. Subscribe to a register to see available workflows."

**Loading state:** Skeleton cards (3 per row) matching the card layout.

**Error handling:** `MudAlert` warning for registers that fail to load, with the rest still displayed.

### View Model

Uses existing `StartableBlueprintViewModel` with no changes:

```csharp
public record StartableBlueprintViewModel
{
    public string BlueprintId { get; init; }
    public string Title { get; init; }
    public string? Description { get; init; }
    public int Version { get; init; }
    public string RegisterId { get; init; }
    public string StartingActionTitle { get; init; }
    public string? StartingActionDescription { get; init; }
}
```

---

## 2. Action Workspace Page

### Route

`/new-submission/{RegisterId}/{BlueprintId}`

### Navigation & Data Flow

**On navigate from listing page:**
1. Listing page stores the published blueprint data + register context in `NavigationStateService` (scoped, in-memory dictionary keyed by correlation ID)
2. Navigate to workspace route
3. Workspace retrieves blueprint from `NavigationStateService`, then clears it

**On direct navigation (bookmark, refresh):**
1. `NavigationStateService` is empty
2. Resolve wallet address from `WalletPreferenceService` (user's default wallet)
3. Fallback: fetch from `GET /api/actions/{wallet}/{register}/blueprints` for the register, find the matching blueprint by ID
4. If wallet not available: prompt wallet selection first
5. If blueprint not found: show error with link back to `/new-submissions`

This eliminates the 404 bug entirely — the draft store (`IBlueprintStore`) is never queried for this flow.

### Layout — 2/3 + 1/3 Split

#### Left Panel (Form Area)

1. **Back link** — "← Back to New Submissions" navigating to `/new-submissions`
2. **Context bar** — blueprint name, version chip, "New Submission" badge
3. **Blueprint intro callout** — purple left border, displays `x-introduction` from the action schema or the blueprint description. Only shown for new submissions (not when this component is reused for pending actions later).
4. **Wallet selector** — inline compact display showing current wallet address with "change" link. Uses existing `WalletPreferenceService` for smart defaults.
5. **Wizard stepper** — only rendered if `x-pages` is defined. Horizontal step indicator showing page titles with completed/active/pending states.
6. **Form sections** — current page's `x-sections` rendered as visual groups. Each section has a title, optional description, and its fields rendered by `SorchaFormRenderer`.
7. **Bottom action bar** — fixed/sticky:
   - No wizard: "Cancel" + "Submit"
   - Wizard page 1: "Cancel" + "Next: {page title} →"
   - Wizard middle pages: "← Back" + "Next: {page title} →"
   - Wizard last page: "← Back" + "Submit"

#### Right Panel (Contextual Help)

Sticky panel with layered help content:

1. **Field-level help** (highest priority) — shown when a field is focused. Displays the field's JSON Schema `description`, `type`, constraints (`minLength`, `maxLength`, `minimum`, `maximum`, `pattern`, `enum`), and required status.
2. **Section-level help** — shown when a section is active but no specific field is focused. Displays the section's `description` or `help` text.
3. **Page-level overview** — always visible at the bottom. Shows completion status per section (checkmark for all valid, dot for in-progress, circle for untouched).
4. **Blueprint description fallback** — shown when nothing else applies (no x-pages, no field focused).

#### Responsive Behaviour

- Desktop (md+): 2/3 + 1/3 side-by-side
- Tablet/mobile (sm and below): help panel collapses to a toggleable drawer or accordion below the form

### Instance Creation Flow

1. User fills form across wizard pages (or single page)
2. Per-page validation on "Next" — only validates fields on the current page
3. On final "Submit":
   a. Create instance: `POST /api/instances` with `{ blueprintId, registerId }`
   b. Execute action 0: `POST /api/instances/{id}/actions/{actionId}/execute` with form payload
4. Loading overlay during submission (full form area, non-interactive)
5. On success: navigate to My Actions or show confirmation with instance reference
6. On error: stay on form, show `MudAlert` error, retain all form data

### State Management

- Form data held in component state across wizard pages — not lost on Next/Back
- No server-side draft saving (YAGNI for now)
- If user navigates away, data is lost (standard browser behaviour)

---

## 3. Schema Layout Extensions

### Overview

Three `x-` prefixed vendor extensions on JSON Schema, following the pattern established by `x-file` in Feature 085. JSON Schema validators ignore `x-` properties, so these are purely for UI rendering.

### `x-pages` — Wizard Pages

Defined on the action's root schema object. An array of page definitions that create a multi-step wizard.

```json
{
  "type": "object",
  "x-pages": [
    {
      "title": "Project Details",
      "description": "Core project and applicant information",
      "layout": "single-column",
      "x-sections": [
        { "title": "Project Identity", "fields": ["projectName", "applicationRef"] },
        { "title": "Applicant Address", "layout": "grid", "fields": ["addressLine1", "city", "postcode"] }
      ]
    },
    {
      "title": "Site Information",
      "layout": "two-column",
      "x-sections": [...]
    }
  ],
  "properties": { ... },
  "required": [ ... ]
}
```

**Page properties:**

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `title` | string | yes | Wizard step label |
| `description` | string | no | Shown in help panel as page overview |
| `layout` | string | no | `"single-column"` (default) or `"two-column"` — how sections are arranged on the page |
| `x-sections` | array | no | Sections within this page (see below) |

### `x-sections` — Field Grouping

Can appear either inside an `x-pages` page definition or directly on the action's root schema (for single-page grouping without a wizard).

```json
{
  "type": "object",
  "x-sections": [
    {
      "title": "Contact Details",
      "description": "Primary contact for this application",
      "layout": "horizontal",
      "fields": ["phone", "email"]
    },
    {
      "title": "Address",
      "help": "Must match your registered organisation address",
      "layout": "grid",
      "fields": ["addressLine1", "addressLine2", "city", "county", "postcode", "country"]
    }
  ],
  "properties": { ... }
}
```

**Section properties:**

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `title` | string | yes | Section heading |
| `description` | string | no | Subtitle below heading |
| `help` | string | no | Shown in help panel when section is active |
| `layout` | string | no | `"vertical"` (default), `"horizontal"` (side-by-side row), or `"grid"` (2-column auto-flow) |
| `fields` | string[] | yes | Property names from the schema's `properties` |

### `x-width` — Field Width Hints

Defined on individual properties to override the section layout for specific fields.

```json
{
  "properties": {
    "description": { "type": "string", "x-width": "full" },
    "city": { "type": "string", "x-width": "half" },
    "postcode": { "type": "string", "x-width": "third" }
  }
}
```

**Values:** `"full"` (12 cols), `"half"` (6 cols), `"third"` (4 cols). Maps directly to MudBlazor `MudItem` breakpoints.

### `x-introduction` — Blueprint/Action Introduction Text

Defined on the action's root schema. Displayed as a callout above the form when starting a new submission.

```json
{
  "type": "object",
  "x-introduction": "A construction permit application requires planning review, structural assessment, environmental check, and building control approval. Please provide complete and accurate information.",
  "properties": { ... }
}
```

Falls back to the blueprint's `description` if not defined.

### Rendering Modes

| Schema has | Wizard | Sections | Result |
|------------|--------|----------|--------|
| Neither | No | No | Flat form, fields in property order (today's behaviour) |
| `x-sections` only | No | Yes | Single page, fields grouped into visual sections |
| `x-pages` with `x-sections` | Yes | Yes | Multi-step wizard with sectioned pages |
| `x-pages` without `x-sections` | Yes | No | Wizard pages but fields within each page render flat |

### Catch-All Rule

Fields listed in `properties` but not referenced in any section or page render at the bottom of the last page (or the flat form). This prevents data loss if the schema and layout extensions drift out of sync.

---

## 4. NavigationStateService

A lightweight scoped service for passing transient objects between page navigations without URL serialisation or re-fetching.

```csharp
public class NavigationStateService
{
    private readonly Dictionary<string, object> _state = new();

    public void Set<T>(string key, T value) where T : notnull
        => _state[key] = value;

    public T? Get<T>(string key) where T : class
        => _state.Remove(key, out var value) ? value as T : null;
}
```

- Registered as scoped (lives for the circuit/tab lifetime in Blazor WASM)
- `Get` removes the entry on read (one-shot, prevents stale data)
- Used by listing page to store published blueprint data before navigation
- Used by workspace page to retrieve it on init

---

## 5. Components Summary

| Component | Location | Purpose |
|-----------|----------|---------|
| `NewSubmissions.razor` | Pages/ | Listing page at `/new-submissions` |
| `NewSubmissionWorkspace.razor` | Pages/ | Action workspace at `/new-submission/{RegisterId}/{BlueprintId}` |
| `ActionWorkspace.razor` | Components/Workflows/ | Reusable 2/3 + 1/3 workspace (form + help panel) |
| `WizardStepper.razor` | Components/Workflows/ | Horizontal step indicator |
| `FormSection.razor` | Components/Workflows/ | Section renderer (title + description + field group) |
| `FieldHelpPanel.razor` | Components/Workflows/ | Right-side contextual help panel |
| `NavigationStateService.cs` | Services/ | Transient navigation state passing |
| `BlueprintPageDefinition.cs` | Sorcha.Blueprint.Models | `x-pages` model |
| `BlueprintSectionDefinition.cs` | Sorcha.Blueprint.Models | `x-sections` model |
| `SchemaLayoutParser.cs` | Sorcha.Blueprint.Models | Parses x-pages/x-sections/x-width from JsonElement |

---

## 6. Files Modified

| File | Change |
|------|--------|
| `SorchaFormRenderer.razor` | Add x-pages/x-sections/x-width awareness, wizard mode, `OnFieldFocused` callback |
| `NavMenu.razor` | Update sidebar link from My Workflows → New Submissions |
| `MyWorkflows.razor` | Remove or redirect to `/new-submissions` |
| `NewSubmissionDialog.razor` | No longer used for new submissions (keep for now, remove later) |

---

## 7. Out of Scope

- Server-side form draft saving
- Adopting ActionWorkspace for Pending Actions (future work — component is designed for it)
- Auto-generating pages/sections from schema structure
- Conditional field visibility based on other field values
- Blueprint publishing changes (x-pages is parsed at render time, not publish time)

---

## 8. Testing Strategy

**Unit tests:**
- `SchemaLayoutParser` — parse x-pages, x-sections, x-width from various schema shapes
- `NavigationStateService` — set/get/clear, one-shot removal, type safety
- Wizard page validation — only current page fields validated

**Component tests (bUnit or manual):**
- Flat form rendering (no x-pages) — backwards compatibility
- Sectioned form rendering (x-sections only)
- Wizard form rendering (x-pages + x-sections)
- Layout modes (vertical, horizontal, grid, two-column)
- Field width hints (full, half, third)
- Help panel reactivity (field focus → field help, section focus → section help)
- Fallback: direct URL navigation fetches from published endpoint

**E2E tests (Playwright):**
- Navigate to new submissions, search/filter, start a blueprint
- Complete a wizard form across pages, submit successfully
- Verify instance creation and action execution
