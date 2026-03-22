# Tasks: Validator Consensus Security

**Input**: Design documents from `/specs/066-validator-consensus-security/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage on new code.

**Organization**: Tasks grouped by user story (US1→US2→US3). US2 depends on US1. US3 is independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

---

## Phase 1: Setup

**Purpose**: Shared model changes and MongoDB infrastructure that all stories need

- [x] T001 Rename `Removed` to `Revoked` in `ValidatorStatus` enum and update all references in `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs`
- [x] T002 Extend `ValidatorInfo` record with new fields (ApprovedAt, ApprovedBy, SuspendedAt, SuspendedBy, RevokedAt, RevokedBy, LastStateChangeAt, Algorithm) in `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs`
- [x] T003 [P] Create `ValidatorAuditEntry` model in `src/Services/Sorcha.Validator.Service/Models/ValidatorAuditEntry.cs`
- [x] T004 [P] Add MongoDB connection configuration to `src/Services/Sorcha.Validator.Service/Configuration/ValidatorRegistryConfiguration.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: MongoDB persistence layer — required before any story can durably store validator state

- [x] T005 Implement MongoDB write-through in `ValidatorRegistry.RegisterAsync` — write to MongoDB first, then update Redis cache in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [x] T006 Implement MongoDB hydration on startup — load all validators from MongoDB into Redis cache in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [x] T007 Create MongoDB indexes for `validators` collection (registerId+status, registerId+validatorId unique) in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [x] T008 [P] Create MongoDB indexes for `validator_audit` collection (registerId+validatorId+timestamp, timestamp) in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T009 [P] Write unit tests for MongoDB persistence — register, hydrate, and verify round-trip in `tests/Sorcha.Validator.Service.Tests/Services/ValidatorRegistryPersistenceTests.cs`

**Checkpoint**: MongoDB durable storage is operational. Validators survive restarts.

---

## Phase 3: User Story 1 — Validator Approval Governance + Admin UI (Priority: P1)

**Goal**: System administrators can manage validator lifecycle (approve/suspend/revoke) via API and Admin UI, with durable storage and audit trail.

**Independent Test**: Register a validator → confirm Pending → approve via UI → confirm Active → suspend → confirm excluded from consensus → revoke → confirm terminal.

### Tests for User Story 1

- [ ] T010 [P] [US1] Write tests for suspend/reactivate/revoke state transitions in `tests/Sorcha.Validator.Service.Tests/Services/ValidatorRegistryStateTransitionTests.cs`
- [ ] T011 [P] [US1] Write tests for last-active-validator guard in `tests/Sorcha.Validator.Service.Tests/Services/ValidatorRegistryGuardTests.cs`
- [ ] T012 [P] [US1] Write tests for audit logging in `tests/Sorcha.Validator.Service.Tests/Services/ValidatorAuditTests.cs`
- [ ] T013 [P] [US1] Write endpoint tests for suspend/reactivate/revoke/audit in `tests/Sorcha.Validator.Service.Tests/Endpoints/ValidatorManagementEndpointTests.cs`

### Implementation for User Story 1

- [ ] T014 [US1] Add `SuspendValidatorAsync`, `ReactivateValidatorAsync`, `RevokeValidatorAsync` to `IValidatorRegistry` interface in `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs`
- [ ] T015 [US1] Add `GetAuditTrailAsync` to `IValidatorRegistry` interface in `src/Services/Sorcha.Validator.Service/Services/Interfaces/IValidatorRegistry.cs`
- [ ] T016 [US1] Implement `SuspendValidatorAsync` with last-active-validator guard and audit logging in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T017 [US1] Implement `ReactivateValidatorAsync` (Suspended→Active only) with audit logging in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T018 [US1] Implement `RevokeValidatorAsync` (terminal, last-active guard) with audit logging in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T019 [US1] Implement `GetAuditTrailAsync` with pagination in `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- [ ] T020 [US1] Add suspend endpoint `POST /{registerId}/{validatorId}/suspend` in `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- [ ] T021 [P] [US1] Add reactivate endpoint `POST /{registerId}/{validatorId}/reactivate` in `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- [ ] T022 [P] [US1] Add revoke endpoint `POST /{registerId}/{validatorId}/revoke` in `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- [ ] T023 [US1] Add audit trail endpoint `GET /{registerId}/audit` in `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- [ ] T024 [US1] Add YARP route for new validator endpoints in `src/Services/Sorcha.ApiGateway/appsettings.json`
- [ ] T025 [US1] Create `ValidatorAdminService` HTTP client in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ValidatorAdminService.cs`
- [ ] T026 [US1] Create `ValidatorManagement.razor` page — list all validators with status, actions (approve/suspend/revoke), confirmation dialogs in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/ValidatorManagement.razor`
- [ ] T027 [US1] Create `ValidatorDetail.razor` page — full detail + audit history in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/ValidatorDetail.razor`
- [ ] T028 [US1] Add validator management nav item to admin sidebar in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Layout/AdminNavMenu.razor`

**Checkpoint**: Full validator lifecycle manageable via API and Admin UI. Audit trail visible. Data survives restarts.

---

## Phase 4: User Story 2 — Consensus Vote Cryptographic Verification (Priority: P2)

**Goal**: Every consensus vote is cryptographically signed and verified against the voter's registered public key before counting.

**Independent Test**: Propose a docket → collect votes → verify valid signatures are counted → verify forged/missing signatures are rejected → verify non-Active validator votes are rejected.

**Depends on**: US1 (validator registry with public keys and status)

### Tests for User Story 2

- [ ] T029 [P] [US2] Write tests for canonical vote signing contract in `tests/Sorcha.Validator.Service.Tests/Services/VoteSigningContractTests.cs`
- [ ] T030 [P] [US2] Write tests for vote signature verification (valid, invalid, missing, wrong key) in `tests/Sorcha.Validator.Service.Tests/Services/VoteVerificationTests.cs`
- [ ] T031 [P] [US2] Write tests for non-Active validator vote rejection in `tests/Sorcha.Validator.Service.Tests/Services/ConsensusEngineVoteFilterTests.cs`

### Implementation for User Story 2

- [ ] T032 [US2] Add `Signature`, `SignerPublicKey`, `Algorithm` fields to `ConsensusVote` in `src/Services/Sorcha.Validator.Service/Models/ConsensusVote.cs`
- [ ] T033 [US2] Implement canonical vote content builder `BuildVoteSigningContent(docketId, docketHash, approved, validatorId)` as static helper in `src/Services/Sorcha.Validator.Service/Services/VoteSigningHelper.cs`
- [ ] T034 [US2] Implement outgoing vote signing — sign canonical content with local validator key via `IWalletServiceClient.SignDataAsync` in `src/Services/Sorcha.Validator.Service/Services/SignatureCollector.cs`
- [ ] T035 [US2] Implement incoming vote verification — verify signature against registered public key via `ICryptoModule.VerifySignature` in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T036 [US2] Add validator status check — reject votes from Pending/Suspended/Revoked validators before signature verification in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T037 [US2] Add security event logging for rejected votes (invalid signature, wrong key, inactive validator) in `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- [ ] T038 [US2] Update gRPC vote response proto to include signature fields in `src/Services/Sorcha.Validator.Service/Protos/` (if applicable)

**Checkpoint**: All consensus votes are cryptographically verified. Forged and unauthorized votes are rejected and logged.

---

## Phase 5: User Story 3 — Transaction Replay Protection (Priority: P3)

**Goal**: Per-wallet monotonic sequence numbers prevent transaction replay attacks at the chain level.

**Independent Test**: Submit tx with seq=1 → succeeds → resubmit with seq=1 → rejected → submit with seq=2 → succeeds → submit with seq=4 (gap) → rejected.

**Depends on**: Phase 2 only (independent of US1/US2)

### Tests for User Story 3

- [ ] T039 [P] [US3] Write tests for WalletSequence MongoDB repository (get, increment, concurrent access) in `tests/Sorcha.Validator.Service.Tests/Services/WalletSequenceRepositoryTests.cs`
- [ ] T040 [P] [US3] Write tests for sequence validation in ValidationEngine (valid, replay, gap, genesis bypass) in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineSequenceTests.cs`
- [ ] T041 [P] [US3] Write tests for sequence query endpoint in `tests/Sorcha.Validator.Service.Tests/Endpoints/SequenceEndpointTests.cs`

### Implementation for User Story 3

- [x] T042 [P] [US3] Add `SequenceNumber` (ulong) field to `Transaction` model in `src/Services/Sorcha.Validator.Service/Models/Transaction.cs`
- [x] T043 [P] [US3] Create `WalletSequence` model in `src/Services/Sorcha.Validator.Service/Models/WalletSequence.cs`
- [ ] T044 [US3] Create `IWalletSequenceRepository` interface and MongoDB implementation with atomic `findOneAndUpdate` in `src/Services/Sorcha.Validator.Service/Services/WalletSequenceRepository.cs`
- [ ] T045 [US3] Create MongoDB indexes for `wallet_sequences` collection in `src/Services/Sorcha.Validator.Service/Services/WalletSequenceRepository.cs`
- [ ] T046 [US3] Add sequence validation stage to `ValidationEngine.ValidateTransactionAsync` — check seq == lastKnown + 1, bypass for genesis/control in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T047 [US3] Increment sequence number only AFTER successful validation (not on rejection) in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T048 [US3] Add fail-closed behavior — reject transactions when sequence store unavailable in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- [ ] T049 [US3] Add sequence query endpoint `GET /{registerId}/sequence/{walletAddress}` in `src/Services/Sorcha.Validator.Service/Endpoints/ValidatorRegistrationEndpoints.cs`
- [ ] T050 [US3] Update `TransactionBuilderService` to fetch and include sequence number when building transactions in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionBuilderService.cs`
- [ ] T051 [US3] Add `GetSequenceNumberAsync` to `IValidatorServiceClient` in `src/Common/Sorcha.ServiceClients/Validator/IValidatorServiceClient.cs`

**Checkpoint**: Replay attacks are blocked. Clients can query their next sequence number. Genesis/control transactions bypass validation.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, integration, and hardening across all stories

- [ ] T052 [P] Update Validator Service README with new endpoints and configuration in `src/Services/Sorcha.Validator.Service/README.md`
- [ ] T053 [P] Update API documentation with new validator management and sequence endpoints in `docs/reference/API-DOCUMENTATION.md`
- [ ] T054 [P] Update development status to reflect completed security audit items in `docs/reference/development-status.md`
- [ ] T055 Update security audit document — mark findings 4.1, 4.2, 4.5 as remediated in `docs/security/SECURITY-AUDIT-2026-03-19.md`
- [ ] T056 Run full test suite and verify >85% coverage on new code
- [ ] T057 Update `.specify/MASTER-TASKS.md` with completed items

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (model changes)
- **US1 (Phase 3)**: Depends on Phase 2 (MongoDB persistence)
- **US2 (Phase 4)**: Depends on US1 (validator registry with keys + status filtering)
- **US3 (Phase 5)**: Depends on Phase 2 only — **can run in parallel with US1/US2**
- **Polish (Phase 6)**: Depends on all stories complete

### User Story Dependencies

- **US1 (P1)**: Phase 2 → US1 (no other story dependencies)
- **US2 (P2)**: Phase 2 → US1 → US2 (needs validator registry with public keys)
- **US3 (P3)**: Phase 2 → US3 (independent — parallelizable with US1/US2)

### Within Each User Story

- Tests written first (TDD)
- Models before services
- Services before endpoints
- Endpoints before UI

### Parallel Opportunities

**Phase 1**: T003 and T004 can run in parallel
**Phase 2**: T008 and T009 can run in parallel after T007
**US1**: T010-T013 (tests) all parallel; T021-T022 (endpoints) parallel; T025-T027 (UI) after endpoints
**US2**: T029-T031 (tests) all parallel; T032-T033 (models) parallel
**US3**: T039-T041 (tests) all parallel; T042-T043 (models) parallel; **entire phase parallel with US1/US2**

---

## Parallel Example: US1 + US3 Concurrent

```
Developer A (US1):                    Developer B (US3):
  T010-T013 tests in parallel           T039-T041 tests in parallel
  T014-T015 interface changes           T042-T043 models in parallel
  T016-T019 service implementation      T044-T045 repository + indexes
  T020-T024 endpoints                   T046-T049 validation + endpoint
  T025-T028 Admin UI                    T050-T051 Blueprint Service integration
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Setup (model changes)
2. Complete Phase 2: Foundational (MongoDB persistence)
3. Complete Phase 3: US1 (validator approval + Admin UI)
4. **STOP and VALIDATE**: Register → approve → suspend → revoke cycle works end-to-end
5. Deploy/demo — administrators can manage validators

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. US1 → Validator governance + Admin UI (MVP)
3. US2 → Consensus votes verified (security hardening)
4. US3 → Replay protection (defense-in-depth)
5. Each story adds security without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story
- Existing ValidatorRegistry has ~80% infrastructure — tasks extend, not replace
- Two `ValidatorInfo` classes exist (service-local + peer client) — only modify service-local
- Commit after each logical group (model changes, service changes, endpoint changes)
- Stop at any checkpoint to validate story independently
