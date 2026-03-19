# Implementation Plan: Participant Resolution, Starting Action Binding & Field-Level Encryption

**Branch**: `065-participant-encryption` | **Date**: 2026-03-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/065-participant-encryption/spec.md`

## Summary

Replace hardcoded wallet addresses in blueprint participants with dynamic resolution at action execution time. Starting actions accept any wallet (binds to participant role for instance lifetime). Organisational participants resolved from published records on the register. DevMode per-register setting allows plaintext payload storage with disclosure filtering. When DevMode is off, envelope encryption uses disclosure groups for field-level access control.

**Key insight from research**: Most infrastructure already exists. The `EncryptionPipelineService`, `DisclosureGroupBuilder`, `DisclosureProcessor`, and `ParticipantIndexService` are all implemented. The work is primarily wiring and validation logic changes, not new crypto implementation.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Cryptography (XChaCha20-Poly1305, multi-algorithm key wrapping), Sorcha.TransactionHandler (EncryptionPipelineService), Sorcha.ServiceClients (Register, Wallet, Blueprint)
**Storage**: MongoDB (registers, transactions), PostgreSQL (tenant/identity), Redis (caching, validator pools)
**Testing**: xUnit + FluentAssertions + Moq, Playwright E2E (council credential flow)
**Target Platform**: Docker (Linux containers), .NET Aspire orchestration
**Project Type**: Distributed microservices
**Performance Goals**: Starting action < 5s, organisational action < 10s, encrypted 20-recipient payload < 15s
**Constraints**: Atomic encryption (all-or-nothing), immutable instance bindings, 4MB payload size limit
**Scale/Scope**: 5 services modified, ~15 files changed, ~500 lines net new code

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes scoped to individual services. No new cross-service coupling. |
| II. Security First | PASS | Encryption enabled by default (DevMode must be explicitly opted into). Disclosure rules enforced in both modes. |
| III. API Documentation | PASS | New endpoint documented in contracts. Existing endpoints unchanged. |
| IV. Testing Requirements | PASS | E2E test (council credential flow) is the primary test vehicle. Unit tests for validation logic. |
| V. Code Quality | PASS | Extends existing patterns (ValidationEngine, ActionExecutionService). No new frameworks. |
| VI. Blueprint Standards | PASS | Blueprint JSON format preserved. WalletAddress becomes optional, not removed. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Participant, Disclosure, Action, Blueprint, Register. |
| VIII. Observability | PASS | Existing structured logging in validation and execution paths. |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/065-participant-encryption/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity changes and state transitions
├── quickstart.md        # Developer quickstart
├── contracts/
│   └── api-changes.md   # API contract changes
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (files to modify)

```text
src/
├── Common/
│   ├── Sorcha.Blueprint.Models/
│   │   └── Participant.cs                          # Make WalletAddress optional
│   └── Sorcha.Register.Models/
│       └── Register.cs                             # Add DevMode field
├── Services/
│   ├── Sorcha.Validator.Service/
│   │   └── Services/ValidationEngine.cs            # 3-tier participant resolution in VAL_BP_002
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Models/Instance.cs                      # Document binding behaviour (no code change)
│   │   └── Services/Implementation/
│   │       └── ActionExecutionService.cs           # Starting action binding + DevMode branch
│   └── Sorcha.Register.Service/
│       ├── Models/RegisterDocument.cs              # DevMode field in MongoDB document
│       └── Endpoints/RegisterEndpoints.cs          # DevMode toggle + participant resolve endpoint
tests/
├── Sorcha.Validator.Service.Tests/                 # Participant resolution unit tests
├── Sorcha.Blueprint.Service.Tests/                 # Starting action binding tests
└── Sorcha.UI.E2E.Tests/Docker/
    └── CouncilCredentialFlowTests.cs               # E2E integration test
```

**Structure Decision**: No new projects. All changes are within existing service boundaries. Follows microservices-first principle.

## Implementation Phases

### Phase A: Participant Resolution & Starting Action Binding (P1)

**Goal**: Any wallet can start a workflow. Organisational participants resolved from register.

1. Make `Participant.WalletAddress` optional in `Blueprint.Models`
2. Modify `ValidationEngine.ValidateBlueprintConformanceAsync` — 3-tier resolution:
   - Starting action: skip wallet validation, return success
   - Instance binding: check `instance.ParticipantWallets`
   - Register lookup: query `ParticipantIndexService` by participant name + org
3. Modify `ActionExecutionService.ExecuteAsync` — on starting action, bind sender wallet to `instance.ParticipantWallets[senderParticipantId]`
4. Add participant resolve endpoint to Register Service
5. Unit tests for validation logic, integration test with E2E flow

**Exit criteria**: Council credential E2E test passes through all 9 actions using the existing plaintext transaction path (DevMode is added in Phase B).

### Phase B: DevMode Per-Register (P2)

**Goal**: Registers can opt into plaintext storage with disclosure read-filtering.

1. Add `DevMode` field to `Register` model + MongoDB document
2. Add `devMode` parameter to register initiation endpoint
3. Add `PUT /api/registers/{registerId}/devmode` toggle endpoint
4. Modify `ActionExecutionService` — check register DevMode before encryption pipeline:
   - DevMode: use plaintext transaction builder
   - Non-DevMode: use encryption pipeline (existing path)
5. Ensure disclosure filtering works at read time for DevMode payloads
6. Update E2E test to create DevMode registers

**Exit criteria**: E2E test creates DevMode register, executes full workflow, payloads readable as plaintext in MongoDB.

### Phase C: Field-Level Encryption Integration (P3)

**Goal**: Non-DevMode registers encrypt payloads using disclosure groups.

1. Verify `EncryptionPipelineService` works with resolved recipient keys (from Phase A resolution)
2. Ensure `ResolveRecipientKeysAsync` uses both instance bindings and register participant records
3. End-to-end test: create non-DevMode register, execute action, verify encrypted storage
4. Verify decryption path: participant queries return only their disclosed fields
5. Test disclosure group optimisation (N recipients with K unique sets → K groups)

**Exit criteria**: Full encrypt/decrypt round-trip works. Disclosure groups optimise correctly. Size limits enforced.
