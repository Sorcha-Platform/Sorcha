# Feature 193 — Keying a governance-added roster member

**Status:** 📋 Scoping — drafted 2026-08-19, NOT started
**Branch:** `feature/193-governance-member-keying`
**Origin:** #1464, reopened after the fix in #1509 was reverted in #1511
**Blocks:** #1400 (F189 US4 live gates T062/T063)

---

## Problem

A register's roster is the list of organisations entitled to govern it, and **authority is matched by
public key**. Genesis seats members with their key, because each one signs its own attestation during
the creation ceremony. **Governance has no equivalent**: `Add` seats a member with
`PublicKey = string.Empty`, and nothing ever fills it in.

The consequence is not that the member is slightly degraded — it is that the member **can never act**.
`GovernanceKeyMatcher.Matches` returns false for an empty key by design, so every approval it produces
is excluded from every tally, and every governance transaction it signs is refused
(`VAL_PERM_002`). It is entitled on paper and inert in fact.

The original code says the key "is recorded when they first attest". **No attest path was ever built.**

### Why this is the last thing blocking US4

F189 US4's premise is that the first governance act on the system register is an ordinary Transfer
replacing the ceremony Owner with a real organisation. `ValidateTransferProposal` requires the
transfer target to be an existing Admin — and the only route onto a roster after genesis is `Add`. So
the target is necessarily unkeyed, and transferring to it would hand the platform's root of trust to
an owner that cannot govern it.

`GovernanceRosterService.ApplyOperation` now **refuses to promote an unkeyed attestation** (#1509,
kept), so that disaster is unreachable. The register is safe and US4 is still blocked.

---

## Two fixes that do NOT work, and why

Both were tried. Recording them because both are the obvious first idea.

### ❌ Derive the key from the subject DID

A classical `ws1` address is Bech32 over `[network][publicKey]`, so the key can be read straight out
of `did:sorcha:w:{address}` — deterministic, needs no new field, and the proposer cannot forge it.

**It derives the wrong key.** The address encodes the wallet's **primary** key. A roster attestation
records the organisation's **slot-100** key (`sorcha:register-attestation`), which is what
`GovernanceSigningService` signs approvals with and what `GovernanceKeyMatcher` matches. Measured on
n1, one wallet:

| | |
|---|---|
| address-derived | `VHWQB/leUxacGAD0K16P/YwsupKRynJkpiUwVs7hYYQ=` |
| slot-100 governance key | `thfb6l9PJ/E2qJYvl2PHaWohph+cWfxLpAflFRHStQI=` |

A derived value is therefore **non-empty and wrong**, which is *worse than empty*: empty fails loudly
at the promotion guard, wrong passes every check and is excluded silently at tally time. Shipped in
#1509 and reverted in #1511 before it reached a transfer. `GovernanceAddLeavesMemberUnkeyedTests`
pins the reasoning so it is not re-derived.

### ❌ Resolve the key from the wallet service at enactment

The slot-100 key exists and the node can ask for it — but **enactment must be deterministic**. Every
node folds the same sealed proposal into the same roster bytes under the same transaction id. A node
that does not host that organisation's wallet would resolve nothing, and two nodes could resolve
differently. That is a ledger divergence, not a degraded lookup.

**⇒ The key must arrive IN sealed content.** That is the constraint the whole design hangs on.

---

## User stories

### US1 — An organisation added by governance can actually govern

**As** an organisation seated on a register's roster by a governance decision
**I want** my governance key recorded with my seat
**So that** my approvals count and my proposals seal, rather than being silently excluded.

Acceptance:

- After an `Add` enacts, the member's attestation carries its **slot-100** key.
- That member can raise a proposal that seals, and produce an approval that is counted in a tally.
- The key recorded is the one the member actually holds — an organisation cannot be seated with
  somebody else's key, and a proposer cannot choose it unilaterally.
- A member that cannot be keyed is seated **unkeyed and unpromotable** (today's behaviour) rather
  than keyed with a guess.

### US2 — SSR ownership can transfer (unblocks #1400)

**As** the platform operator
**I want** to transfer system-register ownership from the ceremony Owner to a real organisation
**So that** the root of trust is governed by an accountable organisation rather than a node key.

Acceptance:

- Seat a real `did:sorcha:w:` organisation as Admin on the SSR, keyed.
- Transfer ownership to it; the transfer reaches quorum and enacts.
- The new Owner can govern; the ceremony Owner can no longer.
- The control record replicates to tiny.

### US3 — An operator can see whether a member is keyed

**As** an operator diagnosing a register
**I want** the roster to tell me which members are keyed
**So that** "entitled but inert" is visible before it becomes a mystery.

Acceptance:

- `GET /registers/{id}/governance/roster` reports, per member, whether a governance key is recorded.
- It does not require reading the sealed docket out of MongoDB to find out.

> This is small and it is not cosmetic. #1464 was hard to see partly *because* the roster endpoint
> projects `Subject`, `Role`, `Algorithm` and `GrantedAt` — **and not the key**. During the live run
> the endpoint reported the genesis Owner with no key at all, which read as a broken SSR until the
> projection was checked.

---

## Decision gates — **ALL SETTLED 2026-08-19**

| Gate | Answer |
|---|---|
| **A** | **A1** — the proposal carries the target's slot-100 key plus an acceptance signature by that key |
| **B** | **B2** — consent to a specific seat, bound to `RosterSnapshotId` so an acceptance cannot be replayed |
| **C** | **C2** — verified in the Validator as well as the Register Service, sharing one implementation |
| **D** | **D1** — leave already-unkeyed members; they are inert and unpromotable |

## Decision gates — detail

### Gate A — How does the key reach sealed content?

- **A1 — The proposal carries it, with the target's acceptance signature.**
  `GovernanceProposalRequest` gains the target's slot-100 public key **and** a signature by that key
  over a canonical "I accept a seat on register X as role Y" statement. Enactment records the key;
  the signature proves the target holds it. One transaction.
  *Requires the target to sign before the proposal is raised — an out-of-band step between the two
  organisations.*

- **A2 — Two phases: seat unkeyed, then self-attest.**
  `Add` enacts as today; the seated member later calls a new
  `POST /registers/{id}/governance/attest`, signing with slot 100, which writes a control transaction
  keying itself. This is what the original comment described.
  *No prior coordination, member self-serves. But two transactions, and a real window in which a
  member is seated-but-inert — which is exactly today's failure, just now intentional and visible.*

- **A3 — The target's approval of its own Add carries the key.**
  Seating an organisation requires its consent, expressed as an approval; that approval necessarily
  carries its slot-100 key, so keying is free.
  *Elegant, and it makes consent a governance rule. But it changes `Add` semantics — an Owner could
  no longer seat anyone under the Owner override, which is how the SSR seats its first Admin.*

*Recommendation: **A1** (SETTLED — Stuart, 2026-08-19). It is the only one that seats a keyed member
in a single transaction, it proves possession rather than trusting the proposer, and it does not
change who may raise an `Add`. Note it DOES change who must take part: see Gate B.
A2 can be added later as a self-service path without invalidating A1. A3's consent rule is
attractive but should be argued on its own merits, not adopted because it happens to solve keying.*

### Gate B — What does the acceptance signature commit the target to?

> **This gate was originally written as "may an organisation be seated without its participation?"
> That is not a choice.** A1 requires a signature only the target can produce, so the target must be
> asked first — B follows from A and cannot be picked independently. The behaviour change is real and
> worth naming (today an Owner seats anyone unilaterally, and the target never touches the flow), but
> it is a consequence, not a decision. Reframed to the question actually left open.

An acceptance signature has to cover *something*. What it covers decides what it can be reused for.

- **B1 — Proof of possession.** The statement binds (register, key): *"this is my governance key for
  register X."* Simple, and the target need not care what role it is being offered.
  *But the same signature seats them at any role, at any later time — including re-seating an
  organisation that was deliberately removed.*

- **B2 — Consent to a specific seat.** The statement binds (register, subject, **role**, key):
  *"I accept an Admin seat on register X, with this key."* Re-seating at a different role needs a
  fresh signature, and the signature says what was agreed to rather than merely who holds a key.

Either way it needs a **replay binding**, or an acceptance is valid forever. The natural one already
exists: **`RosterSnapshotId`** — the roster head at proposal time, which proposals already carry
(FR-011b) so that an approval cannot be counted against a roster that has since changed. An
acceptance bound the same way is valid only against the roster it was produced for, so a removed
organisation's old acceptance cannot be replayed to seat them again.

*Answer: **B2 plus the RosterSnapshotId binding** (Stuart, 2026-08-19). The extra fields are free — the proposal
already knows the subject, role and snapshot — and it makes the ledger record what the organisation
agreed to, not just that it once held a key. Statement shape, mirroring
`GovernanceApprovalStatement`:*

```
sorcha:governance-seat-acceptance:v1 ␟ {registerId} ␟ {subjectDid} ␟ {role} ␟ {publicKey} ␟ {rosterSnapshotId}
```

### Gate C — Where is the acceptance signature verified?

- **C1 — Register Service only**, at propose time.
- **C2 — Register Service AND the Validator**, sharing one implementation.

*Recommendation: **C2**, in `Sorcha.Validator.Core` beside `DetachedApprovalVerifier`. A check that
only the proposing node performs is not a ledger rule — another node replaying the sealed
transaction would accept a key nobody proved. The existing verifier lives there for exactly this
reason, and its remarks say so: "two implementations of one rule is how the two would come to
disagree about whether a governance change is authorised."*

### Gate D — What happens to members already seated unkeyed?

n1 has none right now (the one seated during the T062 attempt was removed). Other installations may.

- **D1 — Leave them.** They are inert and unpromotable; re-`Add` them once this ships.
- **D2 — Repair path.** A2's self-attest doubles as the repair for an existing unkeyed member.

*Recommendation: **D1** for this feature, and note that adopting A2 later gives D2 for free.*

---

## Out of scope

- **Changing what key governance uses.** Slot 100 is the organisation's governance key; this feature
  records it correctly, it does not revisit the choice.
- **Delegated approvals** (#1395) — a separate grant path that fails closed today.
- **#1380**, a service principal signing as any organisation. Carrying a proven key narrows *this*
  gap; it does not address custody.

---

## Migration posture

Additive. New optional fields on `GovernanceProposalRequest`; existing genesis-keyed rosters are
untouched. The wire shape of a sealed `Add` changes (the attestation gains a real key where it had an
empty string), which is a **content** change, not a schema break — readers already handle both, since
every genesis attestation has always carried a key.

⚠ **The propose endpoint takes numeric enums only.** `operationType: "Add"` returns a **500** with a
bare "An error occurred" body (#1384, and the 500-where-a-400-belongs class of #1476). Anything
scripting this feature's gates must send `0`/`1`/`2`. Worth fixing alongside, since this feature adds
fields to the same request and every new caller will hit it.

---

## References

- #1464 — the defect; #1509 the wrong fix; #1511 the revert and the evidence
- #1400 — F189 US4, blocked on this
- `GovernanceApprovalStatement` — the canonicalisation precedent an acceptance statement should mirror
- `DetachedApprovalVerifier` (`Sorcha.Validator.Core`) — the precedent for where a ledger rule lives
- The live run of 2026-08-19 that measured the two keys and produced the revert
