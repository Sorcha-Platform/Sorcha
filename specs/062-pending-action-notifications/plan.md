# Implementation Plan: Pending Action Notifications

**Branch**: `062-pending-action-notifications` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)

## Summary

Transform raw transaction events into meaningful pending action notifications. Users see "Inspection requested by Acme — Order #4421" instead of transaction hashes, with a pending action inbox, real-time alerts, blueprint-defined templates, and urgency badges. Most backend infrastructure exists; this is primarily wiring, enrichment, and UI work.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: SignalR, Redis, MudBlazor, Sorcha.ServiceClients
**Storage**: PostgreSQL (ActivityEvent persistence via Tenant Service), Redis (real-time pub/sub + digest queue)
**Testing**: xUnit + FluentAssertions + Moq + Playwright
**Target Platform**: Blazor WASM (UI) + .NET microservices (backend)
**Project Type**: Web (Blazor frontend + 3 backend services)
**Performance Goals**: <5s notification delivery, <2s inbox load
**Constraints**: Must not break existing activity event system, backwards-compatible Action model change
**Scale/Scope**: Handles up to 500 pending actions per user, 100+ notifications/minute burst

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Each service modified independently; no new coupling |
| II. Security First | PASS | All endpoints require JWT auth; no new secrets |
| III. API Documentation | PASS | New endpoint gets OpenAPI/Scalar docs |
| IV. Testing Requirements | PASS | Unit + integration + E2E tests planned |
| V. Code Quality | PASS | Async/await, DI, nullable enabled |
| VI. Blueprint Standards | PASS | NotificationConfig added as JSON property |
| VII. Domain-Driven Design | PASS | Uses "Action", "Participant", "Blueprint" terminology |
| VIII. Observability | PASS | Existing notification metrics extended |

**Post-design re-check**: All gates still pass. No new projects created — modifications to existing services only.

## Project Structure

### Documentation (this feature)

```text
specs/062-pending-action-notifications/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity definitions
├── quickstart.md        # Implementation guide
├── contracts/           # API contracts
│   └── pending-actions-api.yaml
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (modifications to existing projects)

```text
src/Common/Sorcha.Blueprint.Models/
└── Action.cs                          # Add NotificationConfig property
└── NotificationConfig.cs              # New: notification template model

src/Services/Sorcha.Blueprint.Service/
├── Endpoints/ActionEndpoints.cs       # Add GET /api/actions/pending
├── Services/Implementation/
│   └── EventsHubNotificationBridge.cs # Add summary/urgency rendering
└── Storage/IInstanceStore.cs          # Add pending actions query

src/Services/Sorcha.Wallet.Service/
└── Services/Implementation/
    └── TenantNotificationPreferenceProvider.cs  # New: replaces Default

src/Apps/Sorcha.UI/
├── Sorcha.UI.Core/
│   ├── Services/EventsHubConnection.cs          # Add InboundActionReceived handler
│   ├── Services/PendingActionService.cs         # New: pending actions API client
│   └── Models/PendingActionNotificationDto.cs   # New: UI model
└── Sorcha.UI.Web.Client/
    └── Components/Layout/
        ├── PendingActionInbox.razor              # New: inbox component
        └── PendingActionToast.razor              # New: toast notification

tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── Services/SummaryTemplateRendererTests.cs
│   └── Services/UrgencyCalculatorTests.cs
├── Sorcha.Wallet.Service.Tests/
│   └── Services/TenantNotificationPreferenceProviderTests.cs
└── Sorcha.UI.Core.Tests/
    └── Services/PendingActionServiceTests.cs
```

**Structure Decision**: No new projects. All changes are modifications to existing services following established patterns. This aligns with the microservices-first principle — each service's scope is extended, not duplicated.

## Complexity Tracking

No constitution violations. All changes are incremental additions to existing services.
