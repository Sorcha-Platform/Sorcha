# Implementation Plan: Envelope Encryption Integration

**Branch**: `052-encryption-integration` | **Date**: 2026-03-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/052-encryption-integration/spec.md`

## Summary

Wire the existing async encryption pipeline in the Blueprint Service to the UI and CLI. The backend already returns HTTP 202 with OperationId for async operations and sends SignalR progress events. The UI has an EncryptionProgressIndicator component ready but disconnected. This feature bridges the gap by extending the view model, adding SignalR handlers, implementing retry, operations history, CLI action execution with async support, and cross-page notifications.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: MudBlazor (UI), System.CommandLine (CLI), Refit (CLI HTTP), Spectre.Console (CLI output), SignalR (real-time)
**Storage**: In-memory (active operations), ActivityEvent store (history, 30-day TTL)
**Testing**: xUnit + FluentAssertions + Moq (>85% coverage target)
**Target Platform**: Blazor WASM (UI), Console (CLI), Linux/Windows server (services)
**Project Type**: Distributed microservices with shared UI libraries
**Performance Goals**: <1s progress update latency (SignalR), <3s polling fallback, <2s history page load
**Constraints**: No new databases, no new microservices, no changes to encryption algorithms
**Scale/Scope**: 10 concurrent operations per user, 200 operations in history, 50 per page

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | No new services. Extending Blueprint Service endpoints and UI components. |
| II. Security First | PASS | Wallet ownership validation already exists on operations endpoint. New list endpoint will reuse same pattern. |
| III. API Documentation | PASS | New endpoints will have Scalar docs via `.WithSummary()` and `.WithDescription()`. |
| IV. Testing Requirements | PASS | >85% coverage target for new code. Tests for view models, services, CLI commands, UI components. |
| V. Code Quality | PASS | C# 13, async/await, DI throughout. Nullable reference types enabled. |
| VI. Blueprint Standards | N/A | No blueprint creation in this feature. |
| VII. DDD | PASS | Using correct terminology: Action (not step), Participant (not user). |
| VIII. Observability | PASS | Structured logging in new services. Backend already has comprehensive logging in EncryptionBackgroundService. |

**Gate result**: PASS — no violations.

## Project Structure

### Documentation (this feature)

```text
specs/052-encryption-integration/
├── spec.md
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── operations-api.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
# UI Changes (Sorcha.UI.Core — shared library)
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Models/
│   ├── Workflows/
│   │   └── ActionSubmissionResultViewModel.cs    # MODIFY: Add OperationId, IsAsync
│   └── Admin/
│       ├── EncryptionOperationModels.cs          # MODIFY: Add TransactionHash, context fields
│       └── OperationHistoryModels.cs             # NEW: OperationHistoryItem, OperationHistoryPage
├── Services/
│   ├── WorkflowService.cs                        # MODIFY: Map OperationId/IsAsync from response
│   ├── ActionsHubConnection.cs                   # MODIFY: Add encryption event handlers
│   └── Admin/
│       ├── IOperationStatusService.cs            # MODIFY: Add ListOperationsAsync
│       └── OperationStatusService.cs             # MODIFY: Implement ListOperationsAsync
└── Components/
    └── Admin/
        ├── EncryptionProgressIndicator.razor      # MODIFY: Add retry, completion callback
        └── OperationNotificationListener.razor    # NEW: Global toast notification component

# UI Changes (Sorcha.UI.Web.Client — WASM pages)
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
├── MyActions.razor                                # MODIFY: Show progress indicator on async submit
└── Operations.razor                               # NEW: Operations history page

# UI Changes (Sorcha.UI.Web — host layout)
src/Apps/Sorcha.UI/Sorcha.UI.Web/Components/
└── Layout/
    └── MainLayout.razor                           # MODIFY: Add OperationNotificationListener

# Blueprint Service Changes
src/Services/Sorcha.Blueprint.Service/
├── Endpoints/
│   └── OperationsEndpoints.cs                     # MODIFY: Add list endpoint
├── Services/
│   ├── Interfaces/
│   │   └── IEncryptionOperationStore.cs           # MODIFY: Add ListByWalletAsync
│   └── Implementation/
│       ├── InMemoryEncryptionOperationStore.cs    # MODIFY: Implement ListByWalletAsync
│       ├── NotificationService.cs                 # MODIFY: Send EventsHub notification on completion
│       └── EncryptionBackgroundService.cs          # NO CHANGES (already sends SignalR events)
└── Hubs/
    └── ActionsHub.cs                               # NO CHANGES (already routes wallet groups)

# CLI Changes
src/Apps/Sorcha.Cli/
├── Commands/
│   └── ActionCommands.cs                          # NEW: action execute command
├── Services/
│   └── IBlueprintServiceClient.cs                 # MODIFY: Add SubmitActionAsync
└── Models/
    └── ActionModels.cs                            # NEW: CLI action request/response DTOs

# Tests
tests/
├── Sorcha.UI.Core.Tests/
│   ├── Models/
│   │   └── ActionSubmissionResultViewModelTests.cs  # NEW
│   ├── Services/
│   │   ├── WorkflowServiceTests.cs                  # MODIFY: Test async response mapping
│   │   └── OperationStatusServiceTests.cs           # MODIFY: Test list operations
│   └── Components/
│       ├── EncryptionProgressIndicatorTests.cs       # MODIFY: Test retry, completion
│       └── OperationNotificationListenerTests.cs     # NEW
├── Sorcha.Cli.Tests/Commands/
│   └── ActionCommandTests.cs                        # NEW
└── Sorcha.Blueprint.Service.Tests/
    └── Endpoints/
        └── OperationsEndpointTests.cs               # MODIFY: Test list endpoint
```

**Structure Decision**: Existing distributed project structure. All changes are modifications to existing projects — no new projects created. The only new files are: `OperationHistoryModels.cs`, `OperationNotificationListener.razor`, `Operations.razor`, `ActionCommands.cs`, `ActionModels.cs`, and their corresponding test files.

## Complexity Tracking

No constitution violations to justify. All changes are within existing project boundaries.

## Implementation Strategy

### Vertical Slices (by User Story)

**Slice 1 (US1 — P1)**: Core async flow bridge
- Extend `ActionSubmissionResultViewModel` with `OperationId`/`IsAsync`
- Update `WorkflowService` response mapping
- Wire `EncryptionProgressIndicator` into `MyActions.razor`
- Detect async response → show progress → handle completion
- Tests for view model, service mapping, component integration

**Slice 2 (US2 — P2)**: Retry on failure
- Add retry parameter to `EncryptionProgressIndicator` (accepts original request)
- Add retry button in error state
- Re-submit via `WorkflowService.SubmitActionExecuteAsync`
- Tests for retry flow

**Slice 3 (US6 — P6)**: SignalR push updates
- Add `EncryptionProgress`/`EncryptionComplete`/`EncryptionFailed` handlers to `ActionsHubConnection`
- Update `EncryptionProgressIndicator` to prefer SignalR over polling
- Fall back to polling when SignalR unavailable
- Tests for handler registration, fallback behavior

**Slice 4 (US3 — P3)**: Cross-page notifications
- Create `OperationNotificationListener` component (global, in MainLayout)
- Subscribe to EventsHub for encryption completion events
- Show MudBlazor snackbar on completion/failure
- Add navigation warning banner in `MyActions.razor`
- Backend: Send notification via EventsHub in `NotificationService`
- Tests for listener, toast display

**Slice 5 (US4 — P4)**: Operations history
- Add `ListByWalletAsync` to `IEncryptionOperationStore`
- Add `GET /api/operations` list endpoint with pagination
- Create `OperationHistoryModels` (UI)
- Add `ListOperationsAsync` to `IOperationStatusService`
- Create `Operations.razor` page
- Tests for endpoint, service, page

**Slice 6 (US5 — P5)**: CLI async support
- Create `ActionCommand` with `execute` subcommand
- Add `SubmitActionAsync` to CLI Refit interface
- Implement blocking mode with Spectre.Console progress
- Implement `--no-wait` flag for non-blocking mode
- Tests for command structure, argument parsing

### Dependencies Between Slices

```
Slice 1 (Core Flow) ────────┐
                              ├──▶ Slice 2 (Retry)
                              ├──▶ Slice 3 (SignalR Push)
                              │         │
                              │         ▼
                              ├──▶ Slice 4 (Notifications) ──depends on── Slice 3
                              └──▶ Slice 5 (History)
                                   Slice 6 (CLI) ──independent──
```

- Slice 1 must be completed first (all others depend on the core view model changes)
- Slice 6 (CLI) is fully independent and can run in parallel with any other slice
- Slice 4 depends on Slice 3 (SignalR handlers needed for notification listener)
- Slices 2, 3, 5 can run in parallel after Slice 1

### YARP Gateway Routes

The Blueprint Service operations endpoints are already routed through the API Gateway. The new list endpoint (`GET /api/operations`) will be covered by the existing YARP route pattern for `/api/operations/**`. Verify and add if needed.
