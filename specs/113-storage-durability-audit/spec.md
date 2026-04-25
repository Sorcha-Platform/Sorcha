# Feature Specification: Storage Provider Audit and Validator Mempool Durability

**Feature Branch**: `113-storage-durability-audit`
**Created**: 2026-04-25
**Status**: Draft
**Input**: User description: "Storage provider audit and validator mempool durability — eliminate silent fallback to in-memory stores in containerised deployments, give the validator a mempool that survives process restart, close the documented TOCTOU gap in HAIP nonce consumption, and add cross-backend parity tests where in-memory and persistent paths can drift. Source design: docs/superpowers/specs/2026-04-25-storage-clients-audit-design.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Misconfigured Production deploy fails loudly (Priority: P1)

A Sorcha operator deploys a service to a Production or Staging environment with a missing or typo'd database/Redis connection string. Today the service silently boots on a transient in-memory store and loses every transaction on the next restart. After this feature, the service refuses to start and the deployment fails with a clear error naming the missing connection.

**Why this priority**: This is the highest-impact fix in the feature set. A silent in-memory fallback in a real deployment is an unbounded data-loss risk. Every other axis depends on this guard rail being in place.

**Independent Test**: Boot any audited service (Wallet, Register, Blueprint, Validator, HAIP) in a Production or Staging environment without the relevant connection string. Service must refuse to start and the startup log must list every audited interface that fell through to in-memory along with the reason. Repeating in Development must allow startup with a loud warning instead.

**Acceptance Scenarios**:

1. **Given** Wallet Service is configured for Production with no Wallet database connection string, **When** the service starts, **Then** startup fails with `InvalidOperationException` and the log identifies `IWalletRepository → InMemoryWalletRepository` as the offending registration.
2. **Given** the same misconfiguration in Development, **When** the service starts, **Then** startup succeeds, a `LogWarning` appears with the `[STORAGE-FALLBACK]` banner naming the interface and class, and the `storage-providers` health check reports degraded.
3. **Given** an operator sets `Storage:AllowInMemoryInProduction=true` for a Production CI smoke test, **When** the service starts, **Then** startup succeeds, a `LogCritical` records the override, and the bypass is visible in the registration log.

---

### User Story 2 — Validator mempool survives restart and replica failover (Priority: P1)

A validator process restarts (planned upgrade, OOM kill, node drain). Today every verified-but-not-yet-sealed transaction in its mempool is lost. After this feature, the mempool is held in a shared, durable store; the validator process resumes work from where it left off, and a standby replica with the same validator identity can take over without re-validating already-verified work.

**Why this priority**: Mempool loss is a finality-correctness risk under any non-trivial deployment. Protecting it unlocks HA validator deployment shapes that aren't possible today.

**Independent Test**: Enqueue a batch of verified transactions into the mempool. Restart the validator process. Without re-running validation, the validator must claim and seal those transactions into a docket. With two validator replicas sharing one identity, killing the active replica mid-claim must let the standby pick up the same transactions after the lease expires, with no transaction sealed twice.

**Acceptance Scenarios**:

1. **Given** N transactions have been verified and enqueued for register R, **When** the validator process is killed and restarted, **Then** the validator claims those same N transactions on the next docket-build cycle and seals them without re-running validation.
2. **Given** validator A has claimed a batch and is mid-seal, **When** validator A crashes before confirming, **Then** after the configured lease duration (default 60 seconds) elapses validator B (or A's restart) can claim the same batch and complete the seal, and the validator pipeline's duplicate-detection rejects any second-seal attempt.
3. **Given** a standby validator replica is reading the mempool, **When** the standby calls Peek, **Then** transactions remain available for the active validator to claim — the read is non-consuming.

---

### User Story 3 — HAIP nonces cannot be replayed under concurrent consume (Priority: P1)

A relying party in a credential issuance flow receives a c_nonce from the HAIP service and presents it. Today, two concurrent requests presenting the same nonce can both succeed because consume is `Get` followed by `Remove` — non-atomic. After this feature, exactly one of the concurrent consumes succeeds; the others see "already consumed".

**Why this priority**: Nonce replay protection is security-critical state. The TOCTOU gap is documented in the existing code as a known issue. Closing it is small in code volume but high in risk reduction.

**Independent Test**: Mint a single c_nonce. Spawn 100 concurrent consume requests against it. Exactly one must return success; the other 99 must return "missing/already consumed". The same test against `PreAuthCodeStore` and against the terminal-state transition in `PresentationRequestStore` must produce the same one-winner result.

**Acceptance Scenarios**:

1. **Given** a fresh c_nonce minted in `NonceStore`, **When** 100 tasks call `ConsumeAsync(nonce)` concurrently, **Then** exactly one task returns `true` and the rest return `false`.
2. **Given** a fresh pre-authorised code minted in `PreAuthCodeStore`, **When** two relying-party callbacks redeem it concurrently, **Then** exactly one callback receives the credential and the other receives the standard "already consumed" response with no internal state corruption.
3. **Given** a presentation request in `PresentationRequestStore` with multiple readers, **When** two callbacks attempt to transition it to a terminal state at the same time, **Then** exactly one transition is recorded and the other observes the terminal state and no-ops.

---

### User Story 4 — Developers have parity tests proving in-memory and persistent stores behave identically (Priority: P2)

A developer adds a new domain semantic to a service-specific store interface (e.g., the read-only mirror guard added in Feature 106). Today, only the implementation the developer touched picks up the new behaviour; the other implementation silently diverges until production catches the bug. After this feature, a single contract test suite is exercised against every implementation, and any divergence fails the build before merge.

**Why this priority**: This is preventative — it doesn't fix a current bug, it stops the next one in the same class. Lower priority than the active correctness fixes (US1–US3) but high enough to ship in the same cycle to prevent immediate regressions in the work being done.

**Independent Test**: Add a new method or semantic constraint to `IInstanceStore`, `IActionStore`, `IWalletRepository`, `IVerifiedTransactionQueue`, or `IAtomicDistributedCache`. Implement the change in only one of the two implementations. The contract test must fail for the unchanged implementation, naming the unimplemented behaviour.

**Acceptance Scenarios**:

1. **Given** an `IInstanceStore` contract test base exists, **When** a developer changes one of `InMemoryInstanceStore` or `EfCoreInstanceStore` without changing the other, **Then** the contract test for the unchanged implementation fails on the new behaviour.
2. **Given** a contract test for `IVerifiedTransactionQueue` exercises both `InMemoryVerifiedTransactionQueue` (against an in-process fake) and `RedisVerifiedTransactionQueue` (against MockRedis), **When** either implementation drifts on lease semantics, claim ordering, or expiry handling, **Then** the contract test for that implementation fails with a specific assertion.
3. **Given** the Redis claim Lua script is changed, **When** the dedicated Testcontainers smoke test runs against a real Redis server, **Then** any divergence between MockRedis and real Redis behaviour for that script is caught.

---

### User Story 5 — Operators can observe storage provider state and validator mempool depth (Priority: P3)

A Sorcha operator wants to know at a glance: which services in this deployment are running on persistent backends, which (if any) are on in-memory fallbacks, and how deep is the validator mempool right now. Today there are no metrics for any of this. After this feature, the Aspire dashboard exposes the storage-provider state and mempool depth for every service, and an OTLP-fed alerting backend can fire on "any audited interface in any service is on an in-memory backend in Staging or Production".

**Why this priority**: Operational visibility, not correctness — but cheap given the helper is already in the registration code path. Ship in the same cycle for the small marginal effort.

**Independent Test**: Boot a service with one persistent and one in-memory audited interface. Open the Aspire dashboard at `http://localhost:18888` and inspect the metric explorer (or query the OTLP endpoint directly). Confirm the gauges report the expected `1` for the in-memory interface and the persistent one's `backend` tag is correct. Boot the validator with traffic and confirm `sorcha_validator_mempool_size` updates as transactions are claimed and confirmed.

**Acceptance Scenarios**:

1. **Given** any service has booted, **When** an operator inspects the Aspire dashboard's metric explorer, **Then** `sorcha_storage_provider_info{service, interface, implementation, backend}` is observable for every registered audited interface.
2. **Given** an audited interface is on an in-memory backend, **When** an operator queries `sorcha_storage_fallback_active`, **Then** the gauge reports `1` for that interface and `0` for persistent ones — directly alertable.
3. **Given** the validator is sealing dockets, **When** transactions are claimed and confirmed, **Then** `sorcha_validator_mempool_size{register_id, state}` updates accordingly with `state ∈ {available, claimed}`, and `sorcha_validator_mempool_lease_expired_total` increments when a lease expires unconfirmed.

---

### Edge Cases

- **Redis becomes unavailable mid-flight**: Validator mempool operations must throw a recoverable error; the validator orchestrator must release any held leases (or let them auto-expire) and not corrupt local state. Background expiry sweep must retry silently.
- **Lua claim script fails partway**: Atomic by design — either the whole claim succeeds or no state changes. No partial state cleanup required.
- **Validator confirms a seal but the confirm operation fails on the wire**: The lease eventually expires, the transaction reappears in `available`, gets re-claimed, and the second seal is rejected by the existing pipeline duplicate-detection. The double-seal attempt is visible in metrics.
- **Storage registration log called twice for the same interface**: Throws at startup so the conflict is loud, not silent.
- **In-memory fallback in Development with two test instances sharing the same logical mempool**: In-memory leases are in-process only and do not coordinate. Acceptable — the in-memory path is for single-process use; warn-on-fallback makes the limitation explicit.
- **A relying party presents a c_nonce that has already been consumed**: Returns the same 400 response shape as today; metrics distinguish "first consume" from "second consume" via the outcome label so replay-attempt rates are observable.
- **An audited service boots in Development without any backend configured**: Starts successfully, all warnings logged, all metrics report fallback = 1, health check reports degraded. Developer sees the loud signal but is not blocked.

## Requirements *(mandatory)*

### Functional Requirements

**Storage registration and fail-fast (Axis i)**

- **FR-001**: System MUST log a warning for every in-memory store registration at service startup, naming the interface, the in-memory implementation class, and a free-text reason.
- **FR-002**: System MUST log an information record for every persistent store registration at service startup, naming the interface, the persistent implementation class, and the backend (e.g., postgres, mongo, redis).
- **FR-003**: System MUST refuse to start in Production or Staging environments when any audited interface has been registered with an in-memory implementation, unless the configuration flag `Storage:AllowInMemoryInProduction` is set to true.
- **FR-004**: When the bypass flag is set, system MUST emit a critical log entry recording the bypass and continue startup.
- **FR-005**: System MUST expose a `storage-providers` health check that reports degraded whenever any audited interface is on an in-memory backend.
- **FR-006**: The audited-interface set MUST include `IWalletRepository`, `IRegisterRepository`, `IInstanceStore`, `IActionStore`, `IVerifiedTransactionQueue`, and `IAtomicDistributedCache`. Cache-style stores (`IBlueprintStore`, `IPublishedBlueprintStore`, `BlueprintCache`, `ValidatorRegistry`, etc.) MUST receive the warning but MUST NOT trigger fail-fast.
- **FR-007**: System MUST throw at startup if the registration log is called twice for the same interface within a single service.

**Validator mempool durability (Axis ii)**

- **FR-008**: System MUST persist verified-but-not-yet-sealed transactions in a backing store such that a validator process restart does not lose them.
- **FR-009**: The mempool contract MUST expose claim-with-lease, confirm, and release operations in addition to enqueue, peek, and stats. The previous atomic-dequeue shape MUST be removed.
- **FR-010**: A claim operation MUST mark transactions as held by the caller for a configurable lease duration (default 60 seconds, configurable via `ValidatorMempool:LeaseDurationSeconds`). Held transactions MUST be invisible to subsequent claim operations until the lease expires or is released.
- **FR-011**: System MUST automatically return claimed transactions to the available pool when their lease expires, on the next claim operation.
- **FR-012**: A peek operation MUST return transactions that are available for claiming, without consuming or marking them.
- **FR-013**: The validator orchestrator MUST claim before building a docket, confirm on successful seal, and release on docket-build failure.
- **FR-014**: When a confirmed transaction is later re-claimed (after a confirm-network-failure followed by lease expiry), the validator pipeline MUST detect the duplicate seal attempt and reject it; the rejection MUST be observable in metrics.
- **FR-015**: A development fallback in-memory implementation of the mempool contract MUST exist. When selected, it MUST trigger the warn-on-fallback behaviour from FR-001 and the audited-interface fail-fast from FR-003.

**HAIP secret-state durability (Axis iii)**

- **FR-016**: System MUST provide an atomic distributed-cache primitive that exposes get-and-remove (single round trip), atomic set with TTL, and compare-and-set semantics.
- **FR-017**: Nonce consumption MUST be a single atomic operation with no time-of-check / time-of-use gap. Concurrent consumes of the same nonce MUST result in exactly one success.
- **FR-018**: Pre-authorisation code consumption MUST follow the same atomic single-consume contract as nonce consumption.
- **FR-019**: Presentation request terminal-state transitions MUST be compare-and-set so that two callbacks racing to set different terminal states resolve to a single deterministic winner.
- **FR-020**: A development fallback in-memory implementation of the atomic distributed cache MUST exist with semantically identical behaviour to the production implementation under single-process concurrency. It MUST trigger the warn-on-fallback behaviour from FR-001 and the audited-interface fail-fast from FR-003.

**Cross-backend contract tests (Axis iv)**

- **FR-021**: A contract-test base class MUST exist for each of `IInstanceStore`, `IActionStore`, `IWalletRepository`, `IVerifiedTransactionQueue`, and `IAtomicDistributedCache`, exercising the behavioural contract independently of any implementation.
- **FR-022**: Each contract base MUST have a subclass test fixture for every implementation used in the codebase. For repositories with database backends, the test fixture MUST use the project's existing Testcontainers pattern. For Redis-backed implementations, the fixture MUST use the project's existing MockRedis builder.
- **FR-023**: The Redis-backed mempool MUST have at least one additional Testcontainers-backed test that exercises the claim Lua script against a real Redis server.
- **FR-024**: Existing in-memory-only test files MUST be preserved as thin subclasses of the new contract base where possible, so prior bespoke coverage is not lost.

**Observability (Axis v — cross-cutting)**

All metrics are emitted through the OpenTelemetry `Meter` API (existing
`Sorcha.ServiceDefaults` `AddOpenTelemetry().WithMetrics()` pipeline) and
exported via OTLP to the Aspire dashboard. No Prometheus dependency is
introduced; Aspire is the single observability surface.

- **FR-025**: System MUST emit an OpenTelemetry observable gauge
  `sorcha_storage_provider_info` (meter `Sorcha.Storage`) with tags
  `service`, `interface`, `implementation`, and `backend`. One observation
  per registered audited interface, set once at startup.
- **FR-026**: System MUST emit an OpenTelemetry observable gauge
  `sorcha_storage_fallback_active` (meter `Sorcha.Storage`) with tags
  `service` and `interface`. Value 1 when an audited interface is on an
  in-memory backend; 0 when persistent.
- **FR-027**: System MUST emit an OpenTelemetry observable gauge
  `sorcha_validator_mempool_size` (meter `Sorcha.Validator.Mempool`) with
  tags `register_id` and `state` (where `state ∈ {available, claimed}`).
- **FR-028**: System MUST emit an OpenTelemetry counter
  `sorcha_validator_mempool_lease_expired_total` (meter
  `Sorcha.Validator.Mempool`) with tag `register_id`. Increments whenever
  the expiry sweep auto-releases a stale lease.
- **FR-029**: System MUST emit an OpenTelemetry counter
  `sorcha_haip_nonce_consume_total` (meter `Sorcha.Haip.Nonces`) with tags
  `store ∈ {nonce, preauth, presentation}` and
  `outcome ∈ {success, miss}`.

### Key Entities

- **Storage registration record**: One per audited interface per service. Holds the interface name, the implementation class name, the backend label (or "in-memory"), and a free-text reason for the choice. Built at startup, surveyed by the fail-fast helper, the health check, and the metrics gauge.
- **Verified transaction lease**: A claim-time-bounded hold on a verified transaction in the mempool. Carries the transaction id, the holder identity (implicit in caller context), the lease expiry timestamp, and the original verified-transaction payload. Returned to the available pool on release or lease expiry.
- **Atomic cache entry**: A keyed value with a TTL. Single-consume keys (nonce, pre-auth code) are removed atomically on the first consume. Multi-read keys (presentation request state) are read repeatedly but transitioned to terminal state via compare-and-set.
- **Audited interface set**: The fixed list of repository/store interfaces that trigger fail-fast in Production when on in-memory. Defined in one place; extending it requires an explicit code change so the audit boundary is reviewable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A misconfigured Production deployment (missing connection string for any audited interface) is detected at service-startup time, before any traffic is served. Operators can identify the missing configuration from the startup log alone, without needing to attach a debugger or grep source.
- **SC-002**: A validator process restart loses zero verified-but-not-yet-sealed transactions when running on the persistent backend. Verified by killing the validator after enqueue and before docket-build, restarting, and confirming all enqueued transactions are sealed in the next docket cycle.
- **SC-003**: Under 100 concurrent consumes of a single c_nonce or pre-auth code, exactly one consume succeeds. Verified by automated race tests that fail today's implementation and pass the new one.
- **SC-004**: A developer changing the contract of any audited store interface (e.g., adding a new query method or invariant) cannot merge a change that updates only one of the two implementations — the contract-test suite for the unchanged implementation fails the build.
- **SC-005**: Operators can write a single alert rule against the OTLP-fed metrics backend ("any service in Staging or Production has any audited interface on in-memory") that catches misconfigured deployments using only the OpenTelemetry metrics exposed by this feature.
- **SC-006**: The validator's HA-replica deployment shape (two replicas sharing one validator identity, one active and one standby) becomes possible without code changes outside this feature; demonstrated by a documented test that kills the active replica mid-claim and confirms the standby completes the seal after the lease expires.
- **SC-007**: Each of the eight rollout PRs is independently mergeable and revertable. Verified by the PR sequence shipping in order with no PR depending on a later PR's behaviour for its own tests to pass.
- **SC-008**: No deployed service that previously ran on a persistent backend regresses to in-memory after this feature ships. Verified by the `sorcha_storage_fallback_active` gauge reporting 0 for all audited interfaces in Staging and Production after each PR's deploy.

## Assumptions

- All deployments (Development, Staging, Production) already have access to PostgreSQL, MongoDB, and Redis. No new infrastructure procurement is required.
- The validator's deployment topology in scope for this feature is single-active per identity, with optional standby replicas using the same identity. Multi-active sealing for the same register is explicitly out of scope and remains gated by the existing roster/governance design.
- "Production" and "Staging" map to `IHostEnvironment.IsProduction()` and `IsStaging()` respectively, as configured by `ASPNETCORE_ENVIRONMENT`. Other custom environment names are treated as Development for fail-fast purposes.
- `Sorcha.Testing.MockRedisBuilder` is a faithful enough Redis stand-in for non-Lua operations (sorted sets, hashes, expiry). Lua-script behaviour is the documented exception, covered by a single Testcontainers-backed test.
- The presentation-request store is suspected to already use compare-and-set on terminal-state writes; if the audit confirms this, the change for that store is documentation only.
- Cache-style stores (`InMemoryBlueprintStore`, `InMemoryPublishedBlueprintStore`, `BlueprintCache`, `ValidatorRegistry`, in-process `RoutingTable`, etc.) reload from authoritative sources on cold start and are not on the audited list. Their loss is acceptable; their warn-only treatment is intentional.

## Dependencies

- Existing `IDistributedCache` registration via Redis is in place and resolvable from DI in services that need the new atomic cache.
- Existing `IConnectionMultiplexer` for `StackExchange.Redis.IDatabase` is registered (used today by the Redis Streams subscriber); the atomic cache implementation reuses this.
- The Validator Service currently has no Redis connection string by default in some configurations; deployment manifests must be updated to provide one before PR 8 is released to environments where validator durability is required.
- Existing Sorcha test infrastructure: `Sorcha.Testing.MockRedisBuilder`, the Testcontainers Postgres pattern from `Sorcha.Auth.IntegrationTests`, and the `RegisterRepositoryContractTests` reference pattern.

## Out of Scope

- HTTP client consolidation (Sorcha.Agent and Sorcha.UI raw `new HttpClient` usage migrating onto `Sorcha.ServiceClients.Http` / `IHttpClientFactory`). Worthwhile but cross-cutting and largely cosmetic next to the durability work in this feature.
- Refactoring service-specific in-memory stores onto the generic `Sorcha.Storage.InMemory.InMemoryRepository<T>`. The service stores carry domain-rich semantics that the generic abstraction cannot express without losing type safety.
- Multi-active validator sealing for the same register. Distinct consensus/governance concern, not a mempool concern.
- Migrating `InMemoryBlueprintStore` and `InMemoryPublishedBlueprintStore` to a persistent backing. They are caches that reload from the persistent transaction log; losing them is a cold start, not data loss.
- Wallet HD-derivation, cryptography, or DID-resolution behaviour changes.
