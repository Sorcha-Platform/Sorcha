# Implementation Plan: Platform Organisation Topology

**Branch**: `058-platform-org-topology` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/058-platform-org-topology/spec.md`
**Design Spec**: `docs/superpowers/specs/2026-03-16-platform-org-topology-design.md`

## Summary

Transform Sorcha from a single-org bootstrap model to a three-tier organisation topology: system admin org, public org (social login + email/password signup), and private orgs (created via blueprint or admin invite). Introduces `PlatformUser` as a cross-org identity anchor in the public schema, replacing `PublicIdentity`. Authentication happens at the platform level; authorisation is scoped per-org via `UserIdentity`. Key capabilities: social login (Google, GitHub, Microsoft, Apple), email/password signup with verification, blueprint-driven self-service org creation, org switching, and platform governance (audit access, org suspension).

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: Entity Framework Core, ASP.NET Minimal APIs, YARP 2.2, JWT Bearer, .NET Aspire 13, FluentValidation 11.10, Scalar 2.10
**Storage**: PostgreSQL (EF Core, public + per-org schemas), Redis (caching, session), MongoDB (register storage)
**Testing**: xUnit + FluentAssertions + Moq (1,100+ tests across 30 projects)
**Target Platform**: Docker containers / .NET Aspire orchestration
**Project Type**: Web (microservices — backend services + Blazor WASM frontend)
**Performance Goals**: Social login <30s, org switching <2s (from spec SC-001, SC-005)
**Constraints**: Atomic org provisioning (zero orphaned state on failure), platform-wide email uniqueness, max 4 simultaneous social providers
**Scale/Scope**: Multi-org (1 system admin + 1 public + N private), changes span Tenant Service (primary), API Gateway, Admin UI, Main UI, Bootstrap, Blueprint seeding

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Changes primarily in Tenant Service. Platform API endpoints go through API Gateway. No upward dependencies. |
| II. Security First | PASS | Social login via OAuth2/OIDC with PKCE. Passwords BCrypt hashed. Client secrets AES-256-GCM encrypted. Input validation on all boundaries. SystemAdmin role constraint enforced. |
| III. API Documentation | PASS | All new endpoints use Minimal APIs with `.WithSummary()` and `.WithDescription()`. Scalar UI. OpenAPI contract in `contracts/platform-api.yaml`. |
| IV. Testing Requirements | PASS | >85% coverage target for new code. Unit tests for all services, integration tests for endpoints, E2E tests for UI flows. |
| V. Code Quality | PASS | C# 13, async/await, DI, nullable reference types. License headers required. |
| VI. Blueprint Standards | PASS | "Create Organisation" blueprint as JSON template seeded from catalog. |
| VII. Domain-Driven Design | PASS | Rich domain models (PlatformUser, PlatformSocialLogin). Ubiquitous language (Organisation, Blueprint, Participant). |
| VIII. Observability | PASS | Structured logging, health checks on new endpoints, OpenTelemetry traces for auth flows. |

**Post-Phase 1 Re-check**: All gates remain PASS. No violations required.

## Project Structure

### Documentation (this feature)

```text
specs/058-platform-org-topology/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 research findings
├── data-model.md        # Phase 1 entity definitions
├── quickstart.md        # Phase 1 getting-started guide
├── contracts/
│   └── platform-api.yaml # OpenAPI contract for new endpoints
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Services/
│   ├── Sorcha.Tenant.Service/          # PRIMARY — most changes here
│   │   ├── Models/
│   │   │   ├── PlatformUser.cs         # NEW — platform identity anchor
│   │   │   ├── PlatformSocialLogin.cs  # NEW — multi-provider social links
│   │   │   ├── PlatformUserOrgMembership.cs # NEW — denormalized org lookup
│   │   │   ├── PlatformSettings.cs     # NEW — platform config singleton
│   │   │   ├── PlatformUserStatus.cs   # NEW — enum
│   │   │   ├── UserIdentity.cs         # MODIFY — add PlatformUserId, remove auth fields
│   │   │   ├── Organization.cs         # MODIFY — add IsPlatformOrg, change IDP nav
│   │   │   ├── PasskeyCredential.cs    # MODIFY — reparent to PlatformUserId
│   │   │   ├── IdentityProviderConfiguration.cs # MODIFY — add GitHub enum value
│   │   │   ├── ProvisioningMethod.cs   # MODIFY — add AdminCreated
│   │   │   ├── PublicIdentity.cs       # DELETE
│   │   │   ├── SocialLoginLink.cs      # DELETE
│   │   │   └── OwnerTypes.cs           # DELETE
│   │   ├── Endpoints/
│   │   │   ├── PlatformSettingsEndpoints.cs  # NEW — platform config API
│   │   │   ├── PlatformOrgEndpoints.cs       # NEW — org management API
│   │   │   ├── AuthEndpoints.cs        # MODIFY — social login, register, switch-org
│   │   │   ├── BootstrapEndpoints.cs   # MODIFY — create both orgs + PlatformUser
│   │   │   └── PublicAuthEndpoints.cs  # DELETE
│   │   ├── Services/
│   │   │   ├── IPlatformUserService.cs # NEW
│   │   │   ├── PlatformUserService.cs  # NEW
│   │   │   ├── IPlatformSettingsService.cs # NEW
│   │   │   ├── PlatformSettingsService.cs  # NEW
│   │   │   ├── TokenService.cs         # MODIFY — add platform_user_id claim, merge public token
│   │   │   ├── IPublicUserService.cs   # DELETE
│   │   │   └── PublicUserService.cs    # DELETE
│   │   └── Data/
│   │       └── DatabaseInitializer.cs  # MODIFY — seed both orgs, PlatformSettings
│   ├── Sorcha.ApiGateway/
│   │   └── appsettings.json            # MODIFY — add /api/platform/* routes
│   └── Sorcha.Blueprint.Service/
│       └── (blueprint template seeding) # MODIFY — seed Create Organisation blueprint
├── Apps/
│   ├── Sorcha.Admin/
│   │   └── Sorcha.Admin.Client/        # MODIFY — Platform Settings page, Platform Orgs page
│   └── Sorcha.UI/
│       └── Sorcha.UI.Web.Client/       # MODIFY — social login UI, org switcher, create org page
└── Common/
    ├── Sorcha.Tenant.Models/           # MODIFY — if shared models exist here
    └── Sorcha.ServiceDefaults/
        └── AuthorizationPolicyExtensions.cs # MODIFY — add RequirePlatformAuditor policy

tests/
├── Sorcha.Tenant.Service.Tests/        # MODIFY — PlatformUser, social login, org switching tests
├── Sorcha.Tenant.Service.IntegrationTests/ # MODIFY — bootstrap, platform API integration tests
├── Sorcha.ApiGateway.Tests/            # MODIFY — new route tests
└── Sorcha.UI.E2E.Tests/               # MODIFY — social login, org switcher E2E tests
```

**Structure Decision**: Existing microservice architecture. All backend changes primarily in Tenant Service (identity, auth, platform management). API Gateway gets new routes. UI projects get new pages/components. No new projects created — follows existing patterns.

## Complexity Tracking

> No constitution violations. All changes fit within existing project structure and patterns.

| Aspect | Complexity | Justification |
|--------|-----------|---------------|
| PlatformUser entity | Medium | Replaces PublicIdentity — net entity count unchanged |
| IDP one-to-many change | Medium | Schema change affecting existing code referencing Organization.IdentityProvider |
| Bootstrap changes | Medium | Must create two orgs atomically + PlatformSettings |
| Social login flow | High | OAuth2/OIDC flows with PlatformUser resolution — extends existing OIDC infrastructure |
| Org switching | Medium | New JWT re-issuance flow + PlatformUserOrgMembership queries |
| Blueprint seeding | Low | Follows existing blueprint template patterns |
| UI changes | High | Multiple new pages (Platform Settings, Platform Orgs, social login, org switcher, create org) |
