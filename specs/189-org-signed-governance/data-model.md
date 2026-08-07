# Phase 1 Data Model: Real register governance (Feature 189)

**Governing constraint (R-009).** Under Feature 145 a workflow instance is a deterministic
projection of the sealed ledger. Every fact quorum depends on therefore lives **on the ledger**, and
every node must fold the same answer from it. Nothing below is authoritative in service-local
storage; where a store appears it is a cache or an index that can be rebuilt from the ledger.

---

## Existing entities (reused unchanged unless noted)

### RegisterControlRecord — `Sorcha.Register.Models`

The genesis-sealed record that establishes a register. Already carries everything governance needs.

| Member | Purpose | Change |
|---|---|---|
| `Attestations` | The governance roster: role + subject + public key + signature per organisation | **The key now derives from slot 100** (R-011). Shape unchanged. |
| `RegisterPolicy.Governance.QuorumFormula` | The register's approval rule | none |
| `RegisterPolicy.Governance.BlueprintVersion` | Governance workflow definition | none |
| `CryptoPolicy` | Governed cryptographic posture (incl. one-way `DevMode`) | none |
| `GetVotingMembers()` / `GetQuorumThreshold(excludeDid, formula)` | Quorum arithmetic | none — reuse |

### RegisterAttestation

| Field | Notes |
|---|---|
| `Role` | `Owner` / `Admin` — `Owner` drives the override (R-007) |
| `Subject` | `did:sorcha:w:{walletAddress}` — **parsed to resolve the signing wallet** (Phase A) |
| `PublicKey` | Base64. **Compared as decoded bytes, never as a string** (R-003) |
| `Algorithm`, `Signature`, `GrantedAt` | unchanged |

### GovernanceRoster (reconstructed, not stored)

Rebuilt by `GovernanceRosterService.GetCurrentRosterAsync` from sealed control transactions.
`LastControlTxId` is the identifier of the transaction that last established the roster — **this
becomes the roster-snapshot identity** for FR-011a/b (R-010).

---

## Modified entities

### GovernanceOperationType — `Sorcha.Register.Models.GovernanceModels`

Gains one member. Existing members unchanged.

```
Add | Remove | Transfer | AddValidator | RemoveValidator | RotateValidatorKey
+ CryptoPolicyUpdate    // FR-021 — cryptographic posture becomes a governable operation
```

### GovernanceProposal

Raised by a roster member; accumulates approvals until it meets its rule, expires, or is invalidated.

| Field | Purpose | Rules |
|---|---|---|
| `ProposalId` | Identity | Deterministic from register + operation + snapshot, so a retry is idempotent rather than forking |
| `RegisterId` | Target register | Must exist |
| `OperationType` | What is proposed | Incl. the new `CryptoPolicyUpdate` |
| `Payload` | Operation-specific detail (e.g. the new crypto policy) | Must satisfy the operation's schema |
| `ProposerDid` | Raising organisation | MUST be on the roster snapshot (FR-001) |
| **`RosterSnapshotId`** | The `LastControlTxId` the proposal was raised against | **New.** Drives FR-011a/b — see state transitions |
| **`QuorumFormulaAtRaise`** | The rule in force when raised | **New.** Frozen so the requirement cannot shift (FR-011a) |
| `RaisedAt`, `ExpiresAt` | Validity window | FR-012 |

**Not a database row.** The proposal is a ledger transaction; any table is a rebuildable index.

### GovernanceApproval

One organisation's authorisation of one proposal.

| Field | Rules |
|---|---|
| `ProposalId` | Must reference an open proposal |
| `ApproverDid` | MUST be on the proposal's roster snapshot; approvals from others are ignored, not errors (FR-011) |
| `Signature` | The organisation's slot-100 signature over the proposal identity |
| `ApprovedAt` | |

**Counted once per organisation** (FR-011). A repeat approval is idempotent, not additive.
**Carried as a ledger transaction** (R-009) so every node counts the same approvals.

### CryptoPolicyUpdate control transaction (existing, corrected)

Already shipped and live-verified this session; corrected here only in **who signs it**.

| Property | Value | Why it is pinned |
|---|---|---|
| `BlueprintId` | `register-governance-v1` | Non-empty (`TX_003`) and not `"genesis"` (R-005) |
| `ActionId` | `control.crypto.update` | `ControlDocketProcessor` matches this exact string |
| `Metadata["Type"]` | `Control` | Resolves the sealed `TransactionType`; also drives governance detection after R-004 |
| `Metadata["transactionType"]` | `CryptoPolicyUpdate` | Drives the docket-write DevMode projection |
| `Signatures[]` | **Organisation slot-100 keys** | The fix. Was the node system wallet. |

---

## State transitions

### Proposal lifecycle

```
                     ┌──────────── approval (roster member, snapshot-eligible)
                     ▼                                   │
   Raised ──────► Open ◄─────────────────────────────────┘
                     │
       ┌─────────────┼─────────────────┬──────────────────┐
       ▼             ▼                 ▼                  ▼
   QuorumMet     Expired          Invalidated         Withdrawn
       │        (validity          (roster                │
       ▼         window            changed)               │
   Enacted       elapsed)              │                  │
       │                               │                  │
       └───────────── all terminal, all recorded ─────────┘
```

Rules:

- **Open → QuorumMet** when distinct approvals from snapshot-eligible members satisfy
  `GetQuorumThreshold(snapshotRoster, QuorumFormulaAtRaise)`. Single-owner registers reach this
  immediately via the Owner override — **except `Transfer`**, which always counts (R-007, FR-010).
- **Open → Invalidated** the moment an enacted roster change makes the register's current
  `LastControlTxId` differ from the proposal's `RosterSnapshotId` (FR-011b). Deterministic and
  requires no timer: it is a comparison evaluated whenever the proposal is next examined.
- **Open → Expired** past `ExpiresAt` (FR-012). An expired or invalidated proposal is **never**
  enactable thereafter, even if a late approval arrives.
- **QuorumMet → Enacted** only once the control transaction is **sealed into a docket** (FR-013).
  Submitted ≠ effective (FR-014) — this is the exact defect fixed earlier in this branch.
- Every terminal state is recorded and discoverable (FR-011c, FR-020). None is a silent drop.

### One-way governed settings

`DevMode` may go `true → false` and never back (FR-016), enforced at three layers:
service-level refusal, `ControlDocketProcessor.ValidateCryptoPolicyUpdate` at consensus, and the
absence of any direct-write path (the `PUT /devmode` toggle was removed earlier in this branch).

---

## Invariants

1. **No governed state changes without a sealed ledger record.** (FR-013/014, SC-006)
2. **Every authorising signature is matched to a roster member by decoded key bytes.** (FR-001/005, R-003)
3. **A proposal's eligible approvers and required count are fixed when it is raised.** (FR-011a)
4. **A roster change cannot enact anything** — it invalidates open proposals rather than altering
   their outcome. (FR-011b, SC-010)
5. **Quorum is a pure function of sealed ledger content + the roster snapshot**, so every node folds
   the same result. (R-009)
6. **A register can never be left ungovernable by a governance change.** (FR-024)


---

## External approval (added 2026-08-07 — R-013/R-014/R-015)

### `GovernanceApprovalStatement` v2

Binds the **whole operation**, not a field list. v1's hand-picked list left `ValidatorEntry`,
`RosterSnapshotId`, `QuorumFormulaAtRaise` and `ExpiresAt` unbound — so an `AddValidator` approval
bound "add a validator" and not *which one*.

| Component | Note |
|---|---|
| domain tag | `sorcha:governance-approval:v2` — v1 signatures must not verify |
| `registerId` | an approval on one register must not authorise another |
| `proposalId` | newly bound; v1 relied on `ProposedAt` precision for uniqueness |
| `approverDid` | who is approving |
| approve / reject | the direction |
| operation hash | SHA-256 over the operation's canonical JSON, excluding `ApprovalSignatures` and `Status` (derived/mutable) |

Excluding by *serialisation* rather than by an allow-list is the point: a property added to
`GovernanceOperation` later is covered automatically, and a reflection test enforces it.

### `GovernanceSigningRequest`

| Field | Note |
|---|---|
| `requestId` | correlates the submission |
| `registerId`, `proposalId` | |
| `operation` | the **full** `GovernanceOperation`, canonical form — the client must render it (FR-027) |
| `statementVersion` | `sorcha:governance-approval:v2` |
| `approverDid` | which organisation is being asked |
| `expiresAt` | |

**Carries no digest (FR-028).** A server-supplied digest could disagree with the operation displayed.

### `GovernanceApprovalSubmission`

| Field | Note |
|---|---|
| `requestId`, `approverDid` | |
| `signature`, `publicKey` | organisation slot-100 key — **authority**. The public key travels so the roster match needs no lookup. |
| `authMethod` | `hardware-backed` \| `software` \| `service` — recorded on the ledger so a register can set its own bar (R-016) |
| `coSignature` | the authorising individual's own key — **accountability**. Required for human approvals, absent for autonomous ones (R-015). Verified and bound to the approving org; treated by the validator as attestation metadata, **not** a roster claim. |
| `comment` | optional, audit trail |

Transport needs nothing new: `Signatures` is already a `List` and US1 already iterates every entry.
