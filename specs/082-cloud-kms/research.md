# Research: Cloud KMS Key Management

## Decision 1: Interface Design — Replace vs Extend IEncryptionProvider

**Decision**: Replace `IEncryptionProvider` with two focused interfaces: `IKeyProtectionProvider` (DEK wrap/unwrap) and `ISigningProvider` (KMS-resident signing).

**Rationale**: The existing `IEncryptionProvider` conflates two concerns — it does both AES-256-GCM encryption of wallet data AND DEK lifecycle management. The `EncryptAsync`/`DecryptAsync` methods take plaintext private keys and return base64 ciphertext — they perform the full envelope encryption internally. For cloud KMS, only the DEK wrapping needs to touch the KMS. Splitting the interface cleanly separates:
- DEK protection (every wallet, every provider)
- KMS-resident signing (opt-in, cloud providers only)

**Alternatives considered**:
- Extend `IEncryptionProvider` with `SignAsync`/`VerifyAsync` — rejected because it forces providers to implement operations they don't support (Local has no signing).
- Keep `IEncryptionProvider` and add `ISigningProvider` alongside — rejected because the existing interface mixes concerns (encrypt/decrypt does AES-GCM + DEK fetch internally).

**Migration note**: `EncryptionProviderBase` already separates the DEK operations from AES-GCM via abstract hooks (`ProtectAndStoreKeyAsync`, `RetrieveKeyAsync`). The new `IKeyProtectionProvider` maps directly to these hooks. The AES-GCM encryption stays in `KeyManagementService` where it already logically belongs.

## Decision 2: AES-256-GCM Encryption Location

**Decision**: Move AES-256-GCM encryption logic from `EncryptionProviderBase` into `KeyManagementService`. The new `IKeyProtectionProvider` only wraps/unwraps DEKs.

**Rationale**: Currently `EncryptionProviderBase.EncryptAsync()` does three things: (1) fetch/create DEK, (2) AES-256-GCM encrypt with DEK, (3) base64 encode. For the new architecture, only step 1 (DEK lifecycle) varies by provider. Steps 2-3 are identical regardless of whether the DEK is DPAPI-wrapped or Azure Key Vault-wrapped. Moving them to `KeyManagementService` eliminates the duplication between `EncryptionProviderBase` and `LocalEncryptionProvider` (which re-implements AES-256-GCM inline).

**Alternatives considered**:
- Keep AES-GCM in a shared base class for providers — rejected because `IKeyProtectionProvider` implementations become simpler (just wrap/unwrap bytes) and more testable.

## Decision 3: Azure Key Vault SDK Integration

**Decision**: Use `Azure.Security.KeyVault.Keys` (already in Wallet.Core.csproj) with `CryptographyClient` for wrap/unwrap and `KeyClient` for key creation. Authenticate via `Azure.Identity.DefaultAzureCredential` with `ManagedIdentityCredential` preference.

**Rationale**: The Azure SDK packages are already referenced. The `AzureKeyVaultOptions` config model is already defined in `EncryptionProviderOptions.cs` (lines 167-232) with `VaultUri`, `UseManagedIdentity`, `ManagedIdentityClientId`, `DekCacheTtlMinutes`, and `AllowStaleDeksOnOutage`. This config can be reused directly.

**Key Vault operations mapping**:
- `IKeyProtectionProvider.WrapKeyAsync` → `CryptographyClient.WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, dek)`
- `IKeyProtectionProvider.UnwrapKeyAsync` → `CryptographyClient.UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrappedDek)`
- `ISigningProvider.SignAsync` → `CryptographyClient.SignAsync(SignatureAlgorithm.ES256, hash)`
- `ISigningProvider.CreateSigningKeyAsync` → `KeyClient.CreateEcKeyAsync(new CreateEcKeyOptions(name, KeyCurveName.P256))`

## Decision 4: DEK Cache Retention

**Decision**: Keep the existing `ConcurrentDictionary` + TTL cache pattern from `EncryptionProviderBase`, but move it into `KeyManagementService`. Add configurable grace period (default 15 minutes) for stale DEKs during KMS outages.

**Rationale**: The cache is critical for performance — without it, every sign operation requires a KMS round-trip (~100-500ms). The existing 30-minute TTL is reasonable. The grace period handles transient KMS outages without failing immediately. The `AzureKeyVaultOptions` already has `AllowStaleDeksOnOutage` and `DekCacheTtlMinutes` fields.

**Alternatives considered**:
- Per-provider cache (current pattern) — rejected because the cache logic is identical for all providers. Centralising it in `KeyManagementService` reduces duplication.

## Decision 5: Provider Registration Pattern

**Decision**: Extend the existing factory switch in `WalletServiceExtensions.AddEncryptionProvider()` (line 219) to register `IKeyProtectionProvider` and optionally `ISigningProvider` based on `EncryptionProvider:Type` configuration.

**Rationale**: The existing pattern uses a singleton factory lambda that switches on `options.Type.ToLowerInvariant()`. Add `"azurekeyvault"` case that creates both `AzureKeyProtectionProvider` and `AzureSigningProvider`. The Azure provider project is referenced only by `Sorcha.Wallet.Service` (not Wallet.Core), keeping the core cloud-agnostic.

**Configuration key**: `EncryptionProvider:Type = "AzureKeyVault"` activates the Azure provider. Existing values (`"Local"`, `"WindowsDpapi"`, `"LinuxSecretService"`) continue to work — they register `IKeyProtectionProvider` only (refactored from existing providers), with no `ISigningProvider`.

## Decision 6: Wallet Entity Changes

**Decision**: Add `SigningMode` enum column (default `Local`) and nullable `KmsKeyId` string column to the `Wallet` entity. EF Core migration with default value for existing rows.

**Rationale**: Minimal schema change. Existing wallets get `SigningMode = Local` and `KmsKeyId = null` via migration default. No data transformation needed. The `EncryptedPrivateKey` and `EncryptionKeyId` columns remain for Local wallets. For KmsResident wallets, `EncryptedPrivateKey` is null and `KmsKeyId` holds the cloud key reference.

## Decision 7: TransactionService Signing Path

**Decision**: `TransactionService` does NOT need modification. The signing path change is in `KeyManagementService` which already mediates between `WalletManager` and the encryption provider. Add a `SignWithKmsAsync` method to `KeyManagementService` that delegates to `ISigningProvider`.

**Rationale**: `TransactionService` uses `ICryptoModule` for signing (public-key crypto for payload encryption), not `IEncryptionProvider`. The wallet signing flow goes: `WalletManager.SignAsync` → `KeyManagementService.DecryptPrivateKeyAsync` → local sign. For KMS-resident wallets: `WalletManager.SignAsync` → `KeyManagementService.SignWithKmsAsync` → `ISigningProvider.SignAsync`. The branch point is in `KeyManagementService`.

## Decision 8: Testing Strategy

**Decision**: Unit tests with mocked `IKeyProtectionProvider`/`ISigningProvider` for `KeyManagementService` and `WalletManager`. Azure provider tested with `Azure.Security.KeyVault.Keys` testable abstractions (the SDK methods are virtual/mockable). No Azure emulator required for unit tests.

**Rationale**: The Azure SDK's `CryptographyClient` and `KeyClient` methods are mockable via Moq. Integration tests against a real Key Vault (or Azurite where supported) are a separate concern for CI/CD pipeline configuration — out of scope for initial implementation.
