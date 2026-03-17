# Quickstart: Wallet Recovery

**Feature**: 060-wallet-recovery

## What This Feature Does

Adds two new recovery paths (org-managed and passkey-bound) to the existing mnemonic recovery, so users can recover wallets without remembering their seed phrase. Recovery restores all wallets, revokes delegations by default (with selective preservation), and creates an audit trail.

## Implementation Approach

This feature modifies existing services — no new projects. The work is split across Wallet Service (core recovery logic), Tenant Service (org config + passkey key retrieval), and UI (recovery flows).

### What Already Exists (Don't Rebuild)

| Component | What It Does | Where |
|-----------|-------------|-------|
| Mnemonic recovery | Restores wallet from BIP39 phrase | WalletManager.RecoverWalletAsync |
| Key encryption | AES-256-GCM private key encryption | KeyManagementService.EncryptPrivateKeyAsync |
| Asymmetric crypto | Encrypt/decrypt with public/private keys | CryptoModule.EncryptAsync/DecryptAsync |
| Delegation management | Grant, revoke, check wallet access | DelegationService |
| Passkey infrastructure | FIDO2 registration, authentication | PasskeyService (Tenant) |
| Org admin authorization | Admin role checks, org membership | OrganizationEndpoints (Tenant) |

### What Needs Building

**Layer 1 — Foundation (~6h)**
1. Add `EncryptedMasterKeyBlob`, `RecoveryEnabled` to Wallet entity + migration
2. Create `RecoveryKeyWrap` entity + migration
3. Create `RecoveryAuditLog` entity
4. Create `IRecoveryKeyService` — generate recovery key, wrap to public key, unwrap
5. Modify `WalletManager.CreateWalletAsync` to generate and store recovery wraps

**Layer 2 — Passkey Recovery (~6h)**
6. Add passkey public key retrieval service client
7. Create `PasskeyRecoveryService` — verify passkey, unwrap recovery key, restore wallets
8. Add `POST /api/v1/wallets/recover/passkey` endpoint
9. Create passkey recovery UI page

**Layer 3 — Org-Managed Recovery (~6h)**
10. Create `OrgRecoveryConfig` entity in Tenant Service + migration
11. Add org recovery config endpoints (POST/GET/PUT)
12. Create `OrgRecoveryService` — admin-initiated recovery with MFA verification
13. Add `POST /api/v1/wallets/recover/org` endpoint
14. Create org admin recovery UI page

**Layer 4 — Delegation & Polish (~4h)**
15. Add delegation revocation + selective preservation flow
16. Add `RecoveryAuditLog` persistence
17. Add `GET /api/v1/wallets/recovery-status` endpoint
18. Unit tests for recovery key service, passkey recovery, org recovery

## Key Files to Modify

| File | Change |
|------|--------|
| `Sorcha.Wallet.Core/Domain/Entities/Wallet.cs` | Add recovery fields |
| `Sorcha.Wallet.Core/Data/WalletDbContext.cs` | Add RecoveryKeyWrap, RecoveryAuditLog DbSets |
| `Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs` | Generate recovery wraps at creation |
| `Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` | Add recovery endpoints |
| `Sorcha.Tenant.Service/Models/OrgRecoveryConfig.cs` | New entity |
| `Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs` | Add recovery config endpoints |

## Testing Strategy

- Unit tests for RecoveryKeyService (wrap/unwrap, algorithm coverage)
- Unit tests for PasskeyRecoveryService (happy path, invalid passkey, no wraps)
- Unit tests for OrgRecoveryService (admin auth, MFA, delegation revocation)
- Integration tests for recovery endpoints (full flow with test database)
- E2E: Create wallet → recover via passkey → verify signing works
