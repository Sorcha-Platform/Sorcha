# Data Model: UI Polish & Blueprint Designer

**Feature**: 046-ui-polish-designer
**Date**: 2026-03-02

## Entities

### No New Entities Required

This feature modifies UI components and wiring — no new domain entities, database tables, or storage schemas are introduced. All data flows use existing models:

| Existing Entity | Used By | Location |
|----------------|---------|----------|
| `Blueprint` | Designer save/load | `Sorcha.Blueprint.Models.Blueprint` |
| `ActivityEventDto` | Notification system | `Sorcha.UI.Core.Models.ActivityEventDto` |
| `WalletDto` | Dashboard wallet check | `Sorcha.UI.Core.Models.WalletDto` |
| `UserPreferencesDto` | Default wallet preference | `Sorcha.UI.Core.Models.UserPreferencesDto` |
| `TranslationBundle` (JSON) | Localization | `wwwroot/i18n/{lang}.json` |

## Interface Changes

### IBlueprintApiService — New Method

```
UpdateBlueprintAsync(id: string, blueprint: object) → BlueprintListItemViewModel?
```

Wraps `PUT /api/blueprints/{id}` — the backend endpoint already exists but has no client wrapper.

### IActivityLogService — New Method (Future)

```
No changes in this feature. EventsHubConnection handles real-time push.
```

The `POST /api/events` endpoint is service-to-service. UI receives events via SignalR, not by creating them directly.

### New: IEventsHubConnection

```
ConnectAsync() → Task
DisconnectAsync() → Task
OnEventReceived: event Action<ActivityEventDto>
OnUnreadCountUpdated: event Action<int>
```

Follows existing pattern of `ActionsHubConnection` and `RegisterHubConnection`.

## State Changes

### Designer.razor — New State Fields

| Field | Type | Purpose |
|-------|------|---------|
| `_persistedBlueprintId` | `string?` | Tracks whether current blueprint exists on the API (null = new, non-null = update on save) |
| `_hasUnsavedChanges` | `bool` | Enables unsaved-changes prompt via NavigationLock |
| `BlueprintId` | `string?` (query param) | Allows opening designer with `?id={id}` to auto-load |

### Home.razor — Changed Dependencies

| Before | After |
|--------|-------|
| `IUserPreferencesService` | `IWalletPreferenceService` + `IWalletApiService` |
| `_hasDefaultWallet: bool` | `_defaultWalletAddress: string?` (richer state) |

### MainLayout.razor — Changed Dependencies

| Before | After |
|--------|-------|
| (none) | `IEventsHubConnection` for real-time activity badge updates |
| (none) | `ILocalizationService` for translated navigation text |
