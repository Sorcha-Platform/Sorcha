# Feature 187 — Tasks

**Status:** ✅ DONE — US1 (T001-T008, live-verified on n1), US2 (T009-T018), US3 (T019-T023). Close-out T024/T025 done; T026 (#1215) deliberately left separate.
**Branch:** `feature/187-docket-projection-contract`

Legend: 📋 pending · 🚧 in progress · ✅ done · ⛔ blocked on a gate

---

## US1 — One projection, verified complete (#1370) — **UNBLOCKED, start here**

TDD order. T002 must be RED before T003.

- ✅ **T001** Read `DocketBuildTriggerService.cs` ~640-745 and `DocketSerializer.ToRegisterModel` (54-146) side by side; confirm the delta table in spec.md still matches source before changing anything.
- ✅ **T002** Write `DocketProjectionCompletenessTests` in `tests/Sorcha.Validator.Service.Tests/Services/`. Reflect over `TransactionMetaData`'s public properties; for a representative transaction carrying instanceId + routingDecision + a lifecycle `Type`, assert every property is populated. **Point it at `ToRegisterModel` first and verify RED** — it must name `InstanceId` and `RoutingDecision`. Record the RED output in the PR body.
  - *Per the verification-discipline rule: a guard written after the feature never ran RED. This one must.*
- ✅ **T003** Extract `DocketBuildTriggerService`'s inline `TransactionModel` construction into an `internal static` mapper (suggested: `Services/DocketRegisterProjection.cs`). No behaviour change on the A path — pure extraction.
- ✅ **T004** Repoint `DocketBuildTriggerService:~700` at the extracted mapper. Blueprint + Validator suites green.
- ✅ **T005** Repoint `ValidatorOrchestrator:231` and `DocketDistributor:182` at the extracted mapper.
- ✅ **T006** Delete `DocketSerializer.ToRegisterModel`. Update/retire `DocketSerializerTests` `ToRegisterModel` region (7 tests) and `DocketSerializerSenderWalletTests` (3 tests) — **the `GetSenderWallet` bech32-vs-base64url behaviour they pin is load-bearing (wave-11 audit bug); carry those assertions onto the extracted mapper, do not just delete them.**
- ✅ **T007** Re-point T002 at the extracted mapper; verify GREEN.
- ✅ **T008** Live check on n1 — **PASSED 2026-08-05**. Branch image `validator-service:f187` deployed to n1 (rollback tag `:pre-f187` left in place), `./demos/AIAS/rehearse.ps1 -Target n1` PASSED all three paths (approval issued a credential; both rejections recorded with the on-brand reason). Register `020fa3d1…` went **162→168 txs / 161→167 dockets**, and every newly sealed tx carries `InstanceId=YES routing=PRESENT tracking=3-4 recips=2 sender=ws11q…` with instances advancing action 1→2.
  - **Runtime finding (closes the open question from the issue):** the n1 *baseline*, before deploying, already showed `instanceId=YES routingDecision=PRESENT`. Only projection A produces those, so **path A is confirmed as the live seal path on n1**. US1 is therefore **hardening** on the normal path — the drift was real but latent in the admin (`ValidatorOrchestrator`) and gRPC (`DocketDistributor`) routes, which would have sealed `instanceId=null routingDecision=null`.

## US2 — Honest persistence record (#1371) — ✅ **Gate A RESOLVED = A1 (persist votes)**

Ordered so the wire contract exists before anything tries to fill it.

- ✅ **T009** **MOVE, not mirror.** Investigation showed the validator's `ConsensusVote` is neither a superset nor a working model — every member is immutable evidence, with none of the transient state its sibling types carry (`Transaction.Priority`/`AddedToPoolAt`/`RetryCount`, `Docket.Status`). It was ledger evidence in the wrong assembly, so it MOVED to `Sorcha.Register.Models` rather than being duplicated as a parallel wire type. `Signature` moved with it (its own docs already said it is "persisted to the Register Service as part of the blockchain ledger") and was renamed **`RegisterSignature`** — a bare `Signature` in a broadly-imported namespace is the generic name that let this family of collisions accumulate.
  - Also renamed for clarity, same commit: `Register.Models.Docket`→`DocketHeader`, `Register.Models.ValidatorSignature`→`ReceiptSignature`, `Validator.Service…Interfaces.ValidatorSignature`→`CollectedSignature`.
  - Gate `scripts/check-consensus-vote-contract.ps1` extended to all three canonical types (`VoteDecision`, `ConsensusVote`, `RegisterSignature`), matching both `class`/`enum` and `record` forms. Mutation-tested: FAIL naming all three when re-declared, PASS when removed.
- ✅ **T010** Add `ProposerValidatorId`, `MerkleRoot` and `List<ConsensusVote> Votes` to `Register.Models.Docket`. **Remove the old `string? Votes`** and its false doc-comment.
- ✅ **T011** Add `Votes` to `DocketModel` and `WriteDocketRequest` — neither carries votes today, so the contract must gain the field before either projection can populate it.
- ✅ **T012** `DocketBuildTriggerService:~354` — copy `consensusResult.Votes` onto the docket before the write. **Currently discarded**; path A has the result in hand and drops it. Mirrors `ValidatorOrchestrator:223-224`.
- ✅ **T013** Extend the unified mapper (from T003) to carry `Votes`, `ProposerValidatorId`, `MerkleRoot`. T002's completeness test should now cover them.
- ✅ **T014** `Register.Service/Program.cs:1637-1650` — write `ProposerValidatorId`, `MerkleRoot`, `Votes` to their own fields; **stop the `Votes = request.ProposerValidatorId` smuggle**.
- ✅ **T015** `RegisterServiceClient.cs:344-351` — read each from its own field; delete the `docket.Votes` read and the `MerkleRoot = string.Empty` stub.
- ✅ **T016** Round-trip test: `DocketModel` → persist → read → `DocketModel` preserves every contract-declared field. Reflection-based, same shape as T002.
- ✅ **T017** **Single-validator mode must stay valid.** `DocketBuildTriggerService:~392` writes directly with no consensus engine (this is what n1 and local dev run) — assert an **empty vote list persists cleanly and is not an error**. Do NOT add a guard rejecting empty votes.
- ✅ **T018** *(Stuart: "I'm fine with a wipe and regenesis", 2026-08-05.)* No read-side shim. Existing dockets carry the proposer id in the old `string? Votes`; the remedy is **re-genesis**, not a compatibility path. Confirm no environment needs a read-side shim for dockets already carrying the proposer id in the old `string? Votes`; if re-genesis is the remedy, state that explicitly in the PR body.

## US3 — Docket self-verification (#1372) — ✅ **Gate B RESOLVED = both, scoped**

`MerkleRoot` is persisted by T010. This story is the verification side.

- ✅ **T019** **Done first, and the answer inverts the task's own premise: there is nothing to extend.** `merkleRootConsistent` (`ReceiptValidator.cs:105-110`) compares `receipt.MerkleRoot` against `receipt.InclusionProof.MerkleRoot` — two fields of the SAME caller-supplied object. It never reads the register. A receipt whose two roots agree passes it regardless of what this ledger sealed, so the entire verdict was decidable without touching the ledger. It reads like the check #1372 asks for and is not one.
- ✅ **T020** One rule (`Verification/DocketMerkleCommitment`), five sites. **Generation refuses**: `GET /transactions/{txId}/inclusion-proof`, `GET /credentials/{id}/anchor` and `POST /proofs/inclusion` return **409** when the stored contents do not reproduce the sealed root — a proof against a root the ledger never sealed verifies perfectly and is therefore worse than no proof. **Verification reports a tri-state**: `POST /inclusion-proofs/verify`, `POST /receipts/verify` and `POST /proofs/verify-inclusion` gained `ledgerAnchored` (`verified`/`failed`/`null`) + a reason. A contradicted anchor flips `isValid`; an UNVERIFIABLE one does not — absence of evidence is not evidence of tampering.
  ⚠ **`POST /proofs/inclusion` had to be fixed before it could be checked at all.** It built its tree from RAW TRANSACTION IDS while the validator seals composite `(id, payloadHash, timestamp)` leaves, so its root could never equal the sealed one — on any docket, on any register — and a naive cross-check bolted on would have reported tampering everywhere. Its `MerkleRoot` was also emitted as hex and parsed as base64 by `/verify-inclusion`; a 64-char hex string is valid base64, so it decoded to 48 wrong bytes instead of throwing. The pair never round-tripped and never said so. Nothing in-tree calls either endpoint.
- ✅ **T021** Confirmed by call-site audit: `DocketMerkleCommitment` is referenced from exactly three places that recompute (`BuildInclusionProofAsync`, `POST /proofs/inclusion`, and the F188 `DocketEvidenceAssembler` — already an on-demand admin surface). No docket list or get path touches it.
- ✅ **T022** `DocketMerkleCommitmentTests` (11) + `InclusionProofLedgerAnchorTests` (4, real handlers). The tamper test **executes the attack before showing the catch** — it asserts the altered set really does produce a self-consistent root that differs from the sealed one, then that the comparison fails. Without that first half it would pass just as well if tampering were impossible. Six mutations verified: leaves-by-store-order (3 red), blank-root-as-verified (1), `IsAnchored` as `!= Failed` (2), leaves-as-raw-ids (2), drop the 409 (1), always-"verified" (1).
- ✅ **T023** Cost reasoned, and it is close to nothing. **Generation adds no I/O**: the proof path already fetched the docket's transactions, so the only new work is one Merkle recomputation over a set it was already hashing. **Verification adds one indexed docket-header read and no hashing at all** — the sealed root is a stored field, so the comparison is a string equality. Nothing was narrowed because nothing needed to be.

## Close-out

- ✅ **T024** Update `.specify/MASTER-TASKS.md` with the shipped entry.
- ✅ **T025** Update the `sorcha-architecture` skill if the docket representation table or the seal-path projection changes shape.
- 📋 **T026** Disposition #1215 separately (delete-or-build the control-versioning subsystem) — do NOT fold it into this feature.

---

## Notes for the executing session

- **Do not collapse the docket types.** The shapes legitimately differ; see spec.md → Problem. The wallet-contracts precedent does not transfer.
- **US1 is independently shippable** and carries the most value. If credits or time are short, ship US1 alone as its own PR.
- Suggested PR split: US1 / US2 / US3 as three PRs — they have different blast radii and US2/US3 carry decision gates.
- One logical change per PR, per CLAUDE.md branch policy.
