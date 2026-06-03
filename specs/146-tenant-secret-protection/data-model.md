# Data Model: Tenant Service At-Rest Secret Protection

No new tables. Two existing entities gain/alter columns; changes are **squashed into the initial
migration** (pre-release clean break — see research D5). Mirrors Wallet's `EncryptedPrivateKey` +
`EncryptionKeyId` column pairing.

## Entity: `TotpConfiguration` (`Models/TotpConfiguration.cs`)

| Field | Before | After | Notes |
|-------|--------|-------|-------|
| `EncryptedSecret` | `string` (`v1:{base64}`) | **`byte[]`** (`bytea`) | AES-256-GCM envelope `nonce(12) ∥ ciphertext ∥ tag(16)` of the Base32 TOTP secret. |
| `EncryptionKeyId` | — | **`string`** (new, non-null, e.g. `varchar(64)`) | Identifier of the key that protected the secret (`"jwt-derived-v1"` or `"config-v1"`). |
| *(other fields)* | unchanged | unchanged | `UserId`, `BackupCodes`, `IsEnabled`, `CreatedAt`, `UpdatedAt`, `VerifiedAt` — untouched. Backup-code hashing unchanged. |

## Entity: `IdentityProviderConfiguration` (`Models/IdentityProviderConfiguration.cs`)

| Field | Before | After | Notes |
|-------|--------|-------|-------|
| `ClientSecretEncrypted` | `byte[]` (SHA-256 hash — irreversible) | **`byte[]`** (AES-256-GCM envelope) | Same column type; **semantics change** from one-way hash to reversible AEAD so the real secret is recoverable for the OIDC exchange. |
| `ClientSecretKeyId` | — | **`string`** (new, non-null) | Identifier of the key that protected the client secret. |
| *(other fields)* | unchanged | unchanged | Mapped in **two** `TenantDbContext` configurations (~lines 358 and 466) — both updated. |

## Logical value: protected-secret envelope

```
bytes = nonce(12) ‖ ciphertext(N) ‖ tag(16)     # AES-256-GCM, BCL AesGcm
stored = (bytes, keyId)                          # keyId in the sibling *KeyId column
```

- `nonce`: 12 random bytes per encryption (`RandomNumberGenerator.Fill`).
- `tag`: 16-byte GCM auth tag.
- Integrity: any tamper → `AuthenticationTagMismatchException` on decrypt → handled safely (FR-010).

## Validation / invariants

- `EncryptedSecret` / `ClientSecretEncrypted` length ≥ `nonce(12)+tag(16)` = 28 bytes, else reject as corrupt.
- `*KeyId` non-empty; an unknown `KeyId` at decrypt time → fail closed.
- Protection key length MUST be exactly 32 bytes (override rejected otherwise at startup).

## Migration

Edit (do **not** add a new migration):
- `Migrations/20260513152714_InitialCreate.cs` — `TotpConfigurations.EncryptedSecret` column → `bytea`; add `EncryptionKeyId`; add `ClientSecretKeyId` to the IdP table(s).
- `Migrations/20260513152714_InitialCreate.Designer.cs` + `Migrations/TenantDbContextModelSnapshot.cs` — keep in sync so EF reports no model drift at startup.
- DB is cleared on rollout; no data preserved.
