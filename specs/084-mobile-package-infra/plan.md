# Implementation Plan: Mobile Package Infrastructure

**Branch**: `084-mobile-package-infra` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/084-mobile-package-infra/spec.md`

## Summary

Extract portable libraries from Sorcha.Wallet.Core and Sorcha.ServiceClients, set up NuGet.org publishing pipeline. Creates two new projects (Sorcha.Wallet.Portable, Sorcha.ServiceClients.Http), rewires existing references, and automates package publishing via GitHub Actions.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: NBitcoin, Sorcha.Cryptography, Microsoft.Extensions.Http, Microsoft.AspNetCore.SignalR.Client
**Storage**: N/A (no new data storage — package extraction only)
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0 — verify 638+ existing tests pass post-extraction
**Target Platform**: .NET 10 (single target — SorchaMobile confirmed .NET 10)
**Project Type**: Monorepo package extraction — 2 new projects, 12+ project reference updates
**Performance Goals**: NuGet publish pipeline completes within 10 minutes
**Constraints**: Zero breaking changes to existing projects. All entities are POCO (fluent config only, no EF annotations).
**Scale/Scope**: ~35 files move to Wallet.Portable, ~35 files move to ServiceClients.Http, 9 packages published

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Package extraction improves modularity. No new coupling. |
| II. Security First | PASS | No security changes. Encryption providers stay server-side. |
| III. API Documentation | N/A | No new APIs — extracting existing code. |
| IV. Testing Requirements | PASS | All 638+ existing tests must pass (FR-014). |
| V. Code Quality | PASS | Clean project boundaries, async patterns preserved. |
| VI. Blueprint Creation | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Domain entities move to portable — improves DDD separation. |
| VIII. Observability | N/A | No observability changes. |

**Gate result: PASS**

## Project Structure

### Documentation (this feature)

```text
specs/084-mobile-package-infra/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (package dependency model)
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code Changes

```text
NEW PROJECTS:
├── src/Core/Sorcha.Wallet.Portable/           # Extracted portable wallet
│   ├── Domain/Entities/ (12 files moved)
│   ├── Domain/Enums/ (7 files moved)
│   ├── Domain/Enums.cs (moved)
│   ├── Domain/Events/ (moved)
│   ├── Services/Interfaces/ (7 files moved)
│   ├── Exceptions/ (2 files moved)
│   ├── Constants/ (moved)
│   ├── DerivationPathBuilder.cs (moved from Wallet.Service)
│   └── Sorcha.Wallet.Portable.csproj
│
├── src/Common/Sorcha.ServiceClients.Http/     # Extracted HTTP clients
│   ├── Auth/ (6 files moved)
│   ├── Wallet/ (3+ files moved)
│   ├── Register/ (3 files moved)
│   ├── Blueprint/ (2 files moved)
│   ├── Participant/ (2 files moved)
│   ├── Validator/ (2 files moved)
│   ├── Events/ (3 files moved)
│   ├── Subscription/ (1 file moved)
│   ├── Passkey/ (2 files moved)
│   ├── Did/ (6 files moved)
│   ├── SystemWallet/ (4+ files moved)
│   ├── Helpers/ (1 file moved)
│   ├── Models/ (moved)
│   ├── Hub/SorchaHubConnectionBuilder.cs (NEW)
│   ├── Extensions/HttpServiceCollectionExtensions.cs (NEW)
│   └── Sorcha.ServiceClients.Http.csproj
│
└── .github/workflows/publish-nuget.yml        # NEW pipeline

MODIFIED PROJECTS (reference updates):
├── src/Core/Sorcha.Wallet.Core/               # Now references Wallet.Portable
├── src/Common/Sorcha.ServiceClients/           # Now references ServiceClients.Http
├── src/Services/Sorcha.Wallet.Service/         # DerivationPathBuilder moved out
├── 3 projects referencing Wallet.Core          # Transitive access via Core→Portable
├── 9 projects referencing ServiceClients       # Transitive access via SC→SC.Http
└── Test projects                               # Reference updates as needed
```

### Key Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Entity location | All entities move to Portable | POCO classes, no EF annotations — safe to extract |
| Service implementations | Stay in Wallet.Core | They depend on DbContext — not portable |
| Service interfaces | Move to Portable | Contracts are portable, mobile can mock them |
| Encryption providers | Stay in Wallet.Core | OS-specific (DPAPI, SecretService) |
| DerivationPathBuilder | Move to Portable | Pure logic, mobile needs client-side derivation |
| PeerServiceClient | Stays in ServiceClients | Hybrid HTTP+gRPC — not cleanly splittable |
| DID resolvers | Move to Http | KeyDid and SorchaDid are pure logic, WebDid uses HttpClient |
| Hub connection helper | New in Http | JWT auth + reconnect shared between web and mobile |
| Consumer project changes | None — transitive references | Core→Portable and SC→SC.Http provide transitive access |
