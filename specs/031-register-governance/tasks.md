# Tasks: Register Governance — Genesis Blueprint & Decentralized Identity

**Input**: Design documents from `/specs/031-register-governance/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Rename TransactionType enum and create shared governance models used by all stories.

- [ ] T001 Rename `Genesis = 0` to `Control = 0` and remove `System = 3` in `src/Common/Sorcha.Register.Models/Enums/TransactionType.cs`
- [ ] T002 [P] Create `SorchaDidIdentifier` value object (parse, validate, format both DID types) in `src/Common/Sorcha.Register.Models/SorchaDidIdentifier.cs`
- [ ] T003 [P] Create governance enums (`GovernanceOperationType`, `ProposalStatus`) and models (`GovernanceOperation`, `ControlTransactionPayload`) in `src/Common/Sorcha.Register.Models/GovernanceModels.cs`
- [ ] T004 [P] Add `GetVotingMembers()`, `GetQuorumThreshold()` methods and increase attestation cap from 10 to 25 in `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs`
- [ ] T005 [P] Write unit tests for `SorchaDidIdentifier` (valid/invalid wallet DIDs, valid/invalid register DIDs, edge cases) in `tests/Sorcha.Register.Models.Tests/SorchaDidIdentifierTests.cs`
- [ ] T006 [P] Write unit tests for quorum calculation (`GetVotingMembers`, `GetQuorumThreshold` for m=1 through m=10, removal exclusion) in `tests/Sorcha.Register.Models.Tests/RegisterControlRecordQuorumTests.cs`
- [ ] T007 [P] Write unit tests for governance models (validation rules, roster cap 25, single Owner constraint) in `tests/Sorcha.Register.Models.Tests/GovernanceModelsTests.cs`
- [ ] T008 Run `dotnet build` and `dotnet test tests/Sorcha.Register.Models.Tests` to verify Phase 1 passes

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Update all TransactionType references across codebase. MUST complete before any user story.

**CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T009 Update `TransactionType.Genesis` → `TransactionType.Control` in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs` (line 559)
- [ ] T010 [P] Update `TransactionType.Genesis` → `TransactionType.Control` in `src/Services/Sorcha.Validator.Service/Services/BlueprintVersionResolver.cs` (line 335)
- [ ] T011 [P] Update `TransactionType.System` → `TransactionType.Control` in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs` (lines 302, 480)
- [ ] T012 [P] Update `TransactionType.Genesis` → `TransactionType.Control` in `src/Common/Sorcha.Validator.Core/Validators/ChainValidatorCore.cs` (lines 29, 140, 279, 304)
- [ ] T013 [P] Update `TransactionType.System` → `TransactionType.Control` in `tests/Sorcha.Validator.Service.Tests/Services/BlueprintVersionResolverTests.cs` (line 509)
- [ ] T014 [P] Update `TransactionType.Genesis` → `TransactionType.Control` in `tests/Sorcha.Validator.Core.Tests/Validators/ChainValidatorCoreTests.cs` (6 locations)
- [ ] T015 Search entire codebase for any remaining `TransactionType.Genesis` or `TransactionType.System` references and update
- [ ] T016 Run full `dotnet build` to verify zero compilation errors from enum rename
- [ ] T017 Run `dotnet test tests/Sorcha.Validator.Core.Tests` and `dotnet test tests/Sorcha.Validator.Service.Tests` to verify existing tests pass

**Checkpoint**: Foundation ready — TransactionType.Control is the only governance type. User story implementation can begin.

---

## Phase 3: User Story 1 — Register Creation with Ownership Assertion (Priority: P1) MVP

**Goal**: Every new register gets a genesis Control transaction with the creator as Owner, and the governance blueprint is automatically bound.

**Independent Test**: Create a register → verify genesis TX is Control type → verify roster has single Owner → verify governance blueprint bound.

### Tests for User Story 1

- [ ] T018 [P] [US1] Write tests for roster reconstruction from single genesis Control TX in `tests/Sorcha.Register.Core.Tests/Services/GovernanceRosterServiceTests.cs`
- [ ] T019 [P] [US1] Write tests for genesis Control transaction creation (Control type, DID format subject, single Owner payload) in `tests/Sorcha.Register.Service.Tests/Unit/GenesisControlTransactionTests.cs`
- [ ] T020 [P] [US1] Write tests for governance blueprint validation (passes validation, cycle warning, idempotent seeding) in `tests/Sorcha.Register.Service.Tests/Unit/GovernanceBlueprintSeedingTests.cs`

### Implementation for User Story 1

- [ ] T021 [P] [US1] Create `IGovernanceRosterService` interface and `GovernanceRosterService` (filters Control TXs, returns latest roster) in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T022 [P] [US1] Add `GetControlTransactionsAsync(registerId)` method to `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` and implement in `RegisterServiceClient.cs`
- [ ] T023 [P] [US1] Create governance blueprint JSON template in `examples/templates/register-governance-v1.json` per contracts/governance-api.md blueprint contract
- [ ] T024 [US1] Add filtered transaction query endpoint `GET /api/registers/{registerId}/transactions?type=Control` in Register Service `src/Services/Sorcha.Register.Service/Endpoints/` (or existing endpoint file)
- [ ] T025 [US1] Seed `register-governance-v1` blueprint in `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs` alongside existing `register-creation-v1`
- [ ] T026 [US1] Update `RegisterCreationOrchestrator.CreateGenesisTransaction()` to use `TransactionType.Control`, format attestation subjects as `did:sorcha:w:{walletAddress}` in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [ ] T027 [US1] Update `GenesisManager.CreateGenesisDocketAsync()` to handle Control type in `src/Services/Sorcha.Validator.Service/Services/GenesisManager.cs`
- [ ] T028 [US1] Bind governance blueprint instance to new register in `RegisterCreationOrchestrator.FinalizeAsync()` in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [ ] T029 [US1] Run all Register Service and Validator tests to verify no regressions: `dotnet test tests/Sorcha.Register.Service.Tests` and `dotnet test tests/Sorcha.Validator.Service.Tests`

**Checkpoint**: New registers have a genesis Control TX with Owner DID. Governance blueprint is bound. Roster reconstruction works for single-entry rosters.

---

## Phase 4: User Story 2 — Add Admin via Quorum (Priority: P1)

**Goal**: Admins can propose adding new members. Quorum (>50% voting) required unless Owner overrides. Target must counter-sign to accept.

**Independent Test**: Create register (1 Owner) → Owner adds Admin (bypasses quorum) → Both add third admin (quorum of 2 required) → Verify roster has 3 members.

### Tests for User Story 2

- [ ] T030 [P] [US2] Write tests for quorum collection: Owner bypass, m=1 (auto-approve), m=2 (both needed), m=3 (2 of 3) in `tests/Sorcha.Register.Core.Tests/Services/QuorumCollectionTests.cs`
- [ ] T031 [P] [US2] Write tests for add proposal: valid proposal creation, duplicate DID rejection, roster cap (25) rejection in `tests/Sorcha.Register.Core.Tests/Services/GovernanceProposalTests.cs`
- [ ] T032 [P] [US2] Write tests for acceptance flow: target accepts (roster updated), target declines (no change), 7-day expiry in `tests/Sorcha.Register.Core.Tests/Services/AcceptanceFlowTests.cs`

### Implementation for User Story 2

- [ ] T033 [US2] Add `ValidateQuorum()` method to `IGovernanceRosterService` — calculates adjusted pool, checks >50% threshold, returns `QuorumResult` in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T034 [US2] Implement governance proposal processing: validate proposer is admin, validate operation (Add), check target not in roster, check roster cap in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T035 [US2] Implement quorum collection logic: track approvals/rejections per proposal, detect quorum met or blocked, handle Owner bypass in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T036 [US2] Implement acceptance/decline handling: target counter-signs → build updated roster → create ControlTransactionPayload with full roster in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T037 [US2] Implement 7-day proposal expiration: set `ExpiresAt` on proposal creation, check expiry before accepting votes in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T038 [US2] Add structured logging for governance operations (proposal created, vote received, quorum met, roster updated) in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T039 [US2] Run all Register Core tests: `dotnet test tests/Sorcha.Register.Core.Tests`

**Checkpoint**: Add admin flow works end-to-end. Quorum calculation correct for m=1 through m=10. Owner can bypass. Proposals expire after 7 days.

---

## Phase 5: User Story 5 — DID Resolution (Priority: P2)

**Goal**: Resolve `did:sorcha:w:*` via Wallet Service and `did:sorcha:r:*:t:*` via Register/Peer Service. Extensible for future ZKP payloads.

**Independent Test**: Resolve wallet DID → get public key. Publish credential TX → resolve register DID from different peer → get public key.

### Tests for User Story 5

- [ ] T040 [P] [US5] Write tests for wallet DID resolution (valid address, unknown address, service unavailable) in `tests/Sorcha.Register.Core.Tests/Services/DIDResolverTests.cs`
- [ ] T041 [P] [US5] Write tests for register DID resolution (valid TX, unknown register, unknown TX, P2P fallback) in `tests/Sorcha.Register.Core.Tests/Services/DIDResolverTests.cs`
- [ ] T042 [P] [US5] Write tests for mixed-DID roster reconstruction (roster with both w: and r: DIDs resolves correctly) in `tests/Sorcha.Register.Core.Tests/Services/MixedDIDRosterTests.cs`

### Implementation for User Story 5

> **Historical (Feature 120 Phase 0, 2026-05-09):** The `IDIDResolver` interface and `DIDResolver` implementation referenced in T043–T046 were retired in favour of the W3C-shaped `IDidResolverRegistry` stack (`src/Common/Sorcha.ServiceClients.Http/Did/`). The legacy types had no consumers and were deleted together with their DI registration. Tasks below are preserved for traceability.

- [ ] T043 [P] [US5] Create `IDIDResolver` interface with `ResolveAsync(string did)` returning `DIDResolutionResult` (public key, algorithm, DID type) in `src/Core/Sorcha.Register.Core/Services/IDIDResolver.cs`
- [ ] T044 [US5] Implement `DIDResolver` — wallet DID via `IWalletServiceClient.GetWalletAsync()`, register DID via `IRegisterServiceClient.GetTransactionAsync()` in `src/Core/Sorcha.Register.Core/Services/DIDResolver.cs`
- [ ] T045 [US5] Add P2P fallback for register DID resolution when local register not available — use `IPeerServiceClient` in `src/Core/Sorcha.Register.Core/Services/DIDResolver.cs`
- [ ] T046 [US5] Register `IDIDResolver` in DI for Register Service and Validator Service
- [ ] T047 [US5] Run DID resolution tests: `dotnet test tests/Sorcha.Register.Core.Tests --filter "DIDResolver"`

**Checkpoint**: Both DID types resolve correctly. Mixed-DID rosters work. Graceful fallback when register unavailable.

---

## Phase 6: User Story 3 — Remove Admin via Quorum (Priority: P2)

**Goal**: Admins can propose removing a member. Target is excluded from quorum pool. Owner can bypass.

**Independent Test**: Register with 3 admins → propose removal of one → remaining 2 approve (both needed since >50% of 2) → roster shrinks to 2.

### Tests for User Story 3

- [ ] T048 [P] [US3] Write tests for removal quorum: target excluded from pool, adjusted m, Owner bypass in `tests/Sorcha.Register.Core.Tests/Services/RemovalQuorumTests.cs`
- [ ] T049 [P] [US3] Write tests for removal validation: target must exist, Owner cannot be removed via Remove (must use Transfer), removal leaves valid roster in `tests/Sorcha.Register.Core.Tests/Services/RemovalValidationTests.cs`

### Implementation for User Story 3

- [ ] T050 [US3] Extend `ValidateQuorum()` to exclude target DID from voting pool for Remove operations in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T051 [US3] Implement removal proposal validation: target exists in roster, target is not the Owner (Owner removal requires Transfer), roster remains valid after removal in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T052 [US3] Implement removal roster update: remove target attestation, build ControlTransactionPayload with updated roster in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T053 [US3] Run removal tests: `dotnet test tests/Sorcha.Register.Core.Tests --filter "Removal"`

**Checkpoint**: Remove admin works. Quorum correctly excludes target. Owner bypass works. Cannot remove Owner via Remove operation.

---

## Phase 7: User Story 4 — Transfer Ownership (Priority: P2)

**Goal**: Owner can transfer ownership to an existing Admin. No quorum — just Owner + target acceptance. Old Owner becomes Admin.

**Independent Test**: Register with Owner A + Admin B → Owner A transfers to Admin B → verify B is Owner, A is Admin, roster size unchanged.

### Tests for User Story 4

- [ ] T054 [P] [US4] Write tests for transfer validation: only Owner can propose, target must be Admin (not Auditor/Designer), only existing admin in `tests/Sorcha.Register.Core.Tests/Services/TransferOwnershipTests.cs`
- [ ] T055 [P] [US4] Write tests for transfer execution: old Owner demoted to Admin, new Owner promoted, roster size unchanged, single Owner invariant in `tests/Sorcha.Register.Core.Tests/Services/TransferOwnershipTests.cs`

### Implementation for User Story 4

- [ ] T056 [US4] Implement transfer proposal validation: proposer must be Owner, target must be existing Admin, no quorum routing in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T057 [US4] Implement transfer roster update: promote target to Owner, demote old Owner to Admin, build ControlTransactionPayload in `src/Core/Sorcha.Register.Core/Services/GovernanceRosterService.cs`
- [ ] T058 [US4] Run transfer tests: `dotnet test tests/Sorcha.Register.Core.Tests --filter "Transfer"`

**Checkpoint**: Ownership transfer works. Old Owner becomes Admin. Only existing Admins can receive ownership. Roster size never decreases.

---

## Phase 8: User Story 6 — Validator Rights Enforcement (Priority: P3)

**Goal**: Validator reconstructs roster from Control chain and rejects governance TXs from non-admins. Zero Tenant Service dependency.

**Independent Test**: Submit Control TX from non-admin → rejected. Submit from admin → accepted. Remove admin, re-submit → rejected.

### Tests for User Story 6

- [ ] T059 [P] [US6] Write tests for rights enforcement: non-admin rejected, admin accepted, removed-admin rejected, Owner-only transfer validation in `tests/Sorcha.Validator.Service.Tests/Services/RightsEnforcementServiceTests.cs`
- [ ] T060 [P] [US6] Write tests for quorum verification in validator: quorum met → accept, quorum not met → reject, Owner bypass → accept in `tests/Sorcha.Validator.Service.Tests/Services/RightsEnforcementServiceTests.cs`
- [ ] T061 [P] [US6] Write tests for validation pipeline integration: Control TX triggers rights check, Action TX skips governance check, config flag disables check in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineGovernanceTests.cs`

### Implementation for User Story 6

- [ ] T062 [US6] Create `IRightsEnforcementService` interface with `ValidateGovernanceRightsAsync()` in `src/Services/Sorcha.Validator.Service/Services/IRightsEnforcementService.cs`
- [ ] T063 [US6] Implement `RightsEnforcementService` — reconstruct roster via `IGovernanceRosterService`, verify submitter DID in roster, check role for operation type in `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs`
- [ ] T064 [US6] Add `EnableGovernanceValidation` config flag to `ValidationEngineConfiguration` in Validator Service
- [ ] T065 [US6] Insert governance rights check in `ValidationEngine.ValidateTransactionAsync()` between structure and schema validation — only for `TransactionType.Control` in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T066 [US6] Verify quorum requirements in `RightsEnforcementService`: for Add/Remove check >50%, for Transfer check Owner + target acceptance in `src/Services/Sorcha.Validator.Service/Services/RightsEnforcementService.cs`
- [ ] T067 [US6] Register `IRightsEnforcementService` in Validator Service DI and configure `EnableGovernanceValidation` default
- [ ] T068 [US6] Run validator tests: `dotnet test tests/Sorcha.Validator.Service.Tests`

**Checkpoint**: Non-admin governance TXs rejected. Quorum enforced. Owner bypass works. No Tenant Service calls for governance rights.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: API endpoints, integration tests, documentation updates.

### Governance API Endpoints

- [ ] T069 [P] Create `GovernanceEndpoints.cs` with `GET /api/registers/{registerId}/roster` endpoint in `src/Services/Sorcha.Register.Service/Endpoints/GovernanceEndpoints.cs`
- [ ] T070 [P] Add `GET /api/registers/{registerId}/governance/history` endpoint (paginated) in `src/Services/Sorcha.Register.Service/Endpoints/GovernanceEndpoints.cs`
- [ ] T071 Map governance endpoints in Register Service `Program.cs` with Scalar docs, authorization
- [ ] T072 [P] Write tests for roster endpoint (correct data, 404 for unknown register) in `tests/Sorcha.Register.Service.Tests/Unit/GovernanceEndpointTests.cs`
- [ ] T073 [P] Write tests for history endpoint (pagination, empty history, populated history) in `tests/Sorcha.Register.Service.Tests/Unit/GovernanceEndpointTests.cs`

### YARP Gateway Routes

- [ ] T074 Add YARP routes for `/api/registers/*/roster` and `/api/registers/*/governance/*` to register-cluster in `src/Services/Sorcha.ApiGateway/`

### Integration Tests

- [ ] T075 Write integration test: full governance cycle (create register → add admin → remove admin → transfer ownership → verify roster) in `tests/Sorcha.Register.Core.Tests/Integration/GovernanceIntegrationTests.cs`
- [ ] T076 Write integration test: roster determinism (replay same Control chain → identical roster on two reconstructions) in `tests/Sorcha.Register.Core.Tests/Integration/RosterDeterminismTests.cs`

### Documentation

- [ ] T077 [P] Update `.specify/MASTER-TASKS.md` with governance task status
- [ ] T078 [P] Update `docs/development-status.md` with governance feature completion
- [ ] T079 [P] Add governance patterns to CLAUDE.md memory (DID format, Control type, quorum rules)
- [ ] T080 Run full solution build and test suite: `dotnet build && dotnet test`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on T001 (enum rename) — BLOCKS all user stories
- **Phase 3 (US1)**: Depends on Phase 2 completion
- **Phase 4 (US2)**: Depends on Phase 3 (US1) — needs roster service
- **Phase 5 (US5)**: Depends on Phase 2 — can run in parallel with US2
- **Phase 6 (US3)**: Depends on Phase 4 (US2) — extends quorum logic
- **Phase 7 (US4)**: Depends on Phase 4 (US2) — extends governance operations
- **Phase 8 (US6)**: Depends on Phase 3 (US1) + Phase 5 (US5) — needs roster + DID resolution
- **Phase 9 (Polish)**: Depends on all user stories

### User Story Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundational)
                        │
                        ├─► Phase 3 (US1: Genesis) ──► Phase 4 (US2: Add Admin)
                        │                                      │
                        │                              ┌───────┼───────┐
                        │                              ▼       ▼       │
                        │                     Phase 6 (US3)  Phase 7 (US4)
                        │                              │       │
                        ├─► Phase 5 (US5: DID) ────────┤       │
                        │                              ▼       ▼
                        │                     Phase 8 (US6: Enforcement)
                        │                              │
                        └──────────────────────────────┴─► Phase 9 (Polish)
```

### Parallel Opportunities

**Within Phase 1**: T002, T003, T004, T005, T006, T007 can all run in parallel after T001
**Within Phase 2**: T010, T011, T012, T013, T014 can all run in parallel
**US1 tests**: T018, T019, T020 in parallel
**US1 impl**: T021, T022, T023 in parallel (different files)
**US5 can parallel US2**: DID resolution has no dependency on add admin workflow
**Phase 9 docs**: T077, T078, T079 in parallel

---

## Parallel Example: Phase 1

```bash
# After T001 completes, launch all model + test tasks in parallel:
T002: "Create SorchaDidIdentifier in src/Common/Sorcha.Register.Models/SorchaDidIdentifier.cs"
T003: "Create GovernanceModels in src/Common/Sorcha.Register.Models/GovernanceModels.cs"
T004: "Update RegisterControlRecord in src/Common/Sorcha.Register.Models/RegisterControlRecord.cs"
T005: "Write DID tests in tests/Sorcha.Register.Models.Tests/SorchaDidIdentifierTests.cs"
T006: "Write quorum tests in tests/Sorcha.Register.Models.Tests/RegisterControlRecordQuorumTests.cs"
T007: "Write governance model tests in tests/Sorcha.Register.Models.Tests/GovernanceModelsTests.cs"
```

## Parallel Example: User Story 1

```bash
# After Phase 2, launch US1 tests and impl in parallel:
T018: "Write roster reconstruction tests"
T019: "Write genesis Control TX tests"
T020: "Write blueprint seeding tests"
T021: "Create GovernanceRosterService"
T022: "Add GetControlTransactionsAsync to service client"
T023: "Create governance blueprint JSON"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T008)
2. Complete Phase 2: Foundational enum migration (T009-T017)
3. Complete Phase 3: US1 — Genesis Control TX + roster + blueprint (T018-T029)
4. **STOP and VALIDATE**: Create register → verify Control TX → verify roster → verify blueprint bound
5. Deploy/demo if ready — registers now have decentralized ownership

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. + US1 → Registers have owners (MVP!)
3. + US2 → Multi-admin governance with quorum
4. + US5 → Cross-instance DID resolution
5. + US3 + US4 → Full admin lifecycle (remove + transfer)
6. + US6 → Decentralized rights enforcement
7. + Polish → API endpoints, integration tests, docs

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- Each user story is independently testable at its checkpoint
- Constitution requires >85% test coverage for new code
- Commit after each task or logical group
- Integer value 0 preserved for Control (was Genesis) — no data migration for existing registers
- System=3 references need migration to Control=0 — search for any persisted System type TXs
