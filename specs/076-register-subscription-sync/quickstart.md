# Quickstart: Register Subscription Sync Pipeline

**Feature Branch**: `076-register-subscription-sync`

## What This Feature Does

Fixes the broken "Subscribe to Register" flow. Currently, subscribing to a remote register in the UI creates a subscription record but the register never appears because the register data doesn't exist locally. This feature adds the missing pipeline: Tenant Service → Register Service → Peer Service, so that subscribing creates a local stub register immediately and triggers peer replication to fill it with real data.

## Implementation Overview

### Changes by Service

**1. Register Service** (orchestrator — most changes)
- New internal endpoint: `POST /api/internal/register-subscriptions`
- Accepts subscribe/unsubscribe notifications from Tenant Service
- On subscribe: creates stub register in MongoDB via `RegisterManager`, then calls Peer Service to start sync
- On unsubscribe: stops Peer sync, removes local register if not locally owned
- New `SyncState` field on Register model
- New `RegisterSyncStateChanged` SignalR event via existing event bridge

**2. Register.Models** (shared model)
- Add `SyncState` (string?) to `Register` class

**3. IPeerServiceClient / PeerServiceClient** (new method)
- Add `SubscribeToRegisterAsync(registerId, mode)` → calls existing `POST /api/registers/{registerId}/subscribe`
- Add `UnsubscribeFromRegisterAsync(registerId)` → calls existing `DELETE /api/registers/{registerId}/subscribe`

**4. IRegisterServiceClient / RegisterServiceClient** (new method)
- Add `NotifySubscriptionAsync(notification)` → calls new `POST /api/internal/register-subscriptions`

**5. Tenant Service** (small change)
- Inject `IRegisterServiceClient` into `RegisterSubscriptionService`
- After `SubscribeAsync` saves to DB, fire-and-forget call to Register Service
- After `UnsubscribeAsync` saves to DB, fire-and-forget call to Register Service

**6. UI — Sorcha.UI.Core**
- Add `SyncState` to `RegisterViewModel`
- Add `OnRegisterSyncStateChanged` event to `RegisterHubConnection`
- Update `RegisterService.MapToViewModel` to include `SyncState`
- Update `SubscribeDialog` to pass register name to `SubscribeAsync`
- Update `Index.razor` (Registers page) to show sync indicator on syncing registers

### Data Flow

```
User clicks Subscribe
        │
        ▼
UI → POST /api/organizations/{orgId}/register-subscriptions → Tenant Service
        │
        ├── 1. Save subscription to PostgreSQL (immediate)
        │
        └── 2. Fire-and-forget: POST /api/internal/register-subscriptions → Register Service
                    │
                    ├── 3a. Create stub register in MongoDB (SyncState: "Subscribing")
                    │       → SignalR: RegisterCreated event
                    │
                    └── 3b. POST /api/registers/{id}/subscribe → Peer Service
                                │
                                └── 4. Peer Service starts replication
                                        │
                                        └── 5. As dockets sync, Register Service
                                                updates register (height, status, SyncState)
                                                → SignalR: RegisterSyncStateChanged events
                                                → Final: SyncState = null, Status = Online
```

## Key Files to Modify

| File | Change |
|------|--------|
| `src/Common/Sorcha.Register.Models/Register.cs` | Add `SyncState` field |
| `src/Common/Sorcha.ServiceClients/Peer/IPeerServiceClient.cs` | Add subscribe/unsubscribe methods |
| `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs` | Implement subscribe/unsubscribe |
| `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` | Add `NotifySubscriptionAsync` |
| `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs` | Implement notification call |
| `src/Services/Sorcha.Register.Service/Program.cs` | New internal endpoint |
| `src/Services/Sorcha.Register.Service/Services/RegisterEventBridgeService.cs` | Handle sync state events |
| `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs` | Add `UpdateSyncStateAsync` method |
| `src/Services/Sorcha.Tenant.Service/Services/RegisterSubscriptionService.cs` | Add notification after subscribe/unsubscribe |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/RegisterViewModel.cs` | Add `SyncState` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterHubConnection.cs` | Add sync state event |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterService.cs` | Map `SyncState` |
| `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/SubscribeDialog.razor` | Pass register name |
| `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor` | Show sync indicator |

## Testing Strategy

- Unit tests for `RegisterManager.UpdateSyncStateAsync` and stub creation
- Unit tests for Tenant Service notification (verify fire-and-forget, verify subscription persists on notification failure)
- Integration test: full subscribe flow with mocked Peer Service
- E2E test: subscribe to register in UI, verify it appears with sync state
