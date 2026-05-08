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
