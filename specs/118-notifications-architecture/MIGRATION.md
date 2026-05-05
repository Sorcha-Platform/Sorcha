# Feature 118 Migration Notes

This document tracks the metric-gated and time-gated migration steps for the notifications & realtime architecture refactor. Each entry names the deprecated surface, the replacement, the gating signal, and the planned removal phase.

## `/actionshub` → `/hubs/blueprint`

**Status (after Phase 4 / US2):** alias active.

**Deprecation reason:** Hub renamed from `ActionsHub` to `BlueprintHub` to align route name with service domain (Phase 4 / US2 / spec FR-003).

**Behaviour:**
- Old clients continue to negotiate at `/actionshub` and connect successfully — Blueprint Service registers an explicit `app.MapHub<BlueprintHub>("/actionshub").RequireAuthorization()` alongside the canonical `MapSorchaHubs()` entry at `/hubs/blueprint`.
- A `Deprecation` HTTP header is set on `/actionshub` responses (planned for Phase 4 follow-up — the route alias is sufficient for v1).

**Removal:** Phase 10 polish. After the alias has been live for one full release cycle, the explicit `MapHub<BlueprintHub>("/actionshub")` line is replaced with `MapGet("/actionshub", ...)` returning HTTP 410 Gone with a JSON body naming `/hubs/blueprint` as the replacement.

**Observability:** `sorcha_signalr_connections_total{hub="blueprint",route="/actionshub"}` (planned). Operators monitor that this counter trends to zero before the alias removes.

## `EventsHub` retirement

**Status (after Phase 4 / US2):** parallel-fire window open.

**Deprecation reason:** EventsHub is a cross-domain leak — its events span workflow (`InboundActionReceived`), wallet (`EncryptionOperationCompleted`), and a generic `EventReceived` aggregator. Per spec FR-004, its workflow events fold into BlueprintHub, its wallet events fold into WalletHub, and its user-feed events become `InboxEntry` rows on TenantHub.

**Behaviour during the window:**
- EventsHub continues to accept connections and fan out its current events.
- Phase 5 (US3) lands inbox writers on Wallet/Blueprint that POST to `/api/internal/inbox` and lets TenantHub fire `InboxEntryAdded`.
- Phase 5 also migrates the wallet-domain events (encryption progress / complete / failed) onto WalletHub.
- During the cycle, both EventsHub and the new homes fire the same events. UI consumers migrate to the new homes incrementally (Phase 10 polish).

**Removal gate:** `sorcha_signalr_events_hub_subscribers` (gauge, instrumented by EventsHub `OnConnectedAsync` / `OnDisconnectedAsync` via `SignalRMetrics`) MUST be zero across all replicas for one full release cycle. After that:
- `app.MapHub<EventsHub>("/hubs/events")` is removed.
- `/hubs/events` is replaced with `MapGet(...)` returning HTTP 410 Gone with a JSON body naming the replacement hubs (TenantHub for inbox / unread; BlueprintHub for action signals; WalletHub for encryption + credentials).
- `EventsHub.cs`, `IEventsHubClient.cs`, and `EventsHubNotificationBridge.cs` are deleted.

**Observability:** Grafana panel watching `sorcha_signalr_events_hub_subscribers` (Phase 10 polish ships the dashboard JSON).

## RegisterHub `[Authorize]` cutover

**Status (after Phase 4 / US2):** **not started** — staged for Phase 6 (US4).

**Deprecation reason:** RegisterHub currently accepts unauthenticated websocket connections; subscription is gated by a server-side check but the hub itself does not require auth. Spec FR-011 closes this hole.

**Two-release plan:**
1. **Release N (Phase 6):** UI's `RegisterHubConnection` ships token-passing. Server-side hub remains permissive. `sorcha_signalr_connections_total{hub="register",authenticated=true|false}` counter is added to track adoption.
2. **Release N+1 (Phase 6 second-release task — T091):** Server adds `[Authorize]` to RegisterHub. Anonymous connections rejected with HTTP 401. Ships ONLY when the authenticated counter shows ≥ 99 % adoption from the Release N rollout.

**Risk:** Old clients without token support will fail to connect at the Release N+1 boundary. This is the planned flag day. The pre-release counter check is the gate.

## TenantHub provisioning

**Status (after Phase 4 / US2):** hub class lives, route mapped, no events emitted.

**Why intentionally empty:** Per Phase 4 / Phase 5 split, TenantHub the *hub class* lands in US2 so US3 has a surface to attach inbox events to. The first events (`InboxEntryAdded`, `InboxUnreadCountUpdated`) ship in Phase 5. Membership / security / system announcements are scheduled for follow-up phases inside Feature 118.

**Operational impact:** None today — connections to `/hubs/tenant` succeed and idle. Once Phase 5 lands the inbox, existing connections will receive events without re-connection.

## Multi-node correctness CI fixture

**Status (after Phase 3 / US1 deferral):** workflow exists, fixture is partially functional.

**Deferred problem:** The `multinode-correctness.yml` workflow fails at `Bring up the multi-replica stack` after the network-name fix in PR #517. Specifically, the `api-gateway` container reports unhealthy because the YARP destinations override (`ReverseProxy__Clusters__blueprint-cluster__Destinations__blueprint-2__Address`) is rejected by the YARP configuration binder shape used in the gateway.

**Owner:** Phase 3 follow-up. The cross-replica behaviour is verifiable manually via the standard `docker-compose.multinode.yml` overlay; the CI integration just needs the YARP override syntax corrected. Tracked here so reviewers see the deferred item rather than a stale failing workflow.
