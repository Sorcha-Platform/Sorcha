# Data Model: Storage Provider Audit and Validator Mempool Durability

**Feature**: 113-storage-durability-audit
**Phase**: 1 (post-research)

This feature introduces no new domain entities. The data shapes documented
here are infrastructure types: registration records, lease tokens, atomic
cache entries, and Redis key layouts. They have no representation in any
domain database (Postgres, Mongo) other than the validator-mempool's Redis
state.

---

## 1. StorageRegistrationRecord (in-memory, per-service)

A record produced by `IStorageRegistrationLog` at service startup. Held in a
list inside the singleton implementation; consumed by the fail-fast helper,
the health check, and the metrics gauge.

| Field            | Type      | Notes                                                                            |
| ---------------- | --------- | -------------------------------------------------------------------------------- |
| InterfaceName    | `string`  | Fully-qualified interface name (e.g., `Sorcha.Wallet.Core.IWalletRepository`)    |
| ImplementationName | `string` | Fully-qualified implementation class name                                        |
| Backend          | `string`  | Persistent backend label (`postgres`, `mongo`, `redis`) or literal `in-memory`   |
| Reason           | `string`  | Free-text rationale, especially for in-memory fallbacks                          |
| RegisteredAt     | `DateTimeOffset` | UTC timestamp of registration call                                        |
| IsAudited        | `bool`    | True if `InterfaceName` is in the audited allow-list                             |
| IsInMemory       | `bool`    | True if `Backend == "in-memory"`                                                 |

**Invariants**:

- An interface name appears at most once per service. The registration log
  throws `InvalidOperationException` on a second registration of the same
  interface.
- `IsAudited && IsInMemory` is the condition that triggers fail-fast in
  Production/Staging.

**State transitions**: None. Records are immutable after creation; the list
is append-only over the lifetime of the service.

---

## 2. AuditedStorageInterfaces (static configuration)

An immutable set of interface names that trigger fail-fast in Production when
on an in-memory backend. Defined in code, not configuration.

```csharp
internal static class AuditedStorageInterfaces
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string>
    {
        "Sorcha.Wallet.Core.Repositories.IWalletRepository",
        "Sorcha.Register.Storage.IRegisterRepository",
        "Sorcha.Blueprint.Service.Storage.IInstanceStore",
        "Sorcha.Blueprint.Service.Storage.IActionStore",
        "Sorcha.Validator.Service.Storage.IVerifiedTransactionQueue",
        "Sorcha.AtomicCache.IAtomicDistributedCache",
    };
}
```

Adding or removing entries is an explicit code change, reviewable in the diff
and gated by the contract-test pair existing for the interface.

---

## 3. VerifiedTransaction (existing — domain payload)

The transaction held in the mempool. **Unchanged from current Sorcha.** Listed
here for reference because it crosses the new contract boundary.

| Field          | Type            | Notes                                            |
| -------------- | --------------- | ------------------------------------------------ |
| TransactionId  | `string`        | Globally unique within a register                |
| Transaction    | `Transaction`   | Existing domain envelope                         |
| EnqueuedAt     | `DateTimeOffset` | Used for FIFO tiebreaker within priority class  |
| Priority       | `int`           | Higher value sorts earlier                       |
| ExpiresAt      | `DateTimeOffset` | TTL — transactions older than this are dropped on cleanup |

---

## 4. VerifiedTransactionLease (new — claim-time hold)

Returned by `ClaimAsync`. Carries everything the caller needs to confirm or
release the claim.

| Field           | Type                | Notes                                                   |
| --------------- | ------------------- | ------------------------------------------------------- |
| TransactionId   | `string`            | Same as the underlying `VerifiedTransaction`            |
| RegisterId      | `string`            | Owning register (the lease is per-register)             |
| Transaction     | `VerifiedTransaction` | Full payload — caller never re-fetches                |
| LeaseExpiresAt  | `DateTimeOffset`    | UTC; after this, lease auto-releases on next `ClaimAsync` |

**Invariants**:

- `LeaseExpiresAt > now` at the moment of return.
- A transaction with an active lease is invisible to subsequent `ClaimAsync`
  calls until lease expires or `ConfirmAsync` / `ReleaseAsync` is called.
- `Peek` returns transactions whose state is `available`, never those in
  `claimed`.

**State transitions**:

```
Enqueue           ── ZADD available
ClaimAsync        ── ZREM available; ZADD claimed leaseExpiresAtUnixMs
ConfirmAsync      ── ZREM claimed; HDEL payload; ZREM expiry
ReleaseAsync      ── ZREM claimed; ZADD available (rescore from payload)
LeaseExpiry       ── (within next ClaimAsync) ZREM claimed; ZADD available
TtlExpiry         ── (within CleanupExpired) ZREM all; HDEL payload
```

---

## 5. Redis Key Layout (validator mempool)

All keys are namespaced per-register so per-register operations are O(log N)
without scanning. Cluster slots are aligned by including the register ID in
braces (`{registerId}`) so multi-key operations can be wrapped in a single
transaction or Lua execution on Redis Cluster.

| Key                                              | Type     | Score / Field semantics                                            |
| ------------------------------------------------ | -------- | ------------------------------------------------------------------ |
| `sorcha:vtq:{registerId}:available`              | ZSET     | score = `priorityScore`, member = `txId`                           |
| `sorcha:vtq:{registerId}:claimed`                | ZSET     | score = `leaseExpiresAtUnixMs`, member = `txId`                    |
| `sorcha:vtq:{registerId}:payload`                | HASH     | field = `txId`, value = JSON(VerifiedTransaction)                  |
| `sorcha:vtq:{registerId}:expiry`                 | ZSET     | score = `ttlExpiresAtUnixMs`, member = `txId`                      |
| `sorcha:vtq:registers`                           | SET      | members = active register IDs (used by stats + cleanup sweep)      |

**Score formula**: `priorityScore = (MaxPriority - priority) * 1e13 + enqueuedAtUnixMs`

- Domain priority is descending (higher value = comes first). `(MaxPriority - priority)` flips this so `ZRANGE 0 N-1` returns highest priority first.
- `1e13` is large enough that any realistic FIFO-tiebreaker timestamp delta
  cannot bridge two priority classes (1e13 ms is ~317 years).
- `enqueuedAtUnixMs` provides FIFO order within a priority class.

**TTL on keys**: All four register-keyed keys have a Redis-level `EXPIRE` set
to `7 days` as a defence-in-depth measure. The application-level `expiry`
ZSET drives `CleanupExpired`; the Redis `EXPIRE` is a safety net for fully
abandoned registers.

---

## 6. AtomicCacheEntry (logical — Redis-keyed)

The atomic cache holds opaque string values keyed by application-defined
strings. There is no schema; callers serialise and deserialise their own
payloads.

| Conceptual field | Redis representation                         |
| ---------------- | -------------------------------------------- |
| Key              | Redis string key (caller-chosen, e.g., `haip:nonce:{nonce}`) |
| Value            | Redis string value                           |
| TTL              | `EXPIRE` set at write time                   |

**Operations**:

| Operation                    | Redis primitive                                                      | Atomicity                              |
| ---------------------------- | -------------------------------------------------------------------- | -------------------------------------- |
| `GetAsync`                   | `GET`                                                                | Single round-trip                      |
| `SetAsync(key, value, ttl)`  | `SET key value EX ttl`                                               | Single round-trip                      |
| `RemoveAsync`                | `DEL`                                                                | Single round-trip; idempotent          |
| `GetAndRemoveAsync`          | `GETDEL` (`IDatabase.StringGetDeleteAsync`)                          | Single round-trip; atomic GETDEL       |
| `TryUpdateIfMatchAsync`      | Lua: `GET → if equals → SET → return 1, else return 0`               | Single round-trip; atomic CAS          |

The in-memory fallback uses `ConcurrentDictionary.TryRemove(out value)` for
`GetAndRemoveAsync` (atomic at dictionary level), and a `lock` over a
read+write sequence for `TryUpdateIfMatchAsync`.

---

## 7. HAIP store keying conventions (existing — referenced for completeness)

Keys used by the HAIP migration. Format unchanged; only the consumption
operation changes (Get + Remove → atomic GETDEL).

| Store                        | Key pattern                              | Lifetime            |
| ---------------------------- | ---------------------------------------- | ------------------- |
| `NonceStore`                 | `haip:nonce:{nonce}`                     | TTL 300s (default)  |
| `PreAuthCodeStore`           | `haip:preauth:{code}`                    | TTL 600s (default)  |
| `PresentationRequestStore`   | `haip:presentation:{requestId}`          | TTL = validity window |

---

## 8. Configuration shape

New configuration sections introduced by this feature, all bound via
`IOptions<T>` and surfaced through `appsettings.json`.

```json
{
  "Storage": {
    "AllowInMemoryInProduction": false
  },
  "ValidatorMempool": {
    "LeaseDurationSeconds": 60,
    "MaxClaimBatchSize": 100,
    "CleanupIntervalSeconds": 30
  }
}
```

`Storage:AllowInMemoryInProduction` is a per-service override; it is read by
the fail-fast helper at startup and not re-read at runtime.

`ValidatorMempool` is read by `RedisVerifiedTransactionQueue` and the
background `CleanupExpired` hosted service. `LeaseDurationSeconds` is a
default that callers may override per `ClaimAsync` call.

---

## 9. Metrics labels (cross-cutting)

For the metrics defined in spec FR-025–FR-029. Documented here so callers
have a single source for cardinality:

| Metric                                            | Label set                                                                       |
| ------------------------------------------------- | ------------------------------------------------------------------------------- |
| `sorcha_storage_provider_info`                    | `service`, `interface`, `implementation`, `backend`                             |
| `sorcha_storage_fallback_active`                  | `service`, `interface`                                                          |
| `sorcha_validator_mempool_size`                   | `register_id`, `state` (`available` \| `claimed`)                               |
| `sorcha_validator_mempool_lease_expired_total`    | `register_id`                                                                   |
| `sorcha_haip_nonce_consume_total`                 | `store` (`nonce` \| `preauth` \| `presentation`), `outcome` (`success` \| `miss`) |

`register_id` is the only high-cardinality label and matches the cardinality
already used by existing Sorcha register metrics.
