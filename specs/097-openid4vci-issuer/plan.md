# Implementation Plan: OpenID4VCI Issuer Endpoint (HAIP)

**Branch**: `097-openid4vci-issuer` | **Date**: 2026-04-10 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/097-openid4vci-issuer/spec.md`

## Summary

Stand up a new boundary service `Sorcha.Haip.Service` hosting the OpenID4VCI issuance protocol for external HAIP wallets (GOV.UK Wallet, EUDI Wallet). The service is a thin orchestrator: HAIP on the outside, Sorcha service clients on the inside. Implements the pre-authorized code grant flow (HAIP 1.0 MTI), issuer metadata endpoints, nonce endpoint, token endpoint, and credential endpoint. Blueprint actions trigger Credential Offers via a new `TargetAudience` field on `CredentialIssuanceConfig`.

Consumes all four Wave 2 primitives:
- spec 094: `cnf` binding, KB-JWT, nested disclosure, classical co-key
- spec 095: IETF `status.status_list` claim form
- spec 096: `x5c` chain from tenant root CA

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `Sorcha.ServiceClients` (Wallet, Blueprint, Tenant service clients), `Sorcha.Cryptography.SdJwt` (SD-JWT VC creation), `Sorcha.ServiceDefaults` (Aspire, auth, rate limiting). No external OpenID4VCI NuGet — HAIP 1.0 is narrow enough to implement directly against the spec.
**Storage**: Redis (nonce + pre-authorized code + access token store, TTL-based expiry). No PostgreSQL — the HAIP service is stateless except for transient OAuth state.
**Testing**: xUnit, FluentAssertions, Moq. New test project `tests/Sorcha.Haip.Service.Tests/`.
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: New microservice in existing monorepo. New Aspire resource. New Docker Compose entry. New YARP route.
**Performance Goals**: Token endpoint < 50ms P95, credential endpoint < 200ms P95 (includes SD-JWT signing via Wallet Service).
**Constraints**: Pre-authorized code grant only (no browser redirect auth code flow). HAIP 1.0 MTI conformance. Credential format: `vc+sd-jwt` only.
**Scale/Scope**: New service (~15 source files), 4 endpoints, 1 metadata endpoint, ~80 tasks.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | PASS. New independent service `Sorcha.Haip.Service`. No upward dependencies — calls into Wallet/Blueprint/Tenant via service clients. |
| **II. Security First** | PASS. Zero trust posture: every caller is untrusted external. JWT proof of possession required. Pre-auth codes are one-time-use with TTL. Nonces are single-use. |
| **III. API Documentation** | PASS. OpenAPI/Scalar on internal endpoints. HAIP endpoints follow OpenID4VCI spec (self-documenting via `.well-known` metadata). |
| **IV. Testing Requirements** | PASS. New test project. Unit tests for each endpoint handler. Integration tests for the pre-auth code → token → credential flow. |
| **V. Code Quality** | PASS. Standard C# conventions, nullable enabled. |
| **VI. Blueprint Creation Standards** | PASS. `TargetAudience` field on `CredentialIssuanceConfig` — additive, backward-compatible. |
| **VII. Domain-Driven Design** | PASS. New domain concepts: CredentialOffer, PreAuthorizedCode, HaipAccessToken, NonceStore. |
| **VIII. Observability by Default** | PASS. Aspire ServiceDefaults provides health checks, telemetry. Structured logging for each HAIP flow step. |

**Constitution gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/097-openid4vci-issuer/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # /speckit.tasks output
```

### Source Code (repository root)

New service and modifications to existing projects:

```text
src/
├── Apps/
│   ├── Sorcha.AppHost/
│   │   └── Program.cs                    # CHANGE — add Sorcha.Haip.Service resource
│   └── ...
├── Services/
│   ├── Sorcha.Haip.Service/              # NEW — entire service
│   │   ├── Sorcha.Haip.Service.csproj    # NEW
│   │   ├── Program.cs                    # NEW — entry point
│   │   ├── appsettings.json              # NEW — HAIP config
│   │   ├── Dockerfile                    # NEW
│   │   ├── Endpoints/
│   │   │   ├── MetadataEndpoints.cs      # NEW — .well-known/openid-credential-issuer
│   │   │   ├── TokenEndpoints.cs         # NEW — OAuth 2.0 token endpoint
│   │   │   ├── CredentialEndpoints.cs    # NEW — Credential issuance endpoint
│   │   │   └── NonceEndpoints.cs         # NEW — c_nonce endpoint
│   │   ├── Services/
│   │   │   ├── CredentialOfferService.cs  # NEW — creates and stores offers
│   │   │   ├── PreAuthCodeStore.cs       # NEW — Redis-backed one-time codes
│   │   │   ├── NonceStore.cs             # NEW — Redis-backed c_nonce management
│   │   │   ├── HaipCredentialMinter.cs   # NEW — orchestrates SD-JWT VC creation
│   │   │   └── JwtProofValidator.cs      # NEW — validates wallet JWT proof
│   │   └── Models/
│   │       ├── CredentialOffer.cs         # NEW
│   │       ├── TokenRequest.cs           # NEW
│   │       ├── TokenResponse.cs          # NEW
│   │       ├── CredentialRequest.cs       # NEW
│   │       └── IssuerMetadata.cs         # NEW
│   ├── Sorcha.ApiGateway/
│   │   └── appsettings.json              # CHANGE — add YARP route for /haip/*
│   └── Sorcha.Blueprint.Service/
│       └── Services/Implementation/
│           └── ActionExecutionService.cs  # CHANGE — route HAIP-path issuance
├── Common/
│   ├── Sorcha.Blueprint.Models/
│   │   └── Credentials/
│   │       └── CredentialIssuanceConfig.cs # CHANGE — add TargetAudience field
│   ├── Sorcha.ServiceClients/             # CHANGE — add IHaipServiceClient
│   └── Sorcha.ServiceClients.Http/        # CHANGE — add HaipServiceClient

docker-compose.yml                         # CHANGE — add haip-service container
```

**Structure Decision**: New service boundary per Phase 2 D1 Option A. The HAIP service is independently deployable, shares no database with other services, and communicates via HTTP service clients only.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
