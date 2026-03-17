# Research: Wallet Recovery

**Date**: 2026-03-17 | **Feature**: 060-wallet-recovery

## Decision 1: Recovery Key Generation & Storage

**Decision**: Generate AES-256-GCM recovery key at wallet creation; store encrypted master key blob and recovery key wraps on the Wallet entity in PostgreSQL
**Rationale**: Wallet Service PostgreSQL already stores `EncryptedPrivateKey` and `EncryptionKeyId`. Adding `EncryptedMasterKeyBlob` and `RecoveryKeyWraps` columns keeps recovery data co-located with the wallet. No new storage service needed.
**Alternatives considered**: Separate escrow table, Tenant Service storage — rejected for unnecessary complexity; recovery data is wallet-scoped.

## Decision 2: Recovery Key Wrapping Mechanism

**Decision**: Use `Sorcha.Cryptography.Core.CryptoModule.EncryptAsync()` with the recipient's public key to wrap the recovery key
**Rationale**: CryptoModule already supports asymmetric encryption for ED25519, NISTP256, RSA4096, and post-quantum algorithms. Recovery key is AES-256 (32 bytes) — well within the payload size for asymmetric encryption.
**Alternatives considered**: Key wrapping via AES-KW (RFC 3394) — rejected because CryptoModule already abstracts this; HKDF-based approach — adds complexity without benefit for small payloads.

## Decision 3: Passkey Public Key Retrieval

**Decision**: Passkey public key available via `PasskeyCredential.PublicKeyCose` in Tenant Service; retrieve via new service client method
**Rationale**: `PasskeyCredential` already stores `PublicKeyCose` (CBOR-encoded COSE public key) and `CredentialId`. The Wallet Service needs a service client to call Tenant Service for passkey public key retrieval during wallet creation.
**Alternatives considered**: Pass public key from UI at creation time — rejected because it requires browser API calls and is less secure than server-to-server resolution.

## Decision 4: Organization Recovery Key Pair

**Decision**: Organization generates an ED25519 recovery key pair; public key stored in org settings, private key held by org admin (exported as encrypted PEM or backed by passkey)
**Rationale**: ED25519 is the default algorithm for Sorcha wallets; the recovery public key needs to be stored server-side for wrapping during wallet creation. The private key must be recoverable by org admins but never stored on the server unencrypted.
**Alternatives considered**: RSA-4096 for larger key wrapping capacity — unnecessary for AES-256 key wrapping; per-user org recovery keys — too many keys to manage.

## Decision 5: Delegation Revocation on Recovery

**Decision**: Revoke all `WalletAccess` grants by default; present user with list to selectively preserve; org admin can skip revocation
**Rationale**: `DelegationService.RevokeAccessAsync()` already handles revocation. Recovery implies potential compromise — existing delegations should be treated as suspect. The selective preservation UX adds minimal complexity via a confirmation dialog.
**Alternatives considered**: Never revoke (too risky), always revoke without option (inflexible for org scenarios).

## Decision 6: Multi-Wallet Recovery

**Decision**: Recovery operation scoped to all wallets owned by the authenticated user; single operation restores all
**Rationale**: Wallet query `GetByOwnerAsync()` already returns all wallets for a user. Recovery key wraps are per-wallet, but the triggering event (passkey auth or org admin action) authenticates the user, not a specific wallet.
**Alternatives considered**: Per-wallet recovery — rejected because it's confusing UX and doesn't match the account-scoped auth model.

## Decision 7: AccessRight Enum Extension

**Decision**: Do NOT add a `Recovery` access right to the `AccessRight` enum
**Rationale**: Recovery is an account-level operation (user authenticates, all wallets restored), not a wallet-level delegation. The `AccessRight` enum (Owner/ReadWrite/ReadOnly) describes ongoing access grants. Recovery is a one-time privileged operation authorized by passkey proof or org admin role, not by a wallet access grant.
**Alternatives considered**: Add `Recovery` to AccessRight — rejected because it conflates ongoing access with one-time operations and would require complex cleanup after recovery.

## Key Infrastructure Already in Place

| Component | Location | Status |
|-----------|----------|--------|
| Wallet creation with mnemonic | WalletManager.CreateWalletAsync | Exists |
| Mnemonic recovery | WalletManager.RecoverWalletAsync | Exists |
| Key encryption (AES-256-GCM) | KeyManagementService.EncryptPrivateKeyAsync | Exists |
| Asymmetric encrypt/decrypt | CryptoModule.EncryptAsync/DecryptAsync | Exists |
| Delegation management | DelegationService (grant/revoke/check) | Exists |
| Passkey registration & auth | PasskeyService (Tenant Service) | Exists |
| Org admin authorization | OrganizationEndpoints + RequireAdministrator | Exists |
| Wallet PostgreSQL storage | WalletDbContext, Wallet entity | Exists |
