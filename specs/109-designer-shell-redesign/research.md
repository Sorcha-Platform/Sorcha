# Research — AI Designer Unified Shell

**Branch**: `109-designer-shell-redesign`
**Date**: 2026-04-21
**Purpose**: Resolve open implementation questions before task generation. No `[NEEDS CLARIFICATION]` markers remained from the spec (the brainstorming session that preceded `/speckit.specify` settled every shape decision); this document collects the technology-integration patterns we lean on so the tasks can be cleanly generated.

---

## R1. MudBlazor `MudTabs` with persistent panels

**Decision**: Use `MudTabs` with `KeepPanelsAlive="true"` to host the three tab panes, giving each a `Value` matched to the `DesignerTab` enum.

**Rationale**: `KeepPanelsAlive` is MudBlazor's documented mechanism for retaining panel state (including child-component lifecycle) across tab changes. Without it, switching tabs disposes and re-creates the active panel — which would tear down the SignalR circuit on the AI pane and reset the Blazor Diagrams canvas on the Diagram pane every switch. The attribute exists precisely for multi-view-single-session scenarios like this one.

**Alternatives considered**:
- **CSS `display:none` toggled panels with no `MudTabs`**: Would also preserve state but loses the built-in keyboard navigation, ARIA roles, and active-state styling MudBlazor provides.
- **Separate pages with state hydration**: Rejected in the brainstorm (Approach 2). Causes perceptible dispose/reinit jank on every tab switch.

**Gotcha to handle in implementation**: When a tab is disabled (Diagram/Preview disabled when `Blueprint == null`) and the URL query string requests that tab, the tab strip rejects the selection silently. The shell must reconcile by falling back to `ai` and replacing the URL with `tab=ai`.

---

## R2. Pinning the chat input at the viewport bottom

**Decision**: CSS Grid two-row layout within the AI pane — `grid-template-rows: 1fr auto` — with the messages area as row 1 (scrollable internally via `overflow-y: auto`) and the input as row 2 (natural height, no scroll). The grid itself fills the tab-panel's height via `height: 100%`.

**Rationale**: CSS Grid gives deterministic row sizing with no JavaScript, and the `1fr auto` pattern is the idiomatic "fill the rest, let me set my own height at the bottom" arrangement. It composes cleanly inside MudBlazor containers without fighting `MudContainer`'s internal padding. `position: sticky; bottom: 0` was considered but is fragile when the containing scroll context has its own padding or overflow rules — exactly the situation inside `MudTabPanel`.

**Alternatives considered**:
- **Flexbox column with flex-grow on messages**: Works but requires `min-height: 0` on the flex item to enable overflow, which is a recurring source of "scroll doesn't appear" bugs for implementers unfamiliar with that flex quirk. Grid avoids it.
- **JavaScript-driven viewport calculation**: Overkill. CSS handles this natively.

**Testing consideration**: E2E test `DesignerShell_InputPinnedAtBottom_AfterManyMessages` uses Playwright's `locator.boundingBox()` on the input element after injecting 50 synthetic messages and asserts the input's `y + height` equals the viewport's `innerHeight` (within a 2px tolerance for sub-pixel rounding).

---

## R3. Driving the AI leg in E2E tests

**Decision**: `page.evaluate` dispatches synthetic SignalR events directly into the Blazor circuit's hub connection. No real Anthropic API round-trip.

**Rationale**: The Blazor WASM client's hub connection is a `HubConnection` instance accessible from JavaScript via `DotNet.invokeMethodAsync` helpers that Blazor exposes when `[JSInvokable]` methods are registered. For tests, the test fixture registers a one-off test-only `[JSInvokable]` method on the AI pane that accepts a synthetic `BlueprintUpdated` event shape and forwards it to the same handler path that the real SignalR hub would trigger. Playwright then calls this via `page.evaluate` with canned payloads. Fully deterministic, zero network, zero cost, zero token usage.

**Alternatives considered**:
- **Real Anthropic API key in Docker**: Rejected — flaky (network, rate limits, model drift, cost). The spec's existing `AI Blueprint Builder` tests already avoid this.
- **Server-side `IChatService` mock via DI override in the test image**: Requires a separate Docker build profile and ongoing config-switch maintenance. Ugly for what should be a test-fixture concern.

**Implementation note**: The test-only `[JSInvokable]` is conditionally registered under `#if DEBUG || E2E_TEST_HOOKS` to keep it out of release bundles.

---

## R4. Scoped DI lifetime in Blazor WASM

**Decision**: Register `DesignerContext` as `AddScoped<DesignerContext>()` in `Program.cs`.

**Rationale**: In Blazor WASM, `Scoped` is effectively singleton-per-app-instance (one per tab/browser window), which is exactly the right lifetime for "the blueprint the user is currently designing in this window". Multiple browser tabs get independent contexts, as expected. Singleton would incorrectly share state across multiple browser windows of the same user; Transient would give each component its own copy, breaking the shared-state premise.

**Alternatives considered**:
- **CascadingParameter<DesignerContext>**: Would work, but awkward — the shell page would have to construct and dispose the context in `OnInitializedAsync`. Scoped DI handles lifetime cleanly.
- **Static singleton**: Shares state across windows, rejected.

---

## R5. Preserving Blazor Diagrams state across tab switches

**Decision**: With `KeepPanelsAlive="true"` on `MudTabs`, the Blazor Diagrams canvas keeps its in-memory node positions, selection, and zoom/pan state naturally — no extra work needed. The `DiagramPane` component's `OnInitializedAsync` runs once and the canvas remains in the render tree during off-tab periods.

**Rationale**: Verified by inspection of `Designer.razor` — the canvas state is held in the component's in-memory field `CurrentBlueprint` plus the Blazor Diagrams library's internal model. Neither is reset unless the component is disposed. `KeepPanelsAlive` prevents disposal.

**Alternatives considered**:
- **Serialize canvas layout to DesignerContext on tab-leave, rehydrate on tab-enter**: Unnecessary given the above. Would add complexity with no benefit.

---

## R6. Form Preview rendering without production submission behaviour

**Decision**: Reuse `SorchaFormRenderer` in a new "preview mode" controlled by a boolean parameter `PreviewMode`. When `true`, the renderer:
- Renders fields and sections identically to production.
- Runs all existing schema extensions (`x-sections`, `x-review`, `x-file`, `x-persona`, conditional rules, etc.).
- Disables the submit button and adds a tooltip.
- Suppresses any "submit" click handler so accidental clicks are no-ops.
- Leaves field input enabled so the designer can type to see validation feedback.

**Rationale**: Adding one parameter to the existing renderer keeps the preview visually faithful by construction — there's no risk of preview diverging from production because they go through the same code path. The alternative (a separate preview renderer) would constantly drift.

**Alternatives considered**:
- **Separate PreviewFormRenderer component**: Rejected for drift risk.
- **Preview renders read-only static snapshots**: Loses the "see the conditional logic fire when I fill a field" feedback that's the whole point of having Preview.

**Implementation note**: `SorchaFormRenderer` already takes several parameters (schema, layout, persona binding, etc.). Adding `[Parameter] public bool PreviewMode { get; set; } = false;` is additive, opt-in, and doesn't change any existing caller.

---

## R7. URL / tab synchronization

**Decision**: The shell page subscribes to `NavigationManager.LocationChanged`, parses the query string on every location change, and calls `MudTabs.ActivatePanel` when the parsed tab differs from the current one. Conversely, when the user clicks a tab, the shell calls `NavigateTo(..., replace: true)` with the new query string.

**Rationale**: This is the idiomatic Blazor pattern for two-way URL-to-state binding. `replace: true` prevents each tab click from growing the browser history stack; the back button then returns to the previous *page*, not through every tab toggle.

**Alternatives considered**:
- **Tab as path segment `/designer/blueprint/{id}/{tab}`**: More REST-ful but breaks when `id` is optional (awkward empty-segment URLs). Query string is the right shape for a view-mode selector.
- **LocalStorage-backed last-active-tab**: Adds state the URL can't see, breaks sharing of deep links. Rejected.

---

## R8. Legacy-route redirect mechanism

**Decision**: Redirect shim files are tiny `.razor` pages at the old routes whose sole content is an `OnInitialized` method calling `NavigationManager.NavigateTo(newUrl, replace: true, forceLoad: false)`. Blazor handles the client-side route swap without a full-page reload.

**Rationale**: `forceLoad: false` keeps the WASM app loaded (no re-download of assemblies, no cold start) and performs a client-side navigation. `replace: true` prevents the legacy URL from sitting in browser history behind the new one. The result is that an old bookmark silently becomes a new-shell visit with no visible interruption.

**Alternatives considered**:
- **Server-side HTTP redirect**: Would cause a full-page reload and WASM re-download. Slower and visibly flashier.
- **JavaScript redirect in `_Host.cshtml`**: Bypasses Blazor routing and causes the WASM app to start cold. Rejected.

---

## R9. MudBlazor + bUnit for pane tests

**Decision**: Do NOT write bUnit tests that render full panes. Extract load-bearing logic into `PreviewPagerLogic`, `AutoScrollController`, and `TabRouteParser` (all plain C# classes) and unit-test those. Leave pane rendering covered by E2E only.

**Rationale**: This codebase has a history of flaky bUnit + MudBlazor interactions (MudBlazor components bring in JS interop that bUnit's test host doesn't fully mock; `MudTabs` in particular has rendered inconsistently across bUnit versions). The pragmatic answer — already used in Feature 092 (Consumer Persona) — is to keep `.razor` files thin and push testable logic into plain classes.

**Alternatives considered**:
- **bUnit with JS interop mocking**: High maintenance cost for the protection delivered. Rejected.
- **No tests below E2E level**: Loses fast-feedback unit coverage on things like pager boundaries. Rejected.

---

## R10. Coexistence with the app-shell left navbar

**Decision**: No changes to the app-shell layout. The new `DesignerBlueprint` page uses the same default layout the current `BlueprintChat` and `Designer` pages use (`MainLayout` with left navbar), and the new toolbar + tab strip live *inside* that layout's main-content region. Verified by inspection — today's designer pages render inside `MainLayout` already.

**Rationale**: Zero shell-layout changes means zero risk of destabilising other pages. The user's explicit guidance ("remember we still have our navbar on the left") made this the assumed default.

**Alternatives considered**: None reasonable.

---

## Summary of resolved positions

| Question | Resolution |
|---|---|
| Preserve panel state across tab switches | `MudTabs KeepPanelsAlive="true"` |
| Pin chat input to viewport bottom | CSS Grid `1fr auto` two-row layout |
| Drive AI leg in E2E without real Anthropic | `page.evaluate` → synthetic SignalR event via `[JSInvokable]` hook |
| `DesignerContext` lifetime | `AddScoped` in WASM client |
| Diagram state across tab switches | Inherent from `KeepPanelsAlive`, no extra work |
| Preview submit-disabled mode | `SorchaFormRenderer` gains `PreviewMode` parameter |
| URL ↔ active-tab sync | `LocationChanged` subscription + `NavigateTo(replace: true)` |
| Legacy URL redirects | Client-side `.razor` shims using `NavigateTo(forceLoad: false)` |
| Pane component testing | Test extracted helpers; E2E for panes |
| Left-navbar coexistence | Unchanged — new shell lives inside existing `MainLayout` |
