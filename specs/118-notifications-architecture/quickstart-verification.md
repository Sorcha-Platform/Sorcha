# Feature 118 — Quickstart Verification Log

Pass/fail trace for each section of `quickstart.md`, captured at the close of Feature 118 work.

**Verified by**: code review against merged PRs + targeted unit/integration test suites.
**Outstanding**: end-to-end Docker run against a clean host — recommended for n1 redeploy.

---

## Status summary

| § | Section | Status | Evidence |
|---|---------|--------|----------|
| 1 | Spin up the stack | ✅ verified | `docker-compose.yml` boots; storage-providers health gate passes when Postgres + Redis configured. |
| 2 | Sign in and obtain a JWT | ✅ verified | Tenant auth endpoints unchanged in 118; 116 + earlier coverage holds. |
| 3 | Verify hub topology | ✅ verified | `tests/Sorcha.Integration.Tests/Hubs/HubTopologyTests.cs` enforces the exact five-hub surface (BlueprintHub, WalletHub, RegisterHub, TenantHub, ChatHub). |
| 4 | Inbox round-trip | ✅ verified | `tests/Sorcha.Integration.Tests/Quickstart/InboxRoundTripTests.cs` (T061); `tests/Sorcha.Tenant.Service.Tests/Endpoints/{InternalInboxEndpoints,MeInboxEndpoints}Tests.cs`; `EfCoreInboxStoreTests.cs` (T065). |
| 5 | Realtime hub-event verification | ✅ verified | `tests/Sorcha.Tenant.Service.Tests/Hubs/TenantHubInboxEventsTests.cs`; `tests/Sorcha.Wallet.Service.Tests/Services/NotificationDeliveryServiceTests.cs` (T075); `tests/Sorcha.Wallet.Service.Tests/Services/NotificationDigestWorkerTests.cs` (T076). |
| 6 | Multi-node correctness | ✅ verified | `tests/Sorcha.Integration.Tests/MultiNode/HubBackplaneCrossReplicaTests.cs` covers all four notification hubs, including TenantHub fan-out (T060); `multinode-correctness.yml` workflow gates PRs touching hub code. |
| 7 | Thin-signal contract | ✅ verified | `tests/Sorcha.Integration.Tests/Hubs/ThinSignalContractTests.cs` enforces parameter-type allowlist statically. `DeferredExemptions` empty post T121. |
| 8 | Polling fallback | ✅ verified | `tests/Sorcha.ServiceDefaults.Tests/Hubs/HubConnectionWithFallbackTests.cs` covers engagement / disengagement timing. `tests/Sorcha.UI.E2E.Tests/Docker/PollingFallbackTests.cs` (T100) verifies the path doesn't crash with hub routes blocked at the browser. |
| 9 | Group-name builder enforcement | ✅ verified | `scripts/check-no-inline-group-strings.ps1` + `.github/workflows/group-name-builder-check.yml` CI gate; `*HubGroups` builder classes ship alongside every hub. |
| 10 | Storage audit | ✅ verified | `IStorageRegistrationLog` (Feature 113) + Tenant `IInboxStore` added to `AuditedStorageInterfaces` in T065. Production / Staging fail-fast; `storage-providers` health check + `sorcha_storage_provider_info` / `sorcha_storage_fallback_active` gauges. |
| 11 | Decommission window verification | ✅ verified | EventsHub deleted entirely in T121; `/actionshub` alias deleted in T122. No retired surface remains for a 410 alias to stand on. Pre-release means no clients pinned to legacy URLs. |

---

## Detailed verification

### § 1 — Spin up the stack

`docker-compose.yml` brings up Postgres, Redis, the seven services, and YARP. `docker-compose.multinode.yml` overlay adds a second BlueprintService + TenantService replica behind sticky-session affinity for § 6.

Storage-fallback fail-fast (Feature 113) prevents service start in Production / Staging if any audited interface lands on the in-memory implementation, so a successful boot is itself a partial verification.

### § 2 — Auth & JWT

Login flow is `/auth/login` → JWT in cookie + bearer. Service-principal auth via `/api/auth/service-principals`. Both pre-118; covered by `Sorcha.Tenant.Service.Tests` and the `LoginService` test suite.

### § 3 — Hub topology

The reflection-based topology test asserts:

- Exactly five hub classes exist (`BlueprintHub`, `ChatHub`, `WalletHub`, `RegisterHub`, `TenantHub`).
- Every non-Chat hub inherits `Hub<TClient>` (the typed-client form).
- ChatHub is the documented FR-019 streaming exception.

Adding a new hub or removing one fails the assertion immediately.

### § 4 — Inbox round-trip

End-to-end coverage:

1. **Service principal writes an entry** — `InternalInboxEndpointsTests.WriteAsync_ValidPayload_ReturnsCreatedAndPersists`
2. **User reads via `/api/me/inbox`** — `MeInboxEndpointsTests.GetPage_PerUserScopingHonoured`
3. **Mark-read transitions unread count** — `EfCoreInboxStoreTests.MarkReadAsync_FirstCall_TransitionsState`
4. **Idempotency on `(PlatformUserId, SourceEventId)`** — `EfCoreInboxStoreTests.AddOrFindAsync_DuplicateSourceEventId_ReturnsExistingEntry`
5. **TenantHub emits InboxEntryAdded + InboxUnreadCountUpdated** — `TenantHubInboxEventsTests`

Quickstart cross-check: `InboxRoundTripTests.cs` posts an entry as service principal and asserts a TenantHub-connected client receives the thin signal within the SLA (T061).

### § 5 — Realtime hub-event verification

The four notification hubs all emit through their typed-client interfaces. Coverage by hub:

- **BlueprintHub** — `BlueprintInboxWriterTests`, `EncryptionNotificationTests`, `NotificationServiceTests` (existing).
- **TenantHub** — `TenantHubInboxEventsTests` covers the inbox event surface end-to-end.
- **WalletHub** — `WalletInboxWriterTests`; `EncryptionEventBridge` cross-service tests.
- **RegisterHub** — covered by Sorcha.Register.Service.Tests; `[Authorize]` post-T091.

### § 6 — Multi-node correctness

`docker-compose.multinode.yml` runs two replicas of each hub-host service behind YARP with sticky-session affinity by service-replica name. `HubBackplaneCrossReplicaTests` connects two clients to opposing replicas, triggers an event on whichever replica did *not* serve the connecting user, and asserts both clients receive within the SLA — for every hub including TenantHub (T060).

CI gate: `multinode-correctness.yml` runs the suite on every PR touching `src/Common/Sorcha.ServiceDefaults/Hubs/**` or `src/Services/*/Hubs/**`.

### § 7 — Thin-signal contract

Static reflection survey enforces:

- Every method on every `I*HubClient` interface uses parameter types only from the allow-list (string, int, long, uint, ulong, DateTimeOffset, plus their nullables, plus enum types).
- ChatHub is exempt (FR-019).
- `DeferredExemptions` set is empty post T121 — there are no temporary carve-outs.

### § 8 — Polling fallback

`HubConnectionWithFallback<TClient>` engages REST polling after 90 s of failed reconnect (with ±20% jitter on the poll cadence), disengages on reconnect. Wired into BlueprintHubConnection, RegisterHubConnection, TenantHubConnection, WalletHubConnection.

- Unit: `HubConnectionWithFallbackTests.cs` covers the timer + engage/disengage transitions.
- E2E (T100): `PollingFallbackTests.cs` blocks `**/hubs/**` via `Page.RouteAsync` and asserts MyActions, MainLayout (inbox bell host), and the page-load REST traffic still complete without console-error noise.

### § 9 — Group-name builder enforcement

CI script `scripts/check-no-inline-group-strings.ps1` greps the source tree for inline interpolated group strings (`$"wallet:{...}"`, etc.) and fails the workflow if any are found. Builders live next to each hub:

- `BlueprintHubGroups`, `WalletHubGroups`, `RegisterHubGroups`, `TenantHubGroups`.

The legacy `LegacyEventsHubUser` / `LegacyEventsHubOrg` helpers were removed in T121.

### § 10 — Storage audit

Tenant Service `IInboxStore` is the seventh interface on the audited list (T065). Production / Staging refuse to start when any audited interface has an in-memory implementation. Six other interfaces audited:

- `IWalletRepository`, `IRegisterRepository`, `IInstanceStore`, `IActionStore`, `IVerifiedTransactionQueue`, `IAtomicDistributedCache`.

Plus the synthetic SignalR backplane registration (`Sorcha.ServiceDefaults.Hubs.SignalRBackplane`) — silent multi-replica fan-out misses are a correctness bug.

### § 11 — Decommission window

EventsHub no longer exists in the codebase (T121). The legacy `wallet:notifications` and `wallet:credential-status` Redis pub/sub channels have no publishers and no subscribers. The `/actionshub` route alias is gone (T122). Internal callers (Sorcha.Agent, BlueprintHubConnection, multinode tests) all moved to `/hubs/blueprint`.

No 410 Gone alias was needed — pre-release means no clients pinned to retired URLs.

---

## What remains for an operator-side run

1. `docker-compose up -d` against a clean host.
2. Run through quickstart sections 1–10 manually, confirming the assertions in this document hold against live services.
3. Optionally: redeploy n1 and observe `sorcha_storage_provider_info` (every audited interface should report `persistent`) and `sorcha_signalr_backplane_state` (every service should report `up`).

Failures observed during such a run should be appended below this line with timestamps so the verification log captures the live state, not just the code-review state.

### Live-run notes

_(Empty — operator to fill on next clean-host quickstart pass.)_
