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

## US2 — the SSR handover (this is #1400) — ⛔ on US1

- ⛔ **T013** Deploy to n1 and re-run the staged live gate. Scripts exist from the 2026-08-19 attempt:
  `.governance-livetest/us4-stage1-target-org.ps1`, `…stage2-add-steward.ps1`,
  `…restore-remove-steward.ps1`. Stage 2 must now seat the steward **keyed**.
- ⛔ **T014** Verify the seated key equals the steward's **slot-100** key — not its primary. This is
  the assertion that would have caught #1509 before it reached a transfer, so make it explicit rather
  than implied by a later step passing.
- ⛔ **T015** Propose the Transfer, collect both approvals (the ceremony Owner's server-side via
  #1465, the steward's externally at slot 100), and confirm it enacts by the roster head MOVING —
  never by the HTTP 200.
- ⛔ **T016** T063: the former Owner can no longer govern; the new Owner can. Both proven by a sealed
  docket.
- ⛔ **T017** Confirm the control record replicates to tiny, then re-run `rehearse.ps1 -Target n1` as
  an AIAS regression check.
- ⛔ **T018** Have a restore path ready before starting, and say what it is. The 2026-08-19 attempt
  needed one and had it (`Remove` under the Owner override); do not begin without it.

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
