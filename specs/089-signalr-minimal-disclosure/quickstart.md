# Quickstart: SignalR Minimal Disclosure

## What This Feature Does

Reduces all SignalR notification payloads to thin triggers (signal type + instance ID only). Clients pull details through authenticated REST endpoints. Fixes the notification delivery bug and closes authorization gaps.

## Key Changes

1. **Signal payloads** — Strip all workflow metadata from notifications. Two record types: `SignalNotification` (actions) and `EncryptionSignal` (encryption progress).

2. **Delivery channel** — All action signals route exclusively through `wallet:{address}` groups. The `instance:{id}` group is removed entirely.

3. **Authorization** — Service tokens must include `org_id` claim to subscribe to wallets. No exceptions.

4. **Client behavior** — On signal receipt, clients pull details from existing REST endpoints. On reconnect, clients immediately poll to catch missed signals.

## Files That Change

### Server (Blueprint.Service)
- `Services/Interfaces/INotificationService.cs` — Simplified method signatures
- `Services/Implementation/NotificationService.cs` — Thin payloads, wallet-only delivery
- `Hubs/ActionsHub.cs` — org_id enforcement, new SignalNotification record
- `Models/EncryptionNotifications.cs` — Replaced with EncryptionSignal
- `Services/Implementation/EventsHubNotificationBridge.cs` — Thin SignalR send

### Agent
- `Inbox/SignalRInboxListener.cs` — Handle thin signals, trigger immediate poll

### UI (Sorcha.UI.Core)
- `Models/Actions/ActionNotification.cs` — Replaced with thin types
- `Models/Admin/EncryptionHubModels.cs` — Replaced with EncryptionSignal
- `Services/ActionsHubConnection.cs` — Updated event registrations
- `Services/EventsHubConnection.cs` — Updated event handler

### UI (Sorcha.UI.Web.Client)
- `Components/Layout/PendingActionToast.razor` — Generic messages
- `Components/Layout/PendingActionInbox.razor` — Pull detail on expand

### Tests (update existing + add new)
- Update: NotificationServiceEventsHubTests, SignalRIntegrationTests, ActionsHubConnectionTests, EventsHubConnectionTests
- New: ActionsHubAuthorizationTests, SignalNotificationDeliveryTests

## How to Test

```bash
# Run all affected tests
dotnet test --filter "FullyQualifiedName~Notification|FullyQualifiedName~SignalR|FullyQualifiedName~ActionsHub|FullyQualifiedName~EventsHub"

# Integration test with Docker
docker-compose up -d
# Run a walkthrough (e.g., ConstructionPermit) and verify:
# 1. Agent logs show "Received signal: action-available" (not rich payload fields)
# 2. Agent logs show immediate instance poll after signal
# 3. No "instance:" group references in Blueprint Service logs
# 4. Service tokens without org_id are rejected
```

## Breaking Changes

- SignalR payload shapes change for all notification types
- Service tokens without `org_id` are rejected (previously warned)
- All clients must be updated simultaneously
