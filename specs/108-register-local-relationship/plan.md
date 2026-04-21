# Implementation Plan: Register State Aggregation & Local Relationship

**Branch**: `108-register-local-relationship` | **Date**: 2026-04-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/108-register-local-relationship/spec.md`

## Summary

Make `Register.Service` the single authoritative source of per-register state on a node. Introduce a `RegisterLocalRelationship` derived view over the latest control record (identifies this node's Owner / Admin / Auditor / Designer / Validator / Subscriber roles from its local wallet + validator key). Replace the free-text `Register.SyncState` with a typed enum (`Indeterminate` / `Syncing` / `Caught-up` / `Error`) derived from local docket height, a network-height high-water-mark fed in by `Peer.Service`, and (when applicable) a mempool/seal-progress signal fed in by `Validator.Service`. Rewire `Validator.Service` so it enrols its monitoring from the roster pulled from `Register.Service` instead of side-effect enrolment via `/validate`. Wire `Blueprint.Service.ActionExecutionService` to fan submissions out to both the local validator (which seals iff enrolled) and `Peer.Service.TransactionDistributionService` (which gossips to targets that include the roster validator when local is a subscriber). Net result: a NAT'd subscriber's submission reaches the owner's validator over the existing outbound gRPC channels, and the PingPongN1 walkthrough flips PARTIAL → PASS.

## Technical Context

**Language/Version**: C# 13 on .NET 10 (matches platform constitution).
**Primary Dependencies**: ASP.NET Core Minimal APIs + Scalar (internal/public endpoints), Entity Framework Core 10 (Peer.Service Postgres), MongoDB.Driver (Register.Service Mongo), Grpc.Net 2.71 (existing peer P2P), `Sorcha.ServiceClients.Http` (consolidated HTTP clients), Serilog + OpenTelemetry, FluentValidation.
**Storage**:
 - `Register.Service` — MongoDB (existing `Register` collection gets a new typed `SyncState` + a small bounded in-memory observation store for peer heights and validator seal progress; observations are ephemeral signals, not persisted).
 - `Peer.Service` — Postgres via EF Core (no schema change — observation push is outbound only).
 - `Validator.Service` — in-memory `IRegisterMonitoringRegistry` (remains in-memory; populated from a `Register.Service` query at startup and on relationship-change events).
**Testing**: xUnit v3.2.2, FluentAssertions 8.8.0, Moq 4.20.72; integration smoke via `walkthroughs/PingPongN1/run.ps1` against the local Docker stack.
**Target Platform**: Linux container (Aspire-orchestrated) for dev; `n1.sorcha.dev` for cross-machine verification. Multi-node assumption is load-bearing — every code path must behave correctly when owner and subscriber are on different hosts (per `feedback_multi_node_assumption.md`).
**Project Type**: Microservices with a shared Common/Core library tree (.NET Aspire orchestrated). No new services; all work lands inside existing service and library projects.
**Performance Goals**: No explicit latency targets for this feature beyond the spec's SC-001 (end-to-end round-trip inside the PingPongN1 120s step window). Observation ingestion is fire-and-forget; relationship derivation is cached and only recomputed on control-tx seal, so per-submission cost is O(1) cache read.
**Constraints**:
 - Multi-node: owner/subscriber/validator may run on different hosts (no single-node assumptions).
 - Existing JWT service-to-service auth policies (`CanReadTransactions`, `CanWriteDockets`) must not be bypassed. New internal endpoints take an internal-only policy or reuse existing where the semantics match.
 - No tight coupling upward: `Common` / `Core` may not take service references; service-to-service traffic goes through `Sorcha.ServiceClients.Http` clients.
 - Relationship derivation must not make a synchronous remote call on the submission hot path — cache-first, derive lazily, invalidate on docket seal.
**Scale/Scope**: Sorcha's existing register cardinality (tens of registers per node in dev; design headroom for thousands). Peer height observations per register: small ring buffer (say last 16 adverts) — bounded memory.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|---|---|---|
| I. Microservices-First | PASS | No new services. Register becomes authoritative for its own state via push-in observations from peer & validator — services stay independently deployable. No upward dependency: Common/Models defines the shared contracts; Register.Service depends on Common only. |
| II. Security First | PASS | All new endpoints require service-to-service JWT (reuse `CanReadTransactions` / `CanWriteDockets` where semantics match; introduce one narrow internal policy `CanReportRegisterObservation` for the two push endpoints). No secrets crossing the new wire — observations are public metrics (heights, counts). Control-record-derived relationship is read-only. |
| III. API Documentation | PASS | New endpoints use .NET 10 built-in OpenAPI with `.WithName/.WithSummary/.WithDescription`; Scalar UI picks them up automatically. XML docs on all new public members. |
| IV. Testing | PASS | xUnit unit tests for relationship-derivation logic (pure function over control record), observation-store pruning, sync-state transition table. Integration tests for each new endpoint. E2E verification via PingPongN1 walkthrough. Target ≥85% coverage on new code. |
| V. Code Quality | PASS | No compiler warnings in Release. Async/await throughout. Nullable reference types enabled (already project default). DI-registered services. |
| VI. Blueprint Creation | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | New vocabulary: `RegisterLocalRelationship`, `RegisterSyncState`, `PeerHeightObservation`, `ValidatorSealingObservation`. These extend existing ubiquitous language (Register, Docket, Validator, Subscriber) without conflicting. |
| VIII. Observability | PASS | Structured logs at each state transition (`Syncing → Caught-up`, `Caught-up → Indeterminate`, relationship cache invalidation, validator enrolment add/drop). OpenTelemetry counters: observations-ingested, relationship-cache-hits/misses, sync-state-current (gauge). Health checks on Register.Service unchanged. |

**Result**: PASS. No violations to justify — Complexity Tracking section below is empty.

## Project Structure

### Documentation (this feature)

```text
specs/108-register-local-relationship/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output — internal HTTP contracts for observation push + relationship query
│   ├── register-service-observations.yaml
│   ├── register-service-relationship.yaml
│   └── peer-service-distribute-submission.yaml
├── checklists/
│   └── requirements.md  # Spec quality checklist (already created)
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created by /speckit.plan)
```

### Source Code (repository root)

This feature lands inside the existing microservice tree — no new projects.

```text
src/
├── Common/
│   ├── Sorcha.Register.Models/
│   │   ├── Enums/
│   │   │   └── RegisterSyncState.cs                          # NEW: typed enum (Indeterminate/Syncing/Caught-up/Error)
│   │   ├── LocalRelationship/
│   │   │   ├── RegisterLocalRelationship.cs                  # NEW: derived role record (IsOwner/IsValidator/...)
│   │   │   └── RegisterRoleSet.cs                            # NEW: bitflag or DTO helper
│   │   ├── Observations/
│   │   │   ├── PeerHeightObservation.cs                      # NEW: push DTO (peer → register)
│   │   │   └── ValidatorSealingObservation.cs                # NEW: push DTO (validator → register)
│   │   └── Register.cs                                       # MODIFIED: SyncState string → RegisterSyncState? (enum)
│   └── Sorcha.ServiceClients.Http/
│       ├── Register/
│       │   ├── IRegisterServiceClient.cs                     # MODIFIED: +ReportPeerHeightAsync, +ReportValidatorSealingAsync, +GetLocalRelationshipAsync, +GetMyValidatedRegistersAsync, +GetSyncStateAsync
│       │   └── RegisterServiceClient.cs                      # MODIFIED: implements the above
│       └── Peer/
│           ├── IPeerServiceClient.cs                         # MODIFIED: +DistributeTransactionAsync (submission fan-out)
│           └── PeerServiceClient.cs                          # MODIFIED: implements the above
│
├── Core/
│   └── Sorcha.Register.Core/
│       ├── LocalRelationship/
│       │   ├── IRegisterLocalRelationshipService.cs          # NEW: derives relationship from control record + local identity
│       │   ├── RegisterLocalRelationshipService.cs           # NEW: implementation with per-register in-memory cache
│       │   └── LocalIdentityProvider.cs                      # NEW: resolves "my wallet addresses" + "my validator public key"
│       ├── SyncState/
│       │   ├── IRegisterSyncStateResolver.cs                 # NEW: composes local height + network HWM + advert freshness
│       │   └── RegisterSyncStateResolver.cs                  # NEW: pure function + policy knobs (staleness window, quorum)
│       └── Observations/
│           ├── IObservationStore.cs                          # NEW: bounded in-memory store for peer heights + validator progress
│           └── ObservationStore.cs                           # NEW: ring-buffer per register, thread-safe
│
├── Services/
│   ├── Sorcha.Register.Service/
│   │   ├── Endpoints/
│   │   │   ├── ObservationEndpoints.cs                       # NEW: POST /api/internal/registers/{id}/peer-height-observation, POST /api/internal/registers/{id}/validator-observation
│   │   │   └── RelationshipEndpoints.cs                      # NEW: GET /api/registers/{id}/local-relationship, GET /api/registers/{id}/sync-state, GET /api/internal/my-validated-registers
│   │   ├── Services/
│   │   │   └── RelationshipChangeNotifier.cs                 # NEW: on control-tx seal, invalidates cache and publishes relationship-change event (Redis — same channel pattern as RegisterEventBridge)
│   │   └── Program.cs                                        # MODIFIED: wire new DI registrations, hook docket-seal event for cache invalidation
│   ├── Sorcha.Peer.Service/
│   │   └── Replication/
│   │       ├── RegisterAdvertisementService.cs               # MODIFIED: on advert ingest, POST peer-height-observation to Register.Service
│   │       └── Distribution/
│   │           └── TransactionDistributionService.cs         # MODIFIED: expose REST endpoint + ensure existing gossip targets include owner when local is subscriber
│   ├── Sorcha.Validator.Service/
│   │   ├── Services/
│   │   │   ├── RegisterMonitoringBootstrap.cs                # NEW: on startup + on relationship-change event, queries /api/internal/my-validated-registers and seeds IRegisterMonitoringRegistry
│   │   │   └── ValidationEngineService.cs                    # MODIFIED: on docket seal, push ValidatorSealingObservation
│   │   └── Endpoints/
│   │       └── ValidationEndpoints.cs                        # MODIFIED: remove side-effect monitoringRegistry.RegisterForMonitoring(); enrolment now comes solely from bootstrap
│   └── Sorcha.Blueprint.Service/
│       └── Services/Implementation/
│           └── ActionExecutionService.cs                     # MODIFIED: after signing, parallel call _peerClient.DistributeTransactionAsync(submission) alongside _validatorClient.SubmitTransactionAsync(submission); no ownership branching
│
└── tests/
    ├── Sorcha.Register.Core.Tests/
    │   ├── LocalRelationship/
    │   │   ├── RegisterLocalRelationshipServiceTests.cs      # NEW: pure-function derivation over fixture control records
    │   │   └── LocalIdentityProviderTests.cs                 # NEW
    │   ├── SyncState/
    │   │   └── RegisterSyncStateResolverTests.cs             # NEW: transition table tests (table-driven)
    │   └── Observations/
    │       └── ObservationStoreTests.cs                      # NEW: ring-buffer pruning, thread safety
    ├── Sorcha.Register.Service.IntegrationTests/
    │   ├── ObservationEndpointTests.cs                       # NEW: auth, validation, effect on sync state
    │   └── RelationshipEndpointTests.cs                      # NEW
    ├── Sorcha.Validator.Service.Tests/
    │   └── RegisterMonitoringBootstrapTests.cs               # NEW: startup enrolment, no side-effect path
    ├── Sorcha.Peer.Service.Tests/
    │   └── Replication/RegisterAdvertisementPushObservationTests.cs  # NEW
    └── Sorcha.Blueprint.Service.Tests/
        └── ActionExecutionServiceFanOutTests.cs              # NEW: asserts both clients called, no ownership branching
```

**Structure Decision**: The feature is a horizontal concern cutting across three services plus the Blueprint-service submission path. All new logic lands in existing projects. New pure-derivation code lives in `Sorcha.Register.Core` (consumable from tests without service startup). New wire contracts surface as endpoints on `Sorcha.Register.Service` and `Sorcha.Peer.Service`, with client methods added to the consolidated `Sorcha.ServiceClients.Http`. No new project is introduced.

## Complexity Tracking

> No gate violations — section intentionally empty.
