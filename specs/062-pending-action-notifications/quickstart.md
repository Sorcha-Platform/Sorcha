# Quickstart: Pending Action Notifications

**Feature**: 062-pending-action-notifications

## What This Feature Does

Transforms raw transaction events into meaningful "someone needs you to do something" notifications. Users see business context (order references, inspection requests, approval deadlines) instead of transaction hashes, with a pending action inbox and real-time alerts.

## Implementation Approach

This feature is primarily **wiring and UI work** — most backend infrastructure already exists.

### What Already Exists (Don't Rebuild)

| Component | What It Does | Where |
|-----------|-------------|-------|
| Bloom filter → gRPC notification | Detects inbound transactions, notifies Wallet Service | Register Service |
| Delivery routing | Routes to real-time/digest/rate-limited paths | Wallet Service NotificationDeliveryService |
| Enrichment bridge | Resolves blueprint name, action title, sender name | Blueprint Service EventsHubNotificationBridge |
| SignalR hub | Pushes to user groups, sends InboundActionReceived | Blueprint Service EventsHub |
| Activity events | Persisted events with read/unread, CRUD endpoints | Tenant Service |
| User preferences | NotificationMethod + NotificationFrequency stored | Tenant Service |
| ActivityLogPanel | Renders grouped events in UI | Sorcha.UI |

### What Needs Building

**Layer 1 — Wire existing components (P1, ~8h)**
1. Register `InboundActionReceived` handler in `EventsHubConnection`
2. Create `PendingActionInbox` Blazor component
3. Add pending actions query endpoint to Blueprint Service
4. Persist notifications as ActivityEvents via Tenant Service event endpoint

**Layer 2 — Smart notifications (P2, ~8h)**
5. Add `NotificationConfig` to Action model
6. Implement summary template rendering in EventsHubNotificationBridge
7. Implement urgency calculation from deadline fields
8. Replace `DefaultNotificationPreferenceProvider` with Tenant Service call

**Layer 3 — Polish (P3, ~4h)**
9. Notification grouping in inbox by blueprint + group key
10. Catch-up on reconnect (fetch missed events from Tenant Service)
11. Badge count on inbox icon

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Common/Sorcha.Blueprint.Models/Action.cs` | Add `NotificationConfig?` property |
| `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs` | Add summary/urgency/deadline rendering |
| `src/Services/Sorcha.Blueprint.Service/Endpoints/ActionEndpoints.cs` | Add `GET /api/actions/pending` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs` | Add `InboundActionReceived` handler |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor` | New component |
| `src/Services/Sorcha.Wallet.Service/Services/Implementation/DefaultNotificationPreferenceProvider.cs` | Replace with Tenant Service call |

## Testing Strategy

- Unit tests for summary template rendering, urgency calculation, preference resolution
- Integration tests for pending actions endpoint
- E2E test: submit action as Participant A → verify Participant B sees notification in inbox
