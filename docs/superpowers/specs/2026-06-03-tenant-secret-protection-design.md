# Tenant Service At-Rest Secret Protection — Design

**Date:** 2026-06-03
**Status:** Design approved (brainstorming) — ready for speckit specify → plan → tasks → implement
**Author:** Stuart Fraser + Claude
**Origin:** Security finding C1 (CRITICAL) + two siblings, from `docs/reviews/2026-06-02-architecture-review.md`

---

## 1. Problem

Three secret/key-management defects cluster in `Sorcha.Tenant.Service`, all with the same root cause — a hand-rolled `EncryptSecret`/`DecryptSecret` that was never finished — and all three carry a code comment literally saying *"in production, use AES-256-GCM with Azure Key Vault."*

| # | Defect | Location | Severity |
|---|--------|----------|----------|
| C1 | **TOTP secrets stored as reversible Base64**, not encrypted. The XML doc claims "AES-256-GCM" but the body returns `v1:{Convert.ToBase64String(...)}`. Anyone with Tenant DB read access recovers every user's TOTP seed and can mint valid 2FA codes. | `Services/TotpService.cs:362-387` (`EncryptSecret`/`DecryptSecret`) | CRITICAL |
| 2 | **OIDC client secrets stored as a one-way SHA-256 hash.** `EncryptSecret` returns `SHA256.HashData(...)`; `DecryptSecret` returns the hex of that hash — so the *real* client secret is unrecoverable. This is both insecure and **functionally broken**: `OidcExchangeService` (`:127`) gets hex-of-hash, not the real secret, when authenticating to the IdP token endpoint. | `Services/IdpConfigurationService.cs:248-264` | HIGH (security + correctness) |
| 3 | **Login-token HMAC key is random per process.** `LoginTokenSigningKey = GenerateStableKey()` calls `RandomNumberGenerator.Fill` (despite the "stable" comment), so the 5-minute 2FA intermediate tokens won't validate across replicas or after a restart. | `Services/TotpService.cs:44, 403-408` | MEDIUM (multi-replica correctness) |

There are currently **three divergent, broken** `EncryptSecret`/`DecryptSecret`/key implementations. Fixing only TOTP would clone the anti-pattern and leave a functionally-broken OIDC path next door.

## 2. Goals / Non-goals

**Goals**
- Replace all three with **one** AES-256-GCM secret-protection seam, shaped to converge with the Wallet protector later.
- TOTP secrets and OIDC client secrets stored as **reversible AEAD** ciphertext with a server-held key.
- Login-token HMAC key derived from managed key material → **stable across replicas/restarts**.
- **Fail-closed** in Production/Staging if no key resolves.
- No new mandatory config: the key derives from the existing JWT signing key by default.

**Non-goals (YAGNI)**
- No KMS/HSM implementation now — only the seam, so a KMS impl drops in later.
- No Wallet/Tenant convergence now — only a documented note (see §11).
- No data migration — pre-release clean break via DB clear (see §8).
- M3's OIDC *issuer-validation* concern (`OidcExchangeService.cs:295`, "trusts the token came from the configured IDP") is a **separate** finding/sub-project; this design fixes the client-secret-storage half only.
- No change to backup-code hashing (SHA-256 of high-entropy codes is acceptable) — out of scope.

## 3. Key decisions (from brainstorming)

| Decision | Choice |
|----------|--------|
| Scope | One shared Tenant primitive covering **TOTP secrets + OIDC client secrets**, plus the **login-token HMAC key** sourced from the same root. |
| Key source | **HKDF-SHA256 from the existing JWT signing key** by default (no new config); optional explicit `Tenant:SecretProtection:Key` override; fail-closed in Production. |
| Algorithm | **AES-256-GCM** (BCL `AesGcm`), identical body to Wallet's `SoftwareKeyProtectionProvider`. |
| Migration | **None** — pre-release clean break; DB cleared on rollout; column changes **squashed into the initial migration**. |
| Convergence | Mirror Wallet's `IOrgKeyProtectionProvider` shape; merge during the Hardware Key Storage initiative. |
| KMS | Out of scope now; the seam allows it later. |

## 4. Architecture

```
                         ┌─────────────────────────────────────┐
                         │  ISecretProtectionProvider (seam)    │
                         │  EncryptAsync(byte[]) → (ct, keyId)  │
                         │  DecryptAsync(byte[], keyId) → byte[]│
                         │  ProviderName                        │
                         └──────────────┬──────────────────────┘
                                        │ one impl now
                         ┌──────────────▼──────────────────────┐
                         │  SoftwareSecretProtectionProvider     │
                         │  AES-256-GCM (12-byte nonce, 16 tag)  │
                         │  envelope = nonce ∥ ciphertext ∥ tag  │
                         │  key + keyId injected (resolved by    │
                         │  TenantSecretKeyResolver)             │
                         └──────────────┬──────────────────────┘
        ┌───────────────────────────────┼───────────────────────────────┐
        ▼                                ▼                               ▼
  TotpService                    IdpConfigurationService          Login-token signer
  (TOTP secret at rest)          (OIDC client secret at rest)     (HMAC key, derived
                                                                   from same root via
                                                                   HKDF — NOT via the
                                                                   AEAD provider)
```

The seam is **deliberately a near-clone of Wallet's `IOrgKeyProtectionProvider` / `SoftwareKeyProtectionProvider`** (`Sorcha.Wallet.Core.Services.Interfaces` / `Sorcha.Wallet.Service.Services.Implementation`). The only intentional divergence is *key resolution*; the crypto body is identical.

## 5. Component contracts

### 5.1 `ISecretProtectionProvider` (new, Tenant Service)
```csharp
// Mirrors Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider.
// CONVERGENCE NOTE (see §11): when Hardware Key Storage lands, lift this and the
// Wallet provider onto a shared Sorcha.* provider in Common and add the KMS/HSM impl
// behind this same seam. Keep the AES-GCM body identical until then.
public interface ISecretProtectionProvider
{
    Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(byte[] plaintext, CancellationToken ct = default);
    Task<byte[]> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct = default);
    string ProviderName { get; }   // "Software"
}
```

### 5.2 `SoftwareSecretProtectionProvider` (new, Tenant Service)
- Constants identical to Wallet: `NonceSize = 12`, `TagSize = 16`, `RequiredKeyLength = 32`.
- Body identical to `SoftwareKeyProtectionProvider.EncryptSeedAsync`/`DecryptSeedAsync`: random 12-byte nonce, `AesGcm(key, TagSize)`, envelope `nonce ∥ ciphertext ∥ tag`; decrypt splits and verifies the tag.
- `ProviderName => "Software"`.
- Takes the resolved 32-byte key + `KeyId` via constructor (resolved by §5.3). **No AAD** in v1 — keep the body byte-identical to Wallet (AAD/context-binding is noted as a future shared-provider enhancement, §11).

### 5.3 `TenantSecretKeyResolver` (new, Tenant Service — the one deliberate divergence)
Resolves `(byte[] Key, string KeyId)` once at startup, in priority order:
1. **Explicit override:** `Tenant:SecretProtection:Key` set → `Convert.FromBase64String`, must be 32 bytes → `KeyId = "config-v1"`.
2. **Default — derive from JWT signing key:** `HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm: utf8(JwtConfiguration.SigningKey), outputLength: 32, salt: null, info: utf8("sorcha:tenant:secret-protection:v1"))` → `KeyId = "jwt-derived-v1"`.
3. **Fail-closed:** in Production/Staging, if neither resolves (no override and no JWT signing key) → throw `InvalidOperationException` at startup (host won't start). Mirrors `SorchaIssuer` (F136) and Wallet's throw-on-missing-key. Non-prod may derive from the dev JWT key.

`System.Security.Cryptography.HKDF` (BCL) is used directly. The `KeyId` is persisted with each ciphertext and is the rotation/version tag + future provider discriminator (same role as Wallet's `EncryptionKeyId`).

### 5.4 Login-token HMAC key (TotpService)
A separate derived key, **not** via the AEAD provider (it's a signing key):
`HKDF-SHA256(ikm: utf8(JwtConfiguration.SigningKey), outputLength: 32, info: utf8("sorcha:tenant:login-token-hmac:v1"))`, computed once at startup and injected. Replaces `static readonly LoginTokenSigningKey = GenerateStableKey()`; `GenerateStableKey` is deleted.

## 6. Consumer changes (delete all three old impls)

- **`TotpService`** (`Services/TotpService.cs`)
  - Inject `ISecretProtectionProvider` + the derived login-token HMAC key.
  - `SetupAsync`: `(ct, keyId) = await _protector.EncryptAsync(Encoding.UTF8.GetBytes(base32Secret))`; store `EncryptedSecret = ct`, `EncryptionKeyId = keyId`.
  - `VerifyAndEnableAsync` / `ValidateCodeAsync`: `base32Secret = Encoding.UTF8.GetString(await _protector.DecryptAsync(config.EncryptedSecret, config.EncryptionKeyId))`.
  - Delete `EncryptSecret`/`DecryptSecret` (the `v1:` Base64 pair) and `GenerateStableKey`. `LoginTokenSigningKey` becomes the injected derived key.
- **`IdpConfigurationService`** (`Services/IdpConfigurationService.cs`)
  - Inject `ISecretProtectionProvider`.
  - On create/update: `(ct, keyId) = await _protector.EncryptAsync(Encoding.UTF8.GetBytes(request.ClientSecret))`; store `ClientSecretEncrypted = ct`, `ClientSecretKeyId = keyId`.
  - On read (e.g., for `OidcExchangeService`): `Encoding.UTF8.GetString(await _protector.DecryptAsync(config.ClientSecretEncrypted, config.ClientSecretKeyId))` → the **real** secret (fixes the broken exchange).
  - Delete the SHA-256 `EncryptSecret`/`DecryptSecret`. Make these methods async or thread the protector through; update callers (`OidcExchangeService.cs:127`).
- **`DatabaseInitializer`** (`Data/DatabaseInitializer.cs:479`) — the IdP seed currently calls the old `EncryptSecret`. Route it through `ISecretProtectionProvider` (or drop the seeded secret if it's only placeholder data).

## 7. DI wiring (Tenant `Program.cs` / ServiceCollectionExtensions)
- `TenantSecretKeyResolver` resolves the key once; register `ISecretProtectionProvider` → `SoftwareSecretProtectionProvider` as a **singleton** (holds the derived AES key in memory).
- Register the derived login-token HMAC key as a small singleton holder injected into `TotpService` (scoped).
- Key resolution + fail-closed check runs at startup (constructor of the resolver or a hosted startup check), so a misconfigured Production host fails fast.

## 8. Data model & migration (squash into initial migration — pre-release)

**No new migration.** Because the platform is pre-release and the DB is cleared on rollout, fold the column changes directly into the existing initial migration and snapshot:

- `Migrations/20260513152714_InitialCreate.cs`
- `Migrations/20260513152714_InitialCreate.Designer.cs`
- `Migrations/TenantDbContextModelSnapshot.cs`

Column changes:
- **`TotpConfiguration`** (`Models/TotpConfiguration.cs` + `Data/TenantDbContext.cs` config): `EncryptedSecret` `string → byte[]` (`bytea`); **add** `EncryptionKeyId` (`string`, e.g. `varchar(64)`, non-null).
- **`IdentityProviderConfiguration`** (`Models/IdentityProviderConfiguration.cs` + `TenantDbContext.cs` configs at **both** `:358` and `:466` — the entity is mapped in two places): `ClientSecretEncrypted` stays `byte[]`; **add** `ClientSecretKeyId` (`string`, non-null).

This mirrors Wallet's `EncryptedPrivateKey` (`byte[]`) + `EncryptionKeyId` (`string`) column pairing. Update the entity property types, the Fluent config in `TenantDbContext`, and the three migration/snapshot files so they remain consistent (EF will otherwise flag model drift at startup).

> Implementation note for the speckit tasks: do **not** run `dotnet ef migrations add`. Edit `InitialCreate` + the snapshot by hand (or regenerate the single initial migration from a clean model), keeping the migration id `20260513152714_InitialCreate`.

## 9. Failure modes
- Tampered/garbage ciphertext or wrong key → `AuthenticationTagMismatchException` from `AesGcm.Decrypt`. Callers catch and surface as **auth failure** (TOTP: return invalid) / **config error** (OIDC: surfaced to admin), never an unhandled 500.
- Unknown/missing `KeyId`, or ciphertext shorter than `nonce+tag` → fail closed (throw/return invalid as appropriate).
- No resolvable key at startup in Production/Staging → host fails to start (loud), per §5.3.

## 10. Security analysis
- **Primary threat:** Tenant DB read/dump exposing secrets at rest. Post-fix, TOTP secrets and OIDC client secrets are AES-256-GCM ciphertext; the key is never in the DB (derived from the JWT signing key held only in app config/secret store). A DB-dump attacker has neither the JWT key nor a derived key → cannot recover secrets.
- **Key separation:** HKDF with distinct `info` labels yields cryptographically independent keys for (a) at-rest AEAD and (b) login-token HMAC, both distinct from the JWT signing use. Standard HKDF domain separation.
- **Rotation:** the stored `KeyId` lets a future rotation re-encrypt under a new key/version; an operator who wants the at-rest key to rotate independently of the JWT key sets `Tenant:SecretProtection:Key`.
- **Residual / accepted:** the JWT signing key is shared across services (per the F136 review). Deriving the at-rest key from it means any holder of the JWT key could derive it — acceptable because (a) the DB-dump attacker holds neither, and (b) the explicit-override path exists for environments that want stronger separation. AAD/context-binding (e.g., binding a TOTP ciphertext to its `userId`) is deferred to the shared provider (§11).

## 11. Convergence note (Hardware Key Storage)
This seam is an intentional mirror of Wallet's `IOrgKeyProtectionProvider` / `SoftwareKeyProtectionProvider`. The planned convergence, to be done **during the Hardware Key Storage initiative**:
1. Promote a shared `ISecretProtectionProvider` (+ the AES-GCM software impl) into a Common project.
2. Converge both Wallet (`IOrgKeyProtectionProvider`) and Tenant onto it; key *resolution* stays per-service composition (config / HKDF / KMS).
3. Add the KMS/HSM-resident impl (envelope encryption, reuse Feature 082) behind the same seam.
4. Consider adding AAD/context-binding at that point.

This note MUST appear as an XML-doc on `ISecretProtectionProvider` and in this design doc so the future refactor is a lift, not a redesign.

## 12. Testing strategy
- **Provider unit tests:** encrypt→decrypt round-trip; tamper (flip a ciphertext byte) → throws; wrong key → throws; ciphertext-too-short → throws; envelope layout matches Wallet's (`nonce ∥ ct ∥ tag`).
- **Key resolver:** derivation determinism (same JWT key ⇒ same derived key ⇒ ciphertext from "replica A" decrypts on "replica B"); override precedence; non-32-byte override rejected; **Production fail-closed** when no key resolves.
- **TotpService:** setup→validate round-trip via the real provider; stored `EncryptedSecret` is neither plaintext nor Base64-decodable to the secret; `EncryptionKeyId` persisted.
- **IdpConfigurationService:** store→recover the *real* client secret (regression guard for the SHA-256 bug); `OidcExchangeService` receives the real secret.
- **Login-token:** token minted with the derived key validates after a simulated "restart" (re-derive) and on a second instance (same root ⇒ same key).
- **Clean-break grep guard:** no surviving `v1:` Base64 secret path and no `SHA256.HashData`-based `EncryptSecret` remain in `Sorcha.Tenant.Service`.

## 13. File-change inventory
**New**
- `Services/Interfaces/ISecretProtectionProvider.cs`
- `Services/Implementation/SoftwareSecretProtectionProvider.cs`
- `Services/Implementation/TenantSecretKeyResolver.cs` (or equivalent DI factory)
- Tests under `tests/Sorcha.Tenant.Service.Tests/Services/`

**Modified**
- `Services/TotpService.cs` (use provider + derived HMAC key; delete `EncryptSecret`/`DecryptSecret`/`GenerateStableKey`)
- `Services/IdpConfigurationService.cs` (use provider; delete SHA-256 pair)
- `Services/OidcExchangeService.cs` (consume real decrypted secret; `:127`)
- `Data/DatabaseInitializer.cs` (`:479` seed via provider)
- `Models/TotpConfiguration.cs` (`EncryptedSecret` → `byte[]`; add `EncryptionKeyId`)
- `Models/IdentityProviderConfiguration.cs` (add `ClientSecretKeyId`)
- `Data/TenantDbContext.cs` (column config at the TOTP entity + IdP entity `:358`/`:466`)
- `Migrations/20260513152714_InitialCreate.cs` + `.Designer.cs` + `TenantDbContextModelSnapshot.cs` (squash columns; no new migration)
- `Program.cs` / DI extensions (register provider + resolver + login-token key; startup fail-closed)
- Config: document optional `Tenant:SecretProtection:Key` (base64-32) in appsettings + AUTHENTICATION-SETUP / Tenant README.

## 14. Rollout
Pre-release: deploy, **clear the Tenant DB** (the squashed initial migration creates the new shape). Set `JwtSettings:SigningKey` (already required) — the protection key derives from it. Optionally set `Tenant:SecretProtection:Key` for independent rotation. No backfill, no re-entry of old secrets (none survive the clear).
