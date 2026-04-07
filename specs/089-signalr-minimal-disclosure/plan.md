# Implementation Plan: SignalR Minimal Disclosure & Notification Fix

**Branch**: `089-signalr-minimal-disclosure` | **Date**: 2026-04-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/089-signalr-minimal-disclosure/spec.md`
**Design**: `docs/superpowers/specs/2026-04-07-signalr-minimal-disclosure-design.md`

## Summary

Replace all rich SignalR notification payloads with thin trigger signals (type + instanceId only). Remove the unused `instance:{id}` group broadcasting. Close the service-token-without-org_id authorization loophole. Fix the notification delivery bug by routing all action signals exclusively through ownership-validated `wallet:{address}` groups. Update all clients (Agent, UI, ServiceClients) to handle thin signals and pull details through authenticated REST endpoints.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: ASP.NET Core SignalR, Redis (pub/sub backplane), MudBlazor (UI)
**Storage**: N/A (SignalR groups are transient, in-memory encryption operation store unchanged)
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0, Moq 4.20.72
**Target Platform**: Docker (Linux containers), Blazor WASM (browser)
**Project Type**: Microservices (Blueprint Service server, Agent CLI client, Blazor UI client)
**Performance Goals**: Sub-2-second signal delivery (vs current 30s polling dependency)
**Constraints**: Breaking SignalR contract — all clients updated simultaneously
**Scale/Scope**: ~15 files changed, ~6 new test files, 2 new record types replacing ~10 existing types

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes scoped within existing service boundaries. No new cross-service dependencies. |
| II. Security First | PASS | This feature *improves* security: minimal disclosure, authz hardening, SIGINT risk reduction. |
| III. API Documentation | PASS | SignalR signal contracts documented in contracts/signalr-signals.md. No new REST endpoints. |
| IV. Testing Requirements | PASS | Existing tests updated + new authz and delivery tests. Target >85% on changed code. |
| V. Code Quality | PASS | Async/await patterns maintained. No new dependencies. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses correct terminology (Action, Participant, Disclosure). |
| VIII. Observability | PASS | Structured logging added for delivery tracing. |

No violations. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/089-signalr-minimal-disclosure/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research output
├── data-model.md        # Entity definitions
├── quickstart.md        # Implementation quickstart
├── contracts/
│   └── signalr-signals.md  # SignalR signal contracts
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (files changing)

```text
src/
├── Services/Sorcha.Blueprint.Service/
│   ├── Hubs/ActionsHub.cs                          # org_id enforcement, new record
│   ├── Models/EncryptionNotifications.cs            # → EncryptionSignal
│   ├── Services/Interfaces/INotificationService.cs  # Simplified signatures
│   ├── Services/Implementation/
│   │   ├── NotificationService.cs                   # Thin payloads, wallet-only
│   │   └── EventsHubNotificationBridge.cs           # Thin SignalR send
│   └── Services/Implementation/EncryptionBackgroundService.cs  # Use EncryptionSignal
├── Apps/Sorcha.Agent/
│   └── Inbox/SignalRInboxListener.cs                # Thin signal handler + immediate poll
├── Apps/Sorcha.UI/
│   ├── Sorcha.UI.Core/
│   │   ├── Models/Actions/ActionNotification.cs     # → SignalNotification types
│   │   ├── Models/Admin/EncryptionHubModels.cs      # → EncryptionSignal
│   │   ├── Services/ActionsHubConnection.cs         # Updated registrations
│   │   └── Services/EventsHubConnection.cs          # Updated handler
│   └── Sorcha.UI.Web.Client/Components/Layout/
│       ├── PendingActionToast.razor                 # Generic messages
│       └── PendingActionInbox.razor                 # Pull detail on expand

tests/
├── Sorcha.Blueprint.Service.Tests/
│   ├── Services/NotificationServiceEventsHubTests.cs  # Updated assertions
│   ├── Integration/SignalRIntegrationTests.cs         # Updated payloads
│   ├── Services/ActionsHubAuthorizationTests.cs       # NEW: org_id enforcement
│   └── Services/SignalNotificationDeliveryTests.cs    # NEW: wallet-only delivery
├── Sorcha.UI.Core.Tests/
│   ├── Services/ActionsHubConnectionTests.cs          # Updated types
│   └── Services/EventsHubConnectionTests.cs           # Updated types
└── Sorcha.Agent.Tests/
    └── Inbox/SignalRInboxListenerTests.cs              # NEW or updated: thin signal handling
```

**Structure Decision**: All changes are within existing project boundaries. No new projects or services. The two new record types (`SignalNotification`, `EncryptionSignal`) live in their respective existing files.

## Implementation Phases

### Phase 1: Server-Side Signal Contracts & Delivery (P1 — Core fix)

Define the new signal records and update the server-side notification pipeline. This is the foundation everything else depends on.

**Tasks:**

1.1. **Define `SignalNotification` record** in `ActionsHub.cs` (replacing `ActionNotification`)
   - Fields: SignalType (string, required), InstanceId (string, required), CorrelationId (Guid?), Timestamp (DateTimeOffset)
   - Remove the old `ActionNotification` record

1.2. **Define `EncryptionSignal` record** in `EncryptionNotifications.cs` (replacing all 4 encryption records)
   - Fields: OperationId (string, required), PercentComplete (int, required), Status (string, required), Timestamp (DateTimeOffset)
   - Remove: EncryptionProgressNotification, EncryptionCompleteNotification, EncryptionFailedNotification, RecipientEncryptionNotification

1.3. **Update `INotificationService` interface**
   - Simplify `NotifyActionAvailableAsync` — remove overload with actionTitle, participantId; keep instanceId + walletAddress
   - Simplify `NotifyActionRejectedAsync` — remove rejectedActionId, targetActionId, targetParticipantId, reason params
   - Simplify encryption methods to use `EncryptionSignal`
   - Remove `NotifyRecipientProgressAsync` (detail via pull-back)

1.4. **Update `NotificationService` implementation**
   - Remove all `instance:{id}` group broadcasts
   - All action signals → `wallet:{address}` group only
   - Build `SignalNotification` with appropriate `SignalType`
   - Generate `CorrelationId` = `Guid.NewGuid()` for each signal
   - Encryption methods build `EncryptionSignal` instead of rich types
   - Add structured logging: `"Sending signal {SignalType} to wallet {Wallet} for instance {InstanceId}"`

1.5. **Update `EventsHubNotificationBridge`**
   - Keep all enrichment logic (blueprint name, sender name, summary, urgency, deadline)
   - Keep activity event persistence to Tenant Service
   - Change the `SendAsync("InboundActionReceived", ...)` to send `SignalNotification` instead of `InboundActionNotification`
   - Remove the `InboundActionNotification` inner class (or move to internal if needed for persistence mapping)

1.6. **Update `EncryptionBackgroundService`**
   - Replace all `NotifyEncryptionProgressAsync` calls to use `EncryptionSignal`
   - Replace `NotifyEncryptionCompleteAsync` / `NotifyEncryptionFailedAsync` calls
   - Remove `NotifyRecipientProgressAsync` calls

1.7. **Update callers in `ActionExecutionService`**
   - Update `NotifyParticipantsAsync` to use simplified interface
   - Ensure wallet address lookup still works (ParticipantWallets dictionary)
   - Log warning when wallet address is null for a participant

**Tests:**
- Update `NotificationServiceEventsHubTests.cs` — assert thin payloads
- Create `SignalNotificationDeliveryTests.cs` — verify wallet-only delivery, no instance groups

---

### Phase 2: Authorization Hardening (P1 — Security)

Close the service token loophole.

**Tasks:**

2.1. **Enforce org_id on service tokens in `ActionsHub.SubscribeToWallet`**
   - Change the `LogWarning` + allow to `throw new HubException("Unauthorized: service tokens must include org_id claim")`
   - Verify all Sorcha service tokens include org_id (check Aspire service defaults token issuance)

2.2. **Add authorization tests**
   - Create `ActionsHubAuthorizationTests.cs`
   - Test: service token without org_id → HubException
   - Test: service token with org_id → success
   - Test: user token with valid wallet ownership → success
   - Test: user token with unlinked wallet → HubException

**Tests:**
- `ActionsHubAuthorizationTests.cs` — all 4 scenarios above

---

### Phase 3: Agent Client Update (P1 — Delivery fix)

Update the agent to handle thin signals and trigger immediate polls.

**Tasks:**

3.1. **Update `SignalRInboxListener`**
   - Change `ActionAvailable` handler to deserialize `SignalNotification` instead of rich payload
   - On receipt: extract `instanceId`, trigger immediate instance poll via existing polling mechanism
   - Log: `"Received signal {SignalType} for instance {InstanceId}, triggering poll"`

3.2. **Add on-reconnect immediate poll**
   - Hook into `Reconnected` event on the hub connection
   - Trigger an immediate poll of all subscribed wallets' pending actions
   - Log: `"SignalR reconnected, polling for missed actions"`

3.3. **Update/create agent SignalR tests**
   - Verify thin signal deserialization
   - Verify immediate poll trigger on signal receipt
   - Verify reconnect triggers poll

---

### Phase 4: UI Client Update (P2/P3 — UI)

Update UI models, hub connections, and components.

**Tasks:**

4.1. **Replace UI notification models**
   - `Models/Actions/ActionNotification.cs` — Replace `ActionAvailableNotification`, `ActionRejectedNotification`, `WorkflowCompletedNotification` with `SignalNotification` equivalent
   - `Models/Admin/EncryptionHubModels.cs` — Replace 4 records with `EncryptionSignal` equivalent
   - Keep `PendingActionNotificationDto` unchanged (it's the pull-back model from activity feed)

4.2. **Update `ActionsHubConnection`**
   - Update event registrations (lines 293-413) to use thin types
   - `OnActionAvailable` → receives `SignalNotification`
   - `OnActionRejected` → receives `SignalNotification`
   - `OnWorkflowCompleted` → receives `SignalNotification`
   - Encryption events → receive `EncryptionSignal`
   - Remove `OnRecipientProgress` event (no longer sent)

4.3. **Update `EventsHubConnection`**
   - `OnPendingActionReceived` → receives `SignalNotification` instead of `PendingActionNotificationDto`
   - `OnEncryptionOperationCompleted` → receives `EncryptionSignal`
   - Add on-reconnect immediate data refresh

4.4. **Update `PendingActionToast.razor`**
   - On signal receipt: show generic "New action available" snackbar
   - No urgency/deadline/sender in toast (not available in thin signal)
   - Click navigates to instance view

4.5. **Update `PendingActionInbox.razor`**
   - On signal receipt: add placeholder item to inbox
   - Pull enriched data from activity feed to populate details
   - Maintain burst-throttle batching behavior

4.6. **Update `OperationNotificationListener.razor`**
   - Handle `EncryptionSignal` instead of `EncryptionOperationCompletedDto`
   - Show generic success/failure snackbar

4.7. **Update UI tests**
   - `ActionsHubConnectionTests.cs` — thin type expectations
   - `EventsHubConnectionTests.cs` — thin type expectations

---

## Dependency Graph

```
Phase 1 (Server contracts & delivery)
   ↓
Phase 2 (Authorization hardening) ← can run in parallel with Phase 1 after task 1.1
   ↓
Phase 3 (Agent client) ← depends on Phase 1 (needs server to send thin signals)
   ↓
Phase 4 (UI client) ← depends on Phase 1 (needs server to send thin signals)
```

Phases 3 and 4 can run in parallel once Phase 1 is complete.

## Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| Breaking all clients simultaneously | All clients are internal. Update in one branch. Run full test suite before merge. |
| Service tokens missing org_id | Verify Aspire service defaults before deploying Phase 2. If any tokens lack org_id, fix token issuance first. |
| Encryption store TTL eviction | Check InMemoryEncryptionOperationStore retention. If too short, this is a pre-existing issue — log as follow-up, not a blocker. |
| UI regression (toasts/inbox) | Existing Playwright E2E tests cover toast rendering. Update expectations for generic messages. |
