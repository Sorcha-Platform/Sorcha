# Phase 1 — Data Model: Citizen Wallet PWA

**Feature**: 114-citizen-wallet-pwa
**Date**: 2026-04-26
**Scope**: Persistent entities (server-side and on-device) introduced or extended by this feature.

---

## A. Server-side entities

### A1. PlatformUserDevice (NEW — Tenant Service)

Server-side record that a citizen has enrolled the wallet on a specific device. One per (PlatformUser, device) pair.

**Storage**: Tenant Service Postgres (`PlatformUserDevices` table).

**EF entity**:
```csharp
namespace Sorcha.Tenant.Core.Entities;

public sealed class PlatformUserDevice
{
    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }                          // FK → PlatformUser.Id
    public string Label { get; set; } = string.Empty;                 // citizen-editable
    public string DevicePublicJwkThumbprint { get; set; } = string.Empty;  // SHA-256(canonical JWK), 43 chars base64url
    public string DevicePublicJwkJson { get; set; } = string.Empty;   // canonical JWK JSON, ≤512 chars
    public string Platform { get; set; } = string.Empty;              // e.g. "iOS 19 / Safari 19"
    public string UserAgent { get; set; } = string.Empty;             // raw user agent at enrolment
    public PlatformUserDeviceStatus Status { get; set; }              // Active | Revoked
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? RevokedByPlatformUserId { get; set; }                // self-revoke OR support-revoke
    public DateTimeOffset? LastSeenAt { get; set; }                   // updated on each /sync hit
    public DateTimeOffset DelegationExpiresAt { get; set; }           // mirrors current delegation cred exp
    public string DelegationCredentialJti { get; set; } = string.Empty; // current delegation JWT id (rotates on renew)
    public int StatusListIndex { get; set; }                          // bit position in tenant's citizen-devices status list
    public PlatformUser PlatformUser { get; set; } = default!;        // navigation
}

public enum PlatformUserDeviceStatus
{
    Active = 0,
    Revoked = 1
}
```

**Validation rules**:
- `Label` 1..120 chars, trimmed, citizen-editable.
- `DevicePublicJwkThumbprint` exactly 43 base64url chars (SHA-256 thumbprint).
- `DevicePublicJwkJson` ≤ 512 chars, must parse as a valid EC P-256 JWK with `kty=EC, crv=P-256, x, y` and no private fields.
- `Platform` ≤ 120 chars.
- `UserAgent` ≤ 512 chars.
- `StatusListIndex` ≥ 0, unique per `(PlatformUserId.OrganizationId, list)` allocation pool.
- `RevokedAt` MUST be set iff `Status == Revoked`.
- `DelegationExpiresAt` MUST be > `EnrolledAt`.

**Indexes**:
- `IX_PlatformUserDevices_PlatformUserId_Status` (covering `(PlatformUserId, Status)` for device-list queries).
- `IX_PlatformUserDevices_DevicePublicJwkThumbprint` (lookup by device key on enrol/revoke flows).
- `IX_PlatformUserDevices_StatusListIndex` (status list publication).

**State transitions**:

```
   ┌──────────┐        revoke           ┌──────────┐
   │  Active  │ ─────────────────────▶  │ Revoked  │
   └──────────┘                          └──────────┘
        │                                     │
        │ delegation-renewed (silently)        │ (terminal — never returns to Active;
        │ updates DelegationExpiresAt +        │  citizen must enrol a new device)
        │ DelegationCredentialJti              │
        ▼                                     ▼
       Active                             Revoked
```

Renewal does NOT create a new `PlatformUserDevice` row — same row, updated `DelegationExpiresAt` + `DelegationCredentialJti`.

**Relationships**:
- N:1 `PlatformUserDevice → PlatformUser` (cascade delete: deleting a PlatformUser revokes all their devices via cascade, no orphans).
- The citizen's holder-key derivation is *implicit* — derived per-PlatformUser from the user's wallet root under `sorcha:citizen-holder`, no entity row.

---

### A2. CitizenDeviceStatusList (NEW — Wallet Service)

Tenant-org-scoped status list maintaining a packed bitstring of (active=0, revoked=1) bits, one per allocated `PlatformUserDevice.StatusListIndex` belonging to citizens of the org.

**Storage**: Wallet Service Postgres (`CitizenDeviceStatusLists` table). Bitstring also cached in Redis for fast reads (key: `sorcha:wallet:status-list:{orgId}:{listId}`).

**EF entity**:
```csharp
namespace Sorcha.Wallet.Core.Entities;

public sealed class CitizenDeviceStatusList
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int ListId { get; set; }                               // small int, contiguous from 0
    public int Capacity { get; set; }                             // bits per list, default 32_768
    public byte[] Bitstring { get; set; } = Array.Empty<byte>();  // packed bits, length = Capacity / 8
    public int RevokedCount { get; set; }
    public int LastAllocatedIndex { get; set; }                   // monotonically increasing watermark
    public DateTimeOffset GeneratedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }                 // GeneratedAt + 24h
    public string SignedJwt { get; set; } = string.Empty;         // Token Status List JWT (signed, served as-is)
}
```

**Allocation rules**:
- A new `PlatformUserDevice` claims `StatusListIndex = ++LastAllocatedIndex` from the org's current open list.
- When `LastAllocatedIndex == Capacity`, a new list (`ListId + 1`) is created.
- Indexes are NEVER reused, even on revocation. Revocation flips the bit; allocation moves the watermark.

**Regeneration cadence**:
- On every revocation event (incremental — flip bit, re-sign).
- On every hour by `CitizenStatusListPublisher` background service (for scheduled freshness).

**Signed by**: Org's derived key under `sorcha:citizen-status-signing` (slot 109 — see research R-004).

---

### A3. CitizenWalletSyncCursor (NEW — Wallet Service)

Tracks the last applied event watermark per `(PlatformUserId, DeviceId)` so the sync endpoint can ship deltas. Optional — could be derived from a credential-events stream — but materialised for query performance.

**Storage**: Wallet Service Postgres.

**EF entity**:
```csharp
public sealed class CitizenWalletSyncCursor
{
    public Guid Id { get; set; }
    public Guid PlatformUserId { get; set; }
    public Guid PlatformUserDeviceId { get; set; }
    public long LastEventSeq { get; set; }
    public DateTimeOffset LastSyncAt { get; set; }
}
```

**Unique constraint**: `(PlatformUserId, PlatformUserDeviceId)`.

---

### A4. CitizenPresentationLogReplica (NEW — Blueprint Service via existing Feature 111 lifecycle)

Server-side mirror of presentation events reported by the wallet on sync. Stored as Feature 111 lifecycle records on the originating register (NOT a new table) — the `OfflinePresentationConsumer` writes `PresentationInitiated` and `PresentationOutcome` transactions with the offline timestamps preserved.

**No new entity** — extends the existing register-resident lifecycle event format. Schema delta documented in `contracts/presentation-lifecycle-offline-extension.md`.

---

## B. On-device entities (IndexedDB schema)

Database name: `sorcha-wallet`, version 1. Five object stores.

### B1. `device` (singleton)

| Key | `"self"` |
|---|---|
| `keypair.signingKeyHandle` | `CryptoKey` (non-extractable, ECDSA P-256) — used for OID4VP presentation proofs |
| `keypair.wrappingKeyHandle` | `CryptoKey` (non-extractable, HMAC-SHA256) — used to derive content key (R-002) |
| `keypair.publicJwk` | JWK JSON — citizen-readable, sent to server on enrol |
| `keypair.thumbprint` | string (43-char base64url) — the device key ID |
| `wrapping.salt` | Uint8Array(32) — random per-enrolment, stored alongside `wrappedContentKey` |
| `wrapping.nonce` | Uint8Array(12) — AES-GCM nonce for the wrap |
| `wrapping.wrappedContentKey` | Uint8Array(48) — AES-GCM-256 ciphertext + tag |
| `enrolment.deviceId` | string (Guid) — server-assigned `PlatformUserDevice.Id` |
| `enrolment.platformUserId` | string (Guid) |
| `enrolment.label` | string |
| `enrolment.enrolledAt` | string (ISO 8601 UTC) |

### B2. `delegation` (singleton)

| Key | `"self"` |
|---|---|
| `jwt` | string (compact JWS) — current device delegation credential (an SD-JWT VC) |
| `holderPublicJwk` | JWK JSON — extracted convenience copy |
| `expiresAt` | string (ISO 8601 UTC) |
| `statusListUri` | string (URL) — points to org's citizen-devices status list |
| `statusListIndex` | integer — bit position |
| `lastRenewedAt` | string (ISO 8601 UTC) |

### B3. `credentials` (per-credential rows)

Keyed by credential ID (UUID).

| Field | Type | Notes |
|---|---|---|
| `id` | string (UUID) | server-assigned credential identifier |
| `vct` | string | credential type URI |
| `issuerDid` | string | `did:sorcha:org:...` |
| `cnf.jwk` | JWK | the holder key the credential is bound to (verifier reads this) |
| `displayMeta` | object | issuer-supplied display hints (name, theme, icon URL — re-uses Feature 107 `x-review` shape) |
| `issuedAt` | string (ISO 8601 UTC) | |
| `expiresAt` | string \| null | credential expiry, if any |
| `statusListUri` | string \| null | for revocation checking by verifiers |
| `statusListIndex` | integer \| null | bit position |
| `ciphertext` | Uint8Array | XChaCha20-Poly1305(CK, nonce, jwt) |
| `nonce` | Uint8Array(24) | per-credential nonce |
| `cachedAt` | string (ISO 8601 UTC) | when wallet first stored it locally |
| `lastSeenInSyncAt` | string (ISO 8601 UTC) | last sync that confirmed credential still active |

### B4. `statusLists` (per-list rows)

Keyed by status list URI.

| Field | Type | Notes |
|---|---|---|
| `uri` | string | primary key |
| `bitstring` | Uint8Array | decoded bits |
| `iat` | integer (epoch s) | issued-at from list JWT |
| `exp` | integer (epoch s) | expiry from list JWT |
| `signedJwt` | string | original JWT for re-presentation if needed |
| `fetchedAt` | string (ISO 8601 UTC) | local fetch time |

### B5. `syncQueue` (autoincrement key)

| Field | Type | Notes |
|---|---|---|
| `id` | autoincrement int | primary key |
| `kind` | string | `"presentation-log"` \| `"renew-delegation"` \| `"refresh-status-list"` \| `"ack-credential-receipt"` |
| `payload` | object | kind-specific payload |
| `createdAt` | string (ISO 8601 UTC) | |
| `attempts` | integer | for retry/backoff |
| `lastAttemptAt` | string (ISO 8601 UTC) \| null | |
| `lastError` | string \| null | most recent failure reason |

---

## C. Cryptographic artefact schemas

### C1. Device delegation credential (SD-JWT VC)

JWT header:
```json
{ "alg": "ES256", "typ": "vc+sd-jwt", "kid": "<holder key kid>" }
```

JWT payload (selected claims):
```json
{
  "iss": "did:sorcha:holder:<holderKeyId>",
  "sub": "did:sorcha:device:<deviceJwkThumbprint>",
  "iat": 1735689600,
  "exp": 1767225600,
  "vct": "https://sorcha.dev/vc/citizen-device-delegation/v1",
  "delegated_capabilities": ["presentation.holder-key-binding"],
  "device": {
    "label": "Stuart's iPhone 16",
    "platform": "iOS 19 / Safari 19",
    "enrolled_at": 1735689600
  },
  "cnf": { "jwk": { "kty": "EC", "crv": "P-256", "x": "...", "y": "..." } },
  "status": {
    "status_list": {
      "uri": "https://sorcha.dev/api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt",
      "idx": 4711
    }
  }
}
```

Signed with the holder key (slot 108, `sorcha:citizen-holder`). No selective disclosure salts — every claim is mandatory; this is a closed credential.

### C2. Token Status List JWT (per R-003)

JWT header:
```json
{ "alg": "ES256", "typ": "statuslist+jwt", "kid": "<org status-signing kid>" }
```

JWT payload:
```json
{
  "iss": "did:sorcha:org:<walletAddress>",
  "iat": 1735689600,
  "exp": 1735776000,
  "sub": "https://sorcha.dev/api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt",
  "status_list": {
    "bits": 1,
    "lst": "<base64url(zlib(packed bits))>"
  }
}
```

Signed with `sorcha:citizen-status-signing` (slot 109).

### C3. OID4VP key-binding JWT (presentation proof)

JWT header:
```json
{ "alg": "ES256", "typ": "kb+jwt", "kid": "<device key thumbprint>" }
```

JWT payload:
```json
{
  "iat": 1735689600,
  "aud": "did:sorcha:verifier:<orgId>",
  "nonce": "<verifier-supplied nonce>",
  "sd_hash": "<base64url(SHA-256(KB-input))>"
}
```

Signed with the device signing key (B1 `keypair.signingKeyHandle`).

---

## D. Cross-entity invariants

- **Holder uniqueness**: One `holderKeyId` per `PlatformUserId`. Derived deterministically; not stored. Guaranteed by derivation under `sorcha:citizen-holder` with the PlatformUserId-scoped wallet.
- **Device delegation freshness**: For any active offline presentation, `delegation.exp` MUST be > `now()` at the verifier. Enforced by R-007 replay check + verifier evaluation.
- **Status list consistency**: For any `PlatformUserDevice` with `StatusListIndex = N`, the `Bitstring` of its org's status list MUST have bit N set iff `Status == Revoked`. Enforced by `CitizenStatusListPublisher` regeneration job.
- **Credential ↔ holder binding**: Every cached credential's `cnf.jwk` MUST equal the citizen's holder public JWK on the device. If not (e.g. issued before this PlatformUser had a wallet), the credential is unusable in offline presentation and shown to the citizen as "not yet usable in wallet — re-issue required" (handled at sync time).
- **Sync token monotonicity**: A wallet's `LastEventSeq` is monotonically non-decreasing. A sync token from a future seq is rejected.

---

## E. Out-of-scope entities (Phase 2+)

- `PersonaCacheEnvelope` (Phase 2 — persona offline) — distinct content key, separate IndexedDB store.
- `MdocCredentialCache` (Phase 6) — alternate cache for binary mdoc credentials.
- `ProximityTransportSession` (Phase 5) — transient state for BLE/NFC handshakes.

These are noted to confirm the v1 schema does not preclude them; design slots them under separate object stores rather than mutating existing ones.
