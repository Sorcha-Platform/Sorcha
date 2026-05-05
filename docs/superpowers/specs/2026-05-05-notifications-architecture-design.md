# Notifications Architecture — Design

**Date:** 2026-05-05
**Feature:** 118 — notifications-architecture
**Status:** design (pre-spec)

## Why

Sorcha's notification, alert, and realtime status surfaces grew organically across 100+ phases. The result is functional but uneven: SignalR hubs concentrated in one service, group-name conventions invented per call site, generic cross-domain events leaking between services, and a multi-node correctness bug masquerading as a TODO. This design pulls the layer into a coherent shape before correctness work (web-push dispatcher, durable inbox-as-data, observability) lands on top.

Citizen Wallet PWA is in flight elsewhere and explicitly out of scope here.

## Audit findings (the part that drove the design)

The pre-design audit ran three scouts across the codebase. Highlights:

- **5 SignalR hubs:** ActionsHub, EventsHub, ChatHub (all in Blueprint Service), RegisterHub (Register), WalletHub (Wallet, Feature 114). UI sessions open three concurrent connections — Actions, Events, Register — plus Chat on the Designer.
- **No SignalR Redis backplane.** A `TODO` at `src/Services/Sorcha.Blueprint.Service/Program.cs:254`. Multi-node deploys silently lose roughly half of every group fan-out — directly contradicts the project's "always assume multi-node" rule.
- **Web Push subscriptions stored, never dispatched.** `PlatformUser.PushSubscriptions` accepts VAPID keys via REST; no service reads them and POSTs payloads.
- **RegisterHub is unauthenticated.** Subscription is checked but the hub itself accepts any websocket — DoS surface and a register-ID existence oracle.
- **EventsHub is a cross-domain leak.** Its events span workflow (`InboundActionReceived`), wallet (`EncryptionOperationCompleted`), and a generic `EventReceived` aggregator. Lives in Blueprint Service for historical reasons.
- **Group-name conventions are informal.** `wallet:{addr}` (Actions) vs `wallet:platform-user:{guid:N}` (Wallet) vs `user:{id}` / `org:{id}` (Events) vs `register:{id}` — no central builder. Only `WalletHub.GroupNameFor` exists.
- **Three reconnect policies in tree** (UI / Chat / Agent-CLI), no jitter on any of them.
- **Mix of typed and untyped hubs.** Only RegisterHub uses `IRegisterHubClient`.
- **Cross-tab divergence.** No `BroadcastChannel` or storage-event coordination. Two tabs, two websockets, two unread counts.
- **At-most-once delivery.** Redis pub/sub is the bridge transport. Disconnected at the moment of fire = event gone.

The full audit lives in this brainstorming session's transcript; numbered-finding form below.

## Decisions

### D1. Hub-per-service, with one deliberate exception

| Hub | Service | Purpose |
|---|---|---|
| **TenantHub** *(new)* | Tenant | Identity, membership, admin, system messages, user inbox |
| **BlueprintHub** | Blueprint | Workflow domain — action availability, instance state |
| **WalletHub** | Wallet | Wallet domain — tx ticks, encryption, credentials, citizen device events |
| **RegisterHub** | Register | Register domain — height, dockets, sealing, sync state |
| **ChatHub** | Blueprint | RPC streaming for AI Designer — **deliberate exception**, documented inline |

ChatHub is the only exception because its wire shape is genuinely different: RPC-streamy `StartSession` / `SendMessage` / `ReceiveChunk`, 3-minute keepalive for AI tool execution, no group fan-out at all. Lumping it with notification hubs would force shared timeouts and shared backplane chatter that don't match its workload. The exception is documented in `ChatHub.cs` XML doc and in this design.

EventsHub is retired. Its workflow events fold into BlueprintHub, its wallet events fold into WalletHub, its user-feed events become inbox entries on TenantHub.

ActionsHub renames to BlueprintHub.

### D2. Common conventions via `Sorcha.ServiceDefaults.Hubs`

A single extension wires every hub identically:

```csharp
builder.Services.AddSorchaHub<TenantHub, ITenantHubClient>(
    builder.Configuration,
    routePath: "/hubs/tenant",
    serviceShortName: "tenant");
```

Wires:
- JWT Bearer auth with `platform_user_id` claim guard (matches WalletHub today)
- Redis backplane via SorchaConnections cascade
- `ChannelPrefix = sorcha:signalr:{serviceShortName}` so backplanes are isolated per service — Redis sees scoped keyspaces, no cross-service chatter
- Standard reconnect policy via shared `SorchaHubConnectionBuilder` (already exists; gets jitter added and adopted everywhere)
- OpenTelemetry instrumentation on the `Sorcha.SignalR` meter
- Storage registration log entry (so the Feature 113 audit knows the hub is live)
- Strongly-typed client interface required at compile time (no untyped `Hub` allowed)

### D3. Group naming with code-level builders

Group strings are never constructed inline. Each hub ships a static builder next to it:

```csharp
public static class TenantHubGroups
{
    public static string User(Guid platformUserId) => $"user:{platformUserId:N}";
    public static string Org(Guid orgId) => $"org:{orgId:N}";
    public const string SystemAll = "system:all";
}

public static class BlueprintHubGroups
{
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";
    public static string Instance(Guid instanceId) => $"instance:{instanceId:N}";
    public static string Org(Guid orgId) => $"org:{orgId:N}";
}

public static class WalletHubGroups
{
    public static string Wallet(string walletAddress) => $"wallet:{walletAddress}";
    public static string CitizenWallet(Guid platformUserId) => $"wallet:platform-user:{platformUserId:N}";
}

public static class RegisterHubGroups
{
    public static string Register(Guid registerId) => $"register:{registerId:N}";
}
```

The hub itself is the namespace — `wallet:{addr}` on BlueprintHub and `wallet:{addr}` on WalletHub are separate channels because each hub has its own backplane prefix.

Identifier formatting is uniform:
- GUIDs: `:N` format (no hyphens) for compact log lines
- Wallet addresses: bech32 as-is
- All identifiers are opaque to anyone who can't hit the matching detail endpoint

### D4. Thin-signal contract (formalised)

Every server→client event obeys this shape:

```csharp
public record HubSignal(
    string EventType,                 // "ActionAvailable", "InboxEntryAdded", etc.
    IReadOnlyList<string> Ids,        // all opaque
    DateTimeOffset OccurredAt,
    string TraceId);                  // W3C trace-id for following the chain
```

Events carry **only** IDs, the timestamp, and the trace token. No claims, no descriptions, no progress percentages, no balances. Each event type is paired with exactly one authenticated REST detail endpoint, documented in the hub's typed client interface XML doc. Code review rejects any property that isn't an ID, a timestamp, or a trace token.

ChatHub is exempt — it streams content by design.

Consequences:
- Backplane Redis carries no domain content (smaller blast radius if reads leak)
- Access logs of hub frames carry no PII or claims
- Hub-event size is predictable (~150 bytes)
- Rate-limiting is uniform across event types

The fix is also retroactive: today's `ActionNotification` carries action description and blueprint name, `CredentialNotification` carries issuer info, `EncryptionProgress` carries percentage. All of those get stripped to IDs; clients refetch from the matching REST endpoint.

### D5. User inbox as a Tenant-owned domain concept

The user-facing notification surface is a durable, queryable inbox in Tenant Service. Not a hub group, not a UI synthesis from in-flight events.

Entry shape:

```csharp
public record InboxEntry(
    Guid Id,
    Guid PlatformUserId,
    InboxCategory Category,        // Action | Credential | Membership | Security | System | Workflow | Custom
    InboxSeverity Severity,        // Info | Warning | ActionRequired | Critical
    string CorrelationKey,         // e.g. "tx:{walletAddress}:{txId}" or "membership:{orgId}"
    string DetailHref,             // authenticated REST endpoint to fetch render-ready content
    Guid SourceEventId,            // for de-dupe on writer retry
    DateTimeOffset OccurredAt,
    DateTimeOffset? ReadAt,
    DateTimeOffset? DismissedAt);
```

**Write-ownership rule:** the service that owns the domain concept writes the entry. Wallet writes credential entries; Blueprint writes action entries; Tenant writes membership / security / system entries. All writes route through one internal endpoint:

```
POST /api/internal/inbox          (RequireService policy)
GET  /api/me/inbox                (paginated, filterable)
GET  /api/me/inbox/{id}           (full entry + Detail GET via DetailHref)
GET  /api/me/inbox/unread-count
POST /api/me/inbox/{id}/read      (idempotent)
POST /api/me/inbox/{id}/dismiss   (idempotent)
POST /api/me/inbox/mark-all-read  (idempotent)
```

Idempotency on write is keyed by `(PlatformUserId, SourceEventId)`. Retries by the same writer collapse. Different writers writing related entries with the same correlation key produce separate rows — that's expected (one tx → action entry + credential entry).

**Granularity:** fine-grained. One entry per "user-relevant occurrence." A peer-replicated transaction that delivers a credential AND triggers an action produces two entries with the same correlation key. The UI groups visually when entries with the same correlation key arrive within ~30s of each other; outside that window they stand alone.

**Storage:** Postgres (Tenant DB) for the durable entries; Redis sorted-set per-user index for unread count (atomic increment on write, atomic clear on read/dismiss). Audited via the Feature 113 storage registration log; in-memory fallback gates Production.

**Realtime nudge:** `InboxEntryAdded(entryId)` and `InboxUnreadCountUpdated(n)` fire on TenantHub `user:{platformUserId:N}`. Feed history comes via REST. The hub never carries entry content — only the ID and the unread count.

### D6. Cross-cutting walkthrough — peer-replicated transaction

Validates that the per-domain hub model holds together. Org B's wallet receives a tx from Peer 1:

1. **Peer Service** replicates the docket. **Register Service** projects three Redis events; RegisterHub bridges → fires `DocketSealed` / `RegisterHeightUpdated` / `TransactionConfirmed` on `register:{id}`. *(operator dashboards update; no inbox entries.)*
2. **Wallet Service** receives the tx via InboundTransactionRouter, updates `WalletTransaction`, fires `TransactionReceived` on WalletHub `wallet:{addr}`. *(wallet detail tick goes blue; no inbox entry — ticks aren't notification-worthy on their own.)*
3. If the tx carries a credential → Wallet Service fires `CredentialReceived` on WalletHub **and** writes an inbox entry, category `Credential`, correlation `tx:{addr}:{txId}`. Tenant fires `InboxEntryAdded`.
4. If the tx triggers an action for the recipient → Blueprint Service fires `ActionAvailable` on BlueprintHub **and** writes an inbox entry, category `Action`, same correlation key. Tenant fires `InboxEntryAdded`.

User sees: one grouped notification card with two sub-items, individually dismissible. Operator sees: register dashboard ticking. Wallet page sees: tick going blue. No double-render anywhere.

### D7. Polling fallback as a first-class primitive

Every hub-backed UI surface must work degraded when the hub disconnects. The pattern exists today in exactly one place — `EncryptionProgressIndicator` falls back to polling `/api/operations/{id}` on `OnConnectionStateChanged` — and it's the right pattern. Standardise it.

`Sorcha.ServiceDefaults.Hubs` ships a `HubConnectionWithFallback<TClient>` wrapper that exposes:
- The typed hub client
- Connection-state observable
- A "you should poll now" hint with a recommended interval

Each consumer page provides an optional REST refresher. When the hub is up, refresher is dormant. When the hub disconnects beyond the reconnect window, refresher kicks in at a sensible default (15 s, jittered).

## Migration

Brownfield. Phased to avoid flag-day breakage:

| Phase | What lands |
|---|---|
| **1** | `Sorcha.ServiceDefaults.Hubs` extension. Reconnect policy unified with jitter. Redis backplane added to all five existing hubs *(this alone fixes the multi-node correctness bug — pays for itself before any feature change)*. Polling-fallback wrapper shipped. No event-shape change yet. |
| **2** | TenantHub + InboxEntry domain (Postgres table + Redis sorted-set unread index) + internal write API + `InboxEntryAdded` / `InboxUnreadCountUpdated` realtime. UI gets a notification bell that reads the inbox. Existing notification surfaces unchanged. |
| **3** | Migrate UI mechanical renames (9 files): MyActions, MyCredentials, MainLayout, EncryptionOperationTracker, EncryptionProgressIndicator, OperationNotificationListener, WalletDetail, plus the two register pages stay put. Rewrite ActivityLogPanel + PendingActionToast + PendingActionInbox to inbox-driven (3 files). Add WalletHub event coverage for credentials, encryption, transaction-receive, transaction-receipt. Retire EventsHub (parallel-fire for one release cycle, then delete). |
| **4** | Tighten thin-signal contract — rip remaining payload fields out of events; code-review gate goes on. RegisterHub gains `[Authorize]`. CLI `EventStreamService` rewritten to consume RegisterHub + BlueprintHub + WalletHub via `SorchaHubConnectionBuilder`. Agent's `SignalRInboxListener` retargeted to BlueprintHub. |
| **5** *(phase A correctness)* | Web push dispatcher reading `PlatformUser.PushSubscriptions`. Per-channel preferences (inbox / push / email / digest / off). Observability dashboard for end-to-end notification trace. Cross-tab coordination via `BroadcastChannel`. |

### Specific risks called out

1. **RegisterHub gaining `[Authorize]` is a flag day.** Old clients without a token will fail. Mitigation: ship the UI's `RegisterHubConnection` token-passing change one release before the server-side `[Authorize]` lands.
2. **Path renames break in-flight reconnections.** Keep `/actionshub` as an alias to `/hubs/blueprint` for one release cycle, log a deprecation warning, then remove. Same for `/hubs/events` (which we're removing entirely — graceful 410-with-message rather than 404).
3. **Backplane introduction is a behaviour change.** Today's single-replica deploys "work" because everything is local. Once enabled, hub sends fan to all replicas — any code that incorrectly assumed local-only delivery will reveal itself. The bridges all consume Redis already, which has the same fan-out shape, so risk is low; PR review still scans for it.
4. **Inbox writes are now a synchronous cross-service HTTP hop** on the action/credential creation path. At our event volume this is fine. If Tenant becomes slow we'd block Blueprint/Wallet work — mitigation when we get there is fire-and-forget queue or per-write timeout-with-retry. Not worth solving up front.
5. **Walkthrough agent binary** is shipped to customers (n1 etc.) and bakes the hub URL in. Path rename means a re-publish of `Sorcha.Agent`. The `/actionshub` alias gives the agent a graceful upgrade window.
6. **EventsHub retirement is the highest-risk single step** because UI + Blueprint server change in lockstep. The phase-3 parallel-fire window matters most here — for one cycle the same domain event fires on both EventsHub and the new home, UI consumes from new, EventsHub sees zero subscribers, then we delete.

## Out of scope

- Citizen Wallet PWA realtime — in flight as Feature 114 work elsewhere.
- ChatHub redesign — preserved as-is.
- New gRPC streaming patterns (peer fan-out) — separate concern.
- Email channel changes — Feature 112 architecture stands.
- Webhook outbound delivery to third parties.
- Cross-AI / cross-org notification federation.

## Touch list (final)

### Server-side

```
src/Services/Sorcha.Blueprint.Service/
  Program.cs                                       ← MapHub: drop Events; rename Actions → Blueprint; alias /actionshub
  Hubs/ActionsHub.cs                               → BlueprintHub.cs (typed client interface mandatory)
  Hubs/EventsHub.cs                                → DELETE after parallel-fire window
  Hubs/ChatHub.cs                                  ← unchanged; XML doc updated to mark exception
  Hubs/IBlueprintHubClient.cs                      ← NEW
  Hubs/BlueprintHubGroups.cs                       ← NEW
  Services/Implementation/NotificationService.cs   ← emit only workflow events; wallet events removed
  Services/Implementation/EventsHubNotificationBridge.cs   → DELETE
  Services/Implementation/InboxWriter.cs           ← NEW (calls Tenant POST /api/internal/inbox for action entries)

src/Services/Sorcha.Wallet.Service/
  Program.cs                                       ← unchanged; AddSorchaHub call updated
  Hubs/WalletHub.cs                                ← gain typed client interface; absorb encryption/credential/tx-receive events
  Hubs/IWalletHubClient.cs                         ← NEW
  Hubs/WalletHubGroups.cs                          ← NEW (already has GroupNameFor, formalise)
  Services/Implementation/NotificationDeliveryService.cs   ← retarget output to Tenant inbox endpoint
  Services/Implementation/NotificationDigestWorker.cs      ← retarget output to Tenant inbox endpoint
  Services/Implementation/DeviceRevocationService.cs       ← unchanged
  Services/Implementation/TransactionLifecycleEventBridge.cs   ← retarget output to WalletHub directly
  Services/Implementation/InboxWriter.cs           ← NEW (calls Tenant for credential entries)

src/Services/Sorcha.Register.Service/
  Program.cs                                       ← AddSorchaHub call; RegisterHub now [Authorize]
  Hubs/RegisterHub.cs                              ← add [Authorize]; group builder formalised
  Hubs/RegisterHubGroups.cs                        ← NEW
  Services/RegisterEventBridgeService.cs           ← unchanged

src/Services/Sorcha.Tenant.Service/
  Program.cs                                       ← NEW MapHub<TenantHub>
  Hubs/TenantHub.cs                                ← NEW
  Hubs/ITenantHubClient.cs                         ← NEW
  Hubs/TenantHubGroups.cs                          ← NEW
  Services/InboxService.cs                         ← NEW (durable storage + emit)
  Services/InboxBridgeService.cs                   ← NEW (Redis subscriber → inbox writes for upstream events)
  Endpoints/MeInboxEndpoints.cs                    ← NEW (GET /api/me/inbox*; POST .../{id}/read|dismiss)
  Endpoints/InternalInboxEndpoints.cs              ← NEW (POST /api/internal/inbox; RequireService)
  Models/InboxEntry.cs                             ← NEW
  Migrations/                                      ← NEW table for InboxEntry + indexes

src/Common/Sorcha.ServiceDefaults/Hubs/
  AddSorchaHubExtensions.cs                        ← NEW
  HubSignal.cs                                     ← NEW
  HubConnectionWithFallback.cs                     ← NEW (polling-fallback wrapper for clients)
  SorchaHubConventions.cs                          ← NEW (auth + backplane + tracing)

src/Common/Sorcha.ServiceClients.Http/Hub/
  SorchaHubConnectionBuilder.cs                    ← add jitter; surface connection-state observable
```

### UI-side (12 files)

```
src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/
  ActionsHubConnection.cs                          → split: BlueprintHubConnection + WalletHubConnection (workflow vs wallet delegates)
  EventsHubConnection.cs                           → DELETE after phase 3
  RegisterHubConnection.cs                         ← start passing JWT
  ChatHubConnection.cs                             ← unchanged
  TenantHubConnection.cs                           ← NEW
  EncryptionOperationTracker.cs                    ← inject WalletHubConnection (was ActionsHubConnection)

src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/
  EncryptionProgressIndicator.razor                ← rewire to WalletHubConnection
  OperationNotificationListener.razor              ← rewire to WalletHubConnection

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
  MyActions.razor                                  ← rewire to BlueprintHubConnection
  MyCredentials.razor                              ← rewire to WalletHubConnection
  Wallets/WalletDetail.razor                       ← rewire OnTransactionReceipted → WalletHub
  Registers/Detail.razor                           ← unchanged (RegisterHub stays)
  Registers/Index.razor                            ← unchanged
  Designer/Panes/AiDesignerPane.razor.cs           ← unchanged

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/
  MainLayout.razor                                 ← rewire dual-hub usage: WalletHub for credentials/count, TenantHub for inbox
  ActivityLogPanel.razor                           ← REWRITE: inbox-driven (was OnEventReceived)
  PendingActionToast.razor                         ← REWRITE: inbox-driven
  PendingActionInbox.razor                         ← REWRITE: IS the inbox UI
```

### Non-UI subscribers

```
src/Apps/Sorcha.Cli/Services/EventStreamService.cs ← rewrite to consume RegisterHub + BlueprintHub + WalletHub via SorchaHubConnectionBuilder
src/Apps/Sorcha.Cli/Commands/EventWatchCommand.cs  ← option set may grow
src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs ← retarget to /hubs/blueprint
```

### Tests

All hub integration tests need updates (10 files identified in audit). E2E tests that exercise hub flows.

## Open questions deferred to spec

- **Per-channel preferences UI** — phase 5 only; spec defers to a follow-up phase.
- **Cross-tab coordination details** — `BroadcastChannel` topology, whether to share a single hub connection across tabs or just the inbox state. Phase 5.
- **Web push payload shape** — VAPID-encrypted opaque ID + click-through, or VAPID-encrypted summary. Phase 5.
- **Inbox retention policy** — how long do entries live; do dismissed entries get GC'd. Spec.
- **Admin-broadcast UI** — there's no UI yet for an admin to push a `SystemAnnouncement`; the wire exists but the surface doesn't. Spec.
