# Implementation Plan: Wallet Key Derivation & UI Transaction Lifecycle

**Branch**: `083-wallet-key-derivation` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/083-wallet-key-derivation/spec.md`

## Summary

Deliver org-level HD key derivation (master seed provisioning, deterministic user key derivation, rotation, revocation), wallet UI transaction lifecycle indicators (tick icons with detail panel and receipt proofs), and forward-compatible schema for threshold signing. Uses Sorcha-specific BIP32 derivation paths under purpose `0x534F52'` with pluggable seed encryption.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: NBitcoin (BIP32/39), Entity Framework Core (PostgreSQL), MudBlazor (UI), SignalR (real-time), Sorcha.Cryptography (AES-256-GCM)
**Storage**: PostgreSQL (Wallet Service EF Core context) — new entities + wallet modifications
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0, Moq 4.20.72, Playwright (E2E)
**Target Platform**: .NET 10 server (Wallet Service) + Blazor WASM (Sorcha.UI)
**Project Type**: Microservices — changes span Wallet Service, Wallet Core, UI Web Client, API Gateway, ServiceClients
**Performance Goals**: Key derivation <500ms, UI tick update <2s real-time, receipt verification <2s
**Constraints**: Single squashed migration per context, no FROST implementation, custodial mode only
**Scale/Scope**: ~6 new entities, 4 new endpoints, 3 new UI components, 1 migration

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Org key derivation in Wallet Service, UI in Sorcha.UI, gateway routing via YARP. Service boundary maintained. |
| II. Security First | PASS | Master seed encrypted at rest (AES-256-GCM via pluggable provider). Mnemonic shown once, never stored. Keys derived deterministically. No secrets in source. |
| III. API Documentation | PASS | New endpoints get Scalar OpenAPI docs with WithSummary/WithDescription. XML comments on all public interfaces. |
| IV. Testing Requirements | PASS | Unit tests for derivation logic, integration tests for endpoints, E2E tests for UI ticks. Target >85% coverage on new code. |
| V. Code Quality | PASS | Async/await for all I/O, DI for all services, nullable reference types enabled, C# 13 features. |
| VI. Blueprint Creation | N/A | No blueprint changes in this feature. |
| VII. Domain-Driven Design | PASS | Rich domain entities (OrgMasterKey, DerivedKeyRecord). Ubiquitous language maintained. |
| VIII. Observability | PASS | Structured logging for key derivation events, provisioning, rotation, revocation. Health checks unaffected. |

**Gate result: PASS** — No violations. No complexity justification needed.

## Project Structure

### Documentation (this feature)

```text
specs/083-wallet-key-derivation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI)
│   └── wallet-org-keys.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Core/Sorcha.Wallet.Core/
│   ├── Domain/Entities/
│   │   ├── OrgMasterKey.cs              # NEW
│   │   ├── DerivedKeyRecord.cs          # NEW
│   │   ├── ThresholdKeyGroup.cs         # NEW (schema only)
│   │   ├── SigningKeyShare.cs           # NEW (schema only)
│   │   ├── SigningSession.cs            # NEW (schema only)
│   │   └── Wallet.cs                   # MODIFIED (add DerivedKeyRecordId, CustodyMode)
│   ├── Domain/Enums/
│   │   ├── KeyUsage.cs                  # NEW
│   │   └── CustodyMode.cs              # NEW
│   └── Services/Interfaces/
│       ├── IOrgKeyDerivationService.cs  # NEW
│       └── IOrgKeyProtectionProvider.cs # NEW
├── Services/Sorcha.Wallet.Service/
│   ├── Services/Implementation/
│   │   ├── OrgKeyDerivationService.cs   # NEW
│   │   └── SoftwareKeyProtectionProvider.cs # NEW
│   ├── Endpoints/
│   │   └── OrgKeyEndpoints.cs           # NEW (4 endpoints)
│   └── Data/
│       └── Migrations/                  # Single squashed migration
├── Apps/Sorcha.UI/Sorcha.UI.Web.Client/
│   ├── Components/Wallet/
│   │   ├── TransactionTickIcon.razor    # NEW
│   │   ├── TransactionDetailDrawer.razor # NEW
│   │   └── ReceiptProofCard.razor       # NEW
│   └── Pages/Wallets/
│       └── WalletDetail.razor           # MODIFIED (transactions tab)
├── Common/Sorcha.ServiceClients/
│   └── Wallet/
│       ├── IWalletServiceClient.cs      # MODIFIED (org key methods)
│       └── WalletServiceClient.cs       # MODIFIED (org key methods)
└── Services/Sorcha.ApiGateway/
    └── appsettings.json                 # MODIFIED (YARP route)

tests/
├── Sorcha.Wallet.Core.Tests/
│   └── OrgKeyDerivationTests.cs         # NEW
├── Sorcha.Wallet.Service.Tests/
│   └── OrgKeyEndpointsTests.cs          # NEW
├── Sorcha.Wallet.Service.IntegrationTests/
│   └── OrgKeyIntegrationTests.cs        # NEW
└── Sorcha.UI.E2E.Tests/
    └── TransactionTickTests.cs          # NEW
```

**Structure Decision**: Follows existing Sorcha microservice patterns. New domain entities in Wallet.Core, service implementations in Wallet.Service, UI components in Sorcha.UI.Web.Client. No new projects created.
