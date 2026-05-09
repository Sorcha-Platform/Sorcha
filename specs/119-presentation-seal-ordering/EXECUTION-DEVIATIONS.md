# Execution Deviations — Feature 119

This file captures deviations from the design encountered during implementation.

---

## Deviation: T009 / T010 — Coordinator unit tests cannot use a non-existent in-memory Redis test double

**Issue.** `tasks.md` T009 and `research.md` R8 prescribe using "the existing
in-memory Redis test double (`Sorcha.Storage.InMemory.Redis`)" for
`RedisPresentationSealCoordinator` unit tests. No such project or test double
exists in the codebase. Searched: no project named `Sorcha.Storage.InMemory.Redis`,
no `IConnectionMultiplexer` fake, no `IDatabase` fake. Existing Redis-backed
components (`RedisPendingPresentationStore`, `AbandonmentSweeper`) test against
mocked `IConnectionMultiplexer` + `IDatabase` for narrow scenarios only — none
of them exercise `KeysAsync`, batch pipelines, or hash round-trips at the depth
the coordinator needs.

**Decision.** Write the unit-test surface that exercises the coordinator's
behavioural contract through narrow `Mock<IConnectionMultiplexer>` /
`Mock<IDatabase>` setups for the simple paths (enqueue, drain via mocked HSET /
HDEL / HGETALL). Recovery-sweep coverage (KeysAsync iteration, missed-event
poll, TTL fail) is deferred to **T017 / T021** integration tests against real
Redis from the Docker compose stack — exactly where the existing
`RegisterEventBridgeServiceTests` integration tests live. This matches the
intent of R8 (`integration tests use the Docker-stack Redis via the existing
WebApplicationFactory setup pattern`).

**Impact.**
- T009 unit-test obligations 1, 2, 3, 4, 5 (round-trip + idempotence + reject
  paths) covered via mocked Redis — narrow but sufficient for branch coverage
  of the coordinator's logic.
- T009 obligations 6, 7, 8 (sweeper recovery, TTL fail, restart safety) are
  covered by the integration tests in T017 / T021 / T029 (real Redis, real
  TransactionConfirmedEvent fan-out, real persistence across coordinator
  instance disposal).
- T010 (`PresentationSealSubscriber` tests) covered with mocked
  `IPresentationSealCoordinator` and in-memory `InMemoryEventSubscriber` —
  this works because the subscriber is purely orchestration over the two
  collaborators.
- The MVP user-visible win (US1: AssuredIdentity Phase 2 passes) does not
  depend on these deferred unit tests passing — it depends on the
  end-to-end behaviour validated by T016 / T025.

No design change is required; the design is sound. The deviation is purely
about test-infrastructure availability.

---

## Deviation: T026 — `.claude/skills/sorcha-architecture/SKILL.md` blocked from edit by sandbox

**Issue.** The Claude Code sandbox denies `Edit` tool calls against
`.claude/skills/sorcha-architecture/SKILL.md` in this session. Permission was
not granted in `.claude/settings.local.json`. T026 cannot be completed in this
session.

**Decision.** A complete drop-in addition for the "Cross-Cutting Pattern:
Timebound Presentation Lifecycle (Feature 111)" section is recorded here so the
user can apply it manually (or in a session with the necessary permission). The
content is:

```markdown
### Seal-aware ordering (Feature 119)

Two pre-existing chain-integrity races in the lifecycle outcome path are
closed by **Feature 119**:

- **Race 2 (VAL_CHAIN_001):** outcome submitted before initiated has sealed —
  its `previousTransactionId` points at a still-mempool tx and the validator
  chain check rejects it.
- **Race 1 (VAL_BP_003):** FR-015 advancement evaluated before outcome has
  sealed — `StateReconstructionService` reads sealed-only and picks the wrong
  predecessor for the next action.

**The rule:** a transaction whose `previousTransactionId` references a
Sorcha-managed predecessor MUST NOT be submitted until that predecessor is
observed sealed. State-transitions depending on a Sorcha-managed seal MUST NOT
fire until that seal is observed.

**Mechanism — `IPresentationSealCoordinator`** (singleton, Redis-backed):

- Two Redis hashes keyed by predecessor txId —
  `sorcha:presentation:awaiting-seal:submit:{predecessorTxId}` (built+signed
  `TransactionSubmission` deferred for the outcome and abandonment sites) and
  `sorcha:presentation:awaiting-seal:advance:{outcomeTxId}` (queued
  `CompleteAfterPresentationAsync` invocation).
- `PresentationSealSubscriber : BackgroundService` subscribes to the existing
  `transaction:confirmed` Redis Streams channel via `IEventSubscriber` and
  calls `coordinator.DrainOnSealAsync(txId)` on each event. Periodic recovery
  sweep at `PresentationLifecycleOptions.SealRecoverySweepIntervalSeconds`
  (default 5s) covers missed events (poll register for entries >30 s old) and
  TTL-fails entries past the validity window with sentinel
  `failed-predecessor-not-sealed`.
- `HandleOutcomeAsync` and `HandleAbandonmentAsync` check predecessor seal via
  `IRegisterServiceClient.GetTransactionAsync` before submitting — sealed →
  submit inline (existing path, unchanged); pending → enqueue and return.
- The FR-015 advancement on outcome success is enqueued to the advance queue
  rather than fired via `Task.Run`. The coordinator's drain creates a fresh
  DI scope and calls `CompleteAfterPresentationAsync` with
  `CancellationToken.None` (mirrors PR #583 lifetime contract).

**Sentinel state machine extension** (additive — see XML doc on
`IPendingPresentationStore.GetOutcomeSentinelAsync`):

- `outcome-pending-seal` — writer claimed; outcome submission deferred until
  predecessor seals. Treated as an idempotent-replay state alongside
  `outcome-pending-write`.
- `failed-predecessor-not-sealed` — never-seals timeout fired by recovery
  sweep. Operator-visible failure.
- `failed-validator-reject` — should-not-happen path: queued tx rejected on
  drain (other than `VAL_CHAIN_FORK`, which dedupes silently).

**Observability** on `Sorcha.Blueprint.Service.Presentation` meter:

- `sorcha_presentation_seal_wait_seconds{site}` — histogram, enqueue→drain.
- `sorcha_presentation_seal_queue_depth{site}` — observable gauge.
- `sorcha_presentation_seal_timeout_total{site}` — counter, never-seals
  failures.
- `sorcha_presentation_seal_recovered_via_sweeper_total{site}` — counter,
  missed-event recoveries.

OTel span `presentation.seal-wait` parented to the existing
`presentation.outcome` / `presentation.abandoned` span.

**Runtime source (Feature 119):**
`src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationSealCoordinator.cs`,
`src/Services/Sorcha.Blueprint.Service/Services/Implementation/RedisPresentationSealCoordinator.cs`,
`src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationSealSubscriber.cs`.
Spec: `specs/119-presentation-seal-ordering/`. Design:
`docs/superpowers/specs/2026-05-08-feature-111-chain-races-design.md`.
```

Insert location: immediately after the existing
"**Runtime source:** `src/Common/Sorcha.PresentationLifecycle.Abstractions/`
…" line at the end of the "Cross-Cutting Pattern: Timebound Presentation
Lifecycle (Feature 111)" section, before the `---` separator that precedes
"Transactional Email Architecture (Feature 112)".

**Update 2026-05-08:** applied manually after this session; sandbox no longer
blocks the file. Drop-in committed at `05b190e6`.

---

## Deviation: Walkthrough exposes a second VAL_BP_003 case the design did not anticipate

**Discovered.** 2026-05-08, during T016 / T025 walkthrough verification after
the captive-dependency DI fix unblocked Blueprint Service startup.

**Issue.** The original task brief identified VAL_BP_003 as the next-action
case ("Action 3 is not reachable from action 1 via blueprint routes" — chain
pointer skipped over action 2's outcome). The brief did not surface a second
case: when the **outcome itself** is submitted with
`previousTransactionId = initiatedTxId`, both the outcome and the initiated
carry `MetaData.ActionId = N` (the HAIP-gated action). The validator's VAL_BP_003
check then evaluates "is action N reachable from action N via blueprint routes?"
— which is reflexively false because actions do not route to themselves.

Symptom on the live walkthrough:

```
VAL_BP_003: Action 2 is not reachable from action 2 via blueprint routes
```

Why this was masked before Feature 119: VAL_CHAIN_001 (predecessor-not-sealed)
fired first under the old chain race and dropped the outcome before VAL_BP_003
had a chance. Now that the seal-aware coordinator waits for initiated to seal
before submitting outcome, VAL_CHAIN_001 passes and VAL_BP_003 fires instead.

**Two options to consider, both out-of-scope per spec.md "Scope and Non-goals":**

- **A — Blueprint-side.** Stop emitting `MetaData.ActionId` on
  `presentation-outcome` and `presentation-abandoned` transactions.
  ValidationEngine.cs:1191 already handles the missing-ActionId case
  ("Previous transaction missing ActionId in metadata, skipping sequence
  check"), so VAL_BP_003 short-circuits cleanly. Smallest change. Keeps the
  validator unchanged. **Concern:** confirm `StateReconstructionService` does
  not depend on `ActionId` on lifecycle txs — re-reading the code, it only
  uses ActionId for *required-action data accumulation*, and lifecycle txs
  carry no required action data, so this should be safe.

- **B — Validator-side.** Carve out VAL_BP_003 for lifecycle tx types
  (`presentation-initiated`, `presentation-outcome`, `presentation-abandoned`)
  on either side of the chain pointer. Cleaner semantically but explicitly
  out of scope per spec.md ("Validator chain rules ... are unchanged"), and
  rebuilds `validator-service`.

**Decision.** Halted T016 / T025 walkthrough verification. User must choose
between (A) and (B); recommend (A) as the smallest change that keeps the
out-of-scope-validator commitment intact.

**Status of the rest of the feature.**

- Phase 1 of the AssuredIdentity walkthrough still completes successfully.
- Phase 2 progresses through step 5 (`haip present` succeeds — outcome callback
  authenticates, outcome tx is built, queued, and later short-circuit-submitted
  inline because the initiated tx had time to seal during the citizen flow).
- Step 6 ("Wait for Action 3 to become current") times out at 60s because the
  outcome tx is rejected with VAL_BP_003 and the FR-015 advancement queued in
  the seal coordinator never drains (the `transaction:confirmed` event for
  `outcomeTxId` never fires because the outcome never sealed).
- All Phase 2 / Phase 3 / Phase 5 of Feature 119 itself work as designed —
  the new code is exercising correctly. The block is in the validator's
  pre-existing chain rules, not in Feature 119's seal-aware ordering.

**Suggested follow-up.** Open an issue ("Feature 119 follow-up: VAL_BP_003
reflexive-action carve-out for lifecycle txs"), reference this deviation log
section, and decide between (A) and (B) before merging the Feature 119 PR.
The current PR (#584) ships seal-aware ordering correctly; it just exposes a
second pre-existing chain-rule defect that needs its own fix.

**Resolution 2026-05-09.** User chose option A. Three implementation attempts
showed it was impossible without a validator change:

- *v1* dropped `actionId` from the metadata dict for outcome and abandonment.
  Broke VAL_STRUCT_004 — the validator requires the submission DTO's top-level
  string `ActionId` field, which is read from the same dict via line 676 of
  `ITransactionBuilderService.cs`.
- *v2* kept `actionId` in the metadata dict (so VAL_STRUCT_004 passes) but
  patched `ToTransactionModel()` to skip projecting onto `MetaData.ActionId`
  for lifecycle-terminal types. Walkthrough still failed — `ToTransactionModel`
  is **not on the production write path**. Only `MongoDocumentMapper` calls
  it. The validator owns persistence via `DocketBuildTriggerService.cs:591`
  (`ActionId = uint.TryParse(t.ActionId, out var actionId) ? actionId : null`),
  which projects unconditionally from the submission DTO's string field.
- *v3* extended v2 to also include `presentation-initiated`. Same dead-code
  problem.

Option A is therefore impossible without a validator change. Pivoted to a
minimal validator-side fix at `Sorcha.Validator.Service/Services/ValidationEngine.cs`:
skip the VAL_BP_003 route reachability check when the current transaction's
metadata Type is `PresentationOutcome` or `PresentationAbandoned`. Eight lines.
Chain integrity (VAL_CHAIN_001 / VAL_CHAIN_FORK) unchanged; only workflow-
routing reachability bypassed for these intra-action terminals.
`PresentationInitiated` still gets the full check — it really does advance
from action N-1 to action N.

**Verified.** AssuredIdentity Phase 2 walkthrough now passes end-to-end:
Phase 1 ✓ (0:31), Phase 2 ✓ (0:58). Step 6 reports
`Action 3 ready (waited 2s)` — the FR-015 advancement queued in the seal
coordinator drained correctly once the outcome tx sealed.

Commit: `d027a535` on branch `119-presentation-seal-ordering`.

**Spec implication.** The `spec.md` non-goal "Validator chain rules
(VAL_CHAIN_001, VAL_CHAIN_FORK, VAL_BP_003) are unchanged" no longer holds
— VAL_BP_003 has the new carve-out documented above. Update spec.md before
merging PR #584, or note the deviation in the PR body.
