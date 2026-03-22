# Implementation Plan: Validator Consensus Security

**Branch**: `066-validator-consensus-security` | **Date**: 2026-03-22 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/066-validator-consensus-security/spec.md`

## Summary

Three connected security features addressing audit findings 4.5, 4.1, and 4.2. The Validator Service already has ~80% of the approval infrastructure (endpoints, status enum, consent mode). This plan extends the existing code with: (1) durable MongoDB persistence + suspend/revoke lifecycle + Admin UI, (2) cryptographic vote verification using existing `ICryptoModule`, and (3) per-wallet sequence numbers for replay protection.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: .NET Aspire 13, MudBlazor, StackExchange.Redis, MongoDB.Driver, Sorcha.Cryptography, Polly
**Storage**: MongoDB (durable validator registry + audit + wallet sequences), Redis (L1/L2 cache)
**Testing**: xUnit 3.2, FluentAssertions 8.8, Moq 4.20
**Target Platform**: Linux containers (Docker), Blazor WASM (Admin UI)
**Project Type**: Distributed microservices + Blazor WASM frontend
**Performance Goals**: Vote verification within 30s consensus timeout (<1ms per ED25519 signature), sequence lookup <10ms
**Constraints**: Zero downtime for validator state changes, fail-closed on sequence store unavailable
**Scale/Scope**: ~10 validators per register, ~10K transactions/day, 1 new Admin UI page

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes within Validator Service + Admin UI. No new services. |
| II. Security First | PASS | Core purpose of this feature. Zero trust for votes. |
| III. API Documentation | PASS | All new endpoints documented with OpenAPI/Scalar |
| IV. Testing Requirements | PASS | Target >85% on all new code |
| V. Code Quality | PASS | Async/await, DI, nullable enabled |
| VI. Blueprint Standards | N/A | No blueprint changes |
| VII. Domain-Driven Design | PASS | Using existing domain terms (Validator, Docket, Consensus) |
| VIII. Observability | PASS | Structured logging, audit trail, OpenTelemetry |

**Post-design re-check**: All gates still pass. No new projects created. MongoDB collections added to existing database.

## Project Structure

### Documentation (this feature)

```text
specs/066-validator-consensus-security/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity models and MongoDB collections
├── quickstart.md        # Implementation guide
├── contracts/           # API contracts
│   └── validator-management-api.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 task list (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Validator.Service/
├── Models/
│   ├── ConsensusVote.cs          # Extend with signature fields
│   ├── Transaction.cs            # Add SequenceNumber field
│   └── ValidatorAuditEntry.cs    # NEW: audit trail record
├── Services/
│   ├── Interfaces/
│   │   └── IValidatorRegistry.cs # Extend with suspend/revoke/audit
│   ├── ValidatorRegistry.cs      # MongoDB write-through + new operations
│   ├── ConsensusEngine.cs        # Vote signature verification
│   ├── SignatureCollector.cs      # Verify incoming vote signatures
│   └── ValidationEngine.cs       # Sequence number validation stage
├── Endpoints/
│   └── ValidatorRegistrationEndpoints.cs  # Add suspend/reactivate/revoke/audit/sequence
└── Configuration/
    └── ValidatorRegistryConfiguration.cs  # MongoDB connection config

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/
├── Pages/
│   ├── ValidatorManagement.razor  # NEW: list + actions page
│   └── ValidatorDetail.razor      # NEW: detail + audit history
└── Services/
    └── ValidatorAdminService.cs   # NEW: HTTP client for validator API

tests/Sorcha.Validator.Service.Tests/
├── Services/
│   ├── ValidatorRegistryTests.cs           # Extend with new state transitions
│   ├── ConsensusEngineTests.cs             # Vote verification tests
│   └── ValidationEngineSequenceTests.cs    # NEW: sequence validation tests
└── Endpoints/
    └── ValidatorRegistrationEndpointTests.cs  # NEW endpoint tests
```

**Structure Decision**: All changes fit within existing project boundaries. No new projects needed. The Admin UI pages go in the existing (empty) Admin.Client project. Validator Service extensions are purely additive.

## Complexity Tracking

No constitution violations. All changes extend existing services within established patterns.
