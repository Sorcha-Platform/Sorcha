# Data Model: Wallet Key Derivation & UI Transaction Lifecycle

**Feature**: 083-wallet-key-derivation
**Date**: 2026-04-04

## Entity Relationship Overview

```
Organisation (Tenant Service)
  │
  └── OrgMasterKey (1:1)
        │
        └── DerivedKeyRecord (1:N)
              │
              └── Wallet (1:1) ── WalletTransaction (1:N)
                                       │
                                       └── TransactionTickStatus (enum: Pending, Sealed, Receipted)

ThresholdKeyGroup (standalone, schema only)
  ├── SigningKeyShare (1:N)
  └── SigningSession (1:N)
```

---

## New Entities

### OrgMasterKey

Represents an organisation's root HD seed for hierarchical key derivation. One per organisation.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| OrganizationId | string | Unique, NOT NULL | Organisation reference (from Tenant Service) |
| EncryptedSeed | byte[] | NOT NULL | Master seed encrypted by IOrgKeyProtectionProvider |
| ProtectionProvider | string | NOT NULL | Provider name: "Software" or "AzureKeyVault" |
| ProtectionKeyId | string | NOT NULL | Key reference used for encryption (for decryption lookup) |
| Algorithm | string | NOT NULL, default "ED25519" | Cryptographic algorithm for derived keys |
| MasterPublicKey | string | NOT NULL | Extended public key (xpub) for verification without decryption |
| Status | enum | NOT NULL, default Active | Active, Rotated, Revoked |
| CreatedAt | DateTime | NOT NULL | Provisioning timestamp |
| RotatedAt | DateTime? | | Timestamp of last rotation (null if never rotated) |
| CreatedBy | string | NOT NULL | Admin user ID who provisioned |

**Indexes**: Unique on `OrganizationId`

**State transitions**:
- `Active` → `Rotated` (when master key is rotated — future capability)
- `Active` → `Revoked` (emergency revocation)
- `Rotated` / `Revoked` are terminal states

---

### DerivedKeyRecord

Links a user to a specific key derived from the org master seed. One record per derivation path.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| OrgMasterKeyId | Guid | FK → OrgMasterKey, NOT NULL | Parent master key |
| OrganizationId | string | NOT NULL | Denormalised for query performance |
| UserId | string | NOT NULL | User reference (from Tenant Service) |
| DepartmentId | uint | NOT NULL, default 0 | Department level (0 = flat org) |
| KeyUsage | enum | NOT NULL | Identity=0, VCIssuance=1, Governance=2, Communications=3, ServiceAuth=4 |
| KeyIndex | uint | NOT NULL, default 0 | Rotation index — increment to rotate |
| DerivationPath | string | NOT NULL | Full BIP32 path string (e.g., m/0x534F52'/123'/0'/456'/0/0) |
| WalletAddress | string | FK → Wallet, NOT NULL | Resulting wallet |
| Status | enum | NOT NULL, default Active | Active, Rotated, Revoked |
| CustodyMode | enum | NOT NULL, default Custodial | Custodial, CoSigned, SelfCustody |
| CreatedAt | DateTime | NOT NULL | Derivation timestamp |
| RevokedAt | DateTime? | | Revocation timestamp (null if active) |

**Indexes**:
- Unique composite: `(OrgMasterKeyId, UserId, DepartmentId, KeyUsage, KeyIndex)`
- Non-unique: `(OrganizationId, UserId)` for user key lookup
- Non-unique: `(WalletAddress)` for wallet→derivation record lookup

**State transitions**:
- `Active` → `Rotated` (key rotation — new key at next index)
- `Active` → `Revoked` (permanent revocation, wallet locked)
- `Rotated` allows decryption only; `Revoked` blocks all operations

**Validation rules**:
- KeyUsage must be a valid enum value (0-4)
- KeyIndex must be monotonically increasing per (user, usage) combination
- DepartmentId defaults to 0 for flat organisations
- WalletAddress must reference an existing wallet

---

### Wallet (Modified)

Two new fields added to the existing Wallet entity.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| DerivedKeyRecordId | Guid? | FK → DerivedKeyRecord, nullable | Null for standalone/legacy wallets, set for org-derived wallets |
| CustodyMode | enum | NOT NULL, default Custodial | Custodial, CoSigned, SelfCustody |

**Migration note**: Existing wallets get `DerivedKeyRecordId = null` and `CustodyMode = Custodial` (defaults). No data migration needed.

---

## Threshold Signing Entities (Schema Only)

These tables are created by the migration but have no service code, endpoints, or business logic in this release.

### ThresholdKeyGroup

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| GroupPublicKey | string | NOT NULL | Combined group public key |
| Threshold | int | NOT NULL | K — minimum signers required |
| TotalShares | int | NOT NULL | N — total participants |
| Algorithm | string | NOT NULL | e.g., "FROST-ED25519" |
| DkgSessionId | string? | | Reference to DKG ceremony that created this group |
| OrganizationId | string | NOT NULL | Organisation scope |
| Status | enum | NOT NULL, default Pending | Pending, Active, Revoked |
| CreatedAt | DateTime | NOT NULL | |

**Indexes**: Non-unique on `OrganizationId`

### SigningKeyShare

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| ThresholdKeyGroupId | Guid | FK → ThresholdKeyGroup, NOT NULL | Parent group |
| ParticipantId | string | NOT NULL | User or service identity |
| ShareIndex | int | NOT NULL | Position in share set (1-based) |
| EncryptedShareData | byte[] | NOT NULL | Encrypted share material |
| ProtectionKeyId | string | NOT NULL | Key reference for decryption |
| Status | enum | NOT NULL, default Active | Active, Revoked |
| CreatedAt | DateTime | NOT NULL | |

**Indexes**: Unique composite: `(ThresholdKeyGroupId, ShareIndex)`

### SigningSession

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | Guid | PK | Unique identifier |
| ThresholdKeyGroupId | Guid | FK → ThresholdKeyGroup, NOT NULL | Group being used for signing |
| TransactionId | string? | | Transaction being signed (null for non-tx ceremonies) |
| State | enum | NOT NULL, default Initializing | Initializing, Round1, Round2, Complete, Failed |
| RequiredSigners | int | NOT NULL | K from group threshold |
| CollectedPartials | int | NOT NULL, default 0 | Partial signatures received so far |
| ExpiresAt | DateTime | NOT NULL | Session timeout |
| CreatedAt | DateTime | NOT NULL | |
| CompletedAt | DateTime? | | Completion timestamp |

**Indexes**: Non-unique on `ThresholdKeyGroupId`

---

## Enumerations

### KeyUsage
| Value | Name | Description |
|-------|------|-------------|
| 0 | Identity | DID identity keys |
| 1 | VCIssuance | Verifiable Credential issuance signing |
| 2 | Governance | Governance/voting keys |
| 3 | Communications | Encrypted communications |
| 4 | ServiceAuth | Service-to-service authentication |

### CustodyMode
| Value | Name | Description |
|-------|------|-------------|
| 0 | Custodial | Full key in server storage, encrypted at rest |
| 1 | CoSigned | Server share + device share (future) |
| 2 | SelfCustody | Full key on device, optional recovery escrow (future) |

### OrgMasterKeyStatus
| Value | Name | Description |
|-------|------|-------------|
| 0 | Active | Master key in use for derivation |
| 1 | Rotated | Master key replaced (derived keys still valid) |
| 2 | Revoked | Master key permanently disabled |

### DerivedKeyStatus
| Value | Name | Description |
|-------|------|-------------|
| 0 | Active | Key can sign and decrypt |
| 1 | Rotated | Key can decrypt only (signing disabled) |
| 2 | Revoked | Key fully disabled |

### ThresholdKeyGroupStatus
| Value | Name | Description |
|-------|------|-------------|
| 0 | Pending | DKG in progress |
| 1 | Active | Group ready for signing |
| 2 | Revoked | Group permanently disabled |

### SigningSessionState
| Value | Name | Description |
|-------|------|-------------|
| 0 | Initializing | Session created, awaiting participants |
| 1 | Round1 | First round of FROST protocol |
| 2 | Round2 | Second round of FROST protocol |
| 3 | Complete | Signing succeeded |
| 4 | Failed | Signing failed or timed out |

---

## Migration Strategy

- **Single squashed migration** for the Wallet Service DbContext
- All new tables and wallet modifications in one migration
- Threshold tables created empty — no seed data
- Existing wallets get default values: `DerivedKeyRecordId = null`, `CustodyMode = Custodial`
- No intermediate migrations — clean history
