# Implementation Plan: Verifiable Credential Lifecycle & Presentations

**Branch**: `039-verifiable-presentations` | **Date**: 2026-02-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/039-verifiable-presentations/spec.md`
**Design**: [design document](../../docs/archive/plans/2026-02-21-verifiable-credentials-design.md)

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend Sorcha's existing VC foundation into a complete verifiable credential system with lifecycle management, W3C Bitstring Status List, OID4VP presentations with QR codes, multi-method DID resolution, card-based wallet UI, and cross-blueprint credential flows.

**Architecture:** Wallet-centric approach — the wallet owns credentials and presentations, the register serves as immutable audit trail and hosts canonical status lists. Extends existing Wallet Service, Blueprint Service, and Blueprint Engine. No new microservices.

**Tech Stack:** .NET 10 / C# 13, SD-JWT (existing Sorcha.Cryptography), MudBlazor, W3C Bitstring Status List v1.0, OID4VP, DID Core

## Summary

This feature upgrades Sorcha's 95%-complete VC foundation into a production-ready credential system. The existing SD-JWT cryptography, credential models, wallet storage (9 endpoints), and blueprint engine integration (CredentialVerifier + CredentialIssuer) provide the base. This plan adds:

1. **Credential lifecycle** — 5 states (Active/Suspended/Revoked/Expired/Consumed) with usage policies
2. **Bitstring Status List** — W3C standard for privacy-preserving revocation/suspension tracking
3. **OID4VP presentations** — Standard presentation protocol with QR code support for in-person verification
4. **DID resolution** — Pluggable resolver with `did:sorcha`, `did:web`, `did:key` implementations
5. **Wallet card UI** — Visual credential cards with issuer-defined styling
6. **Cross-blueprint flows** — Credentials issued by one blueprint required by another

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Sorcha.Cryptography (SD-JWT), MudBlazor 8.x, FluentValidation 11.10, JsonSchema.Net 7.4, QRCoder (QR generation), SimpleBase (multicodec for did:key)
**Storage**: PostgreSQL (wallet credentials via EF Core), MongoDB (register transactions), Redis (status list cache)
**Testing**: xUnit + FluentAssertions + Moq (unit), Playwright (E2E)
**Target Platform**: Web (Blazor WASM PWA) + .NET microservices (Docker/Aspire)
**Project Type**: Web application (distributed microservices + Blazor WASM frontend)
**Performance Goals**: Status list fetch <100ms (cached), OID4VP round-trip <5s (remote) / <8s (QR), DID resolution <5s (did:web) / <10ms (did:key)
**Constraints**: Status list minimum 131,072 entries (W3C privacy), QR encodes URL not credential, HTTPS-only for did:web
**Scale/Scope**: Extends 3 existing services (Wallet, Blueprint, Register) + UI. ~15 new files, ~10 modified files, ~20 new test files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Extends existing Wallet, Blueprint, Register services. No new services. DID resolver is a shared library in ServiceClients (appropriate for cross-service use). |
| II. Security First | PASS | Nonce-bound presentations prevent replay. Bitstring Status List preserves holder privacy. HTTPS-only for did:web. Authorization: original issuer + governance roles for lifecycle ops. Public status list endpoint — privacy via list size, not access control (W3C aligned). |
| III. API Documentation | PASS | All new endpoints documented with Scalar/OpenAPI. XML documentation on public interfaces. |
| IV. Testing Requirements | PASS | >85% coverage target for new code. xUnit + FluentAssertions + Moq. Contract tests for IDidResolver. Integration tests for status list and presentation flows. |
| V. Code Quality | PASS | C# 13, async/await, DI, nullable reference types enabled. No compiler warnings. |
| VI. Blueprint Standards | PASS | Credential config remains in JSON blueprint templates. UsagePolicy and DisplayConfig added to existing CredentialIssuanceConfig model. |
| VII. Domain-Driven Design | PASS | Uses ubiquitous language: Blueprint, Action, Participant, Disclosure. New terms: Credential, Presentation, StatusList, DidDocument — all domain concepts. |
| VIII. Observability | PASS | Structured logging for all credential operations (issuance, revocation, presentation). ActivitySource tracing for DID resolution. Health checks for status list cache. |

**Gate result: ALL PASS — proceed to Phase 0.**

## Project Structure

### Documentation (this feature)

```text
specs/039-verifiable-presentations/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── did-resolver.md
│   ├── status-list-endpoints.md
│   ├── presentation-endpoints.md
│   └── credential-lifecycle-endpoints.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
# Shared library (DID resolution)
src/Common/Sorcha.ServiceClients/
├── Did/
│   ├── IDidResolver.cs              # NEW: Pluggable resolver interface
│   ├── IDidResolverRegistry.cs      # NEW: Registry for method-specific resolvers
│   ├── DidResolverRegistry.cs       # NEW: Implementation
│   ├── SorchaDidResolver.cs         # NEW: did:sorcha resolution
│   ├── WebDidResolver.cs            # NEW: did:web resolution
│   ├── KeyDidResolver.cs            # NEW: did:key resolution
│   └── DidDocument.cs               # NEW: W3C DID Core model
└── Extensions/
    └── ServiceCollectionExtensions.cs  # MODIFY: Add DID resolver DI

# Credential models
src/Common/Sorcha.Blueprint.Models/Credentials/
├── CredentialIssuanceConfig.cs      # MODIFY: Add UsagePolicy, MaxPresentations, DisplayConfig
├── CredentialDisplayConfig.cs       # NEW: Issuer visual template
├── BitstringStatusList.cs           # NEW: Status list model
├── CredentialStatus.cs              # NEW: credentialStatus claim model
└── UsagePolicy.cs                   # NEW: Reusable/SingleUse/LimitedUse enum

# Wallet Service (presentations + lifecycle)
src/Services/Sorcha.Wallet.Service/
├── Endpoints/
│   ├── CredentialEndpoints.cs       # MODIFY: Add suspend/reinstate endpoints
│   └── PresentationEndpoints.cs     # NEW: OID4VP presentation endpoints
├── Services/
│   └── PresentationRequestService.cs # NEW: Presentation request management
└── Credentials/
    └── CredentialStore.cs           # MODIFY: Add Suspended/Consumed states

# Wallet Core (entity)
src/Common/Sorcha.Wallet.Core/Domain/Entities/
└── CredentialEntity.cs              # MODIFY: Add StatusListIndex, PresentationCount, UsagePolicy

# Blueprint Service (status list)
src/Services/Sorcha.Blueprint.Service/
├── Endpoints/
│   ├── CredentialEndpoints.cs       # MODIFY: Add suspend/reinstate endpoints
│   └── StatusListEndpoints.cs       # NEW: Public status list serving
└── Services/
    └── StatusListManager.cs         # NEW: Bitstring management + register storage

# Blueprint Engine (verification upgrades)
src/Core/Sorcha.Blueprint.Engine/Credentials/
├── CredentialVerifier.cs            # MODIFY: Add status list check, nonce, usage policy
├── CredentialIssuer.cs              # MODIFY: Add status list allocation, display config
└── BitstringStatusListChecker.cs    # NEW: IRevocationChecker implementation

# UI (credential cards + presentation)
src/Apps/Sorcha.UI/Sorcha.UI.Core/
├── Components/Credentials/
│   ├── CredentialCard.razor         # NEW: Visual card component
│   ├── CredentialCardList.razor     # NEW: Card list with filtering
│   ├── CredentialDetailView.razor   # NEW: Full detail + actions
│   ├── PresentationRequestDialog.razor # NEW: Approve/deny presentation
│   └── QrPresentationDisplay.razor  # NEW: QR code display for verifiers
├── Services/
│   ├── ICredentialApiService.cs     # NEW: Credential API client
│   ├── CredentialApiService.cs      # NEW: Implementation
│   └── QrPresentationService.cs     # NEW: QR code generation
└── Models/Credentials/
    ├── CredentialCardViewModel.cs   # NEW: Card display model
    └── PresentationRequestViewModel.cs # NEW: Request display model

# Pages
src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/
└── MyCredentials.razor              # NEW: Credential tab page

# Tests
tests/Sorcha.ServiceClients.Tests/Did/
├── SorchaDidResolverTests.cs        # NEW
├── WebDidResolverTests.cs           # NEW
├── KeyDidResolverTests.cs           # NEW
└── DidResolverRegistryTests.cs      # NEW

tests/Sorcha.Wallet.Service.Tests/
├── Credentials/CredentialLifecycleTests.cs  # NEW
└── Presentations/PresentationEndpointTests.cs # NEW

tests/Sorcha.Blueprint.Service.Tests/
├── StatusList/StatusListManagerTests.cs     # NEW
└── StatusList/StatusListEndpointTests.cs    # NEW

tests/Sorcha.Blueprint.Engine.Tests/Credentials/
├── StatusListCheckerTests.cs        # NEW
└── UsagePolicyVerificationTests.cs  # NEW

tests/Sorcha.Blueprint.Models.Tests/Credentials/
├── BitstringStatusListTests.cs      # NEW
└── UsagePolicyTests.cs              # NEW

tests/Sorcha.UI.Core.Tests/Credentials/
├── CredentialCardTests.cs           # NEW
└── CredentialApiServiceTests.cs     # NEW
```

**Structure Decision**: Extends the existing monorepo microservices architecture. DID resolution lives in ServiceClients (shared library) because it's needed by multiple services. No new microservices — Wallet Service handles presentations, Blueprint Service handles status lists, matching their existing responsibilities.

## Complexity Tracking

No constitution violations to justify — all gates pass.
