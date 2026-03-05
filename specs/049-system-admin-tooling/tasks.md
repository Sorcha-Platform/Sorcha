# Tasks: System Administration Tooling (Feature 049)

**Input**: Design documents from `/specs/049-system-admin-tooling/`
**Prerequisites**: plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md
**Branch**: `049-system-admin-tooling`

**Tests**: Unit tests and E2E tests are included in Phase 10 per the plan's Phase 7 build order.

**Organization**: Tasks grouped by user story. 7 user stories (3x P1, 2x P2, 1x P3, 1x P2-CLI).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (YARP Route)

**Purpose**: Single backend-adjacent change needed before UI work

- [X] T001 Add YARP route cluster and route for `/api/admin/validators/{**}` proxying to Validator Service with `RequireAdministrator` auth policy in `src/Services/Sorcha.ApiGateway/appsettings.json`

**Checkpoint**: API Gateway proxies admin validator endpoints. Verify with `curl http://localhost/api/admin/validators/...`

---

## Phase 2: Foundational (View Models + Services + Validator Tab Refactor)

**Purpose**: All view models, service interfaces, implementations, and DI registration that user stories depend on. Also refactors Validator page to multi-tab layout (prerequisite for US3, US4, US6).

**CRITICAL**: No user story work can begin until this phase is complete.

### View Models (all parallelizable — different files)

- [X] T002 [P] Create `ServicePrincipalViewModel`, `CreateServicePrincipalRequest`, `ServicePrincipalSecretViewModel`, `ServicePrincipalListResult`, and `ExpirationPreset` enum in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ServicePrincipalViewModel.cs`
- [X] T003 [P] Create `RegisterPolicyViewModel`, `RegisterPolicyFields`, `ApprovedValidatorInfo`, `PolicyVersionViewModel`, `PolicyHistoryViewModel`, `PolicyUpdateProposalViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/RegisterPolicyViewModels.cs`
- [X] T004 [P] Create `PendingValidatorViewModel`, `ConsentQueueViewModel`, `RegisterConsentGroup` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ValidatorConsentViewModels.cs`
- [X] T005 [P] Create `AggregatedMetricsViewModel`, `ValidationSummaryViewModel`, `ConsensusSummaryViewModel`, `PoolSummaryViewModel`, `CacheSummaryViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ValidatorMetricsViewModels.cs`
- [X] T006 [P] Create `SystemRegisterViewModel`, `BlueprintSummaryViewModel`, `BlueprintDetailViewModel`, `BlueprintPageResult` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/SystemRegisterViewModels.cs`
- [X] T007 [P] Create `ThresholdConfigViewModel`, `ThresholdSetupRequest` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ThresholdConfigViewModel.cs`
- [X] T008 [P] Create `ValidatorConfigViewModel` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/ValidatorConfigViewModel.cs`

### Service Interfaces + Implementations (parallelizable — different files)

- [X] T009 [P] Create `IServicePrincipalService` interface (8 methods: List, Get, Create, UpdateScopes, Suspend, Reactivate, Revoke, RotateSecret) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IServicePrincipalService.cs` per contracts/ui-services.md
- [X] T010 [P] Create `ServicePrincipalService` implementation using HttpClient → `/api/service-principals/*` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ServicePrincipalService.cs`
- [X] T011 [P] Create `ISystemRegisterService` interface (4 methods: GetStatus, GetBlueprints, GetBlueprint, GetBlueprintVersion) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ISystemRegisterService.cs` per contracts/ui-services.md
- [X] T012 [P] Create `SystemRegisterService` implementation using HttpClient → `/api/system-register/*` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/SystemRegisterService.cs`
- [X] T013 Extend `IValidatorAdminService` with 14 new methods (consent queue 5, metrics 5, threshold 2, config 1) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IValidatorAdminService.cs` per contracts/ui-services.md
- [X] T014 Extend `ValidatorAdminService` implementation for all 14 new methods using HttpClient → `/api/validators/*`, `/api/metrics/*` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ValidatorAdminService.cs`
- [X] T015 Extend `IRegisterService` with 3 new methods (GetPolicy, GetPolicyHistory, ProposePolicyUpdate) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/IRegisterService.cs` per contracts/ui-services.md
- [X] T016 Extend `RegisterService` implementation for 3 new policy methods using HttpClient → `/api/registers/{id}/policy/*` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/RegisterService.cs`

### DI Registration

- [X] T017 Register `IServicePrincipalService` and `ISystemRegisterService` as scoped services in the UI DI container (follow existing registration pattern for `IValidatorAdminService`) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Program.cs` or the shared service registration extension

### Validator Page Tab Refactoring

- [X] T018 Refactor `ValidatorPanel.razor` from single-mempool layout to `MudTabs` layout with 5 tabs: Mempool (existing content), Consent Queue (placeholder), Metrics (placeholder), Threshold (placeholder), Config (placeholder) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorPanel.razor`

**Checkpoint**: Solution builds. All view models compile. Service interfaces resolve through DI. Validator page shows 5 tabs with placeholders for new tabs and existing mempool content on first tab.

---

## Phase 3: User Story 1 — Service Principal Management (Priority: P1) MVP

**Goal**: Replace hardcoded read-only service principals page with full CRUD DataGrid supporting create, edit scopes, rotate secret, suspend/reactivate, and revoke.

**Independent Test**: Create a service principal in the UI, copy the secret, use it to authenticate via the token endpoint.

### Implementation for User Story 1

- [X] T019 [P] [US1] Create `SecretDisplayDialog.razor` — one-time credential display modal with copy-to-clipboard and warning banner (include loading state while secret generates) in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/ServicePrincipals/SecretDisplayDialog.razor`
- [X] T020 [P] [US1] Create `CreateServicePrincipalDialog.razor` — form dialog with ServiceName, Scopes multi-select, ExpirationPreset dropdown; on submit calls `IServicePrincipalService.CreateAsync` then opens SecretDisplayDialog; include validation errors and loading state in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/ServicePrincipals/CreateServicePrincipalDialog.razor`
- [X] T021 [P] [US1] Create `EditScopesDialog.razor` — inline scope editing dialog with multi-select; calls `IServicePrincipalService.UpdateScopesAsync`; include loading state and error alert in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/ServicePrincipals/EditScopesDialog.razor`
- [X] T022 [US1] Create `ServicePrincipalCrud.razor` — MudDataGrid with columns (Name, ClientId, Status chip, Scopes, LastUsed, Expiration), row actions (Edit Scopes, Rotate Secret, Suspend/Reactivate, Revoke), "Include Inactive" toggle, "Create" button; uses confirmation dialogs for destructive actions; include loading skeleton, error alert with retry, and empty state per SC-007 in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/ServicePrincipals/ServicePrincipalCrud.razor`
- [X] T023 [US1] Replace placeholder content in `ServicePrincipals.razor` page — remove hardcoded data and "coming soon" banner, embed `ServicePrincipalCrud` component in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/ServicePrincipals.razor`

**Checkpoint**: Navigate to `/admin/principals`. DataGrid shows live service principals. Can create → copy secret → edit scopes → rotate → suspend → reactivate → revoke.

---

## Phase 4: User Story 2 — Register Policy Management (Priority: P1)

**Goal**: Add policy configuration to register creation wizard and policy viewing/updating to register detail page.

**Independent Test**: Create a register with custom policy settings, view policy tab on detail page, propose a policy update.

### Implementation for User Story 2

- [X] T024 [P] [US2] Create `RegisterPolicyStep.razor` — wizard step component for Create Register wizard with form fields (MinValidators, MaxValidators, SignatureThreshold, RegistrationMode dropdown, TransitionMode) pre-filled with defaults in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/RegisterPolicyStep.razor`
- [X] T025 [P] [US2] Create `PolicyHistoryDialog.razor` — paginated table of policy versions (Version, UpdatedBy, UpdatedAt) with "View" link per version in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PolicyHistoryDialog.razor`
- [X] T026 [P] [US2] Create `PolicyDiffDialog.razor` — propose-update form showing current vs proposed values side-by-side, confirmation with governance vote warning in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/PolicyDiffDialog.razor`
- [X] T027 [US2] Create `RegisterPolicyTab.razor` — detail tab component showing current effective policy as card layout, "View History" and "Propose Update" action buttons in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/RegisterPolicyTab.razor`
- [X] T028 [US2] Integrate `RegisterPolicyStep` into existing `CreateRegisterWizard.razor` as a new wizard step between Options and Create steps in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Registers/CreateRegisterWizard.razor`
- [X] T029 [US2] Add "Policy" tab to register detail page `Detail.razor` using `RegisterPolicyTab` component in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Registers/Detail.razor`

**Checkpoint**: Create Register wizard shows Policy step. Register detail page shows Policy tab with current policy, history dialog, and propose-update flow.

---

## Phase 5: User Story 3 — Validator Consent Queue (Priority: P1)

**Goal**: Add consent queue tab to Validator admin page with pending validator list, bulk approve/reject, and refresh from chain.

**Independent Test**: Register a validator against a consent-mode register, approve it via Consent Queue tab, verify it appears in approved list.

### Implementation for User Story 3

- [X] T030 [US3] Create `ValidatorConsentPanel.razor` — Consent Queue tab content: pending validators grouped by register with checkboxes, "Approve Selected" / "Reject Selected" buttons with confirmation dialogs (reject includes optional reason field), approved validators section with "Refresh from Chain" button, empty state for no pending requests, open-mode notice; include loading skeleton, error alert with retry for service-unavailable (edge case), and handle max-validator-count rejection (409) with policy constraint message in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorConsentPanel.razor`
- [X] T031 [US3] Wire `ValidatorConsentPanel` into the Consent Queue tab placeholder in `ValidatorPanel.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorPanel.razor`

**Checkpoint**: Validator admin page Consent Queue tab shows pending requests grouped by register, bulk approve/reject works, refresh from chain works.

---

## Phase 6: User Story 4 — Validator Metrics Dashboard (Priority: P2)

**Goal**: Add metrics tab with KPI summary cards and expandable subsystem detail panels with auto-refresh.

**Independent Test**: Navigate to Metrics tab, verify KPI cards display live data from each metrics subsystem.

### Implementation for User Story 4

- [X] T032 [US4] Create `ValidatorMetricsPanel.razor` — Metrics tab content: 4 KPI summary cards (success rate, dockets proposed, queue depth, cache ratio), 4 expandable `MudExpansionPanel` sections (Validation, Consensus, Pools, Caches) with detailed metrics, auto-refresh toggle with interval control (default 5s, SC-004: ≤10s latency) using existing Timer polling pattern; include loading skeleton, error alert with retry for service-unavailable (edge case), and empty state when no metrics available in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorMetricsPanel.razor`
- [X] T033 [US4] Wire `ValidatorMetricsPanel` into the Metrics tab placeholder in `ValidatorPanel.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorPanel.razor`

**Checkpoint**: Validator admin page Metrics tab shows live KPI cards and expandable detail panels with auto-refresh.

---

## Phase 7: User Story 5 — System Register Visibility (Priority: P2)

**Goal**: New admin page showing System Register initialization status and paginated blueprint catalog.

**Independent Test**: Navigate to `/admin/system-register`, verify status card and blueprint list match actual system register state.

### Implementation for User Story 5

- [X] T034 [P] [US5] Create `SystemRegisterDashboard.razor` — status card (RegisterId, DisplayName, IsInitialized, BlueprintCount, CreatedAt), paginated MudDataGrid of blueprints (BlueprintId, Version, PublishedAt, PublishedBy, IsActive), blueprint detail dialog on row click with version selector, warning banner when not initialized in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/SystemRegister/SystemRegisterDashboard.razor`
- [X] T035 [US5] Create `SystemRegister.razor` page at route `/admin/system-register` with `[Authorize(Roles = "Administrator")]`, embedding `SystemRegisterDashboard` in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/SystemRegister.razor`
- [X] T036 [US5] Add "System Register" nav link under System nav group in `MainLayout.razor` with `AuthorizeView` role check in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Layout/MainLayout.razor`

**Checkpoint**: `/admin/system-register` shows status card and blueprint catalog. Nav link visible to admins.

---

## Phase 8: User Story 6 — Threshold Signing & Config (Priority: P3)

**Goal**: Add threshold signing status tab and read-only config tab to Validator admin page.

**Independent Test**: View threshold signing configuration per register. View validator configuration with redacted sensitive values.

### Implementation for User Story 6

- [X] T037 [P] [US6] Create `ValidatorThresholdPanel.razor` — Threshold tab content: per-register status cards (GroupPublicKey, Threshold t, TotalValidators n, ValidatorIds, CollectedShares during signing), "Setup Threshold Signing" action for unconfigured registers with guided form (RegisterId, Threshold, TotalValidators, ValidatorIds) and validation (t ≤ n, edge case); include loading skeleton, error alert with retry for service-unavailable (edge case), and empty state when no registers configured in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorThresholdPanel.razor`
- [X] T038 [P] [US6] Create `ValidatorConfigPanel.razor` — Config tab content: read-only key-value display from `ValidatorConfigViewModel`, sensitive keys displayed as `***` using `RedactedKeys` list; include loading skeleton, error alert with retry, and empty state in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorConfigPanel.razor`
- [X] T039 [US6] Wire `ValidatorThresholdPanel` and `ValidatorConfigPanel` into their respective tab placeholders in `ValidatorPanel.razor` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/Validator/ValidatorPanel.razor`

**Checkpoint**: Validator admin page Threshold tab shows per-register status and setup action. Config tab shows read-only config.

---

## Phase 9: User Story 7 — CLI Commands (Priority: P2)

**Goal**: Add 17 CLI subcommands across 5 command groups: register policy (3), register system (2), validator consent (4), validator metrics (6), validator threshold (2).

**Independent Test**: Run each CLI command against a running platform instance and verify formatted output (table and JSON).

### CLI Refit Interface Extensions

- [X] T040 [P] [US7] Extend `IRegisterServiceClient` Refit interface with 5 new methods (GetPolicy, GetPolicyHistory, ProposePolicyUpdate, GetSystemRegisterStatus, GetSystemRegisterBlueprints) in `src/Apps/Sorcha.Cli/Services/IRegisterServiceClient.cs` per contracts/cli-commands.md
- [X] T041 [P] [US7] Extend `IValidatorServiceClient` Refit interface with 12 new methods (6 metrics, 4 consent, 2 threshold) in `src/Apps/Sorcha.Cli/Services/IValidatorServiceClient.cs` per contracts/cli-commands.md

### CLI DTOs

- [X] T042 [P] [US7] Create CLI DTOs for policy responses (`RegisterPolicyResponse`, `PolicyHistoryResponse`, `PolicyUpdateRequest`, `PolicyUpdateResponse`) in `src/Apps/Sorcha.Cli/Models/RegisterPolicy.cs`
- [X] T043 [P] [US7] Create CLI DTOs for metrics and threshold responses (if not using raw `HttpResponseMessage` deserialization) in `src/Apps/Sorcha.Cli/Models/ValidatorMetrics.cs`

### Register Commands

- [X] T044 [US7] Add `register policy get` subcommand — gets current policy for a register, table/JSON output in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs` (add PolicyCommand group)
- [X] T045 [US7] Add `register policy history` subcommand — paginated version history, table/JSON output in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [X] T046 [US7] Add `register policy update` subcommand — propose policy update with confirmation prompt unless `--yes`, output proposed version in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- [X] T047 [US7] Add `register system status` subcommand — system register status, table/JSON output in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs` (add SystemCommand group)
- [X] T048 [US7] Add `register system blueprints` subcommand — paginated blueprint list, table/JSON output in `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`

### Validator Commands

- [X] T049 [US7] Add `validator consent pending` subcommand — list pending validators for a register, table/JSON output in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs` (add ConsentCommand group)
- [X] T050 [US7] Add `validator consent approve` subcommand — approve validator with confirmation, success message in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs`
- [X] T051 [US7] Add `validator consent reject` subcommand — reject validator with optional `--reason`, confirmation, success message in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs`
- [X] T052 [US7] Add `validator consent refresh` subcommand — refresh approved validators from chain, table/JSON output in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs`
- [X] T053 [US7] Add `validator metrics` (aggregated) subcommand — KPI summary, table/JSON output in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs` (add MetricsCommand group)
- [X] T054 [US7] Add `validator metrics validation|consensus|pools|caches|config` subcommands — per-subsystem metrics, table/JSON output in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs`
- [X] T055 [US7] Add `validator threshold status` subcommand — threshold config for a register, table/JSON output in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs` (add ThresholdCommand group)
- [X] T056 [US7] Add `validator threshold setup` subcommand — setup threshold signing with confirmation, output group public key in `src/Apps/Sorcha.Cli/Commands/ValidatorCommands.cs`

**Checkpoint**: All 17 CLI subcommands build and execute. `sorcha register policy get --register-id <id>` returns formatted output. `sorcha validator metrics` shows KPI summary.

---

## Phase 10: Tests & Polish

**Purpose**: Unit tests for services and CLI, E2E tests for UI, documentation updates.

### Unit Tests — UI Services

- [X] T057 [P] Create unit tests for `ServicePrincipalService` (all 8 methods, mock HttpClient) in `tests/Sorcha.UI.Core.Tests/Services/ServicePrincipalServiceTests.cs`
- [X] T058 [P] Create unit tests for `SystemRegisterService` (all 4 methods, mock HttpClient) in `tests/Sorcha.UI.Core.Tests/Services/SystemRegisterServiceTests.cs`
- [X] T059 [P] Create unit tests for `ValidatorAdminService` extensions (14 new methods, mock HttpClient) in `tests/Sorcha.UI.Core.Tests/Services/ValidatorAdminServiceTests.cs`
- [X] T060 [P] Create unit tests for `RegisterService` extensions (3 new policy methods, mock HttpClient) in `tests/Sorcha.UI.Core.Tests/Services/RegisterServiceTests.cs`

### Unit Tests — CLI Commands

- [X] T061 [P] Create unit tests for `register policy` commands (get, history, update) in `tests/Sorcha.Cli.Tests/Commands/RegisterPolicyCommandTests.cs`
- [X] T062 [P] Create unit tests for `register system` commands (status, blueprints) in `tests/Sorcha.Cli.Tests/Commands/RegisterSystemCommandTests.cs`
- [X] T063 [P] Create unit tests for `validator consent` commands (pending, approve, reject, refresh) in `tests/Sorcha.Cli.Tests/Commands/ValidatorConsentCommandTests.cs`
- [X] T064 [P] Create unit tests for `validator metrics` commands (aggregated + 5 subsystems) in `tests/Sorcha.Cli.Tests/Commands/ValidatorMetricsCommandTests.cs`
- [X] T065 [P] Create unit tests for `validator threshold` commands (status, setup) in `tests/Sorcha.Cli.Tests/Commands/ValidatorThresholdCommandTests.cs`

### E2E Tests (if Playwright infrastructure available)

- [ ] T066 [P] Create E2E test for Service Principals page (create, rotate, suspend, revoke flow) in `tests/Sorcha.UI.E2E.Tests/Admin/ServicePrincipalsTests.cs`
- [ ] T067 [P] Create E2E test for Register Policy (wizard step + detail tab) in `tests/Sorcha.UI.E2E.Tests/Admin/RegisterPolicyTests.cs`
- [ ] T068 [P] Create E2E test for Validator admin tabs (consent, metrics, threshold, config) in `tests/Sorcha.UI.E2E.Tests/Admin/ValidatorAdminTests.cs`
- [ ] T069 [P] Create E2E test for System Register page in `tests/Sorcha.UI.E2E.Tests/Admin/SystemRegisterTests.cs`

### Documentation & Cleanup

- [X] T070 Update `docs/reference/development-status.md` with Feature 049 completion status
- [X] T071 Update `.specify/MASTER-TASKS.md` with Feature 049 task completion
- [X] T072 Run `quickstart.md` verification steps (build, test, manual UI + CLI checks)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (YARP route must be in place for validator endpoints)
- **US1 Service Principals (Phase 3)**: Depends on Phase 2 (needs IServicePrincipalService + view models)
- **US2 Register Policy (Phase 4)**: Depends on Phase 2 (needs IRegisterService extensions + view models)
- **US3 Consent Queue (Phase 5)**: Depends on Phase 2 (needs IValidatorAdminService extensions + ValidatorPanel tabs)
- **US4 Metrics (Phase 6)**: Depends on Phase 2 (needs IValidatorAdminService extensions + ValidatorPanel tabs)
- **US5 System Register (Phase 7)**: Depends on Phase 2 (needs ISystemRegisterService + view models)
- **US6 Threshold + Config (Phase 8)**: Depends on Phase 2 (needs IValidatorAdminService extensions + ValidatorPanel tabs)
- **US7 CLI Commands (Phase 9)**: Depends on Phase 1 only (YARP route). Independent of UI phases.
- **Tests & Polish (Phase 10)**: Depends on all story phases being complete

### User Story Dependencies

- **US1 (P1)**: Independent after Phase 2 — no cross-story dependencies
- **US2 (P1)**: Independent after Phase 2 — no cross-story dependencies
- **US3 (P1)**: Independent after Phase 2 — no cross-story dependencies
- **US4 (P2)**: Independent after Phase 2 — no cross-story dependencies
- **US5 (P2)**: Independent after Phase 2 — no cross-story dependencies
- **US6 (P3)**: Independent after Phase 2 — no cross-story dependencies
- **US7 (P2)**: Independent after Phase 1 — can run in parallel with Phase 2 + UI stories

### Within Each User Story

- Models (Phase 2) before services (Phase 2) — already handled by foundational phase
- Service interfaces before implementations
- Dialog components before parent components that embed them
- Parent components before page wiring

### Parallel Opportunities

- **Phase 2**: All 7 view model tasks (T002-T008) can run in parallel. All service interfaces (T009, T011, T013, T015) can run in parallel. Implementations (T010, T012, T014, T016) follow their respective interfaces.
- **Phases 3-8**: US1, US2, US3, US4, US5, US6 can ALL run in parallel after Phase 2 (different files, different pages)
- **Phase 9**: Can run in parallel with Phases 3-8 (CLI is independent of UI). Refit extensions (T040-T041) in parallel. CLI DTOs (T042-T043) in parallel.
- **Phase 10**: All unit test files (T057-T065) in parallel. All E2E test files (T066-T069) in parallel.

---

## Parallel Example: Phase 2 (Foundational)

```bash
# Wave 1 — All view models in parallel:
Task T002: "ServicePrincipalViewModel.cs"
Task T003: "RegisterPolicyViewModels.cs"
Task T004: "ValidatorConsentViewModels.cs"
Task T005: "ValidatorMetricsViewModels.cs"
Task T006: "SystemRegisterViewModels.cs"
Task T007: "ThresholdConfigViewModel.cs"
Task T008: "ValidatorConfigViewModel.cs"

# Wave 2 — All service interfaces + implementations in parallel:
Task T009: "IServicePrincipalService.cs"
Task T010: "ServicePrincipalService.cs"
Task T011: "ISystemRegisterService.cs"
Task T012: "SystemRegisterService.cs"
Task T013: "IValidatorAdminService.cs (extend)"
Task T014: "ValidatorAdminService.cs (extend)"
Task T015: "IRegisterService.cs (extend)"
Task T016: "RegisterService.cs (extend)"

# Wave 3 — DI + Tab refactor:
Task T017: "DI registration"
Task T018: "ValidatorPanel.razor tab refactor"
```

## Parallel Example: UI Stories (after Phase 2)

```bash
# All 6 UI stories can dispatch in parallel to different agents:
Agent 1: US1 tasks (T019-T023) — Service Principals
Agent 2: US2 tasks (T024-T029) — Register Policy
Agent 3: US3 tasks (T030-T031) — Consent Queue
Agent 4: US4 tasks (T032-T033) — Metrics Dashboard
Agent 5: US5 tasks (T034-T036) — System Register
Agent 6: US6 tasks (T037-T039) — Threshold + Config
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US3 — all P1)

1. Complete Phase 1: Setup (YARP route)
2. Complete Phase 2: Foundational (view models, services, tab refactor)
3. Complete Phase 3: US1 — Service Principals
4. Complete Phase 4: US2 — Register Policy
5. Complete Phase 5: US3 — Consent Queue
6. **STOP and VALIDATE**: Test all P1 stories independently
7. Deploy/demo if ready — all critical admin functions available

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 → Test → Service principals fully manageable (MVP!)
3. Add US2 + US3 → Test → Policy + consent management available
4. Add US4 + US5 → Test → Metrics + system register visibility
5. Add US7 → Test → CLI automation available
6. Add US6 → Test → Advanced threshold features
7. Tests & Polish → Production ready

### Parallel Execution Strategy

With subagent parallelization:

1. Phase 1 + Phase 2 sequentially (foundation work)
2. Once Phase 2 complete:
   - Dispatch 6 UI story agents in parallel (US1-US6)
   - Dispatch 1 CLI agent in parallel (US7)
3. Phase 10 tests after all stories complete

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after Phase 2
- US7 (CLI) can start after Phase 1 — doesn't need Phase 2 UI services
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Total tasks: 72 (1 setup + 17 foundational + 5 US1 + 6 US2 + 2 US3 + 2 US4 + 3 US5 + 3 US6 + 17 US7 + 16 tests/polish)
