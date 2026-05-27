# Phase 1 Data Model: Presentation Lifecycle Chain-Race Resolution

**Feature**: 119-presentation-seal-ordering
**Date**: 2026-05-08

## Scope

Two new transient Redis schemas and one extended sentinel state machine. **No** new on-register transaction types, **no** new database tables, **no** EF migration.

---

## 1. Transient state — Redis schema

### 1.1 Submit queue: `awaiting-seal:submit:{predecessorTxId}`

**Key**: `sorcha:presentation:awaiting-seal:submit:{predecessorTxId}`
**Type**: Redis hash
**TTL**: `pending.ValidityWindowSeconds` (default 600s, copied from pending presentation)
**Per-presentation**: each entry's hash key uniquely identifies one queued submission, since each predecessor has at most one chain-pointer-bearing successor.

| Field | Type | Description |
|---|---|---|
| `presentationRequestId` | string (Guid) | The presentation this submission belongs to. |
| `site` | string | `outcome` \| `abandonment`. Determines which sentinel transitions and metric labels apply. |
| `submissionJson` | string | The fully built and signed `TransactionSubmission` DTO, JSON-serialised. Includes `previousTransactionId = predecessorTxId`. |
| `targetSentinelOnSuccess` | string | Final sentinel value to set on successful submission (e.g. `success`, `decline`, `abandoned`). |
| `enqueuedAt` | string (ISO-8601) | Used by the recovery sweeper to age out stuck entries and to compute wait-duration metrics. |
| `traceContext` | string | W3C traceparent for OTel span continuity across enqueue → drain. |

**Lifecycle transitions**:

```
[absent] --(HandleOutcomeAsync sees predecessor not sealed)--> [present, sentinel=outcome-pending-seal]
[present] --(transaction:confirmed event)--> [absent + outcome submitted, sentinel=success/decline]
[present] --(sweeper at TTL)--> [absent + sentinel=failed-predecessor-not-sealed]
[present] --(submitter validator-reject)--> [absent + sentinel=failed-validator-reject]
```

### 1.2 Advance queue: `awaiting-seal:advance:{outcomeTxId}`

**Key**: `sorcha:presentation:awaiting-seal:advance:{outcomeTxId}`
**Type**: Redis hash
**TTL**: `pending.ValidityWindowSeconds`

| Field | Type | Description |
|---|---|---|
| `presentationRequestId` | string (Guid) | The presentation whose advancement is queued. |
| `instanceId` | string (Guid) | Workflow instance to advance. |
| `completedActionId` | string (int) | Action being completed by this advancement (used by `CompleteAfterPresentationAsync`'s idempotency guard). |
| `registerId` | string | For tracing, not used to authorise. |
| `draftPayloadJson` | string | The pending-presentation's `draftPayload`, carried through. |
| `enqueuedAt` | string (ISO-8601) | |
| `traceContext` | string | W3C traceparent. |

**Lifecycle transitions**:

```
[absent] --(HandleOutcomeAsync success path, after submission Success)--> [present]
[present] --(transaction:confirmed event for outcomeTxId)--> [absent + CompleteAfterPresentationAsync invoked]
[present] --(sweeper at TTL)--> [absent + structured warning, FR-119-007]
```

The advancement is fire-and-forget (errors logged not propagated). Idempotency is provided by `CompleteAfterPresentationAsync`'s existing `instance.CurrentActionIds.Contains(completedActionId)` early-return.

---

## 2. Sentinel state machine extension

The existing `outcome-sentinel:{presentationRequestId}` Redis key (research R6 of Feature 111) gains three new values:

| Value | Set by | Replaces / extends |
|---|---|---|
| `outcome-pending-seal` | `HandleOutcomeAsync` when predecessor not sealed | New — sits between `outcome-pending-write` and final `success` / `decline` |
| `failed-predecessor-not-sealed` | Recovery sweeper at TTL | New terminal state for the never-seals case |
| `failed-validator-reject` | Seal subscriber on submission reject | New terminal state for the should-not-happen path |

Full state machine (* denotes pre-existing):

```
[unset]* --SET NX outcome-pending-write*--> outcome-pending-write*
[unset]* --SET NX abandoned*--> abandoned*

outcome-pending-write* --(submission inline succeeds)--> success* | decline*
outcome-pending-write* --(predecessor not sealed)--> outcome-pending-seal      [new]

outcome-pending-seal --(seal event drains queue, submission succeeds)--> success* | decline*
outcome-pending-seal --(sweeper TTL)--> failed-predecessor-not-sealed           [new]
outcome-pending-seal --(validator reject on drain)--> failed-validator-reject  [new]

abandoned* --(late outcome callback)--> abandoned+outcome*
```

**Idempotent-replay logic** (`HandleOutcomeAsync` lines 262-275) is updated to recognise `outcome-pending-seal` as a "writer claimed; deduplicate" state — same shape as existing `outcome-pending-write` handling.

---

## 3. No on-register changes

This feature does not introduce, modify, or remove any:

- On-register transaction type
- Validator chain rule
- Validator reachability rule
- Blueprint configuration field
- Domain enum

The `previousTransactionId` field on `presentation-outcome` and `presentation-abandoned` transactions is unchanged in shape; only the *timing* of when those transactions are submitted changes.

---

## 4. Configuration extensions

`PresentationLifecycleOptions` gains one new field:

| Field | Type | Default | Purpose |
|---|---|---|---|
| `SealRecoverySweepIntervalSeconds` | int | 5 | Tick cadence for the recovery sweeper (research R3). |

`appsettings.json` example:

```json
"PresentationLifecycle": {
  "DefaultValidityWindowSeconds": 600,
  "SealRecoverySweepIntervalSeconds": 5
}
```

---

## Summary

All data model changes are transient (Redis-only) and additive. No database migrations, no on-register schema changes, no breaking changes to existing data structures. Ready for contracts.
