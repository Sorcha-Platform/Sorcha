# Research: Tenant Service At-Rest Secret Protection

All decisions below were settled during brainstorming and are captured authoritatively in
`docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md`. There are **no open
`NEEDS CLARIFICATION` items**. This file records each decision in Decision / Rationale /
Alternatives form for the planning record.

## D1 — One shared protection seam for all three secrets

- **Decision**: A single Tenant-local `ISecretProtectionProvider` covers TOTP secrets and OIDC client secrets; the 2FA login-token HMAC key is sourced from the same root key (but signed directly, not via the AEAD provider).
- **Rationale**: All three defects share one root cause and three divergent broken `EncryptSecret`/`DecryptSecret`/key implementations. Fixing only TOTP would clone the anti-pattern and leave the functionally-broken OIDC path. One seam = one obvious path.
- **Alternatives**: (a) Fix TOTP only — rejected: leaves known siblings broken. (b) Promote a shared Common primitive reused by Wallet + Tenant now — rejected as scope creep; deferred to the Hardware Key Storage initiative (D6).

## D2 — Key source: derive from the JWT signing key by default

- **Decision**: Default protection key = `HKDF-SHA256(ikm = JwtConfiguration.SigningKey, info = "sorcha:tenant:secret-protection:v1", L = 32)`, `KeyId = "jwt-derived-v1"`. Optional override `Tenant:SecretProtection:Key` (base64-32) takes precedence, `KeyId = "config-v1"`. Fail-closed in Production/Staging if neither resolves.
- **Rationale**: The JWT signing key is already provisioned and (in hardened envs) required and fail-closed — so no new mandatory config is introduced. HKDF with a distinct `info` gives proper domain separation from the signing use.
- **Alternatives**: (a) Dedicated required key — rejected as "new config" the user wanted to avoid (kept as the optional override). (b) ASP.NET Data Protection — rejected: needs a shared, protected key-ring store not currently set up. (c) KMS-resident now — deferred (D6); adds a Key Vault dependency + hot-path round-trip.

## D3 — Algorithm + envelope: mirror Wallet exactly

- **Decision**: AES-256-GCM via BCL `AesGcm`; 12-byte nonce, 16-byte tag; envelope = `nonce ∥ ciphertext ∥ tag`; `ProviderName = "Software"`. Byte-identical to `Sorcha.Wallet.Service…SoftwareKeyProtectionProvider`.
- **Rationale**: Matches Constitution Principle II ("AES-256-GCM"), matches the existing Wallet precedent, and makes the future convergence (D6) a lift, not a redesign. No AAD in v1 (keeps the body identical; AAD/context-binding deferred to the shared provider).
- **Alternatives**: XChaCha20-Poly1305 (used elsewhere via `ISymmetricCrypto`) — rejected to stay byte-aligned with Wallet and the constitution's stated AES-256-GCM.

## D4 — KeyId stored alongside ciphertext (rotation tag)

- **Decision**: Persist a `KeyId` string with each ciphertext (`TotpConfiguration.EncryptionKeyId`, `IdentityProviderConfiguration.ClientSecretKeyId`).
- **Rationale**: Exactly how Wallet tracks `EncryptionKeyId`; lets a future key change be reconciled and distinguishes software vs (future) KMS keys. No bespoke self-describing envelope needed.
- **Alternatives**: Encode version into the ciphertext blob — rejected; the column approach matches Wallet and is simpler.

## D5 — Migration: pre-release clean break, squash into initial migration

- **Decision**: No data migration, no legacy-format decode. Column changes (`TotpConfiguration.EncryptedSecret` `string→byte[]` + `EncryptionKeyId`; add `IdentityProviderConfiguration.ClientSecretKeyId`) are folded into `Migrations/20260513152714_InitialCreate.cs` + `.Designer.cs` + `TenantDbContextModelSnapshot.cs`. DB is cleared on rollout.
- **Rationale**: Pre-release; the old TOTP secrets were plaintext-equivalent (not worth preserving) and the OIDC secrets are unrecoverable anyway. A clean break avoids carrying decode paths for broken formats. Per the user's explicit instruction.
- **Alternatives**: Eager/lazy re-encrypt, or force re-enrolment — all rejected for a pre-release DB-clear rollout.

## D6 — Convergence note (Hardware Key Storage)

- **Decision**: The seam is an intentional mirror of Wallet's `IOrgKeyProtectionProvider`. A convergence note (XML-doc on the interface + the design doc) records: during the Hardware Key Storage initiative, promote a shared `ISecretProtectionProvider` to Common, converge Wallet + Tenant, and add the KMS/HSM impl behind the same seam.
- **Rationale**: The user will refactor later (likely alongside hardware key storage); keeping the contract shape-compatible now makes that a lift.
- **Alternatives**: Converge now — rejected as out-of-scope.

## Resolved non-issues
- **Performance**: in-memory AES-GCM with no network round-trip; key derived once at startup → negligible latency on login/TOTP/OIDC paths. No benchmark task required.
- **OIDC issuer-validation** (`OidcExchangeService` trusting the IdP token without issuer validation) is a **separate** security finding (review M3), explicitly out of scope here.
- **Backup-code hashing**: unchanged (SHA-256 of high-entropy codes is acceptable).
