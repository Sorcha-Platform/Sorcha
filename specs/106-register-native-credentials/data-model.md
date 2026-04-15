# Data Model — Register-native credential delivery

**Feature**: 106-register-native-credentials
**Phase**: 1 (/speckit.plan)
**Date**: 2026-04-15

## Scope

Feature 106 is an additive feature — the data model changes are a small set of enum extensions and a single new boolean column. No new entities, no new relationships, no new aggregates. This document catalogues the additions, the state transitions, and the validation rules they introduce.

## Entity deltas

### 1. `CredentialIssuanceConfig.TargetAudience` enum extension

**Location**: `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`

**Current enum values**:

| Value | Name | Semantics |
|---|---|---|
| 0 | `SorchaInternal` | Default — credential routed via the internal Sorcha wallet path (currently unused as a distinct branch; falls through to HAIP in practice) |
| 1 | `HaipExternalWallet` | External wallet via OpenID4VCI pre-authorized code flow — wave 14b pattern |

**New value added by Feature 106**:

| Value | Name | Semantics |
|---|---|---|
| 2 | `SorchaLocalWallet` | Credential sealed into issuing action's recipient-addressed disclosures for on-platform wallet delivery. Engine mints → encrypts → seals; holder node's Wallet Service detects, decrypts, and stores as `PendingAcceptance`. |

**Backwards compatibility**: `SorchaInternal` remains the default when `targetAudience` is omitted from a blueprint. Existing blueprints that explicitly use `HaipExternalWallet` continue to work unchanged. Existing blueprints that default to `SorchaInternal` continue through their current code path (which routes to HAIP today — Feature 106 does NOT redirect `SorchaInternal` to the new `SorchaLocalWallet` branch, to avoid migration surprises).

**Validation rules added at publish time**:

- **VAL_BP_CRED_001**: When `targetAudience = SorchaLocalWallet`, the action's `recipientParticipantId` MUST resolve to a participant defined in the blueprint.
- **VAL_BP_CRED_002**: When `targetAudience = SorchaLocalWallet`, the action MUST declare a disclosure group that includes the recipient participant. The engine will inject the `/credential` pointer into that group at execution time. Publishing an action without a recipient-targeted disclosure when `SorchaLocalWallet` is selected is a warning (not an error — the engine can synthesise the disclosure at runtime, but explicit is better than implicit).
- **VAL_BP_CRED_003**: `credentialType`, `claimMappings`, and `recipientParticipantId` remain required regardless of `targetAudience`.

---

### 2. `CredentialStatus` enum extension

**Location**: `src/Core/Sorcha.Wallet.Portable/Domain/Entities/CredentialEntity.cs`

**Current enum values**:

| Value | Name | Semantics |
|---|---|---|
| 0 | `Active` | Valid, usable for presentations |
| 1 | `Expired` | Past embedded `notValidAfter` — cannot be presented |
| 2 | `Revoked` | Issuer marked as revoked via status list — cannot be presented |
| 3 | `Suspended` | Temporarily suspended — cannot be presented, may be reactivated |

**New values added by Feature 106**:

| Value | Name | Semantics |
|---|---|---|
| 4 | `PendingAcceptance` | Decrypted and persisted in the holder's wallet store, but not yet explicitly accepted. Visible in the MyCredentials PENDING tab and in MyActions as a claim action. Cannot be used for presentations until the holder clicks Accept. |
| 5 | `Declined` | Holder explicitly declined. Retained for audit. Cannot be used for presentations. Can be hard-deleted via explicit `DELETE`. |

**State machine transitions** (authoritative definitions for the implementation):

```
  [null] (new row)
    │
    │ Wallet Service IInboundCredentialDetector extracts and persists
    ▼
  [PendingAcceptance]
    │
    ├── Holder clicks Accept     ──────────────► [Active]
    │
    ├── Holder clicks Decline    ──────────────► [Declined]
    │
    └── Embedded notValidAfter passes ─────────► [Expired]

  [Active]
    │
    ├── Embedded notValidAfter passes ─────────► [Expired]
    │
    └── Status list marks revoked  ────────────► [Revoked]

  [Declined]
    │
    └── Explicit DELETE /credentials/{id}  ────► (row removed)

  [Expired], [Revoked], [Suspended] — existing terminal/semi-terminal states, unchanged.
```

**Invariants**:

- **INV-1**: A row can only enter `PendingAcceptance` from `null` (new arrival). No re-entering this state.
- **INV-2**: `PendingAcceptance` can transition to `Active`, `Declined`, or `Expired`. No other transitions.
- **INV-3**: `Declined` is terminal until explicit delete. No transition out of `Declined` except row removal.
- **INV-4**: An `Active` credential cannot be re-declined — declining is only meaningful for `PendingAcceptance`.
- **INV-5**: Holder-initiated state changes (`PendingAcceptance → Active`, `PendingAcceptance → Declined`) MUST be accompanied by a sealed register transaction. The local state transition and the register transaction are the client's responsibility to execute in parallel; the server-side PATCH endpoint does not block on the register transaction completing.

**Repository filter updates**:

- `GET /api/v1/wallets/{address}/credentials` — new `?status={CredentialStatus}` query parameter, defaults to `Active` if omitted (preserves existing caller expectations).
- `GET /api/v1/wallets/{address}/credentials?status=PendingAcceptance` populates the MyCredentials PENDING tab.
- `GET /api/v1/wallets/{address}/credentials?status=Declined` populates a future "declined history" view (out of scope for this feature, but the filter must support it).

**Concurrency**: multiple wallet nodes receiving the same credential (via different peer-sync paths) use the credential's deterministic `Id` (typically a DID URI) as the deduplication key. Second-write-wins would corrupt the audit trail, so the repository MUST upsert with a status-guard: "only insert if no row with this Id exists".

---

### 3. `Instance.IsReadOnlyMirror` column

**Location**: `src/Services/Sorcha.Blueprint.Service/Data/Entities/InstanceEntity.cs`

**New column**:

| Column | Type | Default | Semantics |
|---|---|---|---|
| `IsReadOnlyMirror` | `boolean` | `false` | `true` when this row was reconstructed from peer-replicated register observations by `InstanceMirrorReconstructor` rather than created by a local action execution. Writes to mirror rows are rejected by `IInstanceStore.UpdateAsync` unless the caller is the reconstructor itself. |

**EF migration**: `20260415_AddReadOnlyMirrorColumn.cs` — adds the column with a default of `false` so existing rows remain writable. No data migration required.

**Invariants**:

- **INV-6**: A row with `IsReadOnlyMirror = true` MUST NOT be written to by the normal execution pipeline. `IInstanceStore.UpdateAsync` precondition-checks this flag and throws `InvalidOperationException` on violation.
- **INV-7**: Only `InstanceMirrorReconstructor` can set or clear `IsReadOnlyMirror = true`, and only via the new `InstanceStore.UpdateMirrorAsync` method which explicitly bypasses INV-6.
- **INV-8**: `IsReadOnlyMirror` is set at creation time and never flipped during the row's lifetime. If an instance that was a mirror later needs to accept local writes (e.g. the holder's node becomes the authoritative issuer), that's a new row; the mirror is retained for audit.

**Reconstruction idempotency**: multiple `docket:confirmed` events for the same transaction are safe to replay. The reconstructor checks for an existing row by `InstanceId` before creating or updating. Updates are advance-only — a reconstructed row can only move forward in `CurrentActionIds` based on confirmed transactions, never backward.

---

## Entities unchanged

The following entities are **not** modified by Feature 106:

- **`Blueprint`** — no new fields. `SorchaLocalWallet` is recognised through the existing `targetAudience` field on its actions.
- **`BlueprintAction`** — no new fields. Existing `CredentialIssuanceConfig`, `Disclosures`, `RejectionConfig` all sufficient.
- **`Transaction`** — no new fields. The encrypted credential payload rides in the existing `Payloads` / `Disclosures` structures.
- **`Wallet`** — no new fields. The detector uses `Owner`, `Address`, and key material via the existing surfaces.
- **`Docket`** — no new fields. Reconstruction fires on the existing `docket:confirmed` Redis event.

This is deliberate. Feature 106 is designed to ride on existing infrastructure; the absence of new entities is a feature, not an oversight.

---

## Validation rules (consolidated)

| Rule ID | Enforced by | When | What |
|---|---|---|---|
| VAL_BP_CRED_001 | Blueprint Service publish-time validator | Publish | `SorchaLocalWallet` requires a resolvable `recipientParticipantId` |
| VAL_BP_CRED_002 | Blueprint Service publish-time validator | Publish | `SorchaLocalWallet` action should declare a disclosure covering the recipient (warning, not error) |
| VAL_BP_CRED_003 | Blueprint Service publish-time validator | Publish | `credentialType`, `claimMappings`, `recipientParticipantId` remain required |
| INV-1 | `CredentialEntity` repository upsert logic | Inbound detection | No re-entering `PendingAcceptance` |
| INV-2 | `CredentialStatus` transition enforcement in `CredentialRepository.PatchStatusAsync` | PATCH `/credentials/{id}` | Only allow valid transitions from `PendingAcceptance` |
| INV-3 | Same | PATCH `/credentials/{id}` | `Declined` is terminal until delete |
| INV-4 | Same | PATCH `/credentials/{id}` | `Active` cannot be re-declined |
| INV-5 | Client responsibility, documented in contracts | Accept/Decline flow | Local PATCH and register transaction MUST both complete; client reconciles on failure |
| INV-6 | `IInstanceStore.UpdateAsync` precondition check | Any instance write | Reject writes to mirror rows from normal execution |
| INV-7 | `InstanceStore.UpdateMirrorAsync` (new method) | Reconstructor calls only | Only the reconstructor can write mirror rows |
| INV-8 | Reconstruction upsert logic | Reconstruction | `IsReadOnlyMirror` is set once at creation, never flipped |

---

## Out of scope (data-model only)

- **No new tables.** The spec's "credentials inbox" surface is a view over existing `CredentialEntity` rows filtered by status.
- **No new schemas on existing tables beyond `IsReadOnlyMirror`.** The `CredentialEntity` and `InstanceEntity` tables are otherwise unchanged.
- **No data migration.** The new enum values are additions; existing rows are valid under both old and new enum definitions. The new boolean column defaults to `false`, which is the correct value for every pre-existing row.
- **No breaking API changes.** Existing endpoints return the same shape; they just gain new query parameters and accept new enum values in request bodies.

The simplicity of this data model is intentional and load-bearing for the "Feature 106 ships safely without disrupting wave 14b" constraint.
