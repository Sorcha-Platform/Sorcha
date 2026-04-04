# Data Model: Cloud KMS Key Management

## Entities

### Wallet (Modified)

Existing entity extended with two new fields:

| Field | Type | Default | Nullable | Description |
|-------|------|---------|----------|-------------|
| SigningMode | SigningMode enum | Local | No | How this wallet's keys are managed |
| KmsKeyId | string | null | Yes | Cloud KMS key identifier (null for Local wallets) |

Existing fields unchanged:
- `EncryptedPrivateKey` (string, nullable) — base64 AES-256-GCM ciphertext. Populated for Local wallets, null for KmsResident.
- `EncryptionKeyId` (string) — DEK identifier. Used for Local wallets.
- `PublicKey` (string) — Always populated. For KmsResident, retrieved from KMS at creation.
- `Address` (string) — Always populated. Derived from public key.
- `Algorithm` (WalletAlgorithm enum) — For KmsResident, must be P256.

### SigningMode (New Enum)

| Value | Description |
|-------|-------------|
| Local | Private key stored encrypted locally using envelope encryption (DEK + AES-256-GCM) |
| KmsResident | Private key created and held within cloud KMS. No local key material. |

### KmsKeyInfo (New Model)

Response from `ISigningProvider.CreateSigningKeyAsync`:

| Field | Type | Description |
|-------|------|-------------|
| KeyId | string | Cloud provider's key identifier/URI |
| PublicKey | byte[] | Public key bytes (P-256 uncompressed point) |
| Algorithm | string | Algorithm name (e.g., "P-256") |
| CreatedAt | DateTimeOffset | When the key was created in the KMS |

### WalletKeyManagementOptions (New Configuration)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| DefaultSigningMode | SigningMode | Local | Default for wallets not matching any policy rule |
| KmsResidentPaths | string[] | System paths (100-103) | Derivation paths that default to KmsResident |
| AllowSigningModeOverride | bool | true | Whether API callers can override the policy default |

## State Transitions

### Wallet Signing Mode

Set at creation time, immutable after creation:

```
CreateWallet(path, algorithm, signingMode?)
  |
  +-- Policy resolves to Local  --> SigningMode = Local (has EncryptedPrivateKey, no KmsKeyId)
  +-- Policy resolves to KmsResident --> SigningMode = KmsResident (no EncryptedPrivateKey, has KmsKeyId)
```

No transition from Local → KmsResident or vice versa after creation (would require key migration — deferred scope).

## Validation Rules

- If `SigningMode == KmsResident`, then `Algorithm` MUST be `P256`
- If `SigningMode == KmsResident`, then `EncryptedPrivateKey` MUST be null
- If `SigningMode == KmsResident`, then `KmsKeyId` MUST NOT be null
- If `SigningMode == Local`, then `EncryptedPrivateKey` MUST NOT be null
- If `SigningMode == Local`, then `KmsKeyId` SHOULD be null

## EF Core Migration

```
ALTER TABLE "Wallets" ADD COLUMN "SigningMode" integer NOT NULL DEFAULT 0;
ALTER TABLE "Wallets" ADD COLUMN "KmsKeyId" text NULL;
```

Default value 0 = `SigningMode.Local`. All existing wallets remain Local with no data changes.
