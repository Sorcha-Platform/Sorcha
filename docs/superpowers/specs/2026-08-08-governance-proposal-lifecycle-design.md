# Governance proposal lifecycle — separating propose from enact

**Feature**: 189 (org-signed governance)
**Status**: proposed, for review — nothing built
**Depends on**: T075 (approvals as action submissions, landed `45b91707`)
**Blocks**: T043, T044, T045, T046, T048, T049, T082, T085, and any live proof of T075

---

## Problem

`/propose` does not raise a proposal. It raises **and enacts** one, in a single transaction.

Step 5 of the endpoint evaluates quorum at raise time and returns `400 "Quorum not met"` unless the
Owner override fires or every approval is supplied inline. If it passes, steps 6–7 apply the
operation and write the **updated roster** into the same payload.

Three consequences follow, and all three block the external approval surface:

1. **A multi-party proposal cannot be recorded at all.** The only way past step 5 is to already hold
   every signature — which is the server-side, all-at-once model R-014 withdrew.
2. **An enacting proposal invalidates itself.** Because it carries a populated roster, it becomes the
   newest roster-bearing control transaction, so `GetCurrentRosterAsync` returns it as
   `LastControlTxId`. Its own `RosterSnapshotId` still names the *previous* control transaction, so
   the FR-011b comparison (`RosterSnapshotId != LastControlTxId` ⇒ `VAL_PERM_009 roster-changed`) is
   true the instant it seals. An approval arriving afterwards has nothing valid to attach to.
3. **A proposal is born `Approved`.** There is no state an approval could move it out of.

So the approval carry built in T075 is correct and currently unreachable: there is no proposal in a
state that can receive one.

### The evidence

Not a code trace — the live fixture register on n1 (`cbb1fa4c…`), decoded from Mongo:

```
73fb4c4ece7d docket=0  attestations=1  validators=1  op=-    snapshot=-            status=-
360bf52a115d docket=1  attestations=2  validators=0  op=Add  snapshot=73fb4c4ece7d status=Approved
```

The proposal in docket 1 carries **two** attestations — the `Add` already applied — names the genesis
as its snapshot, and is `Approved`. Exactly the shape described above.

---

## Decision

Split the single transaction into three kinds on the governance chain, distinguished by **signed
payload content**, never by metadata (metadata sits outside the signature, the payload hash and the
docket merkle leaf — the C-VAL finding that moved the lifecycle predicates onto `Payload.type`).

| Kind | Payload | Carries a roster? | Moves `LastControlTxId`? |
|---|---|---|---|
| **Proposal** | `ControlTransactionPayload { Operation (Status=Pending), Roster: null }` | no | **no** |
| **Approval** | `GovernanceApprovalActionPayload` (built, T075) | no | no |
| **Enactment** | `ControlTransactionPayload { Operation (Status=Recorded), Roster: updated }` | yes | yes |

**A proposal carrying no roster is what makes the whole thing work**, and the mechanism already
exists: `GovernanceRosterService.GetCurrentRosterAsync` walks control transactions newest-to-oldest
and *skips* any whose payload has no populated roster. That loop was added defensively for an
unrelated F142 bug (a stray non-roster control transaction wiping the publish gate's view of the
roster). It means a pending proposal is invisible to roster reconstruction for free — so a proposal
neither invalidates itself nor any sibling proposal, and FR-011b keeps meaning what it should: *a
genuine roster change* invalidates open proposals, and nothing else does.

---

## Architecture

### The chain

```
genesis ──▶ proposal (Pending, no roster)
                 ├──▶ approval (org A)      ─┐
                 ├──▶ approval (org B)       ├─ siblings; the fork check exempts a Control predecessor
                 └──▶ approval (org C)      ─┘
                                             └──▶ enactment (Recorded, updated roster)
```

Approvals chain off the proposal (already implemented in T075). The enactment chains off the roster
head, because it is the roster mutation.

### Quorum is recounted from sealed approvals, never from the payload

Today `RightsEnforcementService` verifies `operation.ApprovalSignatures` carried **inside** the
enacting payload. That cannot survive the split: with approvals as separate transactions, an
enactment's payload would have to embed a copy of them.

**Embedding is rejected, and the argument is decisive.** Two nodes could observe different subsets of
sealed approvals at the moment they enact, embed different lists, and produce **different payload
bytes for the same deterministic transaction id**. R-009's "every node folds identically" fails at
exactly the point it matters most. So:

> The validator loads the sealed approval transactions for the proposal, rebuilds the v2 statement
> from the **proposal's stored operation**, verifies each signature, counts distinct approvers that
> are on the snapshot roster, and compares against the threshold — reusing
> `GovernanceRosterService.ValidateQuorumAsync` for the arithmetic (R-007), never reimplementing it.

This is T044 and the enforcement half of T079 landing together, and it is the substantive validator
change in this design.

### ⚠ The enactment payload must be byte-deterministic

`ApplyOperation` stamps `GrantedAt = DateTimeOffset.UtcNow` on a new attestation, and the validator
operations stamp `AuthorizedAt` / `RevokedAt` the same way. Two nodes enacting the same proposal
would produce different bytes, and therefore the same transaction id with a different payload hash —
a conflicting duplicate that reads as a fork rather than as the idempotent resubmission it is.

**Every timestamp in an enactment must be derived from sealed content**, not from the clock. The
proposal's `ProposedAt` is the natural source: it is inside the payload, inside the approval digest,
and identical on every node. This is a small change with no visible symptom until two nodes enact
concurrently, which is precisely the class of defect this feature keeps producing.

### Who submits the enactment

Three options; the recommendation is **(b)**.

- **(a) The approving client's own request.** Simplest, but quorum is only knowable once the approval
  *seals*, so the request would have to wait for a seal it does not control. Rejected.
- **(b) A reaction on `docket:confirmed`.** The Register Service already subscribes to that channel
  (`RegisterEventBridgeService`, currently SignalR-only). On each seal, check whether any open
  proposal on that register has reached quorum from sealed approvals; if so, submit the enactment
  with a deterministic transaction id so concurrent nodes dedupe rather than fork. Matches R-009 —
  the trigger is sealed content, so every node reaches the same answer. **Cost**: the Register
  Service gains its first governance background reaction.
- **(c) An explicit `POST /proposals/{id}/enact`.** Auditable and simple, but leaves a register in a
  quorum-met-but-not-enacted limbo until somebody calls it — the "quorum met, enactment fails →
  never limbo" row of the design's failure table, reintroduced as a design feature.

### The Owner override — two options, recommendation is **(1)**

1. **Keep today's single transaction.** An Owner-override change stays exactly one control
   transaction: propose-and-enact, unchanged bytes, unchanged behaviour. The split applies only where
   quorum is genuinely required. **This preserves the only live-proven path in the feature (T093) and
   FR-031's unattended single-owner property, at the cost of two enactment shapes in the code.**
2. **Always propose then enact.** One model everywhere; an Owner-override change becomes two control
   transactions submitted from one request. Cleaner conceptually, but it doubles the seal latency of
   the most-used governance operation, changes what every single-owner register's ledger looks like,
   and puts the no-regression gate (T086) at risk for a tidiness gain.

### API surface

| Endpoint | Change |
|---|---|
| `POST /governance/propose` | On quorum-not-met, **record a pending proposal** (`202`) instead of `400`. Owner-override path unchanged (`200`). |
| `POST /governance/proposals/{id}/approve` | New (T045). Verify via `IDetachedApprovalVerifier`, carry via `IGovernanceApprovalActionSubmitter`. `202` / `400` bad signature / `403` not on the snapshot roster / `409` not open or expired / `422` bad authorisation / idempotent repeat. |
| `GET /governance/proposals` | Add a status filter (T046). Status is derived from sealed content, never stored. |
| `GET /governance/proposals/{id}` | New — full audit detail: operation, approvals so far with their accountable individuals, outcome and reason (T043/T046). |

**Status is derived, not stored.** `Open` while the proposal is the newest thing chained to it and
unexpired; `Enacted` once an enactment referencing it has sealed; `Invalidated` when
`RosterSnapshotId != LastControlTxId`; `Expired` past `ExpiresAt`. A stored status is a second source
of truth that drifts from the ledger — the thing R-009 exists to prevent.

### The validator carve-out a pending proposal needs

`VAL_PERM_005` refuses a non-Owner `Add`/`Remove` carrying no approval signatures — which is exactly
what a pending proposal *is*. The carve-out keys on `Operation.Status == Pending`: a proposal enacts
nothing, so there is nothing for quorum to authorise. `Status` is inside the signed payload and is
already excluded from the approval digest (it is lifecycle, not content), so this is safe: a
submitter who sets `Pending` to dodge the check gets a transaction that changes no roster.

---

## Failure cases

| Case | Behaviour |
|---|---|
| Proposal raised without quorum | Recorded `Pending`. **No roster written.** |
| Approval arrives for an unknown or unsealed proposal | `404` / `409`. Never carried. |
| Approval from an organisation not on the snapshot roster | `403`, reason `refused-not-on-roster`. Never a silent drop (FR-011c). |
| Repeat approval | Idempotent — the deterministic transaction id collides and dedupes (built in T075). |
| Roster genuinely changes while a proposal is open | `Invalidated`. Signatures stay valid bytes; the proposal dies (FR-011b / SC-010). |
| Two nodes enact concurrently | Same deterministic id, byte-identical payload ⇒ idempotent resubmission. **Only true if every timestamp is derived from sealed content.** |
| Quorum met, enactment rejected by the validator | Terminal, with the validator's reason recorded. Never limbo. |
| Proposal expires with approvals collected | `Expired`. A late approval cannot enact it (FR-012). |

---

## Testing

**Unit, mutation-verified.** Every new guard perturbed until the *named* test goes red, then
restored — a guard written after the code has never run red and may be vacuous.

- A pending proposal payload carries **no** roster, and `GetCurrentRosterAsync` skips it. *Mutation:
  write the current roster into a pending proposal and watch `LastControlTxId` move.*
- Two independently-built enactments for the same proposal are **byte-identical**. *Mutation:
  restore `DateTimeOffset.UtcNow` in `ApplyOperation` and watch it fail.* This one cannot be caught
  by inspection.
- Quorum recount reads sealed approval transactions, not the payload; a forged approval that is not
  a sealed transaction does not count.
- SC-010: under `Unanimous` with one approver outstanding, removing that approver **invalidates**
  rather than enacts.

**Live gates**, in order — do not batch them:

1. Multi-party proposal raised on n1 is recorded `Pending`, seals, and does **not** move the roster.
2. An approval carried by T075's submitter seals against it, and the proposal stays valid.
3. Quorum met ⇒ enactment seals and the roster moves (T048).
4. **Substitution (T085)**: sign an `AddValidator` for validator A, submit with B's entry — rejected,
   with the rejection visible in the validator log rather than absorbed.
5. **No regression (T086)**: a single-owner register still completes governance unattended.

---

## Sequencing

1. Pending-proposal shape + the `Status == Pending` validator carve-out. Smallest change that makes a
   proposal recordable; live gate 1 proves it before anything is built on it.
2. `POST .../approve` (T045) over the existing verifier and submitter. Live gate 2 — **this is the
   live proof of T075**.
3. Quorum recount from sealed approvals + enactment reaction (T044, T079). Live gates 3 and 4.
4. `GET` proposals/detail with derived status (T043, T046).
5. CLI `sorcha governance approve` (T082) — the autonomous-bot path, and what makes gates 3–4
   drivable without a UI.

Steps 1–2 alone unblock the live proof this design exists to enable.

---

## Deliberately out of scope

- **Retiring the Owner override.** Single-owner registers keep unattended governance; #1380 narrows
  rather than closes.
- **Withdrawing a proposal.** A real state (`withdrawn` appears in T043's list) but it needs its own
  signed record, and nothing is blocked on it.
- **Backfilling the existing n1 fixture.** The T093 proposal is already enacted and invalidated;
  testing the lifecycle needs a fresh proposal, not a migration.
- **Full blueprint conformance for governance transactions** (T054–T057). They remain
  `Metadata["Type"] = "Control"` and exempt from action-schema validation, because
  `register-governance-v1` is not published to ordinary registers.
