# Implementation Plan: Wallet Recovery

**Branch**: `060-wallet-recovery` | **Date**: 2026-03-17 | **Spec**: [spec.md](spec.md)

## Summary

Add passkey-bound and organization-managed wallet recovery paths alongside the existing mnemonic recovery. At wallet creation, an AES-256 recovery key encrypts the master key; the recovery key is then wrapped to each configured recipient's public key (passkey or org recovery key). Recovery unwraps the key, restores all user wallets, revokes delegations by default with selective preservation, and creates an audit trail.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Cryptography (AES-256-GCM, asymmetric encrypt), NBitcoin (BIP32/39/44), Fido2NetLib (WebAuthn)
**Storage**: PostgreSQL (Wallet entity extension, new RecoveryKeyWrap + RecoveryAuditLog entities, OrgRecoveryConfig in Tenant)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: .NET microservices (Wallet Service, Tenant Service) + Blazor WASM UI
**Project Type**: Modifications to existing services (no new projects)
**Security Goals**: Recovery key never stored unencrypted server-side; MFA required for org admin recovery; audit trail for all recovery operations
**Constraints**: No production instances — wallet creation only (no retroactive recovery enablement needed)

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Wallet Service owns recovery logic; Tenant Service owns org config + passkey resolution |
| II. Security First | PASS | AES-256-GCM for recovery key; asymmetric wrapping; MFA for org admin; delegation revocation |
| III. API Documentation | PASS | New endpoints get OpenAPI/Scalar docs |
| IV. Testing Requirements | PASS | Unit + integration tests planned for all paths |
| V. Code Quality | PASS | Async/await, DI, nullable enabled |
| VI. Blueprint Standards | N/A | No blueprint changes |
| VII. Domain-Driven Design | PASS | Uses Wallet, Participant, Delegation terminology |
| VIII. Observability | PASS | Recovery operations logged with structured events |

## Project Structure

### Documentation (this feature)

```text
specs/060-wallet-recovery/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity definitions
├── quickstart.md        # Implementation guide
└── contracts/
    └── wallet-recovery-api.yaml
```

### Source Code (modifications to existing projects)

```text
src/Core/Sorcha.Wallet.Core/
├── Domain/Entities/
│   ├── Wallet.cs                      # Add EncryptedMasterKeyBlob, RecoveryEnabled
│   ├── RecoveryKeyWrap.cs             # New: per-path recovery key wrap
│   └── RecoveryAuditLog.cs            # New: immutable audit trail
├── Domain/Enums.cs                    # Add RecoveryPathType enum
├── Data/WalletDbContext.cs            # Add new DbSets + configuration
├── Services/Interfaces/
│   └── IRecoveryKeyService.cs         # New: recovery key lifecycle
└── Services/Implementation/
    ├── WalletManager.cs               # Modify CreateWalletAsync for recovery wraps
    └── RecoveryKeyService.cs          # New: generate, wrap, unwrap recovery keys

src/Services/Sorcha.Wallet.Service/
├── Endpoints/WalletEndpoints.cs       # Add recovery endpoints
├── Services/
│   ├── Interfaces/
│   │   ├── IPasskeyRecoveryService.cs # New
│   │   └── IOrgRecoveryService.cs     # New
│   └── Implementation/
│       ├── PasskeyRecoveryService.cs  # New: passkey-bound recovery flow
│       └── OrgRecoveryService.cs      # New: org-managed recovery flow
└── Models/
    ├── RecoverPasskeyRequest.cs       # New
    ├── RecoverOrgRequest.cs           # New
    └── RecoveryResult.cs              # New: shared response model

src/Services/Sorcha.Tenant.Service/
├── Models/OrgRecoveryConfig.cs        # New entity
├── Data/TenantDbContext.cs            # Add OrgRecoveryConfig DbSet
└── Endpoints/OrganizationEndpoints.cs # Add recovery config endpoints

src/Common/Sorcha.ServiceClients/
└── Passkey/
    ├── IPasskeyServiceClient.cs       # New: passkey public key retrieval
    └── PasskeyServiceClient.cs        # New: HTTP client

tests/
├── Sorcha.Wallet.Core.Tests/
│   └── Services/RecoveryKeyServiceTests.cs
├── Sorcha.Wallet.Service.Tests/
│   ├── Services/PasskeyRecoveryServiceTests.cs
│   └── Services/OrgRecoveryServiceTests.cs
└── Sorcha.Tenant.Service.Tests/
    └── Endpoints/OrgRecoveryConfigTests.cs
```

## Complexity Tracking

No constitution violations. All changes are modifications to existing services. The passkey service client is a new addition to Sorcha.ServiceClients following the established pattern (IEventServiceClient, IParticipantServiceClient).
