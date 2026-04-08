# Quickstart: New Submissions & Action Workspace

**Feature**: 091-new-submissions-workspace
**Branch**: `091-new-submissions-workspace`

## What This Feature Does

Replaces the broken "My Workflows" page and dialog-based new submission flow with:
1. A searchable blueprint catalogue at `/new-submissions`
2. A full-page action workspace at `/new-submission/{registerId}/{blueprintId}`
3. Schema layout extensions (`x-pages`, `x-sections`, `x-width`, `x-introduction`) for wizard pages and field grouping

## Prerequisites

- Docker Desktop running with `docker-compose up -d`
- At least one register with published blueprints
- A user account with wallet access and register subscriptions

## Key Files

### New Files

| File | Purpose |
|------|---------|
| `Sorcha.UI.Web.Client/Pages/NewSubmissions.razor` | Blueprint catalogue listing page |
| `Sorcha.UI.Web.Client/Pages/NewSubmissionWorkspace.razor` | Action workspace host page |
| `Sorcha.UI.Core/Components/Workflows/ActionWorkspace.razor` | Reusable 2/3 + 1/3 form + help workspace |
| `Sorcha.UI.Core/Components/Workflows/WizardStepper.razor` | Wizard step indicator |
| `Sorcha.UI.Core/Components/Workflows/FormSection.razor` | Section renderer with layout modes |
| `Sorcha.UI.Core/Components/Workflows/FieldHelpPanel.razor` | Contextual help panel |
| `Sorcha.UI.Core/Services/NavigationStateService.cs` | Page-to-page data passing |
| `Sorcha.Blueprint.Models/BlueprintPageDefinition.cs` | x-pages model |
| `Sorcha.Blueprint.Models/BlueprintSectionDefinition.cs` | x-sections model |
| `Sorcha.Blueprint.Models/SchemaLayoutInfo.cs` | Parsed layout result |
| `Sorcha.Blueprint.Models/SchemaLayoutParser.cs` | x-extension parser |

### Modified Files

| File | Change |
|------|--------|
| `SorchaFormRenderer.razor` | Add `OnFieldFocused` callback, section/page-aware rendering |
| `MainLayout.razor` | Update nav link from My Workflows → New Submissions |

## Testing

```bash
# Run all UI core tests
dotnet test tests/Sorcha.UI.Core.Tests

# Run Blueprint.Models tests (parser)
dotnet test tests/Sorcha.Blueprint.Models.Tests

# Run E2E tests
dotnet test tests/Sorcha.UI.E2E.Tests
```

## Usage Flow

### Starting a New Submission

1. Navigate to **New Submissions** in the sidebar
2. Browse/search blueprints grouped by register
3. Click **Start** on a blueprint card
4. Fill in the form (wizard pages if `x-pages` defined, flat form otherwise)
5. Use the right-side help panel for field guidance
6. Click **Submit** to create a new workflow instance

### Adding Layout Extensions to a Blueprint

Add `x-pages` and/or `x-sections` to an action's schema in the blueprint JSON:

```json
{
  "actions": [{
    "title": "Submit Application",
    "dataSchemas": [{
      "type": "object",
      "x-introduction": "Please provide complete project details.",
      "x-pages": [
        {
          "title": "Project Details",
          "x-sections": [
            {
              "title": "Applicant",
              "layout": "horizontal",
              "fields": ["firstName", "lastName"]
            },
            {
              "title": "Address",
              "layout": "grid",
              "fields": ["addressLine1", "city", "postcode"]
            }
          ]
        },
        {
          "title": "Documents",
          "x-sections": [
            {
              "title": "Required Files",
              "fields": ["sitePlan", "drawings"]
            }
          ]
        }
      ],
      "properties": {
        "firstName": { "type": "string", "description": "Applicant first name" },
        "lastName": { "type": "string", "description": "Applicant last name" },
        "addressLine1": { "type": "string", "x-width": "full" },
        "city": { "type": "string", "x-width": "half" },
        "postcode": { "type": "string", "x-width": "half" },
        "sitePlan": { "type": "string", "format": "file-reference" },
        "drawings": { "type": "string", "format": "file-reference" }
      },
      "required": ["firstName", "lastName", "addressLine1", "city", "postcode"]
    }]
  }]
}
```

### Layout Modes

| Mode | Where | Effect |
|------|-------|--------|
| `x-pages[].layout: "single-column"` | Page | Sections stack vertically (default) |
| `x-pages[].layout: "two-column"` | Page | Sections flow into two columns |
| `x-sections[].layout: "vertical"` | Section | Fields stack top-to-bottom (default) |
| `x-sections[].layout: "horizontal"` | Section | Fields side-by-side in a row |
| `x-sections[].layout: "grid"` | Section | Fields in 2-column auto-flow grid |
| `x-width: "full"` | Field | 12 columns (full width) |
| `x-width: "half"` | Field | 6 columns (half width) |
| `x-width: "third"` | Field | 4 columns (one third width) |
