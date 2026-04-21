# Implementation Plan: AI Designer Unified Shell

**Branch**: `109-designer-shell-redesign` | **Date**: 2026-04-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/109-designer-shell-redesign/spec.md`

## Summary

Consolidate the two blueprint designer pages (`/designer/chat` and `/designer`) into a single shell at `/designer/blueprint/{BlueprintId?}?tab={ai|diagram|preview}` with three tabs sharing live state via a new scoped `DesignerContext` service. The AI tab becomes full-width chat with its input pinned to the bottom; the Diagram tab hosts the existing Blazor Diagrams canvas; the Preview tab (new) renders one action at a time through the existing `SorchaFormRenderer` with an auto-cursor that follows AI activity plus manual prev/next/jump controls.

Technical approach: new shell component + scoped DI context service + three lean pane components, each reading from and narrowly writing to the shared context. Existing `BlueprintChat.razor` and `Designer.razor` pages are reduced to 20-line redirect shims. No API, schema, or validator changes — pure UI refactor in `Sorcha.UI.Web.Client` plus testable helpers in `Sorcha.UI.Core`. Closes GAP-011b (US6 chat-input regression E2E) via `page.evaluate`-injected synthetic SignalR events.

The engineering design document at `docs/superpowers/specs/2026-04-21-ai-designer-layout-redesign-design.md` is the authoritative source for architectural detail; this plan references it rather than duplicating.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (per `src/Apps/Directory.Build.props`)
**Primary Dependencies**: MudBlazor (tabs, toolbar, popover, dialog, form controls); Blazor Diagrams (existing Diagram canvas); SignalR client (`IChatHubConnection`); existing `SorchaFormRenderer` component; existing `IBlueprintApiService` HTTP client
**Storage**: N/A for this feature. Persistence path unchanged — blueprints persist via existing `IBlueprintApiService.SaveAsync`; chat session history persists via existing `IChatSessionStore` on the server (Redis-backed).
**Testing**: xUnit + FluentAssertions + Moq for unit tests of `DesignerContext` and extracted helpers; Playwright (NUnit) + Docker test infrastructure for E2E (`DockerTestBase`, `LoginPage` page objects already in use)
**Target Platform**: Blazor WASM rendering in modern desktop browsers (Chrome, Edge, Firefox). No Blazor Server/Hybrid changes. No mobile-specific layout.
**Project Type**: Single-page WASM client inside a multi-project solution. This feature touches two projects: `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client` (UI) and `src/Apps/Sorcha.UI/Sorcha.UI.Core` (testable helpers).
**Performance Goals**: Tab switches MUST NOT cause any new network round-trip (all three panes kept alive under `MudTabs KeepPanelsAlive="true"`). AI-tool-call → Preview cursor update must complete within one Blazor render cycle (observable as "the form for the named action is on screen by the time the next chat message renders").
**Constraints**: No API, schema, or validator changes. No new permissions. Must coexist with the existing app-shell left navbar. Must preserve the existing `NavigationLock` semantics for unsaved changes. Must not break the existing citizen-facing `SorchaFormRenderer` contract (reused in read-only preview mode).
**Scale/Scope**: Admin-only audience (roles: Administrator, SystemAdmin, Designer). Small user base. One blueprint per designer session. Typical blueprint sizes: 2–20 actions; the jump dropdown and pager are sized for this range.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Status | Notes |
|---|---|---|---|
| I. Microservices-First | UI-only; no service boundary changes | PASS | Existing services unchanged. |
| II. Security First | No new input boundaries, no new secrets, existing JWT + role auth preserved | PASS | `@attribute [Authorize(Roles = "Administrator,SystemAdmin,Designer")]` carries over to the new shell page. |
| III. API Documentation | No new public API | N/A | No new endpoints to document. |
| IV. Testing Requirements | Target >85% coverage for new code | PASS (planned) | `DesignerContext` + 3 helpers + E2E suite defined in spec. |
| V. Code Quality | C# 14 / .NET 10, nullable enabled, no warnings | PASS | Inherited from project's `Directory.Build.props`. |
| VI. Blueprint Creation Standards | No blueprint format or JSON-e usage changes | N/A | |
| VII. Domain-Driven Design | Terms (Blueprint, Action, Participant, Disclosure) used consistently | PASS | Spec + design doc use the ubiquitous language throughout; no new terms introduced. |
| VIII. Observability | Client-side console logging preserved; no new server telemetry | PASS | E2E test `DesignerShell_ConsoleNoErrors_DuringTabSwitches` actively enforces no console errors during tab switching. |

**No constitution violations. No complexity-tracking entries required.**

## Project Structure

### Documentation (this feature)

```text
specs/109-designer-shell-redesign/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (contains only a README — no new APIs)
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

This feature adds code under two existing projects. New files:

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Designer/
├── DesignerBlueprint.razor         # shell page: routes, toolbar, tab strip, hosts panes
├── DesignerBlueprint.razor.cs      # code-behind (keep .razor markup thin)
├── DesignerToolbar.razor           # shared toolbar component (title, Save/Load/Export, validation pill)
├── DesignerTabEnum.cs              # { Ai, Diagram, Preview }
└── Panes/
    ├── AiDesignerPane.razor        # full-width chat + pinned input (extracted from BlueprintChat.razor)
    ├── AiDesignerPane.razor.cs
    ├── DiagramPane.razor           # Blazor Diagrams canvas bound to DesignerContext (extracted from Designer.razor)
    ├── DiagramPane.razor.cs
    ├── FormPreviewPane.razor       # NEW — pager chrome + SorchaFormRenderer
    └── FormPreviewPane.razor.cs

src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Designer/
├── DesignerContext.cs              # scoped DI service (state + events)
├── PreviewPagerLogic.cs            # pure next/prev/jump over action list
├── AutoScrollController.cs         # "auto-scroll unless user scrolled up" state machine
└── TabRouteParser.cs               # query-string tab value → enum with fallback
```

Test files (new):

```text
tests/Sorcha.UI.Core.Tests/Services/Designer/
├── DesignerContextTests.cs         # ~15 xUnit tests covering state transitions and events
├── PreviewPagerLogicTests.cs       # ~5 tests: next/prev/jump/boundaries/unknown-id recovery
├── AutoScrollControllerTests.cs    # ~5 tests: append/scroll-up-pause/resume/disposal/rapid-append
└── TabRouteParserTests.cs          # ~5 tests: valid/case-insensitive/unknown/missing/extra-params

tests/Sorcha.UI.E2E.Tests/Docker/
└── DesignerShellTests.cs           # 10 Playwright tests (see spec §Testing)
```

Files reduced to redirect shims (one release cycle, then deleted):

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
├── BlueprintChat.razor             # 653 LoC → 20 LoC redirect shim
└── Designer.razor                  # → 20 LoC redirect shim
```

Files deleted:

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Chat/
├── BlueprintPreview.razor          # no longer referenced after right-hand preview column removed
└── ValidationBadge.razor           # deleted IF no longer used outside the chat page (verify during implementation)
```

Files modified:

```text
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs
└── register DesignerContext as scoped DI service

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Layout/NavMenu.razor (or equivalent)
└── update designer nav entry to /designer/blueprint

# Any blueprint-list pages or in-app "Edit in Designer" handoffs that build legacy URLs
# (found by grep for /designer/chat/ and /designer?)
```

**Structure Decision**: Single-project-feature layout. New files concentrate under `Pages/Designer/` and `Services/Designer/` namespaces in the existing `Sorcha.UI.Web.Client` and `Sorcha.UI.Core` projects. No new project is added. This aligns with how Feature 092 (Consumer Persona) structured its UI components (under `Pages/MyProfile.razor` + `Services/Persona/`) and keeps the solution project count unchanged.

## Complexity Tracking

*No constitution violations. Table intentionally left empty.*

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
