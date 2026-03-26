# Implementation Plan: Blueprint Service Ledger Recovery & Register Status Sync

**Branch**: `070-ledger-recovery` | **Date**: 2026-03-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/070-ledger-recovery/spec.md`

## Summary

Blueprint Service's published blueprint index is volatile (in-memory). On restart, users see no available blueprints. Fix: recover published state from the register ledger on startup, gate the health check during recovery, and periodically refresh register status and blueprint discovery during runtime.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Blueprint.Service, Sorcha.Register.Service, Sorcha.ServiceClients
**Storage**: MongoDB (register ledger — source of truth), In-memory (published blueprint cache)
**Testing**: xUnit, FluentAssertions, Moq
**Target Platform**: Docker containers
**Project Type**: Distributed microservices
**Performance Goals**: Recovery completes in <30s for 10 registers / 100 blueprints; periodic refresh <5s
**Constraints**: No new persistent storage; ledger is the only source of truth
**Scale/Scope**: 2 services modified, ~6 new files, ~500 lines new code + tests

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes within existing service boundaries. Register Service adds a query endpoint; Blueprint Service adds a hosted service. No new services. |
| II. Security First | PASS | Recovery uses existing authenticated service clients. No new attack surface. |
| III. API Documentation | PASS | New endpoint documented with OpenAPI. Health check response documented. |
| IV. Testing Requirements | PASS | Unit tests for recovery logic, integration test for restart cycle. |
| V. Code Quality | PASS | Async patterns, DI, hosted service pattern. |
| VI. Blueprint Creation Standards | PASS | No changes to blueprint format. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Register, Blueprint, Publish. |
| VIII. Observability | PASS | Structured logging for recovery progress. Health check reports recovery metrics. |

## Project Structure

### Documentation (this feature)

```text
specs/070-ledger-recovery/
├── plan.md              # This file
├── spec.md              # Feature specification
├── spec-notes.md        # Initial discovery notes
├── research.md          # Technical decisions
├── data-model.md        # Recovery state entities
├── quickstart.md        # Implementation guide
└── contracts/
    └── register-published-blueprints.md  # API contracts
```

### Source Code (repository root)

```text
src/
├── Services/
│   ├── Sorcha.Register.Service/
│   │   └── Endpoints/
│   │       └── RegisterEndpoints.cs          # Add GET .../blueprints/published
│   └── Sorcha.Blueprint.Service/
│       ├── Services/
│       │   └── Implementation/
│       │       └── BlueprintRecoveryService.cs  # NEW: BackgroundService for recovery + refresh
│       ├── Models/
│       │   ├── RegisterHealthStatus.cs          # NEW: Online/Offline/Degraded enum
│       │   └── RecoveryState.cs                 # NEW: Recovery progress tracker
│       └── Program.cs                           # Update health check gating

tests/
├── Sorcha.Blueprint.Service.Tests/
│   └── BlueprintRecoveryServiceTests.cs      # NEW: Recovery logic tests
└── Sorcha.Register.Service.Tests/
    └── PublishedBlueprintsEndpointTests.cs    # NEW: Endpoint tests
```

**Structure Decision**: All changes fit within existing projects. One new hosted service in Blueprint Service, one new endpoint in Register Service, supporting models and tests.

## Complexity Tracking

No constitution violations. Pattern precedents:
- Hosted service: same pattern as `EncryptionBackgroundService`, `SystemRegisterBootstrapper`
- Register query endpoint: same pattern as existing `GetTransactionsByBlueprintAsync`
- Health check gating: same pattern as Aspire health check dependency chain
