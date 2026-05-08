# Phase 0 Research: Presentation Lifecycle Chain-Race Resolution

**Feature**: 119-presentation-seal-ordering
**Date**: 2026-05-08

## Scope

This document records the research decisions resolving the (very few) NEEDS CLARIFICATION points from Technical Context, plus best-practice findings for the new components introduced by this feature. The substantive design rationale is in the design document at `docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`; this research file captures the implementation-level technology and pattern choices that flow from it.

---

## R1 — Backing store for the seal-wait queues

**Decision**: Redis hash keyed by predecessor txId. Two separate keyspaces: `sorcha:presentation:awaiting-seal:submit:{predecessorTxId}` for queued submissions, `sorcha:presentation:awaiting-seal:advance:{outcomeTxId}` for queued workflow advancements.

**Rationale**:
- Durability across Blueprint Service restart is required (FR-119-010). Redis already provides this; no new infrastructure.
- Matches the storage choice for `IPendingPresentationStore` and `outcome-sentinel` already in Feature 111. Single coordination layer, single failure model.
- Single-key lookup on event arrival is O(1). No scan needed.
- Per-predecessor key naming means concurrent presentations don't collide on the same hash.

**Alternatives considered**:
- *In-memory ConcurrentDictionary.* Rejected: violates FR-119-010 (restart durability). Operationally fragile.
- *PostgreSQL table.* Rejected: heavier than needed for transient state with TTL semantics. EF migration overhead, joins not required.
- *Single shared Redis hash across all presentations.* Rejected: scan cost on every seal event scales with concurrent presentations.

---

## R2 — Event delivery: pubsub vs. consumer groups

**Decision**: Use the same `IEventSubscriber` abstraction `RegisterEventBridgeService` and `InstanceMirrorReconstructor` already use. Channel `transaction:confirmed`. Subscription pattern: per-process subscriber, no consumer groups.

**Rationale**:
- The Blueprint Service is the only consumer that needs this signal for *its own internal coordination*. There's no fan-out across replicas — each Blueprint Service replica only owns presentations whose initiation it served, because the pending state is held in Redis keyed by `presentationRequestId` and the verifier callback hits the gateway → Blueprint Service routing.
- Wait — actually under Feature 118 multi-replica Blueprint Service the verifier callback could land on a different replica than the one that initiated. The pending state in Redis is shared, but the *queue drain* needs to happen on whichever replica has the work. Two patterns work:
  1. **Every replica subscribes; first to claim the queue entry processes it.** Use `Redis SET NX` on a "claim" key per queue entry. Simple, no consumer-group infrastructure.
  2. **Redis Streams consumer group with `XREADGROUP`.** More robust for at-least-once semantics, but heavier wiring.
- Pattern 1 chosen for parity with `InstanceMirrorReconstructor` (which uses the same plain pubsub) and simplicity. The existing `transaction:confirmed` channel is pubsub-style; we don't need to refactor that.

**Alternatives considered**:
- *Redis Streams XREADGROUP-based consumer group.* Rejected: would require changing the publisher side (`Sorcha.Register.Service` event publish path) and is heavier than needed. The claim-key pattern gives at-least-once semantics adequately for this use case (idempotent drains via existing R6 sentinel).
- *MassTransit or RabbitMQ.* Rejected: introduces a new infrastructure dependency for state that already flows through Redis in the same trust boundary.

---

## R3 — Recovery sweeper cadence

**Decision**: 5-second tick, in the same `BackgroundService` as the `transaction:confirmed` subscriber.

**Rationale**:
- Missed events should be rare. The sweeper is insurance, not the primary path.
- 5 s is short enough to mask a missed event without operator-visible regression (well under the docket-build cycle of 5–15 s).
- 5 s is long enough not to dominate Redis traffic — at most a few `GetTransactionAsync` calls per tick per pending entry >30 s old.
- Configurable via `PresentationLifecycleOptions.SealRecoverySweepIntervalSeconds`.

**Alternatives considered**:
- *1-second tick.* Rejected: unnecessary load; the event channel handles the common case in <100 ms.
- *Exponential backoff per entry.* Rejected: complexity not justified for entries that rarely live more than one cycle.

---

## R4 — Failure timeout for never-seals

**Decision**: TTL = `pending.ValidityWindowSeconds` (default 600 s, blueprint-configurable). Same value already governs `IPendingPresentationStore` TTL.

**Rationale**:
- Operationally consistent — operators already understand the validity-window semantics.
- A presentation whose start record never seals is effectively a stuck presentation; treating it as "expired" matches the user-visible model.
- Configurable per-blueprint via existing `BlueprintPresentationConfig.PresentationValidityWindowSeconds`.

**Alternatives considered**:
- *Hard-coded 600 s.* Rejected: blueprint authors already tune validity window for their use case; the failure timeout should follow.
- *Separate `SealTimeoutSeconds` knob.* Rejected: extra config surface for no operational gain.

---

## R5 — Sentinel state machine extensions

**Decision**: Three new sentinel values, layered on top of the existing R6 state machine from Feature 111:

| New value | Set by | Meaning |
|---|---|---|
| `outcome-pending-seal` | `HandleOutcomeAsync` when initiated not yet sealed | Writer claimed; submission deferred to seal subscriber |
| `failed-predecessor-not-sealed` | Recovery sweeper at TTL | Predecessor never sealed; presentation failed |
| `failed-validator-reject` | Seal subscriber on submission rejection | Should-not-happen path; loud-log fallback |

The existing `outcome-pending-write` value is preserved for the inline (predecessor-already-sealed) path. The two paths converge at final `success` / `decline` / `abandoned` / `abandoned+outcome` values.

**Rationale**: Minimal extension of an existing state machine. Idempotent-replay logic in `HandleOutcomeAsync` (lines 262-275) handles the new states cleanly because it already enumerates terminal-or-pending states.

**Alternatives considered**:
- *Separate "seal-wait" key parallel to outcome-sentinel.* Rejected: two coordination keys instead of one. Race surface between them.

---

## R6 — Idempotent seal-event handling

**Decision**: When the seal subscriber fires for `txId=X`, it issues `HDEL` on both queue keys atomically and processes only the value it actually removed. Replay (same event delivered twice) is safe: second `HDEL` returns zero, no work done.

**Rationale**: Redis hash semantics make this trivial. No additional dedup state.

**Alternatives considered**:
- *Tracking processed event IDs.* Rejected: unbounded growth.

---

## R7 — Cancellation token discipline for fire-and-forget advancement

**Decision**: The seal subscriber's `CompleteAfterPresentationAsync` invocation gets a fresh `IServiceScope` and `CancellationToken.None` (not the subscriber's `stoppingToken`).

**Rationale**:
- Already established by PR #583 — the FR-015 advancement must outlive the originating HTTP callback's CT, and the work is durably persisted on the register before advancement so failures here are loggable not propagatable.
- Same pattern, lifted from inside `HandleOutcomeAsync`'s `Task.Run` to inside the subscriber's drain loop. Identical lifetime semantics.

**Alternatives considered**: None — pattern is already proven.

---

## R8 — Test infrastructure

**Decision**: Use the existing in-memory Redis test double (`Sorcha.Storage.InMemory.Redis`) for unit tests. Integration tests use the Docker-stack Redis via the existing `WebApplicationFactory` setup pattern.

**Rationale**: No new test infrastructure. The in-memory double has been used by `RedisAtomicDistributedCacheTests` and `IPendingPresentationStore` tests already.

**Alternatives considered**: *Testcontainers Redis* — rejected, slower, more flaky than the in-memory double, and the integration test layer already covers real Redis via Docker Compose.

---

## R9 — Walkthrough success measurement

**Decision**: AssuredIdentity Phase 2 pass / fail is the binary success criterion. SC-119-001 requires 10 of 10 consecutive runs. Use the existing `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` script wrapped in a PowerShell loop:

```powershell
1..10 | ForEach-Object {
  Write-Host "Run $_..."
  ./walkthroughs/AssuredIdentity/run.ps1 -Profile gateway
  if ($LASTEXITCODE -ne 0) { throw "Run $_ failed" }
}
```

**Rationale**: Real end-to-end signal. The walkthrough already exercises the failing path; passing it 10 times in a row exercises both fast-citizen and (rarely) the slow-citizen path.

**Alternatives considered**: *Synthetic timing-controlled test* — rejected as the *primary* signal because the walkthrough is the falsifiable user-visible claim. Synthetic tests live at the unit/integration layer for branch coverage.

---

## Summary

All NEEDS CLARIFICATION items from Technical Context are resolved. No design questions remain open. Ready for Phase 1 (data-model, contracts, quickstart).
