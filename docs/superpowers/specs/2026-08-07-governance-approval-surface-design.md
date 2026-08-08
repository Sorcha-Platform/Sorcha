# Governance approval surface — external signing

**Date:** 2026-08-07
**Feature:** 189 (org-signed governance), US2/US3
**Status:** Design approved, not implemented
**Related:** issue #1380, R-009, `specs/189-org-signed-governance/`

---

## Problem

Feature 189 US1 made governance work: control transactions are signed by the organisation at slot 100
and every signature is checked against the register's roster. What it did not do is decide **who
holds the key**.

Today `GovernanceApprovalService` signs on the server, and the drafted contract for
`POST …/proposals/{id}/approve` says so plainly: *"the caller's organisation is resolved from its
token; the approval is signed with that organisation's slot-100 key"*. That is issue #1380 expressed
as an API. Any service-tier token can name any organisation, so under `QuorumFormula.Unanimous` — the
consortium setting, where the protection is meant to be strongest — one principal can produce every
approval a change requires.

An approval has to be produced by something **external to the system**. Whether that is a human or an
autonomous privileged bot is deliberately not the platform's business.

## Decision

Approvals are produced by **detached signature over a canonical digest**. The server publishes what
needs signing; something outside the server signs it; the server assembles the result onto the
ledger and never holds a multi-party register's slot-100 key.

The human UI and the autonomous bot are then **two clients of one protocol**, not two features.

**Single-owner registers are explicitly carved out.** `GovernanceRosterService`'s Owner override
continues to meet quorum 1-of-1 headlessly, unchanged. Without this the feature is a regression for
every register that exists.

---

## The finding that shapes everything: the digest does not bind what it authorises

`GovernanceApprovalStatement` (v1, built in US1) binds a hand-picked field list: domain tag,
`registerId`, `OperationType`, `ProposerDid`, `TargetDid`, `TargetRole`, `ProposedAt`, `approverDid`,
and approve/reject.

`GovernanceOperation` carries more than that:

| Unbound field | Consequence once signing is external |
|---|---|
| `ValidatorEntry` | An `AddValidator` approval binds "add a validator" — **not which one**. Its public key and endpoint are outside the digest. |
| `RosterSnapshotId` | Defended separately by T042's count-time comparison; not by the signature. |
| `QuorumFormulaAtRaise` | Neither bound nor compared — the bar could move after signing. |
| `ExpiresAt` | An expiry could be extended after approval. |
| crypto-policy payload | `CryptoPolicyUpdate` has no descriptive field, so an approval binds only "*a* crypto-policy change at time T". |

Under server-side signing this is close to inert: the server builds *and* signs, so there is no
separate party to mislead. **Detached signing is precisely what makes it exploitable** — the premise
is that an external party reviews something and signs it, so any unbound field is a way to display
one thing and enact another, leaving a cryptographically valid signature and no record of the
substitution.

### Resolution — statement v2

`GovernanceApprovalStatement` v2 binds the **canonical serialisation of the whole operation**, not a
field list: domain tag `sorcha:governance-approval:v2`, `registerId`, `proposalId`, `approverDid`,
approve/reject, and a hash of the operation's canonical JSON with derived/mutable members excluded
(`ApprovalSignatures`, `Status`).

A hand-maintained field list is the smell this codebase has repeatedly been bitten by — a field added
later is silently uncovered, with no compiler error and no failing test. Binding the serialisation
removes the category.

v1 is a clean break: v1 signatures must not verify under v2. Consistent with the feature's existing
no-compatibility-window stance, and there are no collected approvals in the wild.

**Corollary:** the approver must be able to *see* everything the digest binds. The signing request
carries the full operation and the client renders it. "Sign this hash" is not review.

---

## Architecture

### The signing request

```
GovernanceSigningRequest
  requestId, registerId, proposalId
  operation          // the FULL GovernanceOperation, canonical form
  statementVersion   // "sorcha:governance-approval:v2"
  approverDid        // which organisation is being asked
  expiresAt
```

**It carries no digest, deliberately.** If the server supplied one, a client could sign a digest that
does not match the operation it displayed — reintroducing the substitution one level up. The client
recomputes the digest from the operation it rendered, so there is nothing for the two to disagree
about.

### The submission

```
GovernanceApprovalSubmission
  requestId, approverDid
  signature, publicKey       // org slot-100 key — the AUTHORITY
  authMethod                 // hardware-backed | software | service
  coSignature? { adminDid, signature, publicKey, authMethod }   // the ACCOUNTABILITY
  comment?
```

`publicKey` travels so the validator matches against the roster without a lookup, as US1's
per-signature check already does.

**The co-signature is the maintainer's contribution and it earns its place.** The org key carries
authority; an admin's own key carries accountability. Without it the ledger records only "org X
approved" — it cannot say which human authorised it, which is exactly what US3 exists to provide. A
platform-tier token already carries `wallet_address` (`TokenService`, `Tier.Platform` branch), so org
admins already have a key; no new provisioning.

`Signatures` is already a `List`, and US1 already iterates every entry, so a co-signed approval needs
no new transport.

### Clients

- **Web console** — the review surface. Renders what changes, who proposed it, approvals so far, what
  happens at quorum. This is where the screen space is.
- **Wallet PWA (web or mobile)** — holds the org governance key, recomputes the digest, displays what
  it is about to authorise, signs. Identical code on both; `authMethod` records which kind of key.
- **CLI / autonomous bot** — fetches open proposals for its org, applies its own rules, signs. No UI,
  same envelope. Discovery via `GET …/governance/proposals?status=open` or the Feature 118 hub, whose
  thin-signal contract (ids only, detail over REST) already fits.

### On device equivalence

Web and mobile are the same client to the server, and the protocol is device-agnostic — build once.
They are **not** equivalent in security: `WebCryptoDeviceKeyService` yields non-extractable keys
either way, but a phone is typically secure-enclave backed with biometric unlock, whereas a desktop
browser profile is a file on disk reachable by anything running as that user. Reviewing in the console
and signing in a PWA on the same machine also collapses the isolation — one compromise gets both. That
still beats server-side signing (an attacker needs the admin's machine, not just a service token).

Rather than mandate a device, **record `authMethod` on the ledger** so a register can set its own bar
— e.g. `Unanimous` operations require hardware-backed signatures. Enforceable and auditable instead of
assumed, and admins without phones are not blocked.

### Ledger flow

1. Proposal raised; `RosterSnapshotId` + `QuorumFormulaAtRaise` captured (already built).
2. Server derives a signing request per roster organisation.
3. External holder signs the v2 digest and posts the submission.
4. Server assembles the action submission and puts it through the **validator** — not straight to
   storage. Writing directly to Mongo was the original US1 defect and must not return.
5. Validator matches the org signature against the roster; treats a co-signature as attestation, **not**
   a roster claim. Without this the co-signature is rejected as "not on roster".
6. On seal, quorum is recounted from **sealed approval transactions** — a pure function of sealed
   content, so every node folds identically (R-009).

Two properties fall out: approvals become visible cross-node with no extra work, and there is no
service-side approval table to drift from the ledger.

---

## Failure cases

| Case | Behaviour |
|---|---|
| Operation mutated after signing | Verification fails (v2). Currently **passes** for `ValidatorEntry`. |
| Server presents a digest ≠ operation | Impossible — no digest is transmitted. |
| Approver not on the roster snapshot | `403`, recorded `refused-not-on-roster`. Never a silent drop (FR-011c). |
| Roster changes mid-proposal | `Invalidated`; signatures remain valid bytes, the proposal dies (T042). |
| Repeat approval | Idempotent, count unchanged. |
| Proposal or signing request expired | `409` with the reason. |
| Co-signature invalid, or admin of a different org | **Reject the submission.** Never accept while silently dropping the co-signature — that downgrades accountability invisibly. |
| Quorum met, enactment fails | Terminal state with a reason; never limbo. |
| Last governor would be removed | Refused (FR-024 / T047). |

---

## Testing

**Reflection-driven digest coverage.** Enumerate `GovernanceOperation`'s properties; for each not
explicitly excluded, mutate it and assert the digest changes. A hand-listed test rots exactly as a
hand-listed field list does. This mirrors the existing reflection tests in
`Sorcha.Wallet.Contracts.Tests` for derivation contexts, so it is an established pattern here.

That test must go **red today** for `ValidatorEntry`, `RosterSnapshotId`, `QuorumFormulaAtRaise` and
`ExpiresAt` — which is how we know it was not written to pass.

**Validator-side:** a co-signature must not satisfy the roster; an org signature is still required.

**Live gates.** T048 (three-org `Unanimous`: not enacted at 2 of 3, enacted and replicated at 3 of 3)
and T049 (SC-010: removing the last outstanding approver invalidates rather than enacts) already
exist. Add:

> **Substitution gate** — review and sign an `AddValidator` for validator A, then submit with
> validator B's entry. Must be rejected on n1, and the rejection must appear in the validator log
> rather than being absorbed.

That gate is what distinguishes independent approval from something that merely looks like it.

---

## Sequencing

T041 ("approvals as ledger transactions") and T054 ("each organisation's approval submitted as an
action") describe the same object — R-009 equates them outright: *"approvals are ledger transactions
(action submissions)"*. An approval's `BlueprintId`, `ActionId` and payload schema are defined by the
blueprint, which is T053. Building T041 standalone means inventing a shape and changing it in US3 —
landing on the "bespoke code beside a decorative blueprint" the brief explicitly rejects.

So:

1. **Statement v2** + the reflection test. Independent, and it makes everything after it safe.
2. **T053** — revise `register-governance-v1`: quorum from the register's configured rule, add the
   crypto-policy operation, add `dataSchemas` for proposal and approval payloads.
3. **T041 + T054 together** — approvals as action submissions of that blueprint.
4. **Validator** — co-signature handling; the substitution gate.
5. **T045/T046** — the endpoints, now thin.
6. **CLI approve** — proves the ledger mechanics on n1 without waiting for UI.
7. **Console review UI + PWA signing.**
8. **T043** terminal outcomes, **T048/T049** + substitution live gates.

Steps 1–6 deliver a working, externally-signed governance path; 7 makes it usable by the people it is
for.

---

## Deliberately out of scope

- **Retiring the Owner override.** Single-owner registers keep unattended governance.
- **Key rotation for slot 100.** Real, and separate.
- **Rendering the governance diff** is in scope for the console (a JSON blob is not review), but its
  visual design is not settled here.
- **Closing #1380 for the single-owner case.** Multi-party is closed outright; single-owner still
  signs server-side by choice. The issue stays open with reduced scope.
