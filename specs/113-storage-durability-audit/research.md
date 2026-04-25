# Phase 0 Research: Storage Provider Audit and Validator Mempool Durability

**Feature**: 113-storage-durability-audit
**Date**: 2026-04-25
**Status**: Complete (zero open `NEEDS CLARIFICATION` items)

This document records the design decisions made during planning, the rationale
for each, and the alternatives considered. Decisions are referenced by ID
(`R-01`, `R-02`, …) elsewhere in the planning artefacts.

---

## R-01 — Validator mempool backing store: Redis Sorted Sets

**Decision**: Replace `VerifiedTransactionQueue`'s per-process
`ConcurrentDictionary<string, RegisterQueue>` with a Redis Sorted Set per
register, plus a parallel hash for transaction payloads, claimed-set, and
expiry index.

**Rationale**:

- The existing data structure is "priority-ordered queue with FIFO tiebreaker
  + by-id lookup + TTL". Redis Sorted Sets map onto this 1:1 (`ZADD score
  member`, `ZRANGE`, `ZREM`).
- Sorcha already runs Redis in every deployment (caches, streams, presentation
  lifecycle). No new infrastructure procurement.
- Single Lua script (`claim-and-release.lua`) makes the
  walk-expired-leases-then-claim-batch operation atomic in one round trip,
  unblocking the HA-replica deployment shape required by FR-008–FR-014.
- Restart durability is automatic if Redis is configured with AOF or RDB
  persistence (n1 already does both per `docker-compose.n1.yml`).

**Alternatives considered**:

- **Redis Streams + consumer groups.** Closer to "queue" semantics, but
  consumer-group acknowledgements operate on individual messages, not on
  priority-ordered batches. Reordering pending entries on
  release-after-failure is awkward. Strictly more moving parts than this
  feature needs. Rejected.
- **Postgres outbox table.** Most durable and queryable. Adds a database
  dependency to the validator (currently has none in some configurations) and
  introduces transactional coupling between validator and a write-heavy
  Postgres instance. Rejected on dependency-footprint grounds.
- **Keep in-memory + write-ahead log.** A WAL would give restart durability
  without Redis but does nothing for HA replicas, and reinventing
  multi-replica coordination is strictly worse than reusing Redis primitives.
  Rejected.

**Implications**:

- The validator gains a mandatory Redis dependency in Production (was already
  present in n1 deployments via the cache stack, but the connection string
  needs to be wired into `Sorcha.Validator.Service` config). Documented in
  spec Dependencies; deployment manifests update in PR 8.
- The `IVerifiedTransactionQueue` contract changes from atomic-`Dequeue` to
  `Claim`/`Confirm`/`Release` (R-03).

---

## R-02 — Lease default duration and configurability

**Decision**: 60 seconds default, configurable via
`ValidatorMempool:LeaseDurationSeconds`.

**Rationale**:

- Docket-build worst case in current Sorcha is well under 60s; the default
  gives margin for slow validation paths and Redis blip recovery without
  pinning transactions for unreasonable spans.
- A configurable knob lets large or unusually slow deployments tune up; small
  test deployments tune down for faster lease-expiry tests.
- Aligns with the user's explicit choice during brainstorming.

**Alternatives considered**:

- **30 seconds.** Cuts the worst-case retry latency in half but risks
  premature lease expiry under genuine load. Rejected as default; remains
  available via the config knob.
- **5 minutes.** Generous margin but means a crashed validator's transactions
  are stuck for 5 minutes before another replica can pick them up. Rejected
  as poor failover UX.
- **Per-call lease parameter, no default.** Forces every call site to pick a
  number; we have one call site so this is overhead with no benefit.
  Rejected.

---

## R-03 — Mempool contract shape: lease pattern over atomic dequeue

**Decision**: `IVerifiedTransactionQueue` exposes `Enqueue`,
`ClaimAsync(maxCount, leaseDuration)`, `ConfirmAsync(transactionIds)`,
`ReleaseAsync(transactionIds)`, `Peek`, `Contains`, `GetCount`,
`GetTotalCount`, `Remove`, `Clear`, `ClearAll`, `CleanupExpired`, `GetStats`,
`GetRegisterStats`. The atomic-`Dequeue` and `ReturnToQueue` operations from
the previous contract are removed.

**Rationale**:

- Lease pattern lets a standby replica `Peek` without consuming, and lets the
  active replica `Claim` with a time-bounded hold. Same API works for the
  single-validator deployment (lease just expires unobserved).
- `Confirm`/`Release` separation lets the validator orchestrator distinguish
  "seal succeeded, drop the transactions" from "seal failed, return them" —
  cleaner than the existing `Dequeue` + `ReturnToQueue` round-trip on the
  failure path.
- Crash-recovery is automatic: if a validator dies between `Claim` and
  `Confirm`, the lease auto-releases on the next `ClaimAsync` by any replica.

**Alternatives considered**:

- **Keep `Dequeue` + add a parallel `PeekClaim`.** Two near-identical APIs
  inviting confusion at call sites. Rejected.
- **Outbox pattern with explicit poison-message handling.** Overengineered
  for the validator's single-call-site consumer. Rejected.

**Implications**:

- Single in-process caller (`DocketBuildTriggerService` / `ValidatorOrchestrator`)
  needs migration. Existing `Dequeue` callers in tests need rewriting against
  the new shape — covered by the PR 7 test rewrite.

---

## R-04 — Atomic cache placement: new common project vs fold into ServiceDefaults

**Decision**: New common project `Sorcha.AtomicCache` under `src/Common/`,
parallel to `Sorcha.Storage.Abstractions` and `Sorcha.ServiceClients`.

**Rationale**:

- Atomic cache is a primitive abstraction that any service might need
  (HAIP today, future credential-status services, future rate-limiters).
  Folding into `ServiceDefaults` means everything that consumes
  `ServiceDefaults` gets a transitive dependency on
  `StackExchange.Redis.IDatabase`, which is over-broad — the Tenant Service
  doesn't need it, the Wallet Service doesn't need it.
- Mirrors the existing `Sorcha.Storage.*` and `Sorcha.ServiceClients.*`
  pattern for cross-cutting infrastructure.
- The `IStorageRegistrationLog` from Axis (i) does live in `ServiceDefaults`
  because it's a startup-time concern that every service needs and that has
  no transitive Redis dependency.

**Alternatives considered**:

- **Inside `Sorcha.ServiceDefaults`.** Smaller diff, faster to set up. But
  spreads `StackExchange.Redis` dependency more widely than necessary.
  Rejected.
- **Inside `Sorcha.Haip.Service` only, exposed via internal interface.**
  Only HAIP needs it today; other features can import when they need it.
  But the abstraction is generic and worth lifting; deferring leaks the same
  TOCTOU pattern into the next consumer. Rejected.

**Implications**:

- One new csproj + one new test csproj. Minor solution-file churn.
- HAIP service references `Sorcha.AtomicCache` instead of consuming
  `IDistributedCache` directly for atomic operations.

---

## R-05 — Audited interface set: explicit allow-list

**Decision**: The fail-fast set is hardcoded as a `static readonly HashSet<string>`
inside `StorageRegistrationLog`:
`IWalletRepository`, `IRegisterRepository`, `IInstanceStore`, `IActionStore`,
`IVerifiedTransactionQueue`, `IAtomicDistributedCache`. Cache-style stores
(`IBlueprintStore`, `IPublishedBlueprintStore`, `BlueprintCache`,
`ValidatorRegistry`, etc.) receive the warning but are not on the list.

**Rationale**:

- An explicit allow-list keeps the audit boundary reviewable. Adding to it
  is a code change that goes through code review, with a clear blast radius.
- Auto-detecting "is this an in-memory store" by reflection or naming
  convention is fragile and would either over-include caches (false alarms)
  or miss new stores (false negatives).
- Cache-style stores reload from authoritative sources on cold start, so
  losing them is a cold start, not data loss. Treating them as audited would
  refuse to start over a recoverable condition — the wrong default.

**Alternatives considered**:

- **Attribute-based**: `[AuditedStorage]` on the interface. More magic.
  Future Claude / future developer doesn't know the attribute exists until
  they hit the failure. Rejected.
- **Configuration-driven**: list interfaces in appsettings. Encourages
  per-environment list drift. Rejected.

**Implications**:

- Adding a new audited store interface in a future feature is a one-line
  addition to the allow-list plus a corresponding test.

---

## R-06 — In-memory atomic cache semantics

**Decision**: `InMemoryAtomicDistributedCache` uses a `ConcurrentDictionary`
with `TryRemove`-with-out-value (atomic at dictionary level) for `GetAndRemoveAsync`.
A scoped `_lock` guards the read+write sequence inside `TryUpdateIfMatchAsync`
to make CAS atomic within a single process.

**Rationale**:

- `ConcurrentDictionary.TryRemove(key, out value)` is documented as atomic
  and returns the removed value — exact match for `GETDEL` semantics within
  a process.
- A simple `lock` for CAS is correct, well-understood, and not on a hot path.
- Behaviour matches the Redis implementation under single-process
  concurrency, which is all the in-memory implementation is for. Multi-process
  concurrency is the Redis implementation's job; the warn-on-fallback log
  makes the limitation explicit.

**Alternatives considered**:

- **`SemaphoreSlim` per key.** Fine-grained but unnecessary for the
  test/dev use case. Rejected.
- **`ImmutableDictionary` with `Interlocked.CompareExchange`.** Lock-free
  but rebuilds the dictionary on every write — worse for the in-memory
  fallback's intended use as a fast test fixture. Rejected.

---

## R-07 — Health check granularity

**Decision**: One health check named `storage-providers` that reports
`Healthy` if all audited interfaces are persistent, `Degraded` if any
audited interface is in-memory, with the in-memory interfaces enumerated in
the health-check `Description`.

**Rationale**:

- One check per interface would explode the `/health` response and create
  noisy alerts for the same underlying configuration issue.
- `Degraded` (rather than `Unhealthy`) matches the existing Sorcha pattern
  where `Unhealthy` = "service cannot serve traffic" and `Degraded` = "service
  is serving but not at full capability". A Production deployment with an
  in-memory fallback should never reach this state because fail-fast prevents
  startup; in Development the service is genuinely degraded but functional.

**Alternatives considered**:

- **Per-interface health checks.** Rejected for noise.
- **Degraded vs Unhealthy split based on environment.** Adds environment
  awareness to the health check, which is otherwise environment-agnostic.
  Rejected — fail-fast is the environment-aware behaviour, the health check
  stays simple.

---

## R-08 — `Storage:AllowInMemoryInProduction` bypass behaviour

**Decision**: A single boolean configuration flag at the service root
(`Storage:AllowInMemoryInProduction`). When true, fail-fast is skipped and
a single `LogCritical` records the bypass with all audited interfaces
currently on in-memory listed. The flag is a per-service override.

**Rationale**:

- Some legitimate use cases exist: CI smoke tests that boot in Production
  mode against an ephemeral environment, debugging scenarios where an
  operator wants to introspect a service without spinning up Postgres/Redis.
- `LogCritical` ensures the override is loud — it's the highest log level
  Sorcha uses, and it'll surface in any reasonable monitoring stack.
- Per-service rather than per-interface keeps the flag simple. If someone
  wants finer granularity later, they can add `Storage:AllowInMemoryInProduction:Interfaces`
  as a future change.

**Alternatives considered**:

- **No bypass at all.** Forces the CI smoke-test workflow to use
  `ASPNETCORE_ENVIRONMENT=Development`, which changes other behaviour
  (verbose error pages, etc.) that the smoke test specifically wants to
  validate. Rejected.
- **Per-interface bypass list.** Premature; one boolean covers the
  observed cases. Rejected.

---

## R-09 — Contract test fixture strategy

**Decision**:

- For database-backed implementations (`EfCoreInstanceStore`,
  `EfCoreActionStore`, `EfCoreWalletRepository`,
  `MongoRegisterRepository`): use Testcontainers with the existing pattern
  from `Sorcha.Auth.IntegrationTests` and `Sorcha.Peer.Service.Integration.Tests`.
- For Redis-backed implementations (`RedisVerifiedTransactionQueue`,
  `RedisAtomicDistributedCache`): use `Sorcha.Testing.MockRedisBuilder` for
  the bulk of the contract suite. Add **one** Testcontainers-backed test
  per Redis-backed implementation that exercises any Lua scripting against
  a real Redis server.

**Rationale**:

- MockRedis is fast (in-memory simulation, no container spin-up) and faithful
  for the data-structure operations used. Most contract tests run in <100ms
  with MockRedis vs ~5s spin-up + ~50ms per test against Testcontainers.
- Lua-script behaviour is the documented exception — MockRedis's Lua
  interpreter is the most likely place for behavioural divergence from real
  Redis. One real-Redis smoke test per Lua script keeps total test time
  bounded while covering the highest-risk surface.
- This matches the user's explicit call during brainstorming.

**Alternatives considered**:

- **Pure Testcontainers everywhere.** Slower, but most honest. Rejected
  on test-loop speed grounds; the one Lua smoke catches the divergence
  cases.
- **Pure MockRedis.** Fast, but gambles on Lua-script fidelity. Rejected
  on correctness grounds.

---

## R-10 — Ordering: Spec Out-of-Scope items

The following are confirmed out of scope and will not be researched further
in this feature:

- **HTTP client consolidation** (Sorcha.Agent + Sorcha.UI). Documented as a
  separate future cycle; raw `new HttpClient(handler)` instances in
  `Sorcha.UI.Core/Extensions/ServiceCollectionExtensions.cs` and
  `Sorcha.Agent/Commands/HaipReceiveCommand.cs` / `HaipPresentCommand.cs`
  remain untouched.
- **`Sorcha.Storage.InMemory` generic-store refactor.** The service-specific
  in-memory stores carry domain-rich semantics that the generic
  `InMemoryRepository<T>` cannot express. Keeping them is the right call;
  contract tests (Axis iv) cover the parity concern that drove the original
  refactor instinct.
- **Multi-active validator sealing.** Out of scope per spec.
- **`InMemoryBlueprintStore` / `InMemoryPublishedBlueprintStore` migration.**
  These are caches that reload from the persistent transaction log. They
  receive the warn-on-fallback log but are not audited.

---

## Open questions

None. All decisions captured above.
