# Data Model: Production Issuer Signature Verification

**Feature**: 120-production-issuer-signature-verification
**Phase**: 1 (data model)
**Date**: 2026-05-09

## Scope

This document captures the data shapes — entities, fields, relationships, validation rules — introduced or modified by Feature 120. Implementation-level concerns (DI lifetimes, EF migrations, JSON serialization quirks) live in the design doc and code; this document is the contract between requirements and persistence.

## Entities

### 1. `OrgDidDocument` (NEW — Tenant Service, persistent)

The published DID document for one organisation. One document covers both `did:sorcha:org:{addr}` and `did:web:{platform}:orgs:{orgId}` — the document declares both identifiers and links them via `alsoKnownAs`.

| Field | Type | Constraints | Source |
|-------|------|-------------|--------|
| `Id` | `Guid` | Primary key. | Generated. |
| `OrganizationId` | `Guid` | FK → `Organization.Id`. Unique (one document per org). | FR-004. |
| `PrimaryDid` | `string` | Canonical primary DID (`did:sorcha:org:{addr}`). Indexed. Max 200 chars. | FR-005. |
| `FederatedDid` | `string` | Federated DID (`did:web:{platform}:orgs:{orgId}`). Indexed. Max 200 chars. | FR-005. |
| `DocumentJson` | `string` | The serialized W3C DID document (the same JSON served at the public endpoint). UTF-8, max 16KB. | FR-007. |
| `KeyVersionFingerprint` | `string` | Hash of `(PrimaryDid, all-active-VMs sorted by id, alsoKnownAs sorted)`. Used to detect when regeneration is a no-op. | FR-006. |
| `LastRegeneratedAt` | `DateTimeOffset` | When the document was last regenerated. | FR-006. |
| `LastRegenerationReason` | `KeyEventReason` enum | What triggered the most recent regeneration: `IssuanceKeyDerived`, `IssuanceKeyRotated`, `IssuanceKeyRevoked`, `StatusSigningKeyDerived`, `Bootstrap`. | FR-006. |
| `Version` | `int` | Monotonic version counter. Incremented on every regeneration. v1 starts at 1. | Forward-compat for FR-022 (cache invalidation; future version metadata per R8). |

**Validation rules**:
- `PrimaryDid` MUST start with `did:sorcha:org:`.
- `FederatedDid` MUST start with `did:web:` and MUST contain the platform domain configured at deploy time.
- `DocumentJson` MUST parse as a valid W3C DID document with a non-empty `verificationMethod` array. JSON Schema validation REQUIRED on save (per Constitution II).
- `DocumentJson.alsoKnownAs` MUST contain exactly the other identifier (`PrimaryDid` ↔ `FederatedDid`). Other entries permitted in future for BYO-domain.

**State transitions**:
- `null` (no document) → `Bootstrap` regeneration on first issuance key derivation (Phase 2 trigger).
- `IssuanceKeyDerived` → `IssuanceKeyRotated` → `IssuanceKeyRevoked` (manual via governance ops).
- `StatusSigningKeyDerived` is additive; never invalidates existing VMs.

**Indexing**:
- Unique index on `OrganizationId` (one doc per org).
- Index on `PrimaryDid` (resolver lookup path).
- Index on `FederatedDid` (resolver lookup path).

**Storage**: PostgreSQL via Tenant Service's existing EF Core context. Audited storage interface (`Sorcha.ServiceDefaults.Storage` per Feature 113), but **NOT on the fail-fast list** — this is cache-style storage; rebuildable from the wallet's key state if lost. Logs warning if registered as in-memory; does not gate startup.

---

### 2. `Organization` (EXISTING — additive field)

| Field | Type | Constraints | Source |
|-------|------|-------------|--------|
| `DefaultKidStyle` | `KidStyle` enum | Default `Versioned`. Not exposed in v1 admin UI. | FR-013. |

**`KidStyle` enum**: `Versioned = 0` (default — emit `#vc-issuance-{n}` style kid in JWS headers), `Thumbprint = 1` (emit `#{rfc7638-thumbprint}` style kid).

**Migration**: additive non-nullable column with default value `0`. EF migration via `dotnet ef migrations add AddOrganizationDefaultKidStyle`. No data backfill needed.

---

### 3. `RegisterControlRecord.RegisterPolicy` (EXISTING — two additive fields, RESERVED)

| Field | Type | Constraints | Source |
|-------|------|-------------|--------|
| `RequireIssuerSignature` | `bool?` | Optional. Null/absent = use platform default. **NOT READ AT V1.** | FR-020. |
| `PermittedIssuers` | `string[]?` | Optional. Null/empty = no register-wide allowlist (any resolvable issuer). **NOT READ AT V1.** | FR-021. |

**Validation rules**:
- `RequireIssuerSignature`: `bool?`, no further constraint.
- `PermittedIssuers`: each entry MUST start with `did:` and MUST be ≤200 chars. Validated by `RegisterPolicyValidator`.

**JSON serialization**: both fields use `JsonIgnoreCondition.WhenWritingNull` so existing genesis records produced before this feature do not gain spurious null fields.

**Forward-compat verification (SC-007)**: a v0.119 register policy record (no `RequireIssuerSignature`, no `PermittedIssuers`) MUST deserialise cleanly with both fields null after this feature ships. Test in `Sorcha.Register.Models.Tests/RegisterControlRecordBackwardCompatTests.cs`.

---

### 4. `IssuanceKeyState` (NEW — Wallet Service, persistent)

The lifecycle state of one organisation's issuance key. Persisted alongside the existing wallet/key infrastructure.

| Field | Type | Constraints | Source |
|-------|------|-------------|--------|
| `Id` | `Guid` | Primary key. | Generated. |
| `OrganizationId` | `Guid` | FK → `Organization.Id`. | FR-018. |
| `Slot` | `int` | Feature 083 derivation slot. v1 = 1 (`KeyUsage.VCIssuance`). | FR-018. |
| `RotationIndex` | `int` | Monotonic; starts at 1 for the first derived key, increments on each rotation. Forms the kid suffix `#vc-issuance-{n}`. | FR-011, FR-017. |
| `Status` | `IssuanceKeyStatus` enum | `Active`, `Rotated`, `Revoked`. | FR-016, FR-017. |
| `PublicKey` | `byte[]` | Raw public key bytes (pre-multibase encoding). | FR-002. |
| `Algorithm` | `string` | Wallet algorithm string (`ED25519`, `NIST-P256`, etc.). | Driven by Feature 083. |
| `Thumbprint` | `string` | RFC 7638 base64url SHA-256 thumbprint of the JWK form. Cached for kid-resolution fallback. | FR-011, FR-012. |
| `DerivedAt` | `DateTimeOffset` | When this key was derived. | FR-018. |
| `RotatedAt` | `DateTimeOffset?` | When the key was rotated (status moved to `Rotated`). | FR-017. |
| `RevokedAt` | `DateTimeOffset?` | When the key was revoked. | FR-016. |
| `RevocationReason` | `string?` | Free text recorded with the revocation governance op. Max 500 chars. | FR-016. |
| `RevokedByGovernanceOpId` | `Guid?` | FK to the governance op that revoked the key. | FR-016. |

**`IssuanceKeyStatus` enum**: `Active = 0`, `Rotated = 1`, `Revoked = 2`.

**State transitions**:
- `null` (no key) → `Active` (first derivation).
- `Active` → `Rotated` (governance op `RotateIssuanceKey`; new row with incremented `RotationIndex` becomes `Active`).
- `Active` → `Revoked` (governance op `VAL_CRED_GOV_001`).
- `Rotated` → `Revoked` permitted (revoking a previously-rotated key).
- `Revoked` → terminal (no further transitions).

**Validation rules**:
- For each `(OrganizationId, Slot)` at most ONE row in `Active` status (DB unique partial index).
- `RotationIndex` MUST be unique per `OrganizationId`.
- `PublicKey` length MUST match the algorithm's expected raw size (validated by `Sorcha.Cryptography`).
- `Thumbprint` regex `^[A-Za-z0-9_-]{43}$` (base64url SHA-256, no padding).

**Storage**: PostgreSQL via Wallet Service's existing context. Audited storage interface (Feature 113) — cache-style, NOT on fail-fast list (rebuildable from the master key + persisted state).

**Privacy**: this entity contains no private key material. Private keys remain in Wallet Service's existing custodial storage (Feature 083) and are never returned by `IIssuanceKeyService` queries.

---

### 5. `CredentialRequirement.AcceptedIssuers` (EXISTING — semantic change only)

No model change. The `AcceptedIssuers` field already exists at `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs:28`.

**Semantic change (FR-014)**: when matching a credential's `IssuerDid` against an `AcceptedIssuers` entry, the match succeeds if:
1. Direct string match (current behaviour), OR
2. The credential's `IssuerDid` resolves (via `IDidResolverRegistry.ResolveWithAlsoKnownAsAsync`) to a document whose `alsoKnownAs` contains an entry that matches a string in `AcceptedIssuers`, OR
3. The string in `AcceptedIssuers` resolves to a document whose `alsoKnownAs` contains the credential's `IssuerDid`.

The third case enables blueprint authors to list a `did:web` form and accept credentials issued under the equivalent `did:sorcha:org` form (and vice versa).

Updated in: `Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs:51-52`, `PresentationRequestService.cs:364-365`. Existing tests preserved + new tests for the equivalence cases.

---

## Relationships

```text
Organization (1) ────┬──── (1) OrgDidDocument
                     │
                     └──── (1..*) IssuanceKeyState  [ at most one Active per slot ]

RegisterControlRecord ──── (1) RegisterPolicy
                                  ├── RequireIssuerSignature (RESERVED, not read v1)
                                  └── PermittedIssuers      (RESERVED, not read v1)

CredentialRequirement.AcceptedIssuers (existing) → semantic enhancement only
```

## Resolution flow (data perspective)

```text
1. JWS header { iss, kid }
        │
        ▼
2. IDidResolverRegistry.ResolveWithAlsoKnownAsAsync(iss)
        │
        ├─→ method dispatch (sorcha | web | key)
        │       │
        │       ▼
        │    SorchaDidResolver | WebDidResolver | KeyDidResolver
        │       returns DidDocument for primary
        │
        ├─→ if doc.alsoKnownAs is non-empty:
        │       for each linked DID:
        │           recursive (non-cycling) ResolveAsync
        │           verify VerificationMethod.publicKeyMultibase matches across docs
        │       merge into result; null if any link fails or mismatches
        │
        └─→ DidDocument { verificationMethod: [ ...VMs across all valid links... ] }

3. Match kid → VerificationMethod
        │
        ├─→ exact-string match against verificationMethod[].id
        │
        └─→ thumbprint fallback (if no exact match):
                for each VM:
                    compute RFC 7638 thumbprint of VM.publicKeyJwk
                    if matches kid fragment → match
        │
        ▼
4. JsonWebKey extracted; signature verification proceeds.
```

## Telemetry-touching shapes (informational)

OTel attribute keys defined for this feature (no persistent schema; lists kept here so contributors don't drift them):

| Attribute | Values | Surface |
|---|---|---|
| `did.method` | `sorcha`, `web`, `key`, ... | All resolver spans |
| `did.alsoKnownAs.cross_resolved` | `true`, `false` | `did.resolve` span |
| `did.alsoKnownAs.match` | `match`, `mismatch`, `unreachable`, `none` | `did.resolve` span |
| `verifier.issuer.outcome` | `success`, `did-unresolved`, `kid-unmatched`, `signature-failed` | `verifier.issuer-resolve` span |
| `verifier.issuer.kid_match_mode` | `exact`, `thumbprint-fallback` | `verifier.issuer-resolve` span |

Counters listed in plan.md / spec FR-003 / SC-006.
