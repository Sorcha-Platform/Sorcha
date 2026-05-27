# Implementation Plan: Register Governance

**Branch**: `031-register-governance` | **Date**: 2026-02-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/031-register-governance/spec.md`

## Summary

Implement decentralized register governance via a genesis blueprint that manages admin rosters using multi-sig quorum voting. The feature introduces a DID scheme (`did:sorcha:w:*` and `did:sorcha:r:*:t:*`), renames `TransactionType.Genesis` to `Control`, seeds a looping governance blueprint, and adds rights enforcement to the Validator. All governance state is stored on-register as Control transactions in normal dockets — any peer can independently verify admin rights.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: .NET Aspire 13+, MongoDB.Driver, StackExchange.Redis, FluentValidation, JsonSchema.Net
**Storage**: MongoDB (transactions/dockets), Redis (pending registrations/caching)
**Testing**: xUnit + FluentAssertions + Moq (>85% coverage target)
**Target Platform**: Linux containers (Docker) / Windows development
**Project Type**: Microservices (7 services, 39 source projects)
**Performance Goals**: Roster reconstruction <1s for 1,000 Control TXs; DID resolution <500ms
**Constraints**: Zero Tenant Service dependency for governance rights; deterministic roster across peers
**Scale/Scope**: Up to 25 roster members, 10,000 governance operations per register

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes span Register, Validator, Blueprint services — each independently deployable. New code goes in existing projects (no new service). |
| II. Security First | PASS | Multi-sig quorum, cryptographic attestations, signature verification. No secrets committed. Input validation on DID format and roster operations. |
| III. API Documentation | PASS | New governance endpoints documented with Scalar. XML docs on all public APIs. |
| IV. Testing Requirements | PASS | >85% target for new code. Unit tests for DID parsing, quorum calculation, roster reconstruction, rights enforcement. |
| V. Code Quality | PASS | C# 13, async/await, DI throughout. Nullable reference types enabled. |
| VI. Blueprint Creation Standards | PASS | Governance blueprint created as JSON template (`register-governance-v1.json`). |
| VII. Domain-Driven Design | PASS | Uses correct terms: Blueprint (not workflow), Action (not step), Participant (not user). |
| VIII. Observability by Default | PASS | Structured logging for governance operations. ActivitySource for roster reconstruction. |

**Post-Design Re-check**: All gates pass. No violations to track.

## Project Structure

### Documentation (this feature)

```text
specs/031-register-governance/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Entity models and relationships
├── quickstart.md        # Developer guide
├── contracts/           # API contracts
│   └── governance-api.md
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.Register.Models/
│   │   ├── Enums/TransactionType.cs           # MODIFY: Genesis→Control, remove System
│   │   ├── RegisterControlRecord.cs           # MODIFY: Add voting helpers, cap to 25
│   │   ├── SorchaDidIdentifier.cs             # NEW: DID value object
│   │   └── GovernanceModels.cs                # NEW: Operation, Payload, Enums
│   ├── Sorcha.Validator.Core/
│   │   └── Validators/ChainValidatorCore.cs   # MODIFY: Genesis→Control references
│   └── Sorcha.ServiceClients/
│       └── Register/IRegisterServiceClient.cs # MODIFY: Add GetControlTransactionsAsync
├── Core/
│   └── Sorcha.Register.Core/
│       └── Services/
│           ├── GovernanceRosterService.cs      # NEW: Roster reconstruction
│           └── DIDResolver.cs                  # NEW: DID resolution
├── Services/
│   ├── Sorcha.Register.Service/
│   │   ├── Services/
│   │   │   ├── RegisterCreationOrchestrator.cs # MODIFY: Use Control type
│   │   │   └── SystemRegisterService.cs        # MODIFY: Seed governance blueprint
│   │   └── Endpoints/
│   │       └── GovernanceEndpoints.cs          # NEW: Roster + history endpoints
│   ├── Sorcha.Validator.Service/
│   │   └── Services/
│   │       ├── ValidationEngine.cs             # MODIFY: Add rights enforcement stage
│   │       ├── GenesisManager.cs               # MODIFY: Handle Control type
│   │       ├── BlueprintVersionResolver.cs     # MODIFY: Genesis→Control
│   │       ├── ValidatorRegistry.cs            # MODIFY: System→Control
│   │       └── RightsEnforcementService.cs     # NEW: Governance rights validation
│   └── Sorcha.Blueprint.Service/               # MINIMAL: No changes expected
└── examples/
    └── templates/
        └── register-governance-v1.json         # NEW: Governance blueprint

tests/
├── Sorcha.Register.Models.Tests/               # DID parsing, quorum, validation rules
├── Sorcha.Register.Core.Tests/                 # Roster reconstruction, DID resolution
├── Sorcha.Validator.Service.Tests/             # Rights enforcement, Control TX validation
├── Sorcha.Validator.Core.Tests/                # Chain validation with Control type
└── Sorcha.Register.Service.Tests/              # Governance endpoints, genesis update
```

**Structure Decision**: All changes fit within existing projects. No new service or project creation required — new files are added to existing projects following established folder conventions.

## Complexity Tracking

No constitution violations — no entries needed.

## Implementation Phases

### Phase 1: Foundation — TransactionType + DID Model (Low Risk)

**Goal**: Rename enum, create DID value object, update governance models.

**Files Modified**:
- `TransactionType.cs` — Rename Genesis→Control (0), remove System (3)
- `RegisterControlRecord.cs` — Add `GetVotingMembers()`, `GetQuorumThreshold()`, increase cap to 25

**Files Created**:
- `SorchaDidIdentifier.cs` — Parse/validate/format both DID types
- `GovernanceModels.cs` — GovernanceOperationType, ProposalStatus, GovernanceOperation, ControlTransactionPayload

**Tests**: DID parsing (valid/invalid formats), quorum calculation (m=1 through m=10), roster validation rules.

**Risk**: Low — model-only changes, no runtime impact until integrated.

### Phase 2: Enum Migration — Update All References (Medium Risk)

**Goal**: Update all 6 source files + 2 test files that reference `TransactionType.Genesis` or `TransactionType.System`.

**Files Modified**:
- `RegisterCreationOrchestrator.cs` (line 559) — `Genesis` → `Control`
- `BlueprintVersionResolver.cs` (line 335) — `Genesis` → `Control`
- `ValidatorRegistry.cs` (lines 302, 480) — `System` → `Control`
- `ChainValidatorCore.cs` (lines 29, 140, 279, 304) — `Genesis` → `Control`
- `BlueprintVersionResolverTests.cs` (line 509) — `System` → `Control`
- `ChainValidatorCoreTests.cs` (6 locations) — `Genesis` → `Control`

**Tests**: Ensure all existing tests pass with renamed enum. No behavioral change.

**Risk**: Medium — widespread rename but integer values preserved (0 stays 0). Compile-time errors catch missed references.

### Phase 3: DID Resolution Service (Medium Risk)

> **Historical (Feature 120 Phase 0, 2026-05-09):** Superseded by the W3C-shaped `IDidResolverRegistry` (`src/Common/Sorcha.ServiceClients.Http/Did/`). The Sorcha-specific `IDIDResolver` / `DIDResolver` types described below were deleted in Phase 0 of Feature 120 — they had no consumers in src/ or tests/.

**Goal**: Implement DID resolver for both wallet and register DID types.

**Files Created**:
- `IDIDResolver.cs` — Interface with `ResolveAsync(string did)` returning public key + algorithm
- `DIDResolver.cs` — Implementation using `IWalletServiceClient` for `w:` and `IRegisterServiceClient` for `r:t:`

**Dependencies**: `IWalletServiceClient.GetWalletAsync()`, `IRegisterServiceClient.GetTransactionAsync()`

**Tests**: Mock wallet/register clients, test both DID type resolution, test invalid DID handling, test unreachable register fallback.

**Risk**: Medium — depends on existing service clients working correctly.

### Phase 4: Roster Reconstruction Service (Medium Risk)

**Goal**: Build service that derives current admin roster from Control transaction chain.

**Files Created**:
- `IGovernanceRosterService.cs` — Interface
- `GovernanceRosterService.cs` — Filters Control TXs, returns latest roster

**Files Modified**:
- `IRegisterServiceClient.cs` — Add `GetControlTransactionsAsync(registerId)` method
- `RegisterServiceClient.cs` — Implement method

**Register Service Endpoint** (new):
- `GET /api/registers/{registerId}/transactions?type=Control` — filtered transaction query

**Tests**: Roster from single genesis TX, roster after add/remove/transfer operations, determinism verification (same input → same roster).

**Risk**: Medium — new service client method, but follows existing patterns.

### Phase 5: Governance Blueprint + System Register Seeding (Low Risk)

**Goal**: Create the governance blueprint JSON and seed it on startup.

**Files Created**:
- `examples/templates/register-governance-v1.json` — 5-action looping blueprint

**Files Modified**:
- `SystemRegisterService.cs` — Add `register-governance-v1` to seeded blueprints

**Tests**: Blueprint validation (passes existing validation), cycle detection produces warning (expected), seeding is idempotent.

**Risk**: Low — follows existing `register-creation-v1` pattern exactly.

### Phase 6: Rights Enforcement in Validator (High Risk)

**Goal**: Add governance rights checking to the validation pipeline.

**Files Created**:
- `IRightsEnforcementService.cs` — Interface
- `RightsEnforcementService.cs` — Reconstructs roster, validates submitter role

**Files Modified**:
- `ValidationEngine.cs` — Insert rights check between structure and schema validation stages
- `ValidationEngine.cs` — For Control TXs: verify submitter is admin, verify quorum met
- `ValidationEngine.cs` — Add configuration flag `EnableGovernanceValidation`

**Tests**: Non-admin rejected, admin accepted, removed-admin rejected, Owner bypass, quorum threshold verification, Transfer requires Owner.

**Risk**: High — modifies the critical validation pipeline. Must not break existing transaction validation.

### Phase 7: Genesis Flow Update (Medium Risk)

**Goal**: Update register creation to use Control type and bind governance blueprint.

**Files Modified**:
- `RegisterCreationOrchestrator.cs` — Genesis TX uses `TransactionType.Control`, DID format for subjects
- `RegisterCreationOrchestrator.cs` — Bind governance blueprint instance to new register
- `GenesisManager.cs` — Handle Control type (previously only Genesis)

**Tests**: Create register → verify Control type, verify DID format in attestation subjects, verify governance blueprint bound.

**Risk**: Medium — changes the register creation flow but preserves the two-phase attestation model.

### Phase 8: Governance API Endpoints (Low Risk)

**Goal**: Add roster and history endpoints to Register Service.

**Files Created**:
- `GovernanceEndpoints.cs` — `GET /roster`, `GET /governance/history`

**Files Modified**:
- `Program.cs` (Register Service) — Map new endpoints

**Tests**: Roster endpoint returns correct data, history endpoint pagination, authorization checks.

**Risk**: Low — read-only endpoints, no state mutation.

### Phase 9: Integration Testing + Documentation (Low Risk)

**Goal**: End-to-end governance flow tests and documentation updates.

**Tests**:
- Full governance cycle: create register → add admin → remove admin → transfer ownership
- Peer roster reconstruction matches local roster
- Proposal timeout (7-day expiry)

**Documentation**:
- Update CLAUDE.md with governance patterns
- Update MASTER-TASKS.md
- Update development-status.md

**Risk**: Low — verification phase, no new production code.

## Dependencies Between Phases

```
Phase 1 (Foundation) ─┬─► Phase 2 (Enum Migration)
                      │
                      └─► Phase 3 (DID Resolution)
                              │
Phase 4 (Roster) ◄───────────┘
     │
     ├─► Phase 5 (Blueprint Seeding)
     │
     ├─► Phase 6 (Rights Enforcement) ◄── Phase 2
     │
     └─► Phase 7 (Genesis Update) ◄────── Phase 2, Phase 5
              │
              └─► Phase 8 (API Endpoints)
                       │
                       └─► Phase 9 (Integration + Docs)
```

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Enum rename breaks deserialization | Integer values preserved (0→0); only name changes |
| Validation pipeline regression | Configuration flag `EnableGovernanceValidation` for gradual rollout |
| Existing tests break | Phase 2 is isolated rename — compile errors catch all missed references |
| Roster reconstruction divergence | Determinism tests with fixed Control TX sequences |
| Performance regression | Roster caching after first reconstruction per register |
