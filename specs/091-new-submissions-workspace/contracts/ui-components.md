# UI Component Contracts

**Feature**: 091-new-submissions-workspace
**Date**: 2026-04-08

No new backend API endpoints are required. All existing endpoints are used as-is. This document defines the UI component interfaces.

## Page Components

### NewSubmissions.razor

**Route**: `/new-submissions`
**Purpose**: Blueprint catalogue listing page

**Dependencies** (injected):
- `IBlueprintApiService` — fetch available blueprints per register
- `IRegisterSubscriptionService` — get user's subscribed registers
- `IWalletService` — get user's wallets
- `NavigationStateService` — store blueprint data before navigation
- `NavigationManager` — navigate to workspace
- `IJSRuntime` — localStorage for view mode preference

**State**:
- `List<RegisterBlueprintGroup> _registerGroups` — blueprints grouped by register
- `string _viewMode` — `"cards"` or `"table"` (persisted)
- `string _searchQuery` — filter text
- `string _sortBy` — `"name"`, `"register"`, `"version"`
- `bool _isLoading`

### NewSubmissionWorkspace.razor

**Route**: `/new-submission/{RegisterId}/{BlueprintId}`
**Purpose**: Hosts ActionWorkspace for starting a new blueprint instance

**Parameters**:
- `[Parameter] string RegisterId`
- `[Parameter] string BlueprintId`

**Dependencies** (injected):
- `NavigationStateService` — retrieve stored blueprint data
- `IBlueprintApiService` — fallback fetch on direct navigation
- `IWorkflowService` — create instance + execute action
- `IWalletService` — wallet list
- `WalletPreferenceService` — default wallet selection
- `NavigationManager` — back navigation

## Reusable Components

### ActionWorkspace.razor

**Purpose**: 2/3 form + 1/3 help panel layout. Reusable for both new submissions and (future) pending actions.

**Parameters**:
```csharp
[Parameter] public Blueprint Blueprint { get; set; }
[Parameter] public Models.Action Action { get; set; }
[Parameter] public string RegisterId { get; set; }
[Parameter] public string SelectedWallet { get; set; }
[Parameter] public EventCallback<string> SelectedWalletChanged { get; set; }
[Parameter] public EventCallback<FormSubmission> OnSubmit { get; set; }
[Parameter] public EventCallback OnCancel { get; set; }
[Parameter] public bool ShowIntroduction { get; set; } = true;
[Parameter] public RenderFragment? HeaderContent { get; set; }
```

**Internal state**:
- `SchemaLayoutInfo _layout` — parsed from action schema
- `int _currentPage` — wizard page index (0-based)
- `Dictionary<string, object?> _formData` — accumulated across pages
- `string? _focusedField` — for help panel
- `string? _activeSection` — for help panel

### WizardStepper.razor

**Purpose**: Horizontal step indicator for wizard pages

**Parameters**:
```csharp
[Parameter] public List<BlueprintPageDefinition> Pages { get; set; }
[Parameter] public int CurrentPage { get; set; }
[Parameter] public EventCallback<int> OnPageSelected { get; set; }
```

### FormSection.razor

**Purpose**: Renders a section group (title, description, fields with layout)

**Parameters**:
```csharp
[Parameter] public BlueprintSectionDefinition Section { get; set; }
[Parameter] public JsonElement Schema { get; set; }
[Parameter] public Dictionary<string, object?> FormData { get; set; }
[Parameter] public Dictionary<string, string>? FieldWidths { get; set; }
[Parameter] public EventCallback<string> OnFieldFocused { get; set; }
```

### FieldHelpPanel.razor

**Purpose**: Right-side contextual help panel

**Parameters**:
```csharp
[Parameter] public string? FocusedField { get; set; }
[Parameter] public string? ActiveSection { get; set; }
[Parameter] public JsonElement? Schema { get; set; }
[Parameter] public SchemaLayoutInfo? Layout { get; set; }
[Parameter] public string? BlueprintDescription { get; set; }
```

## Service

### NavigationStateService.cs

**Registration**: Scoped (Blazor WASM tab lifetime)

**Interface**:
```csharp
public class NavigationStateService
{
    public void Set<T>(string key, T value) where T : notnull;
    public T? Get<T>(string key) where T : class;  // Removes on read
}
```

## Model Classes

### SchemaLayoutParser.cs

**Location**: `Sorcha.Blueprint.Models`

**Static methods**:
```csharp
public static class SchemaLayoutParser
{
    public static SchemaLayoutInfo Parse(JsonElement actionSchema);
    public static bool TryGetFieldWidth(JsonElement propertySchema, out string? width);
}
```

## Existing API Endpoints Used (No Changes)

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/api/actions/{wallet}/{register}/blueprints` | List available blueprints for register |
| POST | `/api/instances` | Create new workflow instance |
| POST | `/api/instances/{id}/actions/{actionId}/execute` | Execute starting action |
