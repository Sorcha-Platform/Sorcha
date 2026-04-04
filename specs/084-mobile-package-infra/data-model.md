# Data Model: Mobile Package Infrastructure

**Feature**: 084-mobile-package-infra
**Date**: 2026-04-04

## Overview

This feature creates no new data entities. It extracts existing entities and types into new project boundaries. This document models the package dependency graph — the "data model" for this feature.

---

## Package Dependency Graph

```
NuGet.org Packages (consumed by SorchaMobile)
├── Sorcha.Wallet.Portable
│   ├── Sorcha.Cryptography
│   └── NBitcoin
├── Sorcha.ServiceClients.Http
│   ├── Microsoft.Extensions.Http
│   ├── Microsoft.AspNetCore.SignalR.Client
│   └── Microsoft.Extensions.Configuration.Abstractions
├── Sorcha.Cryptography (standalone)
├── Sorcha.Blueprint.Models (standalone)
├── Sorcha.Register.Models (standalone)
├── Sorcha.Tenant.Models (standalone)
├── Sorcha.TransactionHandler
│   └── Sorcha.Cryptography
├── Sorcha.Validator.Core
│   └── Sorcha.Cryptography
└── Sorcha.Blueprint.Engine
    └── Sorcha.Blueprint.Models

Server-Only Packages (NOT on NuGet.org)
├── Sorcha.Wallet.Core
│   ├── Sorcha.Wallet.Portable (transitive)
│   ├── Microsoft.EntityFrameworkCore
│   └── Npgsql.EntityFrameworkCore.PostgreSQL
└── Sorcha.ServiceClients
    ├── Sorcha.ServiceClients.Http (transitive)
    ├── Grpc.Net.Client
    └── Google.Protobuf
```

---

## Sorcha.Wallet.Portable — Contents

### Entities (moved from Wallet.Core, namespace preserved)

| Entity | Namespace | Key Dependencies |
|--------|-----------|-----------------|
| Wallet | Sorcha.Wallet.Core.Domain.Entities | Enums only |
| WalletAddress | Sorcha.Wallet.Core.Domain.Entities | Wallet (navigation) |
| WalletAccess | Sorcha.Wallet.Core.Domain.Entities | Wallet (navigation) |
| WalletTransaction | Sorcha.Wallet.Core.Domain.Entities | Wallet (navigation) |
| CredentialEntity | Sorcha.Wallet.Core.Domain.Entities | None |
| RecoveryKeyWrap | Sorcha.Wallet.Core.Domain.Entities | Wallet (navigation) |
| RecoveryAuditLog | Sorcha.Wallet.Core.Domain.Entities | None |
| OrgMasterKey | Sorcha.Wallet.Core.Domain.Entities | DerivedKeyRecord (navigation) |
| DerivedKeyRecord | Sorcha.Wallet.Core.Domain.Entities | OrgMasterKey, Wallet (navigations) |
| ThresholdKeyGroup | Sorcha.Wallet.Core.Domain.Entities | SigningKeyShare, SigningSession (navigations) |
| SigningKeyShare | Sorcha.Wallet.Core.Domain.Entities | ThresholdKeyGroup (navigation) |
| SigningSession | Sorcha.Wallet.Core.Domain.Entities | ThresholdKeyGroup (navigation) |

### Enums (moved, namespace preserved)

| Enum | Namespace |
|------|-----------|
| WalletStatus, AccessRight, RecoveryPathType, TransactionState, TransactionDirection | Sorcha.Wallet.Core.Domain |
| KeyUsage | Sorcha.Wallet.Core.Domain.Enums |
| CustodyMode | Sorcha.Wallet.Core.Domain.Enums |
| DerivedKeyStatus | Sorcha.Wallet.Core.Domain.Enums |
| OrgMasterKeyStatus | Sorcha.Wallet.Core.Domain.Enums |
| SigningMode | Sorcha.Wallet.Core.Domain.Enums |
| SigningSessionState | Sorcha.Wallet.Core.Domain.Enums |
| ThresholdKeyGroupStatus | Sorcha.Wallet.Core.Domain.Enums |

### Interfaces (moved, namespace preserved)

| Interface | Namespace |
|-----------|-----------|
| IOrgKeyDerivationService | Sorcha.Wallet.Core.Services.Interfaces |
| IOrgKeyProtectionProvider | Sorcha.Wallet.Core.Services.Interfaces |
| IWalletService | Sorcha.Wallet.Core.Services.Interfaces |
| IKeyManagementService | Sorcha.Wallet.Core.Services.Interfaces |
| IDelegationService | Sorcha.Wallet.Core.Services.Interfaces |
| ITransactionService | Sorcha.Wallet.Core.Services.Interfaces |
| IRecoveryKeyService | Sorcha.Wallet.Core.Services.Interfaces |

### Other (moved)

| Type | Notes |
|------|-------|
| DerivationPathBuilder | Moved from Wallet.Service (pure static logic) |
| Exceptions (2 files) | WalletNotFoundException, WalletAccessAlreadyExistsException |
| Constants | SorchaDerivationPaths |
| Domain Events | WalletEvent base class |

---

## Sorcha.ServiceClients.Http — Contents

### Client Classes (moved, namespace preserved)

| Client | Interface | Namespace |
|--------|-----------|-----------|
| ServiceAuthClient | IServiceAuthClient | Sorcha.ServiceClients.Auth |
| DelegationTokenClient | IDelegationTokenClient | Sorcha.ServiceClients.Auth |
| TokenIntrospectionClient | ITokenIntrospectionClient | Sorcha.ServiceClients.Auth |
| WalletServiceClient | IWalletServiceClient | Sorcha.ServiceClients.Wallet |
| RegisterServiceClient | IRegisterServiceClient | Sorcha.ServiceClients.Register |
| BlueprintServiceClient | IBlueprintServiceClient | Sorcha.ServiceClients.Blueprint |
| ParticipantServiceClient | IParticipantServiceClient | Sorcha.ServiceClients.Participant |
| ValidatorServiceClient | IValidatorServiceClient | Sorcha.ServiceClients.Validator |
| EventServiceClient | IEventServiceClient | Sorcha.ServiceClients.Events |
| SubscriptionServiceClient | ISubscriptionServiceClient | Sorcha.ServiceClients.Subscription |
| PasskeyServiceClient | IPasskeyServiceClient | Sorcha.ServiceClients.Passkey |
| WebDidResolver | (IDidResolver) | Sorcha.ServiceClients.Did |
| SorchaDidResolver | (IDidResolver) | Sorcha.ServiceClients.Did |
| KeyDidResolver | (IDidResolver) | Sorcha.ServiceClients.Did |
| DidResolverRegistry | IDidResolverRegistry | Sorcha.ServiceClients.Did |
| SystemWalletSigningService | ISystemWalletSigningService | Sorcha.ServiceClients.SystemWallet |
| ServiceClientAuthHelper | (static) | Sorcha.ServiceClients.Helpers |

### New Types

| Type | Namespace | Purpose |
|------|-----------|---------|
| SorchaHubConnectionBuilder | Sorcha.ServiceClients.Http.Hub | JWT auth + reconnection for SignalR |
| HttpServiceCollectionExtensions | Sorcha.ServiceClients.Http.Extensions | HTTP-only DI registrations |

---

## Packages Published to NuGet.org

| # | Package ID | New? | Description |
|---|-----------|------|-------------|
| 1 | Sorcha.Wallet.Portable | NEW | Wallet entities, enums, interfaces, derivation |
| 2 | Sorcha.ServiceClients.Http | NEW | REST clients, SignalR helper, auth |
| 3 | Sorcha.Cryptography | Existing | Multi-algorithm crypto |
| 4 | Sorcha.Blueprint.Models | Existing | Blueprint JSON-LD models |
| 5 | Sorcha.Register.Models | Existing | Register domain models |
| 6 | Sorcha.Tenant.Models | Existing | Tenant/org domain models |
| 7 | Sorcha.TransactionHandler | Existing | Transaction building/serialization |
| 8 | Sorcha.Validator.Core | Existing | Enclave-safe validation |
| 9 | Sorcha.Blueprint.Engine | Existing | Portable blueprint execution |
