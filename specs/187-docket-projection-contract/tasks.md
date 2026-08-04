# Feature 187 — Tasks

**Status:** 📋 NOT STARTED — scaffolded 2026-08-04, awaiting go-ahead
**Branch:** `feature/187-docket-projection-contract`

Legend: 📋 pending · 🚧 in progress · ✅ done · ⛔ blocked on a gate

---

## US1 — One projection, verified complete (#1370) — **UNBLOCKED, start here**

TDD order. T002 must be RED before T003.

- 📋 **T001** Read `DocketBuildTriggerService.cs` ~640-745 and `DocketSerializer.ToRegisterModel` (54-146) side by side; confirm the delta table in spec.md still matches source before changing anything.
- 📋 **T002** Write `DocketProjectionCompletenessTests` in `tests/Sorcha.Validator.Service.Tests/Services/`. Reflect over `TransactionMetaData`'s public properties; for a representative transaction carrying instanceId + routingDecision + a lifecycle `Type`, assert every property is populated. **Point it at `ToRegisterModel` first and verify RED** — it must name `InstanceId` and `RoutingDecision`. Record the RED output in the PR body.
  - *Per the verification-discipline rule: a guard written after the feature never ran RED. This one must.*
- 📋 **T003** Extract `DocketBuildTriggerService`'s inline `TransactionModel` construction into an `internal static` mapper (suggested: `Services/DocketRegisterProjection.cs`). No behaviour change on the A path — pure extraction.
- 📋 **T004** Repoint `DocketBuildTriggerService:~700` at the extracted mapper. Blueprint + Validator suites green.
- 📋 **T005** Repoint `ValidatorOrchestrator:231` and `DocketDistributor:182` at the extracted mapper.
- 📋 **T006** Delete `DocketSerializer.ToRegisterModel`. Update/retire `DocketSerializerTests` `ToRegisterModel` region (7 tests) and `DocketSerializerSenderWalletTests` (3 tests) — **the `GetSenderWallet` bech32-vs-base64url behaviour they pin is load-bearing (wave-11 audit bug); carry those assertions onto the extracted mapper, do not just delete them.**
- 📋 **T007** Re-point T002 at the extracted mapper; verify GREEN.
- 📋 **T008** Live check on n1: seal a docket via the normal path, confirm `RoutingDecision` + `InstanceId` present in Mongo, and an instance advances. *(F145's dormant-routing trap — a green suite does not prove the fold.)*

## US2 — Honest persistence record (#1371) — ⛔ **BLOCKED on Gate A**

- ⛔ **T009** **GATE A** — decide `Votes`: persist real `List<ConsensusVote>` (A1) or delete (A2). Record the decision and its reasoning in spec.md before proceeding.
- 📋 **T010** Add `ProposerValidatorId` and `MerkleRoot` to `Register.Models.Docket`.
- 📋 **T011** `Register.Service/Program.cs:1637-1650` — write `ProposerValidatorId` and `MerkleRoot` to their own fields; stop the `Votes` smuggle.
- 📋 **T012** `RegisterServiceClient.cs:344-351` — read both from their own fields; delete the `docket.Votes` read and the `MerkleRoot = string.Empty` stub.
- 📋 **T013** Apply the Gate A decision to `Votes`.
- 📋 **T014** Round-trip test: `DocketModel` → persist → read → `DocketModel` preserves every contract-declared field. Reflection-based, same shape as T002.
- 📋 **T015** Confirm no environment needs a read-side shim for dockets already carrying the proposer id in `Votes`; if re-genesis is the remedy, say so explicitly in the PR body.

## US3 — Docket self-verification (#1372) — ⛔ **BLOCKED on Gate B**

- 📋 **T016** Establish whether F079's `merkleRootConsistent` (`Program.cs:3186`) already cross-checks sealed-vs-recomputed. **This finding may decide Gate B — do it first.**
- ⛔ **T017** **GATE B** — persist + verify on read (B1), or explicit receipt cross-check (B2).
- 📋 **T018** Implement the Gate B decision. If B1, `MerkleRoot` is already added by T010 — this is the read-side verify + fail-loud.
- 📋 **T019** Test: a docket whose stored transactions have been altered fails verification instead of passing against a recomputed root.

## Close-out

- 📋 **T020** Update `.specify/MASTER-TASKS.md` with the shipped entry.
- 📋 **T021** Update the `sorcha-architecture` skill if the docket representation table or the seal-path projection changes shape.
- 📋 **T022** Disposition #1215 separately (delete-or-build the control-versioning subsystem) — do NOT fold it into this feature.

---

## Notes for the executing session

- **Do not collapse the docket types.** The shapes legitimately differ; see spec.md → Problem. The wallet-contracts precedent does not transfer.
- **US1 is independently shippable** and carries the most value. If credits or time are short, ship US1 alone as its own PR.
- Suggested PR split: US1 / US2 / US3 as three PRs — they have different blast radii and US2/US3 carry decision gates.
- One logical change per PR, per CLAUDE.md branch policy.
