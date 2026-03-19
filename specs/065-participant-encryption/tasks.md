# Tasks: Participant Resolution, Starting Action Binding & Field-Level Encryption

**Input**: Design documents from `/specs/065-participant-encryption/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — the spec requires E2E integration testing via the council credential flow, plus unit tests for validation logic.

**Organization**: Tasks grouped by user story. US1+US2 are co-P1 and tightly coupled. US3 (DevMode) and US4 (Encryption) are sequential.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story (US1, US2, US3, US4)
- Exact file paths included

---

## Phase 1: Setup

**Purpose**: Branch preparation and shared model changes

- [ ] T001 Make `WalletAddress` optional on `Participant` model in `src/Common/Sorcha.Blueprint.Models/Participant.cs` — remove `[Required]` attribute, keep field as nullable string
- [ ] T002 [P] Add `DevMode` boolean field to `Register` model in `src/Common/Sorcha.Register.Models/Register.cs` — default `false`
- [ ] T003 [P] Add `DevMode` field to MongoDB register document in `src/Core/Sorcha.Register.Storage.MongoDB/Models/MongoRegisterDocument.cs`
- [ ] T004 Verify solution builds cleanly after model changes: `dotnet build`

**Checkpoint**: Model changes compile. No behaviour change yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Participant resolution endpoint needed by both US1 and US2

**CRITICAL**: US1 and US2 cannot proceed without the register participant resolve endpoint.

- [ ] T005 Add `GET /api/registers/{registerId}/participants/resolve` endpoint to `src/Services/Sorcha.Register.Service/Endpoints/ParticipantEndpoints.cs` — accepts `participantName` and `organisationName` query params, returns published participant record with addresses
- [ ] T006 [P] Add unit test for participant resolve endpoint in `tests/Sorcha.Register.Service.Tests/Endpoints/ParticipantResolveEndpointTests.cs` — test found, not-found, and revoked cases
- [ ] T007 Add YARP route for participant resolve endpoint in `src/Services/Sorcha.ApiGateway/appsettings.json` — route to register-cluster with RequireAuthenticated policy
- [ ] T008 Verify Register Service builds and participant resolve endpoint returns data: `dotnet test tests/Sorcha.Register.Service.Tests/`

**Checkpoint**: Participant resolve endpoint functional. US1/US2 can proceed.

---

## Phase 3: User Story 1 — Any Wallet Starts a Workflow (Priority: P1)

**Goal**: Starting actions accept any wallet. Sender binds to participant role for instance lifetime.

**Independent Test**: Publish blueprint with participant "citizen" (no wallet), execute starting action with any wallet, verify action accepted and wallet bound.

### Tests for User Story 1

- [ ] T009 [P] [US1] Unit test for starting action binding in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionStartingActionTests.cs` — test: any wallet accepted on starting action, wallet bound in ParticipantWallets, second submission from same wallet succeeds, submission from different wallet for same role rejects
- [ ] T010 [P] [US1] Unit test for VAL_BP_002 starting action skip in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineStartingActionTests.cs` — test: starting action skips wallet validation, non-starting action without binding rejects

### Implementation for User Story 1

- [ ] T011 [US1] Modify `ActionExecutionService.ExecuteAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — when `action.IsStartingAction` and `instance.ParticipantWallets[senderParticipantId]` is empty, bind sender wallet to participant. Reject if already bound to different wallet.
- [ ] T012 [US1] Modify `ValidationEngine.ValidateBlueprintConformanceAsync` in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — for `isStartingAction: true`, skip VAL_BP_002 sender wallet check (accept any wallet)
- [ ] T013 [US1] Update `CouncilCredentialFlowTests.cs` in `tests/Sorcha.UI.E2E.Tests/Docker/CouncilCredentialFlowTests.cs` — remove `"pending-citizen-wallet"` fallback from blueprint publishing, verify citizen wallet binding works end-to-end
- [ ] T014 [US1] Rebuild validator Docker image and run E2E test to verify starting action accepted: `docker-compose build validator-service && dotnet test tests/Sorcha.UI.E2E.Tests/ --filter "Category=LongRunning"`

**Checkpoint**: Citizen can start a workflow with any wallet. Action 0 accepted. Wallet bound.

---

## Phase 4: User Story 2 — Organisational Participants from Published Records (Priority: P1)

**Goal**: Council staff actions resolved from published participant records on register, not hardcoded wallets.

**Independent Test**: Publish participant record for "ID Department" with two wallets, submit action from each, both accepted.

**Dependencies**: T005 (participant resolve endpoint), T012 (VAL_BP_002 changes)

### Tests for User Story 2

- [ ] T015 [P] [US2] Unit test for register-based participant resolution in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineParticipantResolutionTests.cs` — test: organisational participant resolved from register, multiple wallets accepted, revoked participant rejected, missing participant rejected
- [ ] T016 [P] [US2] Unit test for disclosure wallet resolution in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionDisclosureResolutionTests.cs` — test: disclosure maps participant to register wallet when not in instance bindings

### Implementation for User Story 2

- [ ] T017 [US2] Extend `ValidationEngine.ValidateBlueprintConformanceAsync` in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — for non-starting actions: (1) check `instance.ParticipantWallets`, (2) if not found, query register participant resolve endpoint by participant name + organisation, (3) check if signer wallet is in resolved addresses
- [ ] T018 [US2] Inject `IRegisterServiceClient` into `ValidationEngine` (if not already present) for participant lookup in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T019 [US2] Extend disclosure wallet resolution in `ActionExecutionService.ApplyDisclosures` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — when `instance.ParticipantWallets` lacks a participant, resolve from register participant index before warning
- [ ] T020 [US2] Update E2E test: remove hardcoded wallet addresses from blueprint templates in `blueprints/templates/council-id-application-template.json` and `council-service-request-template.json` — organisational participants should have no walletAddress, only role + organisation
- [ ] T021 [US2] Rebuild validator + blueprint-service Docker images and run E2E test to verify ID Dept / Service Dept / Return Dept actions resolve correctly

**Checkpoint**: Full council credential flow works — citizen starts, departments act on their steps. No hardcoded wallet addresses in blueprints.

---

## Phase 5: User Story 3 — DevMode Per-Register (Priority: P2)

**Goal**: DevMode registers store plaintext payloads. Disclosure filtering at read time.

**Independent Test**: Create DevMode register, execute action, verify plaintext payload in MongoDB. Query as different participant, verify disclosure filtering.

**Dependencies**: Phase 3 + Phase 4 complete (participant resolution working)

### Tests for User Story 3

- [ ] T022 [P] [US3] Unit test for DevMode register initiation in `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterInitiateDevModeTests.cs` — test: devMode parameter accepted, stored on register
- [ ] T023 [P] [US3] Unit test for DevMode toggle endpoint in `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterDevModeToggleTests.cs` — test: enable/disable DevMode, only owner/admin can toggle
- [ ] T024 [P] [US3] Unit test for plaintext path selection in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionDevModeTests.cs` — test: DevMode register skips encryption, non-DevMode register encrypts

### Implementation for User Story 3

- [ ] T025 [US3] Add `devMode` parameter to register initiate request in `src/Services/Sorcha.Register.Service/Endpoints/RegisterEndpoints.cs` — pass through to register creation, store on register document
- [ ] T026 [US3] Add `PUT /api/registers/{registerId}/devmode` toggle endpoint in `src/Services/Sorcha.Register.Service/Endpoints/RegisterEndpoints.cs` — accepts `{ "enabled": bool }`, requires register owner or SystemAdmin
- [ ] T027 [US3] Add YARP route for DevMode toggle endpoint in `src/Services/Sorcha.ApiGateway/appsettings.json`
- [ ] T028 [US3] Modify `ActionExecutionService.ExecuteAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — before encryption pipeline, query register DevMode status. If DevMode, use plaintext transaction builder path. If not, proceed with encryption.
- [ ] T029 [US3] Add register DevMode query to `IRegisterServiceClient` if not present — `GetRegisterAsync(registerId)` should return DevMode flag
- [ ] T030 [US3] Update E2E test `CreateRegisterAsync` to pass `devMode: true` in register initiation in `tests/Sorcha.UI.E2E.Tests/Docker/CouncilCredentialFlowTests.cs`
- [ ] T031 [US3] Rebuild all modified Docker images and run full E2E test — verify complete council flow executes with plaintext payloads

**Checkpoint**: E2E council credential flow completes end-to-end in DevMode. Payloads visible as plaintext in MongoDB.

---

## Phase 6: User Story 4 — Field-Level Encryption (Priority: P3)

**Goal**: Non-DevMode registers use envelope encryption with disclosure groups.

**Independent Test**: Create non-DevMode register, execute action with divergent disclosures, verify encrypted storage and per-participant decryption.

**Dependencies**: Phase 5 complete (DevMode provides the toggle; encryption is the non-DevMode path)

### Tests for User Story 4

- [ ] T032 [P] [US4] Unit test for disclosure group encryption in `tests/Sorcha.TransactionHandler.Tests/Encryption/DisclosureGroupEncryptionTests.cs` — test: 2 recipients with same disclosure → 1 group, 2 recipients with different disclosure → 2 groups, atomic failure on key wrap error
- [ ] T033 [P] [US4] Unit test for recipient key resolution from instance bindings + register in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionEncryptionTests.cs` — test: bound participant keys resolved, register participant keys resolved, revoked participant fails

### Implementation for User Story 4

- [ ] T034 [US4] Verify `ResolveRecipientKeysAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` resolves keys from both instance bindings and register participant index — extend if needed
- [ ] T035 [US4] Verify `EncryptionPipelineService.EncryptDisclosedPayloadsAsync` in `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs` handles resolved keys correctly — no changes expected, but integration test needed
- [ ] T036 [US4] Create non-DevMode register in E2E test and execute action — verify payload stored encrypted in MongoDB
- [ ] T037 [US4] Verify decryption path: participant queries return only their disclosed fields via the existing action query endpoint
- [ ] T038 [US4] Verify size limit enforcement: submit oversized payload, confirm clear error before encryption

**Checkpoint**: Full encrypt/decrypt round-trip works. Disclosure groups optimise correctly.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and validation

- [ ] T039 [P] Update blueprint templates README in `blueprints/README.md` — document that walletAddress is optional on participants
- [ ] T040 [P] Update `COUNCIL-CREDENTIAL-FLOW-FINDINGS.md` in `tests/Sorcha.UI.E2E.Tests/Docker/` — mark resolved issues, document DevMode usage
- [ ] T041 [P] Update `docs/reference/development-status.md` — mark participant resolution and DevMode encryption as complete
- [ ] T042 Run full test suite: `dotnet test` — verify no regressions across all 30 test projects
- [ ] T043 Run E2E test twice consecutively to verify idempotency: `docker-compose down -v && docker-compose up -d && dotnet test --filter "Category=LongRunning"`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — model changes only
- **Phase 2 (Foundational)**: Depends on Phase 1 — participant resolve endpoint
- **Phase 3 (US1)**: Depends on Phase 2 — starting action binding
- **Phase 4 (US2)**: Depends on Phase 2 + T012 from Phase 3 — organisational resolution
- **Phase 5 (US3)**: Depends on Phase 3 + Phase 4 — DevMode toggle
- **Phase 6 (US4)**: Depends on Phase 5 — encryption wiring
- **Phase 7 (Polish)**: Depends on all previous phases

### User Story Dependencies

- **US1 + US2 (P1)**: Tightly coupled — both modify VAL_BP_002. US2 depends on T012 from US1. Can be done sequentially in one pass.
- **US3 (P2)**: Depends on US1+US2 (participant resolution needed for DevMode to be useful)
- **US4 (P3)**: Depends on US3 (DevMode provides the toggle; US4 implements the encrypted path)

### Within Each User Story

- Tests written first (TDD)
- Model/interface changes before implementation
- Validator changes before Blueprint Service changes (pipeline flows validator → register)
- E2E verification at each checkpoint

### Parallel Opportunities

- T002 + T003 (model changes in different files)
- T009 + T010 (US1 tests in different test projects)
- T015 + T016 (US2 tests in different test projects)
- T022 + T023 + T024 (US3 tests in three different test projects)
- T032 + T033 (US4 tests in different test projects)
- T039 + T040 + T041 (documentation updates)

---

## Parallel Example: User Story 1

```text
# Launch US1 tests in parallel (different test projects):
Task T009: "Unit test for starting action binding in tests/Sorcha.Blueprint.Service.Tests/"
Task T010: "Unit test for VAL_BP_002 starting action skip in tests/Sorcha.Validator.Service.Tests/"

# Then implement sequentially:
Task T011: ActionExecutionService binding logic
Task T012: ValidationEngine starting action skip
Task T013: E2E test update
Task T014: Docker rebuild + E2E verification
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1: Model changes (T001-T004)
2. Complete Phase 2: Participant resolve endpoint (T005-T008)
3. Complete Phase 3: Starting action binding (T009-T014)
4. Complete Phase 4: Organisational resolution (T015-T021)
5. **STOP and VALIDATE**: Run E2E council credential flow end-to-end
6. This alone unblocks the entire E2E test suite

### Incremental Delivery

1. US1+US2 → Council flow works with plaintext (existing path) → **MVP**
2. US3 → DevMode makes plaintext explicit and toggleable → Development convenience
3. US4 → Encryption wiring → Full DAD security model → Production readiness

---

## Notes

- Total tasks: **43**
- US1: 6 tasks | US2: 7 tasks | US3: 10 tasks | US4: 7 tasks
- Setup: 4 tasks | Foundational: 4 tasks | Polish: 5 tasks
- Parallel opportunities: 6 groups (12 tasks parallelizable)
- Suggested MVP: US1 + US2 (Phases 1-4, 21 tasks) — unblocks entire E2E flow
- Docker image rebuilds needed: validator-service (US1), blueprint-service (US2), register-service (US3)
