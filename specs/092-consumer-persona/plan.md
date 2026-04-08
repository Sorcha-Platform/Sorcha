# Implementation Plan: Consumer Persona and Nav Tidy

**Branch**: `092-consumer-persona` | **Date**: 2026-04-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/092-consumer-persona/spec.md`
**Design source**: [`docs/superpowers/specs/2026-04-08-consumer-persona-and-nav-tidy-design.md`](../../docs/superpowers/specs/2026-04-08-consumer-persona-and-nav-tidy-design.md)

## Summary

Ship a consumer-grade form-filling experience built around a per-user "My Profile / Persona" — a bundle of self-asserted identity attributes encrypted at rest with a purpose-derived wallet key, surfaced through `SorchaFormRenderer` as cream-tinted autofill with a clear `self` provenance tick and per-field accessible announcement. The persona lives in Tenant Service (attached to `PlatformUser`); the key is derived by Wallet Service under a new `sorcha:persona-vault` purpose so the two concerns never co-locate. Contracts (`IPersonaService`, `PersonaAttribute<T>`) are stable across future PoA delegation, VC-backed attributes, and a later move to client-side decryption. A small navigation tidy-up ships alongside: drop the "Navigation" drawer header, remove Settings and Notifications from the side nav, add "My Profile" to `UserProfileMenu`, and merge notification preferences into a Settings tab.

## Technical Context

**Language/Version**: C# 13 on .NET 10
**Primary Dependencies**: .NET Aspire 13, Minimal APIs, Scalar, YARP (Gateway), EF Core (Tenant), MudBlazor 8, Refit-style service clients, Sorcha.Cryptography, JsonSchema.Net 7.4.0, JsonE.NET
**Storage**: PostgreSQL (Tenant Service, new `platform_user_personas` table folded into existing initial setup migration)
**Testing**: xUnit v3.2.2, FluentAssertions 8.8.0, Moq 4.20.72 (unit + integration); Playwright (UI E2E against Docker test infrastructure per the `sorcha-ui` skill)
**Target Platform**: Tenant Service + Wallet Service (Linux containers via Aspire/Docker Compose); Blazor WASM client (`Sorcha.UI.Web.Client`)
**Project Type**: Web application (microservices backend + Blazor WASM frontend) — mirrors existing Sorcha repo layout
**Performance Goals**: Cold-load persona autofill applied within 500 ms at p95 from form-interactive; warm-cache fill indistinguishable from initial render (SC-006a). Persona read/write endpoints under existing `RateLimitPolicies.Api`.
**Constraints**: Ciphertext never co-located with the encryption key (FR-003). Persona contract must not change shape when decryption moves from server to client in a later phase (FR-035). Multi-value lists hard-capped at 5 entries each (FR-002a). Schema extension (`x-persona`) wins over inference allowlist (FR-010, FR-011). All write operations audited via existing `IActivityLogService` (FR-007).
**Scale/Scope**: Per-user resource, max 5 entries per multi-value list. Write volume is low (profile edits, not hot-path). Read volume equals form-open rate, mitigated by a session-lifetime client cache so the hot path is local.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Microservices-First | **Pass** | Tenant Service owns persona storage; Wallet Service owns encryption key derivation under `sorcha:persona-vault`. No upward dependencies. Ciphertext and key are held by different services. |
| II. Security First | **Pass** | Encryption at rest via XChaCha20-Poly1305 (same primitive as Feature 085 file chunks); key separation enforced by service boundary; inputs validated at the Tenant endpoint via FluentValidation; write audit logging; rate-limited via existing `RateLimitPolicies.Api`. |
| III. API Documentation | **Pass** | New endpoints use Minimal APIs with `.WithSummary()` / `.WithDescription()` and XML comments; Scalar exposes them. No Swashbuckle. |
| IV. Testing Requirements | **Pass** | xUnit unit tests for services and resolver, FluentAssertions, Moq for boundary doubles; integration tests for endpoints; Playwright E2E per `sorcha-ui` skill. Target ≥85% coverage for new code. Deterministic, AAA pattern. |
| V. Code Quality | **Pass** | Targets .NET 10 / C# 13, nullable enabled, async/await on all I/O, DI throughout, no warnings in Release. |
| VI. Blueprint Creation Standards | **Pass** | `x-persona` is a **JSON schema extension** on existing JSON/YAML blueprint documents. No new Fluent API surface required. The `blueprint-builder` skill will emit `x-persona` tags on obvious fields going forward. |
| VII. Domain-Driven Design | **Pass** | Persona is attached to `PlatformUser` (the existing canonical term for the cross-org identity anchor). The feature keeps the distinction between "participant" (blueprint role) and "user" (platform identity) clean — persona belongs to the latter. |
| VIII. Observability by Default | **Pass** | Writes are activity-logged. Persona reads and writes emit OpenTelemetry traces via existing Tenant/Wallet telemetry. Structured logging for inference warnings and crypto failures. |

**Result**: No violations. No Complexity Tracking entries required.

## Project Structure

### Documentation (this feature)

```text
specs/092-consumer-persona/
├── plan.md              # This file
├── research.md          # Phase 0 — design decisions + rejected alternatives
├── data-model.md        # Phase 1 — entities, DTOs, invariants
├── contracts/
│   ├── tenant-persona-api.yaml    # OpenAPI for /me/persona endpoints
│   └── wallet-persona-crypto.yaml # OpenAPI for internal /persona/encrypt|decrypt endpoints
├── quickstart.md        # Phase 1 — how to exercise end-to-end locally
├── checklists/
│   └── requirements.md  # Spec quality checklist (already present)
└── tasks.md             # Phase 2 output — produced by /speckit.tasks
```

### Source Code (repository root)

The feature touches existing Sorcha projects only — no new top-level projects. Paths below are all under `C:\Projects\Sorcha`.

```text
src/Common/
├── Sorcha.Cryptography/
│   └── DerivationContexts.cs                         # ADD PersonaVault = "sorcha:persona-vault"
├── Sorcha.Tenant.Models/
│   └── Persona/
│       ├── PersonaAttributesV1.cs                    # NEW — plaintext shape
│       ├── PersonaReadModelV1.cs                     # NEW — read-side DTO with Default* + All*
│       ├── PersonaAttribute.cs                       # NEW — generic wrapper (Value, Source, VerifiedBy, LastUpdated)
│       ├── PersonaEmail.cs / PersonaPhone.cs / PersonaAddress.cs  # NEW — multi-value entries
│       └── PersonaReadOptions.cs                     # NEW — actingAs parameter wrapper
└── Sorcha.ServiceClients.Http/
    └── IPersonaClient.cs                             # NEW — HTTP client surface (Refit)

src/Services/Sorcha.Tenant.Service/
├── Data/
│   ├── Entities/PlatformUserPersona.cs               # NEW — EF entity
│   ├── Configurations/PlatformUserPersonaConfiguration.cs  # NEW — EF config
│   ├── TenantDbContext.cs                            # MODIFY — add DbSet + relationship
│   └── Migrations/                                   # EDIT existing initial setup migration — DO NOT add new incremental migration
├── Services/
│   ├── Interfaces/IPersonaService.cs                 # NEW
│   └── Implementation/PersonaService.cs              # NEW — orchestrates Wallet S2S + repo
├── Endpoints/PersonaEndpoints.cs                     # NEW — MapGroup("/me/persona") + MapDelete cascade hook
└── Extensions/ServiceCollectionExtensions.cs         # MODIFY — register IPersonaService

src/Services/Sorcha.Wallet.Service/
├── Services/Interfaces/IPersonaCryptoService.cs      # NEW
├── Services/Implementation/PersonaCryptoService.cs   # NEW — derives sorcha:persona-vault, AEAD
└── Endpoints/PersonaCryptoEndpoints.cs               # NEW — internal-only (persona:crypto scope)

src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Services/Persona/
│   ├── IPersonaService.cs                            # NEW — client service
│   └── PersonaServiceClient.cs                       # NEW — wraps IPersonaClient + session cache
├── Services/Forms/
│   └── PersonaAutofillResolver.cs                    # NEW — pure resolver, unit-testable
├── Models/Forms/PersonaFillResult.cs                 # NEW
└── Components/
    ├── Forms/SorchaFormRenderer.razor (+ .razor.css) # MODIFY — integrate resolver, tint styles, a11y labels
    ├── Forms/PersonaFillSummary.razor                # NEW — above-form summary + Review popover
    └── Shared/UserProfileMenu.razor                  # MODIFY — add "My Profile" MudMenuItem above "Settings"

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── Components/Layout/MainLayout.razor                # MODIFY — remove drawer "Navigation" header + Settings/Notifications nav items
└── Pages/
    ├── Profile.razor                                 # NEW — @page "/profile"
    └── Settings.razor                                # MODIFY — wrap in MudTabs, add Notifications tab content

tests/
├── Sorcha.Tenant.Service.Tests/
│   └── PersonaServiceTests.cs                        # NEW
├── Sorcha.Tenant.Service.IntegrationTests/
│   └── PersonaEndpointsTests.cs                      # NEW
├── Sorcha.Wallet.Service.IntegrationTests/
│   └── PersonaCryptoEndpointsTests.cs                # NEW
├── Sorcha.Cryptography.Tests/
│   └── PersonaVaultDerivationTests.cs                # NEW (or folded into existing derivation tests file)
├── Sorcha.UI.Core.Tests/
│   ├── PersonaAutofillResolverTests.cs               # NEW
│   └── PersonaServiceContractTests.cs                # NEW — reflection-backed contract guard
└── Sorcha.UI.E2E.Tests/
    ├── PersonaAutofillTests.cs                       # NEW (Playwright)
    └── NavTidyTests.cs                               # NEW (Playwright)
```

**Structure Decision**: Web application using the existing Sorcha microservices layout. No new solution projects are added — every file lands in an existing project. Source files follow the existing folder conventions established by Features 083 (Org Key Derivation), 085 (Stored Data Transactions), and 091 (New Submissions Workspace). The EF schema change is folded into the existing Tenant Service initial setup migration per the pre-release squash rule (see `feedback_migration_squash` memory), not added as a new migration file.

## Phase 0 — Research

All NEEDS CLARIFICATION markers were resolved during `/speckit.clarify` and captured in the spec's Clarifications section. The remaining "research" for this plan is a short record of the design decisions and rejected alternatives that were settled during the earlier brainstorming session and which need to be visible to reviewers and downstream tasks. Output: [`research.md`](./research.md).

## Phase 1 — Design & Contracts

### Data model
See [`data-model.md`](./data-model.md). Contains: entity definitions, DTO shapes, multi-value invariants, cascade behaviour, and the single EF configuration update.

### API contracts
See [`contracts/tenant-persona-api.yaml`](./contracts/tenant-persona-api.yaml) and [`contracts/wallet-persona-crypto.yaml`](./contracts/wallet-persona-crypto.yaml). Both are OpenAPI 3.1 documents written against the Minimal API endpoint groups. The Tenant document describes the public `/me/persona` surface exposed through the API Gateway; the Wallet document describes the internal `/persona/encrypt|decrypt` pair which is **not** routed through the gateway.

### Quickstart
See [`quickstart.md`](./quickstart.md). Explains how to exercise the feature end-to-end against the Docker Compose stack: save a persona, open a form with matching fields, observe the cream-tinted autofill, toggle the global preference, and verify the nav tidy.

### Agent context update
The project's agent context file is `CLAUDE.md` at the repo root, which already contains API documentation sections following the pattern set by Features 079, 083, 085, and 091. A new "Consumer Persona API" section will be added as part of execution (Phase 2 tasks), keeping with the existing convention rather than running a separate update script — the `update-agent-context.ps1` script does not exist in this repo's `.specify/scripts/`. The plan captures this responsibility as a task for `/speckit.tasks` to schedule.

### Post-design constitution re-check

After generating the Phase 1 artifacts, re-checking against the constitution:

| Principle | Re-check |
|---|---|
| Microservices-First | Still passes — the data model confirms Tenant/Wallet boundary cleanly; no cross-boundary leakage. |
| Security First | Still passes — the API contracts make the `persona:crypto` scope explicit on Wallet endpoints; Tenant `/me/persona` requires user JWT; ciphertext never crosses the public gateway. |
| API Documentation | Still passes — OpenAPI contracts will be emitted from Minimal API metadata; Scalar renders them. |
| Testing Requirements | Still passes — tests listed in the spec map 1:1 to the new components and cover happy/unhappy paths. |
| Code Quality | Still passes — nullable enabled across all new files; async throughout. |
| Blueprint Standards | Still passes — `x-persona` is an author-tagged schema extension; no Fluent API. |
| DDD | Still passes — persona attaches to `PlatformUser`; "acting as" parameter reserves delegate surface without pollution. |
| Observability | Still passes — contracts specify write audit logging and OpenTelemetry trace propagation. |

**Result**: No new violations introduced by Phase 1 design.

## Complexity Tracking

No entries. All design decisions match constitution principles and existing platform patterns.
