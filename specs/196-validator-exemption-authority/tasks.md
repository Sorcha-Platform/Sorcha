---
description: "Task list for Feature 196 — Validator Exemption Authority"
---

# Tasks: Validator Exemption Authority

**Input**: Design documents from `/specs/196-validator-exemption-authority/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/authority-resolution.md](./contracts/authority-resolution.md)

**Tests**: Test tasks are included and are **not optional here**. The spec makes them a delivery
criterion (SC-001, SC-002) because the defect class this feature closes has previously survived a
green suite.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1–US4 as defined in [spec.md](./spec.md)

## Phase ordering note

Phases follow the **plan's execution order**, which deliberately differs from pure priority order.
US2 is P1 but runs last because it is the only story that can lock out legitimate administrative
traffic if implemented wrongly, and because it carries a roster-provisioning prerequisite the others
do not. US3/US4 are lower priority but land earlier because they are low-risk and reduce the surface
first. Every story remains independently testable and deliverable.

---

## Phase 1: Setup & Decision Gates

**Purpose**: Settle the two open decisions and establish an honest baseline before touching code.

- [x] T001 ✅ **DONE 2026-08-28.** R2 decided: publication authority is the **register's validator roster**, matched on the **existing `sorcha:register-control` context**. Moving to the dedicated `sorcha:blueprint-publish` context is filed as a follow-up (T056), not part of this feature. Recorded in `research.md` §R2; `spec.md` FR-003 revised accordingly. **Phase 6 unblocked.**
- [x] T002 ✅ **DONE 2026-08-28.** FR-007 decided: **fail closed in every environment** — no environment gate, no operator bypass flag. Recorded in `spec.md` Assumptions with both rejected alternatives and why. **Phase 2 unblocked.**
- [x] T003 Record the pre-change baseline: run `dotnet test --project tests/Sorcha.Validator.Service.Tests/Sorcha.Validator.Service.Tests.csproj` and capture the pass/fail counts into `specs/196-validator-exemption-authority/baseline.md` so later "still green" claims are checkable.
- [x] T004 *(Informational only — de-risked 2026-08-28.)* Note which code paths re-run the validation engine over already-sealed transactions, for the record in `research.md` §R5. No longer blocking: the estate may be wiped, so historical validity is not owed. **No longer blocks Phase 6.**

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The single grant decision and the anchor it needs. Nothing in Phases 3–6 can proceed
without these.

**⚠ No user story is independently deliverable until this phase completes.**

- [x] T005 Move `INodeTrustAnchor` from `src/Services/Sorcha.Register.Service/Provenance/INodeTrustAnchor.cs` to `src/Core/Sorcha.Register.Core/Provenance/INodeTrustAnchor.cs`, updating the namespace to `Sorcha.Register.Core.Provenance` and preserving the XML docs (including the #1374 reference).
- [x] T006 Update all Register Service references to the relocated interface in `src/Services/Sorcha.Register.Service/Provenance/NodeTrustAnchor.cs`, `src/Services/Sorcha.Register.Service/Provenance/DocketEvidenceAssembler.cs` and `src/Services/Sorcha.Register.Service/Program.cs`. The concrete loader stays in the Register Service.
- [x] T007 Register a Validator-side binding of `INodeTrustAnchor` over the same configured/embedded genesis in `src/Services/Sorcha.Validator.Service/Program.cs`. It MUST NOT be a second source of truth — same genesis, same fingerprint function (`GenesisFileLoader.ComputeFingerprint`).
- [x] T008 [P] Create `ExemptionKind` enum (`Genesis`, `Control`, `BlueprintPublish`) in `src/Services/Sorcha.Validator.Service/Services/ExemptionKind.cs`.
- [x] T009 [P] Create the transient decision models `ExemptionClaim`, `ExemptionAuthority`, `ExemptionDecision` in `src/Services/Sorcha.Validator.Service/Services/ExemptionModels.cs` per [data-model.md](./data-model.md). No persisted shape changes.
- [x] T010 Create `ExemptionAuthorityResolver` in `src/Services/Sorcha.Validator.Service/Services/ExemptionAuthorityResolver.cs` as the **single producer** of `ExemptionDecision`. Implement the rule from [contracts/authority-resolution.md](./contracts/authority-resolution.md): grant iff a claim is present AND its authority is satisfied; unresolvable authority never grants.
- [x] T011 Implement per-register authority caching keyed on the register's last control transaction in `ExemptionAuthorityResolver.cs`, so governance changes invalidate naturally. Do not add a second O(n) control-chain walk (see #1224).
- [x] T012 [P] Add the `RefusedClaim` structured log event and a counter dimensioned by kind/route/reason on the existing validator meter, in `src/Services/Sorcha.Validator.Service/Services/ExemptionAuthorityResolver.cs` and the validator's metrics registration. Satisfies FR-013. "Not entitled" and "could not resolve" MUST be distinguishable.
- [x] T013 [P] Add a reflection test asserting every `ExemptionKind` value has a non-null authority rule, in `tests/Sorcha.Validator.Service.Tests/Services/ExemptionKindCoverageTests.cs`. Adding a kind without classifying it must fail the build, in the manner already used for derivation contexts.

**Checkpoint**: The resolver exists and is the only producer, but no call site consumes it yet —
behaviour is unchanged and the suite is still green at the T003 baseline.

---

## Phase 3: User Story 1 — A forged genesis claim is refused (Priority: P1)

**Goal**: Both routes to the genesis exemption require the network's anchor.

**Independent test**: An unauthorised wallet claiming genesis by either route is refused sender
authorisation, while the same wallet without the claim is refused too (the counterfactual), and the
genuine genesis still bootstraps.

### Tests for US1

- [x] T014 [P] [US1] Write the counterfactual control test in `tests/Sorcha.Validator.Service.Tests/Services/ExemptionAuthorityTests.cs`: an unauthorised wallet submitting an ordinary transaction is refused `VAL_BP_002`. This proves later refusals are caused by the check, not by blanket rejection.
- [x] T015 [P] [US1] Write a failing test: an unauthorised wallet claiming genesis via the transaction-type label is refused, in `ExemptionAuthorityTests.cs`.
- [x] T016 [P] [US1] Write a failing test: an unauthorised wallet claiming genesis via the blueprint-identifier route (`BlueprintId == "genesis"`) is refused, in `ExemptionAuthorityTests.cs`.
- [x] T017 [P] [US1] Write a failing test: a transaction bearing the constant genesis transaction id with a different payload and a valid self-signature is refused, in `ExemptionAuthorityTests.cs`. This is the case the id check alone does not catch.
- [x] T018 [P] [US1] Write a passing-path test: the genuine genesis transaction is accepted, in `ExemptionAuthorityTests.cs`. Use real hashing — **do not stub `IHashProvider`** (see research R7).

### Implementation for US1

- [x] T019 [US1] Implement the `Genesis` authority rule in `src/Services/Sorcha.Validator.Service/Services/ExemptionAuthorityResolver.cs`: transaction id equals `GenesisSignatureVerifier.ComputeGenesisTxId()`, register is the system register, and the signing key fingerprint matches `INodeTrustAnchor.GenesisPublicKeyFingerprint`.
- [x] T020 [US1] Fold the `BlueprintId == "genesis"` route into the same rule in `src/Services/Sorcha.Validator.Service/Services/TransactionTypeClassifier.cs`, so `IsGenesisTransaction` and `IsGenesisOrControlTransaction` derive from the resolver rather than from either unsigned field.
- [x] T021 [US1] Verify the genesis freshness window (`VAL_TIME_002` / `GenesisMaxAge`) still keys off the same decision in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:1961`, so a claimed genesis cannot select the short window without the authority.

### Verification for US1

- [x] T022 [US1] Mutation-test each new check: remove T019's fingerprint comparison and confirm T015–T017 go red; restore. Remove T020's route fold and confirm T016 goes red; restore. Record outcomes in `baseline.md`. **A guard that stays green against its own removal is not delivered.**

**Checkpoint**: US1 is independently deliverable. Genesis bootstrap still works; both forged routes refused.

---

## Phase 4: User Story 4 — Fields cannot disagree with the signature (Priority: P3)

**Goal**: A transaction cannot present one identity to the rules and another to its own signature.

**Independent test**: A transaction whose unsigned identifiers disagree with their signed
counterparts is refused; agreeing transactions are unaffected.

### Tests for US4

- [x] T023 [P] [US4] Write a failing test: a transaction whose unsigned `blueprintId` disagrees with `Payload.blueprintId` is refused, in `tests/Sorcha.Validator.Service.Tests/Services/SignedFieldAgreementTests.cs`.
- [x] T024 [P] [US4] Write a failing test: the same for `actionId`, in `SignedFieldAgreementTests.cs`.
- [x] T025 [P] [US4] Write a passing-path test: transactions whose fields agree, and transaction kinds carrying no signed counterpart (genesis, control, publication payloads), are unaffected, in `SignedFieldAgreementTests.cs`. An absent counterpart is not a disagreement.

### Implementation for US4

- [x] T026 [US4] Implement the field-agreement check in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`, comparing submission-level `BlueprintId` / `ActionId` / `InstanceId` against their counterparts inside the signed payload where present. Emit a distinct validation code.
- [x] T027 [US4] Confirm the new code is validator-internal and NOT promoted to `Sorcha.Blueprint.Models` unless a second project names it (CLAUDE.md pattern 16 — promotion is triggered by a cross-boundary consumer, not by family membership).

### Verification for US4

- [x] T028 [US4] Mutation-test: remove the comparison in T026 and confirm T023–T024 go red; restore.

**Checkpoint**: US4 independently deliverable. Ordinary action traffic unaffected.

---

## Phase 5: User Story 3 — The governance waiver cannot drift from its check (Priority: P2)

**Goal**: One decision, not two that must be kept in step. No behaviour change.

**Independent test**: Governance still works identically; making the roster check unavailable
withholds the waiver rather than granting it on the label alone.

### Tests for US3

- [x] T029 [P] [US3] Write a test: a governance transaction from a roster member is accepted and quorum behaviour is unchanged, in `tests/Sorcha.Validator.Service.Tests/Services/ExemptionAuthorityTests.cs`.
- [x] T030 [P] [US3] Write a test: a governance transaction from a non-member is refused, in `ExemptionAuthorityTests.cs`.
- [x] T031 [P] [US3] Write a test: with the roster unresolvable, the waiver is withheld rather than granted (FR-007 fail-closed), in `ExemptionAuthorityTests.cs`.
- [x] T032 [P] [US3] Write a regression test proving quorum is still attainable — a change requiring multiple approvals reaches enactment — in `ExemptionAuthorityTests.cs`. This guards FR-008 directly.

### Implementation for US3

- [x] T033 [US3] Route the `Control` grant through `ExemptionAuthorityResolver` in `src/Services/Sorcha.Validator.Service/Services/TransactionTypeClassifier.cs`, deriving it from the roster outcome rather than an independent string comparison.
- [x] T034 [US3] Have `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs` consume the same resolved decision instead of recomputing `IsGovernanceTransaction` independently, preserving the existing legacy-publication guard (#917) as the effective-kind disambiguation described in research R6.
- [x] T035 [US3] Verify the two load-bearing exemptions are untouched: the fork bypass for shared-predecessor approvals, and the chain-derived sender binding. Neither may be narrowed (FR-008).

### Verification for US3

- [x] T036 [US3] Mutation-test: make the roster check unavailable and confirm T031 observes a withheld waiver rather than a granted one; restore.

**Checkpoint**: US3 independently deliverable. Governance behaviour byte-identical.

---

## Phase 6: User Story 2 — A forged publication claim is refused (Priority: P1, sequenced last)

**Authority source DECIDED (T001)**: the register's **validator roster**, matched on the existing
`sorcha:register-control` derivation context.

**De-risked 2026-08-28.** The validator roster today carries **docket-signing** entries, so a
publication-authorising entry must exist. Because the estate may be wiped, this is a **forward
requirement, not a migration**: register creation and genesis must emit the entry, and a clean
re-genesis proves it. T037 implements that rather than discovering what current registers hold.

**Goal**: Publication requires proved publishing authority on that register.

**Independent test**: A publication-labelled transaction signed by a wallet without publishing
authority is refused; a genuine publication still seals with an unchanged publication identifier.

### Roster provisioning for US2 (prerequisite)

- [x] T037 [US2] ✅ DONE — publication-authorising validator-roster entry emitted under `sorcha:register-control` at register creation (`src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`) and in the genesis roster for the system register. Forward requirement only — no migration of existing registers; a clean re-genesis is the proof.

### Tests for US2

- [x] T038 [P] [US2] Write a failing test: a publication-labelled transaction signed by a wallet without publishing authority is refused, in `tests/Sorcha.Validator.Service.Tests/Services/ExemptionAuthorityTests.cs`.
- [x] T039 [P] [US2] Write a passing-path test: a genuine publication is accepted and its publication identifier is unchanged, in `ExemptionAuthorityTests.cs`. Guards SC-007 and FR-010.
- [x] T040 [P] [US2] Write a test that the **effective-kind disambiguation** holds: a publication labelled as governance but carrying the secondary publication field is judged as a publication, not a governance operation, in `ExemptionAuthorityTests.cs`. This guards the #917 code path, which stays correct regardless of whether legacy data exists.
- [x] T041 [P] [US2] Write a test: a publication on a register whose validator roster has been updated through governance is accepted under the current roster, in `ExemptionAuthorityTests.cs`.
- [x] T042 [P] [US2] Write a test: with the validator roster unresolvable, the publication exemption is withheld rather than granted (FR-007, fail closed in every environment), in `ExemptionAuthorityTests.cs`.

### Implementation for US2

- [x] T043 [US2] Implement the `BlueprintPublish` authority rule in `src/Services/Sorcha.Validator.Service/Services/ExemptionAuthorityResolver.cs`: match the signer against the register's validator roster on the `sorcha:register-control` context, honouring entry status. Resolve **from the register**, never from `Metadata["SystemWalletAddress"]` on the transaction under test.
- [x] T044 [US2] Route the publication grant through the resolver in `src/Services/Sorcha.Validator.Service/Services/TransactionTypeClassifier.cs`, evaluating against the **effective** kind after the legacy-era guard, not the raw label.
- [x] T045 [US2] Confirm no change to the canonical publication payload or `BlueprintPublicationId.Compute` inputs. Run `BlueprintCanonicalJsonGoldenVectorTests` — it must stay green with no vector regeneration.

### Verification for US2

- [x] T046 [US2] Mutation-test: remove the authority comparison in T043 and confirm T038 goes red; restore.
- [x] T047 [US2] Verify the sealed-docket verification path is untouched (FR-012) by exercising a replica pulling sealed history, not by reading the code.

**Checkpoint**: All four grant routes closed.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T048 Run the full solution suite (`dotnet build && dotnet test`) and compare against the T003 baseline. Any delta must be explained, not absorbed.
- [x] T049 Verify the CI gates still pass, in particular `scripts/check-publication-id-owner.ps1` (`publication-id-owner-gate`) and `scripts/check-error-code-contract.ps1` (`error-code-contract-gate`).
- [x] T050 [P] Deploy to **n1** and run the core walkthrough suite: `pwsh walkthroughs/run-all.ps1 -Profile n1 -AuthGapMs 1000`. Confirm the running image is the one built — a restart with a short compose file list swaps the artefact under test and still passes green.
- [x] T051 [P] Deploy to **tiny** and confirm replication: a replica pull of sealed history, and byte-identical replication of a register from n1. Always multi-node — a single-node pass is not evidence.
- [x] T052 Live-verify each administrative operation end to end on n1: node bootstrap from genesis, a blueprint publication, and a governance propose→approve→enact. Record evidence in `baseline.md`. **Merged is not proven.**
- [x] T053 [P] Update `src/Services/Sorcha.Validator.Service/README.md` with the exemption-authority rule and the distinction between claim and authority.
- [x] T054 [P] Add the exemption-authority rule to `CLAUDE.md` as a numbered critical pattern (next after 22), stating that an exemption is granted from proved signer authority and never from a submitter-settable field.
- [x] T055 [P] Update `docs/reference/API-DOCUMENTATION.md` and `.claude/skills/sorcha-architecture/SKILL.md` only if the endpoint surface or a documented cross-cutting pattern changed. If neither changed, record that explicitly rather than editing for the sake of it.
- [x] T056 File the follow-up arising from the T001 decision: move blueprint publication onto the dedicated `sorcha:blueprint-publish` derivation context, with a dual-accept transition for already-sealed publications. Reference this feature and #1591.
- [x] T057 Update `.specify/MASTER-TASKS.md` with Feature 196 status (📋 → 🚧 → ✅) and close #1591 with the run evidence from T052, stating explicitly that the peer gRPC surface remains open and is tracked separately (spec Out of Scope).
- [x] T058 Remove scratch and probe files created during implementation; confirm `git status` is clean of working artefacts before the PR.

---

## Dependencies

```text
Phase 1 (Setup / decisions)
    ├── T001 ✅ decided ────────────────────────► Phase 6 (US2)
    ├── T002 ✅ decided ────────────────────────► Phase 2
    └── T004 (informational — gates nothing)

Phase 2 (Foundational) ──► Phase 3 (US1) ──┐
                       ──► Phase 4 (US4) ──┤
                       ──► Phase 5 (US3) ──┼──► Phase 7 (Polish)
                       ──► Phase 6 (US2) ──┘

Within Phase 6:  T037 (discovery) ──► T038–T047
```

- **Phase 2 blocks everything.** The resolver is the single producer; stories consume it.
- **US1, US4, US3 are mutually independent** once Phase 2 lands and may proceed in any order or
  concurrently.
- **US2 depends internally on T037** (roster provisioning). T004 no longer gates it.
- Phase 7 requires all stories complete.

## Parallel Opportunities

- **Phase 2**: T008, T009, T012, T013 are `[P]` — separate files, no interdependencies. T010/T011 are
  sequential on the same file.
- **Phase 3**: T014–T018 all `[P]` (test authoring). Implementation T019–T021 is sequential.
- **Phase 4/5**: test-authoring tasks within each phase are `[P]`.
- **Phase 6**: T038–T042 are `[P]`, after T037 provisions the roster entry they assert against.
- **Across phases**: once Phase 2 completes, US1, US3 and US4 can be worked concurrently — but
  **never by two implementer agents on the same checkout**. Use separate worktrees or run serially.
- **Phase 7**: T050/T051 are `[P]` (different hosts); T053–T055 are `[P]` (different files).

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That alone closes the widest hole — the exemption with
the highest severity and no compensating check — and is independently shippable.

**Increment 2**: US4 then US3, both low-risk, reducing the surface before the riskiest work.

**Increment 3**: US2, including roster provisioning at register creation. De-risked by the
wipe-permitted decision — it is now a forward requirement rather than a migration.

## Task Summary

| Phase | Story | Tasks | Count |
|---|---|---|---|
| 1 — Setup & decisions | — | T001–T004 (T001, T002 done) | 4 |
| 2 — Foundational | — | T005–T013 | 9 |
| 3 — Genesis | US1 (P1) | T014–T022 | 9 |
| 4 — Field agreement | US4 (P3) | T023–T028 | 6 |
| 5 — Governance coupling | US3 (P2) | T029–T036 | 8 |
| 6 — Publication | US2 (P1, gated) | T037–T047 | 11 |
| 7 — Polish | — | T048–T058 | 11 |
| **Total** | | | **58** |
