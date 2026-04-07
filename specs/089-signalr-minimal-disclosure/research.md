# Research: SignalR Minimal Disclosure

## Decision 1: Signal Payload Contract Shape

**Decision:** Single `SignalNotification` record for action signals, separate `EncryptionSignal` for encryption progress.

**Rationale:** Action signals share the same delivery channel (wallet groups) and need identical fields. Encryption signals have a distinct operational purpose (progress bars) and different delivery semantics (operation-scoped, not instance-scoped).

**Alternatives considered:**
- Single universal record for all signal types — rejected because encryption needs `PercentComplete` inline and doesn't have an `InstanceId`.
- Per-signal-type records (ActionAvailableSignal, ActionRejectedSignal, etc.) — rejected as unnecessary; all action signals carry the same fields after stripping metadata.

## Decision 2: Reconnection Strategy

**Decision:** Keep existing `InfiniteRetryPolicy` in `SorchaHubConnectionBuilder` (1s, 2s, 5s, 10s, 30s then 30s forever). Add on-reconnect immediate poll in agent and UI clients.

**Rationale:** The existing retry policy already provides exponential backoff with infinite retry — matches the spec requirement. It uses SignalR's built-in `WithAutomaticReconnect`, not Polly. The missing piece is the on-reconnect catch-up poll.

**Alternatives considered:**
- Replace with Polly policies — rejected; SignalR has its own retry mechanism via `IRetryPolicy` and the current implementation is sound.
- Shorter retry intervals — rejected; 30s steady-state is appropriate given the 30s polling fallback.

## Decision 3: Instance Group Removal Strategy

**Decision:** Remove all `instance:{id}` group references from `NotificationService`. No client ever subscribes to instance groups, so removal has zero client impact.

**Rationale:** Confirmed via codebase search — no client calls `SubscribeToInstance`. The `instance:{id}` group is only used server-side in `NotificationService` for broadcasting. All messages sent to these groups go nowhere. Removal is safe.

**Alternatives considered:**
- Keep instance groups but add authz — rejected; no clients use them and they represent unnecessary attack surface.

## Decision 4: Service Token org_id Enforcement

**Decision:** Reject service tokens without `org_id` claim in `ActionsHub.SubscribeToWallet`.

**Rationale:** All Sorcha-issued service tokens include `org_id` via Aspire service defaults. The backward-compatibility allowance was a development-time convenience that creates a security hole.

**Alternatives considered:**
- Deprecation warning period — rejected; this is a security fix, not a feature removal. All callers are internal.

## Decision 5: UI Client Notification Model Migration

**Decision:** Replace all rich notification models in `Sorcha.UI.Core/Models/` with thin signal equivalents. UI components that currently display notification details inline (PendingActionToast, PendingActionInbox) will switch to showing generic messages and pulling detail on demand.

**Rationale:** The UI has parallel model hierarchies: server-side records in Blueprint.Service and client-side DTOs in UI.Core. Both need updating. The EventsHub bridge continues to persist enriched data to Tenant Service, so the activity feed endpoint remains the source of truth for rich notification data.

**Alternatives considered:**
- Keep rich models in UI and only thin the wire format — rejected; maintaining two shapes (wire vs display) adds complexity for no benefit.

## Key File Inventory

### Server-Side (Blueprint.Service)
| File | Lines | What Changes |
|------|-------|-------------|
| `Services/Interfaces/INotificationService.cs` | 12-110 | Simplify method signatures |
| `Services/Implementation/NotificationService.cs` | 1-406 | Remove instance groups, thin payloads |
| `Hubs/ActionsHub.cs` | 1-233 | Close org_id loophole, replace ActionNotification record |
| `Models/EncryptionNotifications.cs` | 1-126 | Replace 4 records with EncryptionSignal |
| `Services/Implementation/EventsHubNotificationBridge.cs` | 1-300 | Thin the SignalR send, keep enrichment for persistence |

### Client-Side (ServiceClients.Http)
| File | Lines | What Changes |
|------|-------|-------------|
| `Hub/SorchaHubConnectionBuilder.cs` | 1-71 | No changes needed (retry already correct) |

### Agent (Sorcha.Agent)
| File | Lines | What Changes |
|------|-------|-------------|
| `Inbox/SignalRInboxListener.cs` | 1-134 | Handle SignalNotification, trigger immediate poll |

### UI (Sorcha.UI.Core)
| File | Lines | What Changes |
|------|-------|-------------|
| `Models/Actions/ActionNotification.cs` | 1-134 | Replace 4 records with thin equivalents |
| `Models/Admin/EncryptionHubModels.cs` | 1-55 | Replace 4 records with EncryptionSignal |
| `Services/ActionsHubConnection.cs` | 1-478 | Update event registrations to thin types |
| `Services/EventsHubConnection.cs` | 1-324 | Update InboundActionReceived handler |
| `Components/Admin/OperationNotificationListener.razor` | 1-35 | Update to EncryptionSignal shape |

### UI (Sorcha.UI.Web.Client)
| File | Lines | What Changes |
|------|-------|-------------|
| `Components/Layout/PendingActionToast.razor` | 1-65 | Generic message, pull detail on click |
| `Components/Layout/PendingActionInbox.razor` | 1-324 | Generic items, pull detail on expand |

### Tests
| File | What Changes |
|------|-------------|
| `NotificationServiceEventsHubTests.cs` | Update to thin payload assertions |
| `SignalRIntegrationTests.cs` | Update to thin payload shapes |
| `ActionsHubConnectionTests.cs` | Update event type expectations |
| `EventsHubConnectionTests.cs` | Update event type expectations |
| New: `ActionsHubAuthorizationTests.cs` | Test org_id enforcement |
| New: `SignalNotificationDeliveryTests.cs` | Test wallet-only delivery |
