# Implementation Plan: Web Nav Drawer — Responsive (no mini rail)

**Branch**: `158-web-nav-responsive-drawer` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/158-web-nav-responsive-drawer/spec.md`

## Summary

Replace the web host's navigation `MudDrawer` `Variant="DrawerVariant.Mini"` with
`DrawerVariant.Responsive` in `Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` so a
closed drawer releases **all** horizontal space (no residual icon-only mini rail). The Responsive
variant gives the conventional pattern for free: above the framework breakpoint (desktop) the open
drawer **pushes** content and the closed drawer hands the width back; below the breakpoint (phone)
the drawer **overlays** content with a dismissable scrim and defaults to closed. The drawer's
*contents* (nav links, role-gated sections, badges, dividers) and all routing are unchanged — this
is a purely presentational/layout edit to a single Razor file, plus a refresh of the existing
Playwright E2E coverage to assert the new spatial behaviour.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly + interactive render)

**Primary Dependencies**: MudBlazor **9.5.0** (`MudDrawer`, `MudLayout`, `MudAppBar`,
`MudNavMenu`); pinned centrally in `Directory.Packages.props:137`

**Storage**: N/A — no persistence. Drawer open/closed state is in-memory component state
(`_drawerOpen`), surviving in-session navigation via the persistent `MainLayout`.

**Testing**: xUnit + Playwright E2E (`tests/Sorcha.UI.E2E.Tests`, Docker suite) — existing
`NavigationTests.cs` + `PageObjects/NavigationComponent.cs`

**Target Platform**: Browser (WASM) — desktop and phone viewport widths

**Project Type**: Web application (Blazor) — change confined to the web host's primary layout

**Performance Goals**: No measurable runtime cost; drawer open/close transition must remain smooth
(MudBlazor default animation) with no clipped/orphaned artefacts (SC-005).

**Constraints**: Change limited to the **web** host (`Sorcha.UI.Web.Client`). The Wallet PWA
(`Sorcha.Wallet.Pwa/MainLayout.razor`, bottom `FloatingTabBar`, no drawer) and any Verifier layout
are explicitly **out of scope**. No new breakpoint introduced — reuse MudBlazor's default
responsive breakpoint. No CSS file changes anticipated (`MainLayout.razor.css` holds only the inbox
bell animation).

**Scale/Scope**: One Razor component edited (~3 markup attributes + optional cleanup of now-dead
`@if (_drawerOpen)` guards); one E2E test file + one page object refreshed.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Status |
|-----------|----------|--------|
| I. Microservices-First | No | No service boundaries touched. PASS |
| II. Security First | No | No auth/authz/data-access change; role-gated sections preserved verbatim (FR-006). PASS |
| III. API Documentation | No | No API surface added/changed. PASS |
| IV. Testing Requirements | Yes | Behaviour is validated via existing Playwright E2E; tests refreshed to assert push-vs-overlay + closed-releases-space. PASS |
| V. Code Quality | Yes | Nullable on; no new warnings; remove the `Mini`-only `OpenMiniOnHover` property to avoid a dead/ineffective attribute. PASS |
| VI. Blueprint Standards | No | N/A. PASS |
| VII. Domain-Driven Design | No | No domain model. PASS |
| VIII. Observability | No | No telemetry surface. PASS |

**Result**: PASS — no violations, no complexity to justify. Documentation sync (CLAUDE.md §12 lists
the layout file as the bell-drawer host; no pattern change here) requires no edit beyond the
SPECKIT plan pointer.

## Project Structure

### Documentation (this feature)

```text
specs/158-web-nav-responsive-drawer/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output — variant/breakpoint/cleanup decisions
├── data-model.md        # Phase 1 output — N/A rationale (no entities)
├── quickstart.md        # Phase 1 output — manual + E2E validation guide
├── contracts/
│   └── drawer-behavior.md   # Phase 1 output — UI behaviour contract (states × viewport)
├── checklists/          # Pre-existing
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
└── Components/Layout/
    ├── MainLayout.razor          # EDIT — MudDrawer Variant Mini → Responsive (line 76);
    │                             #        drop OpenMiniOnHover; review ClipMode;
    │                             #        optionally retire now-dead @if(_drawerOpen) guards
    │                             #        (lines 87, 116, 137, 242)
    └── MainLayout.razor.css      # NO CHANGE expected (inbox-bell animation only)

tests/Sorcha.UI.E2E.Tests/
├── Docker/NavigationTests.cs                 # EDIT — assert closed releases space (desktop)
│                                             #        + overlay/closed-default (phone)
└── PageObjects/NavigationComponent.cs        # EDIT if drawer locator/open-detection needs
                                              #        adjustment for Responsive variant
```

**Structure Decision**: Existing Blazor web-app layout. No new projects, files, or directories in
the source tree — the feature is a localized edit to `MainLayout.razor` (the single web host
layout that owns the `MudDrawer`) with a matching refresh of the existing E2E navigation suite. The
PWA's separate `MainLayout.razor` is untouched.

## Complexity Tracking

*No constitution violations — section intentionally empty.*
