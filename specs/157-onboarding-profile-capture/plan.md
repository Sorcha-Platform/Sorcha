# Implementation Plan: Onboarding Profile Capture

**Branch**: `157-onboarding-profile-capture` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/157-onboarding-profile-capture/spec.md`

## Summary

Feature 157 extends three existing first-run surfaces rather than introducing new subsystems:

1. **Complete-your-profile onboarding step (US1, P1)** — a new wizard step that reads the signed-in
   user's persona (`GET /api/me/persona`), pre-fills known fields, lets the user confirm/amend basic
   identity attributes, and writes them back (`PUT /api/me/persona`). The persona endpoints, encrypted
   storage, validation, and a client `IPersonaService` already exist (Feature 092/103/125); this feature
   adds the onboarding-time UI step that seeds the personal-context persona.
2. **Sensible wallet defaults (US2, P2)** — the web wallet-creation wizard (`CreateWallet.razor`) already
   accepts `?name=` and `?words=` query parameters. The onboarding entry point will pass a sensible
   default name and `words=24`; standalone wallet creation keeps its current behaviour (12-word default).
3. **EmailVerified on `/api/auth/me` (US3, P3)** — surface the already-tracked
   `PlatformUser.EmailVerified` flag through the `CurrentUserResponse` DTO, plus the matching client model.

The bulk of the engineering is the US1 onboarding step; US2 is wiring + a conditional default; US3 is a
small read-only projection change. No new service, no new persistence, no new auth mechanism.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: Blazor (Web Client + Wallet PWA), MudBlazor, ASP.NET Core Minimal APIs,
`Sorcha.ServiceClients.Http`, `Sorcha.Tenant.Models` (persona models), FluentValidation

**Storage**: No schema change. Persona persists as an encrypted blob in the existing
`PlatformUserPersonas` table (Tenant Service, Postgres/EF Core). `EmailVerified` already exists on
`PlatformUser`. No migration required.

**Testing**: xUnit + FluentAssertions + Moq (unit/integration); Playwright (Blazor E2E) for the onboarding
step. Tenant Service integration tests for the `/api/auth/me` change.

**Target Platform**: Linux server (Tenant Service); WASM/Blazor (Sorcha.UI.Web.Client) and the Wallet PWA
(citizen surface).

**Project Type**: Web application (Blazor front-ends + .NET service backend) within the Sorcha monorepo.

**Performance Goals**: No new hot path. Persona read/write is a single user-scoped round-trip; the client
`IPersonaService` already caches the persona for session lifetime. Target: profile step completes the
save in a single PUT and continues onboarding (SC-001: under 1 minute end-to-end, user-driven).

**Constraints**: Persona is personal-context only (`context` omitted ⇒ `Guid.Empty`). Profile save must be
all-or-nothing (FR-005) and must not silently advance on failure (Edge Cases). `EmailVerified` must be
unambiguous when unknown (FR-011). Reuse the snackbar-retirement feedback surfaces (Pattern #12 —
`IInlineFeedback`, no `ISnackbar`). Persona write requires a provisioned wallet (the endpoint returns 409
if not) — onboarding ordering must account for this.

**Scale/Scope**: ~3 user stories. Touch points: 1 new onboarding step component, 1 onboarding entry-point
wiring change, 1 DTO field + 1 handler line + 1 client model field, plus tests. No fan-out across services.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ Pass | No new service; no new cross-service dependency. Persona stays in Tenant; encryption already delegated to Wallet via `IPersonaCryptoClient`. |
| II. Security First | ✅ Pass | No new sensitive surface. Persona remains encrypted at rest (XChaCha20-Poly1305). Input validation reuses existing `PersonaAttributesV1` invariants (FluentValidation + DataAnnotations). `EmailVerified` is a read-only projection of existing state — no new trust decision baked into it. |
| III. API Documentation | ✅ Pass | New DTO field gets `/// <summary>`. No new endpoint added (persona endpoints already documented); the `/api/auth/me` summary/description is updated to mention the new field. |
| IV. Testing | ✅ Pass | Unit + integration for the DTO change; integration/E2E for the onboarding step (verified + unverified email; persona round-trip; skip-optional; re-entry update-in-place). Target >85% on new code. |
| V. Code Quality | ✅ Pass | Async I/O, DI, nullable enabled, no new warnings. Matches existing Blazor + Minimal API idioms. |
| VI. Blueprint Standards | ✅ N/A | No blueprint involvement. |
| VII. Domain-Driven Design | ✅ Pass | Uses existing ubiquitous terms: Persona (self-asserted profile), Platform User. No term drift. |
| VIII. Observability | ✅ Pass | Reuses existing persona service logging + the existing `IEventService` audit on persona replace. No new meters required. |

**Result**: PASS — no violations, Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/157-onboarding-profile-capture/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (decisions on placement, defaults, EmailVerified source)
├── data-model.md        # Phase 1 output (entities touched, no schema change)
├── quickstart.md        # Phase 1 output (runnable validation scenarios)
├── contracts/           # Phase 1 output (auth/me response + persona usage notes)
│   ├── auth-me.md
│   └── persona-onboarding.md
├── checklists/          # Pre-existing (spec quality checklists)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

Touch points only — this feature extends existing files; new files are limited to the onboarding step
component + tests.

```text
src/
├── Services/Sorcha.Tenant.Service/
│   ├── Models/Dtos/AuthDtos.cs                     # + EmailVerified on CurrentUserResponse
│   └── Endpoints/AuthEndpoints.cs                  # GetCurrentUser: populate EmailVerified; update .WithSummary/.WithDescription
│
├── Apps/Sorcha.UI/
│   ├── Sorcha.UI.Components.User/
│   │   ├── Components/Onboarding/                  # + CompleteProfileStep.razor (new onboarding step)
│   │   └── Services/User/Persona/IPersonaService.cs# reused as-is (Get/Update)
│   └── Sorcha.UI.Web.Client/Pages/
│       ├── Home.razor                             # onboarding entry: pass ?name=&words=24 into wallet wizard; sequence profile step
│       └── Wallets/CreateWallet.razor             # default words=24 ONLY in onboarding context; standalone unchanged
│
└── Apps/Sorcha.Wallet.Pwa/
    └── Pages/Enrol.razor                          # (if PWA onboarding in scope) surface profile step post-enrolment

tests/
├── Sorcha.Tenant.Service.Tests/Integration/
│   └── AuthApiTests.cs                            # + EmailVerified assertions (verified + unverified)
└── <UI E2E (Playwright)>                          # onboarding profile step: save, pre-fill, skip, re-entry update
```

**Structure Decision**: Sorcha is an established multi-project monorepo (Option 2 — web app with Blazor
front-ends + .NET service backend). This feature does not introduce new projects or alter the structure;
it extends the Tenant Service endpoint surface, the shared user-facing component library
(`Sorcha.UI.Components.User`), and the web onboarding entry point. The new onboarding step lives in
`Sorcha.UI.Components.User/Components/Onboarding/` so both the web host and the PWA can consume it (per the
Feature 122 shared-component convention).

## Complexity Tracking

> No constitution violations — section intentionally empty.
