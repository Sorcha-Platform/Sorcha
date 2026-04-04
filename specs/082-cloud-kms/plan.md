# Implementation Plan: Cloud KMS Key Management

**Branch**: `082-cloud-kms` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/082-cloud-kms/spec.md`
**Design Reference**: [cloud-kms-key-management-design.md](../../docs/superpowers/specs/2026-04-04-cloud-kms-key-management-design.md)

## Summary

Replace the existing `IEncryptionProvider` with two focused interfaces (`IKeyProtectionProvider` for envelope encryption, `ISigningProvider` for KMS-resident signing) to support multi-cloud key management. Initial implementation covers Azure Key Vault and the existing Local provider. Wallets gain a `SigningMode` (Local or KmsResident) with policy-based defaults and API override.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Azure.Security.KeyVault.Keys, Azure.Identity, NBitcoin, existing Sorcha.Cryptography
**Storage**: PostgreSQL (wallet entities via EF Core), Azure Key Vault (DEKs and KMS-resident keys)
**Testing**: xUnit + FluentAssertions + Moq, WebApplicationFactory for integration tests
**Target Platform**: Linux containers (Docker/Kubernetes), Azure Container Apps
**Project Type**: Microservice (Wallet Service) + shared library (Wallet.Core) + new provider package
**Performance Goals**: Local signing < 1s (cache hit), KMS-resident signing < 2s, DEK cache hit < 10ms
**Constraints**: No private key material in memory for KMS-resident wallets, fail closed on KMS outage (after grace period), < $50/month KMS cost for 1000 wallets
**Scale/Scope**: Up to 1000 wallets, 100K sign operations/month initially

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | New Azure provider is a separate project. Core interfaces in Wallet.Core. No cross-service coupling. |
| II. Security First | PASS | This feature directly implements cloud KMS per constitution ("Support for Azure Key Vault, AWS KMS"). AES-256-GCM encryption at rest maintained. |
| III. API Documentation | PASS | New wallet creation parameter documented via Scalar/OpenAPI. |
| IV. Testing Requirements | PASS | Unit tests for policy resolution, provider interfaces. Integration tests with Key Vault emulator. |
| V. Code Quality | PASS | Async/await, DI, nullable reference types, no warnings. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | SigningMode is a domain concept on the Wallet entity. |
| VIII. Observability | PASS | Audit logging for all key operations (existing EncryptionAuditLogger pattern). |

No violations. Gate passes.

## Project Structure

### Documentation (this feature)

```text
specs/082-cloud-kms/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── wallet-api.yaml  # Updated wallet creation contract
└── checklists/
    └── requirements.md  # Quality checklist
```

### Source Code (repository root)

```text
src/Core/Sorcha.Wallet.Core/
├── Encryption/
│   ├── Interfaces/
│   │   ├── IKeyProtectionProvider.cs     # NEW — replaces IEncryptionProvider
│   │   └── ISigningProvider.cs           # NEW — KMS-resident signing
│   ├── Models/
│   │   └── KmsKeyInfo.cs                 # NEW — key creation response
│   ├── Providers/
│   │   └── LocalKeyProtectionProvider.cs # REFACTORED from EncryptionProviderBase
│   └── Configuration/
│       ├── WalletKeyManagementOptions.cs # NEW — unified config
│       └── SigningModePolicy.cs          # NEW — resolves mode per wallet
├── Domain/
│   ├── Entities/
│   │   └── Wallet.cs                     # MODIFIED — add SigningMode, KmsKeyId
│   └── Enums/
│       └── SigningMode.cs                # NEW
└── Services/
    └── Implementation/
        ├── WalletManager.cs              # MODIFIED — branch on SigningMode
        ├── KeyManagementService.cs       # MODIFIED — use IKeyProtectionProvider
        └── TransactionService.cs         # MODIFIED — branch signing path

src/Providers/
└── Sorcha.Wallet.Providers.Azure/        # NEW PROJECT
    ├── AzureKeyProtectionProvider.cs
    ├── AzureSigningProvider.cs
    ├── AzureKmsOptions.cs
    └── Extensions/
        └── ServiceCollectionExtensions.cs

src/Services/Sorcha.Wallet.Service/
└── Extensions/
    └── WalletServiceExtensions.cs        # MODIFIED — provider registration

tests/
├── Sorcha.Wallet.Core.Tests/
│   ├── Encryption/
│   │   ├── LocalKeyProtectionProviderTests.cs    # REFACTORED
│   │   └── SigningModePolicyTests.cs             # NEW
│   └── Services/
│       ├── WalletManagerTests.cs                  # MODIFIED
│       └── TransactionServiceTests.cs             # MODIFIED
└── Sorcha.Wallet.Providers.Azure.Tests/           # NEW PROJECT
    ├── AzureKeyProtectionProviderTests.cs
    └── AzureSigningProviderTests.cs
```

**Structure Decision**: Follows existing Sorcha pattern — interfaces and domain in `Wallet.Core`, cloud-specific implementations in separate `Providers` project under `src/Providers/`. This keeps the core library cloud-agnostic and the Azure dependency isolated.

## Complexity Tracking

No constitution violations to justify.
