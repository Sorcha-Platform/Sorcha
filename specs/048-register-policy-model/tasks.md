# Tasks: Unified Register Policy Model & System Register

**Input**: Design documents from `/specs/048-register-policy-model/`
**Prerequisites**: plan.md, spec.md, data-model.md, contracts/, research.md, quickstart.md

**Tests**: Included as implementation tasks (not TDD-first) — spec does not request TDD approach.

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

---

## Phase 1: Setup

**Purpose**: Shared enums, constants, and model scaffolding used by all stories

- [X] T001 [P] Create `RegisterPolicy` value object with four config sections, `ApprovedValidator` value object, and `CreateDefault()` factory in `src/Common/Sorcha.Register.Models/RegisterPolicy.cs`
- [X] T002 [P] Add enums (`QuorumFormula`, `RegistrationMode`, `ElectionMechanism`, `TransitionMode`) to `src/Common/Sorcha.Register.Models/GovernanceModels.cs`
- [X] T003 [P] Create `SystemRegisterConstants` static class (deterministic ID, well-known names, env var names) in `src/Common/Sorcha.Register.Models/Constants/SystemRegisterConstants.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core model integration and validation that MUST complete before any user story

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Add nullable `RegisterPolicy?` property to `RegisterControlRecord` with `JsonIgnore(WhenWritingNull)` in `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs`
- [X] T005 Add `PolicyUpdate = 9` to `ControlActionType` enum and `"control.policy.update"` action ID constant — added to `IControlDocketProcessor.cs` (Validator.Service) and `ControlDocketProcessor.cs`
- [X] T006 [P] Create `RegisterPolicyValidator` (and child validators) using FluentValidation in `src/Core/Sorcha.Register.Core/Validation/RegisterPolicyValidator.cs`
- [X] T007 [P] Create `RegisterPolicyService` for policy resolution from control chain in `src/Core/Sorcha.Register.Core/Services/RegisterPolicyService.cs`
- [X] T008 [P] Add policy query methods (`GetRegisterPolicyAsync`, `GetPolicyHistoryAsync`) to `IRegisterServiceClient` in `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs`
- [X] T009 [P] Implement policy query methods in `RegisterServiceClient` in `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs`
- [X] T010 Unit tests for `RegisterPolicy.CreateDefault()`, model serialization, and all validators in `tests/Sorcha.Register.Models.Tests/RegisterPolicyTests.cs`
- [X] T011 Unit tests for `RegisterPolicyService` policy resolution and default fallback in `tests/Sorcha.Register.Core.Tests/Services/RegisterPolicyServiceTests.cs`

**Checkpoint**: Foundation ready — RegisterPolicy model, validation, and service client wired. User story implementation can begin.

---

## Phase 3: User Story 1 — Register Creator Sets Policy at Genesis (P1) MVP

**Goal**: Register creators can specify operational policy at creation time; default policy applied when omitted; backward-compatible with pre-feature registers.

**Independent Test**: Create a register with custom policy (consent-mode, supermajority quorum), verify genesis TX payload contains those settings. Create without policy, verify defaults applied.

### Implementation

- [X] T012 [US1] Add optional `RegisterPolicy? Policy` property to `InitiateRegisterCreationRequest` in `src/Common/Sorcha.Register.Models/RegisterCreationModels.cs`
- [X] T013 [US1] Modify `RegisterCreationOrchestrator.InitiateAsync()` to embed policy (or defaults) into the genesis Control record in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [X] T014 [US1] Modify `GenesisConfigService` to read from `RegisterPolicy` when present on the control record, preserving existing legacy fallback chain, in `src/Services/Sorcha.Validator.Service/Services/GenesisConfigService.cs`
- [X] T015 [US1] Add `GET /api/registers/{registerId}/policy` endpoint returning `RegisterPolicyResponse` (with `isDefault` flag) in `src/Services/Sorcha.Register.Service/Endpoints/` (new or existing endpoints file)
- [X] T016 [US1] Add YARP route for `/api/registers/{registerId}/policy` in `src/Services/Sorcha.ApiGateway/` configuration
- [X] T017 [US1] Unit tests for creation-with-policy and creation-without-policy (defaults) in `tests/Sorcha.Register.Service.Tests/RegisterCreationPolicyTests.cs`
- [X] T018 [US1] Unit tests for `GenesisConfigService` three-tier fallback (policy present, legacy parsing, hardcoded defaults) in `tests/Sorcha.Validator.Service.Tests/GenesisConfigFallbackTests.cs`

**Checkpoint**: Registers can be created with explicit policy or defaults. Pre-feature registers continue working via fallback. Policy queryable via API.

---

## Phase 4: User Story 2 — Platform Operator Bootstraps System Register (P1)

**Goal**: System Register with deterministic ID auto-created on startup when env flag set. Contains system blueprints. Idempotent.

**Independent Test**: Start with `SORCHA_SEED_SYSTEM_REGISTER=true`, verify System Register exists with deterministic ID, system blueprints published as transactions.

### Implementation

- [X] T019 [P] [US2] Create `SystemRegisterConfiguration` for env var binding (`SORCHA_SEED_SYSTEM_REGISTER`, `SORCHA_SYSTEM_REGISTER_BLUEPRINT`) in `src/Services/Sorcha.Register.Service/Configuration/SystemRegisterConfiguration.cs`
- [X] T020 [US2] Create `SystemRegisterBootstrapper` as `IHostedService` — check env flag, check idempotency, create system-setup wallet, call `RegisterCreationOrchestrator`, publish system blueprints — in `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs`
- [X] T021 [US2] Modify `SystemRegisterService` to support on-chain register queries (get info, list blueprints, get blueprint by ID/version) in `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs`
- [X] T022 [US2] Create System Register endpoints (`GET /api/system-register`, `GET /api/system-register/blueprints`, `GET /api/system-register/blueprints/{id}`, `GET /api/system-register/blueprints/{id}/versions/{v}`) in `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs`
- [X] T023 [US2] Add YARP routes for `/api/system-register/**` in API Gateway configuration
- [X] T024 [US2] Register `SystemRegisterBootstrapper` and `SystemRegisterConfiguration` in DI in `src/Services/Sorcha.Register.Service/Program.cs` (or extensions)
- [X] T025 [US2] Unit tests for bootstrap idempotency, env flag gating, deterministic ID generation in `tests/Sorcha.Register.Service.Tests/SystemRegisterBootstrapTests.cs`

**Checkpoint**: System Register bootstraps automatically in dev mode. System blueprints queryable via API. Idempotent on restart.

---

## Phase 5: User Story 3 — Register Admin Manages Approved Validator List (P2)

**Goal**: Consent-mode registers enforce on-chain approved validator list. Only approved validators can register in Redis. Public-mode registers skip the check.

**Independent Test**: Create consent-mode register, approve a validator via governance, verify it can register in Redis. Verify unapproved validator is rejected.

### Implementation

- [X] T026 [US3] Modify `ValidatorRegistry.RegisterAsync()` to check on-chain `approvedValidators` list when `registrationMode == consent` in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [X] T027 [US3] Add `GET /api/registers/{registerId}/validators/approved` endpoint returning `ApprovedValidatorsResponse` in `src/Services/Sorcha.Register.Service/Endpoints/` (policy or validators endpoints file)
- [X] T028 [US3] Add `GET /api/registers/{registerId}/validators/operational` endpoint returning `OperationalValidatorsResponse` in `src/Services/Sorcha.Register.Service/Endpoints/` (same file as T027)
- [X] T029 [US3] Add YARP routes for `/api/registers/{registerId}/validators/approved` and `/api/registers/{registerId}/validators/operational` in API Gateway configuration
- [X] T030 [US3] Unit tests for consent-mode registration gating (approved → allowed, unapproved → rejected, public mode → no check) in `tests/Sorcha.Validator.Service.Tests/ValidatorRegistryConsentTests.cs`

**Checkpoint**: Consent-mode registers enforce approved validator list. Approved and operational validators queryable via API.

---

## Phase 6: User Story 4 — Register Admin Updates Policy via Governance (P2)

**Goal**: Policy updates via `control.policy.update` Control transactions. Validated before vote. Quorum formula parameterized. Transition modes for public-to-consent switch.

**Independent Test**: Create register with default policy, propose policy change (public→consent), achieve quorum, verify new policy active.

### Implementation

- [X] T031 [P] [US4] Create `PolicyUpdatePayload` DTO (extends ControlPayload pattern) with Policy, TransitionMode, UpdatedBy in `src/Common/Sorcha.Register.Models/GovernanceModels.cs`
- [X] T032 [US4] Add `control.policy.update` handler to `ControlDocketProcessor` — extract, validate (version = current+1, FluentValidation), apply to control record, emit event — in `src/Services/Sorcha.Validator.Service/Services/ControlDocketProcessor.cs`
- [X] T033 [US4] Parameterize `GovernanceRosterService.GetQuorumThreshold()` to accept `QuorumFormula` and calculate accordingly (strict-majority, supermajority, unanimous) in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [X] T034 [US4] Add `POST /api/registers/{registerId}/policy/update` endpoint with validation and `PolicyUpdateResponse` in `src/Services/Sorcha.Register.Service/Endpoints/`
- [X] T035 [US4] Add `GET /api/registers/{registerId}/policy/history` endpoint with pagination in `src/Services/Sorcha.Register.Service/Endpoints/`
- [X] T036 [US4] Add YARP routes for policy update and history endpoints in API Gateway configuration (covered by existing catch-all route)
- [X] T037 [US4] Implement transition mode enforcement — when policy commits with `registrationMode` change public→consent, apply `immediate` or `grace-period` logic to Redis TTL refresh in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [X] T038 [US4] Unit tests for `control.policy.update` processing (valid update, version conflict, validation failure) in `tests/Sorcha.Validator.Service.Tests/PolicyUpdateProcessorTests.cs`
- [X] T039 [US4] Unit tests for parameterized quorum calculation (all three formulas, edge cases m=1,2,3) in `tests/Sorcha.Register.Core.Tests/GovernanceQuorumFormulaTests.cs`

**Checkpoint**: Policy updates flow through governance. Quorum formula configurable per-register. Transition modes enforced.

---

## Phase 7: User Story 5 — System Register Disseminates Blueprint Updates (P3)

**Goal**: New blueprint versions published to System Register, replicated to peers, registers adopt via governance vote.

**Independent Test**: Publish a new blueprint version to System Register, verify it's queryable. Register proposes blueprint version upgrade via governance.

### Implementation

- [X] T040 [US5] Extend `SystemRegisterService` to support `control.blueprint.publish` for new blueprint versions in `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs`
- [X] T041 [US5] Add blueprint version validation — reject governance proposals referencing non-existent blueprint versions — in `src/Core/Sorcha.Register.Core/Services/RegisterPolicyService.cs`
- [X] T042 [US5] Verify System Register replicates to peers via existing peer sync — confirm blueprint transactions appear on a second peer node (integration-level verification against `src/Services/Sorcha.Peer.Service/`)
- [X] T043 [US5] Unit tests for blueprint version publishing and non-existent version rejection in `tests/Sorcha.Register.Service.Tests/SystemRegisterBlueprintTests.cs`

**Checkpoint**: Blueprint lifecycle complete — publish, replicate, adopt.

---

## Phase 8: User Story 6 — Validator Operational Presence via Heartbeat (P3)

**Goal**: Redis TTL for operational presence reads from policy's `operationalTtlSeconds`. Consensus engine respects `minValidators`.

**Independent Test**: Start validator, verify Redis entry with configured TTL. Stop validator, wait for TTL, confirm removed from operational list.

### Implementation

- [X] T044 [US6] Modify `ValidatorRegistryConfiguration` to support per-register operational TTL from policy (default 60s) in `src/Services/Sorcha.Validator.Service/Configuration/ValidatorRegistryConfiguration.cs`
- [X] T045 [US6] Modify `ValidatorRegistry` heartbeat refresh to use policy's `operationalTtlSeconds` for Redis TTL in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [X] T046 [US6] Verify consensus engine checks `minValidators` against active Redis entries before docket building — add check if missing, add test if already present — in `src/Services/Sorcha.Validator.Service/Services/` (docket builder or consensus service)
- [X] T047 [US6] Unit tests for TTL configuration from policy, heartbeat refresh, and validator disappears within 2x TTL window (SC-005) in `tests/Sorcha.Validator.Service.Tests/ValidatorOperationalPresenceTests.cs`

**Checkpoint**: Operational presence TTL driven by policy. Min-validator enforcement in consensus.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and validation across all stories

- [X] T048 [P] Update Register Service README with new policy and system register endpoints
- [X] T049 [P] Update `docs/reference/API-DOCUMENTATION.md` with all new endpoints from contracts/
- [X] T050 [P] Update `docs/reference/development-status.md` with feature 048 completion
- [X] T051 [P] Add XML `<summary>` docs to all new public types and methods
- [X] T052 [P] Add structured logging for policy reads, updates, fallbacks, and System Register bootstrap operations
- [X] T053 Update `.specify/MASTER-TASKS.md` with feature 048 status
- [X] T054 Run `quickstart.md` validation — execute all curl commands and verify responses match expected shapes

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 - P1)**: Depends on Phase 2 — MVP target
- **Phase 4 (US2 - P1)**: Depends on Phase 2 — can run in parallel with US1
- **Phase 5 (US3 - P2)**: Depends on Phase 2 + US1 (needs policy model on-chain)
- **Phase 6 (US4 - P2)**: Depends on Phase 2 + US1 (needs existing policy to update)
- **Phase 7 (US5 - P3)**: Depends on US2 (needs System Register)
- **Phase 8 (US6 - P3)**: Depends on US1 (needs policy with operationalTtlSeconds)
- **Phase 9 (Polish)**: Depends on all desired user stories being complete

### User Story Dependencies

```
Phase 1 (Setup)
    │
Phase 2 (Foundational)
    │
    ├──→ US1 (Genesis Policy) ──┬──→ US3 (Approved Validators)
    │                           ├──→ US4 (Policy Updates)
    │                           └──→ US6 (Operational Presence)
    │
    └──→ US2 (System Register) ──→ US5 (Blueprint Dissemination)
```

### Within Each User Story

- Models/DTOs before services
- Services before endpoints
- Endpoints before YARP routes
- Core implementation before tests
- Story complete before moving to next priority

### Parallel Opportunities

- **Phase 1**: All 3 tasks (T001-T003) can run in parallel
- **Phase 2**: T006, T007, T008, T009 can run in parallel (after T004/T005)
- **Phase 3 + Phase 4**: US1 and US2 can run in parallel after Phase 2
- **Phase 5 + Phase 6**: US3 and US4 can run in parallel after US1
- **Phase 7 + Phase 8**: US5 and US6 can run in parallel after their respective dependencies

---

## Parallel Example: Phase 1 (Setup)

```
Agent 1: T001 — RegisterPolicy.cs (model + ApprovedValidator + factory)
Agent 2: T002 — All enums in GovernanceModels.cs
Agent 3: T003 — SystemRegisterConstants.cs
```

## Parallel Example: US1 + US2 (after Foundational)

```
Agent A (US1): T012 → T013 → T014 → T015 → T016 → T017 → T018
Agent B (US2): T019 → T020 → T021 → T022 → T023 → T024 → T025
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T011)
3. Complete Phase 3: User Story 1 (T012-T018)
4. **STOP and VALIDATE**: Create register with/without policy, query policy, verify backward compat
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 (Genesis Policy) → Test independently → **MVP!**
3. US2 (System Register) → Test independently → System blueprints available
4. US3 + US4 (Approved Validators + Policy Updates) → Test independently → Full governance
5. US5 + US6 (Blueprints + Presence) → Test independently → Complete feature
6. Polish → Documentation and validation → PR ready

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable after its dependencies
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- T002 consolidates all 4 enums into one `GovernanceModels.cs` task (merged from original T002+T003)
- T001 consolidates RegisterPolicy + ApprovedValidator into one `RegisterPolicy.cs` task (merged from original T001+T005)
