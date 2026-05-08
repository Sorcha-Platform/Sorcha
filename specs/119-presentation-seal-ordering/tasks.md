---
description: "Task list for Feature 119 — Presentation Lifecycle Chain-Race Resolution via Seal-Aware Ordering"
---

# Tasks: Presentation Lifecycle Chain-Race Resolution via Seal-Aware Ordering

**Input**: Design documents from `/specs/119-presentation-seal-ordering/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
**Tests**: Included — Constitution principle IV mandates ≥85% coverage on new code, and the spec's success criteria explicitly require unit, integration, and walkthrough layers.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Single-project modification within `Sorcha.Blueprint.Service`. New code under `src/Services/Sorcha.Blueprint.Service/`. New tests under `tests/Sorcha.Blueprint.Service.Tests/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Tiny — config knob + sentinel state extension + metrics extension. No new projects, no scaffolding.

- [ ] T001 Add `SealRecoverySweepIntervalSeconds` (int, default 5) to `PresentationLifecycleOptions` in `src/Services/Sorcha.Blueprint.Service/Configuration/PresentationLifecycleOptions.cs`. Wire from `appsettings.json` `PresentationLifecycle:SealRecoverySweepIntervalSeconds`.
- [ ] T002 [P] Extend `IPendingPresentationStore` in `src/Services/Sorcha.Blueprint.Service/Storage/Presentations/IPendingPresentationStore.cs` with sentinel state-machine helpers for the three new values (`outcome-pending-seal`, `failed-predecessor-not-sealed`, `failed-validator-reject`) — extend the existing `TryClaimOutcomeSentinelAsync`/`SetOutcomeSentinelAsync`/`GetOutcomeSentinelAsync` semantics; no new methods.
- [ ] T003 [P] Extend `PresentationLifecycleMetrics` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleMetrics.cs` with the four new instruments per `contracts/presentation-seal-coordinator.cs.md` Observability contract: histogram `sorcha_presentation_seal_wait_seconds`, observable gauge `sorcha_presentation_seal_queue_depth`, counters `sorcha_presentation_seal_timeout_total` and `sorcha_presentation_seal_recovered_via_sweeper_total`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared seal-coordinator + subscriber substrate. All three user stories depend on this phase.

**⚠️ CRITICAL**: No user-story work can begin until Phase 2 is complete. The coordinator is the single coordination point all three stories use.

- [ ] T004 Create `IPresentationSealCoordinator` interface in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationSealCoordinator.cs` per `contracts/presentation-seal-coordinator.cs.md` — methods `EnqueueSubmissionAsync`, `EnqueueAdvancementAsync`, `DrainOnSealAsync`, `RunRecoverySweepAsync`. Full XML doc comments.
- [ ] T005 [P] Add envelope records and enum (`SealAwaitingSubmission`, `SealAwaitingAdvancement`, `SealAwaitingSubmissionSite`, `SweepResult`) in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IPresentationSealCoordinator.cs` (same file as T004).
- [ ] T006 Implement `RedisPresentationSealCoordinator` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/RedisPresentationSealCoordinator.cs`. Two Redis hashes per data-model.md §1.1 / §1.2. Use `IDistributedCache` if it exposes hash ops, else `IConnectionMultiplexer` directly (match the pattern used by `IPendingPresentationStore`). Inject `IValidatorServiceClient`, `IRegisterServiceClient`, `IServiceScopeFactory`, `IPendingPresentationStore`, `PresentationLifecycleMetrics`, `IClock`, `ILogger`. (Depends on T002, T003, T004, T005.)
- [ ] T007 Implement `PresentationSealSubscriber : BackgroundService` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationSealSubscriber.cs`. Subscribe to `transaction:confirmed` via `IEventSubscriber`; on each event call `_coordinator.DrainOnSealAsync(txId)`. Periodic sweep at `SealRecoverySweepIntervalSeconds` calls `_coordinator.RunRecoverySweepAsync()`. (Depends on T006.)
- [ ] T008 Register `IPresentationSealCoordinator` (singleton) and `PresentationSealSubscriber` (hosted service) in `src/Services/Sorcha.Blueprint.Service/Program.cs` alongside the existing presentation lifecycle registrations. (Depends on T006, T007.)
- [ ] T009 [P] Unit tests for `RedisPresentationSealCoordinator` in `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationSealCoordinatorTests.cs` — eight test obligations from `contracts/presentation-seal-coordinator.cs.md` Test contract section: enqueue+drain submission round-trip, enqueue+drain advancement round-trip, drain idempotence, validator-reject path, VAL_CHAIN_FORK dedup, missed-event recovery, TTL-fail, restart safety. Use `Sorcha.Storage.InMemory.Redis` test double + injected `IClock`.
- [ ] T010 [P] Unit tests for `PresentationSealSubscriber` in `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationSealSubscriberTests.cs` — verifies event-handler wiring, sweep-cadence ticking, graceful shutdown.

**Checkpoint**: Coordinator + subscriber + foundational tests green. User stories can now proceed in parallel.

---

## Phase 3: User Story 1 — Fast-citizen presentation completes reliably (Priority: P1) 🎯 MVP

**Goal**: Race 1 (VAL_BP_003 — workflow-advancement-before-outcome-seal) and Race 2 (VAL_CHAIN_001 — outcome-submit-before-initiated-seal) closed via the coordinator from Phase 2. AssuredIdentity Phase 2 step 7 starts passing.

**Independent Test**: Run `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` against a fresh local Docker stack. Phase 2 step 7 must complete on the first attempt with no `VAL_CHAIN_001` or `VAL_BP_003` in Blueprint Service logs.

- [ ] T011 [US1] Modify `HandleOutcomeAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs` (lines ~328-362): after build+sign, call `_registerClient.GetTransactionAsync(pending.InitiatedTransactionId)` to check seal; if sealed → submit inline (existing path); if not sealed → call `_sealCoordinator.EnqueueSubmissionAsync(...)` with site=`Outcome`, set sentinel to `outcome-pending-seal`, return `IsIdempotentReplay=false, OutcomeTransactionId=string.Empty` (or a deferred placeholder).
- [ ] T012 [US1] Replace the FR-015 advancement `Task.Run` block in `HandleOutcomeAsync` (the post-submit success-kind branch added by PR #583) with `await _sealCoordinator.EnqueueAdvancementAsync(new SealAwaitingAdvancement(...))`. Keep the early-return when `outcome.Kind != PresentationOutcomeKind.Success`. (Same file as T011.)
- [ ] T013 [US1] Update the idempotent-replay logic in `HandleOutcomeAsync` (lines ~262-275) to recognise `outcome-pending-seal` as a "writer claimed; deduplicate" state — extend the `string.Equals(existingSentinel, "...", ...)` chain. (Same file as T011/T012.)
- [ ] T014 [P] [US1] Add unit tests to `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceOutcomeTests.cs`: "predecessor sealed → submits inline" and "predecessor pending → enqueues, sentinel=outcome-pending-seal, returns deferred reply." Mock `_registerClient.GetTransactionAsync` and `_sealCoordinator`.
- [ ] T015 [P] [US1] Add unit tests to `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceOutcomeTests.cs`: "FR-015 advancement enqueues to advance queue with correct envelope" — verifies T012's wiring.
- [ ] T016 [US1] Smoke test: run `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` once after rebuilding `blueprint-service` (`docker compose build --no-cache blueprint-service && docker compose up -d --force-recreate --no-deps blueprint-service`). Phase 2 step 7 must pass. (Depends on T011-T015 and Phase 2.)

---

## Phase 4: User Story 2 — Operators can see and bound never-completing presentations (Priority: P2)

**Goal**: Verify the observability surface and the timeout/recovery paths fire as designed. Most of the implementation is already in Phase 2 (the coordinator itself emits metrics and runs the sweep); this phase is largely test coverage of operator-visible behaviour.

**Independent Test**: Inject a forced consensus-rejection on an outcome submission. Within one validity window, verify the failure is recorded with sentinel `failed-predecessor-not-sealed`, the timeout counter increments by exactly one, and a `LogError` entry appears with the presentation request id and predecessor tx id.

- [ ] T017 [P] [US2] Integration test in `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationSealCoordinatorIntegrationTests.cs`: `Metrics_FireOnNormalDrain` — drives a successful enqueue + drain via real Redis (Docker compose-up Redis or in-memory equivalent), asserts the four meter instruments produce non-zero values with correct labels.
- [ ] T018 [P] [US2] Add log-shape assertion to `PresentationSealCoordinatorTests.cs`: forced never-seal entry past TTL produces a `LogError` event with the literal sentinel value `failed-predecessor-not-sealed` and structured fields `presentationRequestId` and `predecessorTxId`. Use `ILogger<RedisPresentationSealCoordinator>` test double.
- [ ] T019 [P] [US2] Sweeper-recovery test in `PresentationSealCoordinatorTests.cs`: enqueue an entry, advance the injected `IClock` past the 30 s missed-event threshold, configure the mock `IRegisterServiceClient.GetTransactionAsync` to return a sealed tx, run `RunRecoverySweepAsync()`, assert the entry was drained and `sorcha_presentation_seal_recovered_via_sweeper_total{site}` incremented by one.
- [ ] T020 [P] [US2] Timeout-fail test in `PresentationSealCoordinatorTests.cs`: enqueue an entry, advance the injected `IClock` past the validity window, run `RunRecoverySweepAsync()`, assert sentinel transitioned to `failed-predecessor-not-sealed`, the timeout counter incremented, and `LogError` fired.
- [ ] T021 [US2] Integration test in `PresentationSealCoordinatorIntegrationTests.cs`: `RealRedisStreams_PublishConfirmedEvent_DrainsQueue` — publishes a real `transaction:confirmed` event via the same `IEventPublisher` the Register Service uses, asserts the subscriber drains the queue end-to-end. Mirrors the integration-test shape of `RegisterEventBridgeServiceTests`.

---

## Phase 5: User Story 3 — Abandonment records also wait for predecessor seal (Priority: P3)

**Goal**: Apply the same coordinator pattern to `HandleAbandonmentAsync` for consistency with US1. Closes the latent variant of the chain race in the abandonment path.

**Independent Test**: Configure a blueprint with a 30 s validity window and `recordAbandonment: true`. Initiate a presentation but do not complete it. The abandonment record seals correctly without `VAL_CHAIN_001`, regardless of whether the start record was sealed at sweeper-fire time.

- [ ] T022 [US3] Modify `HandleAbandonmentAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/PresentationLifecycleService.cs` (lines ~419-532): after build+sign of the abandonment tx, check `_registerClient.GetTransactionAsync(pending.InitiatedTransactionId)`; if sealed → submit inline (existing path); if not sealed → call `_sealCoordinator.EnqueueSubmissionAsync(...)` with site=`Abandonment`. Keep the existing sentinel rollback-on-validator-reject path (lines 503-525) for the inline branch.
- [ ] T023 [P] [US3] Add unit tests to `tests/Sorcha.Blueprint.Service.Tests/Services/PresentationLifecycleServiceAbandonmentTests.cs`: "predecessor sealed → submits inline" and "predecessor pending → enqueues with site=Abandonment." Mock `_registerClient.GetTransactionAsync` and `_sealCoordinator`.
- [ ] T024 [US3] Verify the existing sentinel-rollback path still functions when the validator rejects the abandonment tx after queue-drain (not just inline). Add a test case to `PresentationSealCoordinatorTests.cs` exercising the abandonment-site path through `DrainOnSealAsync`'s validator-reject branch.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation propagation, formal SC validation, and issue cleanup.

- [ ] T025 Run `walkthroughs/AssuredIdentity/run.ps1 -Profile gateway` ten consecutive times against a fresh Docker stack per `quickstart.md` Step 2. All ten must pass. This is the formal SC-119-001 verification.
- [ ] T026 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` "Cross-Cutting Pattern: Timebound Presentation Lifecycle (Feature 111)" section to describe the seal-aware ordering rule and the new `IPresentationSealCoordinator` surface. Reference both the design doc and Feature 119 spec.
- [ ] T027 [P] Add R10 "Seal-aware ordering of chain-bearing lifecycle txs" section to `specs/111-presentation-lifecycle/research.md` summarising the decision and linking to the design doc + Feature 119.
- [ ] T028 [P] Update `specs/111-presentation-lifecycle/data-model.md` §1.4 ("Transaction ordering and chain linkage") to clarify that chain-pointer-bearing lifecycle txs use seal-aware submission ordering — without redefining the chain semantics themselves.
- [ ] T029 [P] Add restart-safety integration test `PresentationSealCoordinatorIntegrationTests.RestartSafety_DrainsAfterReconnect` (SC-119-007): enqueue entries on coordinator instance A, dispose A, instantiate coordinator instance B against the same Redis, verify B's `RunRecoverySweepAsync` drains all of A's pending entries.
- [ ] T030 Close GitHub issue #582 with a summary linking Feature 119 PR + design doc + spec, and update `MEMORY.md > Current Branch` to reflect Feature 119 status.

---

## Dependencies

```
Phase 1 (T001-T003) ──┐
                      ├─→ Phase 2 (T004-T010) ──→ checkpoint
                      │                              ├─→ Phase 3 US1 (T011-T016)
                      │                              ├─→ Phase 4 US2 (T017-T021)
                      │                              └─→ Phase 5 US3 (T022-T024)
                      │                                      └─→ Phase 6 Polish (T025-T030)
```

- **Phase 1 → Phase 2**: T002, T003 are inputs to T006.
- **Phase 2 → Phases 3/4/5**: All user stories depend on the coordinator (T006), subscriber (T007), and DI registration (T008).
- **Phases 3/4/5 are independent of each other** and can be implemented in parallel by different agents/sessions.
- **Phase 6 → All prior**: documentation propagation and SC validation come last.

### Within-phase dependencies

- T006 depends on T002, T003, T004, T005.
- T007 depends on T006.
- T008 depends on T006, T007.
- T009 depends on T006.
- T011, T012, T013 are sequential edits to the same file (`PresentationLifecycleService.cs` — same `HandleOutcomeAsync` method); cannot be parallelised. T014, T015 can run after T011/T012/T013.
- T016 depends on T011-T015 + Phase 2.
- T022 is in a different method (`HandleAbandonmentAsync`) of the same file; can be in flight while Phase 3 commits in batches, but mind merge conflicts — recommend completing Phase 3 first or rebasing.
- T025 depends on all prior phases (it's the formal user-visible verification).

---

## Parallel Execution Examples

### Phase 1 — three [P] tasks
All three setup tasks edit different files and can run in parallel:
- T001 (`PresentationLifecycleOptions.cs`)
- T002 (`IPendingPresentationStore.cs`)
- T003 (`PresentationLifecycleMetrics.cs`)

### Phase 2 — partial parallelism
- T004 + T005 (same file) sequential.
- T009, T010 [P] in parallel after T006/T007 land.
- T006 (depends on T002, T003, T004, T005) is the long pole.

### Phase 4 (US2) — fully parallel
T017, T018, T019, T020 are all in different test classes / file regions — fire in one parallel batch. T021 depends on the integration test fixture set up by T017.

### Phase 6 — fully parallel
T026, T027, T028, T029 [P] in one batch (different files). T025 (walkthrough run) is the only sequential gate. T030 (issue close) at the very end.

---

## Implementation Strategy

**MVP scope = Phase 1 + Phase 2 + Phase 3 (US1).** This delivers the SC-119-001 / SC-119-002 / SC-119-003 user-visible win — fast-citizen presentations stop failing roughly half the time. Everything from US2/US3/Polish can be appended in subsequent commits within the same PR or as follow-up commits.

**Recommended order**:

1. Setup + Foundational (Phase 1 + 2) → MVP scaffolding ready.
2. US1 (Phase 3) → smoke test green → MVP shippable here.
3. US2 (Phase 4) → operator confidence + observability formally tested.
4. US3 (Phase 5) → consistency for the latent abandonment path.
5. Polish (Phase 6) → 10× walkthrough verification + doc propagation + issue cleanup.

**Single-PR deliverable**: all six phases land together in one PR off `master` branched as `119-presentation-seal-ordering` (already created). Required gate: `Run discoverability checks`. `build-and-test` is a known flake (issue #511); rerun `--failed` if needed.

**Total tasks**: 30. Per-story counts: Phase 1 = 3, Phase 2 = 7, US1 = 6, US2 = 5, US3 = 3, Polish = 6.

**Independent test criteria summary**:

| Story | Independent Test |
|---|---|
| US1 | AssuredIdentity Phase 2 step 7 passes against fresh stack (T016 smoke; T025 formal 10×) |
| US2 | Forced never-seal triggers `failed-predecessor-not-sealed` sentinel + counter + log inside one validity window (T020) |
| US3 | Short-window blueprint with `recordAbandonment: true` and never-completed presentation seals abandonment correctly (T023) |
