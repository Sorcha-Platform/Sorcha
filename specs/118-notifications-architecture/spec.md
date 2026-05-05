# Feature Specification: Notifications & Realtime Architecture

**Feature Branch**: `118-notifications-architecture`
**Created**: 2026-05-05
**Status**: Draft
**Input**: User description: "Think through all notifications, alerts and realtime updated statuses. Architecture first — symmetry across services, formal naming, hub topology — then correctness. Citizen wallet PWA is being worked elsewhere; out of scope here."

## Context

Sorcha's notification, alert, and realtime status surfaces grew organically across 100+ phases. The result is functional but uneven. Five SignalR hubs exist (ActionsHub, EventsHub, ChatHub all in Blueprint Service; RegisterHub in Register; WalletHub in Wallet) with three different reconnect policies, two different group-naming conventions, only one hub using a typed client interface, and one hub (RegisterHub) accepting unauthenticated websocket connections. EventsHub is a cross-domain leak — its events span workflow, wallet, and a generic `EventReceived` aggregator — and it lives in Blueprint Service only because that's where it grew. There is no Redis backplane on any hub, despite the project rule that everything assume multi-node deployment; group fan-out on a load-balanced gateway silently misses every connection on the replica that didn't process the publish. Web Push subscriptions are stored in `PlatformUser.PushSubscriptions` but no service ever dispatches a payload — the only true mobile/background channel is dead. Group-name strings are constructed inline in dozens of call sites with no central builder; one typo is a silent miss.

This spec rationalises the layer. The shape is hub-per-service with one deliberate exception (ChatHub stays separate because its wire shape is RPC-streamy, not fan-out), a common `AddSorchaHub` extension that wires auth + Redis backplane + reconnect-with-jitter + tracing identically across services, code-level group-name builders, a thin-signal contract that forbids domain payload on the wire, and a Tenant-owned durable user inbox that becomes the canonical user-facing notification surface. EventsHub retires; ActionsHub renames to BlueprintHub; its wallet-domain events move to WalletHub where they always belonged. The work is brownfield and phased to avoid flag-day breakage; specifically, hub URL aliases survive one release cycle and `[Authorize]` on RegisterHub lands a release after the UI client starts sending its token.

The full design rationale and decision history live at `docs/superpowers/specs/2026-05-05-notifications-architecture-design.md`. Citizen Wallet PWA realtime work (Feature 114) is explicitly out of scope.

**Numbering note.** Highest existing spec is 117; 118 is the next sequential number.

**Related specs.**
- **Builds on** spec 113 (Storage Durability Audit) — the new TenantHub `InboxEntry` domain and the hub backplane Redis use are both audited via `IStorageRegistrationLog`. Production fail-fast applies.
- **Builds on** spec 112 (Email Sweep) — does not change email; clarifies that email is one channel of a multi-channel notification model whose other channels (inbox, push, hub) this spec defines.
- **Builds on** spec 062 (Pending Action Notifications) and spec 069 (Pending Actions UX) — those specs introduced the action-notification flow that is now refactored onto the new architecture.
- **Independent of** Feature 114 (Citizen Wallet PWA) — citizen wallet's WalletHub group `wallet:platform-user:{guid:N}` and its `DeviceRevoked`/`CredentialAvailable` events are preserved unchanged.
- **Required by** any future work on web push delivery, cross-tab coordination, or per-channel notification preferences — those are deliberately deferred to a phase 5 follow-up but are unblocked by what this spec lands.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A multi-node deploy delivers every hub message to every connected client (Priority: P1)

Sorcha runs behind YARP with two replicas of Blueprint Service. A wallet user connects to the new BlueprintHub through replica A. A workflow event for that wallet originates on replica B (the request that produced it was routed there). Today the hub send on replica B never reaches the websocket on replica A; the user gets nothing until the next page refresh. After this spec ships, the same scenario delivers the event to the user within 200 ms because all hubs use a shared Redis backplane with per-service channel isolation.

**Why this priority**: This is a correctness bug, not a feature. The project's CLAUDE.md mandates multi-node assumption; the only reason single-replica deploys appear to work is that they don't exercise the bug. As soon as production scales horizontally, half of every hub fan-out is silently lost. P1 because every other story in this spec assumes hubs deliver reliably.

**Independent Test**: Deploy two replicas of one service hosting a hub (e.g., Blueprint). Open two browsers; both connect to a wallet group; force one to replica A and the other to replica B (sticky-session header or YARP routing). Trigger an event on whichever replica did *not* serve the connecting user. Assert both browsers receive the event within 200 ms p95. Repeat for every hub.

**Acceptance Scenarios**:

1. **Given** two replicas of a hub-hosting service and a Redis instance reachable from both, **When** the service starts, **Then** the SignalR backplane registers with Redis under channel prefix `sorcha:signalr:{serviceShortName}` and the storage registration log records the backplane as `Persistent`.
2. **Given** clients A and B connected to the same hub group via different replicas, **When** any service publishes to that group, **Then** both clients receive the event.
3. **Given** Redis is unreachable at startup in `Production` or `Staging`, **When** the service attempts to start, **Then** startup fails fast with an actionable log message naming the missing connection string (`ConnectionStrings:{Service}:Redis` → `ConnectionStrings:Sorcha:Redis`).
4. **Given** Redis becomes unreachable while the service is running, **When** the next hub send is attempted, **Then** the failure is surfaced via OpenTelemetry (`sorcha_signalr_backplane_state` gauge) and a structured log entry; the service continues to deliver to local-replica connections (degraded mode) without crashing.
5. **Given** the per-service `ChannelPrefix` configuration, **When** Redis pub/sub traffic is inspected, **Then** each service's backplane traffic is on a distinct keyspace and no cross-service chatter is observed.

---

### User Story 2 — Every service hosts at most one notification hub, with one documented exception (Priority: P1)

A new engineer joining the team can read `src/Services/*/Hubs/` and see exactly one `*Hub.cs` per service, each wired through the same `AddSorchaHub<THub, TClient>` extension call in `Program.cs`, each with a typed client interface, each with a static group-name builder beside it. ChatHub remains as the one explicitly-marked exception (RPC-streaming AI Designer); its file carries an XML doc explaining why. The earlier mix of three hubs in Blueprint Service, two reconnect policies, and one untyped client is gone.

**Why this priority**: Hub sprawl is the architectural debt this spec exists to repay. Without a clean topology, every other improvement (backplane, contract, naming) attaches to a moving target. P1 because the migration steps that follow assume the topology decisions are settled and the symmetry holds.

**Independent Test**: From a fresh checkout, run a script that enumerates every type extending `Hub` or `Hub<T>` across `src/Services/`, asserts exactly five matches (TenantHub, BlueprintHub, WalletHub, RegisterHub, ChatHub), asserts every match except ChatHub uses `AddSorchaHub` in its service's `Program.cs`, and asserts ChatHub's class XML doc contains the marker `<remarks>Deliberate exception to one-hub-per-service rule</remarks>`.

**Acceptance Scenarios**:

1. **Given** the codebase post-migration, **When** `src/Services/*/Hubs/*.cs` is enumerated, **Then** five hub classes exist: `TenantHub`, `BlueprintHub`, `WalletHub`, `RegisterHub`, `ChatHub`.
2. **Given** any of the five hubs, **When** its declaration is inspected, **Then** it inherits from `Hub<TClient>` with a typed client interface defined in the same folder.
3. **Given** the four notification hubs (everything except ChatHub), **When** their service's `Program.cs` is inspected, **Then** registration goes through exactly one `services.AddSorchaHub<THub, TClient>(...)` call.
4. **Given** the previous EventsHub at route `/hubs/events`, **When** the service responds to the route after the migration cycle completes, **Then** it returns HTTP 410 Gone with a JSON body naming the replacement hubs and the deprecation date.
5. **Given** the previous ActionsHub route `/actionshub`, **When** a client connects during the deprecation window, **Then** the connection succeeds (route aliased to `/hubs/blueprint`) and a structured deprecation log is emitted; after the deprecation window the route returns 410 Gone.
6. **Given** ChatHub, **When** its source file is read, **Then** the class XML doc explicitly marks it as the deliberate exception and links to this spec.

---

### User Story 3 — A user-relevant notification produces exactly one durable inbox entry the user can read, dismiss, and revisit across reloads (Priority: P1)

A peer-replicated transaction arrives at Org B. It carries a credential issuance for the recipient and triggers a workflow action they must respond to. The user, currently looking at a different page, sees the notification bell badge increment. They click; the inbox panel opens; they see a grouped card with two sub-items ("New credential from Org A — Verified Citizen", "Action required: acknowledge receipt"), each with its own dismiss button. They dismiss the credential entry, navigate away, and refresh the page. The inbox shows only the action entry remaining. They never lost a notification because they were briefly disconnected from SignalR — the entries are durable in Tenant Service and the realtime hub event was only a nudge to refresh the unread count.

**Why this priority**: Today the activity feed is fed by a generic `EventReceived` event over a Redis pub/sub bridge. Pub/sub is at-most-once; anyone disconnected at the moment of fire loses the event entirely. There is no inbox UI, no read/unread state, no dismiss state, no replay across reloads. P1 because the UI surfaces (`ActivityLogPanel`, `PendingActionInbox`, `PendingActionToast`) that today consume `OnEventReceived` need a durable backing model before EventsHub retires; this story provides it.

**Independent Test**: Submit a workflow that emits an inbox-worthy event for a target user. Disconnect that user's browser from SignalR (block websockets in DevTools). Wait 5 s. Restore the connection. Assert the user's inbox shows the entry and the unread count is correct. Reload the page; assert the entry is still present. Dismiss the entry; reload; assert it is no longer present (or filtered as dismissed). Repeat with two related entries firing within 30 s with the same correlation key; assert the UI groups them visually but each is individually dismissible.

**Acceptance Scenarios**:

1. **Given** Tenant Service running with the Postgres `InboxEntry` table provisioned, **When** any service POSTs to the internal endpoint `POST /api/internal/inbox` with a valid `InboxEntry` payload and a `RequireService` token, **Then** the entry is persisted, the per-user unread count is atomically incremented in the Redis sorted-set index, `InboxEntryAdded(entryId)` and `InboxUnreadCountUpdated(n)` fire on TenantHub group `user:{platformUserId:N}`.
2. **Given** the same internal endpoint, **When** a duplicate POST arrives with the same `(PlatformUserId, SourceEventId)`, **Then** the second write is a no-op (HTTP 200 with `idempotent: true`) and no second hub event fires.
3. **Given** an authenticated user, **When** they call `GET /api/me/inbox`, **Then** they receive their own entries (paginated, filterable by category and read/unread state) and never another user's.
4. **Given** an entry, **When** the user calls `POST /api/me/inbox/{id}/read`, **Then** the entry's `ReadAt` is set, the unread count is atomically decremented, `InboxUnreadCountUpdated` fires; subsequent reads of the same endpoint are no-ops.
5. **Given** an entry, **When** the user calls `POST /api/me/inbox/{id}/dismiss`, **Then** the entry's `DismissedAt` is set; default `GET /api/me/inbox` views exclude dismissed entries.
6. **Given** two entries with the same `CorrelationKey` written within 30 s of each other to the same user, **When** the inbox UI renders, **Then** they appear as a single grouped card with sub-items, each individually dismissible.
7. **Given** a peer-replicated transaction that produces both an `Action` and a `Credential` inbox entry on the same correlation key, **When** the recipient's inbox is queried, **Then** exactly two entries exist (one per category), grouped in the UI, neither duplicated.
8. **Given** Production deployment without a Tenant Postgres connection, **When** the service starts, **Then** startup fails fast (audited via Feature 113 storage registration log); in-memory fallback is rejected unless `Storage:AllowInMemoryInProduction=true` is set.

---

### User Story 4 — Hub events on the wire carry only opaque IDs; details fetch via authenticated REST (Priority: P1)

A backplane Redis cluster is read by an operator with valid Redis credentials but no application JWT. They observe SignalR backplane traffic flowing past. Every message is shaped `{ eventType, ids[], occurredAt, traceId }` — no claim values, no descriptions, no balances, no PII. To learn anything beyond an event having occurred, they must hit the matching authenticated REST detail endpoint with a JWT scoped to that user. The blast radius of a backplane read is "events happened" rather than "this is what's in them."

**Why this priority**: Today's events leak. `ActionNotification` carries blueprint name and action description; `CredentialNotification` carries issuer info; `EncryptionProgress` carries percentage. The backplane Redis introduced in US1 will see all of this content. P1 because the contract change is cheapest *before* backplane goes live with current shapes — once contract tightens, the backplane is content-free from day one.

**Independent Test**: Subscribe to the backplane Redis as an external observer. Trigger one of every defined event type from each hub. Assert no message body contains any field other than `EventType`, `Ids[]`, `OccurredAt`, `TraceId`. Assert each event type has a documented `DetailHref` pattern in its typed client interface XML doc. Assert the matching authenticated REST endpoint exists and requires a JWT scoped to the recipient.

**Acceptance Scenarios**:

1. **Given** the `HubSignal` envelope type defined in `Sorcha.ServiceDefaults.Hubs`, **When** any new event method is added to a typed client interface, **Then** its parameters compile only as `string`, `IReadOnlyList<string>`, `DateTimeOffset`, or other ID-like types; descriptive payload properties fail code review.
2. **Given** the four notification hubs, **When** every event method on every typed client interface is enumerated, **Then** each has an XML doc `<see cref="..." />` linking to its detail REST endpoint.
3. **Given** any backplane Redis pub/sub message, **When** its body is parsed, **Then** it deserialises cleanly into `HubSignal` with no extra fields.
4. **Given** ChatHub specifically, **When** its event methods are inspected, **Then** they may carry content payloads (this is the documented exception for streaming) and that exception is called out in the hub's XML doc.

---

### User Story 5 — Group names are built through a single typed builder, not constructed inline (Priority: P2)

A developer adding a new event to TenantHub goes to `TenantHubGroups` and either reuses an existing builder method or adds one with a clear name and signature. They never type `$"user:{platformUserId:N}"` in a service implementation. A grep for `"user:"` across the codebase returns only the builder definition and tests; no string concatenation in service code matches. Adding a new group domain (say `"tenant:{id}"`) is a one-place change that the compiler verifies.

**Why this priority**: Today's group strings are constructed inline in dozens of places — `wallet:{address}` (Actions) vs `wallet:platform-user:{guid:N}` (Wallet) vs `user:{userId}` (Events) — with no central definition. One typo is a silent miss; one rename touches every call site. P2 because the bug surface is small *if* you avoid typos, but the bug is undetectable when it happens. Lands alongside US2 since the topology refactor touches every group string anyway.

**Independent Test**: Run a grep over `src/Services/` and `src/Apps/` for the literal patterns `"wallet:`, `"register:`, `"user:`, `"org:`, `"instance:`, `"system:` outside of files matching `*HubGroups.cs` or `*Tests.cs`. The match count is zero. Compilation fails if any builder method's return type is changed to a non-string (verifying the type contract).

**Acceptance Scenarios**:

1. **Given** the migration complete, **When** `src/Services/*/Hubs/*HubGroups.cs` is enumerated, **Then** one builder file per hub exists, each containing only static methods returning `string`.
2. **Given** any service implementation calling `IHubContext<THub>.Clients.Group(...)`, **When** the call site is inspected, **Then** the group string argument is a call to a `*HubGroups` method, not a string literal or interpolation.
3. **Given** any UI client wrapper calling `connection.InvokeAsync("SubscribeTo*", ...)`, **When** the call site is inspected, **Then** any group identifier passed to the server is built via the corresponding `*HubGroups` method.

---

### User Story 6 — Hub-backed UI surfaces degrade to polling when the websocket is down (Priority: P2)

A user is on the wallet detail page when their network briefly drops. The realtime tick indicators stop updating immediately, but within ~15 s a polling fallback engages and refreshes the same data via REST at a reasonable cadence. When the websocket reconnects, polling stops and realtime takes over again. The user sees a small "Reconnecting…" indicator during the gap but never a stale or missing tick. The same pattern is in effect on every hub-backed surface, not just the encryption progress indicator that has it today.

**Why this priority**: Today exactly one place — `EncryptionProgressIndicator` — falls back to polling on `OnConnectionStateChanged`. Every other surface freezes silently when the hub disconnects. P2 because the surface still works correctly when the hub is up; the failure mode is only visible in real network conditions. Standardising the pattern through a `HubConnectionWithFallback<TClient>` wrapper costs less than building one-off fallbacks per page later.

**Independent Test**: Pick three hub-backed UI surfaces. For each, open the page, confirm realtime updates work, then block websockets in DevTools. Within 20 s, confirm the surface starts polling its REST refresher endpoint. Restore websockets; within 20 s, confirm polling stops and realtime resumes. Confirm no UI flicker, no error toast, no console error during either transition.

**Acceptance Scenarios**:

1. **Given** the `HubConnectionWithFallback<TClient>` wrapper shipped in `Sorcha.ServiceDefaults.Hubs`, **When** a UI consumer instantiates it with a typed hub connection and an optional REST refresher delegate, **Then** the wrapper exposes connection-state observable, a "you should poll now" hint, and the typed hub client itself.
2. **Given** any hub-backed UI surface, **When** the websocket disconnects beyond the reconnect window, **Then** the surface's REST refresher engages at 15 s default cadence (jittered ±20%).
3. **Given** the same surface, **When** the websocket reconnects, **Then** polling halts within one cadence cycle and realtime updates resume.
4. **Given** the wrapper's fallback REST refresher returns an error, **When** three consecutive polls fail, **Then** an inline UI affordance surfaces ("connection lost, last updated …") without a blocking toast.

---

### User Story 7 — Hub-emitted notifications produce push notifications and respect per-channel preferences (Priority: P3 — phase 5 deferral)

A user has the Sorcha web app open in a browser tab, the citizen wallet on their phone, and email notifications enabled. A workflow action is created targeting them. They see the inbox bell increment in the open tab; their phone shows a web push notification; their email has a digest entry queued (they prefer digest for non-urgent items). They tap the push notification; it deep-links into the action page. They had previously turned off email for `Membership` category but kept push on; that preference is honoured.

**Why this priority**: Web push subscriptions exist in the database but no service dispatches them — the only true mobile/background channel is dead. Per-channel preferences exist as a partial check inside `NotificationDeliveryService` but no user-facing UI. P3 because it is a follow-on to the architectural foundation; this spec lands the foundation and explicitly defers the channel-mux and preferences UI. Tracked here so the deferral is visible and so the foundation does not preclude it.

**Why this story remains in scope of this spec**: To lock in the inbox-write-time decision tree (which channels apply to a given entry) so that phase 5 is purely UI + dispatcher work, not a data-model migration.

**Independent Test**: Deferred to phase 5. This spec asserts only that the inbox write API accepts a per-entry channel hint set (`{ inbox, push, email, digest }`) and that the persisted entry stores it; the dispatchers are out of scope here.

**Acceptance Scenarios** (foundation-only):

1. **Given** the `InboxEntry` record, **When** persisted, **Then** it carries an optional `ChannelHints` field listing the channels that should apply if no overriding user preference exists.
2. **Given** the internal write endpoint, **When** a writer omits `ChannelHints`, **Then** a default hint set is applied based on `Category` (e.g., `Action` → `inbox + push + email`; `Workflow` → `inbox` only).
3. **Given** the platform user's preference structure, **When** persisted preferences override the writer's hints, **Then** the resolved channel set for that entry is `hints ∩ preferences-allowed`.
4. **Given** phase 5 has not yet shipped, **When** the system runs, **Then** only the `inbox` channel is delivered; `push` and `email` and `digest` channels are recorded on the entry but not dispatched.

---

### Edge Cases

- **Duplicate writer retries.** A service crashes after writing an inbox entry but before its outer transaction commits, then retries. The `(PlatformUserId, SourceEventId)` uniqueness index ensures the second write is a no-op.
- **Two writers race on the same correlation key.** Action service writes `Action` entry; Wallet service writes `Credential` entry; both with `tx:{addr}:{txId}` correlation. Both succeed (different `SourceEventId`); UI groups visually.
- **Cross-replica unread-count drift.** The Redis sorted-set index is the single source of truth for unread counts; replicas read from it on every change rather than caching locally. A replica failure does not skew the count.
- **Backplane Redis is unreachable.** Hub fan-out degrades to local-replica delivery. `sorcha_signalr_backplane_state` gauge flips to `down`; alert fires; functionality is reduced but does not fail (matches today's behaviour).
- **RegisterHub auth flag day.** A UI client built before the `[Authorize]` change connects without a token after the change lands. Connection rejected with HTTP 401. Mitigated by shipping the UI's token-passing change one release earlier; the flag-day is a deliberate planned cutover.
- **Walkthrough agent on stale binary.** An older `Sorcha.Agent` binary connects to `/actionshub`. During the deprecation window it succeeds via the alias to BlueprintHub; after the window closes it fails with HTTP 410 + actionable JSON body naming the new path.
- **EventsHub parallel-fire window.** During phase 3, the same domain event fires on both EventsHub and the new home (BlueprintHub or WalletHub). UI consumes from new only. Observable via `sorcha_signalr_events_hub_subscribers` gauge dropping to zero before EventsHub is removed.
- **Inbox storage exhaustion.** Phase 5 retention policy out of scope for this spec; entries accumulate indefinitely. A follow-up phase trims on age + dismissed-state.
- **Citizen wallet PWA unaffected.** Feature 114's `wallet:platform-user:{guid:N}` group on WalletHub and its `DeviceRevoked`/`CredentialAvailable` events are preserved; the citizen wallet codebase is not touched by this spec.

## Functional Requirements

**Hub topology and registration**

- **FR-001**: There MUST be exactly five hub classes: `TenantHub`, `BlueprintHub`, `WalletHub`, `RegisterHub`, and `ChatHub`.
- **FR-002**: TenantHub MUST be hosted by Tenant Service at route `/hubs/tenant`. BlueprintHub MUST be hosted by Blueprint Service at route `/hubs/blueprint`. WalletHub MUST be hosted by Wallet Service at route `/hubs/wallet`. RegisterHub MUST be hosted by Register Service at route `/hubs/register`. ChatHub MUST remain at `/hubs/chat`.
- **FR-003**: ActionsHub MUST be renamed to BlueprintHub. The route `/actionshub` MUST be aliased to `/hubs/blueprint` for one release cycle (deprecated, logged) then return HTTP 410 Gone.
- **FR-004**: EventsHub MUST be retired. Its workflow events fold into BlueprintHub; its wallet events fold into WalletHub; its user-feed events become inbox entries on TenantHub. The route `/hubs/events` MUST return HTTP 410 Gone after a one-release-cycle parallel-fire deprecation window.
- **FR-005**: Each of the four notification hubs MUST be registered through `services.AddSorchaHub<THub, TClient>(configuration, routePath, serviceShortName)` from `Sorcha.ServiceDefaults.Hubs`. ChatHub is exempt from this requirement and MUST carry an XML doc marking it as the deliberate exception.
- **FR-006**: Each notification hub MUST inherit from `Hub<TClient>` with a typed client interface defined in the same folder.

**Authentication and backplane**

- **FR-007**: `AddSorchaHub` MUST require JWT Bearer authentication and validate the `platform_user_id` claim on `OnConnectedAsync`, aborting connections that lack it.
- **FR-008**: `AddSorchaHub` MUST register the SignalR Redis backplane using the SorchaConnections cascade (`ConnectionStrings:{Service}:Redis` → `ConnectionStrings:Sorcha:Redis`).
- **FR-009**: The backplane MUST set `ChannelPrefix = sorcha:signalr:{serviceShortName}` so cross-service backplane traffic is isolated.
- **FR-010**: The backplane registration MUST record itself with `IStorageRegistrationLog` as `Persistent` when Redis is configured. Production and Staging MUST fail-fast at startup if the backplane resolves to an in-memory implementation, audited via Feature 113.
- **FR-011**: RegisterHub MUST require `[Authorize]` after a one-release deprecation cycle (UI client ships token-passing first).
- **FR-012**: The backplane MUST expose `sorcha_signalr_backplane_state` as an OpenTelemetry gauge (`up` / `degraded` / `down`).

**Group naming**

- **FR-013**: Each hub MUST ship a static `*HubGroups` class beside it containing only `static` methods returning `string` (or `const string` literals for fixed groups).
- **FR-014**: Service implementation code MUST construct group strings only via `*HubGroups` methods. String literals or interpolations matching `"wallet:`, `"register:`, `"user:`, `"org:`, `"instance:`, `"system:` outside of `*HubGroups.cs` or test files are not permitted.
- **FR-015**: GUID identifiers in group names MUST use `:N` formatting (no hyphens). Wallet addresses MUST use bech32 as-is.

**Thin-signal contract**

- **FR-016**: A `HubSignal` record MUST be defined in `Sorcha.ServiceDefaults.Hubs` with shape `(string EventType, IReadOnlyList<string> Ids, DateTimeOffset OccurredAt, string TraceId)`.
- **FR-017**: Every event method on every notification hub's typed client interface MUST take parameters compatible with `HubSignal` semantics: identifiers (string / GUID / int), timestamps, or trace tokens. Descriptive content (claims, descriptions, percentages, balances) MUST NOT appear in event parameters.
- **FR-018**: Every event method on every notification hub's typed client interface MUST carry an XML doc `<see cref="..." />` linking to its authenticated REST detail endpoint.
- **FR-019**: ChatHub is exempt from FR-017 and FR-018 (streaming content payloads are part of its design); the exemption MUST be documented in its class XML doc.

**User inbox domain (Tenant Service)**

- **FR-020**: Tenant Service MUST expose an `InboxEntry` durable record persisted in Postgres with fields: `Id` (GUID), `PlatformUserId` (GUID), `Category` (enum), `Severity` (enum), `CorrelationKey` (string), `DetailHref` (string), `SourceEventId` (GUID), `OccurredAt` (timestamp), `ReadAt` (nullable timestamp), `DismissedAt` (nullable timestamp), `ChannelHints` (set, default per-category).
- **FR-021**: A unique index MUST exist on `(PlatformUserId, SourceEventId)` so duplicate writer retries collapse.
- **FR-022**: Tenant Service MUST expose `POST /api/internal/inbox` gated by the `RequireService` policy. Idempotent on the unique index.
- **FR-023**: Tenant Service MUST expose `GET /api/me/inbox` (paginated, filterable by category and read state), `GET /api/me/inbox/{id}`, `GET /api/me/inbox/unread-count`, `POST /api/me/inbox/{id}/read`, `POST /api/me/inbox/{id}/dismiss`, `POST /api/me/inbox/mark-all-read`. All scoped to the calling user.
- **FR-024**: Per-user unread count MUST be tracked in a Redis sorted-set index, atomically incremented on write and decremented on read/dismiss. The Redis index MUST be audited via `IStorageRegistrationLog`.
- **FR-025**: TenantHub MUST emit `InboxEntryAdded(entryId)` and `InboxUnreadCountUpdated(unreadCount)` to group `user:{platformUserId:N}` on every inbox state transition.
- **FR-026**: The hub events MUST carry only IDs and counts per FR-017; full entry content MUST be fetched via `GET /api/me/inbox/{id}`.
- **FR-027**: When two entries share a `CorrelationKey` and arrive within 30 s of each other on the same user, the inbox UI MUST render them as a single grouped card with sub-items, each individually dismissible. Outside that window they render standalone.
- **FR-028**: Production and Staging MUST fail-fast at startup if `InboxEntry` storage resolves to an in-memory implementation, audited via Feature 113.

**Write ownership**

- **FR-029**: Wallet Service MUST write inbox entries for credential-domain events (categories: `Credential`).
- **FR-030**: Blueprint Service MUST write inbox entries for workflow-domain events that warrant user-facing notification (categories: `Action`, `Workflow`).
- **FR-031**: Tenant Service MUST write inbox entries for identity, membership, security, and system-message events (categories: `Membership`, `Security`, `System`).
- **FR-032**: No service MUST write inbox entries to a category outside its domain ownership.

**UI client and polling fallback**

- **FR-033**: `Sorcha.ServiceDefaults.Hubs` MUST ship `HubConnectionWithFallback<TClient>` exposing the typed hub client, a connection-state observable, and a poll-now hint with default cadence 15 s ±20% jitter.
- **FR-034**: Existing UI hub-backed surfaces MUST be migrated to consume their hub via `HubConnectionWithFallback<TClient>` where a REST refresher exists.
- **FR-035**: `SorchaHubConnectionBuilder` MUST add reconnect jitter (±20% on each backoff step) to avoid thundering-herd reconnect after deploys.

**Migration sequencing**

- **FR-036**: The migration MUST proceed in phases as outlined in the design doc: (1) ServiceDefaults extension + backplane + jitter; (2) TenantHub + InboxEntry; (3) UI rewires + EventsHub retirement; (4) thin-signal tightening + RegisterHub `[Authorize]` + Agent/CLI updates; (5 — out of scope) push dispatcher + preferences UI.
- **FR-037**: Each migration phase MUST land as one or more atomic PRs that pass build, tests, and the multi-node correctness independent test.
- **FR-038**: During phase 3, the same domain event MUST fire on both EventsHub and its new home for at least one release cycle. EventsHub MUST be removed only after `sorcha_signalr_events_hub_subscribers` gauge stays at zero for the entire window.

**Observability**

- **FR-039**: `Sorcha.SignalR` OpenTelemetry meter MUST expose: `sorcha_signalr_connections_total{hub}` (counter), `sorcha_signalr_messages_sent_total{hub,event_type}` (counter), `sorcha_signalr_backplane_state{service}` (gauge), `sorcha_signalr_reconnects_total{hub,reason}` (counter).
- **FR-040**: Every hub event MUST propagate W3C `traceId` so a notification can be traced from origin service through Redis backplane to client receipt.

## Non-Functional Requirements

- **NFR-001**: Hub-event end-to-end latency from emit-on-source-replica to receive-on-client MUST be ≤ 200 ms p95 in a two-replica deployment with backplane Redis under nominal load.
- **NFR-002**: Inbox-entry write to `InboxEntryAdded` hub-event delivery MUST be ≤ 300 ms p95.
- **NFR-003**: `GET /api/me/inbox` for the first page (20 entries) MUST respond in ≤ 100 ms p95 against the Tenant Postgres instance with 10⁵ entries per user.
- **NFR-004**: Hub event payload size MUST be ≤ 200 bytes p99 (verifies thin-signal contract holds).
- **NFR-005**: Backplane Redis pub/sub bandwidth MUST be ≤ 1 KB/s/connection p95 under nominal notification load.

## Out of Scope

- Citizen Wallet PWA realtime work (Feature 114 in flight).
- ChatHub redesign or RPC-streaming changes.
- gRPC streaming patterns (peer fan-out — separate concern).
- Email channel changes (Feature 112 architecture stands).
- Outbound webhook delivery to third parties.
- Cross-AI / cross-org notification federation.
- Web push payload dispatcher implementation (deferred to phase 5).
- Per-channel notification preferences UI (deferred to phase 5).
- Cross-tab coordination via `BroadcastChannel` (deferred to phase 5).
- Inbox retention / GC policy (deferred to a follow-up).

## Dependencies

- **Postgres** for the durable `InboxEntry` table on the Tenant DB; standard EF Core migrations.
- **Redis** for the SignalR backplane (per service) and the unread-count sorted-set index. Sourced via SorchaConnections cascade. Already deployed.
- **Feature 113** storage registration log — the new TenantHub `InboxEntry` storage and the per-service backplane registration both register through `IStorageRegistrationLog`.
- **`Sorcha.ServiceDefaults`** — the `Hubs` subnamespace is a new public surface in this assembly.
- **Existing typed-client interface** for RegisterHub (`IRegisterHubClient`) — pattern adopted across all four notification hubs.

## Success Criteria

- Multi-node correctness independent test (US1) passes for every hub.
- `src/Services/*/Hubs/` enumerates exactly five hub classes (US2).
- Grep for inline group-string literals across `src/` returns zero matches outside `*HubGroups.cs` and tests (US5).
- Backplane Redis traffic inspection shows zero domain content; only `HubSignal`-shaped envelopes (US4).
- Disconnect-and-reload workflow on the inbox shows full entry persistence and correct unread count (US3).
- WebSocket-disconnect simulation on three different UI surfaces shows polling fallback engages within 20 s and disengages on reconnect (US6).
- All ten hub-related tests in the audit pass after migration; no new categories of hub-related test failure.
- The deprecated `/actionshub` and `/hubs/events` routes return 410 Gone with actionable JSON bodies after their respective deprecation windows close.
- OpenTelemetry dashboards show `sorcha_signalr_*` metrics for every hub on every replica.
- Production and Staging refuse to start when any audited storage interface (`IInboxStore`, the unread-count index) resolves to in-memory.
