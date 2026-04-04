# Mobile Package Infrastructure

**Date:** 2026-04-04
**Status:** Approved
**Scope:** Extract portable libraries, NuGet packaging pipeline, unblock SorchaMobile consumption

---

## Overview

Make Sorcha's shared libraries consumable by the SorchaMobile .NET MAUI app via NuGet.org. Extract server-only dependencies (EF Core, gRPC) from packages the mobile app needs, publish 9 packages to NuGet.org, and set up CI/CD for automated packaging.

SorchaMobile targets .NET 10 (confirmed), eliminating the previously planned multi-target (net8.0;net10.0) requirement. All packages remain single-target net10.0.

### What Changes

| Before | After |
|--------|-------|
| `Sorcha.Wallet.Core` — entities + EF Core + crypto + derivation | `Sorcha.Wallet.Portable` (NEW) — entities, enums, interfaces, derivation logic |
| | `Sorcha.Wallet.Core` — EF Core DbContext + migrations (references Portable) |
| `Sorcha.ServiceClients` — HTTP + gRPC clients | `Sorcha.ServiceClients.Http` (NEW) — REST clients + SignalR hub helper |
| | `Sorcha.ServiceClients` — gRPC clients only (references ServiceClients.Http) |

### Explicitly Out of Scope

- MOB-001 (multi-target net8.0) — eliminated, SorchaMobile confirmed .NET 10
- MOB-005 (device input field types) — feature capability, future spec
- MOB-006 (Blazor UI device controls) — feature capability, future spec
- MOB-007 (org branding) — feature capability, future spec
- MOB-008 (VC exchange protocol) — feature capability, future spec
- SorchaMobile app itself — separate repo, consumes these packages

---

## 1. Sorcha.Wallet.Portable (MOB-002)

New project extracting portable wallet logic from `Sorcha.Wallet.Core`. Zero server dependencies — no EF Core, no PostgreSQL, no ASP.NET Core.

### What Moves to Portable

| Directory | Contents |
|-----------|----------|
| `Domain/Entities/` | Wallet, OrgMasterKey, DerivedKeyRecord, WalletAddress, WalletTransaction, WalletAccess, ThresholdKeyGroup, SigningKeyShare, SigningSession, CredentialEntity, RecoveryKeyWrap, RecoveryAuditLog |
| `Domain/Enums/` | All enums (WalletStatus, KeyUsage, CustodyMode, SigningMode, TransactionState, etc.) |
| `Domain/` | WalletStatus, AccessRight, RecoveryPathType (from Enums.cs) |
| `Services/Interfaces/` | IOrgKeyDerivationService, IOrgKeyProtectionProvider, IWalletService, IKeyManagementService, ITransactionService, IDelegationService, IRecoveryKeyService |

### What Also Moves (from Wallet.Service)

| File | Reason |
|------|--------|
| `DerivationPathBuilder.cs` | Pure logic, no server dependencies — mobile needs it for client-side derivation |

### What Stays in Wallet.Core

| Directory | Contents |
|-----------|----------|
| `Data/` | WalletDbContext, WalletDbContextFactory, migrations |
| `Repositories/` | Repository interfaces and implementations |
| `Encryption/Providers/` | Server-side encryption providers (DPAPI, etc.) |

### Dependency Chain

```
Sorcha.Wallet.Portable (no server deps)
    ├── Sorcha.Cryptography
    └── NBitcoin (BIP32/39 derivation)

Sorcha.Wallet.Core (server-side)
    ├── Sorcha.Wallet.Portable
    ├── Microsoft.EntityFrameworkCore
    └── Npgsql.EntityFrameworkCore.PostgreSQL
```

### Project File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>Sorcha.Wallet.Portable</PackageId>
    <Description>Portable wallet library for Sorcha — HD derivation, key types, enums. No server dependencies.</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Sorcha.Cryptography\Sorcha.Cryptography.csproj" />
    <PackageReference Include="NBitcoin" />
  </ItemGroup>
</Project>
```

### Migration Impact

All projects currently referencing `Sorcha.Wallet.Core` for entity types need to either:
- Reference `Sorcha.Wallet.Portable` instead (if they only need entities/enums)
- Continue referencing `Sorcha.Wallet.Core` (if they need EF Core/DbContext)

The Wallet Service itself references `Wallet.Core` which transitively includes `Wallet.Portable`.

---

## 2. Sorcha.ServiceClients.Http (MOB-004)

New project extracting REST HTTP clients from `Sorcha.ServiceClients`. No gRPC dependencies.

### What Moves to ServiceClients.Http

| Directory | Contents |
|-----------|----------|
| `Auth/` | ServiceAuthClient, IServiceAuthClient, DelegationTokenClient, TokenIntrospectionClient |
| `Helpers/` | ServiceClientAuthHelper |
| `Wallet/` | IWalletServiceClient, WalletServiceClient |
| `Register/` | IRegisterServiceClient, RegisterServiceClient |
| `Blueprint/` | IBlueprintServiceClient, BlueprintServiceClient |
| `Participant/` | IParticipantServiceClient, ParticipantServiceClient |
| `Peer/` | IPeerServiceClient, PeerServiceClient (HTTP portion only) |
| `Subscription/` | ISubscriptionServiceClient, SubscriptionServiceClient |
| `Validator/` | IValidatorServiceClient, ValidatorServiceClient |
| `Events/` | IEventServiceClient, EventServiceClient |
| `Passkey/` | IPasskeyServiceClient, PasskeyServiceClient |
| `Did/` | DID resolver interfaces and implementations |
| `Extensions/` | ServiceCollectionExtensions (HTTP registrations only) |

### New: SorchaHubConnectionBuilder

Shared SignalR hub connection helper for both web and mobile:

```csharp
public class SorchaHubConnectionBuilder
{
    /// Builds a HubConnection with JWT auth, automatic reconnection, and URL resolution.
    public static HubConnection Build(string hubUrl, Func<Task<string?>> tokenProvider);
}
```

Features:
- JWT Bearer token attached to every connection via `AccessTokenProvider`
- Automatic reconnection with exponential backoff (1s, 2s, 5s, 10s, 30s)
- Configurable hub URL (API Gateway base + hub path)
- Logging integration

### What Stays in ServiceClients

| Directory | Contents |
|-----------|----------|
| `Grpc/` | DocketSyncClient, RegisterAddressClient, WalletNotificationClient |
| `Extensions/` | gRPC-specific DI registrations (AddGrpcClient calls) |
| Proto references | Register, Wallet, Peer proto files |

### Dependency Chain

```
Sorcha.ServiceClients.Http (no gRPC)
    ├── Microsoft.Extensions.Http
    ├── Microsoft.AspNetCore.SignalR.Client
    └── System.Net.Http.Json

Sorcha.ServiceClients (server-side)
    ├── Sorcha.ServiceClients.Http
    ├── Grpc.Net.Client
    └── Google.Protobuf
```

### DI Registration Split

`ServiceClients.Http` exposes `AddHttpServiceClients(IServiceCollection, IConfiguration)` for HTTP-only registration. The existing `AddServiceClients` in `ServiceClients` calls this plus adds gRPC clients.

---

## 3. NuGet Packaging Pipeline (MOB-003)

GitHub Actions workflow to build, pack, and publish packages to NuGet.org on merge to master.

### Packages Published

| Package | Source Project | New? |
|---------|---------------|------|
| `Sorcha.Wallet.Portable` | src/Core/Sorcha.Wallet.Portable | NEW |
| `Sorcha.ServiceClients.Http` | src/Common/Sorcha.ServiceClients.Http | NEW |
| `Sorcha.Cryptography` | src/Common/Sorcha.Cryptography | Existing |
| `Sorcha.Blueprint.Models` | src/Common/Sorcha.Blueprint.Models | Existing |
| `Sorcha.Register.Models` | src/Common/Sorcha.Register.Models | Existing |
| `Sorcha.Tenant.Models` | src/Common/Sorcha.Tenant.Models | Existing |
| `Sorcha.TransactionHandler` | src/Common/Sorcha.TransactionHandler | Existing |
| `Sorcha.Validator.Core` | src/Common/Sorcha.Validator.Core | Existing |
| `Sorcha.Blueprint.Engine` | src/Core/Sorcha.Blueprint.Engine | Existing |

### Versioning Strategy

- Version derived from git tags: `1.0.0`, `1.1.0`, etc.
- Pre-release builds on merge to master: `1.0.0-ci.{run_number}`
- Release builds on git tag push: exact tag version
- All packages share the same version (monorepo versioning)

### Workflow

```yaml
name: Publish NuGet Packages
on:
  push:
    branches: [master]
    tags: ['v*']

jobs:
  publish:
    - dotnet restore
    - dotnet build --configuration Release
    - dotnet test --configuration Release
    - dotnet pack --configuration Release --output ./nupkgs
    - dotnet nuget push ./nupkgs/*.nupkg --source nuget.org --api-key ${{ secrets.NUGET_API_KEY }}
```

### Package Metadata

All packages inherit from `Directory.Build.props`:
- Authors: Sorcha Contributors
- License: MIT
- Repository URL: GitHub
- SourceLink enabled for debugging

---

## 4. Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| .NET target | net10.0 only | SorchaMobile confirmed .NET 10, no multi-targeting needed |
| Package registry | NuGet.org (public, listed) | Repo is MIT/public, API key already configured |
| Wallet split | Portable + Core | Clean server/portable boundary, mobile gets zero EF Core deps |
| ServiceClients split | Http + gRPC | Mobile only talks REST via Gateway, no gRPC needed |
| SignalR helper | In ServiceClients.Http | JWT auth + reconnection policy shared between web and mobile |
| DerivationPathBuilder | Moves to Portable | Pure logic, mobile needs it for client-side HD derivation |
| Versioning | Monorepo shared version | Simpler than per-package versioning for internal packages |
| CI trigger | Merge to master + git tags | CI builds for testing, tag builds for releases |
