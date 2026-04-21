# Data Model — AI Designer Unified Shell

**Branch**: `109-designer-shell-redesign`
**Date**: 2026-04-21

This feature introduces **no new persistent entities**. All stored data — `Blueprint`, `Action`, `Participant`, `ChatSession` — uses the existing shapes owned by the Blueprint Service and persisted via existing repositories. Nothing in this feature touches those schemas.

What IS new is the client-side **session state** held in memory for the duration of a designer page load. This document describes that state shape.

---

## 1. `DesignerContext` (client-side, in-memory, scoped-per-circuit)

**Lifetime**: One instance per browser window. Registered as `AddScoped<DesignerContext>()` in `Program.cs`. Created when the shell page initialises; disposed when the user navigates away from the designer.

**Persistence**: None. Blueprint edits survive via the explicit Save action (which calls the existing `IBlueprintApiService.SaveAsync`). Chat history survives via the existing server-side `IChatSessionStore`. Everything else is deliberately ephemeral — fresh per designer session.

### Fields

| Field | Type | Default | Mutated by | Read by |
|---|---|---|---|---|
| `Blueprint` | `Blueprint?` | `null` | `SetBlueprint`, `ApplyAiUpdate` | All three panes; toolbar |
| `Validation` | `ValidationResult?` | `null` | `ApplyAiUpdate`; Diagram-driven revalidate | Toolbar pill; Preview |
| `ChatSessionId` | `string?` | `null` | AI pane on session create/resume | AI pane on tab re-entry |
| `ActiveActionId` | `string?` | `null` | `ApplyAiUpdate` (when `IsManualCursor == false`); `SetActiveActionManual`; `FollowAi`; Diagram click | Preview pane |
| `IsManualCursor` | `bool` | `false` | `SetActiveActionManual` → `true`; `FollowAi` → `false` | `ApplyAiUpdate` guard |
| `IsDirty` | `bool` | `false` | `MarkDirty` → `true`; `MarkClean` → `false` | Toolbar Save button; `NavigationLock` |
| `_lastAiEditedActionId` (private) | `string?` | `null` | `ApplyAiUpdate` (always) | `FollowAi` (re-sync target) |

### Methods (public surface)

| Method | Signature | Behaviour |
|---|---|---|
| `SetBlueprint` | `void SetBlueprint(Blueprint bp)` | Sets `Blueprint`. Clears `ActiveActionId` and flips `IsManualCursor = false`. Does NOT set `IsDirty`. Fires `Changed`. |
| `ApplyAiUpdate` | `void ApplyAiUpdate(Blueprint bp, ValidationResult val, string? editedActionId)` | Sets `Blueprint` and `Validation`. Always updates `_lastAiEditedActionId = editedActionId`. Sets `ActiveActionId = editedActionId` ONLY when `IsManualCursor == false`. Sets `IsDirty = true`. Fires `Changed` exactly once. |
| `SetActiveActionManual` | `void SetActiveActionManual(string actionId)` | Sets `ActiveActionId = actionId` and `IsManualCursor = true`. Fires `Changed`. |
| `FollowAi` | `void FollowAi()` | Sets `IsManualCursor = false`. Sets `ActiveActionId = _lastAiEditedActionId` (may be null — that's fine, Preview empty-state handles it). Fires `Changed`. |
| `MarkDirty` | `void MarkDirty()` | Sets `IsDirty = true` if not already. Fires `Changed` only on transition. |
| `MarkClean` | `void MarkClean()` | Sets `IsDirty = false` if not already. Fires `Changed` only on transition. |
| `UpdateValidation` | `void UpdateValidation(ValidationResult val)` | For Diagram-pane-initiated revalidation. Sets `Validation`. Fires `Changed`. |

### Events

- `event Action? Changed` — fired exactly once per public mutation. Panes subscribe and invoke `StateHasChanged()` in response.

### Invariants

1. **Single-fire event**: a single mutation method call fires `Changed` at most once, even if the method sets multiple fields internally.
2. **Manual cursor sticky until FollowAi**: once `IsManualCursor` flips to `true`, no code path except `FollowAi()` flips it back to `false`.
3. **Tracking persists across override**: `_lastAiEditedActionId` is updated on every `ApplyAiUpdate` regardless of `IsManualCursor`, so `FollowAi()` always has a valid target to snap to (or `null` if the AI hasn't edited anything yet).
4. **Dirty ≠ unsaved-in-chat**: `IsDirty` tracks whether the client-side `Blueprint` differs from what was last persisted via Save. An AI edit marks dirty; Save marks clean; Load (via `SetBlueprint`) marks clean (it's just adopted fresh state, there's nothing new to save).

### State transition diagram (cursor)

```
                  ┌─────────────────────────┐
                  │  IsManualCursor = false │ ◄────────────┐
                  │  (Follow AI is active)  │              │
                  └────┬────────────────────┘              │
                       │                                    │
                       │ user clicks Prev / Next / jump    │ user clicks
                       │ dropdown / action node in Diagram │ "Follow AI"
                       ▼                                    │ toggle
                  ┌─────────────────────────┐              │
                  │  IsManualCursor = true  │──────────────┘
                  │  (manual override)      │
                  └─────────────────────────┘
```

---

## 2. Cursor view model (derived, not stored)

Consumed by `FormPreviewPane` to drive the pager:

| Field | Derivation |
|---|---|
| `TotalActions` | `Blueprint?.Actions.Count ?? 0` |
| `CurrentIndex` | Index of `ActiveActionId` in `Blueprint.Actions`; `-1` if not found |
| `CanGoPrev` | `CurrentIndex > 0` |
| `CanGoNext` | `CurrentIndex >= 0 && CurrentIndex < TotalActions - 1` |
| `DropdownItems` | `Blueprint.Actions.Select(a => (a.Id, $"{index+1} · {a.Title} — {ParticipantNameFor(a.SenderParticipantId)}"))` |

No field is stored — all derived on render. `PreviewPagerLogic` (the extracted pure helper) takes `(actions, currentId, direction)` and returns the next `ActionId`.

---

## 3. Auto-scroll state (inside `AutoScrollController`)

Held inside the controller; not part of `DesignerContext`.

| Field | Type | Default | Notes |
|---|---|---|---|
| `_autoScrollEnabled` | `bool` | `true` | Flips to `false` when the user scrolls up more than 40px from bottom; back to `true` when they scroll to within 40px of bottom. |
| `_lastScrollTop` | `double` | `0` | Distinguishes user-initiated scroll from the controller's own scroll-to-bottom call. |

Methods:

- `OnContentAppended()` — if `_autoScrollEnabled`, invoke JS interop `scrollTo(bottom)`. Update `_lastScrollTop`.
- `OnUserScroll(scrollTop, scrollHeight, clientHeight)` — compute distance from bottom; if user-initiated (delta vs `_lastScrollTop` doesn't match the last known controller-scroll) and distance > 40, `_autoScrollEnabled = false`. If distance ≤ 40, `_autoScrollEnabled = true`.

---

## 4. Tab route value object

Represented as an enum:

```csharp
public enum DesignerTab { Ai, Diagram, Preview }
```

Parsing lives in `TabRouteParser.Parse(string? queryValue)`:
- `null`, empty, whitespace → `Ai`
- Case-insensitive match on `"ai"`, `"diagram"`, `"preview"` → corresponding enum
- Any other value → `Ai` with a debug log "Unknown tab value '{queryValue}', falling back to ai"

Rendering to URL: `tab` query string omitted entirely when `Ai` (the default), explicit `?tab=diagram` or `?tab=preview` otherwise. Keeps the default URL tidy.

---

## 5. Relationships to existing entities

The `DesignerContext.Blueprint` field holds an instance of the existing `Sorcha.Blueprint.Models.Blueprint`. No new relationships, no new fields on Blueprint, no new fields on Action or Participant. This feature is purely a client-side presentation layer over the existing domain model.
