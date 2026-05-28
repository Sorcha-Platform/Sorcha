# Implementation Plan: Blueprint Design Lifecycle Overhaul

**Branch**: `142-blueprint-lifecycle` | **Date**: 2026-05-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/142-blueprint-lifecycle/spec.md`
**Design**: `docs/superpowers/specs/2026-05-27-blueprint-lifecycle-overhaul-design.md`

## Summary

Replace the incoherent designer/publish/run surfaces with one staged "golden path" workspace driven by a persistent lifecycle rail (**Describe → Understand → Rehearse → Go live**) so a non-technical authority admin can turn a plain-English intent into a live, governed public service and amend it over time. The build is reuse-heavy (the existing AI chat agent, the portable engine, the production form renderer, the publish/governance APIs, and the real run components are all reused); the genuinely net-new pieces are: a **rehearsal harness** (in-WASM dry-run + full rehearsal on a reused per-org devMode **sandbox register** with ephemeral per-role identities and server-side signing), a **server-side publish gate** (governance-hard + rehearsal-soft with audited override), **form-layout authoring** (edit mode writing standard `x-*` keywords + matching chat tools), the **journey view** projection, the **register system-info card**, and the **amend loop** (clone-published-to-draft + wiring the stubbed Load). Research decisions are in [research.md](./research.md); entities in [data-model.md](./data-model.md); contracts in [contracts/](./contracts/).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: .NET Aspire 13; Blazor WebAssembly + MudBlazor (designer UI); SignalR (`/hubs/chat`); `Sorcha.Blueprint.Engine` (portable, WASM-safe — validate/calculate/route/disclose); Anthropic API via `AnthropicProviderService` (chat agent); YARP gateway; JsonSchema.Net / JsonLogic / JsonE; `Sorcha.Cryptography`
**Storage**: Existing — MongoDB (register ledger), Postgres (blueprint/tenant/wallet). NET-NEW durable: `RehearsalPass` + `PublishOverride` tables in Blueprint Service Postgres (registered via F113 `IStorageRegistrationLog`). Transient rehearsal-session state in Redis/in-memory
**Testing**: xUnit (unit/engine), bUnit (components), Playwright NUnit against Docker (`[Category("Designer")]`/`[Category("Lifecycle")]`) per the `sorcha-ui` discipline
**Target Platform**: Web — .NET services behind the YARP gateway + Blazor WASM client at `/app/designer/blueprint`. Dry-run runs the engine in-browser (WASM)
**Project Type**: web (multi-service backend + Blazor WASM frontend)
**Performance Goals**: full rehearsal of a 3-step service end-to-end < 5 min (SC-003); dry-run step feedback effectively instant (no backend round-trip); journey/Go-live reads on-demand
**Constraints**: rehearsal MUST NOT write to any live/public register (SC-008); dry-run engine path must be WASM-friendly (no HttpClient); the rehearsal→publish gate must be enforced server-side (cannot be UI-only)
**Scale/Scope**: single-author editing per service; existing platform scale; touches Blueprint Service, Register Service (small), Wallet Service (reuse), and the `Sorcha.UI.Web.Client` + `Sorcha.UI.Core` + `Sorcha.UI.Components.User` designer surfaces

## Constitution Check

*GATE: must pass before Phase 0. Re-checked after Phase 1 (below).*

| Principle | Assessment |
|---|---|
| I. Microservices-first | PASS — changes respect existing boundaries: Blueprint Service (rehearsal orchestration, publish gate, clone-to-draft, chat tools), Register Service (sandbox tag + visibility read + sandbox-scoped reset), Wallet Service (reused, no change), UI. Dependencies flow downward; no new upward coupling. |
| II. Security first | PASS / IMPROVES — adds server-side publish-rights enforcement (today UI-only) and an audited override; devMode plaintext confined to sandbox registers; ephemeral sandbox wallets discarded on reset; new endpoints get DataAnnotations/FluentValidation. |
| III. API documentation | COMMIT — new endpoints (rehearsals, from-published, changed publish, register read addition) get .NET 10 OpenAPI + Scalar, `.WithSummary()/.WithDescription()`, and XML docs. |
| IV. Testing (>85% new) | COMMIT — unit (gate logic, exec-def hash, dry-run engine, fidelity test), bUnit (rail/journey/layout tools), Playwright (golden-path + invariants). |
| V. Code quality | PASS — C# 14/.NET 10, async I/O, DI, nullable enabled, no new warnings. |
| VI. Blueprint standards | PASS — blueprints stay JSON; layout authoring writes standard `x-*` keywords (no proprietary format); engine unchanged. |
| VII. DDD ubiquitous language | PASS — constructs keep Blueprint/Action/Participant/Disclosure/Publish; "Rehearsal", "Sandbox register", "Go live", "Journey" are additive user-facing terms. |
| VIII. Observability | COMMIT — new metrics (`rehearsal_run_total{mode,outcome}`, `rehearsal_duration_seconds`, `publish_override_total`, `sandbox_provision_total`) + structured audit logs for overrides and sandbox lifecycle. |

**Result**: PASS — no violations requiring Complexity Tracking. The work fits existing service boundaries and reuses existing infrastructure; net-new pieces are additive.

## Project Structure

### Documentation (this feature)

```text
specs/142-blueprint-lifecycle/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D10
├── data-model.md        # Phase 1 — entities
├── quickstart.md        # Phase 1 — acceptance walk + invariants
├── contracts/
│   └── blueprint-lifecycle.openapi.yaml   # net-new / changed endpoints
└── tasks.md             # Phase 2 — created by /speckit.tasks (NOT here)
```

### Source Code (repository root) — affected areas

```text
src/Apps/Sorcha.UI/
├── Sorcha.UI.Web.Client/Pages/Designer/      # REFACTOR: DesignerBlueprint.razor (rail replaces 3 tabs),
│                                             #   DesignerToolbar (wire Load); NEW rehearsal/go-live stage views
├── Sorcha.UI.Core/
│   ├── Services/Admin/Designer/              # EXTEND DesignerContext (LifecycleState, ExecDefHash, AmendContext)
│   ├── Services/Admin/                       # BlueprintApiService (clone-to-draft, rehearsal client, register system-info aggregate)
│   └── Components/Blueprints/                # REFACTOR PublishBlueprintWizard → Go-live stage; NEW register system-info card
└── Sorcha.UI.Components.User/
    ├── Components/Forms/                      # SorchaFormRenderer edit-mode; NEW layout tools; JourneyView; LifecycleRail
    └── Services/User/Forms/                   # FormSchemaService (reuse AutoGenerateForm); x-* read/write layer + classifier

src/Core/Sorcha.Blueprint.Engine/             # REUSE engine; NEW in-memory IInstanceStore/IActionStore stubs (dry-run, WASM)

src/Services/Sorcha.Blueprint.Service/
├── Hubs/ + Services/ (ChatHub, BlueprintToolExecutor, AnthropicProviderService)  # EXTEND: guided opening + layout tools
├── Services/Implementation/                  # NEW: RehearsalOrchestrationService; exec-def hash; publish-gate guard
├── Endpoints/                                # NEW rehearsal endpoints, from-published; CHANGE publish endpoint (gate)
└── Storage/                                  # NEW RehearsalPass + PublishOverride stores (F113 registration)

src/Services/Sorcha.Register.Service/         # SMALL: sandbox tag handling; surface `advertise` on register read; sandbox-scoped reset
src/Common/Sorcha.ServiceClients.Http/        # client methods for new endpoints (rehearsals, from-published, register read addition)

tests/
├── Sorcha.UI.E2E.Tests/Docker/Designer/      # Playwright lifecycle suite + page objects
├── Sorcha.Blueprint.Service.Tests/           # gate logic, exec-def hash, rehearsal orchestration, clone-to-draft
└── Sorcha.Blueprint.Engine.Tests/            # dry-run harness + dry-run/full fidelity test
```

**Structure Decision**: Existing repository layout (services under `src/Services`, portable cores under `src/Core`, shared clients under `src/Common`, the two designer UI hosts under `src/Apps/Sorcha.UI`). No new top-level structure; the feature refactors `DesignerBlueprint` into the rail-driven workspace and adds focused services/components within the established boundaries.

## Phase 0 — Research

Complete. All unknowns and the two clarify-deferred items resolved in [research.md](./research.md): D1 sandbox-register provisioning & "disposable=resettable" model, D2 ephemeral per-role signing, D3 in-WASM dry-run harness + gap, D4 rehearsal-pass record + server soft gate, D5 server-side publish-rights, D6 register system-info aggregation (+ `advertise` read), D7 presentational-vs-behavioural keyword classifier, D8 form-authoring edit mode, D9 chat guided-opening + layout tools, D10 clone-to-draft + Load wiring. No remaining NEEDS CLARIFICATION.

## Phase 1 — Design & Contracts

Complete: [data-model.md](./data-model.md) (RehearsalPass, PublishOverride, Rehearsal session, LifecycleState, JourneyViewModel, register additions), [contracts/blueprint-lifecycle.openapi.yaml](./contracts/blueprint-lifecycle.openapi.yaml) (rehearsal lifecycle endpoints, gated publish, clone-to-draft, register read addition), [quickstart.md](./quickstart.md) (acceptance walk + invariant checks + test map).

### Post-Design Constitution re-check

PASS — design introduces no new cross-service coupling beyond reuse, strengthens security (server-side gate + audit), and keeps blueprints as standard JSON. No Complexity Tracking entries required.

## Complexity Tracking

No constitution violations — none required.
