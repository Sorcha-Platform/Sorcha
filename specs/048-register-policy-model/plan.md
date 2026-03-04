# Implementation Plan: Unified Register Policy Model & System Register

**Branch**: `048-register-policy-model` | **Date**: 2026-03-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/048-register-policy-model/spec.md`

## Summary

Consolidate scattered per-register policy (validator config, consensus params, leader election, governance rules) into a unified `RegisterPolicy` model embedded in Control records. The model lives on `RegisterControlRecord` alongside `CryptoPolicy`, is set at genesis, and updated via `control.policy.update` governance transactions. Additionally, bootstrap a System Register with deterministic ID for hosting system blueprints, upgrade the approved validator list to on-chain storage with Redis TTL for operational presence, and parameterize governance quorum formulas.

**Technical approach**: Extend existing `RegisterControlRecord` with a new nullable `RegisterPolicy` property (backward-compatible). Refactor `GenesisConfigService` to read from the new model when present, falling back to legacy parsing. Add `control.policy.update` to `ControlDocketProcessor`. Upgrade `SystemRegisterService` from MongoDB-only to a real on-chain register with deterministic ID. Wire approved validator list checks into `ValidatorRegistry.RegisterAsync()`.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: .NET Aspire 13, FluentValidation 11.10, JsonSchema.Net 7.4, Redis (StackExchange.Redis), MongoDB.Driver
**Storage**: MongoDB (registers, transactions, system register), Redis (validator operational presence, pending registrations, genesis config cache)
**Testing**: xUnit + FluentAssertions + Moq (1,100+ existing tests across 30 projects)
**Target Platform**: Linux containers (Docker), .NET Aspire orchestration
**Project Type**: Distributed microservices (Register Service, Validator Service, Wallet Service, Tenant Service)
**Performance Goals**: Policy reads cached in L1 (5-min TTL) and L2 (30-min Redis TTL). Policy updates visible to all services within 30 seconds of Control TX commit (SC-006).
**Constraints**: Backward-compatible with all existing registers (no migration). `RegisterPolicy` nullable on `RegisterControlRecord`. Genesis config fallback chain preserved.
**Scale/Scope**: Touches 3 services (Register, Validator, Wallet), 4 model projects, ~15 files modified, ~10 new files. Approved validator list capped at 100 entries.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes are isolated per service. RegisterPolicy model in shared `Register.Models`. No new cross-service coupling — services read policy from Control TX chain. |
| II. Security First | PASS | Policy updates require governance quorum. Validator approval uses on-chain DID + public key verification. No secrets in policy model. Input validation via DataAnnotations + FluentValidation on RegisterPolicy. |
| III. API Documentation | PASS | All new endpoints will have `.WithSummary()` / `.WithDescription()`. XML docs on all public types. Scalar UI for new policy endpoints. |
| IV. Testing Requirements | PASS | Target >85% coverage on new code. Unit tests for RegisterPolicy model validation, GenesisConfigService fallback, ControlDocketProcessor policy update handling, SystemRegister bootstrap. Integration tests for creation-with-policy and policy-update-via-governance flows. |
| V. Code Quality | PASS | Async/await for all I/O. DI throughout. Nullable reference types enabled. No compiler warnings. |
| VI. Blueprint Creation Standards | PASS | System blueprints stored as JSON documents on the System Register. No Fluent API for blueprint creation. |
| VII. Domain-Driven Design | PASS | Uses Sorcha ubiquitous language: Register, Blueprint, Participant, Disclosure. RegisterPolicy is a value object on the RegisterControlRecord aggregate. |
| VIII. Observability by Default | PASS | Structured logging for policy reads/updates/fallbacks. OpenTelemetry traces for genesis config resolution. Health check unaffected. |

**Gate Result: ALL PASS** — Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/048-register-policy-model/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── register-policy-endpoints.yaml
│   └── system-register-endpoints.yaml
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/Common/Sorcha.Register.Models/
├── RegisterPolicy.cs                    # NEW: Unified policy model (4 sections)
├── RegisterControlRecord.cs             # MODIFY: Add RegisterPolicy? property
├── GovernanceModels.cs                  # MODIFY: Add QuorumFormula enum, PolicyUpdatePayload
└── Constants/
    └── SystemRegisterConstants.cs       # NEW: Deterministic ID, well-known values

src/Core/Sorcha.Register.Core/
├── Services/
│   ├── GovernanceRosterService.cs       # MODIFY: Parameterize quorum by formula
│   └── RegisterPolicyService.cs         # NEW: Policy resolution from control chain
└── Validation/
    └── RegisterPolicyValidator.cs       # NEW: FluentValidation for RegisterPolicy

src/Services/Sorcha.Register.Service/
├── Services/
│   ├── RegisterCreationOrchestrator.cs  # MODIFY: Accept policy in creation request
│   ├── SystemRegisterService.cs         # MODIFY: Upgrade to on-chain register
│   └── SystemRegisterBootstrapper.cs    # NEW: IHostedService for env-flag bootstrap
├── Endpoints/
│   └── SystemRegisterEndpoints.cs       # NEW: Policy query endpoints
└── Configuration/
    └── SystemRegisterConfiguration.cs   # NEW: Env var binding

src/Services/Sorcha.Validator.Service/
├── Services/
│   ├── GenesisConfigService.cs          # MODIFY: Read from RegisterPolicy, fallback
│   ├── ControlDocketProcessor.cs        # MODIFY: Add control.policy.update handler
│   └── ValidatorRegistry.cs             # MODIFY: Check approved list for consent mode
└── Configuration/
    └── ValidatorRegistryConfiguration.cs # MODIFY: Add operational TTL config

src/Common/Sorcha.ServiceClients/
└── Register/
    ├── IRegisterServiceClient.cs        # MODIFY: Add policy query methods
    └── RegisterServiceClient.cs         # MODIFY: Implement policy query methods

tests/
├── Sorcha.Register.Models.Tests/
│   └── RegisterPolicyTests.cs           # NEW: Model validation tests
├── Sorcha.Register.Core.Tests/
│   └── RegisterPolicyServiceTests.cs    # NEW: Policy resolution tests
├── Sorcha.Register.Service.Tests/
│   ├── SystemRegisterBootstrapTests.cs  # NEW: Bootstrap idempotency tests
│   └── RegisterCreationPolicyTests.cs   # NEW: Creation-with-policy tests
└── Sorcha.Validator.Service.Tests/
    ├── GenesisConfigFallbackTests.cs    # NEW: Fallback chain tests
    └── PolicyUpdateProcessorTests.cs    # NEW: control.policy.update tests
```

**Structure Decision**: Extends existing microservice structure. No new projects — `RegisterPolicy` model added to `Sorcha.Register.Models`, policy service to `Sorcha.Register.Core`, endpoint/bootstrap to `Sorcha.Register.Service`. This follows the established pattern where `CryptoPolicy` was added.

## Complexity Tracking

> No constitution violations to justify. All changes fit within existing architectural patterns.
