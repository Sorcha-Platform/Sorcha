# Wallet Key Derivation & UI Transaction Lifecycle

**Date:** 2026-04-04
**Status:** Approved
**Scope:** Org key derivation foundations, wallet UI transaction ticks, threshold signing schema prep

---

## Overview

Three-layer feature delivering org-level HD key derivation, wallet UI transaction lifecycle indicators, and forward-compatible schema for threshold signing. Builds on existing BIP32/39/44 HD wallet infrastructure, TransactionLifecycleService backend, and Feature 082 Cloud KMS foundations.

### Tranche Structure

| Layer | Scope | Status |
|-------|-------|--------|
| **Build now** | Wallet UI — transaction ticks, detail panel, receipt proofs | Implement |
| **Build now** | Org key derivation — master seed, user key service, rotation | Implement |
| **Schema only** | Threshold signing �� 3 tables, no service code | Migration only |

### Explicitly Deferred

- FROST sidecar implementation (R6-R7) — tables ready, no code
- Co-signed and self-custody custody modes — field exists, only Custodial implemented
- Azure KMS provider — `IOrgKeyProtectionProvider` interface ready, awaits Feature 082
- Policy enforcement (R10) — no schema, no code
- Device share management (R8) — future spec
- Dedicated outbound transaction view — future UI enhancement

---

## 1. Data Model & Schema

### New Entities (Wallet Service — PostgreSQL)

#### OrgMasterKey

One per organisation. Holds the encrypted master seed for hierarchical key derivation.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | PK |
| `OrganizationId` | string | Unique — one master key per org |
| `EncryptedSeed` | byte[] | Encrypted via IOrgKeyProtectionProvider |
| `ProtectionProvider` | string | "Software" or "AzureKeyVault" |
| `ProtectionKeyId` | string | Key reference for decryption |
| `Algorithm` | string | "ED25519" (default) |
| `MasterPublicKey` | string | Extended public key (xpub) for verification |
| `Status` | enum | Active, Rotated, Revoked |
| `CreatedAt` | DateTime | |
| `RotatedAt` | DateTime? | |
| `CreatedBy` | string | Admin user ID |

#### DerivedKeyRecord

Tracks every key derived from an org master, linking user → derivation path → wallet.

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | PK |
| `OrgMasterKeyId` | Guid | FK → OrgMasterKey |
| `OrganizationId` | string | Denormalised for query |
| `UserId` | string | |
| `DepartmentId` | uint | Default 0 (flat org) |
| `KeyUsage` | enum | Identity=0, VCIssuance=1, Governance=2, Communications=3, ServiceAuth=4 |
| `KeyIndex` | uint | Rotation index — increment to rotate |
| `DerivationPath` | string | Full path string for audit trail |
| `WalletAddress` | string | FK → Wallet |
| `Status` | enum | Active, Rotated, Revoked |
| `CustodyMode` | enum | Custodial, CoSigned, SelfCustody |
| `CreatedAt` | DateTime | |
| `RevokedAt` | DateTime? | |

**Unique constraint:** `(OrgMasterKeyId, UserId, DepartmentId, KeyUsage, KeyIndex)`

#### Wallet Entity Modifications

| Field | Type | Notes |
|-------|------|-------|
| `DerivedKeyRecordId` | Guid? | FK → DerivedKeyRecord. Null for standalone/legacy wallets |
| `CustodyMode` | enum | Custodial (default), CoSigned, SelfCustody |

### Threshold Signing Schema (Tables Only — No Implementation)

#### ThresholdKeyGroup

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | PK |
| `GroupPublicKey` | string | Combined group public key |
| `Threshold` | int | K (minimum signers) |
| `TotalShares` | int | N (total participants) |
| `Algorithm` | string | e.g. "FROST-ED25519" |
| `DkgSessionId` | string? | Reference to DKG ceremony that created it |
| `OrganizationId` | string | |
| `Status` | enum | Pending, Active, Revoked |
| `CreatedAt` | DateTime | |

#### SigningKeyShare

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | PK |
| `ThresholdKeyGroupId` | Guid | FK → ThresholdKeyGroup |
| `ParticipantId` | string | User or service identity |
| `ShareIndex` | int | Position in share set |
| `EncryptedShareData` | byte[] | Encrypted share material |
| `ProtectionKeyId` | string | Key reference for decryption |
| `Status` | enum | Active, Revoked |
| `CreatedAt` | DateTime | |

#### SigningSession

| Field | Type | Notes |
|-------|------|-------|
| `Id` | Guid | PK |
| `ThresholdKeyGroupId` | Guid | FK → ThresholdKeyGroup |
| `TransactionId` | string? | Transaction being signed |
| `State` | enum | Initializing, Round1, Round2, Complete, Failed |
| `RequiredSigners` | int | K from group |
| `CollectedPartials` | int | Partial signatures received |
| `ExpiresAt` | DateTime | Session timeout |
| `CreatedAt` | DateTime | |
| `CompletedAt` | DateTime? | |

### Migration Strategy

- Single squashed migration per DbContext — no intermediate migration chain
- Threshold tables created empty with indexes — no seed data, no service code

---

## 2. HD Derivation Path Schema

### Path Structure

```
m / 0x534F52' / org_id' / dept_id' / user_id' / key_usage / index
```

| Level | Value | Hardened | Notes |
|-------|-------|----------|-------|
| Purpose | `0x534F52'` (5,456,978) | Yes | "SOR" in hex — private-use namespace, avoids BIP43/SLIP-0044 collisions |
| Org ID | Numeric hash of org GUID | Yes | Deterministic mapping from org UUID |
| Dept ID | Department number | Yes | `0'` for flat organisations (always present, reserved) |
| User ID | Numeric hash of user GUID | Yes | Deterministic mapping from user UUID |
| Key Usage | 0-4 | No | See table below |
| Index | 0+ | No | Rotation — increment to rotate |

### Key Usage Values

| Value | Name | Purpose |
|-------|------|---------|
| 0 | Identity | DID identity keys |
| 1 | VCIssuance | Verifiable Credential issuance signing |
| 2 | Governance | Governance/voting keys |
| 3 | Communications | Encrypted communications (X25519 derived) |
| 4 | ServiceAuth | Service-to-service authentication |

### GUID to Derivation Index Mapping

Organisation and user GUIDs must be deterministically mapped to BIP32 uint31 values (0 to 2^31-1) for hardened derivation. Use first 4 bytes of SHA-256 hash, masked to 31 bits:

```csharp
uint DerivationIndex(Guid id) =>
    BitConverter.ToUInt32(SHA256.HashData(id.ToByteArray()), 0) & 0x7FFFFFFF;
```

### Collision Avoidance

- **BIP43 purposes**: 44', 49', 84', 86' are all registered. Our `0x534F52'` (5,456,978) is far above any registered value.
- **SLIP-0044 coin types**: Not applicable — Sorcha paths don't follow `m/44'/coin'/...` structure.
- **Internal collisions**: The unique constraint on `(OrgMasterKeyId, UserId, DepartmentId, KeyUsage, KeyIndex)` prevents duplicate derivations. SHA-256 collision probability at 2^31 is negligible for practical org/user counts.

---

## 3. Org Key Derivation Service

### Interface: IOrgKeyDerivationService

Located in `Sorcha.Wallet.Core`, implemented in Wallet Service.

```csharp
public interface IOrgKeyDerivationService
{
    /// Provision org master key. Returns mnemonic ONCE for admin backup.
    Task<OrgMasterKeyProvisionResult> ProvisionMasterKeyAsync(
        string organizationId, string algorithm = "ED25519",
        CancellationToken ct = default);

    /// Derive user key at path. Idempotent — returns existing if path already derived.
    Task<DerivedKeyResult> DeriveUserKeyAsync(
        string organizationId, string userId,
        uint departmentId, KeyUsage usage,
        CancellationToken ct = default);

    /// Rotate key — derives at next index, marks old as Rotated.
    Task<DerivedKeyResult> RotateKeyAsync(
        Guid derivedKeyRecordId,
        CancellationToken ct = default);

    /// Revoke key — marks as Revoked, locks wallet.
    Task RevokeKeyAsync(
        Guid derivedKeyRecordId,
        CancellationToken ct = default);
}
```

### Interface: IOrgKeyProtectionProvider

Pluggable seed encryption — software now, KMS later.

```csharp
public interface IOrgKeyProtectionProvider
{
    Task<(byte[] EncryptedSeed, string KeyId)> EncryptSeedAsync(
        byte[] seed, CancellationToken ct = default);

    Task<byte[]> DecryptSeedAsync(
        byte[] encryptedSeed, string keyId,
        CancellationToken ct = default);

    string ProviderName { get; } // "Software" or "AzureKeyVault"
}
```

Ships with `SoftwareKeyProtectionProvider` (AES-256-GCM, key from configuration). `AzureKmsKeyProtectionProvider` slots in when Feature 082 completes.

### Endpoints

All under Wallet Service, routed via YARP gateway.

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| POST | `/api/wallets/org/{orgId}/master-key` | RequireAdministrator | Provision master key (one-shot) |
| POST | `/api/wallets/org/{orgId}/derive-key` | RequireService or RequireAdministrator | Derive user key |
| POST | `/api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate` | RequireAdministrator | Rotate key |
| DELETE | `/api/wallets/org/{orgId}/keys/{derivedKeyId}` | RequireAdministrator | Revoke key |

### Auto-Derivation Hook

When Tenant Service fires a "user added to organisation" event (via SignalR or internal API), the Wallet Service automatically derives an Identity key (usage=0) at index 0 for the new user. This ensures every org member has a wallet without manual action.

---

## 4. Wallet UI — Transaction Lifecycle

### Transaction Table Enhancement

Add a "Status" column to the existing Transactions tab in `WalletDetail.razor`:

| Icon | State | Meaning |
|------|-------|---------|
| Grey ✓ | Pending | Transaction submitted, not yet sealed |
| Blue ✓ | Sealed | Transaction sealed in docket |
| Blue ✓✓ | Receipted | Receipt confirmed — cryptographic proof of finality |

### Transaction Detail Panel

Clicking a transaction row opens a `MudDrawer` slide-out panel with three sections:

1. **Lifecycle Timeline** — vertical timeline showing Submitted → Sealed → Receipted with timestamps and relative timing (e.g. "17s after submit")
2. **Details Grid** — register, direction, counterparty, sequence number, docket number, block height
3. **Receipt Proof** — receipt ID, Merkle root, validator address, signature. Two action buttons: "Verify Receipt" (calls existing verify endpoint) and "Download Bundle" (calls existing verification bundle endpoint)

### New Components

| Component | Purpose |
|-----------|---------|
| `TransactionTickIcon.razor` | Renders tick icon from `TransactionTickStatus` enum |
| `TransactionDetailDrawer.razor` | Slide-out panel with timeline, details, receipt |
| `ReceiptProofCard.razor` | Receipt proof display with verify/download actions |

### Real-Time Updates

The `TransactionLifecycleEventBridge` already fires SignalR events for `docket:confirmed` and `receipt:generated`. The UI subscribes to wallet-scoped SignalR groups and updates tick icons live. If the detail panel is open for the affected transaction, it updates in place.

---

## 5. Implementation Phases

### Phase 1 — Schema & Migrations

- Add all new entities to Wallet EF Core context
- Add threshold signing entities (empty tables)
- Modify Wallet entity (add `DerivedKeyRecordId`, `CustodyMode`)
- Single squashed migration
- Indexes and FK relationships

### Phase 2 — Org Key Derivation Service

- `IOrgKeyProtectionProvider` + `SoftwareKeyProtectionProvider`
- `IOrgKeyDerivationService` implementation
- Derivation path builder with GUID→index mapping
- 4 REST endpoints under `/api/wallets/org/{orgId}/...`
- Auto-derivation hook for user→org events
- Unit tests for derivation logic
- Integration tests for endpoints

### Phase 3 ��� Wallet UI Transaction Ticks (parallel with Phase 2)

- `TransactionTickIcon.razor` component
- `TransactionDetailDrawer.razor` with lifecycle timeline
- `ReceiptProofCard.razor` with verify/download
- Modify `WalletDetail.razor` Transactions tab
- Wire SignalR subscription for real-time updates
- E2E tests for tick rendering and detail panel

### Phase 4 — Integration & Polish

- YARP gateway route for `/api/wallets/org/**` → Wallet Service
- `IWalletServiceClient` methods for org key operations
- Scalar OpenAPI documentation for new endpoints
- Documentation sync (CLAUDE.md, service README, API docs)

### Dependency Graph

```
Phase 1 (Schema) ──→ Phase 2 (Key Derivation) ─��→ Phase 4 (Integration)
                 ──→ Phase 3 (UI Ticks) ─────────→ Phase 4 (Integration)
```

Phase 2 and Phase 3 can run in parallel after Phase 1 completes.

---

## Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Custody mode | Custodial now, schema for others | Co-signed needs device share infra (future) |
| Seed protection | Pluggable provider, software first | Doesn't block on Feature 082 KMS completion |
| Department level | Always in path, defaults to `0'` | Consistent path depth, no re-derivation needed later |
| Derivation namespace | `m/0x534F52'/...` | Private-use range, "SOR" hex, zero BIP43/SLIP-0044 collision |
| Threshold signing | 3 tables, no implementation | Forward-compatible schema avoids future migrations |
| API paths | `/api/wallets/org/{orgId}/...` | Consistent with dominant unversioned convention, clear gateway routing |
| UI scope | Ticks + detail panel, no dedicated outbound view | Backend ready, moderate UI effort, good DX improvement |
| Migrations | Single squash per project | Clean migration history, no intermediate chain |
