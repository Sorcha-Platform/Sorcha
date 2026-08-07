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

One organisation's approval. The caller's organisation is resolved from its token; the approval is
signed with that organisation's slot-100 key and submitted as a ledger transaction (R-009).

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
