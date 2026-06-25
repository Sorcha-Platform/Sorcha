# Phase 0 Research: Web Nav Drawer — Responsive (no mini rail)

All Technical Context items resolved. No open NEEDS CLARIFICATION items remain. The spec is
self-contained; the referenced design note (`docs/superpowers/specs/2026-06-25-pwa-nav-and-present-camera-design.md`)
was absent at plan time, so decisions derive from the spec + the current MainLayout implementation.

---

## D1 — Drawer variant: `DrawerVariant.Responsive`

- **Decision**: Set `Variant="@DrawerVariant.Responsive"` on the `MudDrawer` at
  `MainLayout.razor:76` (currently `DrawerVariant.Mini`).
- **Rationale**: `Responsive` is MudBlazor 9.5.0's built-in pattern that satisfies FR-001/002/003
  with no custom CSS: above the breakpoint the open drawer pushes content and the closed drawer
  occupies **zero** width (kills the mini rail — the specific defect); below the breakpoint it
  renders as a temporary overlay with a scrim and is closed by default. This is exactly the
  "open on desktop, closed/overlay on phone" convention the spec assumes.
- **Alternatives considered**:
  - *Keep `Mini` and CSS-hide the rail at width 0* — fights the framework, brittle across MudBlazor
    upgrades, and `OpenMiniOnHover` still implies a peek affordance the spec drops. Rejected.
  - *`DrawerVariant.Temporary`* — always overlays, even on desktop; violates FR-002 (desktop push).
    Rejected.
  - *`DrawerVariant.Persistent`* — closed state releases space but identical push/overlay on every
    width; no responsive phone overlay default without extra logic. `Responsive` is the superset.
    Rejected.

## D2 — Remove `OpenMiniOnHover="true"`

- **Decision**: Delete the `OpenMiniOnHover="true"` attribute.
- **Rationale**: It is a `Mini`-variant-only affordance (hover the rail to peek). With the rail
  gone there is nothing to hover; the spec (Edge Cases, "No hover-expand expectation") explicitly
  drops it. Leaving it is dead/ineffective markup and a Code-Quality smell.
- **Alternatives considered**: Leave it for safety — rejected; it has no effect under `Responsive`
  and misleads future readers.

## D3 — Breakpoint

- **Decision**: Use MudBlazor's default `Breakpoint` (do not set the attribute) — the framework
  default (`Md`/`Lg`) is the existing responsive boundary.
- **Rationale**: The spec assumption states "follows the existing responsive breakpoint already used
  by the layout framework; no new custom breakpoint is introduced." Omitting `Breakpoint` keeps the
  default and avoids inventing a value.
- **Alternatives considered**: Pin an explicit breakpoint — rejected as out-of-scope and risks
  diverging from other MudBlazor surfaces.

## D4 — `ClipMode="DrawerClipMode.Always"`

- **Decision**: Retain `ClipMode="DrawerClipMode.Always"` initially; verify during validation that
  the app bar + drawer render without overlap/clipping at both viewport widths (SC-005). Adjust only
  if a visual defect appears.
- **Rationale**: `ClipMode.Always` keeps the app bar above the drawer (drawer starts below the bar)
  — desirable so the menu toggle in the bar stays reachable in every state (FR-004). It is
  orthogonal to the push/overlay variant change. Changing it is not required by any FR.
- **Alternatives considered**: Switch to `Docked`/`Never` pre-emptively — rejected; no requirement
  demands it and it risks regressing the app-bar/toggle layout. Treat as a validation checkpoint,
  not a planned edit.

## D5 — Now-dead `@if (_drawerOpen)` section-header guards

- **Decision**: The four `@if (_drawerOpen)` guards (lines 87, 116, 137, 242) that conditionally
  render section dividers/overline headers may be **simplified/removed** as a clean-up, since drawer
  contents only render when the drawer is open under `Responsive` (no icon-only state where headers
  must hide). This is optional and must not alter visible behaviour.
- **Rationale**: Those guards existed to suppress text dividers while the drawer was in icon-only
  mini mode. With `Responsive` there is no icon-only mode — when the drawer is visible it is fully
  open, so the guards are effectively always true. Removing them reduces dead complexity (Principle
  V) without changing what the user sees. Keeping them is also harmless.
- **Alternatives considered**: Mandatory removal — rejected; not required for correctness, so left
  as a low-risk optional clean-up to keep the diff focused if preferred.

## D6 — State persistence across in-session navigation (FR-005)

- **Decision**: No code needed. `_drawerOpen` (default `true`, `MainLayout.razor:294`) is component
  state on the persistent `MainLayout`; `ToggleDrawer()` flips it. It already survives in-app
  navigation. The Responsive variant derives phone "closed by default" from viewport width, not from
  resetting `_drawerOpen`.
- **Rationale**: Meets FR-005 (desktop default open, phone default closed) with the existing field;
  no storage or new state machine required.
- **Alternatives considered**: Persist to localStorage — rejected as out-of-scope; the spec only
  requires *in-session* persistence, which component state already provides.

## D7 — Scope boundary (PWA / Verifier excluded)

- **Decision**: Edit only `Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`. Do not touch
  `Sorcha.Wallet.Pwa/MainLayout.razor`.
- **Rationale**: The PWA layout has no `MudDrawer` (uses a bottom `FloatingTabBar`); it shares no
  code with the web drawer. The spec scopes the change to the web host only.
- **Alternatives considered**: Unify layouts — rejected, explicitly out of scope.

## D8 — Test strategy

- **Decision**: Extend the existing Playwright suite — `Docker/NavigationTests.cs` +
  `PageObjects/NavigationComponent.cs` — rather than add bUnit component tests. Assert: (a) closed
  drawer releases space on desktop (no `.mud-drawer-mini`; content width grows), (b) phone viewport
  renders drawer closed by default and as overlay (scrim present) when opened, (c) selecting a nav
  item on phone closes the drawer, (d) all nav destinations/role sections still present (SC-004).
- **Rationale**: The behaviour is responsive layout + interaction — only meaningful in a real
  browser at real viewport widths, which Playwright already drives in the Docker suite. The page
  object's `IsDrawerOpenAsync()`/`ToggleDrawerAsync()` are reusable; only assertions/locators that
  keyed on the mini rail need updating.
- **Alternatives considered**: bUnit unit tests — rejected; cannot exercise breakpoint-driven
  push-vs-overlay or scrim rendering, which is the heart of the change.
