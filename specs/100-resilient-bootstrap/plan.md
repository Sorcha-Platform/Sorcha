# Implementation Plan: Resilient System Register Bootstrap

**Branch**: `100-resilient-bootstrap` | **Date**: 2026-04-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/100-resilient-bootstrap/spec.md`

## Summary

The system register bootstrapper currently gives peer sync only 14 seconds before falling back to the embedded genesis, creating orphaned local networks on fresh nodes. This plan introduces a `BootstrapMode` configuration (`SyncOnly`, `GenesisFile`, `Auto`) that gives operators explicit control over bootstrap strategy. `SyncOnly` retries peer sync indefinitely with a two-phase backoff (fast 5s retries for 2 minutes, then 5-minute polling). `GenesisFile` ingests immediately. `Auto` preserves current behaviour for local dev.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: `Microsoft.Extensions.Options`, `Microsoft.Extensions.Hosting` (BackgroundService)
**Storage**: N/A (reads from local register store populated by Peer Service)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Linux containers (Docker), Windows dev
**Project Type**: Microservices (.NET Aspire)
**Performance Goals**: N/A — bootstrap is a one-time startup operation
**Constraints**: Must not regress local dev startup time (<30s). Must handle indefinite polling without memory leaks.
**Scale/Scope**: Changes to 2 source files, 1 config file, 1 test file. ~200 lines of new/modified code.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new inter-service coupling. Register Service bootstrapper checks local store only. |
| II. Security First | PASS | No new external boundaries. Genesis verification unchanged. |
| III. API Documentation | PASS | No new API endpoints. Configuration documented in contracts/README.md. |
| IV. Testing Requirements | PASS | Full test coverage planned for all three modes + edge cases. |
| V. Code Quality | PASS | Async/await, DI, nullable types, no warnings. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing domain terms (system register, genesis, bootstrap). |
| VIII. Observability | PASS | Structured logging with phase, attempt count, timing. |

**Post-design re-check**: All gates still PASS. No new dependencies, no new projects, no architectural changes.

## Project Structure

### Documentation (this feature)

```text
specs/100-resilient-bootstrap/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Configuration model and state machine
├── quickstart.md        # Implementation guide
├── contracts/           # Configuration contract (no API changes)
│   └── README.md
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/Common/Sorcha.ServiceDefaults/
└── SystemRegisterOptions.cs          # MODIFY: Add BootstrapMode enum + timing properties

src/Services/Sorcha.Register.Service/
├── Services/
│   └── SystemRegisterBootstrapper.cs # MODIFY: Mode-driven bootstrap with two-phase retry
└── appsettings.json                  # MODIFY: Add BootstrapMode and timing defaults

docker-compose.n1.yml                 # MODIFY: Set SyncOnly for production node

tests/Sorcha.Register.Service.Tests/
└── Services/
    └── SystemRegisterBootstrapperTests.cs  # CREATE: Tests for all modes
```

**Structure Decision**: No new projects or directories. All changes are modifications to existing files within the Register Service and ServiceDefaults. One new test file.

## Complexity Tracking

No violations. Changes are contained within 2 existing source files + config. No new projects, patterns, or abstractions introduced.
