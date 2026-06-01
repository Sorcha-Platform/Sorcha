# Tasks: Ledger-Derived Workflow Instances

**Input**: Design documents from `/specs/145-ledger-derived-instances/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED. Determinism, idempotency, parity, and routing-validation are the correctness heart of this feature (Constitution IV + quickstart.md); the deterministic core is built test-first.

**Organization**: by user story. MVP = User Story 1 (the consistent cross-node state machine). Foundational carries the shared substrate (the carried `RoutingDecision` + instance identity) that the projection reads.

## Format: `[ID] [P?] [Story] Description`
- **[P]** = parallelizable (different files, no incomplete dependency).
- **[Story]** = US1–US6 from spec.md.
- File paths are repo-relative and grounded in plan.md's "touched areas".

---

## Phase 1: Setup

- [X] T001 [P] Scaffold `RoutingDecision` + `Attestation` types in `src/Common/Sorcha.Register.Models/Transactions/RoutingDecision.cs` + `Attestation.cs` — `[JsonPropertyName]`-stable, canonical-serialisable (per `contracts/routing-decision.md`, data-model Entity 1/2)
- [X] T002 [P] Register new OTel meters `Sorcha.Blueprint.Instances` (projection) and `Sorcha.Blueprint.Reactions` on the ServiceDefaults export allowlist
- [X] T003 [P] Create `scripts/check-ledger-derived-clean-break.ps1` skeleton (patterns stubbed, initially passing) modelled on `scripts/check-trust-clean-break.ps1`
- [X] T004 [P] Add test fixtures area for sealed-docket streams in `tests/Sorcha.Blueprint.Service.Tests/Fixtures/LedgerDerived/` (canonical fixtures the projection tests fold)

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ Blocks all user stories** — the projection (US1) reads the carried decision; the validator (US3) validates it.

- [X] T005 Complete `RoutingDecision{completedActionId, nextActions[], attestation}` + `Attestation.SenderSigned` in `Sorcha.Register.Models`; carry it on `TransactionMetaData` (clear) with canonical serialization; mark the legacy `NextActionId` for removal (compile-guarded)
- [X] T006 [P] Engine: emit the **full** `NextActions` set from routing evaluation in `src/Core/Sorcha.Blueprint.Engine/Routing/` (stop collapsing to a singular next action) — NO-OP: `RoutingEngine.BuildRoutingResult` already emits the full deduped `NextActionIds` set on `RoutingResult.NextActions`; the singular collapse is downstream in `ActionExecutionService` only (addressed by T007)
- [X] T007 Producer: assemble + sender-sign the `RoutingDecision` onto the action tx in `ActionExecutionService.cs` step 10d (~:995). Builds `RoutingDecision{CompletedActionId, NextActions(full set, BranchKey), Attestation.SenderSigned}`, signs `ComputeSignableBytes()` via `SignTransactionAsync`, base64s into `Attestation.Signature`, writes canonical JSON to `transaction.Metadata["routingDecision"]` → rides to the sealed docket via `TrackingData` copy. Legacy `nextActionId` write retained until T024. Builds green.
- [X] T008 [P] Implement deterministic identity `H(registerId, blueprintId, startingActionTxHash)` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InstanceIdentity.cs` (data-model Entity 4)
- [X] T009 [P] Foundational unit tests: `tests/Sorcha.Register.Models.Tests/RoutingDecisionTests.cs` (6 tests — canonical round-trip, camelCase wire-stability, full-set/parallel preservation, attestation-free signable bytes, determinism, terminal empty set — all pass) + `tests/Sorcha.Blueprint.Service.Tests/Services/InstanceIdentityTests.cs` (identity determinism, distinctness, hex format, field-boundary anti-collision, arg validation — all pass). Full-set EMISSION (engine) was T006 no-op; carried-decision full-set is covered here.

**Checkpoint**: a sealed action carries a full, signed `RoutingDecision`; instance ids are deterministic.

---

## Phase 3: User Story 1 - Consistent cross-node state machine (Priority: P1) 🎯 MVP

**Goal**: Every node folds the same sealed transactions into the same instance; any participant acts on any node; the cross-node loop runs autonomously with no mirror.

**Independent Test**: Two-node credential workflow — applicant on the subscriber, approver/agent on the owner; instance control state identical on both nodes after each seal; the approver discovers + acts via pending-actions with no manual step.

### Tests for US1

- [X] T010 [P] [US1] Projection determinism test (same docket stream, varied order + mid-stream restart → identical state) in `tests/Sorcha.Blueprint.Service.Tests/Projection/InstanceProjectionTests.cs` — 10 tests pass: order-independence across all permutations, duplicate-folds-once idempotency, incremental `Apply`==batch `Project` parity, parallel-branch preservation, rejection/completion terminals. Backed by the pure fold `InstanceProjection.cs` (`Project` batch rebuild + `Apply` online watermark fold) — the deterministic core reused by T013 (projector) and T032 (rebuild).
- [ ] T011 [P] [US1] Discovery + cross-node identical-state test (`GetPendingActionsByWalletAsync` surfaces the current action on a non-originating node) in `tests/Sorcha.Blueprint.Service.Tests/Projection/`
- [ ] T012 [P] [US1] Single submission contract test (owner vs subscriber identical; `202` + bounded-wait `200`) in `tests/Sorcha.Blueprint.Service.Tests/Submission/`

### Implementation for US1

- [ ] T013 [US1] Implement `InstanceProjector.cs` (`BackgroundService`, subscribe `docket:confirmed`/`RegisterEventChannels.DocketConfirmed`, pure fold reading `RoutingDecision.nextActions`, idempotent via `lastAppliedTxId`, chain-ordered) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/` — runs on **every** node
- [ ] T014 [US1] Materialized-view writes from the projector in `EfCoreInstanceStore.cs`: add `lastAppliedTxId` watermark, participant-id-keyed bindings, disclosure-scoped `dataView`; remove mirror write methods
- [ ] T015 [US1] Remove `ApplyInstanceStateChanges` imperative mutation from `ActionExecutionService.cs`; the submitter no longer advances instance state
- [X] T016 [US1] Single async submission path in `ActionExecutionService.cs` (+ `EncryptionBackgroundService`): submit = local data-validation (the **202** attests "data-validated + ingressed") + a **carrier-aware** fan-out, and **nothing waits for sealing** — the bounded seal-wait is removed and there is **no `LocallyOwned` branch** (the type, the submitter usage, and the peer return are all removed; `DistributeTransactionResult` is counts-only). Rejected to the consumer only when local data-validation fails AND no carrier accepted (subscriber-safe). Sealing happens wherever roster authority lives (local or a carrier); the projector advances every node on seal; chain ordering is client-gated. (Async sealing-failure feedback to the consumer is a tracked follow-up.)
- [X] T017 [US1] Roster-based sealer selection in `src/Services/Sorcha.Peer.Service/Distribution/TransactionDistributionService.cs`. The Peer service is a separate process that does not own the ledger, so it consults the co-located Register service via the existing `IRegisterServiceClient.GetLocalRelationshipAsync` (Feature 108 `GET /api/registers/{id}/local-relationship`, roster-cached server-side) — NOT a directly-hosted `IRegisterLocalRelationshipService` (which needs `IReadOnlyRegisterRepository` + `ILocalIdentityProvider` the Peer process lacks). `ForwardSubmissionAsync` short-circuits to `LocallyOwned` when the relationship marks this node `IsOwner`/`IsValidator` (its co-located validator seals — no fan-out); a subscriber/unknown relationship falls through to the existing transport (channels → reverse-stream relay → seeds) so behaviour is never worse than before. Also closed **F108 follow-up #1**: `TransactionDistributionGrpcService.SubmitTransaction` now sets `ReceiverIsValidator` honestly from the same relationship lookup (was hard-coded false). Scoped client resolved from the singleton via an optional `IServiceScopeFactory` (the F143 optional-ctor-param pattern; production DI injects it, tests pass null ⇒ heuristic fallback). 4 new roster tests; full Peer.Service suite 708/0. **Topology-heuristic + dual-path REMOVAL is the residual T034 sweep (US5), gated after this lands.** P2P fan-out itself is not unit-provable — the roster *decision* is tested; live fan-out validation rides the cross-node walkthrough.
- [ ] T018 [US1] `POST /api/instances` becomes a local draft (no GUID store row); the starting-action submit returns the canonical ledger-derived `instanceId` in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T019 [US1] Instance + pending-action reads served from the projection (contracts/instance-read.md); emit instance-advanced notification on fold
- [ ] T020 [US1] Delete `InstanceMirrorReconstructor`, `Instance.IsReadOnlyMirror`, `CreateMirrorAsync`/`UpdateMirrorAsync`

**Checkpoint**: the AssuredIdentity cross-node loop runs with identical state on both nodes and autonomous discovery — no mirror (SC-001, SC-002).

---

## Phase 4: User Story 3 - Routing decisions are trusted ledger facts (Priority: P2)

**Goal**: The carried decision is validated at seal and governed; parallel branches preserved.

**Independent Test**: A multi-branch route seals with all branches and every node advances them; a forged/route-inconsistent decision is rejected; a register requiring stronger attestation refuses.

### Tests for US3

- [X] T021 [P] [US3] `VAL_ROUTING_001/002` tests in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineRoutingTests.cs` — 10 tests pass: valid successor passes, parallel branches both preserved, non-successor rejected (`VAL_ROUTING_001`), terminal empty-set passes, completed-action mismatch rejected, forged sig rejected (`VAL_ROUTING_002`), reserved attestation kind rejected, no-decision-carried passes.
- [X] T022 [P] [US3] Governance enforcement test (register `routingAttestation` requiring `validator-reeval` → refused in v1, `VAL_ROUTING_002`) — `ValidateRoutingDecision_GovernanceRequiresStrongerStrength_RefusedInV1` in the same file.

### Implementation for US3

- [X] T023 [US3] `ValidateRoutingDecisionAsync` in `ValidationEngine.cs`: `VAL_ROUTING_001` (every `nextActions[i]` a structural successor of the completed action in the published route graph — `Routes.NextActionIds` ∪ `RejectionConfig.TargetActionId`; terminal `[]` valid; completed-action consistency) + `VAL_ROUTING_002` (governance strength gate, attestation-kind gate, sender-signature verify over `SHA256(ComputeSignableBytes())` against the tx signer via `ICryptoModule.VerifyAsync`). Wired into the main flow (step 4b-iii) behind `EnableRoutingValidation` (default on); skips genesis/control/participant/rejection/intra-action-lifecycle txs and txs carrying no decision.
- [X] T024 [US3] `DocketBuildTriggerService.cs` carries the validated `RoutingDecision` onto the typed sealed `TransactionMetaData.RoutingDecision` (`ResolveRoutingDecision` replaces `ResolveNextActionId`); the singular `NextActionId` seal-write is removed. `InboundTransactionRouter` now derives its wallet-notification hint from `RoutingDecision.NextActions[0]`. Producer-side `nextActionId` string + projector legacy fallback remain until the US5 sweep.
- [X] T025 [US3] `routingAttestation` (typed `AttestationKind?`) added to `RegisterControlRecord` as a sibling of `CryptoPolicy`; the validator reads it via `IGovernanceRosterService.GetCurrentRosterAsync().ControlRecord.RoutingAttestation` (optional ctor dep, defaults `SenderSigned`) and enforces strength (v1 `sender-signed`; `validator-reeval`/`proof` reserved values rejected).

**Checkpoint**: routing decisions are trustworthy, governed, branch-complete (SC-005, SC-007).

---

## Phase 5: User Story 2 - Exactly-once, role-gated side effects (Priority: P2)

**Goal**: Credential mint/deliver + notifications fire once, on the entitled node, safe under replay/restart.

**Independent Test**: Issue a credential; replay the sealed tx + restart the dispatcher → exactly one credential; non-entitled nodes do nothing.

> **⚠️ SCOPE DECISION (Stuart, 2026-06-01): credential mint STAYS INLINE; reactions are notifications/inbox ONLY.** The credential is minted during submit and sealed into the recipient-addressed encrypted disclosure group inside the action tx — it already lives on the immutable, disclosure-controlled, replicated ledger (the DAD model) and is already exactly-once. Moving the mint post-seal would either reopen the "who signs the re-seal" question or deliver out-of-band (losing the ledger-carried security model), and the inline path can't be cross-node live-validated from the dev box. **T028 is DROPPED by design.**

### Tests for US2

- [X] T026 [P] [US2] Reaction idempotency + entitlement test in `tests/Sorcha.Blueprint.Service.Tests/Reactions/ReactionDispatcherTests.cs` (8 tests): entitled-first-time fires once; not-entitled no-op; replay same sealedTx fires once; different sealedTx fires again; ambiguous-assignee no-op; workflow-completed notifies entitled participants once; workflow-completed replay once; workflow-completed not-entitled no-op. Plus AtomicCache contract tests for the SET-NX primitive (3, run across impls).

### Implementation for US2

- [X] T027 [US2] `ReactionDispatcher.cs` (+ `IReactionDispatcher`) — owns notification + durable-inbox side effects, entitlement-gated via `IWalletServiceClient.GetWalletAsync` (null ⇒ wallet not hosted here ⇒ another node reacts), idempotent via `IAtomicDistributedCache.TrySetIfAbsentAsync` (new SET-NX primitive added to the audited cache) on `react:{sealedTxId}:{kind}:{wallet}`. **Invoked in-process by the projector post-fold** (not a separate `BackgroundService`) so it has the freshly-folded instance and there's no projector/reaction race — a deliberate simplification of the spec's BackgroundService shape. The projector's `NotifyAdvancedAsync` is removed → the projector is now pure (state only).
- [~] T028 [US2] **DROPPED by design** — credential mint stays inline (see scope decision above). `ActionExecutionService` credential issuance untouched.
- [X] T029 [US2] `action-available` + `workflow-completed` notification/inbox reactions on the idempotency contract (each fires the existing `INotificationService`, which already does SignalR + durable inbox). `CredentialDeliver`/inbound-detect is N/A — credential delivery rides the existing on-ledger disclosure-group replication, not a reaction.
- [X] T030 [US2] Reaction OTel instruments (`reaction_dispatched_total`, `reaction_idempotent_skip_total`, `reaction_entitlement_skip_total`, tagged by `kind`) on the `Sorcha.Blueprint.Reactions` meter.

**Checkpoint**: workflow notifications/inbox fire exactly once on the entitled node across nodes/replay/restart; credential issuance unchanged (stays inline + on-ledger). **Cross-node live re-test still recommended** (lower risk now mint is untouched — only notification routing changed).

---

## Phase 6: User Story 4 - Verifiable, rebuildable instance state (Priority: P3)

**Goal**: The materialized view is reconstructable from the ledger; divergence is detectable.

**Independent Test**: Rebuild equals the stored view; corrupt/delete the view → rebuild restores it.

### Tests for US4

- [X] T031 [P] [US4] Rebuild-parity test in `tests/Sorcha.Blueprint.Service.Tests/Projection/InstanceRebuildServiceTests.cs` (5 tests): rebuild produces expected control state, no-txs → null, materialized==rebuild → InSync, corrupt view → divergence reported, rebuild-and-persist restores a corrupt view.

### Implementation for US4

- [X] T032 [US4] `InstanceRebuildService.RebuildAsync(registerId, instanceId)` replays the instance's sealed txs (`GetTransactionsByInstanceIdAsync`) through the pure `InstanceProjection.Project` fold. Rather than generalising `StateReconstructionService` (which decrypts *data* state), rebuild reuses the **new shared `InstanceProjectionResolver`** — the single tx→`ProjectedTransaction` resolution now used by BOTH the online `InstanceProjector` and the rebuild, so a rebuild is bit-for-bit identical to the materialized view by construction (the projector's private resolution helpers were extracted into it). Control state needs only the carried `RoutingDecision`, no decryption (FR-010).
- [X] T033 [US4] `CheckParityAsync` (fresh rebuild vs materialized, field-level divergence detail) + `RebuildAndPersistAsync` (operator repair). Internal endpoints `GET /api/internal/instances/{registerId}/{instanceId}/parity` + `POST .../rebuild` (`RequireService` — not a public mutation). The T031 parity test is the CI self-check; a standing periodic sweep across all instances is left as a lightweight follow-up (the per-instance primitive + endpoint are in place).

**Checkpoint**: recovery + integrity invariant (SC-003).

---

## Phase 7: User Story 5 - One submission path; legacy duplication removed (Priority: P3)

**Goal**: Lock in the single path + roster ownership and remove the last legacy residue.

**Independent Test**: Owner-node and subscriber-node submits are identical; the clean-break check finds nothing.

- [X] T034 [US5] (PR #896) Removed the legacy singular `NextActionId` hint end-to-end (producer writes in ActionExecutionService 10c + EncryptionBackgroundService; the `ToTransactionSubmission` whitelist copy; the typed `TransactionMetaData.NextActionId` property; the projector/resolver fallback). **Critically, also COMPLETED US3's dormant routing path**: (A) whitelisted `routingDecision` into `ToTransactionSubmission` (it was being silently dropped → validator never saw it → seal carried none → projector ran on the nextActionId fallback) + (B) made `EncryptionBackgroundService` produce+sign the `RoutingDecision` (encrypted path had none). The carried `RoutingDecision` is now the sole routing source. **Topology-heuristic + dual-path sweep — DONE** (with T016): the Peer `ForwardSubmissionAsync` is now **carrier-aware** — it fans out ONLY to peers that carry the register (direct channels + reverse-stream owners) and the seed/topology fallback is removed, so a non-carrier seed is never dialed (this was hanging the submit → 504). The T017 roster short-circuit/`IRegisterServiceClient` lookup is removed from the fan-out hot path (the grpc `ReceiverIsValidator` keeps its own lookup). **LIVE-VALIDATED (US5 routing)** on a fresh tiny rebuild: sealed action txs show `nextActionId=<absent>`, `RoutingDecision=PRESENT`; full AssuredIdentity loop completed ⇒ VAL_ROUTING runs+passes. (The carrier-aware submit change itself needs a clean single-node re-validate — the spoke-configured tiny's non-carrier seed was the 504.)
- [~] T035 [US5] Owner-vs-subscriber parity = the cross-node walkthrough. US5's routing change validated single-node on tiny; cross-node n1 leg is mechanically identical to US1's proven replication (sealed tx replicates byte-for-byte, n1 projector runs the same fold) — not separately re-run (dev→n1 HTTPS proxy-blocked).

**Checkpoint**: the carried `RoutingDecision` is the only routing source (SC-006, live-proven). Topology-heuristic removal pending T017.

---

## Phase 8: User Story 6 - Presentation lifecycle on the projection (Priority: P3)

**Goal**: Presentation-driven advancement runs through the projection, preserving chain-ordering integrity.

**Independent Test**: A presentation-gated workflow reaches a terminal outcome; the instance advances consistently across nodes with no ordering race.

### Tests for US6

- [ ] T036 [P] [US6] Presentation-advance-on-projection test (consistent across nodes; no ordering regression; `VAL_BP_003` carve-out intact) in `tests/Sorcha.Blueprint.Service.Tests/Presentation/`

### Implementation for US6

- [~] **US6 Increment 1 (DONE, unit-tested)** — `InstanceProjectionResolver` now skips a presentation-lifecycle tx (`TransactionType.IsPresentationLifecycle()` = Initiated/Outcome/Abandoned) that carries no `RoutingDecision`, so the gated action stays current until a successful outcome routes it onward. Also a **latent-bug fix**: today the projector folds these (numeric ActionId is stamped on the sealed tx), retiring a still-current non-terminal presentation action → premature completion + imperative early-exit. 5 resolver tests; suite 851/0. Full 2–3 design (the atomic, live-gated success-advances-via-projector cutover) in `US6-IMPLEMENTATION-PLAN.md`.
- [~] T037 [US6] **Increments 2-3 built + unit-tested (LIVE-GATED for merge).** A SUCCESSFUL `PresentationOutcome` now carries a sender-signed `RoutingDecision` so the projector advances on its seal. New `IPresentationRoutingDecisionBuilder` (implemented by `ActionExecutionService`, which owns the engine routing eval) computes+signs the decision from the real blueprint/instance/draft payload (mirrors `CompleteAfterPresentationAsync`'s routing + step 10d sign; returns null on missing instance / non-current action). `PresentationLifecycleService.HandleOutcomeAsync` attaches it to `built.Metadata["routingDecision"]` before `ToTransactionSubmission` (rides the existing whitelist) on both the inline + F119-deferred paths. **The imperative advance is retired**: the two `EnqueueAdvancementAsync` calls + the legacy `Task.Run → CompleteAfterPresentationAsync` fallback removed; advancement is now the projector's job (ReactionDispatcher fires notifications post-fold). Decline/abandoned carry no decision (projector skips per Increment 1). Updated 2 outcome tests + 2 new (decision-attached on success; builder not called on decline). **`CompleteAfterPresentationAsync`/`ApplyInstanceStateChanges`/`UpdateInstanceAfterExecutionAsync`/`NotifyParticipantsAsync` + the F119 advancement-queue API are KEPT as now-unused** (removal-follows-proven-replacement — delete after live validation; T040 enforces `ApplyInstanceStateChanges` then). **MUST live-validate F111/F127 + Mongo `RoutingDecision=PRESENT` on the sealed success outcome before merge** (the dormant-routing trap). Design: `US6-IMPLEMENTATION-PLAN.md`.
- [~] T038 [US6] F119 outcome-tx **submission** deferral (`EnqueueSubmissionAsync`) retained for predecessor-seal ordering; the **advancement** deferral is subsumed by the seal-ordered projection. `VAL_BP_003` carve-out + F119 idempotency sentinels (`SetOutcomeSentinelAsync`) untouched. Physical deletion of the now-dead advancement API deferred to post-live cleanup.
- [ ] T039 [US6] Migrate presentation consumers off the bespoke advancement path onto the projection

**Checkpoint**: the intricate lifecycle is unified, races preserved-against (FR-018).

---

## Phase 9: Polish & Cross-Cutting

- [~] T040 `scripts/check-ledger-derived-clean-break.ps1` — **`LocallyOwned` (T016/T034, #903) and the singular `NextActionId` (PR #901) now Enforced**, both scoped to code forms with word boundaries so they allow prose + the unrelated UI `IsLocallyOwned` field and the legit Engine `RoutingResult.NextActionId` / wallet-notification `NextActionId`. Combined with the 3 mirror patterns the gate reaches **5/6**; only **`ApplyInstanceStateChanges`** remains gated (after the US6 cleanup deletes the kept-until-proven imperative advance). Already wired into CI (`.github/workflows/ledger-derived-clean-break-gate.yml`).
- [ ] T041 [P] Migrate callers off the synchronous `nextActions`/`issuedCredential` response: walkthroughs + `Sorcha.UI` / `Sorcha.Wallet.Pwa` submit surfaces → subscribe/poll on instance-advanced + credential events (FR-021)
- [ ] T042 [P] Align the `demos/AssuredIdentity/` toolkit: fix the readiness-gate auth (`Get-DemoPublishedBlueprintIds` 401) and confirm autonomous agent discovery now works against the projection
- [ ] T043 Cross-node E2E green run on the standing two-node demo (autonomous loop, no manual approval, identical state) — SC-002, SC-008
- [ ] T044 [P] Docs sync: `docs/reference/API-DOCUMENTATION.md`, `.claude/skills/sorcha-architecture/SKILL.md`, Blueprint/Validator/Register service READMEs, `docs/reference/development-status.md`
- [ ] T045 [P] OTel dashboards + `projection-up-to-head` health check; confirm ≥85% coverage on the new core (Engine routing, projector fold, reactions)

---

## Dependencies & Execution Order

### Phase order
- **Setup (P1)** → **Foundational (P2, blocks all)** → **US1 (P3, MVP)** → **US3 (P4)** → **US2 (P5)** → **US4 (P6)** → **US5 (P7)** → **US6 (P8)** → **Polish (P9)**.

### Story dependencies
- **US1** depends on Foundational (carried `RoutingDecision` + identity). It is the MVP and performs the core removals (mirror, imperative mutation, dual branch).
- **US3** hardens US1's trust (validates the decision US1 already projects). Land right after US1.
- **US2** depends on US1's projection (reactions subscribe to the same seal stream) but is otherwise independent.
- **US4** depends on US1 (rebuild reconstructs the projection).
- **US5** is the formal cleanup after US1's behavioural removals — sequence near the end.
- **US6** depends on US1 (projection) + US3 (decisions on presentation txns); highest-complexity, isolated last.
- **Polish** depends on all; the clean-break gate (T040) runs after US6 so it catches presentation-path residue too.

### Within a story
- Tests before implementation (the deterministic core is TDD).
- Models/seams before services; services before endpoints; removals after their replacements are green.

### Parallel opportunities
- Setup: T001–T004 all [P].
- Foundational: T006, T008, T009 parallel to T005/T007.
- US1 tests T010–T012 parallel; then T013/T014 → T015/T016 → T017/T018/T019 → T020 (removal last).
- Polish: T041, T042, T044, T045 parallel after T040/T043.

---

## Parallel Example: User Story 1

```text
# Tests first (expect fail):
T010 determinism  |  T011 discovery/identical-state  |  T012 single-contract

# Then projection core, then submission unification, then removals:
T013 InstanceProjector → T014 store view → T015 remove imperative → T016 single submit
   → T017 roster sealer | T018 draft creation | T019 reads/notify → T020 delete mirror
```

---

## Implementation Strategy

### MVP (US1)
Setup → Foundational → US1. **STOP and validate**: the cross-node AssuredIdentity loop runs autonomously with identical state on both nodes and no mirror. That alone retires the architecture that caused the recurring friction (SC-001, SC-002).

### Incremental delivery
US1 (consistent state machine) → US3 (trusted decisions) → US2 (exactly-once effects) → US4 (verifiable/rebuildable) → US5 (cleanup + gate) → US6 (presentation) → Polish (gate + caller migration + E2E + docs). Each story is independently testable; the clean-break gate lands last so nothing regresses.

### Notes
- [P] = different files, no incomplete dependency.
- Removals (T015, T020, T024, T034) follow their replacements being green — never delete a path before the projection replaces it.
- The standing demo (`demos/AssuredIdentity/`) is the cross-node acceptance harness throughout.
