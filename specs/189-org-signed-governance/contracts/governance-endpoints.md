# Phase 1 Contracts: Governance endpoints (Feature 189)

All endpoints are Register Service, under the existing governance group
`/api/registers/{registerId}/governance`, and require `CanManageRegisters` composed with
`RequirePlatformAudience` (CLAUDE.md §13 — the tier gate sits *on* the role gate; an Administrator
role carried on a consumer-tier token is refused).

**Universal rule — submitted is not effective.** Every mutating endpoint here returns `202 Accepted`
with the submitted transaction id. The change takes effect only when that transaction is sealed into
a docket, at which point it also replicates to every node (FR-013/FR-014). No endpoint returns a
representation implying the change has already applied. Callers poll the read endpoints.

---

## Existing, corrected

### `POST /api/registers/{registerId}/disable-dev-mode`

Unchanged shape. **Corrected**: the emitted control transaction is now signed by the organisation's
slot-100 governance key rather than the node's system wallet, so it passes roster enforcement on a
register whose genesis has sealed.

`202` `{ registerId, txId, policyVersion, status: "submitted", message }`
`409` dev mode already disabled · `422` refused (incl. any attempt to re-enable) · `403` caller not
on the roster.

### `POST /api/registers/{registerId}/governance/crypto-policy`

Same correction. Additionally gains a non-empty `BlueprintId` (`register-governance-v1`) so it is no
longer rejected `TX_003`, and is now genuinely subject to roster enforcement.

### `POST /api/registers/{registerId}/governance/propose`

**Corrected**: non-empty `BlueprintId`, and it now *is* detected as a governance transaction
(R-004 — it previously bypassed roster enforcement entirely). Request gains `rosterSnapshotId`
semantics implicitly: the server captures the current roster-establishing transaction id and freezes
the quorum rule at raise time (FR-011a).

Response `202`:

```json
{
  "proposalId": "…",
  "registerId": "…",
  "operationType": "CryptoPolicyUpdate",
  "rosterSnapshotId": "…",
  "quorumFormula": "Unanimous",
  "approvalsRequired": 3,
  "approvalsReceived": 1,
  "expiresAt": "2026-08-13T12:00:00Z",
  "status": "Open",
  "txId": "…"
}
```

A single-owner register (Owner override) may return `approvalsReceived == approvalsRequired` and a
follow-on enactment transaction id — except for `Transfer`, which always requires counting (FR-010).

---

## New

### `POST /api/registers/{registerId}/governance/proposals/{proposalId}/approve`

One organisation's approval.

> **SUPERSEDED 2026-08-07 for multi-party registers (R-014).** This previously read *"the caller's
> organisation is resolved from its token; the approval is signed with that organisation's slot-100
> key"* — i.e. the server signed. That is issue #1380 expressed as an API and it is withdrawn.
>
> The server no longer holds a multi-party register's slot-100 key. This endpoint now **accepts a
> detached signature produced externally** (see `GovernanceApprovalSubmission` below) and assembles
> the ledger transaction from it. A single-owner register keeps the unattended Owner override
> (FR-031), which is the only remaining case where the server signs.

The submitted approval is still a ledger transaction, not a table row (R-009).

Request body: none required. Optional `{ "comment": "…" }` for the audit trail.

| Status | Meaning |
|---|---|
| `202` | Approval submitted. Body reports `approvalsReceived` / `approvalsRequired` **as of the last sealed state** — a just-submitted approval is not yet counted. |
| `403` | Caller's organisation is not on the proposal's roster snapshot (FR-011). |
| `409` | Proposal is not open — already enacted, expired, invalidated or withdrawn. Body carries `status` and the reason (FR-011c). |
| `404` | No such proposal on this register. |

Approving twice is **idempotent**: `202`, with the count unchanged (FR-011).

### `GET /api/registers/{registerId}/governance/proposals`

Lists proposals with status. Query: `?status=Open|Enacted|Expired|Invalidated|Withdrawn|All`
(default `Open`). Read-only; available to any caller entitled to read the register (FR-020).

### `GET /api/registers/{registerId}/governance/proposals/{proposalId}`

Full detail for audit (US3): the proposal, its roster snapshot, **each approval individually
attributed** to the approving organisation with its timestamp, and the terminal outcome with reason.

```json
{
  "proposalId": "…",
  "operationType": "Transfer",
  "proposedBy": "did:sorcha:w:ws11q…",
  "rosterSnapshotId": "…",
  "quorumFormula": "Unanimous",
  "approvalsRequired": 3,
  "approvals": [
    { "approverDid": "did:sorcha:w:ws11qA…", "approvedAt": "…", "txId": "…" },
    { "approverDid": "did:sorcha:w:ws11qB…", "approvedAt": "…", "txId": "…" }
  ],
  "status": "Invalidated",
  "statusReason": "roster-changed",
  "outcomeTxId": "…"
}
```

`statusReason` values: `quorum-met` · `expired` · `roster-changed` · `withdrawn` ·
`refused-not-on-roster`. Every terminal state carries one — nothing is silently dropped (FR-011c).

---

## Validator-side contract (not an HTTP surface)

`RightsEnforcementService` changes behaviour in four ways. These are the enforcement contract:

1. **Detection** — a transaction is governance when `Metadata["Type"] == "Control"` **or**
   `BlueprintId == register-governance-v1`, excluding `transactionType == "BlueprintPublish"`
   (the existing #917 carve-out for the bootstrap publish). It no longer keys on
   `Metadata["transactionType"] == "Control"`, which never matched anything (R-004).
2. **Key matching** — decoded **bytes**, fixed-time comparison, tolerant of padded/unpadded and
   base64/base64url on either side (R-003). Never a string equality.
3. **Multi-signature** — every signature is matched; the number of **distinct** roster members
   satisfied must meet the operation's requirement. `Signatures[0]` is no longer privileged.
4. **Genesis allowance narrowed** — `roster == null` still admits the transaction that *creates* the
   roster, but nothing else (R-002). This closes the window in which any control transaction was
   admitted before genesis sealed.

Refusal codes surface a reason an administrator can act on (SC-008): `VAL_PERM_001` (no signature),
`VAL_PERM_002` (signer not on roster), plus a new code for "insufficient distinct roster signatures".

---

## Contract tests

Each of these fails against current `master`:

| Test | Asserts |
|---|---|
| Governance tx signed by the node system wallet | Refused — the node is never on a roster |
| Governance tx signed by an org's slot-100 key | Accepted and seals into a docket |
| Roster key stored padded-base64 vs signature key | Matches (R-003 regression — currently cannot match) |
| `/propose` transaction | Detected as governance and roster-enforced (currently bypasses) |
| Two signatures, one non-roster | Only the roster member counts toward the requirement |
| Approval by non-snapshot member | Ignored, not counted, `403` at the API |
| Roster change with a proposal open | Proposal `Invalidated`, reason `roster-changed`, not enactable |
| Unanimous, last approver removed | **Invalidated, not enacted** (SC-010) |
| Repeat approval | Count unchanged |
| Any `BlueprintId == "genesis"` on a governance tx | Rejected (R-005 guard) |


---

## External signing (added 2026-08-07 — R-013/R-014/R-015)

### `GET /api/registers/{registerId}/governance/proposals/{proposalId}/signing-request`

What an approver must sign. Returned to the organisation being asked.

```
{
  "requestId":        "...",
  "registerId":       "...",
  "proposalId":       "...",
  "operation":        { ...the FULL GovernanceOperation, canonical form... },
  "statementVersion": "sorcha:governance-approval:v2",
  "approverDid":      "did:sorcha:w:ws11q...",
  "expiresAt":        "..."
}
```

**No digest field, deliberately (FR-028).** A server-supplied digest could fail to match the
operation the client displayed, reinstating the substitution R-013 closes, one level up. The client
derives the digest from the operation it rendered, so there is nothing for the two to disagree about.

The client MUST render the operation (FR-027). Signing an opaque value is not approval.

### `GovernanceApprovalSubmission` — the body of `POST .../approve`

```
{
  "requestId":   "...",
  "approverDid": "did:sorcha:w:ws11q...",
  "signature":   "base64",      // organisation slot-100 key — AUTHORITY
  "publicKey":   "base64",      // travels so the roster match needs no lookup
  "authMethod":  "hardware-backed" | "software" | "service",
  "authorisation": {            // ACCOUNTABILITY — required on EVERY approval (FR-029, R-017).
    "kind": "direct",           // "direct" | "delegated"
    "individualDid": "...",     // the human who stands behind this approval
    "signature":  "base64",     // direct: the individual's own key signs the same v2 statement
    "publicKey":  "base64",
    "authMethod": "...",
    "delegation": {             // delegated ONLY: how a machine came to be empowered
      "delegationId": "...",    // ledger record; validity is checkable from sealed content
      "approverPublicKey": "base64",             // the machine key this empowers
      "scope": ["CryptoPolicyUpdate"],           // which operations — Transfer can be withheld
      "expiresAt": "...",
      "signature": "base64",    // signed by the EMPOWERING INDIVIDUAL's key, not the server's
      "publicKey": "base64"
    }
  },
  "comment": "optional, for the audit trail"
}
```

| Status | Meaning |
|---|---|
| `202` | Accepted and submitted to the validator. |
| `400` | Signature does not verify against the v2 statement derived from the stored operation. |
| `403` | Approver is not on the proposal's roster snapshot (FR-011). |
| `409` | Proposal not open, or the signing request has expired. |
| `422` | Authorisation invalid, from an individual outside the approving organisation, or — for a delegated approver — out of scope, expired or revoked. **Refused outright — never accepted with the authorisation quietly dropped (FR-032).** |

> **Why the delegation is signed rather than claimed.** `RequireDelegatedAuthority` already carries a
> `delegated_user_id` claim, but the **server mints the token**. A delegation the server can assert is
> one it can forge, which defeats moving signing outside it (R-014/R-017). The delegation must be
> signed by the empowering individual's own key.
