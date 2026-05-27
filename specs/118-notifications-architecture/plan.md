# Implementation Plan: Notifications & Realtime Architecture

**Branch**: `118-notifications-architecture` | **Date**: 2026-05-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/118-notifications-architecture/spec.md`
**Companion design doc**: `docs/superpowers/specs/2026-05-05-notifications-architecture-design.md`

## Summary

Rationalise Sorcha's notification, alert, and realtime status surfaces into a coherent layer before any further realtime feature lands on top of it. Five SignalR hubs replace today's drifted set: TenantHub (new, owns the durable user inbox), BlueprintHub (renamed from ActionsHub, absorbs the workflow half of EventsHub), WalletHub (absorbs the wallet/credential/encryption half of EventsHub), RegisterHub (gains `[Authorize]`), and ChatHub (the deliberate exception for RPC-streaming AI Designer). All notification hubs are wired through one `AddSorchaHub<THub, TClient>` extension in `Sorcha.ServiceDefaults.Hubs` that registers JWT auth + Redis backplane (with `ChannelPrefix = sorcha:signalr:{serviceShortName}` for cross-service isolation) + reconnect-jitter + OpenTelemetry instrumentation + the Feature 113 storage registration log entry.

Six P1/P2 user stories drive the work: (US1) multi-node correctness via the Redis backplane that's been a TODO since the project went multi-replica; (US2) the topology consolidation and EventsHub retirement; (US3) the durable user inbox owned by Tenant Service, with fine-grained correlated entries grouped visually within a 30s window; (US4) the thin-signal contract that forbids domain payload on the wire so backplane reads carry no PII or claims; (US5) code-level group-name builders that eliminate inline string construction; (US6) standardised polling fallback for hub-backed UI surfaces. A P3 phase-5 deferral story (US7) locks in the inbox-write-time `ChannelHints` data model so future push and preferences work is purely UI + dispatcher, not a data migration.

The work is brownfield and phased to avoid flag-day breakage. ActionsHub's route `/actionshub` is aliased to `/hubs/blueprint` for one release cycle then returns 410 Gone. EventsHub fires its events in parallel with their new homes during the deprecation window, and only retires after `sorcha_signalr_events_hub_subscribers` stays at zero for the entire window. RegisterHub gains `[Authorize]` only after the UI client ships its token-passing change one release earlier. Citizen Wallet PWA realtime (Feature 114) is explicitly out of scope; its `wallet:platform-user:{guid:N}` group and `DeviceRevoked` / `CredentialAvailable` events on WalletHub are preserved unchanged.

## Technical Context

**Language/Version**: C# 14, .NET 10. EF Core migrations for the Tenant DB. Razor / Blazor WASM on the UI side. PowerShell + Bash for local + CI scripting.
**Primary Dependencies**: `Microsoft.AspNetCore.SignalR` (already in tree); `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (NEW — the backplane package). `StackExchange.Redis` (already in tree, used by SorchaConnections cascade). `Microsoft.EntityFrameworkCore` + Npgsql for the Tenant DB `InboxEntry` migration. `OpenTelemetry.Instrumentation.AspNetCore` for the SignalR meter (existing). `Sorcha.ServiceDefaults` and `Sorcha.ServiceDefaults.Storage` (existing) — the new `Sorcha.ServiceDefaults.Hubs` namespace lands here. `Sorcha.AtomicCache` (Feature 113) for the unread-count Redis sorted-set index. xUnit + FluentAssertions + Moq for unit and integration tests. Playwright for hub-aware E2E tests under `tests/Sorcha.UI.E2E.Tests/`.
**Storage**: PostgreSQL (Tenant DB) for the durable `InboxEntry` table — new migration on `Sorcha.Tenant.Service`. Redis for the SignalR backplane (per-service channel prefix) and for the per-user unread-count sorted-set index. Both audited via `IStorageRegistrationLog` from Feature 113; production fail-fast applies. No file-system storage.
**Testing**: xUnit for unit tests across the new `Sorcha.ServiceDefaults.Hubs` extension, the `InboxService`, the `InboxBridgeService`, and each migrated hub. Integration tests under `tests/Sorcha.Tenant.Service.Tests/Integration/InboxEndpointsTests.cs` and `tests/Sorcha.Blueprint.Service.Tests/Integration/SignalRIntegrationTests.cs` (existing — needs update). New cross-replica multi-node correctness test under `tests/Sorcha.Integration.Tests/MultiNode/` exercising the backplane in a Docker-Compose two-replica fixture. Existing 10 hub-related test files all touched. Playwright E2E for the inbox bell + activity panel + polling fallback story.
**Target Platform**: Linux server (Docker / docker-compose), net10.0. CI on GitHub Actions `ubuntu-latest` with Postgres + Redis service containers. Production target n1.sorcha.dev (Azure VM with the existing docker-compose stack — Feature 113 already requires Postgres + Redis configured there).
**Project Type**: Existing multi-service monorepo. No new services — all changes route through existing service projects (Tenant, Blueprint, Wallet, Register) plus shared infra (`Sorcha.ServiceDefaults`, `Sorcha.ServiceClients.Http`).
**Performance Goals**: Hub event end-to-end latency ≤ 200 ms p95 in two-replica deployment with backplane (NFR-001). Inbox-write to `InboxEntryAdded` hub-event ≤ 300 ms p95 (NFR-002). `GET /api/me/inbox` first page (20 entries) ≤ 100 ms p95 against Postgres with 10⁵ entries per user (NFR-003). Hub event payload ≤ 200 bytes p99 — verifies thin-signal contract (NFR-004). Backplane Redis pub/sub bandwidth ≤ 1 KB/s/connection p95 nominal (NFR-005).
**Constraints**: Multi-node correctness is the headline requirement — no fan-out can be lost across replicas (FR-008, US1). Thin-signal contract forbids domain payload on the wire (FR-016 — FR-019). Production / Staging fail-fast at startup if any audited interface (`IInboxStore`, the unread-count Redis index, the SignalR backplane) resolves in-memory (FR-010, FR-024, FR-028). RegisterHub `[Authorize]` cutover is a planned flag day requiring lockstep UI ship-first (FR-011). EventsHub retirement requires parallel-fire window with metric-driven decommission (FR-038).
**Scale/Scope**: Moderate. ~50 distinct tasks across 5 phases. Touches 5 hub classes (one new), 1 new EF migration on Tenant DB, ~30 file moves/renames in Blueprint Service alone, 12 UI subscriber files (3 of which are full rewrites), the CLI `EventStreamService`, and the Agent's `SignalRInboxListener`. New common code lands in `src/Common/Sorcha.ServiceDefaults/Hubs/` (5 files). Bounded by the audit inventory in the design doc — every touch is enumerated.

## Constitution Check

| Principle | Assessment |
|---|---|
| **I. Microservices-First Architecture** | PASS. No new services. TenantHub becomes a new endpoint *within* the existing Tenant Service. Per-domain hub topology actively reinforces service boundaries — wallet events on WalletHub, workflow events on BlueprintHub, identity events on TenantHub. The cross-service inbox write goes through Tenant's HTTP API (`POST /api/internal/inbox`) gated by `RequireService` — no shared mutable state, no upward dependency, services that emit inbox entries don't depend on Tenant Service code, only on its HTTP contract. |
| **II. Security First** | PASS — and tightens the security posture. RegisterHub's existing unauthenticated state is closed (FR-011). Thin-signal contract (FR-016 — FR-019) ensures backplane Redis carries no domain content; if Redis credentials leak the blast radius is "events happened" not "this is what's in them." Internal inbox endpoint gated by `RequireService` policy. JWT Bearer with `platform_user_id` claim guard required on every connection (FR-007). The audit found no new attack surface. |
| **III. API Documentation** | PASS. New endpoints (`/api/me/inbox/*`, `/api/internal/inbox`) use Minimal APIs with `.WithSummary()` / `.WithDescription()` per CLAUDE.md pattern #1. SignalR hub typed-client interfaces carry XML docs linking each event method to its REST detail endpoint (FR-018). OpenAPI surface at `/openapi/v1.json` reflects the new endpoints. The Feature 117 AI-discoverability surface (`STANDARDS.md`, `llms.txt`, MCP manifest) is unaffected — no new standards claims, no new MCP tools. |
| **IV. Testing Requirements** | PASS. New code targets > 85 % coverage. Unit tests for the `AddSorchaHub` extension, `InboxService`, `InboxBridgeService`, every `*HubGroups` builder, the `HubSignal` envelope, the `HubConnectionWithFallback` wrapper. Integration tests for the inbox endpoints and each migrated hub. New cross-replica multi-node test fixture (the headline gap). Existing 10 hub-related test files updated to track the topology changes. xUnit Arrange-Act-Assert pattern preserved. |
| **V. Code Quality** | PASS. async/await throughout; nullable reference types preserved; no Release-build warnings. The `SorchaHubConventions` extension consolidates auth + backplane + tracing wiring that's currently duplicated across services — net reduction of code in service `Program.cs` files. The `*HubGroups` builder pattern eliminates inline string construction (FR-013, FR-014). |
| **VI. Blueprint Creation Standards** | N/A — no Blueprint authoring in this spec. |
| **VII. Domain-Driven Design** | PASS — and improves DDD posture. Today's EventsHub is a leaky cross-domain abstraction (workflow + wallet + generic activity-feed events all on one hub in Blueprint Service). Migration moves each event type to the service that owns its domain: workflow events to BlueprintHub (Blueprint owns `Action`), wallet events to WalletHub (Wallet owns `Credential`, `EncryptionOperation`), identity events to TenantHub (Tenant owns `User`, `Org`, `Membership`). The new `InboxEntry` is a Tenant-domain aggregate with the ubiquitous-language category names (`Action`, `Credential`, `Membership`, `Security`, `System`, `Workflow`). |
| **VIII. Observability by Default** | PASS — and adds new instrumentation. New `Sorcha.SignalR` OpenTelemetry meter exposes connection count, message-sent rate, backplane state, and reconnect rate per hub (FR-039). Every hub event propagates W3C `traceId` so a notification can be traced from origin service through Redis backplane to client receipt (FR-040). The Feature 113 storage registration log records every hub backplane and the inbox storage. Health checks unchanged — existing per-service `/health` and `/alive` endpoints continue to surface degraded state. |

**Constitution gate: PASS.** No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/118-notifications-architecture/
├── spec.md              # (complete)
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── README.md
│   ├── hub-signal.schema.json
│   ├── inbox-entry.schema.json
│   ├── inbox-endpoints.openapi.yaml
│   ├── tenant-hub-client.cs.md
│   ├── blueprint-hub-client.cs.md
│   ├── wallet-hub-client.cs.md
│   └── register-hub-client.cs.md
└── tasks.md             # Phase 2 output (NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.ServiceDefaults/
│   │   └── Hubs/                                            # NEW namespace
│   │       ├── AddSorchaHubExtensions.cs                    # NEW — services.AddSorchaHub<THub, TClient>(...)
│   │       ├── SorchaHubConventions.cs                      # NEW — auth + backplane + tracing wiring
│   │       ├── HubSignal.cs                                 # NEW — thin-signal envelope record
│   │       ├── HubConnectionWithFallback.cs                 # NEW — UI wrapper with poll-fallback
│   │       └── SignalRMetrics.cs                            # NEW — sorcha_signalr_* OTel meter
│   └── Sorcha.ServiceClients.Http/Hub/
│       └── SorchaHubConnectionBuilder.cs                    # CHANGE — add reconnect jitter, surface ConnState observable
│
├── Services/
│   ├── Sorcha.Tenant.Service/                               # gains TenantHub + Inbox domain
│   │   ├── Program.cs                                       # CHANGE — AddSorchaHub<TenantHub, ITenantHubClient>(...)
│   │   ├── Hubs/
│   │   │   ├── TenantHub.cs                                 # NEW
│   │   │   ├── ITenantHubClient.cs                          # NEW
│   │   │   └── TenantHubGroups.cs                           # NEW
│   │   ├── Services/
│   │   │   ├── IInboxService.cs / InboxService.cs           # NEW — durable storage + emit
│   │   │   └── InboxBridgeService.cs                        # NEW — Redis subscriber → inbox writes
│   │   ├── Endpoints/
│   │   │   ├── MeInboxEndpoints.cs                          # NEW — GET/POST /api/me/inbox/*
│   │   │   └── InternalInboxEndpoints.cs                    # NEW — POST /api/internal/inbox (RequireService)
│   │   ├── Models/
│   │   │   └── InboxEntry.cs                                # NEW — EF entity + enums
│   │   └── Migrations/
│   │       └── ###_AddInboxEntry.cs                         # NEW — Postgres table + indexes
│   │
│   ├── Sorcha.Blueprint.Service/
│   │   ├── Program.cs                                       # CHANGE — drop Events; rename Actions→Blueprint; alias /actionshub
│   │   ├── Hubs/
│   │   │   ├── BlueprintHub.cs                              # RENAME — was ActionsHub.cs (typed client now mandatory)
│   │   │   ├── IBlueprintHubClient.cs                       # NEW
│   │   │   ├── BlueprintHubGroups.cs                        # NEW
│   │   │   ├── EventsHub.cs                                 # DELETE after parallel-fire window
│   │   │   └── ChatHub.cs                                   # CHANGE — XML doc marks as deliberate exception
│   │   ├── Services/Implementation/
│   │   │   ├── NotificationService.cs                       # CHANGE — emit only workflow events
│   │   │   ├── EventsHubNotificationBridge.cs               # DELETE
│   │   │   └── BlueprintInboxWriter.cs                      # NEW — calls Tenant POST /api/internal/inbox
│   │
│   ├── Sorcha.Wallet.Service/                               # WalletHub absorbs encryption + credential events
│   │   ├── Program.cs                                       # CHANGE — AddSorchaHub call
│   │   ├── Hubs/
│   │   │   ├── WalletHub.cs                                 # CHANGE — typed client; absorbs new events
│   │   │   ├── IWalletHubClient.cs                          # NEW
│   │   │   └── WalletHubGroups.cs                           # NEW (formalises existing GroupNameFor)
│   │   ├── Services/Implementation/
│   │   │   ├── NotificationDeliveryService.cs               # CHANGE — retarget to Tenant inbox endpoint
│   │   │   ├── NotificationDigestWorker.cs                  # CHANGE — retarget to Tenant inbox endpoint
│   │   │   ├── TransactionLifecycleEventBridge.cs           # CHANGE — output to WalletHub directly
│   │   │   └── WalletInboxWriter.cs                         # NEW — calls Tenant for credential entries
│   │
│   └── Sorcha.Register.Service/
│       ├── Program.cs                                       # CHANGE — AddSorchaHub; RegisterHub now [Authorize]
│       ├── Hubs/
│       │   ├── RegisterHub.cs                               # CHANGE — add [Authorize]
│       │   └── RegisterHubGroups.cs                         # NEW (formalise convention)
│
└── Apps/
    ├── Sorcha.UI/
    │   ├── Sorcha.UI.Core/Services/
    │   │   ├── BlueprintHubConnection.cs                    # NEW — split from ActionsHubConnection (workflow half)
    │   │   ├── WalletHubConnection.cs                       # NEW — split from ActionsHubConnection (wallet half)
    │   │   ├── TenantHubConnection.cs                       # NEW
    │   │   ├── ActionsHubConnection.cs                      # DELETE after migration
    │   │   ├── EventsHubConnection.cs                       # DELETE after migration
    │   │   ├── RegisterHubConnection.cs                     # CHANGE — start passing JWT
    │   │   ├── ChatHubConnection.cs                         # unchanged
    │   │   └── EncryptionOperationTracker.cs                # CHANGE — inject WalletHubConnection
    │   ├── Sorcha.UI.Core/Components/Admin/
    │   │   ├── EncryptionProgressIndicator.razor            # CHANGE — wire WalletHub
    │   │   └── OperationNotificationListener.razor          # CHANGE — wire WalletHub
    │   └── Sorcha.UI.Web.Client/
    │       ├── Pages/
    │       │   ├── MyActions.razor                          # CHANGE — wire BlueprintHub
    │       │   ├── MyCredentials.razor                      # CHANGE — wire WalletHub
    │       │   └── Wallets/WalletDetail.razor               # CHANGE — OnTransactionReceipted via WalletHub
    │       └── Components/Layout/
    │           ├── MainLayout.razor                         # CHANGE — multi-hub rewire
    │           ├── ActivityLogPanel.razor                   # REWRITE — inbox-driven
    │           ├── PendingActionToast.razor                 # REWRITE — inbox-driven
    │           └── PendingActionInbox.razor                 # REWRITE — IS the inbox UI
    │
    ├── Sorcha.Cli/
    │   ├── Services/EventStreamService.cs                   # REWRITE — multi-hub via SorchaHubConnectionBuilder
    │   └── Commands/EventWatchCommand.cs                    # CHANGE — option set update
    │
    └── Sorcha.Agent/
        └── Inbox/SignalRInboxListener.cs                    # CHANGE — retarget to /hubs/blueprint

tests/
├── Sorcha.ServiceDefaults.Tests/Hubs/
│   ├── AddSorchaHubExtensionsTests.cs                       # NEW
│   ├── HubSignalTests.cs                                    # NEW
│   ├── HubConnectionWithFallbackTests.cs                    # NEW
│   └── SignalRMetricsTests.cs                               # NEW
├── Sorcha.Tenant.Service.Tests/
│   ├── Services/InboxServiceTests.cs                        # NEW
│   ├── Services/InboxBridgeServiceTests.cs                  # NEW
│   ├── Endpoints/MeInboxEndpointsTests.cs                   # NEW
│   ├── Endpoints/InternalInboxEndpointsTests.cs             # NEW
│   └── Hubs/TenantHubGroupsTests.cs                         # NEW
├── Sorcha.Blueprint.Service.Tests/
│   ├── Hubs/BlueprintHubGroupsTests.cs                      # NEW
│   ├── Integration/SignalRIntegrationTests.cs               # CHANGE — hub renames
│   └── Services/NotificationServiceTests.cs                 # CHANGE — workflow-only events
├── Sorcha.Wallet.Service.Tests/
│   ├── Hubs/WalletHubGroupsTests.cs                         # NEW
│   ├── Services/NotificationDeliveryServiceTests.cs         # CHANGE — Tenant inbox target
│   └── Services/NotificationDigestWorkerTests.cs            # CHANGE — Tenant inbox target
├── Sorcha.Register.Service.Tests/
│   ├── Hubs/RegisterHubGroupsTests.cs                       # NEW
│   └── SignalRHubTests.cs                                   # CHANGE — [Authorize] semantics
├── Sorcha.UI.Core.Tests/Services/
│   ├── BlueprintHubConnectionTests.cs                       # NEW
│   ├── WalletHubConnectionTests.cs                          # NEW
│   ├── TenantHubConnectionTests.cs                          # NEW
│   └── RegisterHubConnectionTests.cs                        # CHANGE — JWT pass-through
└── Sorcha.Integration.Tests/MultiNode/
    └── HubBackplaneCrossReplicaTests.cs                     # NEW — the headline correctness gate

scripts/
└── check-no-inline-group-strings.ps1                        # NEW — CI grep gate for FR-014
```

**Structure Decision**: Existing multi-service Sorcha monorepo. No new services and no new top-level projects — every change targets an existing project. The new common surface (`Sorcha.ServiceDefaults.Hubs`) lands inside the existing `Sorcha.ServiceDefaults` project as a sub-namespace, which is the established pattern (cf. `Sorcha.ServiceDefaults.Storage` from Feature 113). The tree above lists every file the migration touches; nothing is hand-waved as "TBD." Compatibility surface (deprecated route aliases, parallel-fire windows, ship-UI-before-server-auth) is captured in `tasks.md` as explicit phase ordering rather than left to deployment discretion.

## Complexity Tracking

No constitution violations. Three complexity calls worth surfacing because they look like over-engineering at first glance:

| Decision | Why kept | Simpler alternative rejected because |
|---|---|---|
| Per-service `ChannelPrefix = sorcha:signalr:{serviceShortName}` on the Redis backplane | Each service's hub backplane traffic is isolated in its own Redis keyspace. Without it, all five hubs share one pub/sub fan-out and Redis sees N×events of cross-service chatter. | Sharing one prefix would cost ~15 lines and feel "simpler," but Redis pub/sub fan-out is unbounded — every replica of every service receives every event, filters in memory, drops most. CPU-cheap but noisy and unreviewable. |
| Redis sorted-set as the unread-count index *in addition to* the Postgres `InboxEntry` table | Atomic INCR/DECR for unread-count gives O(1) reads under load and matches the existing atomic-cache pattern from Feature 113. The Postgres table is the source of truth; Redis is a read-time accelerator. | Counting unread entries via `SELECT COUNT(*)` works for small users but degrades sharply at 10⁵ entries — NFR-003 requires ≤ 100 ms p95 for the first-page query, which means the count cannot share that budget. |
| Three full UI rewrites (`ActivityLogPanel`, `PendingActionToast`, `PendingActionInbox`) instead of mechanical rename | The three components consume the generic `OnEventReceived` shape today, which is exactly what the spec retires. Their backing model changes from "hub event stream" to "REST + hub-nudge" — no rename can produce a correct result. | A 1:1 rewire would compile but render stale data on reload (no REST seed) and lose entries when the hub reconnects (no replay). Both behaviours are existing bugs the spec exists to fix. |
