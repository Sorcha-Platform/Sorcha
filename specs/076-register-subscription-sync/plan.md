# Implementation Plan: Register Subscription Sync Pipeline

**Branch**: `076-register-subscription-sync` | **Date**: 2026-03-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/076-register-subscription-sync/spec.md`

## Summary

Fix the broken register subscription flow where subscribing to a remote register succeeds at the Tenant Service level but the register never appears in the UI because the data doesn't exist locally. The solution adds a notification pipeline from Tenant Service → Register Service → Peer Service, creating a stub register immediately and orchestrating peer replication to fill it with real data. SignalR events keep the UI updated in real time.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: .NET Aspire 13+, YARP, SignalR, MongoDB, PostgreSQL (EF Core)
**Storage**: MongoDB (registers), PostgreSQL (subscriptions), Redis (events/SignalR backplane)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Linux containers (Docker), Windows dev
**Project Type**: Distributed microservices (7 services)
**Performance Goals**: Stub register visible in UI within 3 seconds of subscribe click
**Constraints**: Fire-and-forget notification — subscription must persist even if Register Service is down
**Scale/Scope**: 3 services modified (Tenant, Register, Peer clients), 2 shared libraries, 1 UI project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Each service retains its responsibility. Register Service orchestrates sync (its domain). Tenant Service only sends a notification. No upward dependencies. |
| II. Security First | PASS | Internal endpoint uses AllowAnonymous following existing `/api/internal/*` pattern (network-level isolation). No new external attack surface. |
| III. API Documentation | PASS | New internal endpoint will have XML docs and OpenAPI metadata (ExcludeFromDescription for internal). |
| IV. Testing Requirements | PASS | Unit tests for all new methods. Integration test for full flow. E2E test for UI. |
| V. Code Quality | PASS | Async/await throughout. DI for all new services. Nullable types enabled. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses existing ubiquitous language. SyncState is a register lifecycle concern. |
| VIII. Observability | PASS | Structured logging on all new paths. SignalR events for real-time visibility. |

**Post-design re-check**: All gates still pass. No new projects created. No new abstractions — extending existing patterns (event bridge, service clients, internal endpoints).

## Project Structure

### Documentation (this feature)

```text
specs/076-register-subscription-sync/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research decisions
├── data-model.md        # Entity changes and state transitions
├── quickstart.md        # Implementation overview and key files
├── contracts/           # API contracts
│   ├── register-internal-api.yaml
│   └── peer-subscribe-client.yaml
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (modified files — no new projects)

```text
src/
├── Common/
│   ├── Sorcha.Register.Models/
│   │   └── Register.cs                          # Add SyncState field
│   └── Sorcha.ServiceClients/
│       ├── Peer/
│       │   ├── IPeerServiceClient.cs             # Add subscribe/unsubscribe methods
│       │   └── PeerServiceClient.cs              # Implement subscribe/unsubscribe
│       └── Register/
│           ├── IRegisterServiceClient.cs         # Add NotifySubscriptionAsync
│           └── RegisterServiceClient.cs          # Implement notification call
├── Core/
│   └── Sorcha.Register.Core/
│       └── Managers/RegisterManager.cs           # Add UpdateSyncStateAsync method
├── Services/
│   ├── Sorcha.Register.Service/
│   │   ├── Program.cs                            # New internal endpoint
│   │   ├── Services/RegisterEventBridgeService.cs # Handle sync state events
│   │   └── Events/RegisterEvents.cs              # New RegisterSyncStateChangedEvent
│   └── Sorcha.Tenant.Service/
│       └── Services/RegisterSubscriptionService.cs # Add notification after subscribe/unsubscribe
└── Apps/
    └── Sorcha.UI/
        └── Sorcha.UI.Core/
            ├── Models/Registers/RegisterViewModel.cs     # Add SyncState
            ├── Services/RegisterHubConnection.cs          # Add sync state event handler
            ├── Services/RegisterService.cs                # Map SyncState in MapToViewModel
            ├── Services/RegisterSubscriptionService.cs    # Pass register name on subscribe
            └── Components/Registers/
                ├── SubscribeDialog.razor                  # Pass register name
                └── (Index.razor — Registers page)         # Show sync indicator

tests/
├── Sorcha.Register.Core.Tests/                   # UpdateSyncStateAsync tests
├── Sorcha.Register.Service.Tests/                # Internal endpoint tests
├── Sorcha.Tenant.Service.Tests/                  # Notification fire-and-forget tests
└── Sorcha.ServiceClients.Tests/                  # New client method tests
```

**Structure Decision**: No new projects. All changes extend existing files and patterns. This is a cross-cutting integration feature connecting 3 existing services via established client patterns.

## Complexity Tracking

No constitution violations. No new projects, abstractions, or patterns introduced.
