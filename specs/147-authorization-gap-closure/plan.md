# Implementation Plan: Authorization-gap closure

**Branch**: `147-authorization-gap-closure` | **Date**: 2026-06-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/147-authorization-gap-closure/spec.md`
**Design source**: `docs/superpowers/specs/2026-06-03-authorization-gap-closure-design.md`

## Summary

Close four authorization gaps by enforcing the correct trust tier / role at each operation rather than at the perimeter or via a comment, and by moving an omittable rule into its shared definition:

1. **H1** — `Sorcha.Wallet.Service` system-wallet `create`/`recover`: drop `.AllowAnonymous()`; gate `create` → `RequireService`; gate `recover` → new `CanRecoverSystemWallet` (`:service` OR `Administrator`+`:platform`); keep the existing 409-on-exists guard.
2. **H2** — `Sorcha.Blueprint.Service` `CanManageBlueprints`: redefine via a tier-aware requirement+handler so it admits *(service+`:service`)* OR *(org_id+`:platform`)* — fixing all bare sites with no per-endpoint edits.
3. **F124** — `Sorcha.Wallet.Service` pending-applications group: plain `.RequireAuthorization()` → `RequireConsumerAudience`.
4. **LOW** — `Sorcha.Tenant.Service` `AddTenantAuthorization`: delete the duplicate role-only `RequireSystemAdmin` so the shared org-scoped definition stands.

Technical approach is grounded in the existing F136 authorization primitives (`SorchaAudiences`, `TierAudienceRequirement`/`TierAudienceAuthorizationHandler`, `AuthorizationPolicies`) and the existing test pattern (`AuthorizationPolicyExtensionsTests` policy-evaluation harness).

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core authorization (`Microsoft.AspNetCore.Authorization`), `Sorcha.ServiceDefaults.Auth` (F136 `SorchaAudiences`, tier requirement/handler), `Sorcha.ServiceClients.Auth` (`TokenClaimConstants`)
**Storage**: N/A (authorization configuration only; no schema/data changes)
**Testing**: xUnit + FluentAssertions; policy-evaluation via `IAuthorizationService.AuthorizeAsync`; runner is Microsoft.Testing.Platform (`--filter` ignored — whole-project runs)
**Target Platform**: Linux server containers (3 services: Wallet, Blueprint, Tenant)
**Project Type**: web (multi-service)
**Performance Goals**: No measurable change — authorization handlers are claim inspections on the request path
**Constraints**: No behaviour change for legitimate callers (FR-004, FR-007); no new public endpoints; audience strings resolved from the single source of truth at request time (FR-012)
**Scale/Scope**: 3 services, 1 shared pattern reused, ~2 new requirement+handler pairs, 1 policy redefinition, 2 endpoint-group edits, 1 deletion

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|-----------|------------|
| II. Security First (zero trust, authz) | **Directly advances** — closes four authz gaps; enforces tier/role in-code at every boundary. No violation. |
| IV. Testing (>85% new code, xUnit, deterministic, AAA) | Met — TDD: policy-evaluation tests (full `IAuthorizationService` pipeline) + endpoint-metadata regression tests, deterministic, no external deps. |
| V. Code Quality (nullable, no warnings, DI) | Met — new requirement/handler types are nullable-clean, DI-registered, XML-documented. |
| III. API Documentation | Met — no endpoint summaries/descriptions removed; new handler types carry XML docs. |
| I. Microservices-First (no upward deps) | Met — all changes service-local (Wallet/Blueprint/Tenant); new types live in their own service; reuse the existing downward `ServiceDefaults.Auth` primitives. |
| VIII. Observability | No new telemetry required; authz failures surface as standard 401/403 and the existing F136 `IdentityMetrics` tier-rejection counters already cover audience mismatches. No violation. |

**Result: PASS — no violations, no Complexity Tracking entries required.**

## Project Structure

### Documentation (this feature)

```text
specs/147-authorization-gap-closure/
├── plan.md              # This file
├── spec.md              # Feature spec
├── research.md          # Phase 0 — decisions & rationale
├── data-model.md        # Phase 1 — authorization model (policies, requirements, handlers)
├── quickstart.md        # Phase 1 — how to verify the allow/deny matrix
├── contracts/
│   └── authorization-matrix.md   # Phase 1 — per-operation allow/deny contract (no new HTTP endpoints)
└── checklists/
    └── requirements.md  # Spec quality checklist (done)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Wallet.Service/
├── Authorization/                         # NEW folder
│   ├── SystemWalletRecoveryRequirement.cs # NEW — IAuthorizationRequirement
│   └── SystemWalletRecoveryAuthorizationHandler.cs  # NEW — injects SorchaAudiences
├── Extensions/AuthenticationExtensions.cs # EDIT — register CanRecoverSystemWallet + handler
└── Endpoints/
    ├── WalletEndpoints.cs                  # EDIT — drop AllowAnonymous; RequireService + CanRecoverSystemWallet
    └── PendingApplicationEndpoints.cs      # EDIT — RequireConsumerAudience

src/Services/Sorcha.Blueprint.Service/
├── Authorization/                          # NEW folder
│   ├── BlueprintManagementRequirement.cs   # NEW — IAuthorizationRequirement
│   └── BlueprintManagementAuthorizationHandler.cs   # NEW — injects SorchaAudiences
└── Extensions/AuthenticationExtensions.cs  # EDIT — redefine CanManageBlueprints via requirement + register handler

src/Services/Sorcha.Tenant.Service/
└── Extensions/AuthenticationExtensions.cs  # EDIT — delete duplicate role-only RequireSystemAdmin

tests/
├── Sorcha.Wallet.Service.Tests/
│   ├── Authorization/WalletAuthorizationPolicyTests.cs   # NEW — CanRecoverSystemWallet matrix
│   └── Endpoints/SystemWalletEndpointAuthorizationTests.cs # NEW — metadata: no AllowAnonymous + required policy; pending-apps consumer
├── Sorcha.Blueprint.Service.Tests/
│   └── Authorization/BlueprintManagementPolicyTests.cs   # NEW — CanManageBlueprints matrix
└── Sorcha.Tenant.Service.Tests/
    └── Authorization/TenantSystemAdminPolicyTests.cs     # NEW — RequireSystemAdmin org-scoping
```

**Structure Decision**: Multi-service. Each service gets a small `Authorization/` folder for its custom requirement+handler (separation from `Extensions/` config wiring), registered in the existing `Add{Service}Authorization` method. Tests mirror the proven `AuthorizationPolicyExtensionsTests` policy-evaluation harness, placed in an `Authorization/` test folder per service; endpoint-metadata regression tests live under `Endpoints/`.

## Complexity Tracking

No constitution violations — section intentionally empty.
