# Tasks: Notifications & Realtime Architecture

**Input**: Design documents from `/specs/118-notifications-architecture/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Test tasks are included because spec acceptance scenarios reference automated verification (US1 multi-node fixture, FR-014 CI grep gate, US3 inbox round-trip, US6 polling fallback) and the constitution requires > 85 % coverage on new code. Per-story tests sit inside each story's phase.

**Organization**: Tasks are grouped by user story so each ships independently. MVP is User Story 1 alone (backplane on existing hubs, no topology change yet) — every later phase builds on it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story label (US1 — US7) for tasks inside a story phase
- Include exact file paths in descriptions

## Path Conventions

Existing multi-service Sorcha monorepo. Source under `src/`, tests under `tests/`, design docs under `specs/118-notifications-architecture/`. No new top-level projects.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Bring up the new shared namespaces and project references that every later phase depends on. No behaviour change in this phase.

- [X] T001 Create `src/Common/Sorcha.ServiceDefaults/Hubs/` folder and a `Hubs/_namespace.cs` file establishing the `Sorcha.ServiceDefaults.Hubs` namespace.
- [X] T002 [P] Add NuGet package reference `Microsoft.AspNetCore.SignalR.StackExchangeRedis` to `Directory.Packages.props` at the version aligned with the .NET 10 line currently in tree.
- [X] T003 [P] Add a `<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" />` to `src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj`.
- [X] T004 [P] Create `tests/Sorcha.ServiceDefaults.Tests/Hubs/` folder for the new common-code unit tests.
- [X] T005 [P] Create `tests/Sorcha.Integration.Tests/MultiNode/` folder and add `docker-compose.multinode.yml` skeleton at repo root that brings up two Tenant Service replicas behind YARP with sticky-session affinity.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Land the common hub infrastructure that every user story depends on. Nothing user-visible ships here, but every later phase imports from this code.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T006 Implement `HubSignal` thin-signal envelope record in `src/Common/Sorcha.ServiceDefaults/Hubs/HubSignal.cs` per `contracts/hub-signal.schema.json`.
- [X] T007 [P] Implement `SignalRMetrics` OpenTelemetry meter `Sorcha.SignalR` in `src/Common/Sorcha.ServiceDefaults/Hubs/SignalRMetrics.cs` exposing the five instruments listed in `data-model.md` § OpenTelemetry instruments.
- [X] T008 Implement `SorchaHubConventions` static helper in `src/Common/Sorcha.ServiceDefaults/Hubs/SorchaHubConventions.cs` that wires JWT Bearer auth (with `platform_user_id` claim guard), Redis backplane (per-service ChannelPrefix), reconnect policy, and OpenTelemetry instrumentation. Sources Redis connection via `SorchaConnectionsExtensions.GetSorchaRedisConnectionString()`.
- [X] T009 Implement `AddSorchaHubExtensions.AddSorchaHub<THub, TClient>(IServiceCollection, IConfiguration, string routePath, string serviceShortName)` in `src/Common/Sorcha.ServiceDefaults/Hubs/AddSorchaHubExtensions.cs`. Calls into `SorchaHubConventions`, registers the backplane with `ChannelPrefix = sorcha:signalr:{serviceShortName}`, records the registration via `IStorageRegistrationLog` from `Sorcha.ServiceDefaults.Storage`, fail-fast in Production/Staging if backplane resolves in-memory.
- [X] T010 [P] Implement `HubConnectionWithFallback<TClient>` wrapper in `src/Common/Sorcha.ServiceDefaults/Hubs/HubConnectionWithFallback.cs` exposing the typed hub client, a `ConnectionState` observable, and a poll-now hint. Default poll cadence 15 s ±20 % jitter, engages after 90 s of failed reconnect.
- [X] T011 Add reconnect jitter (±20 %, 100 ms floor) to `src/Common/Sorcha.ServiceClients.Http/Hub/SorchaHubConnectionBuilder.cs` per research R-003. Surface `ConnectionState` observable for use by `HubConnectionWithFallback<TClient>`.
- [X] T012 [P] Unit tests for `HubSignal` shape validation in `tests/Sorcha.ServiceDefaults.Tests/Hubs/HubSignalTests.cs` (round-trip JSON, required fields, max-length checks).
- [X] T013 [P] Unit tests for `SignalRMetrics` instruments in `tests/Sorcha.ServiceDefaults.Tests/Hubs/SignalRMetricsTests.cs` (instrument names, tags, gauge state values).
- [X] T014 [P] Unit tests for `AddSorchaHub` registration and fail-fast paths in `tests/Sorcha.ServiceDefaults.Tests/Hubs/AddSorchaHubExtensionsTests.cs`.
- [X] T015 [P] Unit tests for `HubConnectionWithFallback<TClient>` poll engagement and disengagement in `tests/Sorcha.ServiceDefaults.Tests/Hubs/HubConnectionWithFallbackTests.cs`.
- [X] T016 [P] Unit tests for jittered reconnect schedule in `tests/Sorcha.ServiceClients.Tests/Hub/SorchaHubConnectionBuilderTests.cs` (existing file, add jitter tests).

**Checkpoint**: Foundation ready. User-story phases can begin.

---

## Phase 3: User Story 1 — Multi-node hub delivery (Priority: P1) 🎯 MVP

**Goal**: Apply the new `AddSorchaHub` extension to every existing hub so multi-replica deploys reliably fan out group sends. No event-shape change, no topology change. Just stops losing notifications across replicas.

**Independent Test**: Deploy two replicas of one hub-hosting service. Open clients via different replicas. Trigger an event on whichever replica did *not* serve the connecting user. Assert all clients receive the event within 200 ms p95. Repeat for every hub.

### Tests for User Story 1

- [X] T017 [P] [US1] Cross-replica integration test in `tests/Sorcha.Integration.Tests/MultiNode/HubBackplaneCrossReplicaTests.cs` parameterised over each hub (Blueprint, Wallet, Register, Tenant — Tenant added in US3 phase, mark its test as `[Fact(Skip="awaiting TenantHub from US3")]` initially). Uses raw `HubConnectionBuilder` with explicit YARP routing headers to control replica targeting.
- [X] T018 [P] [US1] Add `multinode-correctness.yml` GitHub Actions workflow at `.github/workflows/multinode-correctness.yml` that runs the cross-replica tests on PRs touching `src/Common/Sorcha.ServiceDefaults/Hubs/**` or `src/Services/*/Hubs/**`.

### Implementation for User Story 1

- [X] T019 [US1] Migrate ActionsHub registration in `src/Services/Sorcha.Blueprint.Service/Program.cs` to use `services.AddSorchaHub<ActionsHub, IActionsHubClient>(...)` (interface stub-added in this task — full rename to BlueprintHub waits for US2). ActionsHub gains a typed client interface `IActionsHubClient` in `src/Services/Sorcha.Blueprint.Service/Hubs/IActionsHubClient.cs` mirroring its current methods as a bridge.
- [X] T020 [US1] Migrate EventsHub registration in `src/Services/Sorcha.Blueprint.Service/Program.cs` to use `AddSorchaHub<EventsHub, IEventsHubClient>(...)`. Add `IEventsHubClient` stub interface.
- [X] T021 [US1] Migrate ChatHub registration in `src/Services/Sorcha.Blueprint.Service/Program.cs` to use `AddSorchaHub<ChatHub, IChatHubClient>(...)`. Note ChatHub is exempt from the thin-signal contract; XML doc on the class marks it as the deliberate exception (FR-019). Add `IChatHubClient` stub interface mirroring existing methods.
- [X] T022 [P] [US1] Migrate WalletHub registration in `src/Services/Sorcha.Wallet.Service/Program.cs` to use `AddSorchaHub<WalletHub, IWalletHubClient>(...)`. Add typed client interface (existing WalletHub is currently untyped).
- [X] T023 [P] [US1] Migrate RegisterHub registration in `src/Services/Sorcha.Register.Service/Program.cs` to use `AddSorchaHub<RegisterHub, IRegisterHubClient>(...)`. RegisterHub already has `IRegisterHubClient`. Auth hardening deferred to US4 cutover.
- [X] T024 [US1] Add Redis connection-string requirement to `src/Services/Sorcha.Blueprint.Service/appsettings.json` (and `appsettings.Production.json`) under `ConnectionStrings:Blueprint:Redis`, falling back to `ConnectionStrings:Sorcha:Redis` per the SorchaConnections cascade pattern.
- [X] T025 [P] [US1] Add Redis connection-string requirement to `src/Services/Sorcha.Wallet.Service/appsettings.json`.
- [X] T026 [P] [US1] Add Redis connection-string requirement to `src/Services/Sorcha.Register.Service/appsettings.json`.
- [X] T027 [US1] Update `docker-compose.yml` so every hub-hosting service sees the existing `redis` container via the SorchaConnections cascade env vars (`ConnectionStrings__Sorcha__Redis=redis:6379`). Verify the dev profile.
- [X] T028 [P] [US1] Update `docker-compose.multinode.yml` (created in T005) to bring up two Blueprint Service replicas behind a YARP affinity rule to support the cross-replica test.
- [X] T029 [US1] Wire the `sorcha_signalr_backplane_state` gauge into the `storage-providers` health check in each migrated service via the existing `IStorageRegistrationLog` integration so a degraded backplane reports `Degraded` on `/health/storage-providers`.
- [X] T030 [US1] Update CLAUDE.md pattern #10 to mention SignalR backplane registration alongside the existing storage interfaces.

**Checkpoint**: US1 complete. Multi-node correctness verified on every existing hub. Topology unchanged. MVP ships here.

---

## Phase 4: User Story 2 — Hub-per-service topology with documented exception (Priority: P1)

**Goal**: Consolidate hub topology to one notification hub per service plus the deliberate ChatHub exception. Create TenantHub (empty/auth-only here, inbox lands in US3). Rename ActionsHub → BlueprintHub with `/actionshub` alias. Retire EventsHub via parallel-fire window.

**Independent Test**: Enumerate `src/Services/*/Hubs/*.cs` — exactly five hub classes (TenantHub, BlueprintHub, WalletHub, RegisterHub, ChatHub). All five inherit from `Hub<TClient>` with typed client interfaces. ChatHub class XML doc marks it as the deliberate exception. `/hubs/events` returns 410 after the parallel-fire window closes.

### Tests for User Story 2

- [X] T031 [P] [US2] Topology-enforcement unit test in `tests/Sorcha.ServiceDefaults.Tests/Hubs/HubTopologyTests.cs` that uses reflection across the loaded service assemblies to assert exactly five `Hub<>` types exist and the four non-Chat hubs use `AddSorchaHub`.
- [X] T032 [P] [US2] Existing `tests/Sorcha.Blueprint.Service.Tests/Integration/SignalRIntegrationTests.cs` updated for the rename (new hub class `BlueprintHub`, alias path semantics).
- [X] T033 [P] [US2] New `tests/Sorcha.Tenant.Service.Tests/Hubs/TenantHubTests.cs` covering connect, claim guard, group membership for `user:{platformUserId:N}` on connect.
- [X] T034 [P] [US2] EventsHub retirement test in `tests/Sorcha.Blueprint.Service.Tests/Integration/EventsHubRetirementTests.cs` asserting the `410 Gone` response shape after retirement (initially `[Fact(Skip="awaiting decommission step")]`).

### Implementation for User Story 2

- [X] T035 [US2] Create TenantHub at `src/Services/Sorcha.Tenant.Service/Hubs/TenantHub.cs` inheriting `Hub<ITenantHubClient>` with `[Authorize]` Bearer auth and `platform_user_id` claim guard in `OnConnectedAsync`. On connect, add caller to `TenantHubGroups.User(claim)`.
- [X] T036 [P] [US2] Create `ITenantHubClient` typed interface at `src/Services/Sorcha.Tenant.Service/Hubs/ITenantHubClient.cs` per `contracts/tenant-hub-client.cs.md`. Inbox event methods stay declared but emitting lives in US3.
- [X] T037 [P] [US2] Create `TenantHubGroups` builder at `src/Services/Sorcha.Tenant.Service/Hubs/TenantHubGroups.cs` per `data-model.md` § hub group conventions.
- [X] T038 [US2] Wire `services.AddSorchaHub<TenantHub, ITenantHubClient>(builder.Configuration, "/hubs/tenant", "tenant")` in `src/Services/Sorcha.Tenant.Service/Program.cs`. Add `app.MapHub<TenantHub>("/hubs/tenant")`.
- [X] T039 [US2] Add Redis connection-string requirement to `src/Services/Sorcha.Tenant.Service/appsettings.json` and `appsettings.Production.json`.
- [X] T040 [US2] Add YARP route in `src/Services/Sorcha.ApiGateway/appsettings.json` for `/hubs/tenant` → tenant cluster with WebSocket support.
- [X] T041 [US2] Rename `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs` → `BlueprintHub.cs`. Update class name. Replace stub `IActionsHubClient` with full `IBlueprintHubClient` per `contracts/blueprint-hub-client.cs.md`.
- [X] T042 [P] [US2] Create `BlueprintHubGroups` builder at `src/Services/Sorcha.Blueprint.Service/Hubs/BlueprintHubGroups.cs` per `data-model.md`.
- [X] T043 [US2] Update `src/Services/Sorcha.Blueprint.Service/Program.cs`: `MapHub<BlueprintHub>("/hubs/blueprint")` plus `MapHub<BlueprintHub>("/actionshub")` as deprecated alias logging a `Deprecation` header. Update `AddSorchaHub` call accordingly.
- [X] T044 [US2] Update `src/Services/Sorcha.ApiGateway/appsettings.json` to route both `/hubs/blueprint` and `/actionshub` to blueprint cluster (alias for the deprecation window).
- [X] T045 [US2] Update WalletHub at `src/Services/Sorcha.Wallet.Service/Hubs/WalletHub.cs` to inherit `Hub<IWalletHubClient>` with the full typed contract per `contracts/wallet-hub-client.cs.md`. Migrate all existing method bodies to the new typed-client signatures.
- [X] T046 [P] [US2] Create `WalletHubGroups` builder at `src/Services/Sorcha.Wallet.Service/Hubs/WalletHubGroups.cs` formalising the existing `WalletHub.GroupNameFor` helper.
- [X] T047 [US2] Update RegisterHub at `src/Services/Sorcha.Register.Service/Hubs/RegisterHub.cs` to formalise its existing typed-client interface (already present) and use the new `RegisterHubGroups` builder. NO `[Authorize]` change in this phase — that lands in US4.
- [X] T048 [P] [US2] Create `RegisterHubGroups` builder at `src/Services/Sorcha.Register.Service/Hubs/RegisterHubGroups.cs`.
- [X] T049 [US2] Mark ChatHub as deliberate exception. Update XML doc on the class in `src/Services/Sorcha.Blueprint.Service/Hubs/ChatHub.cs` with `<remarks>Deliberate exception to one-hub-per-service rule. Streaming RPC shape; 3-minute keepalive; carries content payloads. Documented in specs/118-notifications-architecture/spec.md FR-019.</remarks>`.
- [X] T050 [US2] Begin parallel-fire on EventsHub. In each emit site (`NotificationService`, `EventsHubNotificationBridge`), continue firing the existing EventsHub events AND fire the new typed events on BlueprintHub or WalletHub. No subscriber moves yet — subscribers move in Phase 10 polish.
- [X] T051 [US2] Add `sorcha_signalr_events_hub_subscribers` gauge instrument in `src/Services/Sorcha.Blueprint.Service/Hubs/EventsHub.cs` reporting the current SignalR group-membership count derived from `OnConnectedAsync` / `OnDisconnectedAsync`.
- [X] T052 [US2] Document the deprecation policy: add `specs/118-notifications-architecture/MIGRATION.md` capturing the `/actionshub` alias plan, the EventsHub parallel-fire window, and the metric-driven decommission rule (FR-038). Reference the URL from spec FR-004 and FR-005.
- [X] T053 [US2] Update CLAUDE.md to reference the five-hub topology and the ChatHub exception alongside existing pattern documentation.

**Checkpoint**: US2 complete. Five hubs in tree, every notification hub uses `AddSorchaHub`, ChatHub marked, EventsHub firing in parallel with new homes for one release cycle.

---

## Phase 5: User Story 3 — Durable user inbox in Tenant Service (Priority: P1)

**Goal**: Stand up the durable inbox domain. Postgres `InboxEntry` entity, Redis sorted-set unread index, internal write API, public read/manage API, TenantHub `InboxEntryAdded` and `InboxUnreadCountUpdated` realtime events. Other services start writing entries.

**Independent Test**: Submit a workflow that emits an inbox-worthy event for a target user. Disconnect that user's browser from SignalR. Wait 5 s. Restore. Assert inbox shows the entry and unread count is correct. Reload — entry persists. Dismiss — reload — gone. Two related entries firing within 30 s with same correlation key group visually but each individually dismissible.

### Tests for User Story 3

- [X] T054 [P] [US3] EF migration test in `tests/Sorcha.Tenant.Service.Tests/Migrations/InboxEntryMigrationTests.cs` (apply, check schema, check indexes).
- [X] T055 [P] [US3] `InboxService` unit tests in `tests/Sorcha.Tenant.Service.Tests/Services/InboxServiceTests.cs` covering write, idempotent write, read transition, dismiss transition, mark-all-read.
- [ ] T056 [P] [US3] Redis index tests in `tests/Sorcha.Tenant.Service.Tests/Services/InboxUnreadIndexTests.cs` covering ZADD, ZREM, ZCARD atomicity and Postgres fallback when Redis is down.
- [X] T057 [P] [US3] Endpoint integration tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/MeInboxEndpointsTests.cs` covering all six public endpoints, authorization scoping, idempotency.
- [X] T058 [P] [US3] Internal endpoint test in `tests/Sorcha.Tenant.Service.Tests/Endpoints/InternalInboxEndpointsTests.cs` covering RequireService policy gate, idempotent write on `(PlatformUserId, SourceEventId)`, validation errors.
- [X] T059 [P] [US3] TenantHub event-emission tests in `tests/Sorcha.Tenant.Service.Tests/Hubs/TenantHubInboxEventsTests.cs` asserting `InboxEntryAdded` and `InboxUnreadCountUpdated` fire on the user group with correct thin-signal shape.
- [X] T060 [P] [US3] Cross-replica multi-node test for TenantHub in `tests/Sorcha.Integration.Tests/MultiNode/HubBackplaneCrossReplicaTests.cs` (un-skip the TenantHub case from T017).
- [X] T061 [P] [US3] Quickstart end-to-end script `tests/Sorcha.Integration.Tests/Quickstart/InboxRoundTripTests.cs` matching steps 4 and 6 in `quickstart.md` (post entry → realtime fire → REST read → mark read → idempotent re-post).

### Implementation for User Story 3

- [X] T062 [US3] Create `InboxEntry` EF entity, `InboxCategory` and `InboxSeverity` enums, `ChannelHints` `[Flags]` enum at `src/Services/Sorcha.Tenant.Service/Models/InboxEntry.cs` per `data-model.md`.
- [X] T063 [US3] Add `InboxEntries` `DbSet` to `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs` with the five indexes from `data-model.md` § Indexes.
- [X] T064 [US3] Create EF migration `Migrations/AddInboxEntry.cs` for the new table.
- [X] T065 [US3] Implement `IInboxStore` and `EfCoreInboxStore` (Postgres) in `src/Services/Sorcha.Tenant.Service/Storage/IInboxStore.cs` + `EfCoreInboxStore.cs`. Registered through `IStorageRegistrationLog` per Feature 113 pattern; production fail-fast applies. (Path moved to `Storage/` to match Blueprint Service convention.)
- [ ] T066 [US3] Implement `IInboxUnreadIndex` (Redis sorted-set wrapper) in `src/Services/Sorcha.Tenant.Service/Services/InboxUnreadIndex.cs` with `ZADD` / `ZREM` / `ZCARD` operations and Postgres `COUNT(*)` fallback. Register through `IStorageRegistrationLog`.
- [X] T067 [US3] Implement `IInboxService` and `InboxService` in `src/Services/Sorcha.Tenant.Service/Services/InboxService.cs` orchestrating Postgres write + Redis ZADD + TenantHub event emit. Idempotency on `(PlatformUserId, SourceEventId)`.
- [X] T068 [US3] Implement `MeInboxEndpoints` in `src/Services/Sorcha.Tenant.Service/Endpoints/MeInboxEndpoints.cs`: GET /api/me/inbox, GET /{id}, GET /unread-count, POST /{id}/read, POST /{id}/dismiss, POST /mark-all-read. All scoped to caller's `platform_user_id` claim. Per `contracts/inbox-endpoints.openapi.yaml`.
- [X] T069 [US3] Implement `InternalInboxEndpoints` in `src/Services/Sorcha.Tenant.Service/Endpoints/InternalInboxEndpoints.cs`: POST /api/internal/inbox gated by `RequireService` policy. Idempotency on the unique index, `200 idempotent:true` response on duplicate.
- [X] T070 [US3] Wire endpoints into `src/Services/Sorcha.Tenant.Service/Program.cs`. Add YARP route in `src/Services/Sorcha.ApiGateway/appsettings.json` for `/api/me/inbox/*` and (locally — not gateway-exposed) `/api/internal/inbox`.
- [X] T071 [US3] Implement TenantHub inbox event emission in `InboxService`: `InboxEntryAdded(entryId, occurredAt, traceId)` and `InboxUnreadCountUpdated(unreadCount, occurredAt, traceId)` on `TenantHubGroups.User(platformUserId)`. Per `contracts/tenant-hub-client.cs.md`.
- [X] T072 [P] [US3] Create `IPlatformInboxClient` HTTP client in `src/Common/Sorcha.ServiceClients.Http/Inbox/IPlatformInboxClient.cs` with `WriteAsync(InboxEntryWriteRequest)` calling `POST /api/internal/inbox` with the existing `ServiceAuthClient` token flow.
- [X] T073 [US3] Implement `BlueprintInboxWriter` service in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/BlueprintInboxWriter.cs` invoked by `NotificationService` when an action becomes available. Writes a `Category=Action` entry with `tx:{walletAddr}:{txId}` correlation key. Registered in DI.
- [X] T074 [US3] Implement `WalletInboxWriter` service in `src/Services/Sorcha.Wallet.Service/Services/Implementation/WalletInboxWriter.cs` invoked by the credential issuance pipeline. Writes a `Category=Credential` entry with the same correlation key. Registered in DI.
- [X] T075 [P] [US3] Update `NotificationDeliveryService` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/NotificationDeliveryService.cs` to invoke `IPlatformInboxClient.WriteAsync` instead of publishing to `wallet:notifications`. (Pre-release: legacy `wallet:notifications` publish removed entirely — no parallel-fire window kept.)
- [ ] T076 [P] [US3] Update `NotificationDigestWorker` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/NotificationDigestWorker.cs` to produce digest inbox entries (one per user per digest cycle, with `ChannelHints = Inbox | Digest`).
- [ ] T077 [US3] Implement `TenantInboxBridgeService` background subscriber in `src/Services/Sorcha.Tenant.Service/Services/InboxBridgeService.cs` for native Tenant-domain events (membership change, security alert, system announcement) — converts Redis events to inbox writes via `InboxService`.

**Checkpoint**: US3 complete. Inbox is durable, fans out via TenantHub, drives the existing notification surfaces.

---

## Phase 6: User Story 4 — Thin-signal contract codified (Priority: P1)

**Goal**: Tighten every notification-hub event to carry only IDs and timestamps. Strip payload fields from existing events. Add detail-href XML docs to every event method. RegisterHub gains `[Authorize]` after UI ships token-passing first.

**Independent Test**: Subscribe to backplane Redis as external observer. Trigger every event type. Assert no message body contains any field outside `EventType / Ids[] / OccurredAt / TraceId`. Every event method on every typed-client interface has an XML `<see cref="..." />` link to its detail REST endpoint.

### Tests for User Story 4

- [X] T078 [P] [US4] Reflection-based contract test `tests/Sorcha.ServiceDefaults.Tests/Hubs/ThinSignalContractTests.cs` enumerating every method on every `I*HubClient` interface and asserting parameter types are in the allow-list (`string`, `Guid`, `Guid?`, `int`, `long`, `DateTimeOffset`). ChatHub interface excluded explicitly.
- [ ] T079 [P] [US4] Backplane-observation integration test in `tests/Sorcha.Integration.Tests/Hubs/BackplanePayloadShapeTests.cs` — runs the four notification hubs, triggers one of every event type, subscribes to Redis backplane, asserts every JSON message body conforms to `contracts/hub-signal.schema.json`.
- [X] T080 [P] [US4] RegisterHub auth cutover test `tests/Sorcha.Register.Service.Tests/RegisterHubAuthorizeTests.cs` asserting unauthenticated connections are rejected with 401 (initially `[Fact(Skip="awaiting cutover")]`, un-skipped in the second-release task).

### Implementation for User Story 4

- [X] T081 [US4] Strip descriptive fields from existing `ActionNotification` / `CredentialNotification` / `EncryptionSignal` records. Replace with the IDs they carry; move descriptive fields (blueprint name, action description, percentage) to the corresponding REST detail endpoints if not already there. Files: `src/Services/Sorcha.Blueprint.Service/Models/Notifications/*.cs`, `src/Services/Sorcha.Wallet.Service/Models/Notifications/*.cs`.
- [X] T082 [US4] Update every emit site to call the new typed methods on `IBlueprintHubClient` / `IWalletHubClient` with thin-signal parameters. Files: `NotificationService.cs`, `TransactionLifecycleEventBridge.cs`, encryption pipeline emit sites.
- [X] T083 [P] [US4] Add `<see cref="..." />` XML docs to every event method on `IBlueprintHubClient` per `contracts/blueprint-hub-client.cs.md`.
- [X] T084 [P] [US4] Add `<see cref="..." />` XML docs to every event method on `IWalletHubClient` per `contracts/wallet-hub-client.cs.md`.
- [X] T085 [P] [US4] Add `<see cref="..." />` XML docs to every event method on `IRegisterHubClient` per `contracts/register-hub-client.cs.md`.
- [X] T086 [P] [US4] Add `<see cref="..." />` XML docs to every event method on `ITenantHubClient` per `contracts/tenant-hub-client.cs.md`.
- [X] T087 [US4] Update `EncryptionProgressIndicator.razor` and any other UI consumer that previously read percentage off the hub event to fetch detail via `GET /api/operations/{operationId}`.
- [X] T088 [US4] Update `MainLayout.razor` and credential consumers that previously read issuer/credential metadata off the hub event to fetch detail via `GET /api/wallets/{addr}/credentials/{credentialId}`.
- [X] T089 [US4] Ship UI's `RegisterHubConnection` token-passing change in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterHubConnection.cs` — pass the JWT via `?access_token=`. Server-side hub still permissive.
- [X] T090 [US4] Add `sorcha_signalr_connections_total{hub="register",authenticated=...}` counter to track rollout. Wire in `RegisterHub.OnConnectedAsync`.
- [ ] T091 [US4] **Second-release task** (do not bundle with above): Add `[Authorize]` to `src/Services/Sorcha.Register.Service/Hubs/RegisterHub.cs`. Un-skip T080. Remove permissive code path. Only ship after the authenticated counter shows ≥ 99 % adoption.

**Checkpoint**: US4 complete (after second-release task lands). Backplane carries no domain content. RegisterHub closed.

---

## Phase 7: User Story 5 — Group-name builders (Priority: P2)

**Goal**: Eliminate inline group-string construction. Every group string flows through a `*HubGroups` builder. CI grep gate enforces the rule retroactively.

**Independent Test**: Run `scripts/check-no-inline-group-strings.ps1`. Output: `OK: zero inline group-string literals found in production code`. Compilation breaks if any builder method's return type changes from `string` (validates the type contract).

### Tests for User Story 5

- [X] T092 [P] [US5] Unit tests for `TenantHubGroups`, `BlueprintHubGroups`, `WalletHubGroups`, `RegisterHubGroups` builders in their respective service-test projects (covering all formatting rules, GUID `:N`, wallet bech32, etc.).

### Implementation for User Story 5

- [X] T093 [US5] Sweep `src/Services/Sorcha.Blueprint.Service/` for inline string interpolations matching `"wallet:`, `"register:`, `"user:`, `"org:`, `"instance:`, `"system:` outside `*HubGroups.cs` and replace with builder calls.
- [X] T094 [P] [US5] Sweep `src/Services/Sorcha.Wallet.Service/` similarly.
- [X] T095 [P] [US5] Sweep `src/Services/Sorcha.Register.Service/` similarly.
- [X] T096 [P] [US5] Sweep `src/Services/Sorcha.Tenant.Service/` similarly.
- [X] T097 [P] [US5] Sweep `src/Apps/Sorcha.UI/`, `src/Apps/Sorcha.Cli/`, `src/Apps/Sorcha.Agent/` for client-side group-string construction passed to `SubscribeTo*` invocations. Replace with builder calls.
- [X] T098 [US5] Create `scripts/check-no-inline-group-strings.ps1` that greps the patterns under `src/` excluding `*HubGroups.cs` and `*Tests.cs`. Exits non-zero with file:line listing on hit.
- [X] T099 [US5] Add the script to a new `.github/workflows/group-name-builder-check.yml` GitHub Actions workflow that runs on every PR. Required check on `master`.

**Checkpoint**: US5 complete. CI enforces the convention forever.

---

## Phase 8: User Story 6 — Polling fallback as a primitive (Priority: P2)

**Goal**: Standardise the polling-fallback pattern across hub-backed UI surfaces. `HubConnectionWithFallback<TClient>` (already shipped in Phase 2) gets adopted by surfaces that have a corresponding REST refresher.

**Independent Test**: Pick three hub-backed UI surfaces. Open each, confirm realtime works, block WebSockets in DevTools. Within 20 s the surface starts polling its REST refresher (visible in Network tab). Restore — polling stops within 20 s, realtime resumes. No console errors, no toasts.

### Tests for User Story 6

- [ ] T100 [P] [US6] Playwright E2E `tests/Sorcha.UI.E2E.Tests/Docker/PollingFallbackTests.cs` covering three surfaces (WalletDetail, MyActions, MainLayout inbox bell). Block WebSocket via `Page.Route()` to simulate disconnect. Assert REST polling activates and deactivates per the spec.

### Implementation for User Story 6

- [X] T101 [US6] Adopt `HubConnectionWithFallback<IWalletHubClient>` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/WalletHubConnection.cs`. Refresher delegate calls existing `GET /api/wallets/{addr}` endpoint.
- [X] T102 [P] [US6] Adopt `HubConnectionWithFallback<IBlueprintHubClient>` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/BlueprintHubConnection.cs`. Refresher calls `GET /api/instances/{id}` per subscribed instance.
- [X] T103 [P] [US6] Adopt `HubConnectionWithFallback<ITenantHubClient>` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/TenantHubConnection.cs`. Refresher calls `GET /api/me/inbox/unread-count`.
- [X] T104 [P] [US6] Adopt `HubConnectionWithFallback<IRegisterHubClient>` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterHubConnection.cs`. Refresher calls `GET /api/registers/{id}` per subscribed register.
- [X] T105 [US6] Surface a "Reconnecting…" affordance in the existing `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` driven by the connection-state observable on the inbox connection. Inline indicator only — no blocking toast.

**Checkpoint**: US6 complete. Every hub-backed UI surface degrades gracefully on disconnect.

---

## Phase 9: User Story 7 — Phase-5 deferral (Priority: P3, foundation-only)

**Goal**: Lock in the inbox-write-time `ChannelHints` data model so a future phase 5 (web push dispatch + preferences UI + cross-tab) is purely UI + dispatcher work. No dispatchers ship in this phase.

**Independent Test**: Confirm `ChannelHints` field is persisted on every `InboxEntry`. Default channel set is applied when writer omits hints. Only the `Inbox` channel is dispatched today; `Push` / `Email` / `Digest` are recorded but inert.

### Implementation for User Story 7

- [X] T106 [US7] Confirm the `ChannelHints` `[Flags]` enum and the `ChannelHints` field on `InboxEntry` (added in T062) carry through migrations and serialization. Add unit assertion.
- [X] T107 [P] [US7] Implement default-hints-by-category logic in `InboxService`: `Action → Inbox|Push|Email`, `Credential → Inbox|Push`, `Membership → Inbox|Email`, `Security → Inbox|Push|Email`, `System → Inbox`, `Workflow → Inbox`. Per research § Resolved unknowns.
- [X] T108 [US7] Document in `specs/118-notifications-architecture/spec.md` (under Out of Scope) and in `MIGRATION.md` (created in T052) that phase-5 dispatch is deferred and reference the locked-in data model.

**Checkpoint**: US7 complete. Phase-5 unblocked at the data layer.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: UI rewrites (the three components that need new logic, not just rewires), CLI/Agent migration, EventsHub decommission, deprecation alias retirement, docs sync.

### UI rewires (mechanical)

- [X] T109 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyActions.razor` to inject `BlueprintHubConnection` instead of `ActionsHubConnection`. Subscribe to `OnActionAvailable` / `OnActionRejected` / `OnWorkflowCompleted`.
- [X] T110 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyCredentials.razor` to inject `WalletHubConnection`. Subscribe to `OnCredentialReceived` / `OnCredentialStatusChanged`.
- [X] T111 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor` from `RegisterHubConnection.OnTransactionReceipted` to `WalletHubConnection.OnTransactionReceipted`.
- [X] T112 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EncryptionOperationTracker.cs` to inject `WalletHubConnection`.
- [X] T113 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/EncryptionProgressIndicator.razor` to wire `WalletHubConnection`.
- [X] T114 [P] Migrate `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/OperationNotificationListener.razor` to wire `WalletHubConnection`.

### UI rewrites (logic changes — inbox-driven)

- [X] T115 Rewrite `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor` so the unread-count badge reads from `TenantHubConnection.OnInboxUnreadCountUpdated` plus a REST seed from `GET /api/me/inbox/unread-count` on mount. Remove `EventsHubConnection` injection.
- [X] T116 Rewrite `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/ActivityLogPanel.razor` to render from `GET /api/me/inbox` paginated results, with `TenantHubConnection.OnInboxEntryAdded` triggering page-1 refresh. Implement 30 s correlation grouping in the render path.
- [X] T117 Rewrite `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionToast.razor` to fire on `OnInboxEntryAdded` filtered to `Category=Action`. Toast carries title only; click navigates to `DetailHref`.
- [X] T118 Rewrite `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/PendingActionInbox.razor` so it IS the inbox UI: list view, per-entry read/dismiss buttons calling the inbox API, correlation grouping, category filters.

### Non-UI subscribers

- [X] T119 Rewrite `src/Apps/Sorcha.Cli/Services/EventStreamService.cs` to consume `RegisterHub` + `BlueprintHub` + `WalletHub` via `SorchaHubConnectionBuilder` (replacing the rolled-own connection logic and dropping EventsHub). Update `src/Apps/Sorcha.Cli/Commands/EventWatchCommand.cs` option set to reflect new hub paths.
- [X] T120 Update `src/Apps/Sorcha.Agent/Inbox/SignalRInboxListener.cs` to connect to `/hubs/blueprint` instead of `/actionshub`. Update `walkthroughs/*/actors/*.json` if any contain hardcoded hub paths.

### EventsHub decommission

- [ ] T121 After parallel-fire window closes and `sorcha_signalr_events_hub_subscribers` has been at zero across all replicas for the full release cycle: delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/EventsHubConnection.cs`, `src/Services/Sorcha.Blueprint.Service/Hubs/EventsHub.cs`, `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EventsHubNotificationBridge.cs`. Replace `MapHub<EventsHub>` with a `MapGet("/hubs/events", ...)` that returns 410 with the structured JSON body from FR-004.
- [ ] T122 After the deprecation window for `/actionshub` closes: replace its alias in `src/Services/Sorcha.Blueprint.Service/Program.cs` with a `MapGet("/actionshub", ...)` returning 410 with structured JSON naming `/hubs/blueprint`.

### Docs and observability

- [X] T123 [P] Update `docs/reference/API-DOCUMENTATION.md` with the new `/api/me/inbox/*`, `/api/internal/inbox`, and the consolidated hub routes.
- [X] T124 [P] Update `docs/reference/architecture.md` with the five-hub topology diagram and the inbox flow walkthrough.
- [X] T125 [P] Update `.claude/skills/signalr/SKILL.md` to reflect the five-hub topology, `AddSorchaHub` extension, group builders, and ChatHub exception.
- [X] T126 [P] Add a section to `STANDARDS.md` (Feature 117) noting "Notifications & Inbox" as an implemented capability with a link to `specs/118-notifications-architecture/spec.md`.
- [X] T127 [P] Add Grafana dashboard JSON at `ops/grafana/dashboards/sorcha-signalr.json` consuming the `Sorcha.SignalR` meter (connections, messages-sent, backplane-state, reconnects). Include alert rules for backplane-state ≠ up.
- [ ] T128 Run quickstart verification end-to-end against a fresh Docker host per `quickstart.md`. Capture pass/fail per step in `specs/118-notifications-architecture/quickstart-verification.md`.

---

## Dependencies

```
Phase 1 (Setup)
   │
   ▼
Phase 2 (Foundational) ── BLOCKING ──┐
   │                                  │
   ▼                                  │
Phase 3 (US1 — backplane on existing hubs)  ── MVP ──┐
   │                                                  │
   ▼                                                  │
Phase 4 (US2 — topology consolidation, TenantHub class, EventsHub parallel-fire)
   │       ┌──────────────────────────────────┐
   │       ▼                                  │
   │   Phase 7 (US5 — group builders) ────────┤   independent of US3/US4 once topology lands
   │                                          │
   ▼                                          │
Phase 5 (US3 — inbox domain on TenantHub)     │
   │                                          │
   │       ┌──────────────────────────────────┘
   │       ▼
Phase 6 (US4 — thin-signal contract; RegisterHub auth two-release split)
   │
   ▼
Phase 8 (US6 — polling fallback adoption) ── independent once US1 + US2 + US3 land
   │
   ▼
Phase 9 (US7 — channel-hints data lock-in) ── piggybacks on US3 implementation
   │
   ▼
Phase 10 (Polish — UI rewrites, EventsHub decommission, docs)
```

**Story independence**:
- US1 ships alone as MVP. No topology change, just backplane on existing hubs.
- US2 can ship without US3 (TenantHub exists empty/auth-only for one release).
- US3 requires US2's TenantHub class, but US3 is otherwise independent of every other story.
- US4 builds on US1 + US2; can land in parallel with US3 since it touches different files.
- US5 builds on US2 (group builders are part of the topology consolidation).
- US6 builds on US1 (`HubConnectionWithFallback` from Phase 2).
- US7 piggybacks on US3 — `ChannelHints` is part of `InboxEntry`.

## Parallel Execution Opportunities

**Phase 2 (Foundational)** — high parallelism after T006/T008 land:
- T007, T010, T012, T013, T014, T015, T016 are all `[P]` and touch independent files in `Sorcha.ServiceDefaults`.

**Phase 3 (US1)** — backplane wiring per service:
- T022, T023, T025, T026, T028 across Wallet / Register services run in parallel after the Blueprint baseline (T019—T021) lands.

**Phase 4 (US2)** — group builders + topology:
- T036, T037, T042, T046, T048 (builder files) all parallelizable.
- T031—T034 (tests) all parallelizable.

**Phase 5 (US3)** — bulk-parallel test & writer wiring:
- T054—T061 (tests) all `[P]`.
- T072, T075, T076 (cross-service writers + delivery service updates) parallelizable.

**Phase 6 (US4)** — XML doc updates:
- T083—T086 each touch a different interface file — parallelizable.
- T087, T088 (UI consumers) on different files — parallelizable.

**Phase 7 (US5)** — sweeps:
- T093—T097 parallel by service / app project.

**Phase 10 (Polish)** — bulk-parallel UI rewires + docs:
- T109—T114 each touch a different `.razor` or `.cs` file — parallelizable.
- T123—T127 (docs + dashboards) all `[P]`.

## Implementation Strategy

**MVP**: Phases 1 + 2 + 3 alone deliver the multi-node correctness fix without touching topology, contracts, or UI surfaces. This is the smallest deployable improvement and pays for the whole feature: notifications stop being silently lost on multi-replica deploys. Estimated 16 tasks (T001 — T030, minus the parallel-fire and gauge tasks that belong to US2).

**Incremental delivery beyond MVP**:
1. **Release N**: Phases 1 — 3 (MVP — backplane).
2. **Release N+1**: Phase 4 (topology consolidation, EventsHub parallel-fire begins).
3. **Release N+2**: Phase 5 (inbox domain shipped — UI begins consuming).
4. **Release N+2 / N+3**: Phases 6 — 8 in parallel (thin-signal tightening, group-builder enforcement, polling-fallback adoption).
5. **Release N+3**: Phase 9 (channel-hints lock-in — piggybacks on Phase 5 implementation).
6. **Release N+3 / N+4**: Phase 10 (UI rewrites, EventsHub retirement after gauge confirms zero subscribers, deprecation alias removal).

**RegisterHub auth cutover (T091)** ships only after the authenticated-counter gauge (T090) reports ≥ 99 % adoption — explicitly a follow-up release, never in the same release as T089.

**EventsHub deletion (T121)** ships only after `sorcha_signalr_events_hub_subscribers` has been at zero across all replicas for one full release cycle — measured via Grafana dashboard from T127.

**Risk surface**: `RegisterHub` auth cutover and EventsHub decommission are the two metric-gated steps. Everything else is deterministic and lands when the prior phase's checkpoint is green.
