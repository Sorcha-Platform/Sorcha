# Tasks: Register TenantId Removal & Security Hardening

**Input**: Design documents from `/specs/067-register-security-hardening/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — US8 explicitly requires comprehensive test coverage (FR-021 through FR-024).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New enum and shared model changes that all stories depend on

- [x] T001 [P] Create `RegisterPurpose` enum (General=0, System=1) with `JsonStringEnumConverter` in `src/Common/Sorcha.Register.Models/Enums/RegisterPurpose.cs`
- [x] T002 [P] Add `Purpose` property (type `RegisterPurpose`, default `General`) to Register entity in `src/Common/Sorcha.Register.Models/Register.cs`
- [x] T003 [P] Add `Purpose` property (type `RegisterPurpose`, default `General`, optional) to `InitiateRegisterCreationRequest` in `src/Common/Sorcha.Register.Models/RegisterCreationModels.cs`
- [x] T004 Add `Purpose` field to `RegisterCreatedEvent` in `src/Core/Sorcha.Register.Core/Events/RegisterEvents.cs`
- [x] T005 Add MongoDB ascending index on `Purpose` field in `CreateIndexesAsync` in `src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterRepository.cs`

**Checkpoint**: RegisterPurpose enum exists, Register entity has Purpose property, build succeeds

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Authorization policies and service client that MUST be complete before user story work

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 Tighten `CanManageRegisters` policy to require `org_id` claim + Administrator or SystemAdmin role in `src/Services/Sorcha.Register.Service/Extensions/AuthenticationExtensions.cs`
- [x] T007 Add `CanCreateSystemRegisters` policy requiring SystemAdmin org + SystemAdmin role in `src/Services/Sorcha.Register.Service/Extensions/AuthenticationExtensions.cs`
- [x] T008 [P] Create `ISubscriptionServiceClient` interface and `SubscriptionServiceClient` implementation with `GetActiveRegisterIdsForOrgAsync(Guid orgId)` method in `src/Common/Sorcha.ServiceClients/Subscription/SubscriptionServiceClient.cs` — calls Tenant Service `GET /api/organizations/{orgId}/register-subscriptions`, returns RegisterId list, fail-closed (empty list on error)
- [x] T009 Register `ISubscriptionServiceClient` in DI via `AddServiceClients` extension in `src/Common/Sorcha.ServiceClients/ServiceClientExtensions.cs`

**Checkpoint**: Foundation ready — authorization policies and service client available for all user stories

---

## Phase 3: User Story 1 — Register Purpose Classification (Priority: P1) 🎯 MVP

**Goal**: Registers can be classified by purpose (General/System), persisted, and queried

**Independent Test**: Create registers with different purposes, verify the flag persists and is returned in queries

### Tests for User Story 1

- [ ] T010 [P] [US1] Unit test `RegisterPurpose` enum — default value, serialization (JSON string), all valid values in `tests/Sorcha.Register.Models.Tests/`
- [ ] T011 [P] [US1] Unit test `RegisterManager` — purpose is set on creation, purpose is included in query results in `tests/Sorcha.Register.Service.Tests/`

### Implementation for User Story 1

- [x] T012 [US1] Update `RegisterCreationOrchestrator.InitiateAsync` to pass `Purpose` from request through to register creation in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [x] T013 [US1] Update `RegisterManager.CreateRegisterAsync` to set `Purpose` on the new register entity in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- [x] T014 [US1] Update `SystemRegisterBootstrapper` to set `Purpose = RegisterPurpose.System` when creating the system register in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [x] T015 [US1] Ensure `GET /api/registers` response includes `Purpose` field — update register list/get endpoint mapping in `src/Services/Sorcha.Register.Service/Program.cs`
- [x] T016 [US1] Add Purpose validation in `RegisterCreationOrchestrator` — reject `Purpose.System` when caller lacks `CanCreateSystemRegisters` policy, return 403 in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`

**Checkpoint**: Registers have Purpose field, system register is flagged, purpose visible in API responses

---

## Phase 4: User Story 2 — Authenticated Register Creation (Priority: P1)

**Goal**: Register creation requires JWT with admin role — anonymous creation blocked

**Independent Test**: Attempt creation with admin JWT (succeeds), non-admin JWT (403), no JWT (401)

### Tests for User Story 2

- [ ] T017 [P] [US2] API test — anonymous POST to `/api/registers/initiate` returns 401 in `tests/Sorcha.Register.Service.Tests/RegisterCreationApiTests.cs`
- [ ] T018 [P] [US2] API test — non-admin user POST to `/api/registers/initiate` returns 403 in `tests/Sorcha.Register.Service.Tests/RegisterCreationApiTests.cs`
- [ ] T019 [P] [US2] API test — admin user POST to `/api/registers/initiate` succeeds (200) without TenantId in request body in `tests/Sorcha.Register.Service.Tests/RegisterCreationApiTests.cs`

### Implementation for User Story 2

- [x] T020 [US2] Remove `AllowAnonymous` from `/api/registers/initiate` endpoint, apply `CanManageRegisters` policy in `src/Services/Sorcha.Register.Service/Program.cs`
- [x] T021 [US2] Remove `AllowAnonymous` from `/api/registers/finalize` endpoint, require authentication in `src/Services/Sorcha.Register.Service/Program.cs`
- [ ] T022 [US2] Update `RegisterCreationOrchestrator.InitiateAsync` to derive org identity from JWT `org_id` claim instead of request body TenantId in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [ ] T023 [US2] Update existing register creation tests to provide valid admin JWT tokens in `tests/Sorcha.Register.Service.Tests/RegisterCreationApiTests.cs` and `tests/Sorcha.Register.Service.Tests/Unit/RegisterCreationOrchestratorTests.cs`

**Checkpoint**: Register creation requires authentication + admin role, org derived from JWT

---

## Phase 5: User Story 3 — Subscription-Scoped Register Queries (Priority: P1)

**Goal**: GET /api/registers returns only registers the caller's org is subscribed to, plus System registers

**Independent Test**: Create subscriptions for an org, query as that org's user, verify only subscribed + system registers returned

### Tests for User Story 3

- [ ] T024 [P] [US3] Unit test `SubscriptionServiceClient` — happy path returns register IDs, fail-closed returns empty list on HTTP error in `tests/Sorcha.ServiceClients.Tests/`
- [ ] T025 [P] [US3] API test — user sees only subscribed registers + system registers, not unsubscribed registers in `tests/Sorcha.Register.Service.Tests/`
- [ ] T025a [P] [US3] API test — when Tenant Service is unreachable (mock returns error), GET /api/registers returns only system registers, zero general registers leaked (fail-closed FR-014) in `tests/Sorcha.Register.Service.Tests/`

### Implementation for User Story 3

- [x] T026 [US3] Inject `ISubscriptionServiceClient` into Register Service DI in `src/Services/Sorcha.Register.Service/Program.cs`
- [x] T027 [US3] Add `GetRegistersForOrgAsync(Guid orgId)` method to `RegisterManager` — calls `ISubscriptionServiceClient` to get subscribed register IDs, queries local registers matching those IDs plus all `Purpose == System` registers in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- [x] T028 [US3] Update `GET /api/registers` endpoint — remove `?tenantId` query parameter, extract `org_id` from JWT, call `GetRegistersForOrgAsync`, return filtered results in `src/Services/Sorcha.Register.Service/Program.cs`
- [x] T029 [US3] Add structured logging for subscription resolution — log org_id, count of subscribed registers, fail-closed events in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`

**Checkpoint**: Register queries are subscription-scoped, system registers visible to all, fail-closed on service errors

---

## Phase 6: User Story 4 — Attestation-Based Register Deletion (Priority: P2)

**Goal**: Register deletion authorized by control record attestations, not TenantId. System registers undeletable.

**Independent Test**: Delete as attested owner (succeeds), attested admin (succeeds), non-attested user (403), system register (403)

### Tests for User Story 4

- [ ] T030 [P] [US4] API test — attested owner can delete register in `tests/Sorcha.Register.Service.Tests/`
- [ ] T031 [P] [US4] API test — non-attested user gets 403 on delete in `tests/Sorcha.Register.Service.Tests/`
- [ ] T032 [P] [US4] API test — system register deletion returns 403 regardless of attestation in `tests/Sorcha.Register.Service.Tests/`

### Implementation for User Story 4

- [x] T033 [US4] Update `RegisterManager.DeleteRegisterAsync` — replace TenantId ownership check with attestation lookup: get control record, match caller's `wallet_address` JWT claim against Owner/Admin attestation subjects (strip `did:sorcha:org:` prefix from DID for comparison). Guard against empty/null attestations (return 500 with diagnostic info for corrupted control records). File: `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- [x] T034 [US4] Add system register deletion guard — check `Purpose == System`, return 403 with message "System registers cannot be deleted" in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- [x] T035 [US4] Update `DELETE /api/registers/{id}` endpoint — remove `?tenantId` query parameter, extract `wallet_address` from JWT claims, pass to `DeleteRegisterAsync` in `src/Services/Sorcha.Register.Service/Program.cs`

**Checkpoint**: Deletion uses attestation-based auth, system registers protected

---

## Phase 7: User Story 5 — Register-Scoped Real-Time Notifications (Priority: P2)

**Goal**: SignalR notifications scoped to individual registers instead of tenants, with subscription access checks

**Independent Test**: Two clients — one with register access receives events, one without does not

### Tests for User Story 5

- [ ] T036 [P] [US5] Unit test `RegisterHub.SubscribeToRegister` — verifies subscription access check, adds to correct group in `tests/Sorcha.Register.Service.Tests/SignalRHubTests.cs`
- [ ] T037 [P] [US5] Unit test `RegisterEventBridgeService` — routes events to `register:{RegisterId}` groups in `tests/Sorcha.Register.Service.Tests/`

### Implementation for User Story 5

- [x] T038 [US5] Replace `SubscribeToTenant`/`UnsubscribeFromTenant` with `SubscribeToRegister(string registerId)`/`UnsubscribeFromRegister(string registerId)` in `src/Services/Sorcha.Register.Service/Hubs/RegisterHub.cs` — add subscription access check via `ISubscriptionServiceClient`
- [x] T039 [US5] Update `RegisterEventBridgeService` — replace `tenant:{TenantId}` group routing with `register:{RegisterId}` for all event types (RegisterCreated, RegisterDeleted, RegisterStatusChanged) in `src/Services/Sorcha.Register.Service/Services/RegisterEventBridgeService.cs`
- [ ] T040 [US5] Update UI `RegisterHubConnection` to call `SubscribeToRegister` instead of `SubscribeToTenant` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterHubConnection.cs`

**Checkpoint**: SignalR notifications register-scoped with access checks

---

## Phase 8: User Story 6 — Register Creation UI Purpose Selection (Priority: P1)

**Goal**: Purpose dropdown in Create Register Wizard Options step, System option only for system admins

**Independent Test**: Render wizard, verify dropdown with "General" default, system-admin-only filtering, review step display

### Tests for User Story 6

- [ ] T041 [P] [US6] bUnit test — Purpose dropdown renders in Options step with "General" default in `tests/Sorcha.UI.Core.Tests/Components/Registers/CreateRegisterWizardTests.cs`
- [ ] T042 [P] [US6] bUnit test — "System" option hidden for non-system-admin users in `tests/Sorcha.UI.Core.Tests/Components/Registers/CreateRegisterWizardTests.cs`
- [ ] T043 [P] [US6] bUnit test — selected purpose displayed in Review step in `tests/Sorcha.UI.Core.Tests/Components/Registers/CreateRegisterWizardTests.cs`

### Implementation for User Story 6

- [x] T044 [US6] Add `_purpose` field (default `RegisterPurpose.General`) and `IsSystemAdmin` parameter to `CreateRegisterWizard.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [x] T045 [US6] Add `MudSelect<RegisterPurpose>` dropdown in Options step (step index 2) alongside Advertise/Full Replica controls — filter items based on `IsSystemAdmin` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [x] T046 [US6] Display selected purpose in Review step (step index 4) summary in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [x] T047 [US6] Pass `_purpose` value in `InitiateRegisterCreationRequest` in `CreateRegisterAsync` method in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [x] T048 [US6] Update `RegisterCreationState` record to include `Purpose` property in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Registers/RegisterCreationState.cs`

**Checkpoint**: Wizard has Purpose dropdown, System filtered by role, shown in review, submitted with request

---

## Phase 9: User Story 7 — CLI Register Purpose Option (Priority: P2)

**Goal**: CLI supports `--purpose` flag on create, displays purpose in list/get output

**Independent Test**: Create register with `--purpose General`, verify in list output

### Tests for User Story 7

- [ ] T049 [P] [US7] CLI test — `register create` has `--purpose` option (optional, default General) in `tests/Sorcha.Cli.Tests/Commands/RegisterCommandsTests.cs`
- [ ] T050 [P] [US7] CLI test — `register list` output includes Purpose column in `tests/Sorcha.Cli.Tests/Commands/RegisterCommandsTests.cs`

### Implementation for User Story 7

- [x] T051 [US7] Add `--purpose` option (type `RegisterPurpose`, default `General`) to `RegisterCreateCommand` in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [x] T052 [US7] Pass purpose value to `InitiateRegisterCreationRequest` in `RegisterCreateCommand.ExecuteAsync` in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [x] T053 [US7] Add Purpose column to `register list` table output in `RegisterListCommand` in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [x] T054 [US7] Add Purpose field to `register get` detail output in `RegisterGetCommand` in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`

**Checkpoint**: CLI supports purpose on creation and displays in output

---

## Phase 10: User Story 9 — TenantId Removal (Priority: P3)

**Goal**: Remove TenantId from all register-related domain models, DTOs, events, service clients, UI, CLI, and tests

**Independent Test**: Solution builds with 0 errors, all tests pass, no TenantId references in register code

### Implementation for User Story 9

- [x] T055 [US9] Remove `TenantId` property from `Register` entity in `src/Common/Sorcha.Register.Models/Register.cs`
- [x] T056 [P] [US9] Remove `TenantId` property from `RegisterControlRecord` in `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs`
- [x] T057 [P] [US9] Remove `TenantId` property from `InitiateRegisterCreationRequest` in `src/Common/Sorcha.Register.Models/RegisterCreationModels.cs`
- [x] T058 [P] [US9] Remove `TenantId` from `RegisterCreatedEvent`, `RegisterDeletedEvent`, `RegisterStatusChangedEvent` in `src/Core/Sorcha.Register.Core/Events/RegisterEvents.cs`
- [x] T059 [US9] Remove TenantId index creation from `CreateIndexesAsync` in `src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterRepository.cs`
- [x] T060 [US9] Remove `GetRegistersByTenantAsync` method and all TenantId references from `RegisterManager` in `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- [x] T061 [US9] Remove TenantId assignment in `RegisterCreationOrchestrator` (InitiateAsync, FinalizeAsync, genesis metadata) in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [x] T062 [US9] Remove `TenantId = "system"` from `SystemRegisterBootstrapper` in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [x] T063 [US9] Remove `?tenantId` query parameter handling from any remaining endpoints in `src/Services/Sorcha.Register.Service/Program.cs`
- [x] T064 [P] [US9] Remove `TenantId` from `RegisterServiceClient` internal DTO in `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs`
- [x] T065 [P] [US9] Remove `--tenant-id` / `-t` option from `RegisterCreateCommand` in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [x] T066 [P] [US9] Remove `TenantId` parameter from `CreateRegisterWizard.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [x] T067 [P] [US9] Remove `tenantId` parameter from UI `RegisterService.GetRegistersAsync` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterService.cs`
- [x] T068 [P] [US9] Remove TenantId tracking from `McpSessionService` in `src/Apps/Sorcha.McpServer/Services/McpSessionService.cs` (kept — org context for MCP, not Register domain)
- [x] T069 [US9] Update all register-related test files — remove TenantId from test data, update assertions, fix compilation errors across `tests/Sorcha.Register.Service.Tests/`, `tests/Sorcha.Register.Core.Tests/`, `tests/Sorcha.Register.Models.Tests/`, `tests/Sorcha.Cli.Tests/`, `tests/Sorcha.UI.Core.Tests/`, `tests/Sorcha.Integration.Tests/`
- [x] T070 [US9] Build entire solution (`dotnet build --force`) and verify 0 errors, 0 new warnings

**Checkpoint**: TenantId fully removed, solution builds clean, all tests pass

---

## Phase 11: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, validation, and final verification

- [ ] T071 [P] Update `docs/guides/AUTHENTICATION-SETUP.md` with new authorization policies (CanManageRegisters tightened, CanCreateSystemRegisters added)
- [ ] T072 [P] Update `docs/reference/API-DOCUMENTATION.md` with register endpoint changes (auth requirements, removed tenantId param, added purpose field)
- [ ] T073 [P] Update Register Service README with purpose field documentation and new authorization requirements
- [ ] T074 Update `.specify/MASTER-TASKS.md` — add 067 entry, mark status
- [ ] T075 Run full test suite (`dotnet test`) — verify all tests pass, no regressions
- [ ] T076 Verify XML documentation on all new/modified public APIs — no CS1591 warnings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (enum/model) — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — MVP, no dependencies on other stories
- **US2 (Phase 4)**: Depends on Phase 2 — can parallel with US1
- **US3 (Phase 5)**: Depends on Phase 2 (needs ISubscriptionServiceClient) — can parallel with US1/US2
- **US4 (Phase 6)**: Depends on Phase 2 — can parallel with US1-US3
- **US5 (Phase 7)**: Depends on Phase 2 (needs ISubscriptionServiceClient) — can parallel with US1-US4
- **US6 (Phase 8)**: Depends on Phase 1 (needs RegisterPurpose enum) — can parallel with backend stories
- **US7 (Phase 9)**: Depends on Phase 1 (needs RegisterPurpose enum) — can parallel with backend stories
- **US9 (Phase 10)**: Depends on ALL previous phases — MUST be last
- **Polish (Phase 11)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (Purpose)**: Independent after Phase 2
- **US2 (Auth)**: Independent after Phase 2
- **US3 (Queries)**: Independent after Phase 2 (uses ISubscriptionServiceClient from T008)
- **US4 (Deletion)**: Independent after Phase 2
- **US5 (SignalR)**: Independent after Phase 2 (uses ISubscriptionServiceClient from T008)
- **US6 (UI)**: Independent after Phase 1 (model only, no service dependencies)
- **US7 (CLI)**: Independent after Phase 1 (model only, no service dependencies)
- **US9 (Cleanup)**: Depends on US1-US7 ALL complete — sequential only

### Parallel Opportunities

**Maximum parallelism after Phase 2:**
- US1 + US2 + US3 + US4 + US5 (backend stories)
- US6 + US7 (UI/CLI — only need Phase 1)

**Within each story**, [P]-marked tests can run in parallel.

---

## Parallel Example: After Phase 2

```text
# Backend stories (all can run simultaneously):
Agent 1: US1 — T010-T016 (Purpose classification)
Agent 2: US2 — T017-T023 (Auth hardening)
Agent 3: US3 — T024-T029 (Subscription queries)
Agent 4: US4 — T030-T035 (Attestation deletion)
Agent 5: US5 — T036-T040 (SignalR scoping)

# UI/CLI stories (can run after Phase 1, parallel to backend):
Agent 6: US6 — T041-T048 (UI wizard)
Agent 7: US7 — T049-T054 (CLI)

# After all stories complete:
Sequential: US9 — T055-T070 (TenantId removal)
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T009)
3. Complete Phase 3: US1 — Purpose Classification (T010-T016)
4. Complete Phase 4: US2 — Auth Hardening (T017-T023)
5. **STOP and VALIDATE**: Registers have purpose, creation requires admin JWT
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 + US2 → Test independently → Deploy (MVP — security baseline)
3. US3 → Subscription-scoped queries → Deploy (access control)
4. US4 + US5 → Deletion auth + SignalR scoping → Deploy (full security)
5. US6 + US7 → UI + CLI → Deploy (user-facing)
6. US9 → TenantId removal → Deploy (cleanup)
7. Polish → Docs + final verification

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US8 (Test Coverage) is distributed across all story phases as test tasks rather than a separate phase
- US9 (TenantId Removal) MUST be last — depends on all functional replacements being in place
- Total: 76 tasks across 11 phases
