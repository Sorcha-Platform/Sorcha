# Tasks: Encrypted Payload Integration

**Input**: Design documents from `/specs/045-encrypted-payload-integration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage for new code, plan explicitly states unit tests for all new code.

**Organization**: Tasks grouped by user story (7 stories across P0/P1/P2 priority). Foundational algorithm fixes and infrastructure are in Phase 2 since all user stories depend on them.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1-US7)
- Exact file paths included in all descriptions

---

## Phase 1: Setup (Shared Models & Interfaces)

**Purpose**: Create the new types, interfaces, and DTOs that multiple user stories depend on

- [ ] T001 Create encryption pipeline models (EncryptedPayloadGroup, WrappedKey, DisclosureGroup, RecipientInfo, KeySource enum, EncryptionType extensions) in `src/Common/Sorcha.TransactionHandler/Encryption/Models/EncryptionModels.cs`
- [ ] T002 [P] Create EncryptionOperation model and EncryptionOperationStatus enum in `src/Services/Sorcha.Blueprint.Service/Models/EncryptionOperationModels.cs`
- [ ] T003 [P] Create IEncryptionPipelineService interface (EncryptDisclosedPayloadsAsync, EstimateEncryptedSizeAsync) in `src/Common/Sorcha.TransactionHandler/Encryption/IEncryptionPipelineService.cs`
- [ ] T004 [P] Create IDisclosureGroupBuilder interface (BuildGroups method) in `src/Common/Sorcha.TransactionHandler/Encryption/IDisclosureGroupBuilder.cs`
- [ ] T005 [P] Create IEncryptionOperationStore interface (Create, Update, GetById, GetByWalletAddress) in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/IEncryptionOperationStore.cs`
- [ ] T006 [P] Add BatchPublicKeyRequest, BatchPublicKeyResponse, and ExternalKeyInfo DTOs to `src/Common/Sorcha.ServiceClients/Register/Models/PublishedParticipantModels.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Algorithm fixes, batch endpoint, and size enforcement that MUST be complete before pipeline integration

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T007 [P] Implement P-256 ECIES encrypt/decrypt (ECDH + HKDF-SHA256 + AES-256-GCM) replacing stubs at `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs` lines 627-641
- [ ] T008 [P] Write P-256 ECIES round-trip tests (encrypt + decrypt, key sizes, error cases) in `tests/Sorcha.Cryptography.Tests/Core/CryptoModuleNistP256Tests.cs`
- [ ] T009 [P] Fix ML-KEM-768 decapsulate endpoint to call `DecryptWithKemAsync` instead of `DecryptPayloadAsync` at `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs` line 1321
- [ ] T010 [P] Write ML-KEM-768 decapsulate fix tests (round-trip encapsulate+decapsulate) in `tests/Sorcha.Wallet.Service.Tests/Endpoints/MlKemDecapsulateTests.cs`
- [ ] T011 [P] Implement batch public key resolution endpoint `POST /api/registers/{registerId}/participants/resolve-public-keys` per `contracts/register-batch-public-keys.yaml` at `src/Services/Sorcha.Register.Service/Program.cs` (after line 1560)
- [ ] T012 [P] Add `ResolvePublicKeysBatchAsync` method to `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` and implement in `src/Common/Sorcha.ServiceClients/Register/RegisterServiceClient.cs`
- [ ] T013 [P] Write batch public key resolution tests (found, not-found, revoked, mixed results, >200 validation) in `tests/Sorcha.Register.Service.Tests/Endpoints/BatchPublicKeyResolutionTests.cs`
- [ ] T014 [P] Raise MaxTransactionSizeBytes default from 1MB to 4MB at `src/Services/Sorcha.Validator.Service/Configuration/TransactionReceiverConfiguration.cs` line 32
- [ ] T015 [P] Add size enforcement check in `ReceiveTransactionAsync` before deserialization — reject with clear error code if `transactionData.Length > _config.MaxTransactionSizeBytes` at `src/Services/Sorcha.Validator.Service/Services/TransactionReceiver.cs` (after line 66)
- [ ] T016 [P] Write transaction size enforcement tests (under limit accepted, over limit rejected with TRANSACTION_TOO_LARGE, configurable limit) in `tests/Sorcha.Validator.Service.Tests/Services/TransactionReceiverSizeTests.cs`

**Checkpoint**: All 4 algorithms work for key wrapping, batch public key endpoint exists, size enforcement active at 4MB

---

## Phase 3: User Story 1 — Encrypted Action Payloads (Priority: P0) MVP

**Goal**: All action payload data encrypted before register storage. No plaintext on the ledger.

**Independent Test**: Submit a blueprint action with disclosure rules, then query the register transaction directly. Payload data MUST NOT be readable without the recipient's private key.

### Tests for User Story 1

- [ ] T017 [P] [US1] Write EncryptionPipelineService unit tests (encrypt with RecipientKeyInfo[], verify ciphertext output, verify wrapped keys per recipient, verify integrity hash) in `tests/Sorcha.TransactionHandler.Tests/Encryption/EncryptionPipelineServiceTests.cs`
- [ ] T018 [P] [US1] Write ActionExecutionService encryption integration tests (mock pipeline, verify encryption called after disclosure, verify plaintext never reaches transaction builder) in `tests/Sorcha.Blueprint.Service.Tests/Services/ActionExecutionServiceEncryptionTests.cs`

### Implementation for User Story 1

- [ ] T019 [US1] Implement EncryptionPipelineService — orchestrate symmetric encryption via ISymmetricCrypto + key wrapping via ICryptoModule for each recipient, return EncryptedPayloadGroup[] in `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- [ ] T020 [US1] Create `BuildEncryptedActionTransactionAsync` method on ITransactionBuilderService that accepts EncryptedPayloadGroup[] instead of plaintext Dictionary — serialize encrypted payloads into PayloadModel[] with `ContentEncoding: "encrypted"` in `src/Services/Sorcha.Blueprint.Service/Services/Interfaces/ITransactionBuilderService.cs`
- [ ] T021 [US1] Implement `BuildEncryptedActionTransactionAsync` in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionBuilderService.cs` — map EncryptedPayloadGroup to PayloadModel (Data=ciphertext, IV=nonce, Challenges=wrapped keys, Hash=integrity hash, PayloadFlags=algorithm)
- [ ] T022 [US1] Modify `ActionExecutionService.ExecuteAsync` at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` lines 249-274 — insert encryption step between `ApplyDisclosures` (line 249) and transaction building (line 267). Initially: resolve keys from external source, encrypt, call new `BuildEncryptedActionTransactionAsync`
- [ ] T023 [US1] Handle default case: when no disclosure rules defined, encrypt full payload under sender's wallet address only at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [ ] T024 [US1] Handle atomicity: if any recipient's key wrapping fails, fail the entire operation with a clear error identifying the failing recipient and algorithm in `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`

**Checkpoint**: Actions produce encrypted transactions. Single algorithm (e.g., ED25519 only) works. Plaintext never stored.

---

## Phase 4: User Story 2 — Disclosure Group Optimization (Priority: P0)

**Goal**: Recipients with identical disclosure field sets grouped — encrypt once per group, wrap key per member.

**Independent Test**: Submit action where 5 recipients share the same disclosure fields. Verify only 1 ciphertext with 5 wrapped keys, not 5 separate ciphertexts.

### Tests for User Story 2

- [ ] T025 [P] [US2] Write DisclosureGroupBuilder unit tests (identical fields → 1 group, N distinct sets → N groups, single unique recipient → 1 group with 1 key, deterministic grouping) in `tests/Sorcha.TransactionHandler.Tests/Encryption/DisclosureGroupBuilderTests.cs`

### Implementation for User Story 2

- [ ] T026 [US2] Implement DisclosureGroupBuilder — hash sorted disclosed field names to create deterministic GroupId, group recipients by hash, return DisclosureGroup[] in `src/Common/Sorcha.TransactionHandler/Encryption/DisclosureGroupBuilder.cs`
- [ ] T027 [US2] Integrate DisclosureGroupBuilder into EncryptionPipelineService — call BuildGroups before encryption loop, encrypt once per group, wrap key per member within group at `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- [ ] T028 [US2] Write integration test verifying optimization: 10 participants across 3 disclosure field sets → exactly 3 ciphertexts with correct wrapped key distribution in `tests/Sorcha.TransactionHandler.Tests/Encryption/EncryptionPipelineServiceTests.cs`

**Checkpoint**: Encryption produces M ciphertexts (not N). Payload size proportional to disclosure groups.

---

## Phase 5: User Story 3 — Algorithm Completeness (Priority: P0)

**Goal**: All 4 encryption-capable wallet algorithms work for payload encryption in a single transaction.

**Independent Test**: Create wallets with each algorithm, submit action with disclosures to all 4, verify each can decrypt.

### Tests for User Story 3

- [ ] T029 [P] [US3] Write mixed-algorithm integration test — single transaction with ED25519 + P-256 + RSA-4096 + ML-KEM-768 recipients, verify all can unwrap the symmetric key and decrypt in `tests/Sorcha.TransactionHandler.Tests/Encryption/MixedAlgorithmEncryptionTests.cs`

### Implementation for User Story 3

- [ ] T030 [US3] Verify ED25519 (Curve25519 SealedBox) key wrap round-trip through EncryptionPipelineService — encrypt 32-byte symmetric key, decrypt, compare in `tests/Sorcha.TransactionHandler.Tests/Encryption/MixedAlgorithmEncryptionTests.cs`
- [ ] T031 [US3] Verify P-256 ECIES key wrap round-trip through EncryptionPipelineService (uses Phase 2 T007 implementation) in `tests/Sorcha.TransactionHandler.Tests/Encryption/MixedAlgorithmEncryptionTests.cs`
- [ ] T032 [US3] Verify RSA-4096 OAEP-SHA256 key wrap round-trip through EncryptionPipelineService (32-byte key well within 446-byte limit) in `tests/Sorcha.TransactionHandler.Tests/Encryption/MixedAlgorithmEncryptionTests.cs`
- [ ] T033 [US3] Verify ML-KEM-768 KEM encapsulate/decapsulate key wrap round-trip through EncryptionPipelineService (uses Phase 2 T009 fix) in `tests/Sorcha.TransactionHandler.Tests/Encryption/MixedAlgorithmEncryptionTests.cs`

**Checkpoint**: All 4 algorithms encrypt and decrypt correctly. Mixed-algorithm transactions work.

---

## Phase 6: User Story 4 — Public Key Resolution (Priority: P1)

**Goal**: Recipient public keys resolved automatically from register. External keys supported as override.

**Independent Test**: Submit action where recipients have published participant records. Verify public keys resolved from register without manual input.

### Tests for User Story 4

- [ ] T034 [P] [US4] Write public key resolution integration tests (register-published keys, external-provided keys, mixed sources, revoked participant 410, not-found warning) in `tests/Sorcha.Blueprint.Service.Tests/Services/PublicKeyResolutionTests.cs`

### Implementation for User Story 4

- [ ] T035 [US4] Integrate batch ResolvePublicKeysBatchAsync into EncryptionPipelineService — collect all recipient wallet addresses, batch resolve from register, merge with externally-provided keys at `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- [ ] T036 [US4] Add `ExternalRecipientKeys` property to action submission request DTO at `src/Services/Sorcha.Blueprint.Service/Models/` — pass through ActionExecutionService to encryption pipeline
- [ ] T037 [US4] Handle resolution failures: revoked participant → fail with clear error, not-found without external key → skip with warning, register unavailable → retry with exponential backoff (3 attempts) at `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`

**Checkpoint**: Public keys resolved automatically from register. External override works. Mixed sources in one transaction.

---

## Phase 7: User Story 5 — Async Encryption with Progress Feedback (Priority: P1)

**Goal**: Encryption runs asynchronously. User gets HTTP 202 immediately. Real-time SignalR progress.

**Independent Test**: Submit action with 20+ recipients, verify immediate response, then verify progress notifications arrive via SignalR before final confirmation.

### Tests for User Story 5

- [ ] T038 [P] [US5] Write EncryptionBackgroundService unit tests (consume from channel, process encryption, send progress, handle failures) in `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionBackgroundServiceTests.cs`
- [ ] T039 [P] [US5] Write SignalR progress notification tests (EncryptionProgress, EncryptionComplete, EncryptionFailed events sent to correct wallet group) in `tests/Sorcha.Blueprint.Service.Tests/Services/EncryptionNotificationTests.cs`

### Implementation for User Story 5

- [ ] T040 [US5] Create EncryptionWorkItem record and register `Channel<EncryptionWorkItem>` (bounded, 100 capacity) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [ ] T041 [US5] Implement EncryptionBackgroundService : BackgroundService — read from channel, call EncryptionPipelineService, send progress via IHubContext<ActionsHub>, update operation store in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [ ] T042 [US5] Implement InMemoryEncryptionOperationStore (ConcurrentDictionary-backed, with cleanup of completed operations after configurable retention) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/InMemoryEncryptionOperationStore.cs`
- [ ] T043 [US5] Modify ActionExecutionService.ExecuteAsync to: perform validate/calculate/route/disclose synchronously, write EncryptionWorkItem to channel, return HTTP 202 with operationId at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [ ] T044 [US5] Add EncryptionProgress, EncryptionComplete, EncryptionFailed events to NotificationService per `contracts/signalr-encryption-events.yaml` — send to `wallet:{submittingWalletAddress}` group at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/NotificationService.cs`
- [ ] T045 [US5] Add `GET /api/operations/{operationId}` polling endpoint for clients without SignalR at `src/Services/Sorcha.Blueprint.Service/Endpoints/` (new file or existing operations endpoint)
- [ ] T046 [US5] Store encryption completion/failure as persistent ActivityEvent for disconnected users at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [ ] T047 [US5] Register Channel<EncryptionWorkItem>, EncryptionBackgroundService, InMemoryEncryptionOperationStore in Blueprint.Service DI at `src/Services/Sorcha.Blueprint.Service/Program.cs`

**Checkpoint**: Actions return HTTP 202 immediately. SignalR delivers progress. Polling fallback works.

---

## Phase 8: User Story 6 — Transaction Size Enforcement (Priority: P1)

**Goal**: Pre-flight size estimation prevents encrypting payloads that would exceed the 4MB limit.

**Independent Test**: Submit a transaction larger than the configured limit. Verify rejected at pipeline (not just validator).

### Tests for User Story 6

- [ ] T048 [P] [US6] Write pre-flight size estimation tests (under limit passes, over limit fails early, estimation accuracy within 10% margin) in `tests/Sorcha.TransactionHandler.Tests/Encryption/SizeEstimationTests.cs`

### Implementation for User Story 6

- [ ] T049 [US6] Add `EstimateEncryptedSizeAsync` to EncryptionPipelineService — calculate `sum(payload_sizes * encryption_overhead) + sum(recipients * wrapped_key_size) + metadata` and compare against configurable limit at `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- [ ] T050 [US6] Call size estimation before encryption starts in EncryptionBackgroundService — fail with 413-equivalent error if estimate exceeds limit, send EncryptionFailed notification at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [ ] T051 [US6] Make MaxTransactionSizeBytes hot-reloadable via `IOptionsMonitor<TransactionReceiverConfiguration>` (instead of IOptions) in Validator Service for configurable-without-restart at `src/Services/Sorcha.Validator.Service/Services/TransactionReceiver.cs`

**Checkpoint**: Oversized transactions caught before expensive encryption. Config changes apply without restart.

---

## Phase 9: User Story 7 — Recipient Decryption (Priority: P2)

**Goal**: Recipients retrieve and decrypt their disclosed payload fields through standard transaction retrieval.

**Independent Test**: As a recipient, retrieve a transaction and verify disclosed fields are returned decrypted while non-disclosed fields are absent.

### Tests for User Story 7

- [ ] T052 [P] [US7] Write recipient decryption tests (authorized decrypt succeeds, unauthorized denied, integrity hash verification, legacy unencrypted backward compat, rotated key error message) in `tests/Sorcha.Blueprint.Service.Tests/Services/RecipientDecryptionTests.cs`

### Implementation for User Story 7

- [ ] T053 [US7] Implement decryption flow in transaction retrieval path — identify payload group(s) by wallet address from Challenges[].Address, unwrap symmetric key via Wallet Service `POST /api/v1/wallets/{address}/decrypt`, decrypt ciphertext with SymmetricCrypto at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionRetrievalService.cs` (new or modify existing)
- [ ] T054 [US7] Add SHA-256 integrity hash verification post-decryption — compare decrypted plaintext hash against stored PlaintextHash, fail with tamper-detected error if mismatch at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionRetrievalService.cs`
- [ ] T055 [US7] Handle access denied — return appropriate error when requesting wallet address is not in any payload group's Challenges at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionRetrievalService.cs`
- [ ] T056 [US7] Handle backward compatibility — detect legacy transactions via `ContentEncoding != "encrypted"` or zeroed IV (`PayloadManager.IsLegacy()` pattern), return plaintext directly at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionRetrievalService.cs`
- [ ] T057 [US7] Handle rotated key failure — return clear error message stating original key is required when decryption fails due to key rotation at `src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionRetrievalService.cs`

**Checkpoint**: Recipients decrypt disclosed fields. Unauthorized access denied. Legacy transactions still work.

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, observability, gateway routes, and final validation

- [ ] T058 [P] Add OpenTelemetry traces for encryption pipeline steps (key resolution, grouping, encryption, key wrapping, transaction building, submission) in `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs` and `src/Services/Sorcha.Blueprint.Service/Services/Implementation/EncryptionBackgroundService.cs`
- [ ] T059 [P] Update Scalar/OpenAPI documentation — add .WithSummary() and .WithDescription() to batch public key endpoint and operations endpoint
- [ ] T060 [P] Add YARP routes for new endpoints (batch public key resolution, operations polling) at `src/Services/Sorcha.ApiGateway/appsettings.json`
- [ ] T061 [P] Update `docs/API-DOCUMENTATION.md` with new endpoints (batch public key, operations, encrypted action flow)
- [ ] T062 [P] Update `docs/development-status.md` with encryption integration completion
- [ ] T063 [P] Update `.specify/MASTER-TASKS.md` — mark T037-T039 complete, add 045 feature tasks
- [ ] T064 Run full test suite: `dotnet test --configuration Release` — verify SC-007 (all existing tests still pass)
- [ ] T065 Performance validation: verify SC-004 (2s for 5 recipients/3 groups/10KB) and SC-005 (500ms first notification)
- [ ] T066 End-to-end validation: run quickstart.md scenarios against Docker Compose deployment

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) ─────▶ Phase 2 (Foundational) ──BLOCKS──▶ All User Stories
                                                            │
                                                            ├── Phase 3 (US1) ──▶ Phase 4 (US2) ──▶ Phase 5 (US3)
                                                            │
                                                            ├── Phase 6 (US4) ── can start after Phase 3
                                                            │
                                                            ├── Phase 7 (US5) ── can start after Phase 3
                                                            │
                                                            ├── Phase 8 (US6) ── can start after Phase 2
                                                            │
                                                            └── Phase 9 (US7) ── can start after Phase 3

Phase 10 (Polish) ── after all desired stories complete
```

### User Story Dependencies

- **US1 (P0)**: Depends on Phase 2 only. **MVP — implement first.**
- **US2 (P0)**: Depends on US1 (extends EncryptionPipelineService with grouping)
- **US3 (P0)**: Depends on US1 (tests all algorithms through the pipeline). Phase 2 does the actual algorithm work.
- **US4 (P1)**: Depends on US1 (adds register lookup to the pipeline)
- **US5 (P1)**: Depends on US1 (wraps the pipeline in async Channel + BackgroundService)
- **US6 (P1)**: Can start after Phase 2 (size estimation is independent of pipeline wiring)
- **US7 (P2)**: Depends on US1 (decryption is the read-path counterpart to encryption)

### Within Each User Story

- Tests written FIRST (fail before implementation)
- Models/interfaces before services
- Services before endpoints
- Core implementation before integration
- Story complete = independently testable

### Parallel Opportunities

**Phase 2** (maximum parallelism — 4 independent workstreams):
1. P-256 ECIES: T007 + T008 (CryptoModule.cs)
2. ML-KEM fix: T009 + T010 (WalletEndpoints.cs)
3. Batch public keys: T011 + T012 + T013 (Register.Service + ServiceClients)
4. Size enforcement: T014 + T015 + T016 (Validator.Service)

**After US1**: US4, US5, US6, US7 can proceed in parallel (different services/files)

**Phase 10**: T058-T063 all parallelizable (different documentation files)

---

## Parallel Example: Phase 2 (Foundational)

```text
# Launch all 4 foundational workstreams in parallel:
Agent 1: "Implement P-256 ECIES encrypt/decrypt in CryptoModule.cs:627-641 and write tests"
Agent 2: "Fix ML-KEM-768 decapsulate in WalletEndpoints.cs:1321 and write tests"
Agent 3: "Add batch public key endpoint to Register.Service and ServiceClient method with tests"
Agent 4: "Raise tx size to 4MB and enforce in TransactionReceiver.cs with tests"
```

## Parallel Example: Post-US1 Stories

```text
# After US1 (Phase 3) is complete, launch stories in parallel:
Agent A: "US4 — Wire batch public key resolution into encryption pipeline"
Agent B: "US5 — Implement Channel<T> + BackgroundService async pipeline with SignalR"
Agent C: "US6 — Add pre-flight size estimation to encryption pipeline"
Agent D: "US7 — Implement recipient decryption in transaction retrieval path"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T006)
2. Complete Phase 2: Foundational (T007-T016) — **max parallelism: 4 agents**
3. Complete Phase 3: US1 (T017-T024)
4. **STOP and VALIDATE**: Submit an action, query the register, confirm ciphertext-only storage
5. This alone closes the critical security gap — plaintext is gone from the ledger

### Incremental Delivery

1. Setup + Foundational → Foundation ready (all algorithms work, limits enforced)
2. Add US1 → **MVP: Encrypted payloads** (single-group-per-recipient)
3. Add US2 → Disclosure grouping optimization (M ciphertexts not N)
4. Add US3 → All 4 algorithms verified end-to-end
5. Add US4 → Automatic public key resolution from register
6. Add US5 → Async pipeline with real-time progress
7. Add US6 → Pre-flight size estimation
8. Add US7 → Recipient decryption flow
9. Polish → Docs, telemetry, gateway routes, validation

### Parallel Team Strategy

With multiple agents after Phase 2:

1. All complete Setup + Foundational together (Phase 2 has 4 parallel workstreams)
2. One agent: US1 → US2 (sequential, same files)
3. Once US1 done:
   - Agent A: US4 (public key resolution)
   - Agent B: US5 (async pipeline)
   - Agent C: US6 + US7 (size estimation + decryption)
4. US3 can run after US1 — mostly test-only phase verifying algorithm completeness

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently testable once its phase is complete
- Verify tests fail before implementing (TDD approach)
- Commit after each phase completion
- The spec has 23 functional requirements — all mapped to tasks:
  - FR-001 to FR-006 → US1 + US2 (Phase 3 + 4)
  - FR-007 to FR-010 → US4 (Phase 6)
  - FR-011 to FR-012 → Phase 2 foundational (T007, T009)
  - FR-013 to FR-017 → US5 (Phase 7)
  - FR-018 to FR-019 → US6 (Phase 2 + Phase 8)
  - FR-020 to FR-022 → US7 (Phase 9)
  - FR-023 → US1 atomicity (T024)
