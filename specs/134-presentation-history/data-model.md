# Phase 1 Data Model: Cross-Device Citizen Presentation History

## Entity: `CitizenPresentationRecord`

Durable per-citizen record of one presentation the citizen reported making. Lives in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/`, mapped on `WalletDbContext` (PostgreSQL). Holds **disclosed claim names only — never claim values** (FR-002), and carries **no register correlation** (FR-010).

| Field | Type | Notes |
|---|---|---|
| `PlatformUserId` | `Guid` | The owning citizen. Part of the composite primary key. Scopes every query (FR-006). |
| `EntryId` | `Guid` | The wallet-generated entry id from the report. Part of the composite primary key — the unit of identity (FR-004). |
| `CredentialId` | `Guid` | Local cache id of the credential presented (opaque correlation token from the wallet). |
| `VerifierLabel` | `string` (≤200) | Verifier-supplied display label (untrusted, display-only). Nullable. |
| `VerifierDid` | `string?` (≤200) | Verifier DID if the request carried one. Nullable; typically null on the offline path. |
| `DisclosedClaims` | `string[]` (jsonb) | **Names** of the disclosed claims. Never values. |
| `PresentedAt` | `DateTimeOffset` | UTC time the wallet completed the presentation. Sort key (newest-first). |
| `Outcome` | `PresentationLogOutcome` (enum, stored as int/string) | `Presented` / `DeclinedByCitizen` / `VerifierRejected` / `Acknowledged` (the wire enum from `Sorcha.CitizenWallet.Abstractions`). |
| `ReportedAt` | `DateTimeOffset` | Server-side time the record was first stored (audit; set on first upsert, preserved on idempotent re-report). |

### Keys & indexes

- **Primary key**: composite `(PlatformUserId, EntryId)`. Makes `UpsertAsync` idempotent (FR-004) and scopes reads/deletes to the owner (FR-006).
- **Index**: `(PlatformUserId, PresentedAt DESC)` to serve the newest-first list (FR-003) efficiently.

### Validation rules

- `EntryId` and `CredentialId` non-empty; `PresentedAt` not default — enforced upstream by the shipped `PresentationLogReportRequestValidator` (PR2) at the `/presentations/log` boundary, so the store trusts already-validated input.
- `VerifierLabel` / `VerifierDid` ≤ 200 chars (same validator).
- No claim *values* may be persisted — enforced by the type: the entity exposes only `DisclosedClaims : string[]` (names).

### Relationships

- Logically belongs to `PlatformUser` (Tenant Service) via `PlatformUserId`, but there is **no EF foreign key** — `PlatformUser` lives in a different service/DB. The id is carried on the citizen JWT and trusted at the boundary, consistent with the other citizen-wallet entities (`CitizenHolderIndex`, `CitizenCredentialEventLog`).
- No relationship to any register, instance, action, or blueprint (by design — FR-010).

## Mapping: wire ↔ entity

The read/report wire shape is the existing `Sorcha.CitizenWallet.Abstractions.Models.PresentationLogEntry` (reused — D7).

| Wire `PresentationLogEntry` | `CitizenPresentationRecord` |
|---|---|
| `Id` | `EntryId` |
| `CredentialId` | `CredentialId` |
| `VerifierDid` | `VerifierDid` |
| `VerifierLabel` | `VerifierLabel` |
| `DisclosedClaims` | `DisclosedClaims` |
| `PresentedAt` | `PresentedAt` |
| `Outcome` | `Outcome` |
| `RegisterId` / `ActionTxId` | **dropped** (vestigial; not persisted — FR-010) |
| — | `PlatformUserId` (from JWT, not the wire body) |
| — | `ReportedAt` (server clock, first upsert) |

## State & lifecycle

A record has no mutable state machine. Its lifecycle is:

1. **Created** — first `UpsertAsync(platformUserId, entry)` (from the forwarder, off the `/presentations/log` request path).
2. **Idempotent re-report** — subsequent `UpsertAsync` for the same `(PlatformUserId, EntryId)` is a no-op on identity; `ReportedAt` is preserved. (Content is immutable in practice — the wallet never changes a reported entry.)
3. **Deleted** — `DeleteAsync(platformUserId, entryId)` removes the row. Idempotent; cross-user delete is a no-op indistinguishable from absence (FR-006). Hard delete (no soft-delete/tombstone — server-authoritative delete, D4).

## Store interface (`ICitizenPresentationStore`)

```text
Task UpsertAsync(Guid platformUserId, PresentationLogEntry entry, CancellationToken ct)
Task<IReadOnlyList<PresentationLogEntry>> ListAsync(Guid platformUserId, CancellationToken ct)   // newest-first
Task<bool> DeleteAsync(Guid platformUserId, Guid entryId, CancellationToken ct)                  // true if a row was removed; false otherwise (caller still returns 204)
```

Registered via `IStorageRegistrationLog` — `RegisterPersistent(...,"postgres")` when a connection string is present, else `RegisterInMemory(...)`. Not on the F113 fail-fast audited list (D2).
