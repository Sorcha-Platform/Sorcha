# Implementation Plan: Auto-Register Participant & PlatformUser Provisioning

**Branch**: `077-participant-autolink-user-provisioning` | **Date**: 2026-03-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/077-participant-autolink-user-provisioning/spec.md`

## Summary

Close GAP-018 (wallet creation auto-registers participant and auto-links wallet) and GAP-019 (system admin can provision platform users in private organisations). Auto-link bypasses the challenge/verify signature flow for self-created wallets. Admin provisioning creates PlatformUser + UserIdentity + OrgMembership in a single call with optional password and email verification skip.

## Technical Context

**Language/Version**: C# 13 / .NET 10
**Primary Dependencies**: xUnit, FluentAssertions, Moq, FluentValidation, BCrypt.Net
**Storage**: PostgreSQL (Tenant Service EF Core), in-memory for tests
**Testing**: xUnit + FluentAssertions + Moq, WebApplicationFactory for endpoint tests
**Target Platform**: .NET 10 microservices
**Project Type**: Web (microservices)
**Performance Goals**: Auto-link adds <500ms to wallet creation; admin provisioning <1s
**Constraints**: Existing JWT infrastructure, existing participant service, existing NIST password policy
**Scale/Scope**: 2 services modified (Wallet, Tenant), 1 API Gateway route update, ~4 new test files

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | **PASS** | Wallet Service calls Tenant Service via service client. No coupling violations. |
| II. Security First | **PASS** | Auto-link only for self-created wallets. Admin endpoints SystemAdmin-only. Passwords NIST-compliant. Platform-wide uniqueness enforced. |
| III. API Documentation | **PASS** | New endpoints require WithSummary/WithDescription + XML docs. |
| IV. Testing Requirements | **PASS** | >85% coverage target. Unit + integration tests for all new code. |
| V. Code Quality | **PASS** | Async/await, DI, nullable enabled, C# 13. |
| VI. Blueprint Standards | **N/A** | No blueprint changes. |
| VII. Domain-Driven Design | **PASS** | Uses ubiquitous language: Participant, Wallet, Organisation. |
| VIII. Observability | **PASS** | Structured logging for auto-link success/failure. |

No violations.

## Project Structure

### Source Code

```text
# Wallet Service (auto-link trigger)
src/Services/Sorcha.Wallet.Service/
└── Endpoints/WalletEndpoints.cs          # Add post-creation auto-link call

# Tenant Service (participant + wallet link + admin provisioning)
src/Services/Sorcha.Tenant.Service/
├── Endpoints/
│   └── PlatformManagementEndpoints.cs    # Add provisioning + password reset endpoints
├── Services/
│   ├── ParticipantService.cs             # Add AutoLinkWalletAsync method
│   ├── WalletVerificationService.cs      # Add DirectLinkWalletAsync (bypass challenge)
│   └── PlatformUserProvisioningService.cs # New: admin user provisioning
├── Models/Dtos/
│   ├── AdminProvisionUserRequest.cs      # New request DTO
│   └── AdminProvisionUserResponse.cs     # New response DTO
└── Validators/
    └── AdminProvisionUserValidator.cs    # FluentValidation

# Service Clients
src/Common/Sorcha.ServiceClients/
└── Participant/IParticipantServiceClient.cs  # Add AutoLinkWalletAsync

# API Gateway
src/Services/Sorcha.ApiGateway/
└── appsettings.json                      # Add platform-users route

# Tests
tests/Sorcha.Tenant.Service.Tests/
├── Services/AutoLinkWalletTests.cs       # Unit tests for auto-link
├── Services/PlatformUserProvisioningTests.cs  # Unit tests for provisioning
├── Endpoints/PlatformUserEndpointTests.cs     # Integration tests
└── Endpoints/PasswordResetEndpointTests.cs    # Integration tests
```

## Phase Summary

| Phase | Focus | User Stories |
|-------|-------|-------------|
| 1: Setup | DTOs, validators, service interfaces | Foundation |
| 2: US1 | Auto-link wallet during creation | GAP-018 |
| 3: US2 | Admin user provisioning endpoint | GAP-019 |
| 4: US3 | Admin password reset endpoint | GAP-019 |
| 5: Polish | YARP routes, docs, regression | All |
