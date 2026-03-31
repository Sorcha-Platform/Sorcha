# Implementation Plan: Register Sync Status Lifecycle & UI Improvements

**Branch**: `078-register-sync-status` | **Date**: 2026-03-31 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/078-register-sync-status/spec.md`

## Summary

Wire peer-to-peer register sync states to user-visible RegisterStatus, add real-time table updates replacing notification boxes, display encryption warnings for dev-mode registers with a one-way enable switch, and trigger immediate sync on subscription creation.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: MudBlazor (UI), SignalR (real-time), gRPC (peer sync)
**Storage**: PostgreSQL (tenant/blueprint), MongoDB (registers), Redis (cache/ads)
**Testing**: xUnit + FluentAssertions + Moq (>85% coverage)
**Target Platform**: Docker containers (Linux), Blazor WASM (browser)
**Project Type**: Distributed microservices (7 services + UI)
**Performance Goals**: Status transitions visible in UI within 3 seconds
**Constraints**: 30s offline debounce, one-way encryption enable
**Scale/Scope**: 4 user stories, ~9 files modified, backend + frontend

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Microservices-First | PASS | Changes scoped to Peer Service, Register Service, and UI — no cross-service coupling added |
| Security First | PASS | Encryption one-way enable strengthens security posture |
| API Documentation | PASS | New endpoint will have OpenAPI docs + XML comments |
| Testing Requirements | PASS | Will add unit tests for state machine transitions, integration tests for SignalR |
| Code Quality | PASS | Async patterns, DI, nullable types maintained |
| Observability | PASS | Status transitions logged, existing health endpoints unaffected |
| DDD | PASS | Uses existing domain terms (Register, Docket, Disclosure) |

No violations. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/078-register-sync-status/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # State machine and entity changes
├── quickstart.md        # Implementation guide
├── contracts/           # API contract changes
│   └── api-changes.md
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/Services/Sorcha.Peer.Service/
├── Replication/RegisterSyncBackgroundService.cs  # Immediate sync trigger + status reporting
├── Core/RegisterSyncState.cs                     # Existing enum (no changes)

src/Services/Sorcha.Register.Service/
├── Program.cs                                    # Subscription handler status mapping + disable-dev-mode endpoint
├── Services/RegisterEventBridgeService.cs        # Existing event bridge (no changes expected)

src/Core/Sorcha.Register.Core/
├── Managers/RegisterManager.cs                   # Prevent DevMode re-enable

src/Apps/Sorcha.UI/
├── Sorcha.UI.Web.Client/Pages/Registers/
│   ├── Index.razor                               # Placeholder entries + warning icons
│   └── Detail.razor                              # Remove notification boxes, auto-update tables
├── Sorcha.UI.Core/Components/Registers/
│   ├── RegisterCard.razor                        # DevMode warning icon
│   └── RegisterPolicyTab.razor                   # Encryption enable switch

tests/
├── Sorcha.Register.Core.Tests/                   # State transition tests
├── Sorcha.Peer.Service.Tests/                    # Sync trigger tests
└── Sorcha.UI.E2E.Tests/                          # Visual regression for new UI elements
```

**Structure Decision**: Changes span existing service boundaries (Peer Service, Register Service, UI). No new projects or services needed.
