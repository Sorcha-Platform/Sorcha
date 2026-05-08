# Feature 111 — Chain-race resolution via seal-aware ordering

**Date:** 2026-05-08
**Author:** Stuart Fraser (with Claude)
**Status:** Approved (brainstorming complete)
**Closes:** Issue #582 (remaining races after PR #583)
**Related:** Feature 111 spec, PR #583 (FR-015 advancement), PR #581, PR #580

---

## Problem

Two chain-integrity races prevent the AssuredIdentity Phase 2 walkthrough from completing end-to-end after PR #583 closed the FR-015 advancement gap. Both races have the same root cause and surface in roughly half of normal human-paced presentation attempts, not just the synthetic walkthrough.

### Race 1 — VAL_BP_003: state-reconstruction races outcome confirmation

After PR #583's `Task.Run` advancement fires `instance.CurrentActionIds` forward to the next action, the walkthrough immediately submits that next action. `StateReconstructionService` (`src/Services/Sorcha.Blueprint.Service/Services/Implementation/StateReconstructionService.cs:73, 90-99`) reads sealed register transactions only — it cannot see mempool. If the outcome tx hasn't sealed yet (typical when citizen-to-callback latency < docket-build latency), reconstruction picks the previous *sealed* tx as the chain head, and the next action's submission lands with a `previousTransactionId` whose action does not route to the current action. Validator rejects with VAL_BP_003.

### Race 2 — VAL_CHAIN_001: outcome submitted before initiated has sealed

`PresentationLifecycleService.HandleOutcomeAsync` (line 339) builds the outcome tx with `previousTransactionId = pending.InitiatedTransactionId`. The initiated tx id was recorded immediately after `SubmitTransactionAsync` returned `Success=true` (line 190) — i.e., admission to the validator's unverified pool, not seal. If the verifier callback arrives faster than the docket build cycle, the outcome submission's chain check (`ValidationEngine.cs:805-822`) reads the register, finds the predecessor absent, and rejects with VAL_CHAIN_001. The outcome is dropped from the mempool and never seals.

### Common root

Both races derive from a single rule violation: **the system advances chain-pointer decisions based on `built.TxId` (mempool-known) while all chain enforcement reads `register` (sealed-only).** Three call sites violate the rule:

1. `HandleOutcomeAsync` — outcome's `previousTransactionId` ← still-mempool initiated.
2. `HandleAbandonmentAsync` line 489 — abandonment's `previousTransactionId` ← may also be still-mempool initiated (smaller race window, same bug).
3. `HandleOutcomeAsync` post-submit (PR #583's `Task.Run` → `CompleteAfterPresentationAsync`) — advances `instance.CurrentActionIds` before outcome seals, so the next action's reconstruction misses it.

### Why this matters in production, not just in walkthroughs

Real human presentation latency (citizen scans QR, reviews consent, biometric/PIN, wallet POSTs back, HAIP fires callback) is typically 6–11s. Docket build latency is 5–15s per cycle. The verifier callback fires *during* the first or second docket build cycle after initiated submission in roughly half of normal interactions. Citizens with saved credentials + biometric unlock + fast networks complete in 3–4s, well inside the race window. The slow citizen (15+s) is the fortunate one.

**Without this fix, every other production presentation attempt fails on either VAL_BP_003 or VAL_CHAIN_001.**

---

## Why now (and what was rejected)

Two alternative architectural directions were considered and rejected for this PR:

- **(B) Validator-side forward-reference tolerance.** Queue txs whose predecessor is in the mempool, release on seal, cascade-reject on predecessor reject. Generic, but changes validator chain invariants and fork-detection logic for a problem only two transaction types have. Bad scope-to-value ratio.
- **(C) Decouple lifecycle txs from the chain entirely.** Lifecycle txs become register annotations correlated by `presentationRequestId`; the action chain skips them; reachability moves from chain-pointer to instance-state. Likely the cleanest end state, but a much larger blast radius into validator chain rules, `StateReconstructionService`, and the audit/auditor mental model.

Option (A) — event-driven seal-aware ordering — is selected because:

- The infrastructure already exists. `Sorcha.Register.Core.Events.TransactionConfirmedEvent` is published on Redis Streams channel `transaction:confirmed` whenever a tx seals. Three live consumers — `RegisterEventBridgeService`, `InstanceMirrorReconstructor` (Feature 106 Wave D), `TransactionLifecycleEventBridge` — already subscribe.
- It fixes the actual design defect (using mempool-known IDs as chain pointers without waiting for seal) at every site, in one mechanism.
- The fork-resistance and audit-binding properties of chain participation are preserved — `VAL_CHAIN_FORK` continues to enforce "one outcome per initiated."
- It does not foreclose option (C) as a future migration. The seal-event subscription, the `outcome-sentinel` requestId-keyed primitive, and the consumer abstraction all carry forward; the migration cost is roughly equal to doing (C) directly today.

---

## The rule

> **A transaction whose `previousTransactionId` is set to a Sorcha-managed predecessor MUST NOT be submitted until that predecessor has been observed sealed. State transitions that depend on a Sorcha-managed predecessor's seal MUST NOT fire until that seal has been observed.**

"Observed" = `transaction:confirmed{txId=X}` received from Redis Streams, with a time-bounded fallback poll for missed events.

The rule applies uniformly to all three sites. Existing single-action submission paths (`ActionExecutionService.ExecuteAsync` for non-presentation actions) already satisfy the rule because human form-fill latency reliably exceeds docket-build latency outside the presentation flow.

---

## Architecture

### One new component: `IPresentationSealCoordinator`

Singleton in Blueprint Service DI. Backed by Redis (durability across Blueprint Service restarts; matches `IPendingPresentationStore`'s storage choice). Two queues, both Redis hashes keyed by the txId we're waiting on:

```
sorcha:presentation:awaiting-seal:submit:{predecessorTxId}  → SubmissionEnvelope
sorcha:presentation:awaiting-seal:advance:{outcomeTxId}      → AdvancementEnvelope
```

`SubmissionEnvelope` carries the fully-built and signed transaction submission DTO, the originating site (`outcome` | `abandonment`), the sentinel-update target value, structured logging context, and `enqueuedAt`.

`AdvancementEnvelope` carries `instanceId`, `completedActionId`, `draftPayload`, `registerId`, structured logging context, and `enqueuedAt`.

### One new background service: `PresentationSealSubscriber`

`BackgroundService`, registered alongside `RegisterEventBridgeService`. Subscribes to the existing `transaction:confirmed` Redis Streams channel. On each event:

1. Look up `submit:{txId}`. If hit: pop, submit to validator, update sentinel, log + metric.
   - On `VAL_CHAIN_FORK` we treat as "already sealed via another path" and dedupe (idempotency under double-fire).
   - On other errors, sentinel transitions to a failure state, structured log at `LogError`, metric increments.
2. Look up `advance:{txId}`. If hit: pop, resolve a fresh `IServiceScope`, call `IActionExecutionService.CompleteAfterPresentationAsync(...)`. (`CompleteAfterPresentationAsync`'s existing idempotency guard — the `instance.CurrentActionIds` check from PR #583 — handles replays cleanly.)

### Periodic sweeper (5s tick, same `BackgroundService`)

Two recovery responsibilities:

- **Missed event recovery:** for any queue entry older than 30s, poll `_registerClient.GetTransactionAsync(txId)` directly. If sealed, drain it as if the event had fired.
- **Stale / never-seals failure:** entries older than the pending hash's TTL (default 600s) get failed with structured log, sentinel set to `failed-predecessor-not-sealed`, metric `sorcha_presentation_seal_timeout_total{site}` increments. Catches the consensus-rejected-after-mempool-admission case.

### Optimisation: skip the queue when predecessor is already sealed

`HandleOutcomeAsync` and `HandleAbandonmentAsync` check predecessor seal state via `_registerClient.GetTransactionAsync` before queueing. If already sealed, submit inline (existing path, unchanged). The slow-citizen path (citizen took >15s, predecessor already sealed by callback time) keeps zero overhead.

---

## Call-site changes

### Site 1 — `HandleOutcomeAsync` (Race 2)

Today (lines 328-362): build → sign → fetch seq → submit → throw on validator reject → update sentinel.

After:

1. Build → sign (unchanged).
2. Check `GetTransactionAsync(pending.InitiatedTransactionId)`:
   - **Sealed:** submit inline (existing path, unchanged). Continue to Site 3 logic.
   - **Not sealed:** build the full submission DTO with seq number, enqueue `submit:{InitiatedTransactionId}`. Update sentinel to `outcome-pending-seal`. Return 200 to verifier. **The verifier callback does not block on seal.**
3. **Late-after-abandonment branch:** unchanged — initiated has always sealed by sweeper-fire time.

A new sentinel value `outcome-pending-seal` slots into the existing R6 state machine: a "writer claimed but submission deferred" state. Idempotent replays return early as today. The seal subscriber transitions it to final `success` / `decline` after submit succeeds.

### Site 2 — `HandleAbandonmentAsync` (latent race)

Same shape as Site 1. Sweeper builds + signs the abandonment tx; predecessor-sealed → submit inline; predecessor-pending → enqueue. Sentinel rollback path on validator-reject (lines 503-525) extends to handle "never-seals" via the periodic sweeper's failure state.

### Site 3 — FR-015 advancement (Race 1)

Today (PR #583's `Task.Run` block in `HandleOutcomeAsync`): immediately on outcome submission `Success`, fire-and-forget `Task.Run` with fresh DI scope calls `CompleteAfterPresentationAsync`.

After: replace the `Task.Run` with `_sealCoordinator.EnqueueAdvancementAsync(outcomeTxId, advancementEnvelope)`. The seal subscriber drains it once `transaction:confirmed{txId=outcomeTxId}` arrives, in a fresh DI scope (mirroring PR #583's lifetime fix). `CompleteAfterPresentationAsync`'s idempotency guard handles replays.

---

## Edge cases

| Case | Handling |
|---|---|
| Blueprint Service restart with queue entries | Redis-backed → entries survive. New process subscribes, sweeper drains backlog on cold start. |
| Predecessor sealed but event missed (Redis hiccup) | 5s sweeper tick polls `GetTransactionAsync` for entries >30s old. |
| Predecessor never seals (consensus rejection after mempool admission) | Entries >TTL fail with structured log, sentinel = `failed-predecessor-not-sealed`, metric increments. |
| Late-after-abandonment success | Sentinel = `abandoned`. Outcome's predecessor = initiated (long sealed by then). Submit inline. Sentinel → `abandoned+outcome`. No queue interaction. R6 logic unchanged. |
| Concurrent verifier callbacks | R6 sentinel guard fires before any queue interaction. Loser returns idempotent reply. Unchanged. |
| Outcome enqueued, then duplicate callback arrives | Sentinel = `outcome-pending-seal` → loser takes idempotent-replay path. |
| Validator rejects queued outcome on submit | Sentinel → `failed-validator-reject`. `LogError`. Should not happen — queue is single-consumer per predecessor. |
| VAL_CHAIN_FORK protection | Untouched. Each lifecycle tx still has exactly one successor on the chain. The queue serialises submission, doesn't introduce concurrent paths. |
| Cross-node replication on n1 (peer routing) | Seal events are local — Blueprint Service consumes its own register's events. n1's docket cycle is unchanged. No cross-node coordination needed. |
| `PresentationCallbackRelay` 503 from Blueprint Service | Unchanged. Verifier callback returns 200 fast; Blueprint never returns 503 for a queued outcome. |

---

## Observability

**Metrics** (Prometheus via OTel, on `Sorcha.Blueprint.PresentationLifecycle` meter):

- `sorcha_presentation_seal_wait_seconds{site}` — histogram, enqueue-to-drain latency. `site ∈ {outcome, abandonment, advance}`.
- `sorcha_presentation_seal_queue_depth{site}` — gauge, current queue size.
- `sorcha_presentation_seal_timeout_total{site}` — counter, never-seals failures.
- `sorcha_presentation_seal_recovered_via_sweeper_total{site}` — counter, missed-event recoveries (operational health signal).

**OTel spans**: new `presentation.seal-wait` parented to the existing `presentation.outcome` / `presentation.abandoned` spans. Attribute `presentationRequestId`. Lifetime = enqueue → drain.

**Structured logs**: at every state transition (enqueue, drain, sweeper-recover, timeout-fail), with `presentationRequestId` and `predecessorTxId`. `LogError` on timeout-fail; `LogInformation` on normal drains.

---

## Test strategy

| Layer | Coverage |
|---|---|
| Unit — `PresentationSealCoordinator` | Each edge-case row above against an in-memory Redis test double (`Sorcha.Storage.InMemory.Redis`); deterministic seal events. |
| Unit — `PresentationLifecycleService` | Outcome path with predecessor-sealed and predecessor-not-sealed branches, mocked coordinator. |
| Unit — `PresentationLifecycleService` | Abandonment path same shape. |
| Integration — `PresentationSealCoordinatorIntegrationTests` | Sibling of `RegisterEventBridgeServiceTests`. Full Redis Streams subscribe-publish loop. |
| Walkthrough | `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` Phase 2 step 7 must complete; 10 consecutive runs, no flakes. |

---

## Guarantees

1. **No chain-bearing tx submitted before its predecessor seals.** Race 2 (VAL_CHAIN_001) closed.
2. **No state-transition-driven action submission until the outcome it depends on seals.** Race 1 (VAL_BP_003) closed.
3. **Idempotent under restart, retry, and missed events.** Sweeper backstop + Redis durability + existing R6 sentinel + `CompleteAfterPresentationAsync` idempotency guard.
4. **Bounded failure mode for never-seals.** TTL-driven structured failure with operator visibility.
5. **Forward-compatible with future option (C).** Seal-event subscription, `outcome-sentinel` requestId-correlation, and `IPresentationConsumer` all carry forward unchanged.

---

## Scope and non-goals

**In scope:**

- New `IPresentationSealCoordinator` + Redis-backed implementation.
- New `PresentationSealSubscriber : BackgroundService`.
- Modifications to `PresentationLifecycleService.HandleOutcomeAsync`, `HandleAbandonmentAsync`, and the FR-015 advancement path.
- Sentinel state-machine extension (`outcome-pending-seal`, `failed-predecessor-not-sealed`, `failed-validator-reject`).
- Observability surface as listed.
- Test coverage as listed.
- Documentation updates: `sorcha-architecture` skill, `specs/111-presentation-lifecycle/research.md` (new R10), `data-model.md` §1.4 clarification.

**Out of scope:**

- Validator forward-reference tolerance (option B).
- Lifecycle-tx chain decoupling (option C).
- Generalised "submit-after-seal" primitive for non-presentation flows. The mechanism stays scoped to presentation lifecycle until a second use case appears.
- Changes to `AwaitSealAsync` as a public API surface (none introduced).
- Cross-service backplane changes; the existing `transaction:confirmed` Redis Streams channel is sufficient.
- Any change to FR-014 / FR-015 / FR-017 of the Feature 111 spec.

---

## Migration considerations

- **Master-only feature, clean-start (per FR-017).** No grandfathering, no in-flight migration. The new behaviour replaces the old code path outright.
- **Single PR** off `master` to a `fix/feature-111-chain-races` branch.
- **Required gate:** `Run discoverability checks` (per branch protection). `build-and-test` is a known flake (issue #511).
- **No feature flag.** The new behaviour is the only behaviour. Any rollback would be a `git revert`.

---

## Future migration path to option (C)

If at some point the seal-wait latency proves unacceptable (audit-team complaints about >30s outcome-visibility, or a future lifecycle pattern that nests presentations), option (C) remains a clean follow-up.

What carries forward:

- Seal-event subscription pattern (still drives `CompleteAfterPresentationAsync` in (C)).
- `outcome-sentinel` Redis key (becomes the *primary* requestId-correlation handle in (C), not just an idempotency guard).
- `IPresentationConsumer` abstraction (unchanged).
- Lifecycle tx payload schemas (`presentationRequestId` already on every lifecycle tx).

What changes for (A) → (C):

- Drop `previousTransactionId` from outcome and abandonment txs (or set to instance-anchor).
- `StateReconstructionService` filter: skip lifecycle txs when picking chain head.
- Validator chain rules: VAL_CHAIN_001 / VAL_CHAIN_FORK become opt-out for lifecycle tx types. New rule VAL_PRES_001: "at most one terminal outcome per `presentationRequestId`."
- Audit/auditor mental model documentation update.

The migration is additive on the validator side and mechanical on the Blueprint side. No data migration — existing register data is replayable under either rule.

---

## Open questions

None. All design questions resolved during brainstorming on 2026-05-08.
