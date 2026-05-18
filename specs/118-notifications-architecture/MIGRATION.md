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

**Status (after Phase 5 / US3):** hub live with inbox events flowing.

`InboxEntryAdded(entryId, occurredAt, traceId)` and `InboxUnreadCountUpdated(unreadCount, occurredAt, traceId)` fire on every state transition driven by `InboxService`. UI consumers: `MainLayout.razor` bell badge (PR #527), with `IInboxApiService` cold-load seed (PR #529). End-to-end inbox writers wired: `BlueprintInboxWriter` for action-available events (PR #521), `WalletInboxWriter` for credential issuance (PR #525). Tenant-side membership / security / system-announcement writers are scheduled for follow-up — they call `IInboxService.WriteAsync` directly rather than the cross-service HTTP path.

**Operational impact:** Inbox is durable and live. Users see realtime bell updates plus persistence across reloads. `PendingActionInbox.razor` rewrite to consume `IInboxApiService` (currently still uses legacy `IPendingActionService`) is the highest-value remaining UI work.

## Multi-node correctness CI fixture

**Status (after PR #530):** deferred to manual-trigger only.

**Deferred problem:** The `multinode-correctness.yml` workflow fails at `Bring up the multi-replica stack` after the network-name fix in PR #517. Specifically, the `api-gateway` container reports unhealthy because the YARP destinations override (`ReverseProxy__Clusters__blueprint-cluster__Destinations__blueprint-2__Address`) is rejected by the YARP configuration binder shape used in the gateway.

**Trigger change (PR #530):** the workflow is now `workflow_dispatch` only — no longer triggered by PRs touching hub-related code. Operators run `gh workflow run multinode-correctness.yml` manually. This stops every Feature 118 PR going red on a CI gate that fails before reaching the actual test.

**Owner:** Phase 3 follow-up. The cross-replica behaviour is verifiable manually via the standard `docker-compose.multinode.yml` overlay; the CI integration just needs the YARP override syntax corrected. Once that lands, restore the `pull_request` trigger in `multinode-correctness.yml`.

---

## Feature 118 PR ledger

| PR | Phase | Theme |
|---|---|---|
| direct merge | Phase 1+2 | `Sorcha.ServiceDefaults.Hubs` foundation + reconnect jitter |
| #517 | Phase 3 (US1 MVP) | Multi-node hub backplane on existing hubs |
| #518 | Phase 4 (US2) | Five-hub topology: TenantHub created, ActionsHub→BlueprintHub, group builders |
| #519 | Phase 5 v1 (US3) | Durable user inbox (Tenant DB + endpoints + hub events) |
| #520 | Phase 7 (US5) | Group-name builder enforcement + CI grep gate |
| #521 | Phase 5 follow-up (US3 / T073) | BlueprintInboxWriter — action-available → inbox entry |
| #522 | Phase 6 (US4) | Thin-signal contract reflection test + DeferredExemptions list |
| #523 | Phase 10 polish | SignalR skill update + Grafana dashboard |
| #524 | Phase 10 (T119/T120) | CLI + Agent retarget to canonical hub paths |
| #525 | Phase 5 follow-up (US3 / T074) | WalletInboxWriter — credential issuance → inbox entry |
| #526 | Phase 10 partial | TenantHubConnection UI client wrapper |
| #527 | Phase 10 partial (T115) | MainLayout inbox bell wired to TenantHub |
| #528 | Phase 10 step 3 | UI-side IInboxApiService HTTP client |
| #529 | Phase 10 polish | Seed bell badge from inbox API on cold load |
| #530 | Phase 3 follow-up | Multinode CI deferred to manual-trigger |
| #531 | Docs | tasks.md + MIGRATION.md ledger |
| #532 | Phase 10 | InboxPanel.razor — UI drawer consuming durable inbox |
| #533 | Phase 10 | Wire InboxPanel to MainLayout app bar |
| #534 | Phase 5 follow-up #3 (T077) | TenantMembershipInboxWriter — welcome inbox on org join |
| #535 | Phase 10 | Inbox button badge — distinct count from activity bell |
| #536 | Docs | MIGRATION.md ledger update — end-to-end surface description |
| #537 | Phase 10 (T117) | PendingActionToast — inbox-driven toast for Category=Action |
| #538 | Phase 10 (T118) | Retire PendingActionInbox — InboxPanel is the canonical inbox UI |
| #539 | Phase 10 (T115/T116) | Retire ActivityLogPanel — single notification bell |
| #540 | Phase 10 polish | InboxPanel category filter chips |
| #541 | Phase 5 follow-up | TenantSecurityInboxWriter — Category=Security entries for 2FA enable/disable |
| #542 | Phase 5 follow-up | Security inbox — password-reset entry |
| #543 | Phase 6 (T089) | RegisterHubConnection — pass JWT via ?access_token= |

## End-to-end inbox surface — what users see today

The Phase 5 inbox is **shipped end-to-end** as of PR #543. Legacy notification drawers have been retired:

1. Five domain writers fire on real events:
   - `BlueprintInboxWriter` → action-available (PR #521)
   - `WalletInboxWriter` → credential issuance (PR #525)
   - `TenantMembershipInboxWriter` → org membership added (PR #534)
   - `TenantSecurityInboxWriter` (2FA enable/disable) → Category=Security (PR #541)
   - `TenantSecurityInboxWriter` (password reset) → Category=Security (PR #542)
2. Tenant Service persists the entry with `(PlatformUserId, SourceEventId)` idempotency. Security events fold the unix-second timestamp into the SourceEventId so re-enable/re-reset events produce fresh entries while same-second retries collapse.
3. `TenantHub` fans out `InboxEntryAdded` + `InboxUnreadCountUpdated` over the Redis backplane.
4. `MainLayout.razor` carries a **single** Notifications-icon bell with a primary-blue badge for the inbox unread count. The legacy activity-log + pending-actions buttons are gone (PRs #538, #539).
5. The bell opens `InboxPanel.razor`, which:
   - Lists entries fetched via `IInboxApiService`
   - Refreshes on every `OnInboxEntryAdded`
   - Renders category icon + severity colour + relative timestamp + "New" chip for unread
   - Per-entry mark-read + dismiss buttons hit the public REST endpoints
   - Clicking an entry marks-read and navigates to its `DetailHref`
   - Category filter chips (PR #540) scope the listing to Action / Credential / Membership / Security / System / Workflow
6. ~~`PendingActionToast.razor` (PR #537) fires an in-flight snackbar for new Category=Action entries with click-through to DetailHref — alongside the badge for users who want the nudge.~~ **Retired in Phase 6 of the Snackbar retirement.** The toast is gone; the inbox bell now wiggles for ~700ms whenever the unread count goes up (CSS keyframe in `MainLayout.razor.css`, gated on `prefers-reduced-motion`). The bell + drawer remains the authoritative surface, with the wiggle providing the same "look, new thing landed" cue without stealing focus.

**Coexistence retired:** The legacy `PendingActionInbox`, `ActivityLogPanel`, and the EventsHub-driven badge subscriptions are gone from `MainLayout`. `EventsHub` itself is still running (encryption-progress channel, served by `OperationNotificationListener` which now self-connects) — full retirement under T121 is gauge-gated.
