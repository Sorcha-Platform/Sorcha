# Implementation Plan: OpenID4VP Verifier Endpoint (HAIP)

**Branch**: `098-openid4vp-verifier` | **Date**: 2026-04-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/098-openid4vp-verifier/spec.md`

## Summary

Add OpenID4VP verifier endpoints to the existing `Sorcha.Haip.Service` (stood up by spec 097). HAIP wallets (GOV.UK Wallet, EUDI Wallet) submit presentations via `direct_post`. The verifier validates `x5c` chains (spec 096), KB-JWT proof of possession (spec 094), IETF/W3C credential status (spec 095), and matches disclosed claims against Blueprint presentation definitions (DIF Presentation Exchange 2.0). Blueprint actions trigger the flow via a new `PresentationSource` field on credential requirements; actions suspend in `AwaitingExternalPresentation` state and resume when the verifier records a result.

This is the final composition spec in the 093-098 HAIP series. Every earlier spec is a contributor: 093 (baseline verifier fix), 094 (cnf/KB-JWT/nested disclosure), 095 (dual status list consumer), 096 (x5c trust store), 097 (HAIP service + issuance). This spec wires them all together on the verification side.

## Technical Context

**Language/Version**: C# 13, .NET 10
**Primary Dependencies**: `Sorcha.ServiceClients` (Wallet, Blueprint, Tenant service clients), `Sorcha.Cryptography.SdJwt` (SD-JWT VC verification, KB-JWT validation), `Sorcha.ServiceDefaults` (Aspire, auth, rate limiting), `Sorcha.Haip.Service` (spec 097 — existing service extended, not replaced). No external OpenID4VP NuGet — HAIP 1.0 constrains the verifier surface narrowly enough for direct implementation.
**Storage**: Redis (presentation request state, nonce, signed request object cache — TTL-based expiry). No PostgreSQL — verification results are stored as Blueprint action state via existing workflow storage. No new EF entities.
**Testing**: xUnit, FluentAssertions, Moq. Extends existing `tests/Sorcha.Haip.Service.Tests/` from spec 097.
**Target Platform**: Linux server (Docker), net10.0
**Project Type**: Extension to existing microservice in monorepo. No new Aspire resource. No new Docker Compose entry. Reuses spec 097's port, Dockerfile, YARP route prefix.
**Performance Goals**: Authorization Request creation < 100ms P95, `direct_post` verification < 300ms P95 (includes x5c chain walk, KB-JWT verify, status check via HTTP, claim matching).
**Constraints**: `direct_post` response mode only (HAIP 1.0 MTI). `vc+sd-jwt` credential format only. DIF Presentation Exchange 2.0 for presentation definitions. Pre-authorized code flow not involved (that is issuance-side, spec 097).
**Scale/Scope**: ~12 new source files added to `Sorcha.Haip.Service`, 3 endpoints, ~70 tasks.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Microservices-First** | PASS. Extends `Sorcha.Haip.Service` (spec 097). No new service boundary — the verifier is the second role of the same HAIP boundary service. No upward dependencies; calls Wallet/Blueprint/Tenant via service clients. |
| **II. Security First** | PASS. Zero-trust boundary: every caller is an untrusted external wallet. Nonces are single-use, presentation requests expire with TTL, KB-JWT binds audience and nonce, `direct_post` endpoint is anonymous + rate-limited. No claim values leak into denial responses. |
| **III. API Documentation** | PASS. OpenAPI/Scalar on internal endpoints. HAIP endpoints follow OID4VP spec (self-documenting via signed Request Objects and DIF PE 2.0 presentation definitions). |
| **IV. Testing Requirements** | PASS. Extends existing test project. Unit tests for each endpoint handler. Integration tests for the full Authorization Request -> direct_post -> verification pipeline. Parity regression test against internal verifier path. |
| **V. Code Quality** | PASS. Standard C# conventions, nullable enabled, license headers. |
| **VI. Blueprint Creation Standards** | PASS. `PresentationSource` field on `CredentialRequirement` — additive, backward-compatible. Mirrors spec 097's `TargetAudience` pattern. |
| **VII. Domain-Driven Design** | PASS. New domain concepts: PresentationRequest, AuthorizationRequest, PresentationSubmission, VerificationResult. All scoped to the HAIP verifier bounded context. |
| **VIII. Observability by Default** | PASS. Aspire ServiceDefaults provides health checks, telemetry. Structured logging for each verification step. SignalR signal on result transitions. |

**Constitution gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/098-openid4vp-verifier/
├── spec.md              # Feature specification (complete)
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── contracts/           # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # /speckit.tasks output
```

### Source Code (repository root)

Extension to existing service and modifications to existing projects:

```text
src/
├── Apps/
│   ├── Sorcha.AppHost/
│   │   └── Program.cs                    # NO CHANGE — Sorcha.Haip.Service already registered by spec 097
│   └── ...
├── Services/
│   ├── Sorcha.Haip.Service/              # EXISTING (spec 097) — extended with verifier endpoints
│   │   ├── Endpoints/
│   │   │   ├── VerifierEndpoints.cs      # NEW — Authorization Request creation (internal), direct_post callback (public)
│   │   │   └── RequestObjectEndpoints.cs # NEW — request_uri serving (GET, public, returns signed JWT)
│   │   ├── Services/
│   │   │   ├── PresentationRequestManager.cs   # NEW — creates/stores/expires presentation requests
│   │   │   ├── HaipPresentationVerifier.cs     # NEW — orchestrates full vp_token verification pipeline
│   │   │   ├── PresentationDefinitionBuilder.cs # NEW — builds DIF PE 2.0 definitions from Blueprint requirements
│   │   │   └── RequestObjectSigner.cs          # NEW — signs Authorization Request Objects (x5c chain)
│   │   └── Models/
│   │       └── VerifierModels.cs         # NEW — PresentationRequest, AuthorizationRequest, PresentationSubmission, VerificationResult
│   ├── Sorcha.ApiGateway/
│   │   └── appsettings.json              # NO CHANGE — YARP route for /haip/* already covers verifier endpoints
│   └── Sorcha.Blueprint.Service/
│       └── Services/Implementation/
│           └── ActionExecutionService.cs  # CHANGE — route HAIP presentations via PresentationSource field
├── Common/
│   ├── Sorcha.Blueprint.Models/
│   │   └── Credentials/
│   │       └── CredentialRequirement.cs   # CHANGE — add PresentationSource field
│   ├── Sorcha.ServiceClients/             # CHANGE — add verifier methods to IHaipServiceClient
│   └── Sorcha.ServiceClients.Http/        # CHANGE — add verifier methods to HaipServiceClient

tests/
└── Sorcha.Haip.Service.Tests/            # EXISTING (spec 097) — extended with verifier tests
    ├── VerifierEndpointsTests.cs          # NEW
    ├── HaipPresentationVerifierTests.cs   # NEW
    ├── PresentationRequestManagerTests.cs # NEW
    ├── PresentationDefinitionBuilderTests.cs # NEW
    └── RequestObjectSignerTests.cs        # NEW
```

**Structure Decision**: No new service boundary. The HAIP verifier is the second role of the existing `Sorcha.Haip.Service` per spec 097's Phase 2 D1 Option A. The verifier shares the service's Docker container, port, health check, rate-limiting framework, and signing identity. The API Gateway's existing `/haip/*` YARP route covers all new verifier endpoints without configuration changes.

## Complexity Tracking

*No Constitution violations — this section intentionally empty.*
