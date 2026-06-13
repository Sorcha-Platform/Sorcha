# Phase 1 Data Model: PWA Citizen Workflow Inbox

**Feature**: 151-citizen-workflow-inbox | **Date**: 2026-06-13

A introduces **no persistent storage** and **no new server-side model**. The only new types are
client-side DTOs/view-models in the PWA that map the existing server responses. Source server
shape: `Sorcha.Blueprint.Service/Models/PendingActionSummary.cs`.

## Client DTO — `PendingActionItem`

Maps the subset of the server `PendingActionSummary` the inbox needs. Lives in
`Sorcha.Wallet.Pwa/Services/Actions/Models/`.

| Field | Type | Source field | Notes |
|-------|------|--------------|-------|
| `InstanceId` | `string` (Guid) | `InstanceId` | Used to navigate to `applications/{InstanceId}` |
| `ActionId` | `int` | `ActionId` | The action within the instance |
| `Title` | `string` | `ActionTitle` (fallback `BlueprintTitle`) | Display title (FR-002) |
| `WorkflowTitle` | `string` | `BlueprintTitle` | Which application/workflow it belongs to |
| `Reference` | `string?` | `InstanceReference` | Human-readable instance reference, if present |
| `Summary` | `string?` | `Summary` | Optional one-line context |
| `Urgency` | `enum { Normal, Warning, Urgent }` | `Urgency` (string) | Drives ordering + chip (FR-002) |
| `Deadline` | `DateTimeOffset?` | `Deadline` | Optional due date (FR-002) |
| `ReceivedAt` | `DateTimeOffset` | `ReceivedAt` | Tiebreak / secondary sort |
| `NavigationPath` | `string?` | `NavigationPath` | If server supplies an explicit path, prefer it (still made base-relative) |

**Validation / mapping rules**:
- `Urgency` parses case-insensitively; unknown → `Normal` (never throw on an unexpected value).
- `Title` falls back to `BlueprintTitle`, then to `"Action {ActionId}"` so a row is never blank.
- Fields not needed by the inbox (`SenderAddress`, `TransactionId`, `PrepopulatedPayload`,
  `DataSchema`, etc.) are ignored by the inbox client — the open-action flow
  (`IApplicationActionClient`) fetches what the form needs itself.

## Client DTO — `PendingActionsCount`

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `Count` | `int` | `/api/actions/pending/count` → `count` | Total outstanding (FR-006) |
| `UrgentCount` | `int` | `…count` → `urgentCount` | Always 0 today; carried but not relied upon |

## Client view-model — inbox page state

Held by `Actions.razor` (transient, not persisted):

- `IReadOnlyList<PendingActionItem> Items` — current list, **ordered**: Urgent → Warning → Normal,
  then `Deadline` ascending (nulls last), then `ReceivedAt` ascending.
- `bool IsLoading` / `bool HasLoadedOnce` — drives spinner vs. content vs. empty.
- `string? LastError` — non-blocking refresh-failure notice (FR-010); list retained.
- `PendingApplicationNotice? InReviewNotice` — existing Feature-124 notice for the banner (FR-009),
  via `IPendingApplicationClient` (reused, not redefined here).

## Ordering rule (single source of truth)

`Compare(a, b)`:
1. `Urgency` rank desc (Urgent=2, Warning=1, Normal=0)
2. `Deadline` asc, `null` last
3. `ReceivedAt` asc

This rule is implemented once (a comparer) and covered by a unit test (SC: "most pressing first").

## State transitions

There are no server state transitions owned by A. The inbox reflects server truth:
- An action **appears** when the instance is Active and the action's sender binds to the citizen.
- An action **disappears** after the citizen submits it (next list refresh) or once it is no longer
  the citizen's turn — observed via refresh / SignalR, not modelled locally (FR-005, SC-006).
