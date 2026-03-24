# Implementation Plan: Register TenantId Removal & Security Hardening

**Branch**: `067-register-security-hardening` | **Date**: 2026-03-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/067-register-security-hardening/spec.md`

## Summary

Remove the node-local `TenantId` property from the Register entity and replace it with proper JWT-based authorization and org-subscription-based access control. Add a `RegisterPurpose` enum (General/System) to classify registers. Harden register creation (require admin auth), queries (subscription-scoped), deletion (attestation-based), and SignalR notifications (register-scoped groups). Update UI wizard with purpose dropdown, CLI with `--purpose` flag, and comprehensive test coverage.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: .NET Aspire 13.2.0, MudBlazor 9.2.0, SignalR, MongoDB.Driver 3.7.1, System.CommandLine 2.0.5
**Storage**: MongoDB (Register entity, indexes), PostgreSQL (Tenant Service subscriptions via EF Core)
**Testing**: xUnit 3.2.2, FluentAssertions 8.9.0, Moq 4.20.72, bUnit 2.6.2
**Target Platform**: Linux containers (Docker), Windows dev, Blazor WASM client
**Project Type**: Distributed microservices (web + API + CLI)
**Performance Goals**: Subscription resolution adds <100ms to register queries
**Constraints**: Fail-closed on subscription service unavailability; zero information leakage across orgs
**Scale/Scope**: 7 services, ~15 source files modified, ~10 test files modified/created, 1 new service client

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Microservices-First | PASS | Register Service calls Tenant Service via HTTP client — no shared DB access. Dependencies flow downward. |
| II. Security First | PASS | This feature IS the security hardening. Zero trust: JWT-based auth, attestation-based authorization, fail-closed. |
| III. API Documentation | PASS | All modified endpoints will have updated XML docs and OpenAPI descriptions. |
| IV. Testing Requirements | PASS | Comprehensive test plan (US8) — unit, component, CLI, API tests. Target >85% coverage. |
| V. Code Quality | PASS | Async/await for HTTP calls, DI for service client, nullable enabled, no warnings. |
| VI. Blueprint Standards | N/A | No blueprint changes. |
| VII. Domain-Driven Design | PASS | Uses established ubiquitous language (Register, Attestation, Subscription). |
| VIII. Observability | PASS | Structured logging on all authorization decisions. Existing health checks unaffected. |

**Post-Design Re-check**: All gates still pass. New `ISubscriptionServiceClient` follows existing service client patterns. No new projects needed — changes fit within existing project structure.

## Project Structure

### Documentation (this feature)

```text
specs/067-register-security-hardening/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity changes and validation rules
├── quickstart.md        # Implementation order and key files
├── contracts/
│   └── register-api-changes.md  # API contract diffs
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Common/
│   ├── Sorcha.Register.Models/
│   │   ├── Enums/RegisterPurpose.cs           # NEW — Purpose enum
│   │   ├── Register.cs                         # MODIFY — add Purpose, remove TenantId
│   │   ├── RegisterControlRecord.cs            # MODIFY — remove TenantId
│   │   └── RegisterCreationModels.cs           # MODIFY — add Purpose, remove TenantId
│   └── Sorcha.ServiceClients/
│       └── Subscription/
│           └── SubscriptionServiceClient.cs    # NEW — service-to-service subscription queries
├── Core/
│   ├── Sorcha.Register.Core/
│   │   ├── Events/RegisterEvents.cs            # MODIFY — remove TenantId, add Purpose
│   │   └── Managers/RegisterManager.cs         # MODIFY — subscription filtering, attestation auth
│   └── Sorcha.Register.Storage.MongoDB/
│       └── MongoRegisterRepository.cs          # MODIFY — add Purpose index, remove TenantId index
├── Services/
│   └── Sorcha.Register.Service/
│       ├── Extensions/AuthenticationExtensions.cs  # MODIFY — tighten policies
│       ├── Hubs/RegisterHub.cs                     # MODIFY — register-scoped groups
│       ├── Services/
│       │   ├── RegisterCreationOrchestrator.cs     # MODIFY — Purpose flow, remove TenantId
│       │   ├── RegisterEventBridgeService.cs       # MODIFY — register-scoped routing
│       │   └── SystemRegisterBootstrapper.cs       # MODIFY — set Purpose=System
│       └── Program.cs                              # MODIFY — auth, filtering, remove tenantId param
├── Apps/
│   ├── Sorcha.UI/Sorcha.UI.Core/
│   │   └── Components/Registers/
│   │       └── CreateRegisterWizard.razor          # MODIFY — add Purpose dropdown
│   └── Sorcha.Cli/Commands/
│       └── RegisterCommands.cs                     # MODIFY — add --purpose, remove --tenant-id

tests/
├── Sorcha.Register.Models.Tests/               # MODIFY — RegisterPurpose tests
├── Sorcha.Register.Service.Tests/              # MODIFY — auth, filtering, deletion tests
├── Sorcha.UI.Core.Tests/                       # MODIFY — wizard Purpose dropdown tests
├── Sorcha.Cli.Tests/                           # MODIFY — --purpose option tests
└── Sorcha.ServiceClients.Tests/                # MODIFY — SubscriptionServiceClient tests
```

**Structure Decision**: No new projects. All changes fit within existing project structure. One new service client class in `Sorcha.ServiceClients`, one new enum in `Sorcha.Register.Models`.

## Complexity Tracking

No constitution violations to justify. All changes use existing patterns:
- Service client follows `Sorcha.ServiceClients` conventions
- Authorization policies follow `AuthorizationPolicyExtensions` patterns
- MongoDB index management follows existing `CreateIndexesAsync` pattern
- UI dropdown follows existing MudBlazor patterns in the wizard
- CLI option follows existing System.CommandLine patterns
