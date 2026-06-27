# Implementation Plan: PWA Shared Persona/Profile Editor

**Branch**: `163-pwa-shared-persona-editor` | **Date**: 2026-06-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/163-pwa-shared-persona-editor/spec.md`

## Summary

A citizen cannot save their profile from the Sorcha Wallet PWA today: the PWA `Pages/Profile.razor` is a placeholder stub and the persona read/save services (`IPersonaService` / `IPersonaClient`) are registered only in the web host. This feature **extracts the existing profile form** out of the web `MyProfile.razor` into a single **shared editor component** that lives in `Sorcha.UI.Components.User`, hosts that **same** component on both the web `/profile` page and the PWA `/profile` page, and **wires the persona services into the PWA DI container** (including the missing `ILocalStorageService` dependency). No server endpoints, persona fields, or validation rules change — the gap is purely client-side composition and DI registration. bUnit tests cover the shared editor's load / edit / save-success / validation-rejection / provisioning-rejection paths, plus a PWA-host activation test that guards against the "works on web, broken on PWA" regression class.

**Technical approach**: reuse-not-rewrite. The persona service/client/model layer already lives in the shared `Sorcha.UI.Components.User` library (root namespace `Sorcha.UI.Core`), which the PWA already references. The work is: (1) lift the form markup + logic from `MyProfile.razor` into a new `PersonaEditor` component in the shared library; (2) reduce both `MyProfile.razor` (web) and `Profile.razor` (PWA) to thin hosts of that component; (3) register `IPersonaClient` (typed authenticated `HttpClient`), `IPersonaService`, and `AddBlazoredLocalStorage()` in the PWA's `AddCitizenWalletServices`; (4) add bUnit + DI-activation tests.

## Technical Context

**Language/Version**: C# 14 / .NET 10

**Primary Dependencies**: Blazor WebAssembly, MudBlazor, Blazored.LocalStorage, `Sorcha.Tenant.Models.Persona` (model layer), existing `Sorcha.UI.Core.Services.Persona.*` (service/client layer)

**Storage**: None new. Server-side persona store via existing `GET|PUT|DELETE /api/me/persona` (Tenant Service, Feature 092). Browser local storage (Blazored) for the autofill preference only.

**Testing**: xUnit v3 + bUnit + FluentAssertions + Moq, via the `Sorcha.UI.Testing` support library (`ComponentTestFixture`). Shared-editor tests land in `tests/Sorcha.UI.Core.Tests`; PWA-activation test in `tests/Sorcha.Wallet.Pwa.Tests`.

**Target Platform**: Blazor WASM — two hosts: `Sorcha.UI.Web.Client` (web) and `Sorcha.Wallet.Pwa` (mobile companion).

**Project Type**: Web application — shared Razor component library consumed by two WASM front-end hosts.

**Performance Goals**: No specific throughput target; interactive client component. Save/load latency bounded by the existing persona endpoints. Keep the PWA bundle free of `Blazor.Diagrams*`, `YamlDotNet*`, `Sorcha.UI.Core*` (enforced by `scripts/check-pwa-bundle.ps1`).

**Constraints**: Companion-first — ONE shared component, no PWA-specific fork. Component must activate under the PWA DI container (FR-014). Save is full-replace (not patch), consistent with existing web behaviour. Must surface 400 (validation), 409 (wallet-not-provisioned), and network/server failures as distinct inline, recoverable messages.

**Scale/Scope**: One new shared component (+ optional small sub-components), two thin host pages, ~3 PWA DI registrations, ~6 component tests + 1 activation test. Single field set (≤5 entries per multi-value list).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | ✅ PASS | Client-only change. No new service coupling; dependencies flow down into the shared component library which the PWA already references. |
| II. Security First | ✅ PASS | Reuses authenticated `HttpClient` chain (BearerTokenHandler + ServerClockHandler). Input validation enforced server-side (unchanged) and surfaced inline. No secrets. Consumer-tier JWT reaches `/api/me/persona` as on web. |
| III. API Documentation | ✅ PASS | No new endpoints. Public component parameters and service members carry `/// <summary>` (existing services already documented). |
| IV. Testing Requirements | ✅ PASS | bUnit coverage for load/edit/save-success/validation/provisioning + PWA DI activation test (FR-013, FR-014). xUnit. |
| V. Code Quality | ✅ PASS | Nullable enabled, async/await, DI, no new warnings. Reuse over rewrite. |
| VI. Blueprint Standards | ✅ N/A | No blueprints involved. |
| VII. Domain-Driven Design | ✅ PASS | Uses existing ubiquitous terms (Persona). No new domain language. |
| VIII. Observability | ✅ PASS | Reuses existing `ILogger` usage in services and page; structured logging, no string interpolation in logs. |

**Result**: PASS — no violations. Complexity Tracking section not required.

**Post-design re-check**: PASS (see Phase 1). The only design subtlety — a transitive DI dependency (`ILocalStorageService`) missing from the PWA host — is resolved by registering `AddBlazoredLocalStorage()` and is explicitly guarded by the activation test. No constitutional impact.

## Project Structure

### Documentation (this feature)

```text
specs/163-pwa-shared-persona-editor/
├── plan.md              # This file (/speckit-plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── persona-api.md    # Existing /api/me/persona contract (reference; unchanged)
├── checklists/
│   └── requirements.md  # (pre-existing)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/Apps/Sorcha.UI/Sorcha.UI.Components.User/        # SHARED library (root namespace Sorcha.UI.Core)
├── Components/
│   └── Persona/                                      # NEW
│       └── PersonaEditor.razor                       # NEW — the single shared editor (extracted from MyProfile.razor)
└── Services/User/Persona/                            # EXISTING — reused unchanged
    ├── IPersonaService.cs / PersonaService.cs
    ├── IPersonaClient.cs / PersonaHttpClient.cs
    └── (PersonaValidationException, PersonaWalletNotProvisionedException)

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
└── Pages/MyProfile.razor                             # MODIFIED — reduced to a thin host of <PersonaEditor/>

src/Apps/Sorcha.Wallet.Pwa/
├── Pages/Profile.razor                               # MODIFIED — stub replaced by a thin host of <PersonaEditor/>
└── Extensions/ServiceCollectionExtensions.cs         # MODIFIED — register IPersonaClient + IPersonaService + AddBlazoredLocalStorage

tests/Sorcha.UI.Core.Tests/
└── Components/Persona/
    └── PersonaEditorTests.cs                         # NEW — load/edit/save/validation/provisioning bUnit tests

tests/Sorcha.Wallet.Pwa.Tests/
└── Services/
    └── PersonaDiActivationTests.cs                   # NEW — IPersonaService resolves & PersonaEditor renders under PWA DI
```

**Structure Decision**: Web application with a shared Razor component library. The persona model + service + client layers already live in `Sorcha.UI.Components.User` (consumed by both hosts), so the only new shared artifact is the `PersonaEditor` component. The two host pages become thin shells. This is the minimal change that satisfies "one shared editor on both surfaces" (FR-004) and keeps the PWA bundle within its hygiene budget.

## Complexity Tracking

> Not required — Constitution Check passed with no violations.
