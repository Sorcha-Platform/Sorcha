# Tasks: Codebase Cleanup & Consolidation

**Input**: Design documents from `/specs/044-codebase-consolidation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not explicitly requested. Existing 1,100+ tests serve as regression suite.

**Organization**: Tasks grouped by user story. All user stories are fully independent — no cross-story dependencies.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: No setup required — this feature uses existing projects only (ServiceDefaults, ServiceClients, McpServer). No new projects, no new dependencies to install.

*Phase 1 is empty — proceed directly to user stories.*

---

## Phase 2: Foundational

**Purpose**: No foundational blocking work — all 7 user stories are independent and can start immediately.

*Phase 2 is empty — all stories can start in parallel.*

---

## Phase 3: User Story 1 — Consolidate Authorization Policies (Priority: P1)

**Goal**: Extract 6 shared authorization policies from 6 duplicated service files into a single `AuthorizationPolicyExtensions.cs` in ServiceDefaults

**Independent Test**: `dotnet build` succeeds for all services + `dotnet test` passes on all service test projects with zero regressions

### Implementation for User Story 1

- [x] T001 [US1] Create shared authorization policy extension in `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs` — define `AddSorchaAuthorizationPolicies()` with 6 shared policies (RequireAuthenticated, RequireService, RequireOrganizationMember, RequireDelegatedAuthority, RequireAdministrator, CanWriteDockets) using `TBuilder : IHostApplicationBuilder` generic constraint in `namespace Microsoft.Extensions.Hosting` — include XML `<summary>` documentation on `AddSorchaAuthorizationPolicies()` method
- [x] T002 [US1] Update Blueprint Service auth in `src/Services/Sorcha.Blueprint.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, keep only CanManageBlueprints, CanExecuteBlueprints, CanPublishBlueprints as service-specific
- [x] T003 [P] [US1] Update Register Service auth in `src/Services/Sorcha.Register.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, keep only CanManageRegisters, CanSubmitTransactions, CanReadTransactions as service-specific
- [x] T004 [P] [US1] Update Wallet Service auth in `src/Services/Sorcha.Wallet.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, keep only CanManageWallets, CanUseWallet as service-specific
- [x] T005 [P] [US1] Update Validator Service auth in `src/Services/Sorcha.Validator.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, rename method from `AddAuthorizationPolicies()` to `AddValidatorAuthorization()` for consistency, keep only CanValidateChains as service-specific
- [x] T006 [P] [US1] Update Peer Service auth in `src/Services/Sorcha.Peer.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, keep only CanManagePeers as service-specific
- [x] T007 [P] [US1] Update Tenant Service auth in `src/Services/Sorcha.Tenant.Service/Extensions/AuthenticationExtensions.cs` — call `AddSorchaAuthorizationPolicies()`, keep JwtConfiguration class, ConfigureJwtForTokenIssuance(), and service-specific policies (RequireAuditor, RequirePublicUser, CanCreateBlockchain, CanPublishBlueprint) — NOTE: Tenant Service's RequireOrganizationMember and RequireDelegatedAuthority have different implementations than other services. After switching to shared policies, verify Tenant auth behavior is preserved (the shared assertion-based version is stricter than Tenant's current RequireClaim-based version)
- [x] T008 [US1] Verify auth consolidation — run `dotnet build` on all 7 service projects and `dotnet test` on all service test projects
- [x] T008b [US1] Write unit tests for AuthorizationPolicyExtensions in `tests/Sorcha.ServiceDefaults.Tests/AuthorizationPolicyExtensionsTests.cs` — verify all 6 shared policies are registered by name, verify RequireAuthenticated requires authenticated user, verify RequireService requires service token type claim, verify RequireOrganizationMember requires non-empty OrgId

**Checkpoint**: All 7 services use shared auth policies. Service-specific policies remain independently configurable. Zero regressions.

---

## Phase 4: User Story 2 — Consolidate Service Pipeline Infrastructure (Priority: P2)

**Goal**: Extract OpenAPI/Scalar, CORS configuration into ServiceDefaults helpers; fix middleware ordering bugs

**Independent Test**: All 7 services start, Scalar UIs render, CORS headers appear, no duplicate middleware calls

### Implementation for User Story 2

- [x] T009 [P] [US2] Create OpenAPI extensions in `src/Common/Sorcha.ServiceDefaults/OpenApiExtensions.cs` — define `AddSorchaOpenApi(string title, string description)` with standard document transformer (contact: Sorcha Contributors, license: MIT, version: 1.0.0) and `MapSorchaOpenApiUi(string title, ScalarTheme theme = Purple)` for Scalar UI mapping in development — include XML `<summary>` documentation on `AddSorchaOpenApi()` and `MapSorchaOpenApiUi()` methods
- [x] T010 [P] [US2] Create CORS extensions in `src/Common/Sorcha.ServiceDefaults/CorsExtensions.cs` — define `AddSorchaCors()` with AllowAnyOrigin/AllowAnyMethod/AllowAnyHeader default policy — include XML `<summary>` documentation on `AddSorchaCors()` method
- [x] T011 [US2] Update Blueprint Service pipeline in `src/Services/Sorcha.Blueprint.Service/Program.cs` — use `AddSorchaOpenApi()`, `MapSorchaOpenApiUi()`, `AddSorchaCors()`, remove inline OpenAPI/CORS blocks
- [x] T012 [P] [US2] Update Register Service pipeline in `src/Services/Sorcha.Register.Service/Program.cs` — use shared OpenAPI/Scalar/CORS helpers, remove inline blocks
- [x] T013 [P] [US2] Update Wallet Service pipeline in `src/Services/Sorcha.Wallet.Service/Program.cs` — use shared helpers, fix `UseHttpsEnforcement()` ordering (move before `UseInputValidation()`)
- [x] T014 [P] [US2] Update Validator Service pipeline in `src/Services/Sorcha.Validator.Service/Program.cs` — use shared OpenAPI/Scalar helpers (no CORS — intentional for internal service)
- [x] T015 [P] [US2] Update Peer Service pipeline in `src/Services/Sorcha.Peer.Service/Program.cs` — use shared OpenAPI/Scalar helpers (no CORS — intentional for internal service)
- [x] T016 [P] [US2] Update Tenant Service pipeline in `src/Services/Sorcha.Tenant.Service/Program.cs` — use shared helpers, remove duplicate `UseRateLimiter()` call, keep custom rate limiting policies
- [x] T017 [P] [US2] Update ApiGateway pipeline in `src/Services/Sorcha.ApiGateway/Program.cs` — use `AddSorchaCors()`, keep existing aggregated OpenAPI document, use shared Scalar helper for base config
- [x] T018 [US2] Verify pipeline consolidation — run `dotnet build` on all 7 service projects
- [x] T018b [US2] Write unit tests for OpenApiExtensions and CorsExtensions in `tests/Sorcha.ServiceDefaults.Tests/` — verify AddSorchaOpenApi registers OpenAPI with correct contact/license metadata, verify AddSorchaCors registers AllowAnyOrigin policy, verify MapSorchaOpenApiUi maps Scalar endpoint in development

**Checkpoint**: All services use shared OpenAPI/CORS/Scalar helpers. Tenant double rate limiter fixed. Wallet middleware ordering fixed.

---

## Phase 5: User Story 3 — Remove Orphaned CLI Endpoints and Dead Code (Priority: P2)

**Goal**: Fix or remove all CLI commands that reference non-existent backend endpoints; remove unused package

**Independent Test**: `dotnet build` on Sorcha.Cli succeeds; no Refit interfaces reference non-existent endpoints

### Implementation for User Story 3

- [x] T019 [P] [US3] Fix admin alert path in `src/Apps/Sorcha.Cli/Services/IAdminServiceClient.cs` — change `/api/admin/alerts` to `/api/alerts`
- [x] T020 [P] [US3] Remove orphaned schema endpoints from `src/Apps/Sorcha.Cli/Services/IAdminServiceClient.cs` — remove `ListSchemaSectorsAsync()` and `ListSchemaProvidersAsync()` methods
- [x] T021 [US3] Remove orphaned schema commands from `src/Apps/Sorcha.Cli/Commands/AdminCommands.cs` — remove `AdminSchemaSectorsCommand` and `AdminSchemaProvidersCommand` classes and their registration
- [x] T022 [P] [US3] Fix credential endpoint paths in `src/Apps/Sorcha.Cli/Services/ICredentialServiceClient.cs` — update all 7 paths from `/api/credentials/` to `/api/v1/credentials/`
- [x] T023 [P] [US3] Remove unused ReadLine package from `src/Apps/Sorcha.Cli/Sorcha.Cli.csproj` — delete `<PackageReference Include="ReadLine" Version="2.0.1" />`
- [x] T024 [US3] Verify CLI cleanup — run `dotnet build` on Sorcha.Cli project

**Checkpoint**: All CLI commands reference valid backend endpoints. No orphaned Refit interfaces. ReadLine removed.

---

## Phase 6: User Story 4 — Extract Shared MCP Server Models (Priority: P3)

**Goal**: Replace 18 identical private `ErrorResponse` classes with a single shared model

**Independent Test**: `dotnet build` on Sorcha.McpServer succeeds; all tools compile

### Implementation for User Story 4

- [x] T025 [US4] Create shared ErrorResponse model in `src/Apps/Sorcha.McpServer/Infrastructure/Models/ErrorResponse.cs` — public sealed class with `string? Error` property in namespace `Sorcha.McpServer.Infrastructure.Models`
- [x] T026 [P] [US4] Update Admin MCP tools — remove private ErrorResponse from `TenantCreateTool.cs`, `TenantUpdateTool.cs`, `TokenRevokeTool.cs`, `UserManageTool.cs` in `src/Apps/Sorcha.McpServer/Tools/Admin/`, add using for shared model
- [x] T027 [P] [US4] Update Designer MCP tools — remove private ErrorResponse from `BlueprintDiffTool.cs`, `BlueprintExportTool.cs`, `BlueprintSimulateTool.cs`, `BlueprintUpdateTool.cs`, `BlueprintValidateTool.cs`, `DisclosureAnalysisTool.cs`, `WorkflowInstancesTool.cs` in `src/Apps/Sorcha.McpServer/Tools/Designer/`, add using for shared model
- [x] T028 [P] [US4] Update Participant MCP tools — remove private ErrorResponse from `ActionDetailsTool.cs`, `ActionSubmitTool.cs`, `DisclosedDataTool.cs`, `RegisterQueryTool.cs`, `WalletInfoTool.cs`, `WalletSignTool.cs`, `WorkflowStatusTool.cs` in `src/Apps/Sorcha.McpServer/Tools/Participant/`, add using for shared model
- [x] T029 [US4] Verify MCP consolidation — run `dotnet build` on Sorcha.McpServer project

**Checkpoint**: ErrorResponse exists in exactly 1 location. All 18 tools reference the shared model.

---

## Phase 7: User Story 5 — Merge Duplicated CreateWalletRequest DTO (Priority: P3)

**Goal**: Consolidate CreateWalletRequest into ServiceClients with PQC fields as optional

**Independent Test**: `dotnet build` on Wallet.Service, UI.Core, and ServiceClients succeeds

### Implementation for User Story 5

- [x] T030 [US5] Create consolidated CreateWalletRequest in `src/Common/Sorcha.ServiceClients/Wallet/Models/CreateWalletRequest.cs` — include all fields (Name, Algorithm, WordCount, Passphrase, PqcAlgorithm, EnableHybrid, Tags) with validation attributes, PQC fields optional
- [x] T031 [US5] Add ServiceClients project reference to `src/Services/Sorcha.Wallet.Service/Sorcha.Wallet.Service.csproj` and `src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj`
- [x] T032 [US5] Update Wallet Service to use shared DTO — update usings in files that reference `Sorcha.Wallet.Service.Models.CreateWalletRequest`, delete `src/Services/Sorcha.Wallet.Service/Models/CreateWalletRequest.cs`
- [x] T033 [US5] Update UI Core to use shared DTO — update usings in files that reference `Sorcha.UI.Core.Models.Wallet.CreateWalletRequest`, delete `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Wallet/CreateWalletRequest.cs`
- [x] T034 [US5] Verify DTO consolidation — run `dotnet build` on ServiceClients, Wallet.Service, UI.Core, and UI.Web.Client projects

**Checkpoint**: CreateWalletRequest exists in exactly 1 location. Both consumers reference the shared model.

---

## Phase 8: User Story 6 — Add Missing License Headers (Priority: P3)

**Goal**: Add SPDX license header to all ~168 files missing it, achieving 100% coverage

**Independent Test**: `grep -rL "SPDX-License-Identifier" src/**/*.cs` returns zero results; `dotnet build` succeeds

### Implementation for User Story 6

- [x] T035 [US6] Write PowerShell header script — create a script that scans all `src/**/*.cs` files, checks if first line contains `// SPDX-License-Identifier: MIT`, and prepends the two-line header if missing (idempotent)
- [x] T036 [US6] Run header script across entire `src/` directory — execute the script, verify ~168 files are updated
- [x] T037 [US6] Verify license headers — run `dotnet build` on full solution to confirm no compilation errors from added headers

**Checkpoint**: 100% of `.cs` files in `src/` have the SPDX license header.

---

## Phase 9: User Story 7 — Fix SignalR Hub Async Naming (Priority: P3)

**Goal**: Document SignalR hub methods as a naming convention exception

**Independent Test**: CLAUDE.md contains the documented exception

### Implementation for User Story 7

- [x] T038 [US7] Document SignalR naming exception in `CLAUDE.md` — add a note under Development Guidelines / Code Naming section that SignalR hub methods (client-callable by name) are exempt from the `Async` suffix convention per industry standard

**Checkpoint**: CLAUDE.md documents the exception. No code changes needed.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final verification across all user stories

- [x] T039 Run full solution build — `dotnet build` across entire Sorcha.sln — PASS (0 errors, 83 pre-existing warnings)
- [x] T040 Run full test suite — `dotnet test` across all 30 test projects, verify zero regressions against 1,100+ tests — PASS (4,547 passed, 179 pre-existing failures, 31 skipped; all affected projects 100% clean)
- [x] T041 Verify success criteria — check SC-001 through SC-009 from spec.md are all met; for SC-003, compare line counts of each service's Program.cs pipeline section before/after consolidation to confirm ≥15 line reduction

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Empty — no setup needed
- **Foundational (Phase 2)**: Empty — no blocking prerequisites
- **User Stories (Phases 3-9)**: ALL independent — can start in parallel immediately
- **Polish (Phase 10)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: No dependencies — can start immediately
- **US2 (P2)**: No dependencies — can start immediately (different files from US1)
- **US3 (P2)**: No dependencies — can start immediately (CLI files, no overlap)
- **US4 (P3)**: No dependencies — can start immediately (MCP Server files, no overlap)
- **US5 (P3)**: No dependencies — can start immediately (ServiceClients + Wallet + UI files)
- **US6 (P3)**: No dependencies — can start immediately (adds headers only, no logic changes)
- **US7 (P3)**: No dependencies — can start immediately (CLAUDE.md only)

### Within Each User Story

- Create shared structure first (T001, T009/T010, T025, T030)
- Update consumers in parallel where marked [P]
- Verify build/test as final task in each phase

### Parallel Opportunities

All 7 user stories touch completely different files and can execute simultaneously:

```
Wave 1 (all parallel):
  US1: AuthorizationPolicyExtensions.cs + 6 AuthenticationExtensions.cs
  US2: OpenApiExtensions.cs + CorsExtensions.cs + 7 Program.cs
  US3: IAdminServiceClient.cs + ICredentialServiceClient.cs + AdminCommands.cs + Cli.csproj
  US4: McpServer/Infrastructure/Models/ + 18 tool files
  US5: ServiceClients/Wallet/Models/ + Wallet.Service + UI.Core
  US6: ~168 .cs files (headers only)
  US7: CLAUDE.md

Wave 2 (after all stories):
  T039-T041: Full build + test + verification
```

---

## Parallel Example: User Story 1

```bash
# T001 must complete first (creates shared extension)
# Then T002-T007 can all run in parallel (different service files):
Task: "Update Blueprint auth in AuthenticationExtensions.cs"
Task: "Update Register auth in AuthenticationExtensions.cs"
Task: "Update Wallet auth in AuthenticationExtensions.cs"
Task: "Update Validator auth in AuthenticationExtensions.cs"
Task: "Update Peer auth in AuthenticationExtensions.cs"
Task: "Update Tenant auth in AuthenticationExtensions.cs"
```

## Parallel Example: User Story 4

```bash
# T025 must complete first (creates shared ErrorResponse)
# Then T026-T028 can all run in parallel (different tool directories):
Task: "Update Admin MCP tools (4 files)"
Task: "Update Designer MCP tools (7 files)"
Task: "Update Participant MCP tools (7 files)"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete US1: Authorization Policy Consolidation
2. **STOP and VALIDATE**: All services build, all auth tests pass
3. This alone eliminates ~300 lines of the highest-risk duplication

### Incremental Delivery

1. US1 (auth policies) → highest security impact
2. US2 (pipeline) + US3 (CLI) → medium impact, bug fixes
3. US4-US7 → lower impact cleanup
4. Each story adds value without breaking previous stories

### Parallel Agent Strategy

All 7 user stories can be dispatched to parallel subagents:
- Each agent works on completely different files
- No merge conflicts possible between stories
- Final verification (T039-T041) runs after all agents complete

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- All user stories are fully independent — zero cross-story dependencies
- Commit after each user story completion
- Stop at any checkpoint to validate story independently
- Total files touched: ~200+ (mostly license headers)
