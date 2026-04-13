# Instance Binding Cache Contract

**Status**: New cache layer introduced by this feature.
**Implementation**: `Sorcha.Storage.Redis` (existing infrastructure, see `src/Common/Sorcha.Storage.Redis/`)
**Class**: `InstanceBindingCache` in `src/Services/Sorcha.Blueprint.Service/Services/`
**Consumer**: `ActionExecutionService.cs:309-332` (late-bind block)

## Overview

A Redis read-through cache for the participant→wallet binding map of a given workflow instance. Keeps the hot-path action submission lookup under 10ms while preserving the canonical ledger as the ultimate source of truth.

## Key

```text
instance:{instanceId}:bindings
```

- `{instanceId}` is the workflow instance Guid in lowercase string form
- One key per instance
- Optional namespace prefix per environment (e.g. `sorcha:prod:instance:...`) follows the existing Redis key convention

## Value

A serialized JSON object mapping participant role id to wallet address:

```json
{
  "citizen": "ws1qz4djygcwadma43ryram8luelwcpeyd6qgmkmjxkxy2xnpvr6uuswv2mggd",
  "assessor": "ws1qz9v25829mz9s9ezkpad2w0v4uegrd6newyrarx92nqsqx87wfkjgyzgfel"
}
```

The value mirrors the in-memory `Instance.ParticipantWallets` dictionary exactly, so cache reads can be deserialized directly into the existing model.

## TTL

- **1 hour, sliding on read**

The sliding TTL keeps active instances hot while letting cold instances expire and reclaim Redis memory. A cache miss is cheap because the binding is reconstructible from the ledger; the cache exists for hot-path performance, not for durability.

## Read path

```text
[ActionExecutionService late-bind block]
        │
        │ GetBindingsAsync(instanceId)
        ▼
[InstanceBindingCache.GetAsync]
        │
        ├─ Redis HIT → deserialize + return (target latency: < 10ms)
        │
        └─ Redis MISS
              │
              ▼
        [IInstanceStore.GetAsync(instanceId)]
              │
              ├─ Hit → write through to cache + return (target latency: < 50ms)
              │
              └─ Miss
                    │
                    ▼
              [IRegisterServiceClient: walk action chain for this instance]
                    │
                    └─ Replay actions, extract starting-action sender(s) → rebuild
                       ParticipantWallets → write to instance store + cache
                       (target latency: < 500ms; this path is rare)
```

The three-tier fallback means a peer that replicates the register but has no local cache or instance store can still resolve bindings by reading the ledger.

## Write path

The late-bind block at `ActionExecutionService.cs:326-330` writes to both:

```text
instance.ParticipantWallets[senderParticipantId] = request.SenderWallet;

await _instanceStore.UpdateAsync(instance, cancellationToken);   // existing
await _bindingCache.SetAsync(instanceId, instance.ParticipantWallets, ct);  // NEW
```

Cache write is fire-and-forget for telemetry purposes — a write failure is logged at warning level but does not fail the action submission. The instance store write remains authoritative.

## Invalidation

Bindings are immutable per instance, so the cache is conceptually write-once-read-many. The TTL handles eviction of cold instances. There is no explicit invalidation path; if the in-memory dictionary diverges from cache state (which would only happen via a bug), the next instance store read repopulates.

## Telemetry

`InstanceBindingCache` emits the following OpenTelemetry metrics and span tags via the existing `Sorcha.ServiceDefaults` telemetry stack:

| Metric | Type | Tags |
|---|---|---|
| `sorcha.binding_cache.requests` | Counter | `result=hit | miss-instance-store | miss-ledger-replay` |
| `sorcha.binding_cache.read_latency_ms` | Histogram | `result` |
| `sorcha.binding_cache.write_failures` | Counter | (none) |
| `sorcha.binding_cache.ledger_replay_count` | Counter | (none — should be near zero in steady state) |

Span tags on the `ActionExecutionService.execute_action` span:
- `binding.cache_result` — `hit`, `miss-instance-store`, `miss-ledger-replay`
- `binding.participant_id` — for traceability

## Failure modes

| Failure | Behaviour |
|---|---|
| Redis unavailable on read | Fall through to instance store (cache treated as miss) |
| Redis unavailable on write | Log warning, continue (write path is fire-and-forget) |
| Instance store and Redis both unavailable | Last-resort ledger replay; if even that fails, throw with context |
| Serialization failure | Treat as cache miss; log error; reconstruct from instance store |

## Tests required

1. **Hit path**: pre-populate cache, request bindings, assert `< 10ms` p99 in a tight loop
2. **Miss → instance store**: empty cache, populate instance store, request, assert hit and write-through
3. **Miss → ledger replay**: empty cache and instance store, populate register with a starting-action transaction, request, assert reconstruction
4. **Re-bind throws**: pre-bind via cache, attempt second submission with different wallet, assert `InvalidOperationException` (the immutability guarantee is enforced by `ActionExecutionService`, not the cache, but the integration test covers both)
5. **Redis down**: instance store hit must still work; latency degrades gracefully
6. **Sliding TTL**: read after 30 min, assert TTL extended; read after 65 min with no intervening reads, assert miss

## Configuration

- Connection: existing `Redis` config section (no new connection string)
- Key prefix: existing `Redis:KeyPrefix` config (no new keys)
- TTL: hardcoded constant (1 hour) — environments can adjust by overriding `InstanceBindingCacheOptions` if needed, but no new appsettings entry is added by default
