# Implementation Plan: AIAS decision integrity & visibility

**Branch**: `183-aias-decision-visibility` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/183-aias-decision-visibility/spec.md`

**Design source of truth**: `docs/superpowers/specs/2026-07-12-aias-emailverified-claim-source-design.md` (approved via brainstorming; not re-derived here).

## Summary

Fix the AIAS (Feature 174 / M1) web-app happy path — every real citizen application is currently auto-rejected on the agent's email-verified check — and make the reject outcome visible to the applicant.

Two coupled, independently-testable slices:

1. **Decision integrity (US1, P1)** — a reusable, headless JSON-Schema extension **`x-claim-source`** plus a **`ClaimSourceSeeder`** that stamps a form field from a named JWT claim into `FormContext.FormData` at form init, so the applicant's *real* `email_verified` status rides the wallet-signed payload. The AIAS blueprint's `emailVerified` property gains `"x-claim-source": "email_verified"`. Boolean claims fail closed.
2. **Decision visibility (US2, P2)** — a blueprint-declared route annotation **`x-decision-notice`** plus **`BlueprintInboxWriter.WriteDecisionAsync`**, hooked in `ActionExecutionService` after route resolution, that drops a durable F118 bell/inbox entry (reject-only) carrying the on-brand reason to the workflow's starting participant. Fail-safe: never affects sealing/routing.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (per constitution: net10.0, nullable enabled, no Release warnings).

**Primary Dependencies**: Blazor WASM + MudBlazor (web client form renderer); `System.Text.Json` (schema parsing); `Microsoft.AspNetCore.Components.Authorization` (`AuthenticationStateProvider` — claim read); existing `IPlatformInboxClient` / `IParticipantServiceClient` (F118 inbox + participant resolution); existing `IJsonLogicEvaluator` (F176 issuance gate, already in place).

**Storage**: No new persistent store, **no EF migration**. The decision notice writes through the existing F118 durable inbox (Tenant `IInboxStore`, already audited/persistent). The claim-source seed lives only in transient in-memory `FormContext`.

**Testing**: xUnit v3 + FluentAssertions + Moq (per constitution + repo conventions). Unit tests for `ClaimSourceSeeder` (Sorcha.UI.Core.Tests) and the decision-write path (Sorcha.Blueprint.Service.Tests); PowerShell rehearsal harness (`demos/AIAS/rehearse.ps1`) for the end-to-end regression; Chrome DevTools MCP for live n1 verification.

**Target Platform**: Web app at `https://n1.sorcha.dev/app` (Blazor WASM client `Sorcha.UI.Web.Client` + shared `Sorcha.UI.Components.User`); Blueprint Service container.

**Project Type**: Web application (Blazor WASM front end + .NET microservices back end).

**Performance Goals**: No new hot path. The claim read is a local cached-token parse (sub-millisecond); the inbox write is off the sealing path and best-effort.

**Constraints**: The seeded value MUST be present in `FormData` before submit (the multi-page wizard guarantees this; seeding runs at form init). The inbox write MUST NOT block, delay, or reverse sealing/routing (try/log/swallow). Fail-closed on unresolved verified status.

**Scale/Scope**: Small, surgical. ~2 new small units (`ClaimSourceSeeder`, `WriteDecisionAsync`), 2 wiring edits (`SorchaFormRenderer`, `ActionExecutionService`), 2 blueprint-annotation edits, 1 harness edit, and their tests. Spans two deployable components (web client image, Blueprint Service image) + one blueprint re-provision.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ PASS | No new service, no new upward dependency. Client change stays in the UI layer; server change stays in Blueprint Service, reusing its existing `IBlueprintInboxWriter` seam. Core/Domain untouched. |
| II. Security First | ✅ PASS | The verified-status value is carried on the **wallet-signed** payload (tamper-evident, FR-002); fail-closed on unknown (FR-003). No secrets. Input is an existing authenticated claim. The new schema keyword is an `x-*` extension already stripped before validation (F137 precedent). |
| III. API Documentation | ✅ PASS | No new HTTP endpoint. New public methods (`ClaimSourceSeeder.Resolve`, `WriteDecisionAsync`) get XML `<summary>` docs. |
| IV. Testing Requirements | ✅ PASS | xUnit unit tests for both new units (>85% on new code), TDD order (tests first). Regression harness case added. |
| V. Code Quality | ✅ PASS | async/await, DI (`IServiceProvider.GetService` graceful-skip like persona autofill), nullable enabled, no new warnings. |
| VI. Blueprint Standards | ✅ PASS | The two new capabilities are **declared in the blueprint JSON** (`x-claim-source`, `x-decision-notice`), not in C# — squarely on-principle. |
| VII. Domain-Driven Design | ✅ PASS | Uses Participant / Disclosure / Action vocabulary; "starting participant", "route", "decision". No "workflow/step/user" drift in new names. |
| VIII. Observability | ✅ PASS | Structured logging on the inbox-write path (reuses existing `BlueprintInboxWriter` logging); no string-interpolated logs. New failure paths log at Warning. |

**Result: PASS — no violations, Complexity Tracking not required.**

## Project Structure

### Documentation (this feature)

```text
specs/183-aias-decision-visibility/
├── spec.md              # Feature spec (done)
├── plan.md              # This file
├── research.md          # Phase 0 — design decisions (done here; mostly settled in the design doc)
├── data-model.md        # Phase 1 — the two declarations + the notification entity
├── quickstart.md        # Phase 1 — build/test/deploy/verify runbook
├── contracts/           # Phase 1 — the x-claim-source + x-decision-notice schema contracts
├── checklists/
│   └── requirements.md  # Spec quality checklist (done)
└── tasks.md             # Phase 2 (/speckit.tasks — next)
```

### Source Code (repository root)

```text
# US1 — decision integrity (claim-source binding), web client + shared components
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/
├── Services/User/Forms/
│   ├── ClaimSourceSeeder.cs                 # NEW — IClaimSourceSeeder + impl (pure resolver)
│   └── FormSchemaService.cs                 # (reference only — dispatch precedent)
└── Components/Forms/
    └── SorchaFormRenderer.razor             # EDIT — seed x-claim-source at form init (fire-and-forget, like persona autofill)

tests/Sorcha.UI.Core.Tests/Components/Forms/
└── ClaimSourceSeederTests.cs                # NEW — verified→true, unverified→false, absent→false, no-binding→unseeded

# US2 — decision visibility (reject notification), Blueprint Service
src/Services/Sorcha.Blueprint.Service/
├── Services/Implementation/
│   ├── BlueprintInboxWriter.cs              # EDIT — add WriteDecisionAsync (reuse resolution + idempotency)
│   └── ActionExecutionService.cs            # EDIT — after route resolution, fire x-decision-notice writes
└── Models/ (route model)                    # EDIT if needed — surface x-decision-notice on the route model

tests/Sorcha.Blueprint.Service.Tests/Services/
├── BlueprintInboxWriterTests.cs             # EDIT/NEW — decision-write: recipient, reason summary, idempotent, short-circuit
└── ActionExecutionService*Tests.cs          # EDIT/NEW — terminal-route-with-notice → one write; no-notice → none; write-throw → decision unaffected

# Blueprint + demo harness
demos/AIAS/
├── blueprints/aias-assured-identity.template.json   # EDIT — emailVerified gains x-claim-source; reject route gains x-decision-notice
└── rehearse.ps1                                       # EDIT — de-hardcode emailVerified; add unverified→reject case
```

**Structure Decision**: Web application. US1 lives in the shared user-facing component library (`Sorcha.UI.Components.User`, RootNamespace `Sorcha.UI.Core`) so both the web host and the PWA pick it up; US2 lives in Blueprint Service alongside the existing `BlueprintInboxWriter`. Both slices are additive; no layer boundary is crossed.

## Complexity Tracking

No constitution violations — section intentionally empty.
