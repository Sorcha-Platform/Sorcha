# AI Designer Layout Redesign — Unified Shell With AI / Diagram / Preview Tabs

**Date:** 2026-04-21
**Status:** Design (awaiting user review before implementation plan)
**Scope:** UI-only redesign of the blueprint designer surface. No schema, validator, or service contract changes.
**Related:** Feature 063 (AI Blueprint Builder), GAP-011b (US6 regression E2E — closed by this work).

## Background and Problem

The blueprint designer currently exists as two separate pages:

- `/designer/chat` — AI chat designer with a split layout: chat on the left, a right-hand `BlueprintPreview` panel rendering a structured summary of the blueprint (participants, actions list), a draggable splitter between them.
- `/designer` — the Blazor Diagrams canvas for visual/"graphic" editing of actions, participants, and routes.

Moving between them is done via a "handoff" link button in the chat toolbar. There is no form-render preview anywhere in the designer flow — the closest thing is the `SorchaFormRenderer` used at citizen-submission time in `NewSubmissionWorkspace.razor`.

Observed issues in day-to-day use:

1. **Split layout cramps both halves.** The chat gets a narrow column, the preview gets a narrow column, neither is comfortable.
2. **Chat input drifts** when message count grows — the input area is inside the scroll region instead of pinned to the viewport bottom.
3. **No round-trip between AI editing and visual editing.** Switching pages means a full navigation, state is re-hydrated from storage, and the user loses their place.
4. **No way to preview what the citizen will actually see.** The structured summary in the right-hand panel shows blueprint shape but not the rendered form.

This redesign unifies the two pages into a single shell with three tabs (AI Designer / Diagram / Form Preview) sharing live state, fixes the chat layout, and adds form-render preview with an auto-cursor that follows the AI.

## Non-goals

The following are explicitly out of scope for this redesign:

- Changes to the AI tool set, conversation flow, system prompt, or `ChatOrchestrationService`. Feature 063 already shipped those.
- Changes to the schema library, validator, or any service API.
- Bidirectional "diff announce" system messages informing the AI that the user hand-edited the blueprint. The AI picks up manual edits implicitly via the blueprint payload on its next turn.
- Full-instance simulation in the Preview tab (submit Action 1, route to Action 2, etc.). Preview renders a single action at a time in read-only form.
- Real-time collaboration between multiple designers on one blueprint.
- Undo/redo. Neither pane has it today; not being added here.
- A fourth/fifth tab (JSON editor, version history, test run). The shell is designed to accept them but none ship in this work.
- Mobile-first layout. Designer is admin-only (roles: `Administrator, SystemAdmin, Designer`) and primarily a desktop tool. MudBlazor default breakpoints only.
- Persisting Diagram zoom/pan/selection across page reloads (URL or storage). In-memory only.

## User-Facing Outcomes

After this work:

- Opening `/designer/blueprint` lands on a full-width AI chat with the input pinned to the bottom of the viewport, independent of how many messages are above.
- A persistent toolbar along the top shows the blueprint title, connection status, validation pill, and Save / Load / Export controls.
- A tab strip underneath the toolbar switches between **AI Designer**, **Diagram**, and **Form Preview** without tearing down state.
- AI edits in the chat immediately update the blueprint seen by the Diagram and Preview panes.
- The Preview pane auto-cursors to whichever action the AI most recently edited. Prev / Next buttons and a jump dropdown let the designer take manual control; a "Follow AI" toggle resumes auto-cursor.
- Diagram edits (drag a node, rename an action) update the shared blueprint; on returning to the AI tab, the AI's next response sees the new state via its context payload.
- Save is a single action in the shared toolbar and applies regardless of which tab is active.
- Refreshing the page restores the selected tab via the `?tab=` query string.
- Legacy URLs (`/designer/chat`, `/designer/chat/{id}`, `/designer`) redirect to the new canonical route for one release cycle, then the redirect shims are deleted.

## Architecture

### New canonical route

```
/designer/blueprint/{BlueprintId?}?tab={ai|diagram|preview}
```

- `BlueprintId` omitted → new blueprint session.
- `tab` omitted → defaults to `ai`.
- `tab` values outside the allowed set → ignored, falls back to `ai`.
- Tab switches use `NavigateTo(..., replace: true)` so the browser back button returns to the previous *page*, not through every tab toggle.

### `DesignerContext` — scoped DI service

Single source of truth for the live designer session. Registered in `Sorcha.UI.Web.Client/Program.cs` as `AddScoped<DesignerContext>`. One instance per Blazor circuit.

| Field | Type | Written by | Read by |
|---|---|---|---|
| `Blueprint` | `BlueprintModel?` | AI hub updates; Diagram edits; Load | All three panes + toolbar |
| `Validation` | `ValidationResult?` | AI hub updates; Diagram on graph mutation | Toolbar pill + Preview "invalid" hint |
| `ChatSessionId` | `string?` | AI pane on session create / resume | AI pane re-hydrate on tab return |
| `ActiveActionId` | `string?` | AI tool-call handler (auto-cursor); Preview pager (manual) | Preview pane |
| `IsManualCursor` | `bool` | Preview pager (→ true); Follow-AI toggle (→ false) | AI pane's auto-cursor write path |
| `IsDirty` | `bool` | Any mutation that should enable Save | Toolbar Save button, `NavigationLock` |

Exposes `event Action? Changed`. Panes subscribe and call `StateHasChanged()` in response. Mutations are via methods (`SetBlueprint`, `ApplyAiUpdate`, `SetActiveActionManual`, `FollowAi`, `MarkDirty`, `MarkClean`) — never direct field writes. Each method fires `Changed` exactly once.

`ChatSession` history itself lives server-side in the existing `IChatSessionStore`; `DesignerContext` only needs the session ID to re-subscribe if the AI pane is disposed.

### File layout (new)

```
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/
├── DesignerBlueprint.razor         # shell page — routes, toolbar, tab strip, hosts panes
├── DesignerContext.cs               # scoped DI service (state + events)
├── DesignerToolbar.razor            # shared toolbar component
├── DesignerTabEnum.cs               # {Ai, Diagram, Preview}
└── Panes/
    ├── AiDesignerPane.razor         # full-width chat + pinned input
    ├── DiagramPane.razor            # Blazor Diagrams canvas, bound to context
    └── FormPreviewPane.razor        # new — pager + SorchaFormRenderer
```

### Supporting helpers (new — pure logic, testable without bUnit)

```
src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/
├── PreviewPagerLogic.cs             # next/prev/jump over action list
├── AutoScrollController.cs          # "auto-scroll unless user scrolled up" state machine
└── TabRouteParser.cs                # query string tab value → enum, with fallback
```

### Deleted / reduced

- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/BlueprintChat.razor` — reduced to a 20-line redirect shim. Content (chat logic, message wiring) moves into `AiDesignerPane.razor` minus the right-hand preview, minus the splitter, minus the page-level save/load/export buttons.
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer.razor` — reduced to a 20-line redirect shim. Content (Blazor Diagrams canvas, node types, drag-drop) moves into `DiagramPane.razor` minus its own toolbar, minus its own NavigationLock.
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/BlueprintPreview.razor` — deleted. No longer used.
- `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/ValidationBadge.razor` — kept if still used standalone elsewhere; otherwise move its logic into the shared toolbar pill.

Redirect shims live for one release cycle, then get deleted alongside the `/designer/chat` and `/designer` routes.

## Shell Chrome

### Layout

```
┌───────────────────────────────────────────────────────┐
│ App shell left nav (existing, unchanged)              │
├───────────────────────────────────────────────────────┤
│ Row 1 — Shared Toolbar (sticky, ~56px)                │
├───────────────────────────────────────────────────────┤
│ Row 2 — Tab Strip (sticky, ~40px)                     │
├───────────────────────────────────────────────────────┤
│ Row 3 — Active Pane (flex:1, fills remaining viewport)│
└───────────────────────────────────────────────────────┘
```

The whole grid lives inside the shell's main-content region. The app's left navbar remains unchanged and persistent to the left of it.

### Shared toolbar

Single `MudToolBar`, left-to-right:

- Blueprint title — editable inline (`MudTextField` with no border); "Untitled blueprint" placeholder when `Blueprint == null`.
- Dirty indicator — subtle "●" prefix on the title when `DesignerContext.IsDirty`.
- Connection chip — sourced from `IChatHubConnection.State`. Shown only when the AI pane has an active session (hidden on a fresh page with no chat started).
- `MudSpacer`.
- Load button — always enabled, opens the existing `LoadBlueprintDialog`.
- Save button — enabled iff `Blueprint != null && IsDirty`.
- Export split-button — JSON / YAML.
- Validation pill — `✓ Valid` (green) / `⚠ N issues` (amber) / `✗ N errors` (red). Clickable; opens the existing `ValidationPanel` as a `MudPopover` anchored under the pill.
- Messages counter — `Messages: N / 100`, visible only when AI session is active.

### Tab strip

`MudTabs` with three tabs in fixed order:

| Tab | Icon | Label | Disabled when |
|---|---|---|---|
| AI Designer | `SmartToy` | "AI" | never |
| Diagram | `AccountTree` | "Diagram" | `Blueprint == null` |
| Preview | `Preview` | "Preview" | `Blueprint == null` or `Blueprint.Actions.Count == 0` |

Configured with `KeepPanelsAlive="true"` so the SignalR connection and Diagram state survive tab switches.

### Navigation guards

- `NavigationLock` at the shell level, guarding `DesignerContext.IsDirty`. Replaces the per-page locks that existed on `Designer.razor`.
- Load dialog confirms discard first when `IsDirty`.

## The Three Panes

### AI Designer pane

Two-row grid filling the pane:

```
┌─────────────────────────────────────────┐
│ Messages (flex:1, scrolls internally)   │
│ • auto-scrolls on new content           │
│ • pauses auto-scroll when user scrolls  │
│   up; resumes when they return to bottom│
├─────────────────────────────────────────┤
│ Chat input (fixed height, NOT in scroll)│
│ [ Ask the AI...            ] [ Send ]   │
└─────────────────────────────────────────┘
```

- Full-width; max content width ~900px centred for readability on wide screens.
- Input row uses `position: sticky; bottom: 0` or a CSS grid row with `grid-template-rows: 1fr auto` — implementation detail, pick whichever plays cleanest with MudBlazor's `MudContainer`. Result: input does not scroll away and is always visible.
- Auto-scroll governed by `AutoScrollController` helper (pure logic, testable).

Wired to `DesignerContext`:

- Subscribes to `IChatHubConnection.OnBlueprintUpdated(Blueprint, ValidationResult)` and calls `Context.ApplyAiUpdate(bp, val, editedActionId)`.
- When the AI hub delivers a tool-call result that targets a specific action, the handler passes that action's ID so `ApplyAiUpdate` can set `ActiveActionId` (subject to `IsManualCursor == false`).

What stays from today's `BlueprintChat.razor`: `ChatPanel`, `ChatMessageItem`, session create/resume wiring, message-limit tracking, AI dialog flows (handoff to load/export dialogs), the welcome message rendering.

What's removed: the `<BlueprintPreview>` column, the draggable splitter (and its `@onpointerdown` handler, `OnSplitterDragEnd`, `OnSplitterDragStart`), the page-level Save / Load / Export buttons (now in shared toolbar), the handoff-to-Designer button (redundant with the Diagram tab), the validation panel in-pane (now a toolbar popover).

Expected line-count reduction: roughly 650 → 300 lines, almost all of which is chat-specific.

### Diagram pane

The existing Blazor Diagrams canvas, wired to the shared context instead of owning its own state.

Changes from today's `Designer.razor`:

- Local `CurrentBlueprint` field replaced with reads from `Context.Blueprint`. All writes route through context mutations followed by `Context.MarkDirty()`.
- The page's own toolbar (`Save`, `Export`, `Load` buttons at top of `Designer.razor`) is deleted — the shell toolbar replaces it.
- The page's own `NavigationLock` is removed — the shell's replaces it.
- LocalStorage-based draft persistence stays (useful resilience for "closed the tab mid-edit, reopen later"), but the key becomes `Context.Blueprint.Id` instead of page-local.
- Clicking a node writes `Context.SetActiveActionManual(nodeActionId)` so switching to Preview shows the clicked action.

What stays identical: the canvas itself, node types, drag-drop, route lines, context menu, serialization, layout. A user who's familiar with the existing `/designer` page should experience the Diagram tab as "the same thing, just inside tabs now."

Panel-alive on tab switch: node positions, zoom, pan, and selection survive a switch to Preview and back.

### Form Preview pane (NEW)

Two-row layout:

```
┌─────────────────────────────────────────────┐
│ Pager chrome (sticky top, ~48px)            │
│  [◀]  Action 3 of 7  [▼ jump]  [▶]  [🔗 Follow AI]
│  ↳ Sub-row: "As Assessor · Review Permit"   │
├─────────────────────────────────────────────┤
│ Form render (flex:1, scrolls)               │
│  ↳ SorchaFormRenderer bound to:             │
│    • Action.Schema                           │
│    • Action.FormLayout + x-sections          │
│    • x-review if present                     │
│    • x-file, x-persona, etc.                 │
└─────────────────────────────────────────────┘
```

**Cursor logic** — governed entirely by `DesignerContext`:

- `ActiveActionId` is the single source of truth for which action renders.
- `IsManualCursor` starts `false`. When the user clicks Prev / Next / jump, the pane calls `Context.SetActiveActionManual(id)` which flips `IsManualCursor = true`.
- Clicking the "Follow AI" toggle calls `Context.FollowAi()` which flips `IsManualCursor = false` and re-syncs `ActiveActionId` to whatever the AI most recently edited (tracked internally by `DesignerContext` as `_lastAiEditedActionId`).
- The AI pane's `ApplyAiUpdate` path only writes `ActiveActionId` when `IsManualCursor == false` — respecting the user's override.

**Pager chrome:**

- Prev / Next buttons disabled at list boundaries.
- "Action N of M" counter, non-interactive.
- Jump dropdown lists all actions by title + participant, e.g. "3 · Review Permit — Assessor".
- Keyboard shortcuts `[` / `]` for prev / next when the pane has focus. In scope.

**Form render** — uses the existing `SorchaFormRenderer` component, in a read-only "preview" mode:

- Fields interactable for visual testing (you can type, expand sections, see the x-rule behaviour).
- Submit button is rendered but disabled, with the tooltip "Preview — submission disabled". Keeping it visible (rather than hiding) preserves the citizen's eventual visual flow so the designer judges layout faithfully.
- All existing renderer extensions work unchanged: `x-sections`, `x-review`, `x-file`, `x-persona`, etc. What the designer sees is what citizens will see.

**Empty states:**

- `Blueprint == null` → "Start designing in the AI tab or load a blueprint."
- `Blueprint != null && Actions.Count == 0` → "This blueprint has no actions yet. Ask the AI to add one."
- `Blueprint != null && Actions.Count > 0 && ActiveActionId == null` → auto-select the first action.

## Shared Concerns

### Save

- Single Save action in the shared toolbar, enabled iff `Blueprint != null && IsDirty`.
- Payload is `Context.Blueprint` verbatim, regardless of which tab is active or which pane last mutated it.
- Uses the existing `IBlueprintApiService.SaveAsync` endpoint — no API changes.
- On success: `Context.MarkClean()`, toast "Blueprint saved", and — for new blueprints that had no ID — replace the URL via `NavigateTo($"/designer/blueprint/{newId}?tab={currentTab}", replace: true)` so a refresh keeps the blueprint.

### Load

- Toolbar Load button opens the existing `LoadBlueprintDialog` (same component used today by `BlueprintChat.razor`).
- On selection: if `IsDirty`, confirm discard. Then `NavigateTo($"/designer/blueprint/{id}?tab={currentTab}")`.
- The shell's `OnParametersSet` detects the ID change and resets `Context`, then fetches the blueprint and calls `Context.SetBlueprint(bp)`.

### Validation

- Single source: `Context.Validation`.
- Written by the AI pane's hub handler (via `ApplyAiUpdate`) and by the Diagram pane whenever it mutates the graph.
- Toolbar pill renders the summary. Click opens `MudPopover` hosting the existing `ValidationPanel` component.
- Invalid state does NOT block save — save persists drafts. Invalid blocks *publish*, which is a downstream action not in scope.

### Routing and migration

Redirect shims (to be deleted after one release cycle):

| Old | New |
|---|---|
| `/designer/chat` | `/designer/blueprint?tab=ai` |
| `/designer/chat/{id}` | `/designer/blueprint/{id}?tab=ai` |
| `/designer` | `/designer/blueprint?tab=diagram` |

Each shim is a 20-line `.razor` file whose `OnInitialized` calls `NavigationManager.NavigateTo(..., replace: true)`.

**Other call sites to update** (found via grep for `/designer/chat/` and `/designer?`):

- Left-nav entry in `NavMenu.razor` — point at `/designer/blueprint` (no tab param) so new users land on AI by default.
- `BlueprintChat.razor`'s handoff link (`GetDesignerHandoffUrl` helper) — removed entirely.
- Any blueprints-list page "Edit" action that builds a designer URL.

## Testing

### Unit tests — `DesignerContext`

New file `tests/Sorcha.UI.Core.Tests/Services/Designer/DesignerContextTests.cs`, approximately 15 xUnit tests:

- `SetBlueprint` writes `Blueprint`, fires `Changed`, does NOT touch `IsDirty`.
- `ApplyAiUpdate` writes `Blueprint` and `Validation`, fires `Changed` once.
- `ApplyAiUpdate` with an edited action ID and `IsManualCursor == false` writes `ActiveActionId`.
- `ApplyAiUpdate` with an edited action ID and `IsManualCursor == true` does NOT write `ActiveActionId` but DOES update `_lastAiEditedActionId`.
- `SetActiveActionManual` writes `ActiveActionId` and flips `IsManualCursor = true`.
- `FollowAi` flips `IsManualCursor = false` and re-syncs `ActiveActionId` to `_lastAiEditedActionId`.
- `MarkDirty` / `MarkClean` toggle `IsDirty`, fire `Changed`.
- Event firing: `Changed` fires exactly once per public mutation (no duplicate firings from cascaded writes).
- Initial state: all fields at their documented defaults.

### Unit tests — extracted helpers

- `tests/Sorcha.UI.Core.Tests/Services/Designer/PreviewPagerLogicTests.cs` — 5 tests: next / prev / jump happy paths, boundary clamping, unknown ID recovery.
- `tests/Sorcha.UI.Core.Tests/Services/Designer/AutoScrollControllerTests.cs` — 5 tests: fresh append auto-scrolls, scroll-up pauses, return-to-bottom resumes, disposed-after-scroll guard, rapid-append coalescing.
- `tests/Sorcha.UI.Core.Tests/Services/Designer/TabRouteParserTests.cs` — 5 tests: valid values, case-insensitive, unknown value → default, missing value → default, additional query params ignored.

### Component-level

MudBlazor + bUnit is flaky in this codebase; the panes stay as thin presentation layers over the helpers and context. No direct pane-rendering tests — the helpers and context are what's tested in isolation.

### End-to-end (Playwright / Docker)

New file `tests/Sorcha.UI.E2E.Tests/Docker/DesignerShellTests.cs`:

| Test | Asserts |
|---|---|
| `DesignerShell_LegacyChatRoute_Redirects` | `/designer/chat` → `/designer/blueprint?tab=ai` |
| `DesignerShell_LegacyChatWithIdRoute_Redirects` | `/designer/chat/{id}` → `/designer/blueprint/{id}?tab=ai` |
| `DesignerShell_LegacyDesignerRoute_Redirects` | `/designer` → `/designer/blueprint?tab=diagram` |
| `DesignerShell_TabSwitch_PreservesChatSession` | Send message in AI, switch to Diagram, back to AI — message history still visible, connection chip still green |
| `DesignerShell_InputPinnedAtBottom_AfterManyMessages` | **Closes GAP-011b.** Inject 50 synthetic messages via `page.evaluate`, assert input element's bounding box stays at the viewport bottom |
| `DesignerShell_ConsoleNoErrors_DuringTabSwitches` | Record console messages, assert no `error` severity during a 3-tab round-trip |
| `DesignerShell_PreviewPager_StepsThroughActions` | Load fixture blueprint with 3 actions; Next twice → renderer shows Action 3; jump dropdown changes selection |
| `DesignerShell_PreviewFollowAiToggle_AutoCursor` | Inject a synthetic blueprint update naming Action 2; assert Preview cursor moves; click Next (manual override); inject another update naming Action 3; assert cursor does NOT move; click Follow AI; assert cursor jumps to Action 3 |
| `DesignerShell_SaveFromDiagram_PersistsAiEdits` | Start in AI, inject synthetic edit; switch to Diagram; click Save; reload page; all three panes reflect saved state |
| `DesignerShell_DiagramEdit_VisibleInOtherPanes` | Edit action title in Diagram; switch to Preview; assert pager shows new title |

Each test uses the existing `DockerTestBase` + `LoginPage` page objects.

### Driving the AI leg in E2E — decided once here

Three options were considered:

- **Real Anthropic API key in Docker.** Most authentic, most flaky (network, rate limits, cost). Rejected.
- **Mock `IChatService` server-side via DI override.** Clean but requires a test-only Docker image or a config switch with a fake implementation. Ongoing maintenance cost.
- **Inject messages via Playwright `page.evaluate`.** Dispatches a synthetic SignalR `OnMessageReceived` / `OnBlueprintUpdated` event directly into the Blazor circuit's hub connection. Fast, deterministic, fully offline. **Picked.**

For tests that genuinely need real AI round-tripping (if any arise), isolate to a separate `[Category("Anthropic")]` suite gated on an env-var API key, not run in CI.

### Acceptance criteria

- Opening `/designer/blueprint` lands on AI tab full-width, input pinned to bottom.
- Describing a workflow → AI edits blueprint → Preview tab becomes enabled → clicking Preview shows the AI's latest-edited action.
- Pager Prev / Next / jump works; "Follow AI" toggle resumes auto-cursor.
- Hand-edit an action title in Diagram; switch to AI; AI's next response context includes the updated title (observable in dev-mode tool-call log).
- Save from any tab; reload `/designer/blueprint/{id}`; all three panes reflect saved state.
- 50+ messages in AI tab → input pinned at bottom, messages area scrolls independently, no page-level scroll.
- No console errors during a 3-tab round-trip.
- Legacy `/designer/chat` and `/designer` URLs redirect cleanly.

## Release and Rollout

Single PR unless the pane extractions naturally split the diff. No feature flag — this is a straightforward replacement. Deploy alongside the redirect shims. Delete the shims (and delete `BlueprintChat.razor` / `Designer.razor` originals if reduced to shims) in a follow-up PR after one release cycle confirms no stale external links.

No database migration. No API contract change. No schema change.

## Open Questions

None blocking the implementation plan. Two minor-polish items could be picked up during implementation:

- **Broader keyboard shortcuts.** Ctrl+S for Save and number keys (1/2/3) for tab switching are NOT in scope, but could be cheap to add if the input's focus-trap logic is already being touched during implementation.
- **Toolbar density on narrow viewports.** At MudBlazor's `sm` breakpoint, the toolbar becomes crowded. Either accept horizontal scroll on the toolbar or collapse Export into an overflow menu. Implementer's call during build.

## Appendix — Decisions made during brainstorming

| Decision | Rationale |
|---|---|
| Top tabs + shared state (Option A) over left rail (B) or linked pages (C) | Live cross-view sync is the whole point; top tabs are the most familiar pattern; existing left nav handles app-level navigation already. |
| Form Preview tab combines live cursor (option C) and manual navigation (option A) | User wants both — auto-follow for the "I just asked for X, can I see it" feedback loop, plus pager for reviewing the rest. |
| Diagram edits flow to context but do NOT explicitly announce to AI (option i) | Keeps the AI's context token budget lean; manual edits surface on the next turn via the blueprint payload anyway. |
| Approach 1 (new unified shell) over wrapping existing pages (2) or monolith (3) | Cleanest long-term shape; the `DesignerContext` is load-bearing and deserves to be a real service. Pane dispose-on-switch in option 2 is a real UX smell; option 3 produces a thousand-line file. |
