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

- [X] T001 Confirm the branch builds clean and the two prerequisite DevMode commits are present (`git log --oneline master..HEAD`) — commits `55fbc7dd` and `58af4f8b` must be in the branch before any governance work
- [X] T002 Run the full baseline so later failures are attributable: `dotnet build Sorcha.sln`, then Register.Service, Register.Core, Validator.Service and ApiGateway test projects; record the pass counts in the PR description

---

## Phase 2: Foundational (BLOCKING — no user story can start until these land)

**These are blocking because they change the meaning of a roster key. Landing any of them alone
produces registers whose rosters disagree about which key is authoritative.**

- [X] T003 Document slot 100 in `src/Common/Sorcha.Wallet.Contracts/Constants/SorchaDerivationPaths.cs` as the organisation **governance key** — record that only the admin UI wizard already used it while three other creation paths did not, that the roster records whatever key signs the attestation, and that signing with any other key is refused "submitter not found in roster"
- [X] T004 Add a canonical public-key comparison helper (decode padded/unpadded base64 and base64url to bytes, fixed-time compare) in `src/Common/Sorcha.Register.Models/` — this is the R-003 fix and is used by every roster match
- [X] T005 [P] Unit-test the key helper across all four encodings and a non-match, in `tests/Sorcha.Register.Models.Tests/`
- [X] T006 Move attestation signing to slot 100 in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [ ] T007 **[RE-SCOPED — DEFERRED TO US4/T059, NOT MECHANICAL]** The system-register genesis ceremony in `src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs` does **not** sign attestations through the wallet service, so this is not a derivation-path parameter. `BuildControlRecord` mints a *self-referential* Owner attestation whose `Subject` is `did:sorcha:genesis:{fingerprint}` — **not** a `did:sorcha:w:{address}` wallet DID — carrying `Signature = ""` and the **slot-101 control key** as its `PublicKey`. That same key signs the genesis transaction AND defines the network trust anchor (`genesisPublicKeyFingerprint`), and is reused as the validator roster entry. Moving the attestation to slot 100 therefore decouples the roster key from the genesis-signing key and changes what the trust anchor attests. Two consequences to design in US4: (a) `GovernanceSigningService` resolves a signer by parsing a wallet address out of `Subject`, which a `did:sorcha:genesis:` subject cannot satisfy — the SSR is unreachable by that path as it stands; (b) any change here alters the compiled-in anchor and forces a coordinated re-genesis of both nodes.
- [X] T008 Move attestation signing to slot 100 in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/SandboxRegisterProvider.cs` (replace the explicit `derivationPath: null`)
- [X] T009 Move attestation signing to slot 100 in `walkthroughs/modules/SorchaWalkthrough/SorchaWalkthrough.psm1` (`New-SorchaRegister` — the `/sign` call currently passes no derivation path at all)
- [X] T010 Verify T006–T009 are exhaustive: grep for attestation signing call sites and confirm none still signs with the primary key — **found a FIFTH site the original list missed**: `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`, which was **already** signing at slot 100. So three paths (CLI, sandbox, walkthrough) disagreed with the UI, and a register was governable or not depending on which tool created it. No change needed to the wizard; the earlier "slot 100 is referenced nowhere" claim was a grep that omitted `.razor` and is corrected in research.md R-011.
- [X] T011 🔴 **LIVE GATE** Create a register on n1 with the updated code and confirm its roster records a **slot-100** key — **PASSED 2026-08-06** on register `3794f873976c4ad1a21c6f1dce1102d4`: wallet primary `uS780HTirYET…=` vs roster `fFE+9QNpjWLk9+hPDXbfIFctbmex6ONxaOnMVUAkjWA=`. Note the roster key contains `+` — exactly the character that makes the R-003 base64/base64url string comparison fail, so T004 is load-bearing for this very register.

**Checkpoint**: rosters now carry a dedicated governance key. User stories may begin.

---

## Phase 3: User Story 1 — A single organisation governs its register (P1) 🎯 MVP

**Goal**: an organisation changes a governed setting on a register it owns; the change seals to the
ledger and takes effect on every node.

**Independent test**: create a single-owner register, let genesis seal, promote DevMode→Normal,
confirm sealed-in-docket and observable on tiny.

### Tests for US1

- [X] T012 [P] [US1] Contract test: a governance tx signed by the **node system wallet** is refused — the node is never on a roster — in `tests/Sorcha.Validator.Service.Tests/Services/`
- [X] T013 [P] [US1] Contract test: a governance tx signed by an **organisation's slot-100 key** is accepted, in `tests/Sorcha.Validator.Service.Tests/Services/`
- [X] T014 [P] [US1] Regression test for R-003: roster key stored as **padded base64**, signature key supplied as bytes → matches (fails on master), in `tests/Sorcha.Validator.Service.Tests/Services/`
- [X] T015 [P] [US1] Contract test: `/propose`-shaped tx (`Metadata["Type"]=="Control"`) **is** detected as governance and roster-enforced (fails on master — currently bypasses), in `tests/Sorcha.Validator.Service.Tests/Services/`
- [X] T016 [P] [US1] Contract test: two signatures where one is not on the roster → only the roster member counts toward the requirement
- [X] T017 [P] [US1] Contract test: a governance tx carrying `BlueprintId == "genesis"` is rejected (R-005 guard against genesis misclassification)
- [X] T018 [P] [US1] Test that the `roster == null` allowance admits **only** a roster-creating transaction (R-002), in `tests/Sorcha.Validator.Service.Tests/Services/`

### Implementation for US1

- [X] T019 [US1] Create `GovernanceSigningService` in `src/Services/Sorcha.Register.Service/Services/` — resolve the register's roster, select the caller's organisation attestation, parse the wallet address from `Subject` (`did:sorcha:w:{address}`), and sign via `IWalletServiceClient.SignTransactionAsync(address, hash, SorchaDerivationPaths.RegisterAttestation, isPreHashed: true)`
- [X] T020 [US1] Unit-test `GovernanceSigningService`: correct derivation path, correct wallet resolved from `Subject`, and a clear failure when the caller's organisation is not on the roster
- [X] T021 [US1] Switch `CryptoPolicyService.SubmitPolicyUpdateAsync` from `ISystemWalletSigningService` to `GovernanceSigningService` in `src/Services/Sorcha.Register.Service/Services/CryptoPolicyService.cs`; leave the node system wallet in place for genesis/docket duties
- [X] T022 [US1] Update `tests/Sorcha.Register.Service.Tests/Unit/CryptoPolicyServiceTests.cs` for the new signer, keeping the existing mutation-verified assertions (ActionId, metadata keys, payload hash, base64url, one-way guard, non-genesis BlueprintId)
- [X] T023 [US1] `RightsEnforcementService`: replace the `Signatures[0]` string comparison with the T004 byte helper over **every** signature, counting **distinct** roster members satisfied, in `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs`
- [X] T024 [US1] `RightsEnforcementService.IsGovernanceTransaction`: key on `Metadata["Type"] == "Control"` plus the governance `BlueprintId`, preserving the `BlueprintPublish` carve-out (#917); remove the `Metadata["transactionType"] == "Control"` arm that never matched
- [X] T025 [US1] Narrow the `roster == null` allowance to roster-creating transactions only (R-002)
- [X] T026 [US1] Give `/propose` a non-empty `BlueprintId` (`register-governance-v1`) in `src/Services/Sorcha.Register.Service/Program.cs` — **must land with T024**, since fixing TX_003 alone opens the bypass
- [X] T027 [US1] `VAL_PERM_008` for insufficient distinct roster signatures, plus `VAL_PERM_007` (no roster) added earlier; every refusal now states the condition and what to do. **Deliberately scoped to `Transfer` only** — Add/Remove already carry approvals in the PAYLOAD and are checked by `VAL_PERM_005`/`VAL_PERM_006`; applying a transaction-level threshold there pre-empted that check and changed a long-established refusal code out from under any operator tooling keyed on it. It generalises in US2, where approvals become ledger transactions
- [X] T028 [US1] `GovernanceMetrics` on a new `Sorcha.Governance` meter — `sorcha_governance_decision_total{outcome,reason,register_id}`. No subject data: no key, wallet, or DID, because a metrics backend is a far wider audience than the ledger. Injected optionally so no existing construction site changes. Allowlisted in ServiceDefaults by LITERAL, not by the constant, since ServiceDefaults must never reference a service back
- [X] T029 [US1] Mutation-verify T012–T018: reinstate each defect in turn and confirm the matching test goes red. A guard written after the fix has never run red.
- [X] T030 [US1] 🔴 **LIVE GATE** **PASSED 2026-08-06** — register `c62126210a2b4966823ee0b54ca735cf`, promoted only after genesis had SEALED (height 1→2, so roster enforcement was live). Docket 0 = genesis `260b53c8…`, **docket 1 = the CryptoPolicyUpdate `31c3f0d3…`**, both `State: 4`. Contrast the pre-fix run, where the same transaction sat in NO docket.
- [X] T031 [US1] 🔴 **LIVE GATE** **PASSED 2026-08-06** — register `b58ec4066d7d4b458f80b6646c7f5dcc`, subscribed cross-installation by tiny. tiny first replicated the true genesis posture (`DevMode=true Height=1 CaughtUp`) — necessary, because the subscribe-time stub defaults to `DevMode=false` and would otherwise have confounded the result — then after promotion on n1 reached `DevMode=false Height=2 CaughtUp`, with tiny's docket 1 carrying the **same tx id** n1 produced (`90850e9c…`).
- [X] T032 [US1] 🔴 **LIVE GATE** **PASSED 2026-08-06** — register `6b0760aa…`, created before the slot-100 change so its roster holds primary keys. A slot-100 signature was refused with the new message *"none of 1 signature(s) match a roster member"*, and the ledger shows **1 docket (genesis only) and 0 stored CryptoPolicyUpdate transactions** — the refusal left no trace (SC-002). This doubles as live confirmation of the R-011 clean break: pre-change registers are ungovernable by design.

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

- [X] T039 [US2] Add `CryptoPolicyUpdate` to `GovernanceOperationType` in `src/Common/Sorcha.Register.Models/GovernanceModels.cs` (FR-021)
- [X] T040 [US2] Add `RosterSnapshotId` and `QuorumFormulaAtRaise` to the proposal shape, captured at raise time from `GovernanceRoster.LastControlTxId` and the register's configured rule (FR-011a)
- [ ] T041 [US2] ~~Implement approvals as ledger transactions signed with the org's slot-100 key~~ **ABSORBED into T071-T073 (2026-08-07).** R-009 equates "ledger transaction" and "action submission", so this is the same object as T054; and the approval's `BlueprintId`/`ActionId`/payload schema are defined by the blueprint (T053). Building it standalone means inventing a shape and changing it in US3 — the "bespoke code beside a decorative blueprint" the brief rejects. Do NOT start here.
- [X] T042 [US2] Implement invalidation as a comparison at count time — current `LastControlTxId` ≠ proposal's `RosterSnapshotId` ⇒ invalid. No timer, no sweeper, deterministic on every node. **Now actually reachable:** a proposal is recorded rather than refused, and carries no roster so it does not invalidate itself (see the lifecycle design).
- [X] T043 [US2] Record every terminal outcome with a reason (`quorum-met` / `expired` / `roster-changed` / `withdrawn` / `refused-not-on-roster`) — never a silent drop (FR-011c)
> **Two of the five reasons could not be produced, and were not invented.** `withdrawn` has no producer anywhere — no endpoint, no transaction type — so a status for it would be a state no ledger can reach, reported by a surface that looks complete; it is absent until something can raise it. `refused-not-on-roster` is why an individual *approval* did not count, not what happened to the proposal (one whose every approval was refused is still Open) — it belongs per-approval, where `ApprovalTallyRefusal` already carries it, and the detail endpoint reports it there. The three that remain are derived by `GovernanceProposalStatus.Derive`, a pure function in the zero-dependency leaf.

> ✅ **FINDING, CLOSED by T044 (2026-08-08)** — the gate now keys on the operation's proposer via
> `ValidateQuorumAsync` and never on `submitterAttestation.Role`. Guarded by
> `AnEnactmentCarriedByTheOwner_WithoutQuorum_IsStillRefused`, mutation-verified. Kept here because it
> constrains the reaction too: whichever organisation's key a node holds, the carry must confer no
> authority. Original finding follows.
>
> The Owner override is gated on **who signed the transaction**
> (`submitterAttestation.Role != RegisterRole.Owner` in `RightsEnforcementService`), while
> `ValidateQuorumAsync` gates it on **who raised the operation** (`ownerDid == operation.ProposerDid`).
> Those coincide today only because the proposer signs its own enacting transaction.
>
> Once enactment is a separate transaction carried by a reaction, they come apart: an enactment
> carried by the **Owner** would take the override and skip the quorum check entirely — so a
> never-approved proposal raised by anyone could be enacted simply by being carried on an
> Owner-signed transaction. That launders a proposal through the carry, which is precisely what the
> T075 carry/authority separation exists to prevent.
>
> The gate must key on the **operation's proposer**, not on the transaction's signer. Worth deciding
> deliberately rather than patching, because it also constrains the reaction: whichever organisation's
> key a node holds, the carry must not be able to confer authority.
- [X] T044 [US2] Wire quorum evaluation to the existing `GovernanceRosterService.ValidateQuorumAsync` over sealed approval transactions — do **not** reimplement the arithmetic (R-007). `GovernanceApprovalTally` (zero-dependency, plan-then-verify) selects which sealed approvals may count and builds the signature checks; the arithmetic goes through `ValidateQuorumAsync`. The key comes from the **roster**, never from the approval payload — trusting the offered key would make every signature self-certifying. The Validator reads them via `ControlTransactionPayload.EnactsProposalId` and rebuilds each digest from the **proposal's** stored operation. The Owner-carrier hole below is closed: the gate no longer consults `submitterAttestation.Role` on that path.
- [~] T044b **Enactment. Service DONE, trigger NOT built.** `IGovernanceEnactmentService.TryEnactAsync` decides and submits: refuses a decided or invalidated proposal, short-circuits on an existing enactment, counts, builds a byte-deterministic payload and submits through the Validator. **It decides whether to TRY, not whether the change is authorised** — the Validator re-counts and verifies, so this uses only the structural half of the tally and never verifies a signature itself (one crypto loop, one authority).
  - The earlier either/or resolved as **both**: entitlement-gating is a *precondition* (a node with no governance key returns `CannotCarry` rather than failing), and the deterministic tx id is the *dedupe* among the several nodes that can carry it.
  - Closed a gap left by the `ApplyOperation` determinism fix: the `Add` attestation is built by the **caller** and was still stamped `UtcNow` — harmless on the single-node override path, fatal for a carried enactment.
- [X] T044b-trigger `GovernanceEnactmentSubscriber` watches `docket:confirmed` and re-evaluates every open proposal on that register. It re-scans rather than reading the docket's transactions: that would be a round trip per transaction on every docket and would make the trigger depend on never missing an event, whereas re-evaluating is **self-healing** — a dropped event costs a delay until the register's next docket, not a proposal stuck at quorum forever. **That is why no separate sweep timer is needed.** Its own subscriber, not `RegisterEventBridgeService`, so a governance failure cannot stop register notifications; nothing escapes the handler, and one proposal throwing does not stop the others.

> **The propose → approve → enact chain is now complete in code.** Every step has unit and mutation
> coverage and none of it has executed against a deployment. The next thing that matters is T082 (CLI
> approve), because without a client that can produce a detached signature the live gates cannot be
> driven at all — and this feature's entire history says the defects are found on the first live run.
- [X] T045 [US2] Implement `POST /governance/proposals/{proposalId}/approve` per contracts — now accepts a **detached** `GovernanceApprovalSubmission` (R-014); server-side signing for multi-party registers is withdrawn. `202` submitted, `400` signature fails v2 verification, `403` not on snapshot, `409` not open/expired, `422` bad co-signature, idempotent repeat
- [X] T046 [US2] Implement `GET /governance/proposals` (status filter) and `GET /governance/proposals/{proposalId}` (full audit detail)
> **The list endpoint already existed and was sourced from forgeable fields.** It reported `operationType` / `proposerDid` / `targetDid` out of `MetaData.TrackingData`, which sits outside the signature, outside the payload hash and outside the docket's merkle leaf — anyone able to submit can rewrite it with nothing detecting the change. An audit surface built on that is worse than none, because it looks authoritative. It now reads the signed payload.
> **Status is derived on every read.** `Enacted` outranks everything: an enactment IS a roster change, so ordering invalidation first would report every enacted proposal as Invalidated, and ordering expiry first would make one silently re-read as Expired the moment its window passed. Both orderings look correct for as long as anyone tests inside the window; both are mutation-verified.
> **Found by testing against the serialiser production actually uses:** the Register Service configures no JSON options, so its minimal APIs use the web defaults and enums go on the wire as NUMBERS. The first version of the wire-contract test asserted against `SorchaJson.Options`, which this service does not use — it passed while the endpoint emitted `"status": 1`, which no typed client can read (it throws) and no status filter can match. The enums are now pinned on the type, not left to ambient host registration.
> **The shared client DTO bound the old shape.** `GovernanceProposalSummary` still declared `txId` / `docketNumber` / `proposerDid`; System.Text.Json ignores what it cannot match, so it would have deserialised every proposal to a row of nulls — a list rendering "no proposals" against a register full of them. `GovernanceProposalWireContractTests` now derives the agreement by reflection over both types.
- [X] T047 [US2] Enforce FR-024 — a governance change may never leave a register with no organisation able to govern it
- [ ] T048 [US2] 🔴 **LIVE GATE** Three-organisation register under `Unanimous` on n1: not enacted at 2 of 3; enacted and sealed at 3 of 3; replicated to tiny
- [ ] T049 [US2] 🔴 **LIVE GATE** SC-010 live: remove the sole outstanding approver → proposal `Invalidated`, **not** enacted

**Checkpoint**: consortium governance works and cannot be subverted by roster manipulation.

---

## Phase 5: User Story 3 — The governance process is visible and auditable (P3)

**Goal**: the published governance definition is what actually executes, and the trail is on the ledger.

**Independent test**: complete a multi-party change, then reconstruct proposer, approvers and outcome from the ledger alone.

### Tests for US3

- [X] T050 [P] [US3] Test: the revised `register-governance-v1` expresses quorum from the register's configured rule, not a hardcoded percentage
- [X] T051 [P] [US3] Test: proposal and approval payloads validate against the blueprint's new `dataSchemas`
- [X] T052 [P] [US3] Test: the recorded action sequence matches the published definition (FR-018)

### Implementation for US3

- [X] T053 [US3] Revise `blueprints/templates/register-governance-v1.json`: replace the hardcoded `approvalPercentage >= 50.01` with the register's configured rule; add the crypto-policy operation; add `dataSchemas` for proposal and approval payloads; make "Accept Role" conditional on operation type (R-008)
- [ ] T054 [US3] Execute the governance blueprint as a real workflow instance, with each organisation's approval submitted as an action
> **Scope, measured 2026-08-09 before starting.** `register-governance-v1` is seeded to the **system register only** (`SystemRegisterBootstrapper`), and all three governance transaction kinds set `BlueprintId = register-governance-v1` while relying on `Metadata["Type"] = "Control"` to earn the roster check and to be *exempt from action-schema validation* — precisely because the blueprint is not published to ordinary registers. T054 therefore means publishing it to ordinary registers and making the three kinds conformant, schema-validated actions **while keeping the roster check that currently rides on the Control discriminator**. That changes the validation path of every governance transaction on every register: the highest blast radius in the feature, and the exact class where it has repeatedly produced silent defects. Budget it as its own piece with a live gate, not as a tail-end.
- [ ] T055 [US3] Ensure the governance instance folds correctly under F145's `InstanceProjector` — quorum must be a pure function of sealed content so every node agrees (R-009)
- [X] T056 [US3] Ensure each proposal, approval and enactment is individually attributable to its organisation in the ledger record (FR-019)
> **Done ahead of T054/T055, because attribution is a property of the records that exist today, not of a workflow instance.** The load-bearing part is what "attributable" is allowed to mean: a transaction's `Metadata` sits outside the signature, outside the payload hash and outside the docket's merkle leaf, so a DID recorded there is a hint for operators and never evidence. Verified that all three kinds carry their attribution **inside the signed payload** — the proposer on the operation, the approver on the approval payload *and* bound into the statement digest its own detached signature covers, the enactment carrying the proposed operation forward — and that the three remain distinguishable from payload content alone (note `enactsProposalId` is serialised as a null VALUE on a proposal, not omitted, so a reader keying on key-presence would classify every proposal as an enactment). `Metadata["carriedBy"]` is written twice and **read nowhere**; a validator test now pins that rewriting it changes nothing about the decision, mutation-verified by making the decision depend on it.
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

- [X] T064 [P] Update `src/Services/Sorcha.Register.Service/README.md` — governance signing model, the slot-100 key, and why control transactions must never be signed by the node
- [X] T065 [P] Update `docs/reference/API-DOCUMENTATION.md` with the approval endpoints and the corrected governance semantics
- [X] T066 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` with the governance cross-cutting pattern
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


---

## Phase 8: External approval surface (added 2026-08-07)

**Design**: `docs/superpowers/specs/2026-08-07-governance-approval-surface-design.md`
**Research**: R-013 (digest binding), R-014 (key custody), R-015 (co-signature), R-016 (device assurance)
**Goal**: an approval is produced by something external to the platform — human or autonomous bot — and binds exactly what was reviewed.

### Statement v2 — do this first; everything after it is unsafe without it

- [X] T071 🔴 Test FIRST: reflection-driven digest coverage — enumerate `GovernanceOperation`'s properties and assert that mutating any non-excluded one (`ApprovalSignatures`, `Status` excluded) changes the digest. **MUST go RED today** for `ValidatorEntry`, `RosterSnapshotId`, `QuorumFormulaAtRaise`, `ExpiresAt`. A hand-listed test is not acceptable — it rots exactly as the hand-listed field list did (R-013). Pattern precedent: the derivation-context reflection tests in `Sorcha.Wallet.Contracts.Tests`.
- [X] T072 Implement `GovernanceApprovalStatement` **v2**: bind domain tag `sorcha:governance-approval:v2`, `registerId`, `proposalId`, `approverDid`, approve/reject, plus a hash of the operation's canonical JSON minus derived members. v1 signatures MUST NOT verify under v2 (clean break, R-011).
- [X] T073 [P] Test: a v1 signature is rejected under v2.

### Blueprint, then approvals as its actions (the absorbed T041/T054)

> **T071 run, 2026-08-07:** went RED for **five** fields, not the four the design predicted —
> `Justification` was also unbound. A hand-listed test would have covered exactly the four I thought
> of and passed. `Justification` is bound rather than excluded: it is the reason an approver read
> when deciding, so a substituted one attributes a justification to an approval that never saw it.
>
> **`proposalId` binding deferred:** `GovernanceOperation` has no proposal identifier — none exists
> anywhere in the model, though the contract URL assumes one. v2 binds the whole operation (the
> actual vulnerability); binding `proposalId` lands with the proposal-lifecycle work that introduces
> it (T045/T046).

- [X] T074 T053 first — the approval's `BlueprintId`, `ActionId` and payload schema come from the revised `register-governance-v1`. Confirm T053 is complete before starting T075.
- [X] T075 Implement an approval as an **action submission** of the governance blueprint, signed externally, carried to the ledger **through the validator** — never written straight to storage (that was the original US1 defect).

> **T075 as built (2026-08-08).** `GovernanceApprovalActionPayload` (`Sorcha.Register.Models`) is the
> ledger shape; `GovernanceApprovalActionSubmitter` (`Sorcha.Register.Service`) builds it and hands it
> to `IValidatorServiceClient`. It holds no repository, so the direct-to-Mongo shape cannot return
> through it — asserted structurally, not just by behaviour.
>
> **The envelope signature is not the approval signature, and conflating them is the trap.** The
> validator verifies every entry in `Signatures` against `SHA-256("{txId}:{payloadHash}")`. An
> approver signs `GovernanceApprovalStatement` — deliberately not a transaction digest, because they
> sign before any transaction exists and must bind what they reviewed. Filing the detached signature
> in `Signatures` fails `VAL_SIG_002` every time, with a message that blames the approver. So the
> authority lives in the **payload**, and the envelope is signed by whichever roster organisation the
> node can sign as (`preferredSubject: null`), recorded as `Metadata["carriedBy"]`. Signing the
> envelope as the approver whenever the node happened to hold their key was rejected: it would dress a
> carry up as an approval — the server-side signing R-014 withdrew — and only sometimes, which is
> worse than always. A node holding no governance key for the register therefore cannot carry an
> approval, which is correct rather than a limitation.
>
> `Metadata["Type"] = "Control"`, matching `/propose`. That is what earns the roster check and exempts
> it from action-schema validation — necessary today, because `register-governance-v1` is not
> published to ordinary registers and resolving it would fail outright. Full conformance is T054-T057.
> `PreviousTransactionId` is the proposal, mirroring the blueprint's action 1 → 2 route; sibling
> approvals are fine, the fork check exempts a Control predecessor.
>
> **The blueprint's approval `dataSchemas` and the payload model disagreed on nine fields**, in both
> directions — the schema declared `delegation.signature`/`delegation.publicKey`, which live on the
> authorisation and not on the grant, so **no conforming payload could ever have been produced**; and
> it could not express `ApprovalAuthMethod.Unknown`, `algorithm`, `delegationAlgorithm`,
> `organisationDid`, `individualDid` or `grantedAt`. Reconciled, and now held in lockstep by
> `GovernanceApprovalPayloadContractTests` — bidirectional and derived from serialisation, not a hand
> list. All eight of its assertions and all six submitter guards were mutation-verified (perturb, see
> the *named* test go RED, restore).
>
> **`proposalId` and `statementVersion` were added to the payload.** Neither is bound by the approval
> signature and neither needs to be: verification rebuilds the statement from the operation stored on
> the proposal named, so re-pointing an approval changes the operation and the signature stops
> verifying.
>
> **Finding that blocks T045: there is no open-proposal state.** `/propose` evaluates quorum at raise
> time (step 5) and returns **400 "Quorum not met"** unless the Owner override fires or every approval
> is supplied inline — then it applies the operation and writes the updated roster in the same
> transaction. Propose and enact are one atomic step, so today there is nothing for an approval to
> arrive against. The design's ledger flow assumes step 1 records a raised proposal; only its
> `RosterSnapshotId`/`QuorumFormulaAtRaise` capture (T040) was actually built. T045's `409 not
> open/expired`, T046's status filter and T044's recount all rest on that missing state. **Decide
> whether recording an unenacted proposal belongs to T045 or to a task of its own before starting it.**

### The signing protocol

- [X] T076 [P] Implement `GET .../proposals/{proposalId}/signing-request` per contracts. **No digest field** (FR-028) — the client derives it.
- [ ] T077 [P] Test: the signing request carries the full operation and no digest; a client-derived digest matches what the server verifies against.
- [X] T078 Implement submission verification: signature checked against the v2 statement rebuilt from the **stored** operation, not from anything the client sent.
> **T078 found a gap the spec had not named.** Structural validation confirmed an authorisation named an individual and that a signature verified against the key it *supplied* — but nothing bound that key to the claimed `IndividualDid`. Anyone could have signed with their own key while naming a colleague as accountable: valid signature, false audit record, self-declared accountability. `did:sorcha:w:{address}` encodes the wallet address and an address derives from its public key, so the binding is checkable — `DetachedApprovalVerifier.VerifyKeyBelongsToDid` re-derives and compares, and no approval is accepted without it. Added as **FR-035**.
- [X] T079 `authorisation` handling (R-017, supersedes the co-signature framing): verify it, bind it to the approving organisation, and treat it in the validator as **attestation metadata, not a roster claim** — otherwise it is rejected as "not on roster". Refuse the whole submission when invalid; never accept while discarding it (FR-032).
> **T079 was verified once per approval, not once per node.** Only the Register Service that received the submission over HTTP checked the `authorisation`; `RightsEnforcementService.CountSealedApprovalsAsync` then counted every *replicated* approval on the organisation's signature alone. An approval whose accountability was absent or forged therefore counted everywhere except its point of entry — proven by a test that went red on both cases before the fix. Closed by moving `DetachedApprovalVerifier` into `Sorcha.Validator.Core` (referenced by both services, so there is **one** implementation of the rule rather than two that can disagree) and calling it per counted approval. Uncheckable fails closed: a validator with no verifier counts no approval carrying one. `GovernanceAuthorisationValidator.Validate` and the verifier both gained an overload taking `(approverDid, isApproval, authorisation)` so a submission and a sealed payload share one code path with **no** conversion between them — a hand-maintained mapping is what dropped `ValidatorEntry` on `/propose`.
- [X] T087 Delegation record: signed by the **empowering individual's** key (never server-asserted — the server mints tokens, so a claim it can assert is one it can forge). Carries approver public key, organisation, scope over `GovernanceOperationType`, expiry. FR-033.
- [X] T088 (structural half done — the validator takes an injected `isRevoked` predicate so the answer comes from sealed content, not service state; the ledger-side revocation record is now in: GovernanceDelegationRevocation + its own statement, unilateral per R-023) Delegation validity determinable from **sealed ledger content** so every node folds identically (FR-034 / R-009) — delegation and revocation are ledger records, not service state.
- [X] T089 [P] Test: a delegated approval outside its scope is refused (e.g. a bot empowered for `CryptoPolicyUpdate` cannot approve `Transfer`).
- [X] T090 [P] Test: an expired or revoked delegation refuses the approval, and the refusal is recorded with a reason rather than dropped.
- [X] T091 [P] Test: **every** accepted approval resolves to a named individual — directly or through a delegation. No approval reaches the ledger without one (FR-029).
- [X] T080 [P] Test: a co-signature alone does not satisfy the roster; an organisation signature is still required.
- [X] T081 Record `authMethod` on the ledger record (R-016) so a register can require a minimum standard per operation.
> **T081 was narrower than it reads, and one of its two records was already dead.** The approval's `authMethod` is *already* sealed, in `GovernanceApprovalActionPayload` on each approval transaction; `ControlTransactionPayload` carries only `version`/`roster`/`operation`/`enactsProposalId`, so `ApprovalSignature` — the type holding the field — never reaches the ledger at all. Scope settled with the maintainer as: carry it onto the counted vote so a future per-register minimum-standard gate has the fact, and add nothing to the sealed enactment (which would duplicate evidence already on the ledger). `ApprovalTallyCheck` now carries `Authorisation` + `AuthMethod` so the caller needs no second list keyed by approver, and `ToVotes` writes the **same token** the payload serialises, derived from that converter's own naming policy rather than a hand-written switch. `ApprovalSignature.AuthMethod` previously meant *how a person authenticated* (`passkey`/`totp`/…) — a second vocabulary in one field. Its only writer, `GovernanceApprovalService`, had **no callers** (R-014 replaced server-side signing) and is deleted rather than left registered.

### Clients

- [X] T082 CLI `sorcha governance approve` — fetch the signing request, render the operation, sign locally, submit. Proves the ledger mechanics on n1 without waiting for UI, and is the autonomous-bot path.
- [ ] T083 Wallet PWA signing surface — recompute the digest, display what is being authorised, sign with the organisation's slot-100 key. Same code on web and mobile (R-016).
> **Open question to settle first: whose key, held where.** The digest half is ready — `GovernanceApprovalStatement.ComputeDigest` lives in the zero-dependency `Sorcha.Register.Models`, so a WASM client can recompute it and must (FR-028: the client derives the digest from the operation it rendered, so a server-supplied one cannot disagree with what the approver saw). The **signing** half is not settled: an organisation's slot-100 governance key is server-custodied today, which is precisely what R-014 moved signing away from, while the citizen PWA holds device keys that are not on any register's roster. Building a PWA signing surface therefore needs a decision about where an organisation's governance key lives on a personal device — not a UI decision. T084's console surface is the read half and is done; this is the write half.
- [X] T084 Org admin console review surface — render the governance **diff** (roster before/after, policy before/after), not a JSON blob. Approving what you cannot read is FR-027 defeated in the human rather than the protocol.
> **The diff is computed server-side, and that is the design, not a shortcut.** `ApplyOperation` lives in `Sorcha.Register.Core`, which a Blazor client cannot reference — so a client-side preview would be a second implementation of "what does this operation do" *by construction*, and it would eventually show an approver an accurate-looking change that differs from the one that enacts. `ProjectRoster` is extracted so the preview and the enactment payload are the same call. `GovernanceProposalReview` renders who joins / leaves / changes role, with a departing member shown **struck through and still listed** rather than absent: a row that disappears is a change the reader has to notice by its absence.
> **Three no-diff cases each say why.** The server withholds the diff for an operation that changes no membership, for an Invalidated proposal and for an Enacted one — and an empty panel would read as "nothing changes" in all three, so each renders its own explanation. Mutation-verified: silencing them fails exactly those three tests.
> Mounted in the existing Governance tab of the register detail page, above the policy panel. Policy before/after was already covered by `PolicyDiffDialog`. **Note its copy is stale** — it says "validators will vote on this proposal", but under F189 it is organisations on the roster who approve.

### Gates

- [X] T085 🔴 **LIVE GATE** Substitution: review and sign an `AddValidator` for validator A, submit with validator B's entry. MUST be rejected on n1, and the rejection MUST appear in the validator log rather than being absorbed. This is the gate that distinguishes independent approval from something that merely looks like it.
- [X] T086 🔴 **LIVE GATE** Single-owner register completes governance unattended, with no pairing, device or human interaction (FR-031) — the no-regression gate.

**Checkpoint**: the server cannot produce a multi-party approval on its own, and a signature binds exactly what the approver saw.


---

## Phase 9: Defects found by live verification (2026-08-07)

- [X] T092 🔴 Route `/governance/propose` through `IGovernanceSigningService` (slot 100, proposing ORG) instead of `ISystemWalletSigningService` (slot 101, NODE). R-020 — the original F189 defect, left unfixed on this path when US1 moved crypto-policy across. Roster changes are the operations this feature is named after and none of them can complete on a sealed register today.
- [X] T093 🔴 **LIVE GATE** Raise a roster-change proposal on a SEALED **ordinary** register and confirm it lands in a docket. **Not the SSR** — it is unique by design (offline pre-signed genesis, deliberately outside this path until US4), so it can neither confirm nor refute the general behaviour. US1 should have had this gate; its absence is why R-020 survived a live verification.
- [X] T094 ~~Decide and implement individual key provisioning~~ **CLOSED — no work needed.** The mechanism already exists: `POST /api/v1/wallets`, the first-login flow the UI drives. R-018 was my error (I searched only the Tenant Service). Originally: decide individual key provisioning (R-018) — no path currently gives an administrator a personal key, so neither authorisation form is usable by one. Must not reintroduce server-side signing (R-014).

- [ ] T095 Enforce **Owner-only** delegation granting (R-023) in the service layer, where the roster and org membership resolve. Cannot live in `GovernanceAuthorisationValidator` — `Sorcha.Register.Models` is a zero-dependency leaf and knows nothing of organisational roles.
> ⚠ **BLOCKED — there is no granting path to enforce this on (verified 2026-08-09).** `GovernanceDelegation` is referenced in exactly two places outside its own file: `GovernanceApprovalSubmission` carries one, and `GovernanceAuthorisationValidator` verifies its signature. **Nothing on the platform issues a delegation, stores one, or revokes one** — no endpoint, no service, no transaction writer. `GovernanceDelegationRevocation` exists as a type with no producer either. So the whole delegated-authorisation path (R-017) is *verifiable but unreachable*: an approval can carry a delegation only if something outside the platform minted and signed it by hand. T095 is a rule about an operation that does not exist; building the grant path is the real task, and it is substantially bigger than 'enforce Owner-only'. Same shape as the `withdrawn` proposal status.
- [ ] T096 Interactive signing windows to 15 minutes, scripted flows unchanged at 5 (R-023). Needed before T083 builds the PWA signing surface against a window that expires mid-review.
> ⚠ **PREMISE DOES NOT HOLD — no window expires mid-review (verified 2026-08-09).** There are two windows in this feature and neither is the one R-023 describes. The **5 minutes** is `RegisterCreationOrchestrator._pendingExpirationTime`, which gates *register creation* (initiate → sign attestations → finalize) — the scripted flow R-023 says to leave alone. The **approval signing request** inherits `operation.ExpiresAt`, i.e. the proposal's own window, currently **7 days**. So an approver reading a proposal on a phone is not racing anything, and T096 is **not a blocker for T083**. Before doing anything here, decide what it is actually for: raising the register-creation window for a human signing genesis attestations on a separate device is a real improvement, but note that a *client-declared* 'interactive' flag would be self-asserted and confer no property — it would be 15 minutes for anyone who asks.
