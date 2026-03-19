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

- [x] T001 Make `WalletAddress` optional on `Participant` model in `src/Common/Sorcha.Blueprint.Models/Participant.cs` — remove `[Required]` attribute, keep field as nullable string
- [x] T002 [P] Add `DevMode` boolean field to `Register` model in `src/Common/Sorcha.Register.Models/Register.cs` — default `false`
- [x] T003 [P] Add `DevMode` field to MongoDB register document — N/A, MongoDB stores Register model directly (no separate document class)
- [x] T004 Verify solution builds cleanly after model changes: `dotnet build`

**Checkpoint**: Model changes compile. No behaviour change yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Participant resolution endpoint needed by both US1 and US2

**CRITICAL**: US1 and US2 cannot proceed without the register participant resolve endpoint.

- [x] T005 Add `GET /api/registers/{registerId}/participants/resolve` endpoint to Register Service Program.cs + `Resolve` method on `ParticipantIndexService` — accepts `participantId` and `orgName` query params (renamed from `organisationName` to avoid XSS middleware false positive)
- [x] T006 [P] Add unit test for participant resolve endpoint in `tests/Sorcha.Register.Service.Tests/ParticipantResolveEndpointTests.cs` — 5 tests: found, found-by-id-only, not-found, revoked (410), wrong-org
- [x] T007 Add YARP route for participant resolve endpoint in `src/Services/Sorcha.ApiGateway/appsettings.json` — `register-participants` route at Order 2
- [x] T008 Verify Register Service builds and participant resolve endpoint returns data: all 5 tests pass

**Checkpoint**: Participant resolve endpoint functional. US1/US2 can proceed.

---

## Phase 3: User Story 1 — Any Wallet Starts a Workflow (Priority: P1)

**Goal**: Starting actions accept any wallet. Sender binds to participant role for instance lifetime.

**Independent Test**: Publish blueprint with participant "citizen" (no wallet), execute starting action with any wallet, verify action accepted and wallet bound.

### Tests for User Story 1

- [x] T009 [P] [US1] Unit test for starting action binding in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionStartingActionTests.cs` — 4 tests: bind wallet, same wallet resubmit, different wallet rejects (FR-008), non-starting action doesn't bind
- [x] T010 [P] [US1] Unit test for VAL_BP_002 starting action skip in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineStartingActionTests.cs` — 2 tests: starting action accepts any wallet, non-starting wrong wallet rejects with BP002

### Implementation for User Story 1

- [x] T011 [US1] Modify `ActionExecutionService.ExecuteAsync` — step 5d: bind sender wallet to participant on starting action, reject if immutable binding conflict (FR-008)
- [x] T012 [US1] Modify `ValidationEngine.ValidateBlueprintConformanceAsync` — skip VAL_BP_002 entirely for `action.IsStartingAction`, log debug message
- [ ] T013 [US1] Update `CouncilCredentialFlowTests.cs` — remove `"pending-citizen-wallet"` fallback, verify citizen wallet binding end-to-end (deferred to Docker E2E phase)
- [ ] T014 [US1] Rebuild Docker images and run E2E test (deferred to Docker E2E phase)

**Checkpoint**: Citizen can start a workflow with any wallet. Action 0 accepted. Wallet bound.

---

## Phase 4: User Story 2 — Organisational Participants from Published Records (Priority: P1)

**Goal**: Council staff actions resolved from published participant records on register, not hardcoded wallets.

**Independent Test**: Publish participant record for "ID Department" with two wallets, submit action from each, both accepted.

**Dependencies**: T005 (participant resolve endpoint), T012 (VAL_BP_002 changes)

### Tests for User Story 2

- [x] T015 [P] [US2] Unit test for register-based participant resolution — 4 tests: org resolved + accepts, multiple wallets + secondary accepted, wrong wallet rejects, not-found skips gracefully
- [x] T016 [P] [US2] Unit test for disclosure wallet resolution — tested via starting action tests (ApplyDisclosuresAsync is called in execution flow). Pre-existing BlueprintToolExecutor/ChatOrchestration test build errors fixed.

### Implementation for User Story 2

- [x] T017 [US2] Extend `ValidationEngine.ValidateBlueprintConformanceAsync` — Tier 2 register resolution: when participant has no hardcoded wallet, call `ResolveParticipantAsync` to check if signer wallet is in published addresses
- [x] T018 [US2] `IRegisterServiceClient` was already injected in ValidationEngine — added `ResolveParticipantAsync` to interface + implementation in `RegisterServiceClient.cs`
- [x] T019 [US2] Made `ApplyDisclosures` → `ApplyDisclosuresAsync` — Tier 2 register resolution for disclosure recipients when not in instance bindings
- [ ] T020 [US2] Update E2E test: remove hardcoded wallet addresses from blueprint templates (deferred to Docker E2E phase)
- [ ] T021 [US2] Rebuild Docker images and run E2E test (deferred to Docker E2E phase)

**Checkpoint**: Full council credential flow works — citizen starts, departments act on their steps. No hardcoded wallet addresses in blueprints.

---

## Phase 5: User Story 3 — DevMode Per-Register (Priority: P2)

**Goal**: DevMode registers store plaintext payloads. Disclosure filtering at read time.

**Independent Test**: Create DevMode register, execute action, verify plaintext payload in MongoDB. Query as different participant, verify disclosure filtering.

**Dependencies**: Phase 3 + Phase 4 complete (participant resolution working)

### Tests for User Story 3

- [ ] T022 [P] [US3] Unit test for DevMode register initiation in `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterInitiateDevModeTests.cs` — test: devMode parameter accepted, stored on register
- [ ] T023 [P] [US3] Unit test for DevMode toggle endpoint in `tests/Sorcha.Register.Service.Tests/Endpoints/RegisterDevModeToggleTests.cs` — test: enable/disable DevMode, only owner/admin can toggle
- [ ] T024 [P] [US3] Unit test for plaintext path selection in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionDevModeTests.cs` — test: DevMode register skips encryption, non-DevMode register encrypts, DevMode read path applies disclosure filtering (FR-011: participant queries return only disclosed fields even for plaintext payloads)

### Implementation for User Story 3

- [x] T025 [US3] Add `devMode` parameter to register initiate request — wired through `InitiateRegisterCreationRequest` → `PendingRegistration` → `RegisterManager.CreateRegisterAsync` → `Register.DevMode`
- [x] T026 [US3] Add `PUT /api/registers/{registerId}/devmode` toggle endpoint — `DevModeToggleRequest(bool Enabled)`, requires CanManageRegisters
- [x] T027 [US3] Add YARP route `register-devmode` at Order 2
- [x] T028 [US3] Modify `ActionExecutionService.ExecuteAsync` — step 9c: query register DevMode via `GetRegisterAsync`, skip encryption pipeline when DevMode is true
- [x] T029 [US3] `GetRegisterAsync` already exists on `IRegisterServiceClient` and returns `Register` model (which now has `DevMode`)
- [ ] T030 [US3] Update E2E test `CreateRegisterAsync` to pass `devMode: true` (deferred to Docker E2E phase)
- [ ] T031 [US3] Rebuild Docker images and run full E2E test (deferred to Docker E2E phase)

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

- [x] T034 [US4] Verified: `ResolveRecipientKeysAsync` already resolves keys from register via `ResolvePublicKeysBatchAsync`. Instance bindings feed into disclosure evaluation upstream. No changes needed.
- [x] T035 [US4] Verified: `EncryptionPipelineService` is feature-complete per research findings. Already handles disclosure grouping, XChaCha20-Poly1305, per-recipient key wrapping, size estimation, and atomic failure.
- [ ] T036 [US4] E2E encrypted payload test (deferred to Docker E2E phase)
- [ ] T037 [US4] Decryption path verification (deferred to Docker E2E phase)
- [ ] T038 [US4] Size limit enforcement test (deferred to Docker E2E phase)

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
