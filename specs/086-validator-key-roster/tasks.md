# Tasks: Validator Key Roster

**Input**: Design documents from `/specs/086-validator-key-roster/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/governance-api.yaml

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks grouped by user story. US1 and US2 are both P1 (US2 is the data foundation, US1 is the verification that uses it). US3 is P2, US4 is P3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

---

## Phase 1: Setup

**Purpose**: No new projects needed. Verify existing project structure supports the changes.

- [X] T001 Delete all pre-existing registers from local Docker databases (MongoDB register collections, PostgreSQL peer subscriptions, Redis advertisement cache) per FR-005
- [X] T002 Verify system wallet derivation supports `"sorcha:docket-signing"` derivation context by checking `SignTransactionAsync` in `src/Common/Sorcha.ServiceClients.Http/Wallet/IWalletServiceClient.cs`

---

## Phase 2: Foundational (Data Model)

**Purpose**: Create the validator roster models that ALL user stories depend on.

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create `ValidatorKeyStatus` enum (Active, Rotated, Revoked) in `src/Common/Sorcha.Register.Models/ValidatorKeyStatus.cs`
- [X] T004 [P] Create `ValidatorRosterEntry` model with fields (ValidatorId, PublicKey, Algorithm, DerivationContext, Status, AuthorizedAt, RevokedAt) and DataAnnotations validation in `src/Common/Sorcha.Register.Models/ValidatorRosterEntry.cs`
- [X] T005 [P] Create `ValidatorRoster` model with fields (Validators list, RequiredSignatures, Version) and validation rules (min 1, max 10 entries, at least one Active) in `src/Common/Sorcha.Register.Models/ValidatorRoster.cs`
- [X] T006 Add `Validators` property (type `ValidatorRoster`) to `RegisterControlRecord` in `src/Common/Sorcha.Register.Models/RegisterControlRecord.cs`
- [X] T007 [P] Write unit tests for `ValidatorRosterEntry` validation (required fields, Base64 key, status transitions) in `tests/Sorcha.Register.Models.Tests/ValidatorRosterEntryTests.cs`
- [X] T008 [P] Write unit tests for `ValidatorRoster` validation (min/max entries, requiredSignatures bounds, no duplicate ValidatorId, at least one Active) in `tests/Sorcha.Register.Models.Tests/ValidatorRosterTests.cs`
- [X] T009 Write unit test for `RegisterControlRecord` serialization roundtrip with Validators field in `tests/Sorcha.Register.Models.Tests/RegisterControlRecordTests.cs`

**Checkpoint**: Data model complete. ValidatorRoster serializes correctly in RegisterControlRecord JSON.

---

## Phase 3: User Story 2 - Register Genesis Declares Validator Roster (Priority: P1) 🎯 MVP

**Goal**: When a register is created, the genesis control record includes a validators list with the local validator's purpose-derived signing key.

**Independent Test**: Create a new register, inspect genesis control transaction, verify validators list exists with one entry containing derived public key.

### Tests for User Story 2

- [ ] T010 [P] [US2] Write unit test: `RegisterCreationOrchestrator.FinalizeAsync` populates validator roster with one entry when no external roster provided, in `tests/Sorcha.Register.Service.Tests/RegisterCreationValidatorRosterTests.cs`
- [ ] T011 [P] [US2] Write unit test: `RegisterCreationOrchestrator.FinalizeAsync` uses externally-provided validator roster when supplied (FR-014), in `tests/Sorcha.Register.Service.Tests/RegisterCreationValidatorRosterTests.cs`
- [ ] T012 [P] [US2] Write unit test: register creation fails if validator roster is empty (FR-010), in `tests/Sorcha.Register.Service.Tests/RegisterCreationValidatorRosterTests.cs`

### Implementation for User Story 2

- [X] T013 [US2] Modify `RegisterCreationOrchestrator.FinalizeAsync` to derive validator signing key from system wallet using `SignTransactionAsync` with derivation path `"sorcha:docket-signing"` and populate `RegisterControlRecord.Validators` in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [X] T014 [US2] Add optional `validators` parameter to register creation finalize endpoint (FR-014) to accept external validator roster in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [X] T015 [US2] Add validation: reject register creation if Validators list is empty or null (FR-010) in `src/Services/Sorcha.Register.Service/Services/RegisterCreationOrchestrator.cs`
- [X] T016 [US2] Modify `DocketBuilder.BuildDocketAsync` to sign with `SignTransactionAsync(systemWalletAddress, docketHash, "sorcha:docket-signing")` instead of `SignDataAsync` (FR-012) in `src/Services/Sorcha.Validator.Service/Services/DocketBuilder.cs`
- [ ] T017 [P] [US2] Write unit test: `DocketBuilder` signs using derived key path, not root wallet key, in `tests/Sorcha.Validator.Service.Tests/DocketBuilderDerivedKeyTests.cs`

**Checkpoint**: New registers have validator roster in genesis. Dockets signed with purpose-derived key.

---

## Phase 4: User Story 1 - Remote Peer Verifies Synced Dockets (Priority: P1)

**Goal**: Remote peers extract validator keys from genesis control record and verify synced dockets against the declared roster.

**Independent Test**: Create register on Node A, subscribe on Node B, verify dockets pass signature verification and register height matches.

### Tests for User Story 1

- [X] T018 [P] [US1] Write unit test: `ValidatorKeyCache.ExtractFromControlRecord` populates authorized key set from genesis validators list, in `tests/Sorcha.Peer.Service.Tests/Replication/ValidatorKeyCacheTests.cs`
- [X] T019 [P] [US1] Write unit test: `ValidatorKeyCache.IsAuthorizedSigner` returns true for Active key, false for Revoked key, true for Rotated key, in `tests/Sorcha.Peer.Service.Tests/Replication/ValidatorKeyCacheTests.cs`
- [ ] T020 [P] [US1] Write unit test: `DocketFinalizationService` accepts dockets signed by roster-authorized key, rejects unauthorized signers, in `tests/Sorcha.Peer.Service.Tests/DocketFinalizationRosterTests.cs`

### Implementation for User Story 1

- [X] T021 [US1] Refactor `ValidatorKeyCache` from single-key (`ConcurrentDictionary<string, ValidatorKeyEntry>`) to multi-key roster (`ConcurrentDictionary<string, ValidatorRosterCache>`) with `IsAuthorizedSigner(registerId, publicKey)` method in `src/Services/Sorcha.Peer.Service/Replication/ValidatorKeyCache.cs`
- [X] T022 [US1] Add `ExtractFromControlRecord(registerId, RegisterControlRecord)` method to `ValidatorKeyCache` that reads the Validators list and populates the authorized key set in `src/Services/Sorcha.Peer.Service/Replication/ValidatorKeyCache.cs`
- [X] T023 [US1] Modify `DocketFinalizationService.EnsureValidatorKeyCachedAsync` to deserialize genesis control transaction payload and extract `RegisterControlRecord.Validators` instead of extracting from `ProposerSignature` in `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs`
- [X] T024 [US1] Modify `DocketFinalizationService.VerifyProposerSignatureAsync` to check `ValidatorKeyCache.IsAuthorizedSigner(registerId, signerPublicKey)` against the roster instead of comparing against a single cached key in `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs`
- [X] T025 [US1] Ensure `DocketModel` carries the genesis control payload through the relay path by verifying `RegisterServiceClient.ReadDocketAsync` returns transaction data for genesis docket (docket 0) in `src/Common/Sorcha.ServiceClients.Http/Register/RegisterServiceClient.cs`

**Checkpoint**: Cross-node sync works end-to-end. Dockets verified against declared validator roster. Register height matches on both nodes.

---

## Phase 5: User Story 3 - Validator Pool Expansion via Governance (Priority: P2)

**Goal**: Register owners can add/remove/rotate validators via governance proposals. Remote peers update their cached key set from control transactions.

**Independent Test**: Add a validator via governance proposal, verify dockets from new validator are accepted by remote peers.

### Tests for User Story 3

- [ ] T026 [P] [US3] Write unit test: governance `add-validator` operation creates control transaction with two validators, in `tests/Sorcha.Register.Service.Tests/GovernanceValidatorRosterTests.cs`
- [ ] T027 [P] [US3] Write unit test: governance `remove-validator` operation sets validator status to Revoked, rejects if it would leave zero Active validators, in `tests/Sorcha.Register.Service.Tests/GovernanceValidatorRosterTests.cs`
- [ ] T028 [P] [US3] Write unit test: governance `rotate-validator-key` operation marks old key Rotated and adds new Active key, in `tests/Sorcha.Register.Service.Tests/GovernanceValidatorRosterTests.cs`

### Implementation for User Story 3

- [X] T029 [US3] Add `add-validator` governance operation handler in the existing governance proposal endpoint in `src/Services/Sorcha.Register.Service/Program.cs` (governance section)
- [X] T030 [US3] Add `remove-validator` governance operation handler with validation (at least one Active must remain) in `src/Services/Sorcha.Register.Service/Program.cs` (governance section)
- [X] T031 [US3] Add `rotate-validator-key` governance operation handler (mark old Rotated, add new Active, increment roster version) in `src/Services/Sorcha.Register.Service/Program.cs` (governance section)
- [X] T032 [US3] Modify `ValidatorKeyCache` to update authorized key set when a new control transaction is synced (replay all control transactions to rebuild roster) in `src/Services/Sorcha.Peer.Service/Replication/ValidatorKeyCache.cs`
- [ ] T033 [P] [US3] Write unit test: `ValidatorKeyCache` rebuilds authorized keys from control transaction sequence (add then remove), in `tests/Sorcha.Peer.Service.Tests/ValidatorKeyCacheRosterTests.cs`

**Checkpoint**: Governance operations for validator roster work. Remote peers accept dockets from newly-added validators.

---

## Phase 6: User Story 4 - Threshold Signing Schema (Priority: P3)

**Goal**: Validator roster schema includes threshold parameters (requiredSignatures) defaulting to single-signer mode.

**Independent Test**: Inspect roster schema — confirm requiredSignatures field exists and defaults to 1.

### Implementation for User Story 4

- [X] T034 [US4] Add validation in `ValidatorRoster` that `RequiredSignatures` must be >= 1 and <= count of Active validators in `src/Common/Sorcha.Register.Models/ValidatorRoster.cs`
- [X] T035 [US4] Add validation in governance `add-validator` / `remove-validator` that roster changes maintain threshold invariant (requiredSignatures <= Active count) in `src/Services/Sorcha.Register.Service/Program.cs`
- [X] T036 [P] [US4] Write unit test: `ValidatorRoster` rejects `RequiredSignatures > Active count`, accepts `RequiredSignatures = 1` with 1 Active, in `tests/Sorcha.Register.Models.Tests/ValidatorRosterTests.cs`
- [ ] T037 [P] [US4] Write unit test: governance rejects removing a validator if it would violate threshold invariant, in `tests/Sorcha.Register.Service.Tests/GovernanceValidatorRosterTests.cs`

**Checkpoint**: Threshold schema validated. Single-signer mode enforced. Schema ready for future n-of-m enforcement.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T038 Add XML documentation to all public types (ValidatorRoster, ValidatorRosterEntry, ValidatorKeyStatus) in `src/Common/Sorcha.Register.Models/`
- [ ] T039 Add structured logging for validator roster extraction and docket verification decisions in `src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs`
- [ ] T040 Run `quickstart.md` Scenario 1 end-to-end: create register on local, subscribe on n1, verify sync completes with register height matching
- [ ] T041 Update CLAUDE.md Validator Service section if signing flow changed materially
- [ ] T042 [P] Update `docs/reference/development-status.md` with feature 086 completion

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US2 - Genesis)**: Depends on Phase 2 — must complete before US1
- **Phase 4 (US1 - Verification)**: Depends on Phase 3 (needs genesis roster to verify against)
- **Phase 5 (US3 - Governance)**: Depends on Phase 3 (needs roster model) and Phase 4 (needs cache)
- **Phase 6 (US4 - Threshold)**: Depends on Phase 2 (model only), can start after Phase 2
- **Phase 7 (Polish)**: Depends on all story phases complete

### User Story Dependencies

- **US2 (Genesis Roster, P1)**: FIRST — creates the data that all others consume
- **US1 (Peer Verification, P1)**: SECOND — uses the roster data US2 creates
- **US3 (Governance, P2)**: After US1+US2 — extends the roster update path
- **US4 (Threshold, P3)**: After Phase 2 — schema-only, can parallel with US3

### Parallel Opportunities

- T003, T004, T005 (models) can run in parallel
- T007, T008 (model tests) can run in parallel
- T010, T011, T012 (US2 tests) can run in parallel
- T018, T019, T020 (US1 tests) can run in parallel
- T026, T027, T028 (US3 tests) can run in parallel
- US4 (Phase 6) can run in parallel with US3 (Phase 5)

---

## Implementation Strategy

### MVP First (US2 + US1)

1. Complete Phase 1: Setup (delete old registers)
2. Complete Phase 2: Foundational (data model)
3. Complete Phase 3: US2 (genesis roster population)
4. Complete Phase 4: US1 (peer verification)
5. **STOP and VALIDATE**: Create register, sync to n1, verify dockets finalize
6. Deploy and test cross-node

### Incremental Delivery

1. Setup + Foundational → Models ready
2. US2 (Genesis) → Registers carry validator keys → Deploy
3. US1 (Verification) → Cross-node sync works → Deploy + E2E test
4. US3 (Governance) → Validator pool management → Deploy
5. US4 (Threshold) → Schema future-proofed → Deploy

---

## Notes

- US2 is listed before US1 because the genesis roster must exist before peers can verify against it
- The `"sorcha:docket-signing"` derivation context is new — distinct from existing `"sorcha:register-control"`
- Pre-existing registers are deleted (FR-005), not migrated — clean break for preproduction
- Threshold enforcement (n-of-m actual validation) is explicitly OUT OF SCOPE — schema only
