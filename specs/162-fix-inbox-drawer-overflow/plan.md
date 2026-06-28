# Implementation Plan: Fix Inbox/Bell Drawer Overflowing Phone Width

**Branch**: `162-fix-inbox-drawer-overflow` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/162-fix-inbox-drawer-overflow/spec.md`

## Summary

The shared inbox/bell drawer (`InboxPanel.razor`) caps its width with a `::deep .mud-drawer { width: min(420px, 100vw) }` rule in the component-isolated stylesheet `InboxPanel.razor.css`. Because MudBlazor renders the temporary drawer as an out-of-tree overlay, Blazor's CSS-isolation scope attribute is never stamped onto the `.mud-drawer` element, so the scoped rule is silently dropped at runtime. The drawer falls back to its hard-coded `Width="420px"`, which exceeds a phone viewport (~360–390px) and pushes the drawer's left edge — header, chips, titles, timestamps — off-screen.

**Technical approach**: Move the width cap from the component-isolated stylesheet into each host's **global** (non-isolated) stylesheet, scoped to the inbox drawer only, and delete the dead isolated rule. Two refinements surfaced during research that the spec's assumptions did not anticipate:

1. **Web host has no loaded global `app.css`.** The Blazor surface (`Sorcha.UI.Web/wwwroot/app/index.html`) links only the CSS-isolation bundle (`Sorcha.UI.Web.styles.css`) and MudBlazor — the repo's `Sorcha.UI.Web/wwwroot/app.css` is orphaned and never loaded by the app. The global rule must go into a stylesheet that `app/index.html` actually loads.
2. **Scoping is mandatory, not optional.** Both hosts render a second `<MudDrawer>` (the navigation drawer in each `MainLayout`). A bare `.mud-drawer` global rule would resize the nav drawer too, violating FR-007/SC-005. The rule MUST be scoped to the inbox drawer via the existing `data-testid="inbox-drawer"` attribute that MudBlazor splices onto the drawer root.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly); CSS3

**Primary Dependencies**: MudBlazor (MudDrawer), Blazor CSS isolation, Sorcha.UI.Components.User (shared component library)

**Storage**: N/A — presentation-layer styling change only

**Testing**: Playwright E2E (viewport-width assertions) per the `playwright` skill + Docker test infra; existing `InboxPanelTests.cs` (bUnit) for regression

**Target Platform**: WASM in two hosts — Sorcha Wallet PWA (`src/Apps/Sorcha.Wallet.Pwa`) and the web host (`src/Apps/Sorcha.UI/Sorcha.UI.Web` + `Sorcha.UI.Web.Client`)

**Project Type**: Web/mobile front-end (Blazor WASM, shared component library)

**Performance Goals**: No measurable change — CSS-only edit; drawer open unchanged

**Constraints**: Must not regress tablet/desktop side-panel (FR-009); must not resize any other drawer/panel (FR-007/SC-005); no horizontal page scrollbar at any supported width (SC-003)

**Scale/Scope**: 4 files touched (PWA `app.css`, web host global stylesheet, `InboxPanel.razor.css`, optionally `InboxPanel.razor` if a class hook is preferred over `data-testid`). No API, no data model, no service changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Impact | Status |
|-----------|--------|--------|
| I. Microservices-First | No service boundaries touched; front-end CSS only | ✅ PASS |
| II. Security First | No data, auth, secrets, or input boundaries touched | ✅ PASS (N/A) |
| III. API Documentation | No API surface added or changed; no XML doc obligations | ✅ PASS (N/A) |
| IV. Testing Requirements | Add a Playwright viewport-width E2E covering P1/P2/P3; keep existing bUnit tests green. CSS rules are not unit-coverable, so behaviour is proven via E2E | ✅ PASS |
| V. Code Quality | No C# logic change; CSS follows existing comment/style conventions; no new warnings | ✅ PASS |
| VI. Blueprint Standards | N/A | ✅ PASS (N/A) |
| VII. Domain-Driven Design | No domain model change | ✅ PASS (N/A) |
| VIII. Observability | No telemetry surface change | ✅ PASS (N/A) |

**Cross-cutting (CLAUDE.md) checks**:
- Notification routing (Pattern #12): this is the Feature 118 bell drawer; no `ISnackbar` introduced. ✅
- Shared component placement (Feature 122): `InboxPanel` stays in `Sorcha.UI.Components.User`; the fix lives in host-global stylesheets, consistent with the "isolation strips out-of-tree overlay styles" rationale. ✅
- License header on any new stylesheet content. ✅

No violations. Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/162-fix-inbox-drawer-overflow/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output — root-cause + placement/scoping decisions
├── data-model.md        # Phase 1 output — N/A (no data); records why
├── quickstart.md        # Phase 1 output — manual + E2E validation guide
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

No `contracts/` directory: this feature exposes no external interface (no REST/gRPC/CLI/component-API contract changes). The only "contract" is the visual width behaviour, captured as measurable outcomes in the spec and validated via quickstart.md.

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Inbox/
├── InboxPanel.razor          # MudDrawer with data-testid="inbox-drawer" (scoping hook); Width="420px"
└── InboxPanel.razor.css      # REMOVE the dead `::deep .mud-drawer` rule

src/Apps/Sorcha.Wallet.Pwa/wwwroot/css/
└── app.css                   # ADD scoped global rule (already <link>-ed from index.html)

src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/app/
└── index.html                # ADD scoped global rule via a loaded stylesheet
                              #   (inline <style> block OR a new linked css — see research.md)

tests/
├── Sorcha.UI.Core.Tests/Components/Inbox/InboxPanelTests.cs   # keep green (bUnit)
└── (Playwright E2E)          # NEW viewport-width assertion per playwright skill
```

**Structure Decision**: Front-end, two-host Blazor WASM. The shared `InboxPanel` lives in `Sorcha.UI.Components.User`; the width cap is delivered through each host's global, non-isolated CSS because the runtime drawer element is mounted outside the component's isolation scope. The web host requires a stylesheet that `app/index.html` actually loads (the existing `wwwroot/app.css` is not loaded by the Blazor surface) — resolved in research.md.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
