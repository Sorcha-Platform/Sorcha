# Implementation Plan: Validator Key Roster

**Branch**: `086-validator-key-roster` | **Date**: 2026-04-06 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/086-validator-key-roster/spec.md`

## Summary

Add a declared validator signing key roster to the register genesis control record, enabling remote peers to verify synced dockets. Validator keys are purpose-derived from the system wallet (derivation context: `"sorcha:docket-signing"`). The roster supports multiple validators (list from day one) with threshold signing parameters for future n-of-m enforcement. This unblocks cross-node register replication, which currently rejects 100% of synced dockets because the validator public key is inaccessible.

## Technical Context

**Language/Version**: C# 13 / .NET 10  
**Primary Dependencies**: Sorcha.Cryptography (key derivation), Sorcha.Register.Models (control record), Sorcha.Peer.Service (finalization)  
**Storage**: PostgreSQL (Peer Service subscriptions), MongoDB (Register Service dockets/transactions), Redis (advertisement cache)  
**Testing**: xUnit + FluentAssertions + Moq  
**Target Platform**: Linux containers (Docker)  
**Project Type**: Microservices (existing codebase extension)  
**Performance Goals**: No regression to register creation time (<5s)  
**Constraints**: Must not break existing governance quorum flow. Must support external validator roster for future System Register (087).  
**Scale/Scope**: 6 projects modified, ~15 files changed, ~500 lines new code + ~200 lines modified

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes span Register Service, Validator Service, Peer Service — each independently deployable. No new coupling. |
| II. Security First | PASS | Purpose-derived keys (not master), validator roster governance-controlled, signature verification on all finalized dockets. |
| III. API Documentation | PASS | No new public REST endpoints (uses existing governance pipeline). Modified models get XML docs. |
| IV. Testing Requirements | PASS | Unit tests for new models, validation, roster operations. Integration test for end-to-end sync verification. |
| V. Code Quality | PASS | Follows existing patterns (RegisterControlRecord, RegisterAttestation). Nullable types, async/await. |
| VI. Blueprint Creation | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing terminology (Register, Docket, Validator). New term "ValidatorRoster" follows ubiquitous language. |
| VIII. Observability | PASS | Structured logging for roster extraction, key verification, finalization accept/reject. |

No violations. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/086-validator-key-roster/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Research decisions
├── data-model.md        # Entity definitions
├── quickstart.md        # Test scenarios
├── contracts/           # API contract changes
│   └── governance-api.yaml
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (files to modify/create)

```text
src/Common/Sorcha.Register.Models/
├── RegisterControlRecord.cs          # MODIFY: add Validators field
├── ValidatorRoster.cs                # NEW: roster model + entries
└── ValidatorKeyStatus.cs             # NEW: Active/Rotated/Revoked enum

src/Services/Sorcha.Register.Service/
└── Services/
    └── RegisterCreationOrchestrator.cs  # MODIFY: populate validator roster in genesis

src/Services/Sorcha.Validator.Service/
└── Services/
    └── DocketBuilder.cs              # MODIFY: sign with derived key path

src/Services/Sorcha.Peer.Service/
├── Replication/
│   ├── ValidatorKeyCache.cs          # MODIFY: multi-key roster cache
│   └── DocketFinalizationService.cs  # MODIFY: verify against roster
└── Communication/
    └── RelayMessageHandler.cs        # ALREADY MODIFIED: Register Service fallback (current session)

src/Common/Sorcha.ServiceClients.Http/
└── Register/
    └── RegisterServiceClient.cs      # ALREADY MODIFIED: DocketModel tx stubs (current session)

tests/
├── Sorcha.Register.Models.Tests/
│   └── ValidatorRosterTests.cs       # NEW: roster model validation tests
├── Sorcha.Register.Service.Tests/
│   └── RegisterCreationValidatorRosterTests.cs  # NEW: genesis roster population tests
├── Sorcha.Validator.Service.Tests/
│   └── DocketBuilderDerivedKeyTests.cs  # NEW: derived key signing tests
└── Sorcha.Peer.Service.Tests/
    ├── ValidatorKeyCacheRosterTests.cs   # NEW: multi-key cache tests
    └── DocketFinalizationRosterTests.cs  # NEW: roster-based verification tests
```

**Structure Decision**: All changes extend existing projects. No new projects needed. The RegisterControlRecord model is the central integration point — changes there propagate to Register Service (genesis creation), Validator Service (signing), and Peer Service (verification).

## Phases

### Phase 1: Data Model + Signing (P1 foundation)
- ValidatorRoster, ValidatorRosterEntry, ValidatorKeyStatus models
- RegisterControlRecord extended with Validators field
- DocketBuilder signs with purpose-derived key
- Unit tests for models and validation

### Phase 2: Genesis Population (P1 core)
- RegisterCreationOrchestrator populates validator roster
- Auto-detect local validator + support external roster (FR-014)
- Genesis control transaction carries the roster
- Integration test: create register, inspect genesis

### Phase 3: Peer Verification (P1 completion)
- ValidatorKeyCache reads roster from genesis control record
- DocketFinalizationService verifies against roster
- End-to-end test: cross-node sync with signature verification

### Phase 4: Governance Operations (P2)
- Add/remove/rotate validator via governance proposals
- Roster version tracking
- Control transaction propagation to peers
- Tests for governance operations

### Phase 5: Threshold Schema (P3)
- RequiredSignatures field in roster
- Validation rules for threshold parameters
- Schema-only (no enforcement)
- Tests for threshold parameter validation
