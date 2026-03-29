# Implementation Plan: FLE Completion & Crypto Progress UX

**Branch**: `075-fle-crypto-progress-ux` | **Date**: 2026-03-29 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/075-fle-crypto-progress-ux/spec.md`

## Summary

Complete field-level encryption implementation by closing test gaps from spec 065 (DevMode and FLE unit tests, Docker E2E), enhance the encryption pipeline to emit per-recipient SignalR progress events, and build a floating popover UI component that gives users task-oriented feedback during long-running encryption operations. The popover supports expanded, minimised, and dismissed states with navigation persistence.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: MudBlazor 8.x, SignalR, BouncyCastle.Cryptography 2.6.2, xUnit, FluentAssertions, Moq
**Storage**: MongoDB (encrypted payloads), Redis (SignalR backplane), In-memory (operation store)
**Testing**: xUnit + FluentAssertions + Moq, WebApplicationFactory for endpoint tests, Playwright for E2E
**Target Platform**: Blazor WebAssembly (client), .NET 10 microservices (server)
**Project Type**: Web (microservices + Blazor WASM frontend)
**Performance Goals**: Per-recipient events within 1 second of key wrapping, 3-recipient encryption under 2 seconds total
**Constraints**: Existing SignalR infrastructure (no new hubs), MudBlazor component library, scoped DI lifetime convention
**Scale/Scope**: 3 services modified (Blueprint, Register, TransactionHandler), 1 new UI component, ~5 new/modified test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | **PASS** | Changes scoped to existing services. No new services. Dependencies flow downward. |
| II. Security First | **PASS** | Encryption pipeline unchanged. No secrets exposed. Per-recipient events contain display names and field paths only (no key material). |
| III. API Documentation | **PASS** | New/modified endpoints require WithSummary/WithDescription. XML docs on new public types. |
| IV. Testing Requirements | **PASS** | This feature *adds* test coverage (closing spec 065 gaps). >85% target for new code. |
| V. Code Quality | **PASS** | Async/await, DI, nullable enabled, C# 13. |
| VI. Blueprint Standards | **N/A** | No blueprint changes. |
| VII. Domain-Driven Design | **PASS** | Using ubiquitous language: Participant, Disclosure, Action. UI uses task-oriented language per spec. |
| VIII. Observability | **PASS** | Existing OpenTelemetry traces in EncryptionPipelineService. Per-recipient events add observability. |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/075-fle-crypto-progress-ux/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity definitions
├── quickstart.md        # Validation scenarios
├── contracts/
│   ├── signalr-recipient-events.yaml    # New SignalR event contract
│   └── operations-api-changes.yaml      # Polling endpoint enhancement
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
# Backend changes
src/Common/Sorcha.TransactionHandler/Encryption/
├── Models/EncryptionModels.cs           # Add RecipientProgress, DisplayName to RecipientInfo
└── EncryptionPipelineService.cs         # Populate RecipientProgress[] in result

src/Services/Sorcha.Blueprint.Service/
├── Models/
│   ├── EncryptionOperationModels.cs     # Add RecipientOperationStatus[]
│   └── EncryptionNotifications.cs       # Add RecipientEncryptionNotification
├── Services/Implementation/
│   ├── EncryptionBackgroundService.cs   # Emit per-recipient events
│   ├── NotificationService.cs           # Add NotifyRecipientProgressAsync
│   └── InMemoryEncryptionOperationStore.cs  # Track per-recipient state
└── Hubs/ActionsHub.cs                   # No changes (events sent via IHubContext)

# UI changes
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Components/
│   └── Encryption/
│       └── CryptoProgressPopover.razor  # New: floating popover (expanded/minimised/dismissed)
├── Models/
│   └── Encryption/
│       └── EncryptionOperationState.cs  # New: client-side operation tracking model
├── Services/
│   └── EncryptionOperationTracker.cs    # New: global scoped service
└── Extensions/
    └── ServiceCollectionExtensions.cs   # Register tracker

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
└── Components/Layout/MainLayout.razor   # Add <CryptoProgressPopover />

# Tests
tests/Sorcha.Register.Service.Tests/Endpoints/
├── RegisterInitiateDevModeTests.cs      # New: T022
└── RegisterDevModeToggleTests.cs        # New: T023

tests/Sorcha.Blueprint.Service.Tests/Services/
├── ActionExecutionDevModeTests.cs       # New: T024
├── ActionExecutionEncryptionTests.cs    # New: T033
└── EncryptionNotificationTests.cs       # Enhanced: GAP-005

tests/Sorcha.TransactionHandler.Tests/Encryption/
└── DisclosureGroupEncryptionTests.cs    # New: T032

tests/Sorcha.UI.E2E.Tests/Docker/
└── EncryptedPayloadFlowTests.cs         # New: Docker E2E
```

**Structure Decision**: Follows existing Sorcha conventions. New UI components under `Components/Encryption/`. New models under `Models/Encryption/`. Scoped service registered in existing `ServiceCollectionExtensions`. Layout-level component placed in `MainLayout.razor` alongside existing global components.

## Phase Summary

| Phase | Focus | User Stories | Parallel Opportunities |
|-------|-------|-------------|----------------------|
| 1: Setup | Models, interfaces, event contracts | US2 foundation | Models in different projects |
| 2: Backend Events | Per-recipient SignalR + polling | US2 | Notification + Store + Pipeline |
| 3: Test Gaps | DevMode + FLE unit tests | US3, US4 | 5 independent test files |
| 4: UI Popover | Floating progress component | US1, US5 | Tracker service + Component |
| 5: Integration | GAP-005 + wiring | US1, US2 | SignalR test + E2E |
| 6: Docker E2E | Full flow validation | US6 | Independent of unit tests |
| 7: Polish | Docs, cleanup, regression | All | Documentation files |
