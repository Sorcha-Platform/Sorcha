# Research: 078 — Register Sync Status Lifecycle & UI Improvements

## Decision 1: Sync State → Register Status Mapping

**Decision**: Map peer sync states to register statuses at the Register Service level, triggered by peer service notifications.

| RegisterSyncState (Peer) | RegisterStatus (Register) | Trigger |
|--------------------------|---------------------------|---------|
| Subscribing | Checking | Subscription created |
| Syncing | Recovery | Docket chain pull begins |
| FullyReplicated | Online | Full chain synced |
| Active | Online | Forward-only caught up |
| Error | Offline | Sync failures exhausted |
| (connection lost) | Offline | 30s debounce timeout |
| (reconnected) | Checking | Peer heartbeat resumes |

**Rationale**: RegisterStatus enum already has the right values (Online, Offline, Checking, Recovery). The mapping is clean and intuitive. The peer service already notifies Register Service on subscription changes — just need to include the status transition.

**Alternatives**: Could add new RegisterStatus values (e.g., "Syncing") but this adds UI complexity for no benefit — Recovery already communicates the concept.

## Decision 2: Status Propagation Path

**Decision**: Peer Service → Register Service (internal endpoint) → RegisterManager.UpdateRegisterStatusAsync() → SignalR → UI

**Rationale**: This path already exists for status changes. RegisterEventBridgeService subscribes to `register:status-changed` events and broadcasts `RegisterStatusChanged(registerId, status)` via SignalR. The UI already handles this event.

**Key files**:
- Register Service internal endpoint: `Program.cs:305-346` (subscription handler)
- RegisterManager.UpdateRegisterStatusAsync: `RegisterManager.cs:137-169`
- RegisterEventBridgeService: `RegisterEventBridgeService.cs:56-65`
- RegisterHub: `RegisterHub.cs:70`

## Decision 3: Offline Debounce

**Decision**: 30-second grace period at peer service level before reporting "offline" status. Implemented via a timer that cancels if the connection resumes.

**Rationale**: Peer heartbeat intervals are 30s. A single missed heartbeat shouldn't trigger Offline — it could be network jitter. Two missed heartbeats (60s) is a genuine disconnection, but the debounce at 30s gives a buffer while staying responsive.

## Decision 4: Immediate Sync Trigger

**Decision**: After `SubscribeToRegisterAsync()`, signal the background service to run `ProcessSubscriptionAsync()` immediately for the new subscription, instead of waiting for the 5-minute periodic timer.

**Rationale**: Simple signal mechanism (e.g., ManualResetEventSlim or TaskCompletionSource) avoids restructuring the background service loop. The periodic timer continues for retries.

## Decision 5: Table Auto-Update Pattern

**Decision**: Replace notification boxes with direct table prepend via SignalR event handlers. Use a small buffer (100ms) to batch rapid updates.

**Rationale**: The SignalR events `TransactionConfirmed` and `DocketSealed` already fire. Current handlers increment a counter and show a notification box. Changing them to prepend rows is a UI-only change. The 100ms buffer prevents excessive re-renders when many transactions confirm at once.

**Key files**:
- Detail.razor: Lines 59-98 (notification boxes to remove)
- Detail.razor: Lines 345-374 (event handlers to modify)

## Decision 6: Encryption Policy Storage

**Decision**: Add `EncryptionRequired` boolean to the register's CryptoPolicy. Store transitions as control-chain governance transactions. The existing `DevMode` flag on Register controls this — when `DevMode=false`, encryption is required. The one-way switch sets `DevMode=false` permanently.

**Rationale**: The `Register.DevMode` property already controls this behavior. The UI switch just needs to call an endpoint that sets `DevMode=false` and prevents it from being set back to `true`. No new model fields needed.

**Alternatives**: Could add a separate `EncryptionPolicy` entity but this duplicates what DevMode already controls.

## Decision 7: UI Warning for Unencrypted Registers

**Decision**: Check `Register.DevMode == true` in the register card/list component. Show MudBlazor warning icon (`Icons.Material.Filled.Warning`) with amber color and tooltip.

**Rationale**: Simple conditional render. No API changes needed — DevMode is already returned in the register model.
