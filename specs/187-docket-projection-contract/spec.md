# Feature 187 — Docket projection & persistence contract

**Status:** 📋 Planned — scaffolded 2026-08-04, NOT started
**Branch:** `feature/187-docket-projection-contract`
**Issues:** #1370 (duplicate lossy projection), #1371 (persistence contract), #1372 (Merkle self-verification)
**Origin:** investigation of #1215 (control-transaction detection never matches)

---

## Problem

The `Docket` concept has five representations. Each has a genuine role, but the **joins between them are hand-maintained, duplicated, and unverified** — and three separate incidents have already been caused by exactly that.

| # | Type | Location | Role | Transactions |
|---|---|---|---|---|
| 1 | `Docket` | `Validator.Service.Models` | consensus working set | `List<Transaction>` inline |
| 2 | `DocketDto` *(private record)* | `DocketSerializer` | Redis / peer-broadcast bytes | `List<TransactionDto>` |
| 3 | `DocketModel` | `ServiceClients.Http/…/IRegisterServiceClient.cs` | HTTP write contract | `List<TransactionModel>` inline |
| 4 | `Docket` | `Register.Models` | Mongo-persisted **header** | `List<string> TransactionIds` only |
| 5 | `DocketDto` *(private record)* | `UI.Core/Services/Admin/DocketService` | UI read model | — |

**This feature does NOT collapse these into one type.** #1 legitimately differs from #4 — mempool `RetryCount` and `ConsensusVote` have no place in a persistence record, and the register's normalisation (transactions as separate documents, docket as a header over them) is correct and stays. The `Sorcha.Wallet.Contracts` precedent does **not** transfer, because those DTOs were genuinely identical shapes hand-copied; these are not.

What transfers from that precedent is the **gate idea, applied to the projection rather than the type**.

### Prior incidents at this seam

All three were silent, both sides individually correct, and found only by live execution:

1. `InstanceId` never carried through the seal → every F145 Tier-3 lookup returned empty.
2. `TrackingData` omitted → a recovering node rejected every blueprint with `no_provenance`.
3. `LastAppliedTxId` dropped by `EfCoreInstanceStore.UpdateAsync`'s hand-copy list → F145 replay guard dead, F142 go-live gate unearnable.

#1370 is the fourth instance of the same class, still open.

---

## User stories

### US1 — One projection, verified complete *(issue #1370)*

**As** a node operator, **I want** every seal path to persist identical transaction metadata, **so that** which entry point drove the seal cannot change what lands on the ledger.

`DocketSerializer.ToRegisterModel` is a strictly-worse copy of `DocketBuildTriggerService`'s inline projection. It drops `InstanceId` and `RoutingDecision`, and collapses five `TransactionType` members to `Action`.

**Acceptance:**
- One `internal static` mapper is the sole `Validator.Models.Docket` → `DocketModel` projection.
- `ValidatorOrchestrator` and `DocketDistributor` use it; `ToRegisterModel` is deleted.
- A projection-completeness test reflects over `TransactionMetaData`'s properties and fails if any is unpopulated for a representative transaction.
- The completeness test is proven RED against the pre-fix mapper before the fix lands (it must name `InstanceId` and `RoutingDecision`).

**No behaviour change on the A path** — it is already correct. This is B being brought up to it.

### US2 — An honest persistence record *(issue #1371)*

**As** a maintainer, **I want** the persisted docket to carry the fields the contract carries, **so that** the read path stops inventing values.

`Register.Service/Program.cs:1649` writes `Votes = request.ProposerValidatorId`; `RegisterServiceClient.cs:348` reads it back out as `ProposerValidatorId`. It round-trips **by accident**, through a field whose name, type and doc-comment (*"Consensus votes (implementation TBD)"*) all disagree with its contents.

**Acceptance:**
- `Register.Models.Docket` carries `ProposerValidatorId` and `MerkleRoot` as first-class fields.
- The `Votes`-as-proposer-id smuggle is gone in both directions.
- `Votes` is either a real persisted `List<ConsensusVote>` or deleted — see **Gate A**.
- Round-trip test: `DocketModel` → persist → read → `DocketModel` preserves every field the contract declares.

### US3 — A docket that can verify itself *(issue #1372)*

**As** an auditor, **I want** a sealed docket's own Merkle commitment on the record, **so that** integrity does not depend on recomputing from data whose integrity is the question.

Today the sealed root is discarded and recomputed on demand (`Program.cs:2976`). Recomputation over altered data yields a different-but-self-consistent root that inclusion proofs verify against. The sealed value survives only in F079 `TransactionReceipt.MerkleRoot`, in a separate store, and nothing cross-checks automatically.

**Acceptance:** per **Gate B**.

---

## Decision gates — resolve BEFORE executing the affected story

Execution must not guess these. US1 is unblocked and can proceed immediately.

### Gate A (blocks US2) — what happens to `Votes`?

Consensus votes are currently **never persisted** — `ConsensusVote` exists only in `Validator.Service` / `Validator.Core`. So "this docket achieved quorum, here are the signed votes" is not recoverable from the ledger.

- **A1 — Persist them.** Quorum evidence becomes auditable after the fact. Larger change; adds a real `List<ConsensusVote>` to the persisted shape.
- **A2 — Delete `Votes`.** Consensus is a liveness mechanism, not a ledger record; the sealed docket hash plus validator roster is the evidence. Cheaper, and honest.

*Recommendation: A2 unless quorum evidence is wanted for audit, in which case A1. Either is defensible — the current state is neither.*

### Gate B (blocks US3) — persist or cross-check?

- **B1 — Persist `MerkleRoot`** on the docket and verify recomputed-vs-sealed on read, failing loud on mismatch.
- **B2 — Keep recomputation** and make the receipt cross-check an explicit, tested integrity path, documenting that docket integrity derives from receipts.

*Recommendation: B1 — cheaper, and matches what the wire contract already carries. Before choosing B2, confirm whether F079's `merkleRootConsistent` (`Program.cs:3186`) already performs the cross-check.*

---

## Out of scope

- Collapsing the five docket representations into one type or a shared base class (see Problem — the shapes legitimately differ).
- The `DocketSerializer` Redis/broadcast DTO field drops (`RecipientsWallets`, `PreviousTransactionId`, `SequenceNumber`). **Latent** — `DeserializeFromBytes` has zero callers. Noted here so it is not rediscovered as new; file separately if `DeserializeFromBytes` is ever wired up.
- #1215 itself. It becomes trivial after US1 and should be dispositioned separately (the control-versioning subsystem is unreachable at both ends — delete-or-build is its own decision).
- The other duplicate-name candidates surfaced by the scan and **not yet checked**: `ValidationError` ×5, `VerificationResult` ×4, `ValidationResult` ×4, `ParticipantInfo` ×3, `CredentialStatus` ×3.

---

## Migration posture

Pre-release, so per CLAUDE.md §19 schema changes fold into the existing shape rather than shipping deltas. This is **Mongo**, not EF — a document-shape change plus re-genesis, not a migration.

⚠ Existing persisted dockets carry the proposer id in `Votes`. Re-genesis is the intended remedy. Confirm no environment (n1, tiny) needs a read-side shim before removing the smuggle.

---

## References

- `sorcha-architecture` skill — F145 (ledger-derived instances), F111 (presentation lifecycle traps)
- CLAUDE.md §19 (migration policy), §16 (cross-boundary contracts have one home)
- Guard precedent: `EfCoreInstanceStoreUpdateRoundTripTests` — reflection over the whole model, because the defect is the hand-maintained copy list
