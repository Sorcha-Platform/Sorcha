# Feature 193 — Tasks

**Status:** ✅ US1 COMPLETE + tested (2026-08-19). ⛔ US2 (the live SSR handover, #1400) needs a deploy.
Gates **A1 / B2 / C2 / D1**.

**The design changed during implementation**, and Gate A's text predates it. The acceptance is signed by the subject's **PRIMARY** key, not by the governance key it nominates — writing the escalation test showed a signature by the governance key proves only that SOMEBODY holds it, so a proposer could seat another organisation carrying the proposer's own governance key and vote twice. The primary key is checkable against the subject (a `ws1` address is Bech32 over `[network][publicKey]`); the slot-100 key is not, which is why it is NAMED rather than used to sign.
**Branch:** `feature/193-governance-member-keying`

Legend: 📋 pending · 🚧 in progress · ✅ done · ⛔ blocked on a gate

Written for **A1** (the proposal carries the target's slot-100 key plus an acceptance signature by
that key), **C2** (the Validator verifies it too) and **D1** (leave already-unkeyed members).

**B2**: the acceptance is consent to a SPECIFIC SEAT, bound to the roster it was produced for, so it
cannot be replayed to re-seat a removed organisation or to seat one at a role it never agreed to:

```
sorcha:governance-seat-acceptance:v1 ␟ {registerId} ␟ {subjectDid} ␟ {role} ␟ {publicKey} ␟ {rosterSnapshotId}
```

---

## Preflight — cheap, unblocked

- ✅ **T001** Confirm the two keys are still distinct on a current node: sign a probe at
  `sorcha:register-attestation` and with no path, and compare against the wallet's reported
  `publicKey`. One command, and it is the whole premise of the feature. (Measured 2026-08-19:
  `thfb6l9P…` vs `VHWQB/…`.)
- ✅ **T002** Enumerate every writer of `RegisterAttestation.PublicKey` and confirm the two `Add`
  sites are still the only ones leaving it empty — `GovernanceEnactmentService.ProjectRoster` and the
  propose-and-enact override in `Program.cs`. A third would need the same treatment.
- ✅ **T003** Check whether any live register carries an unkeyed member (Gate D). n1 had none as of
  2026-08-19; do not assume that of other installations.

## US1 — a governance-added member can govern — 📋 unblocked (T005 pends Gate B's detail)

TDD order. T004 must be RED before T006.

- ✅ **T004** Write the end-to-end test: `Add` with a valid acceptance signature seats a member whose
  attestation carries the **slot-100** key, and that member's approval is then **counted in a tally**.
  Assert the tally, not just the stored bytes — the stored key mattering is the whole point, and a
  test that only checks storage would pass on a key nobody can use. **Verify RED first.**
- ✅ **T005** Add `GovernanceSeatAcceptanceStatement` to `Sorcha.Register.Models`, mirroring
  `GovernanceApprovalStatement`: a versioned, unit-separated canonical string over
  (register id, subject, role, key) and a `ComputeDigest`. **One implementation** — the producer and
  both verifiers must call it, never rebuild it.
- ✅ **T006** Extend `GovernanceProposalRequest` with `TargetPublicKey` + `TargetAcceptance`
  (signature + algorithm). Optional on the wire so `Remove`/`Transfer` are unaffected; **required for
  `Add`** and refused with a 4xx when absent.
- ✅ **T007** Record the carried key at both `Add` sites, replacing `string.Empty`.
- ✅ **T008** Verify the acceptance signature in `Sorcha.Validator.Core`, beside
  `DetachedApprovalVerifier`, and call it from BOTH the Register Service propose path and the
  Validator's control-transaction validation (Gate C2). A rule only the proposing node enforces is
  not a ledger rule.
- ✅ **T009** Refuse an `Add` whose acceptance signature does not verify against the carried key, or
  whose carried key is empty — with a named reason, never a silent drop (FR-011c).
- ✅ **T010** Re-run T004; verify GREEN.
- ✅ **T011** **Mutation checks**, all three: (a) record the primary key instead of the carried one —
  the tally test must fail; (b) skip the acceptance verification — a forged-key test must fail;
  (c) verify in the Register Service but not the Validator — the per-node recount test must fail.
  (c) is the one that proves C2 was worth doing.
- ✅ **T012** Full suites: Register.Core, Register.Service, Validator.Service, Validator.Core.

## US2 — the SSR handover (this is #1400) — ✅ ALL GATES PASSED 2026-08-19

- ✅ **T013** Deployed and re-run 2026-08-19 on a **clean-genesis** n1 (`down -v`, volumes removed,
  re-genesised on the compiled-in anchor `d75e14004364867dae55f44330330edf`). Stuart's call, and it
  paid: it removed "relics from a previous installation" as a competing explanation for everything
  that followed. Scripts: `.governance-livetest/us4-stage{1,2,2b,3}-*.ps1`.
- ✅ **T014** Asserted explicitly, and it held: the seat carried
  `YY8366ghcJ9oGj273a60ucI4gMZqNITI0vmKEN86I4w=` (slot 100), **not** the primary
  `WTwFBgjtJc26HjlZwDqLTipNlQy2Xs+7h5NQL8CJL5g=`. `us4-stage2b-assert-key.ps1` reads the sealed
  control transaction out of Mongo and fails on the primary key BY NAME, which is what makes it a
  #1509 regression test rather than a "a key is present" check.
- ✅ **T015** **T062 PASSED.** Quorum `2/2`, pool 2, `isOwnerOverride: false` — FR-010's Transfer
  exclusion held. Sealed as **docket 12** proposal → **docket 13** carrying BOTH approvals →
  **docket 14** enactment; ownership moved `did:sorcha:genesis:a3dd941f…` →
  `did:sorcha:w:ws11qpvncpgx…`. Verified from the docket chain and the roster head, not from HTTP.
- ✅ **T016** **T063 PASSED.** The new Owner proposed `Remove(genesis)` and received the **Owner
  override** (`votesRequired: 1`), which sealed as **docket 15** — the ceremony key is off the roster.
  The former Owner is now refused at the API: `400 "Proposer 'did:sorcha:genesis:a3dd941f…' is not in
  the roster"`. A clean refusal, not the 202-then-silence shape.
  First attempt failed on **#1515** (fixed in #1516, verified live on the v2.984.1 deploy). Worth
  keeping: the override was granted even while #1515 was live, so the failure was blueprint
  resolution, never authority — run `scratchpad/check-1515.sh` before blaming this gate.
- ✅ **T017** Both done. **tiny replicates byte-identically** — 16 transactions across dockets 1-15,
  `diff` clean against n1, including the transfer (12-14) and the removal (15). ⚠ tiny had to be
  fully cleared first (`down -v` + fresh images): its pre-re-genesis residue presented as a spliced,
  unlinked chain and I filed it as #1519 before Stuart pointed out the node had to be cleared before
  any assessment. Closed after the clean run. **AIAS rehearse PASSED** on the re-provisioned node —
  approval issued a credential; both rejection paths recorded none.
- ✅ **T018** Held throughout: `Remove` under the Owner override, which is exactly what T063 then
  used deliberately. Also applied to the test design — T063's second half proposes a **Transfer**
  rather than `Remove(steward)`, because if the authority check were broken a `Remove` would strip
  the last member and leave the SSR with zero members, permanently ungovernable. Never point a live
  test at an unrecoverable state to find out whether a guard holds.

## US3 — roster diagnosability

- 📋 **T019** Add a per-member `keyed` boolean (or the key itself — it is public) to
  `GET /registers/{id}/governance/roster`. Unblocked by the gates and independently useful: during
  the live run the endpoint's silence about keys read as a broken SSR.

## Close-out

- ⛔ **T020** Update `.specify/MASTER-TASKS.md`, the `sorcha-architecture` skill's F189 section, and
  the `f189-approval-surface-workstream` memory. Close #1464; unblock or close #1400.

---

## Notes for whoever picks this up

- **The premise is one measurement.** Roster authority matches the slot-100 key; an address encodes
  the primary key. If T001 ever shows them equal, stop — the whole design is unnecessary and
  something else has changed.
- **A non-empty wrong key is worse than an empty one.** Empty fails loudly at the promotion guard;
  wrong passes every check and is excluded silently at tally time. Any change here should be judged
  against that, not against "is the field populated".
- **Assert the tally, not the storage.** The bug this feature fixes is invisible to a test that only
  checks what was written.
- **Keep the promotion guard.** `ApplyOperation` refusing to promote an unkeyed attestation stays
  regardless of which gate answer wins; it is what makes the bad end-state unreachable rather than
  merely unlikely.
