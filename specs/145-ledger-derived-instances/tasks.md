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
- [ ] T016 [US1] Single async submission path in `ActionExecutionService.cs`: always `202 {txId,instanceId,accepted}`; bounded-wait `200` via `instance-advanced:{instanceId}` signal; remove the `!LocallyOwned` branch (contracts/submission-response.md)
- [ ] T017 [US1] Roster-based sealer selection in `src/Services/Sorcha.Peer.Service/Distribution/TransactionDistributionService.cs` via `IRegisterLocalRelationshipService` (retire the seeds/topology heuristic)
- [ ] T018 [US1] `POST /api/instances` becomes a local draft (no GUID store row); the starting-action submit returns the canonical ledger-derived `instanceId` in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T019 [US1] Instance + pending-action reads served from the projection (contracts/instance-read.md); emit instance-advanced notification on fold
- [ ] T020 [US1] Delete `InstanceMirrorReconstructor`, `Instance.IsReadOnlyMirror`, `CreateMirrorAsync`/`UpdateMirrorAsync`

**Checkpoint**: the AssuredIdentity cross-node loop runs with identical state on both nodes and autonomous discovery — no mirror (SC-001, SC-002).

---

## Phase 4: User Story 3 - Routing decisions are trusted ledger facts (Priority: P2)

**Goal**: The carried decision is validated at seal and governed; parallel branches preserved.

**Independent Test**: A multi-branch route seals with all branches and every node advances them; a forged/route-inconsistent decision is rejected; a register requiring stronger attestation refuses.

### Tests for US3

- [ ] T021 [P] [US3] `VAL_ROUTING_001/002` tests (forged sig rejected; non-successor rejected; valid passes; parallel branches preserved) in `tests/Sorcha.Validator.Service.Tests/`
- [ ] T022 [P] [US3] Governance enforcement test (register `routingAttestation` requiring `validator-reeval`/`proof` → refused in v1) in `tests/Sorcha.Validator.Service.Tests/`

### Implementation for US3

- [ ] T023 [US3] Implement `VAL_ROUTING_001` (structural successor vs published route graph) + `VAL_ROUTING_002` (attestation verify + required strength) in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T024 [US3] Carry the validated `RoutingDecision` through the seal in `DocketBuildTriggerService.cs` (replace `ResolveNextActionId`); remove the singular `NextActionId` persistence
- [ ] T025 [US3] Add `routingAttestation` to the register control record + read/derive it in `src/Core/Sorcha.Register.Core/Governance/` (sibling of crypto policy); validator enforces strength (v1 `sender-signed`; reserved values rejected)

**Checkpoint**: routing decisions are trustworthy, governed, branch-complete (SC-005, SC-007).

---

## Phase 5: User Story 2 - Exactly-once, role-gated side effects (Priority: P2)

**Goal**: Credential mint/deliver + notifications fire once, on the entitled node, safe under replay/restart.

**Independent Test**: Issue a credential; replay the sealed tx + restart the dispatcher → exactly one credential; non-entitled nodes do nothing.

### Tests for US2

- [ ] T026 [P] [US2] Reaction idempotency + entitlement test (replay/restart → one credential; non-entitled no-op; one notification) in `tests/Sorcha.Blueprint.Service.Tests/Reactions/`

### Implementation for US2

- [ ] T027 [US2] Implement `ReactionDispatcher.cs` (`BackgroundService`, subscribe `docket:confirmed`, entitlement via `IWalletServiceClient.GetWalletAsync` wallet-host probe, idempotent via `Sorcha.AtomicCache` SET-NX on `(sealedTxId, reactionKind)`) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/`
- [ ] T028 [US2] Move credential mint out of `ActionExecutionService` inline path into a `CredentialMint` reaction (reuse holder-key-bound, encrypt-to-recipient issuance)
- [ ] T029 [US2] `CredentialDeliver`/inbound-detect + `Notification`/`InboxWrite` reactions keyed on the same idempotency contract (contracts/reactions.md)
- [ ] T030 [US2] Wire reaction OTel instruments (`reaction_dispatched_total`, `reaction_idempotent_skip_total`, `reaction_entitlement_skip_total`)

**Checkpoint**: no double-issue across nodes/replay/restart (SC-004).

---

## Phase 6: User Story 4 - Verifiable, rebuildable instance state (Priority: P3)

**Goal**: The materialized view is reconstructable from the ledger; divergence is detectable.

**Independent Test**: Rebuild equals the stored view; corrupt/delete the view → rebuild restores it.

### Tests for US4

- [ ] T031 [P] [US4] Rebuild-parity test (`RebuildAsync == materialized`; corrupt view → restored) in `tests/Sorcha.Blueprint.Service.Tests/Projection/`

### Implementation for US4

- [ ] T032 [US4] Implement `RebuildAsync(instanceId)` by generalising `StateReconstructionService` to fold control state (not just data) from the instance's sealed txs
- [ ] T033 [US4] Periodic/CI parity self-check + an operator-triggered rebuild operation (internal, not a public mutation)

**Checkpoint**: recovery + integrity invariant (SC-003).

---

## Phase 7: User Story 5 - One submission path; legacy duplication removed (Priority: P3)

**Goal**: Lock in the single path + roster ownership and remove the last legacy residue.

**Independent Test**: Owner-node and subscriber-node submits are identical; the clean-break check finds nothing.

- [ ] T034 [US5] Sweep + delete residual references to the topology heuristic, the dual-path branch, and the `NextActionId` hint across Blueprint/Validator/Peer; remove now-dead code
- [ ] T035 [US5] Owner-vs-subscriber parity integration test (same response contract + same projected state) in `tests/Sorcha.Blueprint.Service.Tests/Submission/`

**Checkpoint**: the model is the only model (SC-006).

---

## Phase 8: User Story 6 - Presentation lifecycle on the projection (Priority: P3)

**Goal**: Presentation-driven advancement runs through the projection, preserving chain-ordering integrity.

**Independent Test**: A presentation-gated workflow reaches a terminal outcome; the instance advances consistently across nodes with no ordering race.

### Tests for US6

- [ ] T036 [P] [US6] Presentation-advance-on-projection test (consistent across nodes; no ordering regression; `VAL_BP_003` carve-out intact) in `tests/Sorcha.Blueprint.Service.Tests/Presentation/`

### Implementation for US6

- [ ] T037 [US6] `PresentationOutcome`/`PresentationAbandoned` carry a `RoutingDecision`; the projector advances on their seal (F111/F119 path)
- [ ] T038 [US6] Subsume the F119 `IPresentationSealCoordinator` ordering where the seal-ordered projection now guarantees it; retain the `VAL_BP_003` carve-out + remaining F119 idempotency sentinels
- [ ] T039 [US6] Migrate presentation consumers off the bespoke advancement path onto the projection

**Checkpoint**: the intricate lifecycle is unified, races preserved-against (FR-018).

---

## Phase 9: Polish & Cross-Cutting

- [ ] T040 Complete `scripts/check-ledger-derived-clean-break.ps1` (forbids `InstanceMirrorReconstructor`, `IsReadOnlyMirror`, `Create/UpdateMirrorAsync`, `ApplyInstanceStateChanges`, the `LocallyOwned` branch, `NextActionId` hint, topology heuristic) + wire into CI — runs after US6
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
