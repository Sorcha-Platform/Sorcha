# Implementation Plan: Passkey (WebAuthn/FIDO2) Authentication

**Branch**: `055-passkey-auth` | **Date**: 2026-03-10 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/055-passkey-auth/spec.md`

## Summary

Add passkey (WebAuthn/FIDO2) support to Sorcha using Fido2NetLib. Two use cases: (1) org users register passkeys as an alternative 2FA method alongside TOTP, (2) public users use passkeys or social login (Google, Microsoft, GitHub, Apple) as their primary authentication method. The implementation builds on existing infrastructure: Fido2.AspNet NuGet reference, PublicIdentity model, TokenService, OIDC exchange services, and distributed cache patterns.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Fido2.AspNet 4.0.0, ASP.NET Core Minimal APIs, Entity Framework Core, Redis (distributed cache)
**Storage**: PostgreSQL (TenantDbContext, public schema), Redis (challenge storage)
**Testing**: xUnit + FluentAssertions + Moq
**Target Platform**: Linux containers (Docker), Blazor WASM (browser)
**Project Type**: Web application (microservice backend + Blazor WASM frontend)
**Performance Goals**: Passkey sign-in < 5 seconds end-to-end, registration < 30 seconds
**Constraints**: WebAuthn requires secure context (HTTPS or localhost), RP ID must match deployment domain
**Scale/Scope**: Up to 10 passkey credentials per user, 4 social providers

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | All changes within Tenant Service boundary; UI changes via JS interop |
| II. Security First | PASS | Passkeys are phishing-resistant; challenge storage has TTL; rate limiting on all public endpoints |
| III. API Documentation | PASS | All endpoints will have XML docs + Scalar OpenAPI + `.WithSummary()` |
| IV. Testing Requirements | PASS | Unit tests for PasskeyService, integration tests for endpoints |
| V. Code Quality | PASS | Async/await, DI, nullable enabled, no warnings |
| VI. Blueprint Standards | N/A | Not a blueprint feature |
| VII. Domain-Driven Design | PASS | Follows existing terminology (Participant, Identity) |
| VIII. Observability | PASS | Structured logging for all auth events, health check unaffected |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/055-passkey-auth/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity definitions and relationships
├── quickstart.md        # Development quickstart guide
├── contracts/           # API contract definitions
│   └── passkey-api.md   # REST endpoint contracts
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 task breakdown (via /speckit.tasks)
```

### Source Code (repository root)

```text
src/Services/Sorcha.Tenant.Service/
├── Models/
│   ├── PasskeyCredential.cs        # NEW: WebAuthn credential entity
│   ├── SocialLoginLink.cs          # NEW: Social provider link entity
│   └── PublicIdentity.cs           # MODIFIED: Remove single-credential fields, add navigations
├── Services/
│   ├── PasskeyService.cs           # NEW: Fido2NetLib wrapper service
│   ├── IPasskeyService.cs          # NEW: Interface
│   ├── PublicUserService.cs        # NEW: Public user lifecycle (create, link, manage)
│   └── IPublicUserService.cs       # NEW: Interface
├── Endpoints/
│   ├── PasskeyEndpoints.cs         # NEW: /api/passkey/* (registration, credentials)
│   └── PublicAuthEndpoints.cs      # NEW: /api/auth/public/* (passkey signup, social login)
├── Data/
│   └── TenantDbContext.cs          # MODIFIED: Add PasskeyCredential, SocialLoginLink configs
├── Endpoints/
│   └── AuthEndpoints.cs            # MODIFIED: Add availableMethods to 2FA response, add /verify-passkey
└── Extensions/
    └── ServiceCollectionExtensions.cs  # MODIFIED: Register Fido2, PasskeyService, PublicUserService

src/Services/Sorcha.ApiGateway/
└── appsettings.json                # MODIFIED: Add YARP routes for passkey/public auth endpoints

src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/
├── wwwroot/js/
│   └── webauthn.js                 # NEW: JS interop for navigator.credentials API
├── Pages/
│   ├── Login.razor                 # MODIFIED: Add "Sign in with Passkey" button
│   └── PublicSignup.razor          # NEW: Public user registration page
└── Services/
    └── PasskeyInteropService.cs    # NEW: C# wrapper for JS WebAuthn calls

tests/Sorcha.Tenant.Service.Tests/
├── Services/
│   ├── PasskeyServiceTests.cs      # NEW: Unit tests for PasskeyService
│   └── PublicUserServiceTests.cs   # NEW: Unit tests for PublicUserService
└── Endpoints/
    ├── PasskeyEndpointTests.cs     # NEW: Integration tests for passkey endpoints
    └── PublicAuthEndpointTests.cs  # NEW: Integration tests for public auth endpoints
```

**Structure Decision**: Follows existing Tenant Service structure — models, services with interfaces, endpoints, DbContext configuration. No new projects needed.

## Generated Artifacts

| Artifact | Path | Status |
|----------|------|--------|
| Research | [research.md](./research.md) | Complete |
| Data Model | [data-model.md](./data-model.md) | Complete |
| API Contracts | [contracts/passkey-api.md](./contracts/passkey-api.md) | Complete |
| Quickstart | [quickstart.md](./quickstart.md) | Complete |
| Tasks | tasks.md | Pending (`/speckit.tasks`) |
