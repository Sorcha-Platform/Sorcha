# Data Model: New Submissions & Action Workspace

**Feature**: 091-new-submissions-workspace
**Date**: 2026-04-08

## New Entities

### BlueprintPageDefinition

Represents a single wizard page in a multi-step form. Parsed from `x-pages` array on the action schema.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Title | string | yes | Wizard step label (1-200 chars) |
| Description | string | no | Shown in help panel as page overview |
| Layout | string | no | `"single-column"` (default) or `"two-column"` |
| Sections | BlueprintSectionDefinition[] | no | Sections within this page |

**Validation rules**:
- Title is required, 1-200 characters
- Layout must be one of: `single-column`, `two-column` (case-insensitive)
- Sections array can be empty (fields render flat within the page)

### BlueprintSectionDefinition

Represents a visual grouping of form fields. Can appear inside a page or directly on the action schema (standalone sections without wizard).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Title | string | yes | Section heading (1-200 chars) |
| Description | string | no | Subtitle below heading |
| Help | string | no | Shown in help panel when section is active |
| Layout | string | no | `"vertical"` (default), `"horizontal"`, or `"grid"` |
| Fields | string[] | yes | Property names from schema properties |

**Validation rules**:
- Title is required, 1-200 characters
- Layout must be one of: `vertical`, `horizontal`, `grid` (case-insensitive)
- Fields array must not be empty
- Field names must reference existing properties in the parent schema (warning, not error — catch-all handles orphans)

### SchemaLayoutInfo

Parsed result from `SchemaLayoutParser` — the complete layout configuration for an action's form.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Pages | BlueprintPageDefinition[] | no | Wizard pages (null = no wizard) |
| Sections | BlueprintSectionDefinition[] | no | Standalone sections (null = no sections, unless inside pages) |
| Introduction | string | no | x-introduction text |
| FieldWidths | Dictionary<string, string> | no | Field name → width hint (full/half/third) |
| HasWizard | bool | computed | True if Pages is non-empty |
| HasSections | bool | computed | True if any sections exist (standalone or in pages) |

### NavigationState (Service)

In-memory transient state for passing data between page navigations.

| Field | Type | Description |
|-------|------|-------------|
| Key | string | Correlation key (e.g., `"blueprint:{registerId}:{blueprintId}"`) |
| Value | object | Stored value (typed via generic methods) |

**Lifecycle**: Set before navigation, consumed (removed) on read. Scoped to browser tab lifetime.

## Existing Entities (No Changes)

### StartableBlueprintViewModel

Used as-is for the listing page. No modifications needed.

| Field | Type | Description |
|-------|------|-------------|
| BlueprintId | string | Published blueprint identifier |
| Title | string | Blueprint display name |
| Description | string? | Blueprint description |
| Version | int | Blueprint version number |
| RegisterId | string | Register this blueprint is published to |
| StartingActionTitle | string | First action's title |
| StartingActionDescription | string? | First action's description |

### ActionExecuteRequest

Used as-is for submitting the starting action. No modifications needed.

| Field | Type | Description |
|-------|------|-------------|
| BlueprintId | string | Blueprint identifier |
| ActionId | string | Action index (always "0" for starting action) |
| InstanceId | string | Created instance ID |
| SenderWallet | string | User's signing wallet address |
| RegisterAddress | string | Target register ID |
| PayloadData | Dictionary<string, object> | Form field values |

## Schema Extension Format

### x-pages (on action schema root)

```json
{
  "type": "object",
  "x-pages": [
    {
      "title": "Page Title",
      "description": "Optional page description",
      "layout": "single-column",
      "x-sections": [
        {
          "title": "Section Title",
          "description": "Optional subtitle",
          "help": "Optional help panel text",
          "layout": "vertical",
          "fields": ["field1", "field2"]
        }
      ]
    }
  ]
}
```

### x-sections (on action schema root, standalone)

```json
{
  "type": "object",
  "x-sections": [
    {
      "title": "Section Title",
      "layout": "horizontal",
      "fields": ["field1", "field2"]
    }
  ]
}
```

### x-width (on individual properties)

```json
{
  "properties": {
    "fieldName": { "type": "string", "x-width": "half" }
  }
}
```

Values: `"full"` (12 cols), `"half"` (6 cols), `"third"` (4 cols)

### x-introduction (on action schema root)

```json
{
  "type": "object",
  "x-introduction": "Plain text introduction shown above the form."
}
```

## Rendering Mode Matrix

| x-pages | x-sections | Result |
|---------|------------|--------|
| absent | absent | Flat form, fields in property order |
| absent | present | Single page, fields grouped into sections |
| present | inside pages | Wizard with sectioned pages |
| present | absent in pages | Wizard with flat fields per page |
