# Tasks: Real register governance (Feature 189)

**Branch**: `189-org-signed-governance` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

**Design inputs**: [research.md](./research.md) · [data-model.md](./data-model.md) ·
[contracts/governance-endpoints.md](./contracts/governance-endpoints.md) · [quickstart.md](./quickstart.md)

## Before you start — read this

Two rules govern every task below, both learned the expensive way on 2026-08-06:

1. **A green suite is not evidence.** Every defect this feature fixes was invisible to ~2,500
   passing tests. The mock validator in unit tests accepts anything, which is precisely how a
   missing `BlueprintId` survived to a live run. Tasks marked **🔴 LIVE GATE** are not optional.
2. **Test on a register whose genesis has SEALED (`height >= 1`).** `RightsEnforcementService`
   admits any control transaction while `roster == null`. A governance test on a just-created
   register passes without exercising enforcement at all — this already happened once.

Tests are **required** for this feature (SC-009 plus the ten contract tests in contracts/).

---

## Phase 1: Setup

- [ ] T001 Confirm the branch builds clean and the two prerequisite DevMode commits are present (`git log --oneline master..HEAD`) — commits `55fbc7dd` and `58af4f8b` must be in the branch before any governance work
- [ ] T002 Run the full baseline so later failures are attributable: `dotnet build Sorcha.sln`, then Register.Service, Register.Core, Validator.Service and ApiGateway test projects; record the pass counts in the PR description

---

## Phase 2: Foundational (BLOCKING — no user story can start until these land)

**These are blocking because they change the meaning of a roster key. Landing any of them alone
produces registers whose rosters disagree about which key is authoritative.**

- [ ] T003 Document slot 100 in `src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs` as the organisation **governance key** — record that it was previously declared-but-unreferenced, that the roster records whatever key signs the attestation, and that signing with any other key is refused "submitter not found in roster"
- [ ] T004 Add a canonical public-key comparison helper (decode padded/unpadded base64 and base64url to bytes, fixed-time compare) in `src/Common/Sorcha.Register.Models/` — this is the R-003 fix and is used by every roster match
- [ ] T005 [P] Unit-test the key helper across all four encodings and a non-match, in `tests/Sorcha.Register.Models.Tests/`
- [ ] T006 Move attestation signing to slot 100 in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [ ] T007 Move attestation signing to slot 100 in `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` (the offline genesis ceremony — this is what makes US4 possible later)
- [ ] T008 Move attestation signing to slot 100 in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/SandboxRegisterProvider.cs` (replace the explicit `derivationPath: null`)
- [ ] T009 Move attestation signing to slot 100 in `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` (`New-SorchaRegister` — the `/sign` call currently passes no derivation path at all)
- [ ] T010 Verify T006–T009 are exhaustive: grep for attestation signing call sites and confirm none still signs with the primary key
- [ ] T011 🔴 **LIVE GATE** Create a register on n1 with the updated code and confirm its roster records a **slot-100** key (compare against the wallet's primary public key — they must differ). Everything downstream is meaningless if this is wrong.

**Checkpoint**: rosters now carry a dedicated governance key. User stories may begin.

---

## Phase 3: User Story 1 — A single organisation governs its register (P1) 🎯 MVP

**Goal**: an organisation changes a governed setting on a register it owns; the change seals to the
ledger and takes effect on every node.

**Independent test**: create a single-owner register, let genesis seal, promote DevMode→Normal,
confirm sealed-in-docket and observable on tiny.

### Tests for US1

- [ ] T012 [P] [US1] Contract test: a governance tx signed by the **node system wallet** is refused — the node is never on a roster — in `tests/Sorcha.Validator.Service.Tests/Services/`
- [ ] T013 [P] [US1] Contract test: a governance tx signed by an **organisation's slot-100 key** is accepted, in `tests/Sorcha.Validator.Service.Tests/Services/`
- [ ] T014 [P] [US1] Regression test for R-003: roster key stored as **padded base64**, signature key supplied as bytes → matches (fails on master), in `tests/Sorcha.Validator.Service.Tests/Services/`
- [ ] T015 [P] [US1] Contract test: `/propose`-shaped tx (`Metadata["Type"]=="Control"`) **is** detected as governance and roster-enforced (fails on master — currently bypasses), in `tests/Sorcha.Validator.Service.Tests/Services/`
- [ ] T016 [P] [US1] Contract test: two signatures where one is not on the roster → only the roster member counts toward the requirement
- [ ] T017 [P] [US1] Contract test: a governance tx carrying `BlueprintId == "genesis"` is rejected (R-005 guard against genesis misclassification)
- [ ] T018 [P] [US1] Test that the `roster == null` allowance admits **only** a roster-creating transaction (R-002), in `tests/Sorcha.Validator.Service.Tests/Services/`

### Implementation for US1

- [ ] T019 [US1] Create `GovernanceSigningService` in `src/Services/Sorcha.Register.Service/Services/` — resolve the register's roster, select the caller's organisation attestation, parse the wallet address from `Subject` (`did:sorcha:w:{address}`), and sign via `IWalletServiceClient.SignTransactionAsync(address, hash, SorchaDerivationPaths.RegisterAttestation, isPreHashed: true)`
- [ ] T020 [US1] Unit-test `GovernanceSigningService`: correct derivation path, correct wallet resolved from `Subject`, and a clear failure when the caller's organisation is not on the roster
- [ ] T021 [US1] Switch `CryptoPolicyService.SubmitPolicyUpdateAsync` from `ISystemWalletSigningService` to `GovernanceSigningService` in `src/Services/Sorcha.Register.Service/Services/CryptoPolicyService.cs`; leave the node system wallet in place for genesis/docket duties
- [ ] T022 [US1] Update `tests/Sorcha.Register.Service.Tests/Unit/CryptoPolicyServiceTests.cs` for the new signer, keeping the existing mutation-verified assertions (ActionId, metadata keys, payload hash, base64url, one-way guard, non-genesis BlueprintId)
- [ ] T023 [US1] `RightsEnforcementService`: replace the `Signatures[0]` string comparison with the T004 byte helper over **every** signature, counting **distinct** roster members satisfied, in `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs`
- [ ] T024 [US1] `RightsEnforcementService.IsGovernanceTransaction`: key on `Metadata["Type"] == "Control"` plus the governance `BlueprintId`, preserving the `BlueprintPublish` carve-out (#917); remove the `Metadata["transactionType"] == "Control"` arm that never matched
- [ ] T025 [US1] Narrow the `roster == null` allowance to roster-creating transactions only (R-002)
- [ ] T026 [US1] Give `/propose` a non-empty `BlueprintId` (`register-governance-v1`) in `src/Services/Sorcha.Register.Service/Program.cs` — **must land with T024**, since fixing TX_003 alone opens the bypass
- [ ] T027 [US1] Add a refusal code for "insufficient distinct roster signatures" and ensure every refusal carries an administrator-actionable reason (SC-008)
- [ ] T028 [US1] Add governance metrics (proposal/approval/refusal counters, quorum outcome) on the existing meter, no subject data
- [ ] T029 [US1] Mutation-verify T012–T018: reinstate each defect in turn and confirm the matching test goes red. A guard written after the fix has never run red.
- [ ] T030 [US1] 🔴 **LIVE GATE** On n1, against a register with `height >= 1`: promote DevMode→Normal, confirm the tx id appears in a sealed docket's `TransactionIds` (`State: 4`), and confirm `devMode == false`
- [ ] T031 [US1] 🔴 **LIVE GATE** Confirm the promotion replicated to tiny (`_id`, not `Id` — see quickstart trap)
- [ ] T032 [US1] 🔴 **LIVE GATE** Negative: attempt the same change as a non-roster organisation → refused, and **nothing** written to the ledger

**Checkpoint**: US1 is independently shippable. DevMode promotion works; the original cross-node encryption task is unblocked.

---

## Phase 4: User Story 2 — A consortium jointly governs a shared register (P2)

**Goal**: a change takes effect only when the register's approval rule is satisfied by stakeholder organisations.

**Independent test**: three-owner register under `Unanimous`; not enacted at 2 of 3, enacted at 3 of 3.

### Tests for US2

- [ ] T033 [P] [US2] Test: approval from an organisation not on the proposal's roster snapshot is ignored, not counted
- [ ] T034 [P] [US2] Test: a repeat approval by the same organisation leaves the count unchanged
- [ ] T035 [P] [US2] Test: roster change with a proposal open → `Invalidated`, reason `roster-changed`, not enactable thereafter
- [ ] T036 [P] [US2] Test **SC-010**: under `Unanimous` with one approver outstanding, removing that approver **invalidates** rather than enacts — the security property of the whole feature
- [ ] T037 [P] [US2] Test: `Transfer` never takes the Owner override, even for a sole owner (FR-010)
- [ ] T038 [P] [US2] Test: an expired proposal is not enactable by a late approval (FR-012)

### Implementation for US2

- [ ] T039 [US2] Add `CryptoPolicyUpdate` to `GovernanceOperationType` in `src/Common/Sorcha.Register.Models/GovernanceModels.cs` (FR-021)
- [ ] T040 [US2] Add `RosterSnapshotId` and `QuorumFormulaAtRaise` to the proposal shape, captured at raise time from `GovernanceRoster.LastControlTxId` and the register's configured rule (FR-011a)
- [ ] T041 [US2] Implement approvals as **ledger transactions** signed with the approving organisation's slot-100 key (R-009 — not a service-side table; a store, if any, is a rebuildable index)
- [ ] T042 [US2] Implement invalidation as a comparison at count time — current `LastControlTxId` ≠ proposal's `RosterSnapshotId` ⇒ invalid. No timer, no sweeper, deterministic on every node
- [ ] T043 [US2] Record every terminal outcome with a reason (`quorum-met` / `expired` / `roster-changed` / `withdrawn` / `refused-not-on-roster`) — never a silent drop (FR-011c)
- [ ] T044 [US2] Wire quorum evaluation to the existing `GovernanceRosterService.ValidateQuorumAsync` over sealed approval transactions — do **not** reimplement the arithmetic (R-007)
- [ ] T045 [US2] Implement `POST /governance/proposals/{proposalId}/approve` per contracts — `202` submitted, `403` not on snapshot, `409` not open, idempotent repeat
- [ ] T046 [US2] Implement `GET /governance/proposals` (status filter) and `GET /governance/proposals/{proposalId}` (full audit detail)
- [ ] T047 [US2] Enforce FR-024 — a governance change may never leave a register with no organisation able to govern it
- [ ] T048 [US2] 🔴 **LIVE GATE** Three-organisation register under `Unanimous` on n1: not enacted at 2 of 3; enacted and sealed at 3 of 3; replicated to tiny
- [ ] T049 [US2] 🔴 **LIVE GATE** SC-010 live: remove the sole outstanding approver → proposal `Invalidated`, **not** enacted

**Checkpoint**: consortium governance works and cannot be subverted by roster manipulation.

---

## Phase 5: User Story 3 — The governance process is visible and auditable (P3)

**Goal**: the published governance definition is what actually executes, and the trail is on the ledger.

**Independent test**: complete a multi-party change, then reconstruct proposer, approvers and outcome from the ledger alone.

### Tests for US3

- [ ] T050 [P] [US3] Test: the revised `register-governance-v1` expresses quorum from the register's configured rule, not a hardcoded percentage
- [ ] T051 [P] [US3] Test: proposal and approval payloads validate against the blueprint's new `dataSchemas`
- [ ] T052 [P] [US3] Test: the recorded action sequence matches the published definition (FR-018)

### Implementation for US3

- [ ] T053 [US3] Revise `blueprints/templates/register-governance-v1.json`: replace the hardcoded `approvalPercentage >= 50.01` with the register's configured rule; add the crypto-policy operation; add `dataSchemas` for proposal and approval payloads; make "Accept Role" conditional on operation type (R-008)
- [ ] T054 [US3] Execute the governance blueprint as a real workflow instance, with each organisation's approval submitted as an action
- [ ] T055 [US3] Ensure the governance instance folds correctly under F145's `InstanceProjector` — quorum must be a pure function of sealed content so every node agrees (R-009)
- [ ] T056 [US3] Ensure each proposal, approval and enactment is individually attributable to its organisation in the ledger record (FR-019)
- [ ] T057 [US3] 🔴 **LIVE GATE** Reconstruct a completed multi-party change from the ledger alone on both n1 and tiny, and diff the sequence against the published blueprint

**Checkpoint**: governance is dogfooded — the platform governs itself with its own workflow engine.

---

## Phase 6: User Story 4 — System register ownership transfer (P4)

**Goal**: the acceptance test for the whole feature.

**Independent test**: transfer ownership of a ceremony-created system register; the former owner can no longer govern, the new owner can.

- [ ] T058 [P] [US4] Test: after transfer, the former owner's governance attempt is refused
- [ ] T059 [US4] Mint a fresh genesis with the updated ceremony (slot-100 attestations) via `sorcha system-register create`
- [ ] T060 [US4] Re-genesis n1 within the 1-hour `VAL_TIME_002` window; bring tiny up on the same network
- [ ] T061 [US4] **Re-provision the AIAS demo immediately** (`run-demo.ps1 -Target n1 -Force`, then `rehearse.ps1 -Target n1`) — a re-genesis wipes it, and a broken demo discovered later is far more expensive than doing this now
- [ ] T062 [US4] 🔴 **LIVE GATE** Transfer system register ownership through the governance process; confirm the record replicates to tiny
- [ ] T063 [US4] 🔴 **LIVE GATE** Confirm the former owner can no longer govern and the new owner can

---

## Phase 7: Polish & cross-cutting

- [ ] T064 [P] Update `src/Services/Sorcha.Register.Service/README.md` — governance signing model, the slot-100 key, and why control transactions must never be signed by the node
- [ ] T065 [P] Update `docs/reference/API-DOCUMENTATION.md` with the approval endpoints and the corrected governance semantics
- [ ] T066 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with the governance cross-cutting pattern
- [ ] T067 [P] Update `.specify/MASTER-TASKS.md` and `docs/reference/development-status.md`
- [ ] T068 Record the R-006 limitation prominently (service principals can sign as any organisation, so approvals prove "the node was asked to use the key", not "the organisation approved") — this must not be described as solved
- [ ] T069 Delete the throwaway probe registers on n1 (`c794c86c…`, `96c421c0…`, `6b0760aa…`) once no longer needed as evidence
- [ ] T070 Confirm n1 is running a CI-built image rather than the locally-built `register-service:latest` currently deployed

---

## Dependencies

```
Phase 1 (Setup)
   ↓
Phase 2 (Foundational — slot 100 + key helper)   ← BLOCKS EVERYTHING
   ↓
Phase 3 US1 (P1)  ─── independently shippable, unblocks the cross-node encryption task
   ↓
Phase 4 US2 (P2)  ─── needs US1's correct authority
   ↓
Phase 5 US3 (P3)  ─── needs US2's approvals to have something to execute
   ↓
Phase 6 US4 (P4)  ─── needs all three, plus a re-genesis
   ↓
Phase 7 (Polish)
```

**Within Phase 2**, T006–T009 are four independent files but must land in one change — a partial
move leaves rosters disagreeing about which key is authoritative.

## Parallel opportunities

- T005 alongside T006–T009 (different files)
- All US1 tests T012–T018 in parallel (same test project, distinct cases)
- All US2 tests T033–T038 in parallel
- All polish tasks T064–T067 in parallel

## Implementation strategy

**MVP = Phase 2 + Phase 3 (US1).** That alone restores governance for the single-owner case, which
is the overwhelmingly common one, and unblocks the DevMode→Normal transition that the original
cross-node encryption investigation is waiting on. Ship it before starting US2.

**Do not batch the live gates.** Each 🔴 is placed where a failure is still cheap to diagnose.
Running them all at the end is how a signing defect and an encoding defect present as one
indistinguishable symptom — which is exactly what happened on 2026-08-06.
