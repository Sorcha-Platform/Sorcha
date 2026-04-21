---
description: "Task list for Feature 108 — Register State Aggregation & Local Relationship"
---

# Tasks: Register State Aggregation & Local Relationship

**Input**: Design documents from `specs/108-register-local-relationship/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/`, `quickstart.md`

**Tests**: Included because `plan.md` enumerates test projects and constitution principle IV mandates ≥85% coverage on new code. Tests live alongside implementation (not strict TDD-first) — write them before declaring a task complete.

**Organization**: Tasks are grouped by user story. Within this feature, User Story 1 (the PingPongN1 round-trip) is an **integration verification** of User Stories 2/4/5 — its implementation work is almost entirely captured in those other stories. Phases are therefore ordered by natural execution sequence rather than strict spec priority order: US2 → US3 → US4 → US5 → US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies).
- **[Story]**: `[US1]`..`[US5]` maps to the spec's user stories.
- Absolute-style paths under `src/` and `tests/` — exact enough to hand to an implementer.

## Path Conventions

Sorcha microservices layout:

- **Common models & clients**: `src/Common/Sorcha.Register.Models/`, `src/Common/Sorcha.ServiceClients.Http/`
- **Core derivation**: `src/Core/Sorcha.Register.Core/`
- **Services**: `src/Services/Sorcha.{Register,Peer,Validator,Blueprint}.Service/`
- **Tests**: `tests/Sorcha.{Register.Core,Register.Service.IntegrationTests,Peer.Service,Validator.Service,Blueprint.Service}.Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Thin — no new projects to create. Just register the two new cross-cutting constants the rest of the feature uses.

- [ ] T001 Add `CanReportRegisterObservation` authorization policy constant and policy registration in `src/Common/Sorcha.ServiceDefaults/Authorization/AuthorizationPolicies.cs` (create file if missing) so internal observation-push endpoints can require it. Mirror the pattern used by `CanWriteDockets` / `CanReadTransactions`.
- [ ] T002 [P] Add Redis channel name constant `register:relationship-changed` in `src/Common/Sorcha.Register.Models/Events/RegisterEventChannels.cs` alongside the existing channel constants.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared DTOs, enums, client-interface changes, and the in-memory observation store. All five user stories consume at least one item from this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### New wire contracts and types

- [ ] T003 [P] Add `RegisterSyncState` enum (`Indeterminate`, `Syncing`, `CaughtUp`, `Error`) in `src/Common/Sorcha.Register.Models/Enums/RegisterSyncState.cs` — see `data-model.md` §1.
- [ ] T004 [P] Add `RegisterRoleSet` `[Flags]` enum (`None`, `Owner`, `Admin`, `Auditor`, `Designer`, `Validator`) in `src/Common/Sorcha.Register.Models/LocalRelationship/RegisterRoleSet.cs`.
- [ ] T005 [P] Add `RegisterLocalRelationship` record in `src/Common/Sorcha.Register.Models/LocalRelationship/RegisterLocalRelationship.cs` with derived `IsOwner`/`IsAdmin`/`IsAuditor`/`IsDesigner`/`IsValidator`/`IsSubscriber` convenience properties — see `data-model.md` §2.
- [ ] T006 [P] Add `PeerHeightObservation` record in `src/Common/Sorcha.Register.Models/Observations/PeerHeightObservation.cs` with `[Required]`/validation attributes — see `data-model.md` §3.
- [ ] T007 [P] Add `ValidatorSealingObservation` record in `src/Common/Sorcha.Register.Models/Observations/ValidatorSealingObservation.cs` — see `data-model.md` §4.
- [ ] T008 [P] Add `RegisterRelationshipChangedEvent` record in `src/Common/Sorcha.Register.Models/Events/RegisterRelationshipChangedEvent.cs` (Redis pub/sub payload) — see `data-model.md` §7.
- [ ] T009 [P] Add `RegisterSyncStateView` + nested `ValidatorSealingSnapshot` record in `src/Common/Sorcha.Register.Models/LocalRelationship/RegisterSyncStateView.cs` — see `data-model.md` §8.

### Register entity migration

- [ ] T010 Migrate `Register.SyncState` from `string?` to `RegisterSyncState?` in `src/Common/Sorcha.Register.Models/Register.cs` and add a BSON class-map converter in `src/Core/Sorcha.Register.Storage.MongoDB/Serialization/RegisterSyncStateConverter.cs` handling legacy values per `research.md` D10 (`"Subscribing"/"Syncing"` → `Syncing`, `"Synced"` → `CaughtUp`, `"Error"` → `Error`, `null` → `Indeterminate`, unknown → log+`Indeterminate`).

### Shared client-interface additions (declarations only; implementations land in each story)

- [ ] T011 Extend `IRegisterServiceClient` in `src/Common/Sorcha.ServiceClients.Http/Register/IRegisterServiceClient.cs` with: `ReportPeerHeightAsync`, `ReportValidatorSealingAsync`, `GetLocalRelationshipAsync`, `GetSyncStateAsync`, `GetMyValidatedRegistersAsync`. XML docs on each matching the contract in `contracts/register-service-*.yaml`.
- [ ] T012 Extend `IPeerServiceClient` in `src/Common/Sorcha.ServiceClients/Peer/IPeerServiceClient.cs` with `DistributeTransactionAsync(registerId, TransactionSubmission, ct)`. XML docs per `contracts/peer-service-distribute-submission.proto`.

### Observation store (Core)

- [ ] T013 [P] Add `IObservationStore` interface in `src/Core/Sorcha.Register.Core/Observations/IObservationStore.cs` — see `data-model.md` §5.
- [ ] T014 Implement `ObservationStore` with per-register `ConcurrentDictionary<sourcePeerId, PeerHeightObservation>` + single-slot `ValidatorSealingObservation`; cap distinct peers at 16 with oldest eviction; thread-safe. File: `src/Core/Sorcha.Register.Core/Observations/ObservationStore.cs`. Register as singleton in `Program.cs` of Register.Service.
- [ ] T015 [P] Add `ObservationStorePruner` as `IHostedService` in `src/Core/Sorcha.Register.Core/Observations/ObservationStorePruner.cs` — evicts observations whose register has been silent for >30 minutes to prevent memory growth.

### Local identity provider (Core)

- [ ] T016 Add `ILocalIdentityProvider` interface and `LocalIdentityProvider` implementation in `src/Core/Sorcha.Register.Core/LocalRelationship/LocalIdentityProvider.cs`. Resolves `{ WalletAddresses: string[], ValidatorPublicKey: byte[]? }`. v1 impl: on first access, caches a call to `IWalletServiceClient.ListLocalWalletsAsync()` + reads `IValidatorWalletProvider.PublicKey` when running inside Register.Service. Note: Register.Service consumes the WalletServiceClient via existing consolidated clients; no direct Wallet-DB dependency.

### Foundational tests

- [ ] T017 [P] Unit tests for `RegisterSyncState` BSON converter in `tests/Sorcha.Register.Storage.MongoDB.Tests/Serialization/RegisterSyncStateConverterTests.cs` — all five legacy→enum mappings + unknown-string fallback.
- [ ] T018 [P] Unit tests for `ObservationStore` in `tests/Sorcha.Register.Core.Tests/Observations/ObservationStoreTests.cs` — per-peer upsert, eviction at 16 peers, thread-safety under concurrent writes.

**Checkpoint**: Foundation ready — user story implementation can now begin. Tasks in US2/US3/US4/US5 can run in parallel once this phase closes.

---

## Phase 3: User Story 2 — Node derives its role at startup and on changes (Priority: P1)

**Goal**: Compute `RegisterLocalRelationship` for every register at startup and whenever a control transaction is sealed. Expose via HTTP. Publish change events.

**Independent Test**: Boot a node holding three registers (owner, validator, subscriber). `GET /api/registers/{id}/local-relationship` returns the correct role set for each. Publish a governance transaction adding the node's validator key to the third register's roster; verify the role flips within one docket-seal cycle without restart.

### Tests for User Story 2 ⚠️

- [ ] T019 [P] [US2] Unit tests for `RegisterLocalRelationshipService.Derive` in `tests/Sorcha.Register.Core.Tests/LocalRelationship/RegisterLocalRelationshipServiceTests.cs`: owner-only, validator-only, owner+validator, legacy-pre-086 fallback, attestation-DID-does-not-resolve-locally.
- [ ] T020 [P] [US2] Unit tests for `LocalIdentityProvider` in `tests/Sorcha.Register.Core.Tests/LocalRelationship/LocalIdentityProviderTests.cs` — wallet-list caching, validator-key null when not running in Validator.Service.
- [ ] T021 [P] [US2] Integration tests for relationship endpoints in `tests/Sorcha.Register.Service.IntegrationTests/RelationshipEndpointTests.cs`: `/local-relationship` success + 404; `/my-validated-registers` header required + filtered output.

### Implementation for User Story 2

- [ ] T022 [P] [US2] Add `IRegisterLocalRelationshipService` interface in `src/Core/Sorcha.Register.Core/LocalRelationship/IRegisterLocalRelationshipService.cs` — methods `DeriveAsync(registerId, ct)` and `Invalidate(registerId)`.
- [ ] T023 [US2] Implement `RegisterLocalRelationshipService` in `src/Core/Sorcha.Register.Core/LocalRelationship/RegisterLocalRelationshipService.cs`: reads latest docket's control transaction via `IRegisterRepository`, matches attestations + roster against `ILocalIdentityProvider`, caches result keyed by registerId with `ControlRecordVersion` for stale-detection. (Depends on T005, T016, T022.)
- [ ] T024 [US2] Implement `RelationshipEndpoints.cs` in `src/Services/Sorcha.Register.Service/Endpoints/RelationshipEndpoints.cs` — maps `GET /api/registers/{registerId}/local-relationship`, `GET /api/registers/{registerId}/sync-state` (stub this one returning `Indeterminate` for now — US3 fills it in), and `GET /api/internal/my-validated-registers`. Adds `.RequireAuthorization("CanReadTransactions")` on the public endpoints. OpenAPI summary/description on each.
- [ ] T025 [US2] Wire endpoints + DI registrations into `src/Services/Sorcha.Register.Service/Program.cs`: register `IRegisterLocalRelationshipService`, `ILocalIdentityProvider`, call `app.MapRelationshipEndpoints()`.
- [ ] T026 [P] [US2] Implement `GetLocalRelationshipAsync` and `GetMyValidatedRegistersAsync` in `src/Common/Sorcha.ServiceClients.Http/Register/RegisterServiceClient.cs`. Include `await SetAuthHeaderAsync(ct)` before each request (see PR #357 for the pattern).
- [ ] T027 [US2] Add `RelationshipChangeNotifier` in `src/Services/Sorcha.Register.Service/Services/RelationshipChangeNotifier.cs` — publishes `RegisterRelationshipChangedEvent` to Redis channel `register:relationship-changed` on demand. Inject `IEventPublisher` (existing).
- [ ] T028 [US2] Hook docket-seal in `src/Services/Sorcha.Register.Service/Program.cs` `docketsGroup.MapPost("/")` handler (the WriteDocket endpoint, around line 1341): after `InsertDocketAsync`, if any transaction in the docket has `MetaData.TransactionType == Control`, call `localRelationshipService.Invalidate(registerId)` then `relationshipChangeNotifier.PublishIfChangedAsync(registerId)`. `PublishIfChangedAsync` compares old vs new derivation and only fires the event when the local role set actually changed.

**Checkpoint**: US2 fully functional — any node can be queried for its role on any register, and role changes due to governance transactions propagate via Redis.

---

## Phase 4: User Story 3 — Operator sees accurate sync state (Priority: P2)

**Goal**: Replace the free-text SyncState with a typed, derived-from-evidence lifecycle. Ingest observations from peer + validator services. Expose via sync-state endpoint.

**Independent Test**: Stop a subscriber for 60s while the owner produces three dockets. Restart and `GET /api/registers/{id}/sync-state` shows `Syncing` with a numeric gap; after pull completes and two confirming adverts arrive, transitions to `CaughtUp`. Block peer adverts for 60+s and it degrades to `Indeterminate`.

### Tests for User Story 3 ⚠️

- [ ] T029 [P] [US3] Table-driven unit tests for `RegisterSyncStateResolver.Resolve` in `tests/Sorcha.Register.Core.Tests/SyncState/RegisterSyncStateResolverTests.cs` — every row of the transition table in `data-model.md` §1 (Indeterminate → Syncing → CaughtUp → Syncing → Error → Syncing → CaughtUp).
- [ ] T030 [P] [US3] Integration tests for observation endpoints in `tests/Sorcha.Register.Service.IntegrationTests/ObservationEndpointTests.cs`: auth required, validation rules (negative height, timestamp skew > 5min), register-not-found 404, successful upsert changes `/sync-state` output on next read.
- [ ] T031 [P] [US3] Integration test for `/sync-state` endpoint in `tests/Sorcha.Register.Service.IntegrationTests/SyncStateEndpointTests.cs`: end-to-end height pump + observation → state transition.

### Implementation for User Story 3

- [ ] T032 [P] [US3] Add `IRegisterSyncStateResolver` interface + `RegisterSyncStateResolver` implementation in `src/Core/Sorcha.Register.Core/SyncState/RegisterSyncStateResolver.cs` — pure function of `(currentState, localHeight, observations, consecutiveFailureCount, now, stalenessWindow)`. Default staleness window = 60s (configurable via `IOptions<RegisterSyncStateOptions>`).
- [ ] T033 [P] [US3] Add `ObservationEndpoints.cs` in `src/Services/Sorcha.Register.Service/Endpoints/ObservationEndpoints.cs` — maps `POST /api/internal/registers/{id}/peer-height-observation` and `POST /api/internal/registers/{id}/validator-observation`, both `.RequireAuthorization("CanReportRegisterObservation")`. Validate inputs; reject validator observations from callers whose validator key isn't on the roster (call `IRegisterLocalRelationshipService.DeriveAsync` with the caller's key from `X-Validator-Public-Key`).
- [ ] T034 [US3] Update `RelationshipEndpoints.MapSyncStateEndpoint` (stubbed in T024) to actually call `IRegisterSyncStateResolver.Resolve` using `localHeight` from the register record + observations from `IObservationStore`.
- [ ] T035 [P] [US3] Implement `ReportPeerHeightAsync`, `ReportValidatorSealingAsync`, `GetSyncStateAsync` in `src/Common/Sorcha.ServiceClients.Http/Register/RegisterServiceClient.cs` with `SetAuthHeaderAsync` on each.
- [ ] T036 [US3] Wire Peer.Service to push peer-height observations: modify `src/Services/Sorcha.Peer.Service/Replication/RegisterAdvertisementService.cs` so that every time `UpdateAdvertisement` is called with a non-local advert, it also fires `IRegisterServiceClient.ReportPeerHeightAsync(registerId, new PeerHeightObservation(...))` — fire-and-forget with error-logged failure, do not block advert processing.
- [ ] T037 [US3] Wire Validator.Service to push sealing observations: modify `src/Services/Sorcha.Validator.Service/Services/ValidationEngineService.cs` (or `DocketBuildTriggerService`) to call `IRegisterServiceClient.ReportValidatorSealingAsync` on each successful docket seal and on mempool-depth change (throttle to 1 Hz — use a simple time-gated gate).
- [ ] T038 [US3] Update admin UI `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Index.razor` to consume the new `RegisterSyncState` enum (currently reads the free-text `SyncState` property). Display names: `Indeterminate` → "Unknown", `Syncing` → "Syncing (N behind)", `CaughtUp` → "Caught up", `Error` → "Error".
- [ ] T039 [P] [US3] Add OpenTelemetry counters in `src/Services/Sorcha.Register.Service/Telemetry/RegisterStateMetrics.cs`: `register_observations_ingested_total{source=peer|validator}`, `register_sync_state_current` (gauge per state).

**Checkpoint**: US3 fully functional — operator dashboard shows typed states with inputs exposed.

---

## Phase 5: User Story 4 — Validator enrols from roster, not from submission side-effects (Priority: P2)

**Goal**: Validator.Service stops side-effect-enrolling from `/validate`. Instead, at startup and on relationship-change events, it queries Register.Service for the registers its key validates for and seeds `IRegisterMonitoringRegistry`.

**Independent Test**: Two nodes share a register where only Node A is on the roster. Submit a transaction directly to Node B's `/validate`. Node B's mempool receives it, but `IRegisterMonitoringRegistry.GetAll()` on Node B does NOT contain the register, and no docket is produced on Node B. Node A seals normally.

### Tests for User Story 4 ⚠️

- [ ] T040 [P] [US4] Unit test in `tests/Sorcha.Validator.Service.Tests/RegisterMonitoringBootstrapTests.cs`: startup populates monitoring from `GetMyValidatedRegistersAsync` result; relationship-change event refreshes the list; safety-poll runs without duplicating entries.
- [ ] T041 [P] [US4] Integration test in `tests/Sorcha.Validator.Service.Tests/Endpoints/ValidationEndpointSideEffectRemovedTests.cs`: posting to `/validate` for a register not in the monitoring list leaves the monitoring list unchanged after the call returns.

### Implementation for User Story 4

- [ ] T042 [US4] Remove the side-effect line `monitoringRegistry.RegisterForMonitoring(request.RegisterId);` from `src/Services/Sorcha.Validator.Service/Endpoints/ValidationEndpoints.cs:155`. Leave the mempool-accept path unchanged (the tx still lands in the pool, it just won't be processed into dockets on nodes that aren't monitoring).
- [ ] T043 [US4] Add `RegisterMonitoringBootstrap` as `IHostedService` in `src/Services/Sorcha.Validator.Service/Services/RegisterMonitoringBootstrap.cs`. On `StartAsync`: read validator public key from `IValidatorWalletProvider`, call `IRegisterServiceClient.GetMyValidatedRegistersAsync(publicKey)`, seed `IRegisterMonitoringRegistry`. Also subscribe to Redis channel `register:relationship-changed` and on event refresh the relevant register's enrolment via a fresh `GetLocalRelationshipAsync` query (add or remove from registry based on `IsValidator`).
- [ ] T044 [US4] Add a safety-poll loop inside `RegisterMonitoringBootstrap` — every 5 minutes, re-run the full `GetMyValidatedRegistersAsync` and reconcile (adds missing, removes stale). Handles missed Redis events after pod restart.
- [ ] T045 [US4] Wire `RegisterMonitoringBootstrap` into `src/Services/Sorcha.Validator.Service/Program.cs` DI + hosted-service registration.
- [ ] T046 [US4] Ensure drain-on-removal semantics (FR-015): when a register is removed from monitoring, `ValidationEngineService.ProcessRegisterAsync` must not be re-invoked for it, but any task currently inside that method completes. Verify by adding a cancellation-check at the top of `ProcessRegisterAsync` after acquiring the `_activeRegisters` lock.

**Checkpoint**: US4 complete — two-node deployment cannot fork because subscribers never attempt to seal.

---

## Phase 6: User Story 5 — Blueprint action submission is owner-agnostic (Priority: P2)

**Goal**: `ActionExecutionService` issues the submission to both local validator and peer distribution concurrently. Peer.Service fans out to source peers for subscribed registers. Owner's peer receives via new gRPC RPC and hands to its local validator.

**Independent Test**: Write a blueprint action that calls the submission API with no conditional logic. On a node owning the register: transaction is processed by the local validator. Move ownership to a different node and rerun: transaction reaches the owner's validator via peer distribution with no code change in the blueprint handler.

### Tests for User Story 5 ⚠️

- [ ] T047 [P] [US5] Unit test in `tests/Sorcha.Blueprint.Service.Tests/ActionExecutionServiceFanOutTests.cs`: asserts both `_validatorClient.SubmitTransactionAsync` and `_peerClient.DistributeTransactionAsync` are called exactly once, concurrently (use `Moq` verification). Asserts no conditional ownership branching via a code-inspection test (scan for `SyncState` / `IsOwner` string literals in the submission path — none expected).
- [ ] T048 [P] [US5] Integration test in `tests/Sorcha.Peer.Service.Tests/GrpcServices/TransactionDistributionV2Tests.cs`: receiver accepts `SubmitTransaction`, forwards to a mocked `IValidatorServiceClient`, returns `accepted=true` with populated `receiver_role`.
- [ ] T049 [P] [US5] Integration test in `tests/Sorcha.Peer.Service.Tests/Replication/RegisterAdvertisementPushObservationTests.cs`: when an advert is ingested, `IRegisterServiceClient.ReportPeerHeightAsync` is called with the advertising peer's ID and the advertised height.

### Implementation for User Story 5

- [ ] T050 [P] [US5] Add `DistributeTransactionAsync` implementation in `src/Common/Sorcha.ServiceClients/Peer/PeerServiceClient.cs` — POSTs to local peer-service's new `/api/internal/peer/distribute` endpoint.
- [ ] T051 [US5] Add `POST /api/internal/peer/distribute` endpoint in `src/Services/Sorcha.Peer.Service/Endpoints/DistributeEndpoints.cs` (create file). Policy: `CanWriteDockets` (reuse — same semantic tier as submitting an action transaction). Hands the submission to `TransactionDistributionService.ForwardSubmissionAsync(registerId, submission, ct)` (new method).
- [ ] T052 [US5] Add gRPC service `TransactionDistributionV2` in `src/Services/Sorcha.Peer.Service/Protos/transaction_distribution_v2.proto` matching `contracts/peer-service-distribute-submission.proto`. Register in `src/Services/Sorcha.Peer.Service/Sorcha.Peer.Service.csproj` as a gRPC proto.
- [ ] T053 [US5] Implement `TransactionDistributionV2GrpcService` (server side) in `src/Services/Sorcha.Peer.Service/GrpcServices/TransactionDistributionV2GrpcService.cs`: receives `SubmitTransactionRequest`, deserializes `submission_json`, derives its own `RegisterLocalRelationship` for the register (to populate `ReceiverRoleSnapshot`), hands the submission to the local `IValidatorServiceClient.SubmitTransactionAsync`. Returns `accepted=true` on success.
- [ ] T054 [US5] Add `ForwardSubmissionAsync(registerId, submission, ct)` method on `TransactionDistributionService` in `src/Services/Sorcha.Peer.Service/Distribution/TransactionDistributionService.cs`. Implementation: look up `RegisterSubscription` by registerId; if locally owned (no subscription row), no-op returning immediately; else iterate `SourcePeerIds`, resolve each to an active connection via `PeerConnectionPool`, issue `TransactionDistributionV2.SubmitTransaction` RPC. Aggregate `accepted == true` across peers; if all fail, log error (not exception — fan-out is best-effort; the local validator submit may still have succeeded).
- [ ] T055 [US5] Modify `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:850-859` — after building the submission, issue `_validatorClient.SubmitTransactionAsync` and `_peerClient.DistributeTransactionAsync` concurrently via `Task.WhenAll`. Await both. Success = at least one returned accepted. Keep the existing `WaitForTransactionConfirmationAsync` polling; it already waits for the docket to appear locally (which happens via the owner's seal + replication).
- [ ] T056 [US5] Inject `IPeerServiceClient` into `ActionExecutionService` constructor (same pattern as `_validatorClient`, `_walletClient`, `_registerClient`). Update DI registration in Blueprint.Service `Program.cs` if needed (peer client should already be available via `AddServiceClients` — verify).
- [ ] T057 [P] [US5] Add observability: structured log on each fan-out in `TransactionDistributionService.ForwardSubmissionAsync` — `{ registerId, txId, targetPeers, acceptedCount }`. Counter `peer_submission_forwards_total{outcome=accepted|rejected|no-targets}`.

**Checkpoint**: US5 complete — submission always fans out; owner-agnostic.

---

## Phase 7: User Story 1 — Subscriber round-trip works end-to-end (Priority: P1) 🎯 MVP GOAL

**Goal**: Integration verification that US2 + US4 + US5 compose into the PingPongN1 round-trip behaviour. Primarily a verification phase — actual implementation is in the upstream stories.

**Depends on**: Phase 3 (US2), Phase 5 (US4), Phase 6 (US5). US3 is recommended but not strictly required for round-trip correctness.

**Independent Test**: Run `walkthroughs/PingPongN1/run.ps1 -Rounds 2` and assert `RESULT: PASS`, exit code 0, both rounds green on all four axes.

### Tests for User Story 1 ⚠️

- [ ] T058 [US1] Add an E2E assertion helper in `tests/Sorcha.IntegrationTests/PingPongN1AssertionTests.cs` (create project if missing — otherwise nest under `Sorcha.Peer.Service.Tests`) that drives the walkthrough headlessly and parses exit code + finding count. Marked `[Trait("Category", "E2E")]` so CI can opt in.

### Implementation / verification for User Story 1

- [ ] T059 [US1] Run `walkthroughs/PingPongN1/setup.ps1 -Force` then `walkthroughs/PingPongN1/run.ps1 -Rounds 2` against the local Docker stack (after deploying the build from this branch). Capture the console output and the relevant `docker compose logs peer-service` lines showing:
  - `Strategy 2 succeeded` (US2 happy path)
  - `RegisterMonitoringBootstrap` seeding monitoring (US4 happy path)
  - `ActionExecutionService` fan-out logs showing both validator + peer calls (US5 happy path)
  - `Docket N for register … finalized successfully` on both ends for both directions.
- [ ] T060 [US1] Update `walkthroughs/PingPongN1/README.md` — flip the "Known limitation: reverse push" section to "Resolved in Feature 108", link to this spec.
- [ ] T061 [US1] Update `memory/project_p2p_replication_backlog.md` — mark Finding B resolved alongside the roster-extraction entry; retain follow-ups (mutable JSON options, N+1 fallback, DateTimeKind).

**Checkpoint**: PingPongN1 reports `RESULT: PASS`. Feature-complete from the user-visible outcome angle.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, coverage verification, housekeeping.

- [ ] T062 [P] Update `CLAUDE.md` "Critical Patterns" section with a new bullet describing ownership-agnostic submission + the RegisterLocalRelationship derivation.
- [ ] T063 [P] Update `docs/reference/API-DOCUMENTATION.md` with the new endpoints from `contracts/register-service-relationship.yaml` and `contracts/register-service-observations.yaml`.
- [ ] T064 [P] Update service READMEs: `src/Services/Sorcha.Register.Service/README.md` (new endpoints section), `src/Services/Sorcha.Validator.Service/README.md` (bootstrap-driven monitoring), `src/Services/Sorcha.Peer.Service/README.md` (SubmitTransaction RPC).
- [ ] T065 Verify all new public types have XML doc comments — build with `/warnaserror:CS1591` scoped to the feature's new files.
- [ ] T066 Verify Scalar OpenAPI picks up the new endpoints: boot the stack locally, `curl http://localhost/openapi/v1.json | jq '.paths | keys'` includes the new paths.
- [ ] T067 Coverage check: `dotnet test --collect:"XPlat Code Coverage"` and verify ≥85% on `Sorcha.Register.Core`, the new endpoint files in Register.Service, `RegisterMonitoringBootstrap`, and `TransactionDistributionV2GrpcService`. Add missing tests for any gaps.
- [ ] T068 Run `.specify/MASTER-TASKS.md` update — mark Feature 108 in progress / complete as appropriate. Cross-reference PR #357 as the predecessor.
- [ ] T069 Run the full `quickstart.md` end-to-end against local stack + n1.sorcha.dev to confirm SC-001 through SC-008.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately.
- **Phase 2 (Foundational)**: Depends on Phase 1. Blocks every user story.
- **Phase 3 (US2)**: Depends on Phase 2. Independent of US3/US4/US5 thereafter.
- **Phase 4 (US3)**: Depends on Phase 2. Can run in parallel with Phase 3 (US2) and Phase 5 (US4) if staffed.
- **Phase 5 (US4)**: Depends on Phase 2 AND Phase 3 (T027: relationship-change Redis event must exist for bootstrap to subscribe). Can run in parallel with Phase 4 (US3).
- **Phase 6 (US5)**: Depends on Phase 2. Can run in parallel with Phase 3, 4, 5.
- **Phase 7 (US1)**: Depends on Phase 3, Phase 5, Phase 6. Phase 4 recommended.
- **Phase 8 (Polish)**: Depends on all desired user stories complete.

### User story dependencies

- **US2 (P1)** — root of dependency tree; nothing else depends on US1-5 to start, but US1/US4 depend on US2.
- **US3 (P2)** — independent of other user stories; optional for US1 round-trip but needed for operator observability.
- **US4 (P2)** — depends on US2 (consumes the `register:relationship-changed` event and `GetMyValidatedRegistersAsync` query).
- **US5 (P2)** — independent of US2/US3/US4 for implementation; consumes US4's side-effect-free validator to avoid forks but implementation doesn't block on US4.
- **US1 (P1)** — integration outcome; depends on US2, US4, US5.

### Within each user story

- Models → services → endpoints → clients.
- Tests may be written in parallel with implementation (not strict TDD-first per spec's implicit stance) but must pass before checkpoint.
- Commit after each task or each logical group (per `CLAUDE.md` workflow rules).

### Parallel opportunities

- **Phase 2**: T003–T009 are all `[P]` — pure model additions, no cross-file dependencies. T013 is `[P]`. T015, T017, T018 are `[P]`.
- **Phase 3 (US2)**: T019, T020, T021 tests all `[P]`. T022, T026 are `[P]`.
- **Phase 4 (US3)**: T029, T030, T031 tests all `[P]`. T032, T033, T035, T039 are `[P]`.
- **Phase 5 (US4)**: T040, T041 tests all `[P]`.
- **Phase 6 (US5)**: T047, T048, T049 tests all `[P]`. T050, T057 are `[P]`.
- **Cross-story**: Phases 3/4/5/6 can run concurrently once Phase 2 completes (subject to the US4-depends-on-US2 ordering note above).

---

## Parallel Example: Phase 2 model additions

```text
# All new model/DTO files in Phase 2 are in separate files → no conflicts:
Task T003: Add RegisterSyncState enum in src/Common/Sorcha.Register.Models/Enums/RegisterSyncState.cs
Task T004: Add RegisterRoleSet flags in src/Common/Sorcha.Register.Models/LocalRelationship/RegisterRoleSet.cs
Task T005: Add RegisterLocalRelationship record in src/Common/Sorcha.Register.Models/LocalRelationship/RegisterLocalRelationship.cs
Task T006: Add PeerHeightObservation in src/Common/Sorcha.Register.Models/Observations/PeerHeightObservation.cs
Task T007: Add ValidatorSealingObservation in src/Common/Sorcha.Register.Models/Observations/ValidatorSealingObservation.cs
Task T008: Add RegisterRelationshipChangedEvent in src/Common/Sorcha.Register.Models/Events/RegisterRelationshipChangedEvent.cs
Task T009: Add RegisterSyncStateView in src/Common/Sorcha.Register.Models/LocalRelationship/RegisterSyncStateView.cs
```

---

## Implementation Strategy

### MVP scope (minimum to flip PingPongN1 to PASS)

1. Phase 1 (Setup).
2. Phase 2 (Foundational) — in full, all 16 tasks.
3. Phase 3 (US2) — all 10 tasks.
4. Phase 5 (US4) — all 7 tasks.
5. Phase 6 (US5) — all 11 tasks.
6. Phase 7 (US1) — verify + update docs.
7. Phase 4 (US3) can be deferred but is highly recommended for operator visibility.
8. Phase 8 polish before ship.

### Incremental delivery

- After Phase 2: can't demo yet — shared plumbing only.
- After Phase 3 (US2): can demo `GET /local-relationship` against any register — useful for operator diagnostics.
- After Phase 5 (US4): can demo that a subscriber's validator doesn't try to seal a register it isn't authorised for — measurable via monitoring-registry inspection.
- After Phase 6 (US5) + Phase 3 (US2) + Phase 5 (US4): PingPongN1 should round-trip — Phase 7 is the verification run.
- After Phase 4 (US3): sync-state UI improvements visible to operators.

### Parallel team strategy

With three developers after Phase 2 completes:

- Developer A: Phase 3 (US2) — owns the core derivation + endpoints.
- Developer B: Phases 4 + 5 (US3, US4) — both touch Validator.Service and observation flows; natural pairing.
- Developer C: Phase 6 (US5) — Blueprint + Peer wiring.

All three converge on Phase 7 (US1) for the walkthrough verification, then Phase 8 polish.

---

## Notes

- `[P]` marker = different files, no dependencies on incomplete tasks.
- `[USn]` label maps each task to a user story for traceability. Foundational, Setup, and Polish phases intentionally have no `[US]` label.
- Each user story phase ends at a checkpoint suitable for PR boundary — consider splitting the implementation into 3-4 PRs (Foundational + US2; US4 + US5; US3; US1 verification + Polish) if preferred.
- Verify tests are green before claiming a task complete (see `superpowers:verification-before-completion`).
- Commit frequently; avoid conflating cross-story work in a single commit.
