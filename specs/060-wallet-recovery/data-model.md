# Data Model: Wallet Recovery

**Feature**: 060-wallet-recovery | **Date**: 2026-03-17

## Modified Entities

### Wallet (Sorcha.Wallet.Core)

Add recovery-related columns to existing Wallet entity.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| EncryptedMasterKeyBlob | string | No | Master key encrypted with recovery key (AES-256-GCM). Base64. Null for wallets created before this feature. |
| RecoveryEnabled | bool | No | Whether recovery key wraps exist for this wallet. Default false. |

### New: RecoveryKeyWrap (Sorcha.Wallet.Core)

One-to-many from Wallet. Each wrap stores the recovery key encrypted to a different recipient's public key.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | Guid | Yes | Primary key |
| WalletAddress | string | Yes | FK to Wallet.Address |
| RecoveryPath | RecoveryPathType | Yes | Enum: Mnemonic, OrgManaged, Passkey |
| EncryptedRecoveryKey | string | Yes | Recovery key encrypted to recipient public key. Base64. |
| RecipientKeyId | string | Yes | Identifier of the public key used for wrapping (passkey CredentialId or org recovery key ID) |
| Algorithm | string | Yes | Asymmetric algorithm used for wrapping (e.g., ED25519, NISTP256) |
| CreatedAt | DateTimeOffset | Yes | When wrap was created |
| RevokedAt | DateTimeOffset? | No | When wrap was revoked (null = active) |

### New: RecoveryPathType Enum

```csharp
public enum RecoveryPathType
{
    Mnemonic,    // Path 1: Existing mnemonic-based (no wrap needed, marker only)
    OrgManaged,  // Path 3: Org admin recovery
    Passkey      // Path 4: Passkey-bound recovery
}
```

### New: RecoveryAuditLog (Sorcha.Wallet.Core)

Immutable audit trail for all recovery operations.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | Guid | Yes | Primary key |
| UserId | string | Yes | User whose wallets were recovered |
| TenantId | string | Yes | Organization context |
| RecoveryPath | RecoveryPathType | Yes | Which path was used |
| InitiatedBy | string | Yes | User ID of initiator (self or org admin) |
| WalletsRecovered | int | Yes | Count of wallets restored |
| DelegationsRevoked | int | Yes | Count of delegations revoked |
| DelegationsPreserved | int | Yes | Count of delegations user chose to keep |
| IpAddress | string? | No | Client IP for audit |
| Timestamp | DateTimeOffset | Yes | When recovery completed |

### New: OrgRecoveryConfig (Tenant Service)

Per-organization recovery key configuration.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Id | Guid | Yes | Primary key |
| OrganizationId | Guid | Yes | FK to Organization. Unique. |
| RecoveryPublicKey | string | Yes | ED25519 public key for wrapping recovery keys. Base64. |
| RecoveryKeyId | string | Yes | Stable identifier for this recovery key (for RecoveryKeyWrap.RecipientKeyId) |
| CreatedBy | string | Yes | Admin who configured recovery |
| CreatedAt | DateTimeOffset | Yes | When configured |
| RotatedAt | DateTimeOffset? | No | Last key rotation timestamp |

## Existing Entities (No Changes)

### WalletAccess (Delegation)

Used as-is. Recovery will call `DelegationService.RevokeAccessAsync()` for each active delegation, then selectively re-grant based on user choices.

### PasskeyCredential (Tenant Service)

Used as-is. `PublicKeyCose` field provides the passkey public key needed for wrapping. A new service client method will retrieve this.

## State Transitions

### Recovery Flow

```
User initiates recovery (passkey auth or org admin)
  → Verify identity (passkey challenge or admin MFA)
  → Retrieve RecoveryKeyWraps for all user wallets
  → Decrypt recovery key using recipient private key
  → Decrypt EncryptedMasterKeyBlob using recovery key
  → Re-encrypt master key with new encryption key ID
  → Update Wallet.EncryptedPrivateKey
  → Revoke all WalletAccess grants (default)
  → Prompt user to selectively preserve delegations
  → Log RecoveryAuditLog entry
  → Wallet(s) restored and usable
```

### Wallet Creation Flow (Modified)

```
Existing flow:
  Generate mnemonic → derive master key → encrypt private key → save wallet

New additions:
  → Generate AES-256 recovery key
  → Encrypt master key with recovery key → save as EncryptedMasterKeyBlob
  → If user has passkey: wrap recovery key to passkey public key → save RecoveryKeyWrap
  → If org has recovery config: wrap recovery key to org public key → save RecoveryKeyWrap
  → Set RecoveryEnabled = true
```
