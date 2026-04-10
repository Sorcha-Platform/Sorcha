# Tasks: System Register Genesis Trust Anchor

**Input**: Design documents from `/specs/099-genesis-trust-anchor/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Tests are included per constitution requirement (>80% coverage for core, >85% for new code).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Genesis file models, configuration, and shared components used by all user stories.

- [x] T001 [P] Create SystemRegisterGenesis model in src/Common/Sorcha.Register.Models/Genesis/SystemRegisterGenesis.cs
- [x] T002 [P] Create GenesisTransactionData model in src/Common/Sorcha.Register.Models/Genesis/GenesisTransactionData.cs
- [x] T003 [P] Create GenesisSignature model in src/Common/Sorcha.Register.Models/Genesis/GenesisSignature.cs
- [x] T004 [P] Create GenesisValidatorKeyFile model in src/Common/Sorcha.Register.Models/Genesis/GenesisValidatorKeyFile.cs
- [x] T005 Create SystemRegisterOptions configuration model in src/Common/Sorcha.ServiceDefaults/SystemRegisterOptions.cs
- [x] T006 Add EmbeddedResource entry for Resources/system-register-genesis.json in src/Common/Sorcha.Register.Models/Sorcha.Register.Models.csproj
- [x] T007 Create placeholder system-register-genesis.json in src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json (empty JSON object, replaced by ceremony)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Genesis file loading and signature verification — used by ceremony CLI, bootstrapper, and peer sync.

**CRITICAL**: No user story work can begin until this phase is complete.

- [x] T008 Create GenesisFileLoader (config path → embedded resource fallback → error) in src/Common/Sorcha.Register.Models/Genesis/GenesisFileLoader.cs
- [x] T009 Create GenesisSignatureVerifier (verify genesis transaction signature using ICryptoModule) in src/Common/Sorcha.Register.Models/Genesis/GenesisSignatureVerifier.cs
- [x] T010 [P] Add SystemRegisterOptions binding in src/Common/Sorcha.ServiceDefaults/ (register config section "SystemRegister")
- [x] T011 [P] Write GenesisFileLoaderTests (config path, embedded fallback, missing file, invalid JSON) in tests/Sorcha.Register.Models.Tests/Genesis/GenesisFileLoaderTests.cs
- [x] T012 [P] Write GenesisSignatureVerifierTests (valid signature, tampered payload, wrong algorithm) in tests/Sorcha.Register.Models.Tests/Genesis/GenesisSignatureVerifierTests.cs

**Checkpoint**: Genesis loading and verification infrastructure ready. All user stories can now proceed.

---

## Phase 3: User Story 1 - Genesis Ceremony (Priority: P1) MVP

**Goal**: Operator runs CLI command to produce a pre-signed genesis block and validator key file offline.

**Independent Test**: Run `sorcha system-register create --network-id test-net` and verify it produces a valid genesis file with correct signatures and a separate validator key file.

### Tests for User Story 1

- [x] T013 [P] [US1] Write ceremony unit tests (key generation, genesis signing, deterministic register ID, file output) in tests/Sorcha.Cli.Tests/Commands/SystemRegisterCreateCommandTests.cs
- [x] T014 [P] [US1] Write verify command unit tests (valid file passes, tampered file fails, exit codes) in tests/Sorcha.Cli.Tests/Commands/SystemRegisterVerifyCommandTests.cs

### Implementation for User Story 1

- [x] T015 [US1] Create SystemRegisterCommand group (parent command) in src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs
- [x] T016 [US1] Implement SystemRegisterCreateCommand — generate ED25519 keypair via CryptoModule, build RegisterControlRecord with deterministic SystemRegisterId, populate ValidatorRoster, sign control record at sorcha:register-control, output genesis file and validator key file in src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs
- [x] T017 [US1] Implement SystemRegisterVerifyCommand — load genesis file, verify all signatures, display validator roster and fingerprint, exit code 0/1 in src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs
- [x] T018 [US1] Register SystemRegisterCommand group in src/Apps/Sorcha.Cli/Program.cs (add to root command)
- [x] T019 [US1] Add GenesisPublicKeyFingerprint constant field to src/Common/Sorcha.Register.Models/Constants/SystemRegisterConstants.cs

**Checkpoint**: Genesis ceremony CLI works end-to-end. Operator can create and verify genesis files offline.

---

## Phase 4: User Story 2 - First Instance Bootstrap (Priority: P1)

**Goal**: First validator instance loads pre-signed genesis, seals genesis docket, and seeds blueprints. No runtime genesis creation.

**Independent Test**: Start instance with valid genesis file and imported validator key, verify system register created and blueprints seeded.

### Tests for User Story 2

- [x] T020 [P] [US2] Write SystemRegisterBootstrapperTests — 4 flow paths (local exists, peer sync, genesis ingest + seal, stop) in tests/Sorcha.Register.Service.Tests/Services/SystemRegisterBootstrapperTests.cs

### Implementation for User Story 2

- [x] T021 [US2] Create GenesisIngestionService — load genesis file via GenesisFileLoader, verify signature, build TransactionSubmission from pre-signed data, submit to Validator Service in src/Services/Sorcha.Register.Service/Services/GenesisIngestionService.cs
- [x] T022 [US2] Rewrite SystemRegisterBootstrapper — replace CreateSystemRegisterAsync with 4-step flow (check local → peer sync → ingest genesis → stop), inject GenesisIngestionService, keep WaitForGenesisDocketAsync and SeedBlueprintsIfMissingAsync in src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs
- [x] T023 [US2] Register GenesisIngestionService and SystemRegisterOptions in Register Service DI in src/Services/Sorcha.Register.Service/Program.cs
- [x] T024 [US2] Add SystemRegister config section to src/Services/Sorcha.Register.Service/appsettings.json and docker-compose environment variables

**Checkpoint**: First instance bootstrap works with pre-signed genesis. No more self-created genesis at runtime.

---

## Phase 5: User Story 3 - Joining Instance Peer Sync (Priority: P1)

**Goal**: New instance syncs system register from peers, verifying genesis signature against trust anchor before accepting.

**Independent Test**: Start second instance with matching genesis file and running peer, verify it syncs and rejects non-matching genesis.

### Tests for User Story 3

- [ ] T025 [P] [US3] Write SystemRegisterSyncVerifierTests — matching fingerprint accepted, mismatched rejected, non-system-register bypassed in tests/Sorcha.Peer.Service.Tests/Replication/SystemRegisterSyncVerifierTests.cs

### Implementation for User Story 3

- [ ] T026 [US3] Create ISystemRegisterSyncVerifier interface and SystemRegisterSyncVerifier implementation — verify genesis transaction signature against trusted public key from GenesisFileLoader, only applies to SystemRegisterConstants.SystemRegisterId in src/Services/Sorcha.Peer.Service/Replication/SystemRegisterSyncVerifier.cs
- [ ] T027 [US3] Inject ISystemRegisterSyncVerifier into DocketFinalizationService — add genesis signature check before step 1 for system register genesis dockets (Version 0) in src/Services/Sorcha.Peer.Service/Replication/DocketFinalizationService.cs
- [ ] T028 [US3] Register SystemRegisterSyncVerifier and SystemRegisterOptions in Peer Service DI in src/Services/Sorcha.Peer.Service/Program.cs
- [ ] T029 [US3] Add SystemRegister config section to src/Services/Sorcha.Peer.Service/appsettings.json

**Checkpoint**: Peer sync verifies genesis signature. Rogue peers with different genesis are rejected.

---

## Phase 6: User Story 4 - Genesis File Verification (Priority: P2)

**Goal**: Operator can verify a genesis file's authenticity before deploying.

**Independent Test**: Run verify command against known-good and known-bad genesis files.

**Note**: This story is already implemented by T017 (SystemRegisterVerifyCommand) in User Story 1. This phase validates that the verify command meets the P2 acceptance scenarios.

- [ ] T030 [US4] Add detailed verification output (network ID, roster details, per-signature pass/fail reporting) to SystemRegisterVerifyCommand in src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs
- [ ] T031 [US4] Write additional verify tests for edge cases (corrupted file, wrong version, missing fields) in tests/Sorcha.Cli.Tests/Commands/SystemRegisterVerifyCommandTests.cs

**Checkpoint**: Verify command provides complete diagnostic output for operators.

---

## Phase 7: User Story 5 - Validator Key Import (Priority: P2)

**Goal**: Operator imports genesis validator key into Wallet Service so the first validator can seal dockets.

**Independent Test**: Import key file, verify validator can sign dockets with the rostered genesis key.

### Tests for User Story 5

- [ ] T032 [P] [US5] Write import-validator-key endpoint tests (valid key, invalid key, idempotent reimport) in tests/Sorcha.Wallet.Service.Tests/Endpoints/ImportValidatorKeyEndpointTests.cs
- [ ] T033 [P] [US5] Write CLI import command tests (valid file, invalid file, unreachable service) in tests/Sorcha.Cli.Tests/Commands/SystemRegisterImportKeyCommandTests.cs

### Implementation for User Story 5

- [ ] T034 [US5] Add POST /api/v1/wallets/import-validator-key endpoint to src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs — accept raw private/public key pair, create wallet entry, idempotent on matching public key
- [ ] T035 [US5] Add ImportValidatorKey method to WalletManager (key validation, wallet creation from raw keys, address derivation) in src/Services/Sorcha.Wallet.Service/ (WalletManager or new service)
- [ ] T036 [US5] Add IWalletServiceClient.ImportValidatorKeyAsync method to src/Common/Sorcha.ServiceClients.Http/Wallet/IWalletServiceClient.cs
- [ ] T037 [US5] Implement SystemRegisterImportValidatorKeyCommand in src/Apps/Sorcha.Cli/Commands/SystemRegisterCommands.cs — load key file, call Wallet Service import endpoint, display result

**Checkpoint**: Validator key import works end-to-end. First validator can seal genesis dockets.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, integration testing, and final hardening.

- [ ] T038 [P] Update CLAUDE.md with System Register Genesis Trust section (API endpoints, ceremony workflow)
- [ ] T039 [P] Update src/Services/Sorcha.Register.Service/README.md with new bootstrap behaviour
- [ ] T040 [P] Update docs/reference/development-status.md with feature 099 status
- [ ] T041 Run genesis ceremony to produce initial dev genesis file, commit to src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json
- [ ] T042 Write integration test: ceremony → first instance bootstrap → seal genesis → second instance peer sync in tests/Sorcha.Register.Service.IntegrationTests/ or tests/Sorcha.Peer.Service.IntegrationTests/
- [ ] T043 Run quickstart.md validation — execute the documented steps end-to-end
- [ ] T044 Add structured log messages for all bootstrap states (NetworkId on startup, fingerprint mismatch, stop reasons) in src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 Genesis Ceremony (Phase 3)**: Depends on Phase 2
- **US2 First Instance Bootstrap (Phase 4)**: Depends on Phase 2 (and US1 for genesis file to test with)
- **US3 Peer Sync Verification (Phase 5)**: Depends on Phase 2
- **US4 Genesis Verification (Phase 6)**: Depends on US1 (extends verify command)
- **US5 Validator Key Import (Phase 7)**: Depends on Phase 2
- **Polish (Phase 8)**: Depends on all user stories

### User Story Dependencies

- **US1 (P1)**: Independent after Phase 2
- **US2 (P1)**: Needs a genesis file to test with (produced by US1 ceremony or test fixture)
- **US3 (P1)**: Independent after Phase 2 (uses genesis file from test fixture)
- **US4 (P2)**: Extends US1 verify command
- **US5 (P2)**: Independent after Phase 2

### Within Each User Story

- Tests written first (fail before implementation)
- Models/interfaces before services
- Services before endpoints/CLI commands
- Core implementation before integration

### Parallel Opportunities

- T001-T004: All genesis models can be created in parallel
- T011-T012: Foundation tests in parallel
- T013-T014: US1 tests in parallel
- T025, T032-T033: US3 and US5 tests in parallel
- US1, US3, US5 can proceed in parallel after Phase 2 (different services/files)

---

## Parallel Example: Phase 1 Setup

```
# All models can be created simultaneously:
T001: SystemRegisterGenesis.cs
T002: GenesisTransactionData.cs
T003: GenesisSignature.cs
T004: GenesisValidatorKeyFile.cs
```

## Parallel Example: User Stories After Phase 2

```
# These stories touch different services and can run in parallel:
Stream A (CLI):     US1 → T015-T019 (Sorcha.Cli)
Stream B (Register): US2 → T021-T024 (Register.Service)
Stream C (Peer):    US3 → T026-T029 (Peer.Service)
Stream D (Wallet):  US5 → T034-T037 (Wallet.Service)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (models)
2. Complete Phase 2: Foundational (loader + verifier)
3. Complete Phase 3: US1 Genesis Ceremony
4. **STOP and VALIDATE**: Run ceremony, verify genesis file is valid
5. Commit genesis file to embedded resource

### Incremental Delivery

1. Setup + Foundational → Genesis infrastructure ready
2. US1 Genesis Ceremony → Can create/verify genesis files (MVP!)
3. US5 Validator Key Import → Can import keys into Wallet Service
4. US2 First Instance Bootstrap → First instance can boot with pre-signed genesis
5. US3 Peer Sync Verification → Multi-instance networks verified
6. US4 Verify Enhancements → Better operator diagnostics
7. Polish → Documentation, integration tests, dev genesis file

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Verify tests fail before implementing
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
