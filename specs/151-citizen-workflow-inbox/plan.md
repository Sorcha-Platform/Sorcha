# Implementation Plan: PWA Citizen Workflow Inbox

**Branch**: `151-citizen-workflow-inbox` | **Date**: 2026-06-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/151-citizen-workflow-inbox/spec.md`

**Source design**: `docs/superpowers/specs/2026-06-13-pwa-citizen-workflow-inbox-design.md`

## Summary

Add a consumer-tier "Things to do" inbox to the Citizen Wallet PWA. The inbox lists the workflow
actions currently awaiting the signed-in citizen (their turn) by consuming the **existing**
`GET /api/actions/pending` and `GET /api/actions/pending/count` endpoints — which already resolve a
citizen's wallet(s) from a consumer-tier token via `platform_user_id`. Tapping an action routes
into the **existing** `ApplicationInstance` fill-and-submit flow (unchanged). The existing
Feature-124 pending-application notice is surfaced as a lightweight "In review" banner, and a nav
count badge reflects outstanding work, refreshing live on the existing citizen SignalR signal.

**Technical approach**: one new typed client (`IMyActionsClient`) over the two existing endpoints,
one new inbox page in `Sorcha.Wallet.Pwa`, and a nav count badge — reusing the shared
`SorchaFormRenderer` and `ApplicationInstance` flow with **no backend change** and strictly
**consumer-tier**.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (Blazor WebAssembly PWA)

**Primary Dependencies**: `Sorcha.Wallet.Pwa` (host); shared `Sorcha.UI.Components.User`
(`SorchaFormRenderer`, form controls, feedback primitives); MudBlazor (Material components);
existing PWA HTTP client + consumer-tier auth message handler; existing
`CitizenWalletHubConnection` (SignalR) for live refresh.

**Storage**: N/A — no persistence in A. (Local drafts / encrypted IndexedDB are sub-project C.)

**Testing**: bUnit component tests (`JSRuntimeMode.Loose`) + stub `HttpMessageHandler` client tests,
per the `sorcha-ui` / `playwright` / `xunit` skills. No new backend tests (no backend change).

**Target Platform**: Blazor WASM PWA mounted at `/wallet/` behind the API Gateway (consumer tier).

**Project Type**: Mobile-style web (PWA) front-end feature against existing backend services.

**Performance Goals**: Inbox renders the citizen's outstanding actions promptly on open; live count
reflects a newly-arrived action within ~10s while the app is open (SC-004); single-form action
completable in under 2 minutes excluding user data entry (SC-003).

**Constraints**: No backend changes (SCOPE-001); strictly consumer-tier (FR-012); reuse the shared
renderer and existing submit path; base-relative navigation only (PWA path-prefix rule); no
`ISnackbar` (Critical Pattern #12 — use `IInlineFeedback`).

**Scale/Scope**: One client + one page + one nav badge in `Sorcha.Wallet.Pwa`. ~3 production units,
~2 test classes. No new endpoints, no new persistence.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Applies? | Status |
|-----------|----------|--------|
| I. Microservices-First | No service boundaries touched | ✅ PASS — front-end only, consumes existing APIs; no upward/new coupling |
| II. Security First | Consumer-tier surface | ✅ PASS — no secrets; consumer-tier token via existing handler; reuses existing authz on existing endpoints; no new external boundary (no new input validation surface) |
| III. API Documentation | No new APIs | ✅ N/A — no new endpoints; existing OpenAPI unaffected |
| IV. Testing (>85% new code) | Yes | ✅ PLANNED — bUnit + client tests for all new units; TDD per task ordering |
| V. Code Quality | Yes | ✅ PLANNED — nullable enabled, async I/O, DI, no warnings; follows existing PWA patterns |
| VI. Blueprint Standards | No blueprints | ✅ N/A |
| VII. Domain-Driven Design | Yes | ✅ PASS — uses ubiquitous terms (Action, Participant, Instance); "inbox/Things to do" is UI labelling over Actions |
| VIII. Observability | Front-end | ✅ PASS — structured logging via existing PWA logger; no string-interpolated logs; no new service health surface |

**Result**: PASS. No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/151-citizen-workflow-inbox/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (client view models + consumed shapes)
├── quickstart.md        # Phase 1 output (build/run/test)
├── contracts/           # Phase 1 output (consumed endpoint contracts — read-only)
│   └── consumed-endpoints.md
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.Wallet.Pwa/
├── Services/
│   └── Actions/                         # NEW
│       ├── IMyActionsClient.cs          # NEW — typed client over the two existing endpoints
│       ├── HttpMyActionsClient.cs       # NEW — implementation
│       └── Models/                      # NEW — client DTOs (PendingActionItem, PendingActionsCount)
├── Pages/
│   └── Actions.razor                    # NEW — the "Things to do" inbox page (route: actions)
├── Components/
│   └── Layout/ (or Shared nav)          # MODIFY — add "Things to do" nav entry + count badge
├── Pages/ApplicationInstance.razor      # UNCHANGED — reused as the open-action target
└── Program.cs                           # MODIFY — register IMyActionsClient in DI

src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
└── Components/Forms/ (SorchaFormRenderer, controls)   # UNCHANGED — reused

tests/
└── (PWA component-test project, per existing convention)
    ├── Actions/MyActionsClientTests.cs              # NEW — JSON mapping via stub handler
    └── Pages/ActionsInboxTests.cs                   # NEW — bUnit: list/empty/order/badge/nav
```

**Structure Decision**: Front-end feature contained within `Sorcha.Wallet.Pwa`, reusing the shared
`Sorcha.UI.Components.User` library and the existing `ApplicationInstance` submit flow. The exact
nav-host file and the `Actions.razor`-vs-repurpose-`Applications.razor` decision are resolved in
research.md / tasks (see Open Decisions). No backend project is touched.

## Open Decisions (resolved in research.md)

- **Page identity**: new `Actions.razor` vs. repurposing the empty-stub `Applications.razor`.
  Decision in research.md (must not collide with sub-project B's catalogue plans for
  `Applications.razor`).
- **Test project location**: which existing PWA test project hosts the new tests.
- **Nav host**: which layout/nav component carries the "Things to do" entry + badge.

## Complexity Tracking

> No Constitution Check violations. Section intentionally empty.
