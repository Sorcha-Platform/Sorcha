# Feature Specification: Codebase Cleanup & Consolidation

**Feature Branch**: `044-codebase-consolidation`
**Created**: 2026-02-27
**Status**: Draft
**Input**: Comprehensive codebase audit findings — duplication, redundancy, pattern anomalies, and dead code

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Consolidate Authorization Policies (Priority: P1)

A platform developer maintaining any of the 7 microservices should have a single place to update authorization policies (RequireAuthenticated, RequireService, RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator) rather than editing 6 separate `AuthenticationExtensions.cs` files with near-identical code.

Currently 3 policies are duplicated identically across all 6 services (RequireAuthenticated, RequireService, RequireOrganizationMember), 2 more are in 3-4 services (RequireAdministrator, RequireDelegatedAuthority), and CanWriteDockets is shared between Register and Validator (~300+ lines total). Note: Tenant Service implements RequireOrganizationMember and RequireDelegatedAuthority differently (RequireClaim-based vs RequireAssertion-based) — consolidation must use the stricter assertion-based version from the other 5 services. Each service also has unique policies (e.g., Blueprint's CanManageBlueprints, Register's CanSubmitTransactions) that must remain service-specific.

**Why this priority**: Authorization is security-critical infrastructure. 6 copies of the same policy logic creates risk of policy drift — updating one service but missing another. This is the highest-impact consolidation item.

**Independent Test**: Can be fully tested by verifying all 7 services start successfully with identical auth behavior and all existing auth-related tests pass.

**Acceptance Scenarios**:

1. **Given** the 4 common policies exist in a shared location, **When** a developer updates RequireService claim logic, **Then** all 6 services inherit the change without individual edits.
2. **Given** a service has unique policies (e.g., Blueprint's CanManageBlueprints, CanPublishBlueprints), **When** the shared policies are registered, **Then** service-specific policies are still independently configurable.
3. **Given** the consolidation is complete, **When** all existing tests run, **Then** zero regressions in auth behavior across all services.
4. **Given** the Validator Service currently uses `AddAuthorizationPolicies()` (inconsistent naming vs. other services), **When** the consolidation is complete, **Then** all services use a consistent naming pattern.
5. **Given** both Register and Validator services define identical `CanWriteDockets` policies, **When** the consolidation is complete, **Then** this shared policy exists once.

---

### User Story 2 — Consolidate Service Pipeline Infrastructure (Priority: P2)

A platform developer adding a new microservice should be able to configure the standard middleware pipeline (Serilog, rate limiting, input validation, security headers, HTTPS enforcement, CORS, OpenAPI/Scalar) with minimal boilerplate, rather than copying ~60 lines of pipeline setup from an existing service's Program.cs.

Currently all 7 services duplicate identical builder registrations and middleware ordering. This has already caused bugs: Tenant Service calls `UseRateLimiter()` twice, Wallet Service has `UseHttpsEnforcement()` in the wrong position, and middleware ordering is inconsistent across services.

**Why this priority**: Affects all 7 services and has already produced bugs. Consolidation prevents future ordering mistakes and makes new service creation trivial.

**Independent Test**: Can be tested by verifying all 7 services start successfully, Scalar API docs render, CORS headers appear on responses, rate limiting works, and the known bugs are fixed.

**Acceptance Scenarios**:

1. **Given** shared OpenAPI/Scalar configuration exists, **When** a service registers with a title and description, **Then** contact info (Sorcha Contributors), license (MIT), and Scalar theme are applied automatically.
2. **Given** shared CORS configuration exists, **When** a service opts in, **Then** the standard AllowAnyOrigin/Method/Header policy is applied without per-service boilerplate.
3. **Given** a standard middleware pipeline helper exists, **When** a service calls it, **Then** MapDefaultEndpoints, UseSerilogLogging, UseApiSecurityHeaders, UseHttpsEnforcement, UseInputValidation are applied in the correct order.
4. **Given** the Tenant Service currently calls `UseRateLimiter()` twice, **When** the pipeline is consolidated, **Then** the duplicate call is removed.
5. **Given** the Wallet Service currently applies `UseHttpsEnforcement()` after `UseInputValidation()`, **When** the pipeline is consolidated, **Then** the middleware order is corrected.
6. **Given** the ApiGateway has its own aggregated OpenAPI document, **When** shared OpenAPI helpers are introduced, **Then** the Gateway can opt out or extend without conflict.

---

### User Story 3 — Remove Orphaned CLI Endpoints and Dead Code (Priority: P2)

A developer using the Sorcha CLI should not encounter 404 errors when executing admin or credential commands. Currently 11 CLI endpoints reference backend APIs that either don't exist or have incorrect paths.

Specifically:
- `IAdminServiceClient`: `/api/schemas/sectors`, `/api/schemas/providers` (no backend endpoint), `/api/admin/alerts` (should be `/api/alerts`)
- `ICredentialServiceClient`: 7 credential endpoints using `/api/credentials/` but YARP routes to `/api/v1/credentials/` — paths to be fixed to match
- `ReadLine` v2.0.1 NuGet package: included but never referenced

**Why this priority**: Runtime 404 failures erode trust in the CLI tool and confuse developers. Dead code creates maintenance burden.

**Independent Test**: Can be tested by verifying all remaining CLI commands succeed against running services and no orphaned Refit interfaces reference non-existent endpoints.

**Acceptance Scenarios**:

1. **Given** `IAdminServiceClient` references `/api/schemas/sectors` and `/api/schemas/providers` with no backend, **When** cleanup is complete, **Then** these endpoints and their commands are removed.
2. **Given** `IAdminServiceClient` references `/api/admin/alerts`, **When** cleanup is complete, **Then** the path is corrected to `/api/alerts` (matching the Gateway endpoint).
3. **Given** `ICredentialServiceClient` uses `/api/credentials/` paths but YARP routes to `/api/v1/credentials/`, **When** cleanup is complete, **Then** the CLI paths are updated to `/api/v1/credentials/` to match the existing YARP routes.
4. **Given** the `ReadLine` v2.0.1 NuGet package is unused, **When** cleanup is complete, **Then** the package reference is removed from Sorcha.Cli.csproj.

---

### User Story 4 — Extract Shared MCP Server Models (Priority: P3)

An MCP Server tool developer should use a shared `ErrorResponse` model rather than defining a private copy in each tool file. Currently 18 tools each define an identical `private sealed class ErrorResponse { public string? Error { get; set; } }`.

**Why this priority**: Low severity but high file count — 18 identical private classes. Consolidation reduces noise and ensures consistent error response structure.

**Independent Test**: Can be tested by verifying all 18 MCP tools compile and return identical error JSON responses after consolidation.

**Acceptance Scenarios**:

1. **Given** 18 MCP tools each define private ErrorResponse, **When** consolidation is complete, **Then** a single shared ErrorResponse class exists and all tools reference it.
2. **Given** the shared model is created, **When** any MCP tool returns an error, **Then** the JSON response format is identical to the current behavior.

---

### User Story 5 — Merge Duplicated CreateWalletRequest DTO (Priority: P3)

The wallet creation request model should exist in one location, not separately in both the Wallet Service and UI Core with diverging properties. The Wallet Service version includes `PqcAlgorithm` and `EnableHybrid` (post-quantum cryptography support) which the UI version lacks.

**Why this priority**: Diverging DTOs mean the UI cannot leverage PQC features and the models will drift further apart over time.

**Independent Test**: Can be tested by verifying wallet creation works from both the UI and direct API calls after consolidation.

**Acceptance Scenarios**:

1. **Given** CreateWalletRequest exists in two locations with different properties, **When** consolidation is complete, **Then** a single definition exists that both projects reference.
2. **Given** the consolidated model includes PqcAlgorithm and EnableHybrid as optional fields, **When** the UI sends a request without these fields, **Then** they default gracefully (no breaking change).

---

### User Story 6 — Add Missing License Headers (Priority: P3)

All source files in the repository should include the required SPDX license header for MIT compliance. Currently ~168 files (~84% coverage) are missing the header, concentrated in Sorcha.Cli (~50 files) and Sorcha.Demo (~16 files).

**Why this priority**: License compliance is a legal requirement. While not functionally impactful, gaps create audit risk. This work is automatable.

**Independent Test**: Can be tested by running a search across all `.cs` files in `src/` and confirming 100% contain the SPDX header.

**Acceptance Scenarios**:

1. **Given** ~168 files are missing the SPDX header, **When** cleanup is complete, **Then** every `.cs` file in `src/` begins with `// SPDX-License-Identifier: MIT` and `// Copyright (c) 2026 Sorcha Contributors`.
2. **Given** headers are added, **When** the solution builds, **Then** no compilation errors are introduced.

---

### User Story 7 — Fix SignalR Hub Async Naming (Priority: P3)

SignalR hub methods that are async should follow the project's `Async` suffix convention, or the project should explicitly document SignalR hubs as a naming exception. Currently 6 async methods across EventsHub and ActionsHub omit the suffix: `Subscribe`, `Unsubscribe`, `SubscribeOrg`, `UnsubscribeOrg`, `SubscribeToWallet`, `UnsubscribeFromWallet`.

**Why this priority**: Low impact — only 6 methods across 2 hub files. SignalR convention typically omits the suffix for client-callable methods, so this may be an intentional pattern.

**Independent Test**: Can be tested by verifying SignalR client connections still work after any rename, or that the coding guidelines document the exception.

**Acceptance Scenarios**:

1. **Given** 6 SignalR hub methods lack the Async suffix, **When** the decision is made, **Then** either the methods are renamed with updated client-side references OR the project guidelines explicitly note SignalR hubs as a naming exception.

---

### Edge Cases

- What happens if a service's unique authorization policy name collides with a shared policy name?
- What happens if the corrected credential CLI paths still fail against the backend (endpoint contract mismatch beyond path)?
- What happens if the shared OpenAPI transformer is applied to the ApiGateway, which has its own aggregated OpenAPI document?
- What happens if renaming SignalR hub methods breaks existing client JavaScript in the Blazor UI?
- What happens if the consolidated CreateWalletRequest DTO is placed in a project that the UI currently doesn't reference?

## Requirements *(mandatory)*

### Functional Requirements

**Authorization Consolidation:**
- **FR-001**: System MUST provide shared authorization policy definitions for the common policies (RequireAuthenticated, RequireService, RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator) in a single shared location
- **FR-002**: System MUST allow each service to register additional service-specific policies alongside the shared ones
- **FR-003**: System MUST maintain identical authorization behavior after consolidation (zero regressions)
- **FR-004**: System MUST consolidate the duplicated `CanWriteDockets` policy (identical in Register and Validator services)

**Pipeline Consolidation:**
- **FR-005**: System MUST provide a shared OpenAPI document transformer that applies standard contact, license, and version metadata
- **FR-006**: System MUST provide a shared Scalar UI configuration helper with configurable title and theme
- **FR-007**: System MUST provide a shared CORS configuration that services can opt into
- **FR-008**: System MUST enforce a correct and consistent middleware ordering across services
- **FR-009**: System MUST fix the Tenant Service duplicate `UseRateLimiter()` registration
- **FR-010**: System MUST fix the Wallet Service middleware ordering (UseHttpsEnforcement before UseInputValidation)

**CLI Cleanup:**
- **FR-011**: System MUST remove CLI commands that reference non-existent backend endpoints (schema sectors, schema providers)
- **FR-012**: System MUST correct the AdminAlerts CLI endpoint path from `/api/admin/alerts` to `/api/alerts`
- **FR-013**: System MUST fix the credential command paths from `/api/credentials/` to `/api/v1/credentials/` to match existing YARP routes
- **FR-014**: System MUST remove the unused `ReadLine` NuGet package from Sorcha.Cli

**MCP Server:**
- **FR-015**: System MUST provide a single shared `ErrorResponse` model for all MCP tools

**DTO Consolidation:**
- **FR-016**: System MUST consolidate `CreateWalletRequest` into a single definition referenced by both Wallet Service and UI Core, with PQC fields as optional

**License Compliance:**
- **FR-017**: All `.cs` source files in `src/` MUST include the two-line SPDX license header

**Naming Compliance:**
- **FR-018**: SignalR hub async methods MUST either receive the `Async` suffix or be documented as a convention exception in project guidelines

### Key Entities

- **SharedAuthorizationPolicies**: Common policy definitions (RequireAuthenticated, RequireService, RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator) registered once and consumed by all services
- **ServicePipelineConfiguration**: Shared builder registrations and middleware ordering for OpenAPI, CORS, security headers, rate limiting, and input validation
- **ErrorResponse**: Shared MCP tool error model with `Error` property
- **CreateWalletRequest**: Unified wallet creation DTO with optional PQC fields (PqcAlgorithm, EnableHybrid)

## Clarifications

### Session 2026-02-27

- Q: Should orphaned CLI credential commands be removed or fixed? → A: Fix CLI paths — update `ICredentialServiceClient` from `/api/credentials/` to `/api/v1/credentials/` to match existing YARP routes (credential backend exists behind versioned routes).

## Assumptions

- RequireAuthenticated and RequireService are identical across all 6 services. RequireOrganizationMember differs in Tenant Service (RequireClaim vs RequireAssertion with non-empty check) — the shared version MUST use the stricter assertion-based implementation. RequireDelegatedAuthority differs in Tenant Service (RequireClaim vs RequireAssertion) — the shared version MUST use the assertion-based implementation. These changes may tighten Tenant Service authorization behavior slightly.
- Removing orphaned CLI commands will not break the System.CommandLine parser (commands can be removed independently)
- The `AllowAnyOrigin` CORS policy is acceptable for the current development stage; production CORS hardening is a separate concern
- SignalR hub methods conventionally omit the `Async` suffix because they are client-callable by name; documenting this as an exception is acceptable
- The Sorcha.Demo project requires license headers despite being a demo application
- The consolidated CreateWalletRequest can be placed in an existing shared project (e.g., ServiceClients) without circular dependency issues

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 7 microservices start successfully and pass their existing test suites with zero regressions
- **SC-002**: Common authorization policy code exists in exactly 1 location (down from 6 copies)
- **SC-003**: Each service's Program.cs pipeline boilerplate is reduced by at least 15 lines through shared helpers
- **SC-004**: Zero CLI commands reference non-existent backend endpoints
- **SC-005**: MCP ErrorResponse class exists in exactly 1 location (down from 18 copies)
- **SC-006**: CreateWalletRequest DTO exists in exactly 1 location (down from 2 copies)
- **SC-007**: 100% of `.cs` files in `src/` contain the SPDX license header (up from ~84%)
- **SC-008**: No duplicate middleware registrations exist (Tenant double rate limiter fixed)
- **SC-009**: Full solution builds and all 1,100+ tests pass after consolidation
