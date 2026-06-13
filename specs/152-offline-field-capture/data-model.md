# Phase 1 Data Model: PWA Offline / Field Capture

**Feature**: 152-offline-field-capture | **Date**: 2026-06-13

All new state is **device-local, encrypted at rest** (XChaCha20-Poly1305, device key). No new
server-side model. Three new IndexedDB stores; the action payload + attachment shapes reuse the
existing `ActionSubmissionRequest` / `FileAttachment` server contract.

## Store: `drafts` (key: `instanceId:actionId`)

| Field | Type | Notes |
|-------|------|-------|
| `InstanceId` | string (Guid) | The action's instance |
| `ActionId` | int | The action |
| `FormData` | dictionary | Flat JSON-Pointer-keyed form values (as `SorchaFormRenderer` emits) |
| `Media` | list of `DraftMedia` | Captured photos/files (see below) |
| `Status` | enum | `Editing → ReadyToSubmit → Queued → Submitted | NeedsAttention` |
| `SavedAt` | DateTimeOffset | Last autosave |
| `ConflictReason` | string? | Set when `NeedsAttention` (already-submitted / step-moved-on / closed) |

`DraftMedia`: `{ FileName, ContentType, Bytes (encrypted blob), CapturedAt }` — respects the existing
40 MB ceiling.

## Store: `submitQueue` (key: autoincrement `id`)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | long | Queue order |
| `InstanceId` / `ActionId` | string / int | Target action |
| `Payload` | dictionary | Nested submission payload (as the execute body expects) |
| `Attachments` | list of `FileAttachment` | base64 inline (or chunk refs for large files) |
| `IdempotencyKey` | string | Reused server key so a re-flush can't duplicate |
| `State` | enum | `Queued → Submitting → Submitted | NeedsAttention` |
| `Attempts` | int | For backoff |
| `LastError` | string? | Last transient error / conflict reason |

## Store: `actionContext` (key: `instanceId:actionId`)

| Field | Type | Notes |
|-------|------|-------|
| `InstanceId` / `ActionId` | string / int | Identity |
| `ActionSchema` | json | Blueprint action (schema + layout) for offline render |
| `RegisterId` / `SenderWallet` | string | Submission context (as `ApplicationFormContext` carries) |
| `CachedAt` | DateTimeOffset | Freshness; refreshed when online |

## Conflict classification (pure)

`SubmitConflictClassifier.Classify(serverOutcome) -> ConflictResult`:
- `Submitted` — success (incl. idempotent same-key replay).
- `Stale(reason)` — no longer applicable: `AlreadySubmitted` / `StepMovedOn` / `InstanceClosed`.
- `Retry` — transient (network / 5xx).

Drives queue-item + draft state transitions; unit-tested table-driven.

## State transitions (client-owned)

```
Draft:   Editing --(complete)--> ReadyToSubmit --(enqueue)--> Queued
Queue:   Queued --(flush)--> Submitting --(Submitted)--> Submitted (draft cleared)
                                         --(Stale)-----> NeedsAttention (draft kept + reason)
                                         --(Retry)-----> Queued (backoff)
NeedsAttention --(citizen: discard)--> removed
              --(citizen: re-open fresh)--> Editing against current action context
```

## Reused server contract (no new model)

- `ActionSubmissionRequest { PayloadData, Files: List<FileAttachment>? }` and
  `FileAttachment { FileName, ContentType, ContentBase64 }` — already defined; US5 makes `/execute`
  honor `Files` (reusing `BuildFileTransactionsAsync`).
- Server idempotency keyed by `(instanceId, actionId, senderWallet, lastTransactionId)` — reused for
  safe retry / duplicate prevention.
