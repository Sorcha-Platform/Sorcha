# Storage Provider Audit and Validator Mempool Durability — Design

**Date:** 2026-04-25
**Status:** Draft (pending plan)
**Owner:** Stuart Fraser

## Summary

Eliminate the silent-fallback-to-InMemory class of bug in containerised Sorcha
deployments, give the validator a mempool that survives process restart, close
the known TOCTOU gap in HAIP nonce consumption, and add cross-backend parity
tests where the InMemory and persistent paths can drift.

The work splits into four independent axes that share a single piece of
infrastructure (a startup-time storage registration log) and ship as a
sequenced set of PRs that each leave the system in a working state.

## Background

A casual audit surfaced four overlapping concerns:

1. **Wallet, Register, and Blueprint services all carry an `if connection
   string set → EfCore/Mongo, else → InMemory` registration shape.** A
   misconfigured deployment silently boots on a transient store and loses data
   on every restart. There is no log line, no health check, and no dashboard
   signal that a service is running on InMemory.
2. **The Validator Service mempool (`VerifiedTransactionQueue`) is a per-process
   `ConcurrentDictionary`.** Verified-but-not-yet-sealed transactions vanish on
   process restart. Multi-replica deployments with a shared validator identity
   would diverge.
3. **HAIP replay-protection state has a known TOCTOU gap.** `NonceStore.ConsumeAsync`
   does `GetStringAsync` followed by `RemoveAsync` — two round-trips. A
   concurrent request can read the same nonce between them and both succeed.
   `PreAuthCodeStore` and `PresentationRequestStore` have similar shapes.
4. **The Blueprint and Wallet services lack the cross-backend contract tests
   that the Register module already has** (`RegisterRepositoryContractTests`).
   Feature 106's read-only-mirror semantics had to be hand-mirrored from
   `EfCoreInstanceStore` into `InMemoryInstanceStore` because there was no
   structural test forcing parity. The next semantic to land is at the same
   risk.

## Scope

### In scope

1. **Config lockdown + warn-on-fallback** — every InMemory store registration
   logs `LogWarning` at startup naming the interface and class. In `Production`
   and `Staging` environments, services *fail-fast* with a clear error if they
   would have selected an InMemory path for an audited interface. A single
   `Storage:AllowInMemoryInProduction=true` config flag bypasses fail-fast for
   exceptional deployments (CI smoke tests against ephemeral envs).
2. **Validator mempool durability** — replace `VerifiedTransactionQueue`'s
   in-memory `ConcurrentDictionary<string, RegisterQueue>` with a
   Redis-Sorted-Set-backed implementation. Tighten the
   `IVerifiedTransactionQueue` contract from atomic-`Dequeue` to a
   `Claim`/`Confirm`/`Release` lease pattern so an HA standby replica can
   coordinate without consuming work it cannot seal. Keep the in-memory
   implementation as the test/dev fallback.
3. **HAIP secret-state durability sweep** — close the `Get + Remove` TOCTOU in
   `NonceStore` by switching to atomic GETDEL via a new
   `IAtomicDistributedCache` shim. Apply the same fix to `PreAuthCodeStore`.
   Audit `PresentationRequestStore` for the same shape and apply
   compare-and-set on terminal-state writes.
4. **Cross-backend contract tests** — port the `RegisterRepositoryContractTests`
   pattern to `IInstanceStore`, `IActionStore`, `IWalletRepository`, and the
   new `IAtomicDistributedCache`. Each contract is an abstract test base
   exercised against every implementation.

### Out of scope (deferred backlog)

- HTTP client consolidation. `Sorcha.Agent` and `Sorcha.UI.Core` carry
  ~30 raw `new HttpClient(handler)` instances that bypass `IHttpClientFactory`
  and the consolidated `Sorcha.ServiceClients.Http` patterns. Worthwhile but
  cross-cutting and largely cosmetic next to a validator that loses its
  mempool on restart. Separate cycle.
- `Sorcha.Storage.InMemory` generic-store refactor. Service stores are
  domain-rich (mirror semantics, version concurrency, multi-key queries).
  Forcing them through `InMemoryRepository<T>` either demotes them to thin
  facades (same code, more layers) or loses domain query type-safety. The
  contract-test pattern delivers the actual value the refactor was reaching
  for, at lower cost.
- Multi-active validator sealing. A consensus/governance problem, not a
  mempool problem. Mempool durability is a prerequisite but not a substitute.
- Migrating `InMemoryBlueprintStore` and `InMemoryPublishedBlueprintStore` to
  a persistent backing. These caches reload from the persistent transaction
  log; losing them is a cold-start, not data loss. They get the warning log
  but are not on the audited list.

### Non-goals

- No new infrastructure dependencies. Postgres, Redis, and Mongo are already
  in every deployment.
- No behavioural change for end users. All four axes are correctness and
  observability fixes that are silent on the happy path.
- No HD wallet or cryptography changes.

## Architecture

### Axis (i): Storage registration log + fail-fast

A small shared helper in `Sorcha.ServiceDefaults`:

```csharp
public interface IStorageRegistrationLog
{
    void RegisterPersistent(string interfaceName, string implementationName, string backend);
    void RegisterInMemory(string interfaceName, string implementationName, string reason);
}
```

Each service's startup wiring calls one of these *before* the matching
`AddScoped`/`AddSingleton`. The implementation:

- `RegisterPersistent` — `LogInformation` summarising "IWalletRepository →
  EfCoreWalletRepository (postgres: wallet-db)".
- `RegisterInMemory` — `LogWarning` with the greppable banner
  `[STORAGE-FALLBACK] IWalletRepository → InMemoryWalletRepository — DATA WILL
  NOT SURVIVE RESTART. Reason: <reason>`.
- Health check `storage-providers` reports degraded if any in-memory provider
  is registered.
- A boot-time banner at INFO summarises the service's persistent vs in-memory
  tally so a `kubectl logs` immediately shows what is missing.

A second helper, `EnforcePersistentStorageInProduction(IHostEnvironment)`, is
called once at the end of service startup. If `env.IsProduction()` or
`env.IsStaging()` and any audited interface fell through to InMemory, throw
`InvalidOperationException` with the registration log as the message.
Development logs the warnings but starts.

The `Storage:AllowInMemoryInProduction=true` config flag bypasses the
fail-fast and emits `LogCritical` so the override is loud and visible.

**Audited interfaces** (failing fast in Production):

- `IWalletRepository`
- `IRegisterRepository`
- `IInstanceStore`
- `IActionStore`
- `IVerifiedTransactionQueue`
- `IAtomicDistributedCache`

`InMemoryBlueprintStore`, `InMemoryPublishedBlueprintStore`, `BlueprintCache`,
`ValidatorRegistry`, and similar caches log the warning but are not audited —
their loss is a cold-start, not data loss.

### Axis (ii): Redis-backed validator mempool

The `IVerifiedTransactionQueue` contract changes from atomic-`Dequeue` to a
lease-shaped API:

```csharp
public interface IVerifiedTransactionQueue
{
    bool Enqueue(string registerId, Transaction tx, int priority = 0);

    Task<IReadOnlyList<VerifiedTransactionLease>> ClaimAsync(
        string registerId, int maxCount, TimeSpan leaseDuration, CancellationToken ct);
    Task ConfirmAsync(
        string registerId, IEnumerable<string> transactionIds, CancellationToken ct);
    Task ReleaseAsync(
        string registerId, IEnumerable<string> transactionIds, CancellationToken ct);

    IReadOnlyList<VerifiedTransaction> Peek(string registerId, int maxCount);
    bool Contains(string registerId, string transactionId);
    int GetCount(string registerId);
    int GetTotalCount();

    bool Remove(string registerId, string transactionId);
    int Clear(string registerId);
    int ClearAll();
    int CleanupExpired();

    VerifiedQueueStats GetStats();
    RegisterQueueStats GetRegisterStats(string registerId);
}
```

`ClaimAsync` returns leases — transactions remain in the queue but are marked
claimed-until-`leaseExpiresAt`. `ConfirmAsync` removes after seal.
`ReleaseAsync` returns to the available pool. Expired leases auto-release on
the next `ClaimAsync`.

#### Redis key layout

Per-register namespacing for O(log n) operations:

```
sorcha:vtq:{registerId}:available    ZSET — score = priorityScore, member = txId
sorcha:vtq:{registerId}:claimed      ZSET — score = leaseExpiresAtUnixMs, member = txId
sorcha:vtq:{registerId}:payload      HASH — txId → JSON(VerifiedTransaction)
sorcha:vtq:{registerId}:expiry       ZSET — score = ttlExpiresAtUnixMs, member = txId
sorcha:vtq:registers                 SET  — set of active registerIds
```

`priorityScore` is `(maxPriority - priority) * 1e13 + enqueuedAtUnixMs` —
higher domain priority sorts earlier under `ZRANGE`, FIFO within the same
priority.

#### Operations

| Op            | Redis primitives                                                                                                                                                             |
| ------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Enqueue       | TXN: `ZADD available`, `HSET payload`, `ZADD expiry`, `SADD registers`. Reject if `ZCARD available + ZCARD claimed >= MaxTransactionsPerRegister`.                           |
| ClaimAsync    | Lua: walk `claimed` for `score < now`, return them to `available`; then `ZRANGE available 0 N-1`, for each `ZREM available` and `ZADD claimed leaseExpiry`; return payloads. |
| ConfirmAsync  | TXN: `ZREM claimed`, `HDEL payload`, `ZREM expiry`.                                                                                                                          |
| ReleaseAsync  | TXN: `ZREM claimed`, `ZADD available` (score recomputed from payload).                                                                                                       |
| Peek          | `ZRANGE available 0 N-1`, `HMGET payload`. Read-only.                                                                                                                        |
| CleanupExpired| `ZRANGEBYSCORE expiry 0 now`, delete from all four keys. Background hosted service runs every 30s.                                                                           |
| Stats         | `ZCARD` per register, sample over `payload` for oldest/newest.                                                                                                               |

Multi-key writes go through `MULTI/EXEC` or Lua. The claim-and-auto-release
script is a single Lua atomic so the standby coordination story does not
require round-trip locking.

#### Lease default and configuration

`ValidatorMempool:LeaseDurationSeconds` defaults to 60s — sized to docket-build
worst-case in current Sorcha plus margin. Configurable per deployment.

#### Service registration

```csharp
if (!string.IsNullOrEmpty(redisConn))
{
    services.AddSingleton<IVerifiedTransactionQueue, RedisVerifiedTransactionQueue>();
    storageLog.RegisterPersistent(
        "IVerifiedTransactionQueue", "RedisVerifiedTransactionQueue", "redis");
}
else
{
    services.AddSingleton<IVerifiedTransactionQueue, InMemoryVerifiedTransactionQueue>();
    storageLog.RegisterInMemory(
        "IVerifiedTransactionQueue", "InMemoryVerifiedTransactionQueue",
        "no Redis connection string configured");
}
```

`IVerifiedTransactionQueue` is on the audited list. Production fails fast.

#### In-memory implementation

The existing `VerifiedTransactionQueue` is renamed
`InMemoryVerifiedTransactionQueue` and adapted to the new lease-shaped API.
`ClaimAsync` becomes a synchronous take-and-track-claimed; `ConfirmAsync`
removes from `_byId`; `ReleaseAsync` puts back into the SortedSet. Same lock,
same correctness story.

#### Orchestrator changes

Single caller (`DocketBuildTriggerService` /  `ValidatorOrchestrator`):

```csharp
var leases = await _queue.ClaimAsync(registerId, maxBatch, _leaseDuration, ct);
try {
    var docket = await BuildAndSealDocketAsync(leases, ct);
    await _queue.ConfirmAsync(registerId, leases.Select(l => l.TransactionId), ct);
} catch {
    await _queue.ReleaseAsync(registerId, leases.Select(l => l.TransactionId), ct);
    throw;
}
```

If the validator dies between Claim and Confirm, the lease auto-releases on the
next Claim by any replica. If Confirm fails after seal, the lease eventually
expires and the transaction reappears in `available` — the validator's
existing duplicate-detection in the seal pipeline rejects the second seal and
emits `validator_mempool_double_seal_attempt` for observability.

### Axis (iii): HAIP atomic cache

A new shim wraps `StackExchange.Redis.IDatabase` (already DI-registered for
Redis Streams) and exposes the GETDEL-shaped operations that
`IDistributedCache` does not:

```csharp
public interface IAtomicDistributedCache
{
    Task<string?> GetAndRemoveAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);
    Task<bool> RemoveAsync(string key, CancellationToken ct);
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task<bool> TryUpdateIfMatchAsync(
        string key, string expected, string newValue, TimeSpan ttl, CancellationToken ct);
}
```

Implementations:

- `RedisAtomicDistributedCache` — `IDatabase.StringGetDeleteAsync`,
  `StringSetAsync(when: When.Always)`, Lua compare-and-set for
  `TryUpdateIfMatchAsync`.
- `InMemoryAtomicDistributedCache` — `ConcurrentDictionary` with
  `TryRemove`-with-return-value (atomic at dictionary level), `_lock` + lookup
  for CAS. Slower than Redis at scale, semantically identical for tests.

`IAtomicDistributedCache` is on the audited list. Production fails fast.

#### NonceStore and PreAuthCodeStore

Both rewrites become single-line:

```csharp
public async Task<bool> ConsumeAsync(string nonce, CancellationToken ct = default)
{
    var key = $"haip:nonce:{nonce}";
    var value = await _cache.GetAndRemoveAsync(key, ct);
    return value != null;
}
```

One round-trip. Atomic. No TOCTOU.

#### PresentationRequestStore

Different shape — read-many before consume. Audit for:

- TTL set on every write, not just first creation.
- Final state-transition writes (consumed, expired) use
  `TryUpdateIfMatchAsync` so two callbacks racing each other into different
  terminal states resolve to one winner.

If the audit shows it is already CAS-correct, the change is a justifying
comment plus a test asserting the behaviour. Don't change correct code.

### Axis (iv): Cross-backend contract tests

Three new contract-test base classes in the test layer:

```
tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/
  IInstanceStoreContractTests.cs        (abstract)
  InMemoryInstanceStoreContractTests.cs (subclass)
  EfCoreInstanceStoreContractTests.cs   (subclass, Testcontainers Postgres)

tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/
  IActionStoreContractTests.cs          (abstract + two subclasses)

tests/Sorcha.Wallet.Service.Tests/Repositories/Contracts/
  IWalletRepositoryContractTests.cs     (abstract + two subclasses)
```

Each abstract base exercises CRUD, version concurrency, mirror semantics, and
domain queries. Existing `InMemoryInstanceStoreTests` and
`InMemoryActionStoreTests` files become thin subclasses pointing at the
abstract suite; bespoke in-memory-only tests stay where they are.

`IVerifiedTransactionQueueContractTests` follows the same pattern with
MockRedis (`Sorcha.Testing.MockRedisBuilder`) for the Redis subclass. One
additional Testcontainers-backed test covers the claim Lua script
specifically — Lua is the place where MockRedis is most likely to behave
subtly differently from real Redis.

`IAtomicDistributedCacheContractTests` covers atomic GETDEL semantics, TTL
expiry, idempotent missing-key behaviour, and CAS races.

## Error handling

| Failure                                                  | Behaviour                                                                                                                                                                                                                                              |
| -------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Redis unavailable at validator startup                   | Service refuses to start in Production (audited interface). In Development, falls back to InMemory with the loud warning.                                                                                                                              |
| Redis becomes unavailable mid-flight                     | Operations throw `RedisConnectionException`. `ValidatorOrchestrator` already has try/catch around the docket-build path; the lease auto-releases on next Claim. Background `CleanupExpired` retries silently.                                          |
| Lua claim script fails partway                           | Lua is atomic — either the whole claim succeeds or no state changes.                                                                                                                                                                                   |
| Confirm fails after seal                                 | Lease eventually expires, transaction reappears in `available`, gets re-claimed and re-sealed as a duplicate. Validator's existing duplicate-detection in the seal pipeline rejects the second seal. Logged as `validator_mempool_double_seal_attempt`. |
| HAIP atomic-cache GETDEL on missing key                  | Returns null, treated as "nonce already consumed or never existed", same 400 response as today.                                                                                                                                                        |
| Storage registration log called twice for same interface | Throws `InvalidOperationException` at startup. Better to fail loudly than silently let two registrations fight.                                                                                                                                        |

## Observability

Five OpenTelemetry instruments across three new meter sources, plus
structured log fields. Metrics flow through the existing
`Sorcha.ServiceDefaults` `AddOpenTelemetry().WithMetrics()` pipeline and
export via OTLP to the Aspire dashboard. No Prometheus dependency
introduced.

```
Meter "Sorcha.Storage":
  sorcha_storage_provider_info{service, interface, implementation, backend}
    (observable gauge, one observation per registered interface, set at startup)
  sorcha_storage_fallback_active{service, interface}
    (observable gauge; = 1 when an audited interface is on InMemory; direct alerting target)

Meter "Sorcha.Validator.Mempool":
  sorcha_validator_mempool_size{register_id, state}
    (observable gauge; state ∈ {available, claimed})
  sorcha_validator_mempool_lease_expired_total{register_id}
    (counter; high value = validator dying mid-seal or lease too short)

Meter "Sorcha.Haip.Nonces":
  sorcha_haip_nonce_consume_total{store, outcome}
    (counter; store ∈ {nonce, preauth, presentation}, outcome ∈ {success, miss})
```

Each new meter source is registered via `metrics.AddMeter("...")` in
`Sorcha.ServiceDefaults.Extensions.ConfigureOpenTelemetry`, alongside the
existing `Sorcha.Peer.Service` and `Sorcha.Blueprint.Service.Presentation`
entries.

Structured log fields on every InMemory-fallback warning: `service`,
`interface`, `implementation`, `reason`, `environment`.

## Testing

- **Axis (i)**: unit tests on `StorageRegistrationLog`; integration test per
  service (Wallet, Register, Blueprint) — `WebApplicationFactory` in
  `Production` env without connection strings → service fails to start with
  the expected exception. Same factory in `Development` → starts with
  warnings. Health-check test confirms degraded when any audited InMemory is
  registered.
- **Axis (ii)**: `IVerifiedTransactionQueueContractTests` abstract base
  exercised against InMemory and MockRedis subclasses. Lease expiry test
  (claim, advance fake clock past lease, claim again with second client →
  second client gets the same transaction). Crash-recovery test (claim,
  dispose without Confirm, advance past lease, new client claims). Concurrent
  multi-replica claim test. One Testcontainers-backed Lua-script smoke test.
- **Axis (iii)**: `IAtomicDistributedCacheContractTests` against InMemory and
  MockRedis. Concurrent-consume race test on `NonceStore` and
  `PreAuthCodeStore` — 100 tasks consuming the same nonce, exactly one
  returns true. Today's implementation fails this test.
  `PresentationRequestStore` multi-reader-then-consume test.
- **Axis (iv)**: contract-test pairs for `IInstanceStore`, `IActionStore`,
  `IWalletRepository`. Each contract exercises CRUD, version concurrency,
  mirror semantics, domain queries.

## Rollout / commit sequence

Eight PRs ordered for safe roll-back at any cut:

1. **Storage registration log + audited-interface infra** in
   `Sorcha.ServiceDefaults`. No behavioural change. Lands first because
   everything else depends on it.
2. **Wallet Service — adopt registration log + fail-fast.** Includes the
   `IWalletRepository` contract-test pair.
3. **Register Service — same shape as PR 2.** Already has contract tests;
   adds the registration helpers and fail-fast.
4. **Blueprint Service — same shape as PR 2.** Adds contract tests for
   `IInstanceStore` and `IActionStore`.
5. **`IAtomicDistributedCache` + InMemory + Redis impls + contract tests.**
   No consumers yet; lands as infra.
6. **HAIP migration.** `NonceStore` and `PreAuthCodeStore` move to the atomic
   shim. `PresentationRequestStore` audit (tighten or annotate). Race-condition
   tests.
7. **`IVerifiedTransactionQueue` lease-shaped contract change + InMemory
   implementation rename.** Touches the interface, the in-memory class, the
   orchestrator call sites. Single-validator deployment behaviour unchanged.
8. **`RedisVerifiedTransactionQueue` + contract test pair + Validator service
   registration + audited list entry.** Flips the validator to Redis-backed
   mempool. Fail-fast in Production if Redis missing.

Each PR is independently mergeable, has its own test suite, and can be
reverted without affecting later cuts.

## Documentation updates

- `CLAUDE.md` — new "Critical Pattern": "Storage registration must go through
  `IStorageRegistrationLog`. Audited interfaces fail-fast in Production."
- Each touched service README — note the new fail-fast behaviour and the
  `Storage:AllowInMemoryInProduction` bypass flag.
- `docs/reference/development-status.md` — bump validator durability and HAIP
  race-protection status.
- `.claude/skills/sorcha-architecture/SKILL.md` — extend the "Validator Key
  Roster" section with mempool durability notes; add a new "Storage provider
  audit" section pointing at the registration-log helper.
- `MEMORY.md > Key Discoveries` — short entry on the fallback-warn pattern.

## Risks

1. **MockRedis Lua-script fidelity** — the claim script is the most complex
   piece and the place where MockRedis is most likely to behave differently
   from real Redis. Mitigation: one Testcontainers-backed test for the claim
   Lua specifically.
2. **Production fail-fast in n1** — first deploy after these PRs land, if any
   service's connection string is misconfigured, the service refuses to start.
   n1 currently boots clean, but this kind of change finds latent config bugs
   the hard way. Mitigation: PR 1 logs the registration table at INFO before
   the fail-fast call, so `kubectl logs` on a failing service immediately
   shows what is missing.
3. **Lease leakage in InMemory fallback** — the in-memory queue's lease
   tracking is in-process-only. If a dev runs two test instances against the
   same in-memory queue, leases do not coordinate. Acceptable — the InMemory
   path is not for multi-process use; that is the whole point of the
   warn-on-fallback.

## Open questions

None at design time. All decisions captured above.
