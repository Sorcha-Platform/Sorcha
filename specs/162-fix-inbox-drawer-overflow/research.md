# Phase 0 Research: Fix Inbox/Bell Drawer Overflowing Phone Width

All NEEDS CLARIFICATION items from Technical Context are resolved below. Each decision is grounded in the current repository state, verified by reading the actual files (paths cited).

---

## R1 — Root cause: why the existing scoped rule never applies

**Decision**: Confirmed — the rule is dropped by Blazor CSS isolation because the drawer is rendered out-of-tree.

**Evidence**:
- `Sorcha.UI.Components.User/Components/Inbox/InboxPanel.razor.css` contains `::deep .mud-drawer { width: min(420px, 100vw) !important; max-width: 100vw; }`.
- `InboxPanel.razor:26` declares `<MudDrawer ... Variant="@DrawerVariant.Temporary" Width="420px" data-testid="inbox-drawer">`.
- A temporary MudDrawer renders as an overlay appended outside the component's DOM subtree, so Blazor never stamps the component's `b-{hash}` scope attribute onto the `.mud-drawer` element. `::deep` only re-targets descendants of a scoped element; with no scoped ancestor at runtime, the compiled selector matches nothing and the drawer falls back to `Width="420px"`.

**Rationale**: This is the documented failure mode for styling MudBlazor overlay/portal elements (drawers, dialogs, menus, popovers) from component-isolated CSS. The fix is to express the rule in a **global, non-isolated** stylesheet.

**Alternatives considered**:
- *Keep `::deep` and add `!important`* — already `!important`; the problem is selector non-match, not specificity. Rejected.
- *Inline `Style="width:min(420px,100vw)"` on `<MudDrawer>`* — MudDrawer renders `width` from its `Width` parameter into its own inline style; a competing inline `Style` is brittle across MudBlazor versions and cannot express the `max-width` companion cleanly. Rejected in favour of a global CSS rule.

---

## R2 — Web host stylesheet placement (the spec's `app.css` is not loaded)

**Decision**: Add the rule to a stylesheet that the web Blazor surface **actually loads** — `Sorcha.UI/Sorcha.UI.Web/wwwroot/app/index.html`. Preferred: extend the existing inline `<style>` block already in that file's `<head>`. Acceptable alternative: add a new `<link href="inbox-drawer.css">` (global, non-isolated) under `wwwroot/app/` and the rule there.

**Evidence**:
- `Sorcha.UI.Web/wwwroot/app/index.html` is the WASM host page (`<base href="/app/">`). Its `<head>` links `_content/MudBlazor/MudBlazor.min.css`, `Sorcha.UI.Web.styles.css` (the CSS-isolation bundle), and Z.Blazor.Diagrams — **but no global `app.css`** — plus an inline `<style>` block (`html, body { overflow-x: hidden; ... }`, `#blazor-error-ui`, loading spinner).
- `Sorcha.UI.Web/wwwroot/app.css` exists but a repo-wide grep shows it is referenced **only** indirectly nowhere by the Blazor app surface; the marketing pages link `landing.css`, not `app.css`. So editing `wwwroot/app.css` (as the spec literally suggests) would have **no runtime effect**.
- The web host's inbox bell drawer is rendered by `Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` (toggles `InboxPanel`), so the host page that loads it is `app/index.html`.

**Rationale**: The spec's assumption ("add to the web host app.css") was written before the host's actual stylesheet wiring was inspected. The corrective principle is unchanged (rule must live in a *loaded, non-isolated* stylesheet); only the concrete file differs. The inline `<style>` block already in `app/index.html` is the lowest-risk loaded location and keeps the cap colocated with the existing `overflow-x: hidden` body rule that backstops SC-003.

**Alternatives considered**:
- *Add the rule to `wwwroot/app.css` and newly `<link>` it* — introduces a brand-new global stylesheet link and a render-blocking request for one rule; larger surface than needed. Rejected unless a dedicated file is preferred for maintainability (recorded as the acceptable alternative).
- *Put it in `Sorcha.UI.Web.styles.css`* — that is the auto-generated CSS-isolation bundle; it must not be hand-edited. Rejected.

---

## R3 — PWA stylesheet placement

**Decision**: Add the rule to `src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/app.css`.

**Evidence**: `Sorcha.Wallet.Pwa/wwwroot/index.html:10` already links `<link href="css/app.css" rel="stylesheet" />` ahead of the isolation bundle `Sorcha.Wallet.Pwa.styles.css`. The PWA's bell drawer is rendered via `Sorcha.Wallet.Pwa/MainLayout.razor:140` `<InboxPanel @bind-IsOpen="_inboxPanelOpen" />`.

**Rationale**: `app.css` is a loaded, non-isolated global stylesheet — exactly the target the fix needs. Matches the spec's PWA instruction verbatim.

---

## R4 — Scoping the global rule to ONLY the inbox drawer (FR-007 / SC-005)

**Decision**: Scope the rule with the attribute selector `.mud-drawer[data-testid="inbox-drawer"]`, reusing the `data-testid="inbox-drawer"` already present on `InboxPanel.razor:26`. (Optionally, add an explicit `Class="inbox-drawer"` to the MudDrawer and target `.mud-drawer.inbox-drawer` instead — slightly more idiomatic for styling, but adds a markup change; both are acceptable, decided at task time.)

**Evidence**:
- Both hosts render a **second** drawer in their layout: `Sorcha.UI.Web.Client/.../MainLayout.razor:76` `<MudDrawer Variant="@DrawerVariant.Mini" ...>` (navigation), and the PWA layout likewise has navigation chrome. A bare `.mud-drawer { width: min(420px,100vw) }` global rule would match these too and resize them — violating FR-007 and SC-005.
- MudBlazor splices unmatched user attributes (including `data-testid`) onto the drawer's root `.mud-drawer` element, so `.mud-drawer[data-testid="inbox-drawer"]` selects exactly the inbox drawer and nothing else.

**Rationale**: A scoped global selector preserves the "single authoritative source" goal while guaranteeing no collateral resize. The `data-testid` already exists (and is also the Playwright hook), so the zero-markup-change option is available.

**Alternatives considered**:
- *Unscoped `.mud-drawer`* — simplest but resizes the nav drawer. Rejected (fails FR-007/SC-005).
- *`Anchor.End` selector (`.mud-drawer-end`)* — would also catch any future right-anchored drawer; less precise than the testid/class. Rejected.

---

## R5 — Width values and breakpoint behaviour

**Decision**: Retain the existing values unchanged — `width: min(420px, 100vw) !important; max-width: 100vw;`. No media query needed.

**Evidence**: The current `InboxPanel.razor.css` rule already uses `min(420px, 100vw)`, which natively yields full-width below 420px and a 420px side panel at/above 420px — satisfying FR-002, FR-003, and the rotation edge case without an explicit `@media` breakpoint.

**Rationale**: The spec (Assumptions) is explicit that only the *location* of the rule changes, not its values. `min()` + `max-width: 100vw` is self-adapting across the 420px threshold, covering the portrait/landscape rotation edge case on next open.

---

## R6 — Removing the dead isolated rule (FR-008)

**Decision**: Delete the `::deep .mud-drawer { ... }` block from `InboxPanel.razor.css`. If that leaves the file empty (only the license header/comment), remove the file or leave only the SPDX header per repo convention.

**Rationale**: The rule never applies at runtime (R1); leaving it in place creates a misleading second source of truth. FR-008 requires a single authoritative location.

---

## R7 — Validation strategy

**Decision**: Prove behaviour with a Playwright E2E that opens the inbox drawer at multiple viewport widths and asserts drawer width vs viewport width and absence of horizontal scroll; keep the existing bUnit `InboxPanelTests.cs` green. Use the Docker test infrastructure per the `playwright` and `sorcha-ui` skills.

**Rationale**: CSS width behaviour is not expressible in a unit test; the measurable outcomes (SC-001…SC-005) are inherently rendered-DOM assertions. The `data-testid="inbox-drawer"` hook makes the drawer element directly selectable. See quickstart.md for concrete viewports and assertions.

**Alternatives considered**: *Manual-only verification* — insufficient for a regression guard the spec frames as measurable outcomes. Rejected; manual steps still documented in quickstart.md for fast local checking.
