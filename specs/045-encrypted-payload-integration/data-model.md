# Data Model: Encrypted Payload Integration

**Feature**: 045-encrypted-payload-integration
**Date**: 2026-03-01

## New Entities

### EncryptedPayloadGroup

Represents a single disclosure group's encrypted payload with per-recipient wrapped keys.

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| GroupId | string | Deterministic hash of sorted disclosed field names | SHA-256 hex, 64 chars |
| DisclosedFields | string[] | Sorted list of JSON Pointer paths included in this group | Non-empty, valid JSON Pointers |
| Ciphertext | byte[] | Encrypted payload data (XChaCha20-Poly1305 or AES-256-GCM) | Non-empty |
| Nonce | byte[] | Encryption nonce/IV | 24 bytes (XChaCha20) or 12 bytes (AES-GCM) |
| PlaintextHash | byte[] | SHA-256 hash of plaintext for post-decryption integrity verification | 32 bytes |
| EncryptionAlgorithm | EncryptionType | Symmetric cipher used | XCHACHA20_POLY1305 (default) or AES_GCM |
| WrappedKeys | WrappedKey[] | Per-recipient wrapped symmetric keys | At least 1 entry |

### WrappedKey

A symmetric key encrypted (wrapped) for a specific recipient.

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| WalletAddress | string | Recipient's wallet address | Non-empty, Base58 |
| EncryptedKey | byte[] | Symmetric key wrapped with recipient's public key | Non-empty |
| Algorithm | WalletNetworks | Asymmetric algorithm used for wrapping | ED25519, NISTP256, RSA4096, ML_KEM_768 |

### DisclosureGroup

Intermediate grouping used during encryption pipeline (not persisted on-chain).

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| GroupId | string | Deterministic hash of sorted field names | SHA-256 hex, 64 chars |
| DisclosedFields | string[] | Sorted JSON Pointer paths | Non-empty |
| FilteredPayload | Dictionary&lt;string, object&gt; | Disclosure-filtered payload data (plaintext, pre-encryption) | Non-null |
| Recipients | RecipientInfo[] | Recipients sharing this exact disclosure set | At least 1 |

### RecipientInfo

Recipient with resolved public key for encryption.

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| WalletAddress | string | Recipient wallet address | Non-empty, Base58 |
| PublicKey | byte[] | Resolved public key bytes | Non-empty |
| Algorithm | WalletNetworks | Key algorithm | Valid enum value |
| Source | KeySource | How the key was obtained | Register or External |

### EncryptionOperation

Trackable background encryption operation.

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| OperationId | string | Unique tracking identifier | GUID format |
| Status | EncryptionOperationStatus | Current state | Valid enum value |
| BlueprintId | string | Source blueprint | Non-empty |
| ActionId | string | Source action | Non-empty |
| InstanceId | string | Workflow instance | Non-empty |
| SubmittingWalletAddress | string | Wallet that initiated the action | Non-empty, Base58 |
| TotalRecipients | int | Number of recipients | >= 1 |
| TotalGroups | int | Number of disclosure groups | >= 1 |
| CurrentStep | int | Current pipeline step (1-based) | 1 to TotalSteps |
| TotalSteps | int | Total pipeline steps | >= 1 |
| StepName | string | Human-readable current step | Non-empty |
| PercentComplete | int | Progress percentage | 0 to 100 |
| TransactionHash | string? | Result transaction hash on success | 64-char hex or null |
| Error | string? | Error message on failure | Null unless failed |
| FailedRecipient | string? | Wallet address of failed recipient | Null unless failed |
| CreatedAt | DateTimeOffset | Operation creation time | UTC |
| CompletedAt | DateTimeOffset? | Operation completion time | UTC or null |

## Enums

### EncryptionOperationStatus

| Value | Description |
|-------|-------------|
| Pending | Queued, awaiting processing |
| ResolvingKeys | Resolving recipient public keys |
| Encrypting | Encrypting payload groups |
| BuildingTransaction | Assembling encrypted transaction |
| Submitting | Submitting to validator |
| Complete | Successfully submitted |
| Failed | Failed with error |

### KeySource

| Value | Description |
|-------|-------------|
| Register | Resolved from register's published participant index |
| External | Provided externally in the action submission request |

## Modified Entities

### PayloadModel (wire format — existing, extended)

New/modified fields:

| Field | Change | Description |
|-------|--------|-------------|
| ContentEncoding | New value: `"encrypted"` | Distinguishes encrypted from legacy plaintext payloads |
| PayloadFlags | New value: algorithm name | e.g., `"xchacha20-poly1305"` — symmetric cipher identifier |

Legacy payloads have `ContentEncoding: "identity"` or null, and `PayloadFlags: null`. The `IsLegacy()` check (zeroed IV) remains backward compatible.

### TransactionReceiverConfiguration (existing, modified)

| Field | Change | Description |
|-------|--------|-------------|
| MaxTransactionSizeBytes | Default: 1MB → 4MB | `4 * 1024 * 1024` |

### ActionSubmissionRequest (new/modified DTO)

New fields for externally-provided public keys:

| Field | Type | Description |
|-------|------|-------------|
| ExternalRecipientKeys | Dictionary&lt;string, ExternalKeyInfo&gt;? | Optional map of wallet address → public key for recipients not on the register |

### ExternalKeyInfo

| Field | Type | Description |
|-------|------|-------------|
| PublicKey | string | Base64-encoded public key |
| Algorithm | string | Algorithm identifier: ED25519, NISTP256, RSA4096, ML_KEM_768 |

### BatchPublicKeyRequest (new DTO)

| Field | Type | Description | Validation |
|-------|------|-------------|------------|
| WalletAddresses | string[] | Wallet addresses to resolve | 1-200 entries, non-empty strings |
| Algorithm | string? | Optional algorithm filter | Valid algorithm name or null |

### BatchPublicKeyResponse (new DTO)

| Field | Type | Description |
|-------|------|-------------|
| Resolved | Dictionary&lt;string, PublicKeyResolution&gt; | Successfully resolved keys |
| NotFound | string[] | Addresses with no published participant record |
| Revoked | string[] | Addresses whose participant record is revoked |

## State Transitions

### EncryptionOperation Lifecycle

```
Pending → ResolvingKeys → Encrypting → BuildingTransaction → Submitting → Complete
                ↓              ↓              ↓                  ↓
              Failed         Failed         Failed             Failed
```

- **Pending → ResolvingKeys**: Picked up by EncryptionBackgroundService
- **ResolvingKeys → Encrypting**: All public keys resolved (batch + external)
- **Encrypting → BuildingTransaction**: All groups encrypted, all keys wrapped
- **BuildingTransaction → Submitting**: Transaction assembled, signed
- **Submitting → Complete**: Validator accepted transaction
- **Any → Failed**: Unrecoverable error at any step (atomic — no partial state)

## Relationships

```
ActionSubmission (1) ──creates──▶ EncryptionOperation (1)
EncryptionOperation (1) ──produces──▶ EncryptedPayloadGroup (M)
EncryptedPayloadGroup (1) ──contains──▶ WrappedKey (N per group)
DisclosureGroup (M) ──maps to──▶ EncryptedPayloadGroup (M)
DisclosureGroup (1) ──has──▶ RecipientInfo (N per group)
```

Where M = number of unique disclosure field sets, N = number of recipients per group.
