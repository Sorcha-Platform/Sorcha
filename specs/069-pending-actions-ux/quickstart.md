# Quickstart: 069-pending-actions-ux

## What This Feature Does

Transforms the "My Pending Actions" page from developer-oriented (showing blueprint IDs and instance UUIDs) to user-oriented (showing workflow names, action titles, and human-readable application references). Also fixes the empty Execute Action form dialog.

## Key Changes

### 1. Blueprint Model — Add InstanceReference Template
- **File**: `src/Common/Sorcha.Blueprint.Models/Blueprint.cs`
- **What**: Add `InstanceReference` property (new `InstanceReferenceTemplate` class)
- **Why**: Blueprint authors define how to generate human-readable instance references

### 2. Blueprint Engine — Generate References
- **File**: `src/Core/Sorcha.Blueprint.Engine/` (action execution path)
- **What**: After first action completes, evaluate the reference template against `AccumulatedData` and write to `Instance.Metadata["instanceReference"]`
- **Why**: Each workflow instance gets a unique, searchable identifier like "CP-RIV-14W-a7k3"

### 3. Pending Actions Endpoint — Enrich Response
- **File**: `src/Services/Sorcha.Blueprint.Service/Endpoints/ActionEndpoints.cs`
- **File**: `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs`
- **What**: Look up blueprint via `ActionResolverService` to populate real `ActionTitle` (instead of "Action {id}") and include `InstanceReference` from instance metadata
- **Why**: Cards need real data to display

### 4. UI — Pending Actions Page Overhaul
- **File**: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor`
- **What**: Redesign cards to show blueprint title, action title, instance reference. Add card/table toggle with localStorage persistence. Group by blueprint type.
- **Why**: Core UX improvement

### 5. UI — Fix Execute Action Form
- **File**: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` (TakeAction method)
- **File**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Workflows/ActionForm.razor`
- **What**: Fetch blueprint via existing `GET /api/blueprints/{id}` when TAKE ACTION is clicked, extract the action's `DataSchemas`, pass to the form renderer
- **Why**: Currently the form dialog is empty because `DataSchema` is never populated

### 6. Documentation — Blueprint Schema, CLAUDE.md, Skills
- **Files**: `docs/reference/blueprint-schema.md` (or equivalent), `CLAUDE.md`, `.specify/skills/blueprint-builder`
- **What**: Document the `instanceReference` property with schema, transforms, examples. Update CLAUDE.md blueprint creation standards. Update blueprint-builder skill so AI assistants auto-suggest references.
- **Why**: Blueprint authors (human and AI) need to know this exists and how to configure it

## Implementation Order

1. **P1a**: Add `InstanceReferenceTemplate` to Blueprint model + reference generation logic + tests
2. **P1b**: Enrich pending actions endpoint with action titles + instance reference + tests
3. **P2a**: Fix Execute Action form (schema fetch on-demand) + tests
4. **P2b**: Documentation — blueprint schema docs, CLAUDE.md, blueprint-builder skill
5. **P3**: UI overhaul — card redesign, card/table toggle, grouping, localStorage preference

## Testing

- Unit tests for reference generation (transforms, edge cases, fallback)
- Unit tests for pending action enrichment (title lookup, missing blueprint graceful fallback)
- Integration test: create instance → submit Action 1 → verify reference in metadata
- E2E test: login → view pending actions → verify card content → click TAKE ACTION → verify form fields render
