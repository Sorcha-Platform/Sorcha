# Cloud KMS Key Management

**Date:** 2026-04-04
**Status:** Approved
**Scope:** Multi-cloud KMS integration for wallet key protection and KMS-resident signing
**Replaces:** SEC-002 (Azure-only scope expanded to multi-cloud)

---

## Problem

Wallet private keys are encrypted at rest using AES-256-GCM with DEKs (Data Encryption Keys) protected by platform-specific providers (DPAPI, Linux Secret Service, macOS Keychain). In Docker, DEKs are file-backed with PBKDF2 fallback from `/etc/machine-id`. This is adequate for development but insufficient for production:

- No hardware-backed key protection
- No audit trail for key access
- No support for compliance requirements (FIPS 140-2, SOC 2)
- No option for keys that never leave a hardware security module

The platform needs cloud KMS integration that is provider-agnostic, supports both envelope encryption (majority of wallets) and KMS-resident signing (high-security wallets), and maintains the current performance characteristics.

## Architecture

### Two Interfaces

The existing `IEncryptionProvider` is replaced by two focused interfaces:

**`IKeyProtectionProvider`** — Envelope encryption for DEK lifecycle.

```csharp
public interface IKeyProtectionProvider
{
    Task<string> CreateKeyAsync(string keyId, CancellationToken ct = default);
    Task<byte[]> WrapKeyAsync(string keyId, byte[] plaintext, CancellationToken ct = default);
    Task<byte[]> UnwrapKeyAsync(string keyId, byte[] ciphertext, CancellationToken ct = default);
    Task<bool> KeyExistsAsync(string keyId, CancellationToken ct = default);
}
```

Used by every wallet. Wraps/unwraps DEKs. The DEK is cached in-memory (30-minute TTL with configurable grace period during outages). AES-256-GCM encryption of private keys remains local — only the DEK touches the KMS.

**`ISigningProvider`** — KMS-resident key creation and signing.

```csharp
public interface ISigningProvider
{
    Task<KmsKeyInfo> CreateSigningKeyAsync(string keyId, string algorithm, CancellationToken ct = default);
    Task<byte[]> SignAsync(string kmsKeyId, byte[] data, CancellationToken ct = default);
    Task<bool> VerifyAsync(string kmsKeyId, byte[] data, byte[] signature, CancellationToken ct = default);
    Task<byte[]> GetPublicKeyAsync(string kmsKeyId, CancellationToken ct = default);
}
```

Used only by wallets with `SigningMode = KmsResident`. Key material never leaves the KMS. P-256 (ECDSA) only for initial implementation — the only algorithm universally supported across Azure, AWS, and GCP KMS.

### Wallet Entity Changes

```csharp
public enum SigningMode
{
    Local,       // Private key stored encrypted locally (envelope encryption)
    KmsResident  // Private key lives in cloud KMS, never extracted
}
```

New fields on `Wallet` entity:
- `SigningMode` — `Local` (default) or `KmsResident`
- `KmsKeyId` — Cloud provider's key reference (null for Local wallets)

### Signing Mode Policy

Default assignment based on wallet purpose, with API override:

**Defaults (production):**
- System derivation paths `m/44'/0'/0'/0/100-103` (attestation, control record, docket, blueprint) → `KmsResident`
- All other wallets → `Local`

**Defaults (development/Docker):**
- All wallets → `Local` (no KMS available)

**API override:** `POST /api/v1/wallets` accepts optional `signingMode` parameter. Callers can force `KmsResident` for any wallet or `Local` to override the system default.

Policy is configured in `appsettings.json`:
```json
{
  "WalletKeyManagement": {
    "DefaultSigningMode": "Local",
    "KmsResidentPaths": ["m/44'/0'/0'/0/100", "m/44'/0'/0'/0/101", "m/44'/0'/0'/0/102", "m/44'/0'/0'/0/103"],
    "AllowSigningModeOverride": true
  }
}
```

## Provider Packages

Each cloud gets a separate project implementing both interfaces:

| Package | `IKeyProtectionProvider` | `ISigningProvider` | Notes |
|---------|------------------------|--------------------|-------|
| `Sorcha.Wallet.Providers.Local` | DPAPI / Secret Service / Keychain | Not supported | Refactored from existing `EncryptionProviderBase` |
| `Sorcha.Wallet.Providers.Azure` | Key Vault wrap/unwrap | Key Vault sign/verify (P-256) | Initial cloud implementation |
| `Sorcha.Wallet.Providers.Aws` | AWS KMS encrypt/decrypt | AWS KMS sign/verify (P-256) | Future |
| `Sorcha.Wallet.Providers.Gcp` | GCP Cloud KMS encrypt/decrypt | GCP Cloud KMS sign/verify (P-256) | Future |

One cloud provider active per deployment, configured via:
```json
{
  "WalletKeyManagement": {
    "Provider": "Azure",
    "Azure": {
      "VaultUri": "https://sorcha-keyvault.vault.azure.net/",
      "TenantId": "...",
      "ManagedIdentity": true
    }
  }
}
```

The Local provider is always available and is the default for Docker/development. It implements `IKeyProtectionProvider` only. Attempting to create a `KmsResident` wallet with the Local provider returns an error.

## Flows

### Wallet Creation

```
CreateWallet(derivationPath, algorithm, signingMode?)
  |
  +-- Resolve signingMode:
  |     1. Explicit API parameter (if AllowSigningModeOverride)
  |     2. Policy match on derivation path (KmsResidentPaths)
  |     3. DefaultSigningMode from config
  |
  +-- if KmsResident:
  |     +-- Validate algorithm is P-256 (only supported KMS algorithm)
  |     +-- ISigningProvider.CreateSigningKeyAsync(keyId, "P-256") -> kmsKeyId
  |     +-- ISigningProvider.GetPublicKeyAsync(kmsKeyId) -> publicKey
  |     +-- Store: kmsKeyId, publicKey, signingMode=KmsResident
  |     +-- No local private key, no DEK, no encrypted blob
  |
  +-- if Local:
        +-- Derive private key from seed via HD path (existing BIP32/44)
        +-- Generate or fetch DEK
        +-- IKeyProtectionProvider.WrapKeyAsync(dekId, rawDek) (on DEK creation)
        +-- AES-256-GCM encrypt private key with DEK (existing)
        +-- Store: encryptedPrivateKey, encryptionKeyId, signingMode=Local
```

### Transaction Signing

```
SignAsync(walletId, data)
  |
  +-- Load wallet entity
  |
  +-- if wallet.SigningMode == KmsResident:
  |     +-- ISigningProvider.SignAsync(wallet.KmsKeyId, hash(data)) -> signature
  |     +-- Latency: ~100-500ms (network + HSM)
  |     +-- No local key material at any point
  |
  +-- if wallet.SigningMode == Local:
        +-- Unwrap DEK (cache hit: ~10us, cache miss: KMS call ~100-500ms)
        +-- AES-256-GCM decrypt private key with DEK
        +-- Sign locally via CryptoModule (ED25519/P-256/RSA)
        +-- Zeroize decrypted key from memory
        +-- Latency: ~100us on cache hit
```

### DEK Unwrap (Envelope Encryption Detail)

```
UnwrapDek(encryptionKeyId)
  |
  +-- Check in-memory cache
  |     +-- Hit + not expired: return cached DEK (~10us)
  |     +-- Hit + expired but within grace period + KMS down: return stale DEK (log warning)
  |
  +-- Cache miss or expired:
        +-- IKeyProtectionProvider.UnwrapKeyAsync(keyId, encryptedDek)
        +-- Cache DEK with TTL (default 30 minutes)
        +-- Return DEK
```

## Resilience

**Envelope encryption (Local signing mode):**
- DEK cache with 30-minute TTL (existing)
- Configurable grace period: if KMS is unreachable when cache expires, extend TTL for N minutes and log warning
- Grace period default: 15 minutes
- After grace period exhausted: fail closed

**KMS-resident signing:**
- Fails closed immediately. No local fallback — the private key does not exist locally. This is the security guarantee.
- Cloud KMS SLAs apply (Azure: 99.99%, AWS: 99.999%, GCP: 99.99%)

**Multi-region KMS:**
- Deferred. Can be added as a provider configuration option (primary + secondary vault URI) without interface changes.

## Performance and Cost

### Performance

| Operation | Local mode | KMS-resident mode |
|-----------|-----------|-------------------|
| Sign (cache hit) | ~100us (local crypto) | ~100-500ms (KMS round-trip) |
| Sign (cache miss) | ~100-500ms (unwrap DEK) + ~100us (sign) | ~100-500ms (same) |
| Create wallet | ~50ms (derive + encrypt) | ~200-1000ms (KMS key creation) |
| Key rotation | ~100-500ms (unwrap old + wrap new) | N/A (cloud-managed) |

### Cost (per month, estimated)

| Scenario | Azure Key Vault | AWS KMS | GCP Cloud KMS |
|----------|----------------|---------|---------------|
| 100 DEKs, 10K unwrap ops | ~$3 | ~$103 (key cost) | ~$6 |
| 10 KMS-resident keys, 50K sign ops | ~$15 (Premium) | ~$160 | ~$15 |
| 1000 DEKs, 100K unwrap ops | ~$30 | ~$1030 | ~$60 |

Note: AWS KMS charges $1/key/month which dominates at scale. Azure and GCP charge per-operation. DEK caching dramatically reduces operation counts.

## Migration

- Existing wallets: `SigningMode = Local`, no changes. Add column with default.
- No data migration needed. Existing encrypted private keys continue working.
- New KMS-resident wallets created going forward.
- Optional future: batch re-key command to migrate DEKs from Local provider to cloud KMS. Out of scope for initial implementation.

## Project Structure

```
src/Core/
  Sorcha.Wallet.Core/
    Encryption/
      Interfaces/
        IKeyProtectionProvider.cs    # NEW — replaces IEncryptionProvider
        ISigningProvider.cs          # NEW — KMS-resident signing
        KmsKeyInfo.cs                # NEW — key creation response model
      Providers/
        LocalKeyProtectionProvider.cs  # REFACTORED from EncryptionProviderBase
      Configuration/
        WalletKeyManagementOptions.cs  # NEW — unified config model
        SigningModePolicy.cs           # NEW — resolves signing mode per wallet
    Domain/
      Entities/
        Wallet.cs                      # MODIFIED — add SigningMode, KmsKeyId
      Enums/
        SigningMode.cs                 # NEW
    Services/
      Implementation/
        WalletManager.cs               # MODIFIED — branch on SigningMode
        KeyManagementService.cs        # MODIFIED — use IKeyProtectionProvider
        TransactionService.cs          # MODIFIED — branch signing path

src/Providers/
  Sorcha.Wallet.Providers.Azure/       # NEW PROJECT
    AzureKeyProtectionProvider.cs
    AzureSigningProvider.cs
    AzureKmsOptions.cs
    Extensions/
      ServiceCollectionExtensions.cs
```

## Initial Scope

1. Define `IKeyProtectionProvider` and `ISigningProvider` interfaces
2. Refactor existing `EncryptionProviderBase` → `LocalKeyProtectionProvider` implementing `IKeyProtectionProvider`
3. Add `SigningMode` and `KmsKeyId` to Wallet entity + EF migration
4. Add `SigningModePolicy` for default resolution
5. Implement `Sorcha.Wallet.Providers.Azure` (both interfaces)
6. Modify `WalletManager` and `TransactionService` to branch on signing mode
7. Update wallet creation API to accept optional `signingMode`
8. Tests: unit tests for policy resolution, integration tests with Azure Key Vault emulator

**Deferred:**
- AWS and GCP provider packages
- Batch re-key command
- Multi-region KMS failover
- Additional KMS-resident algorithms beyond P-256
