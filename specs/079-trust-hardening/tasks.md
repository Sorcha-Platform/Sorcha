# Tasks: Transaction Receipts, Merkle Inclusion Proofs & Revocation Transactions

**Input**: Design documents from `/specs/079-trust-hardening/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Shared models and enum extensions that all user stories depend on.

- [X] T001 Add `Revocation = 4` to TransactionType enum in `src/Common/Sorcha.Register.Models/Enums/TransactionType.cs`
- [X] T002 [P] Create `MerkleProofStep` record, `ProofPosition` enum, and `MerkleInclusionProof` record in `src/Common/Sorcha.Register.Models/MerkleInclusionProof.cs` (fields: TransactionHash, DocketNumber, MerkleRoot, ProofPath as MerkleProofStep[], LeafIndex, TreeSize)
- [X] T003 [P] Create `ValidatorSignature` record, `ValidatorKeyInfo` record, and `TransactionReceipt` record in `src/Common/Sorcha.Register.Models/TransactionReceipt.cs` (ValidatorSignature fields: ValidatorAddress, SignatureValue, Algorithm, SignedAt; TransactionReceipt fields: ReceiptId, TransactionId, RegisterId, DocketNumber, MerkleRoot, InclusionProof, Signatures[], SealedAt, Version)
- [X] T004 [P] Create `RevocationReason` enum and `RevocationPayload` record in `src/Common/Sorcha.Register.Models/RevocationPayload.cs` (fields: OriginalTxId, OriginalDocketNumber, Reason, SupersededByTxId?, Metadata?)
- [X] T005 [P] Create `TransactionLifecycleStatus` enum and `TransactionStatusResponse` record in `src/Common/Sorcha.Register.Models/TransactionStatusResponse.cs` (fields: TransactionId, Status, RevocationTxId?, SupersededByTxId?, RevokedAt?, Reason?)
- [X] T006 [P] Create `VerificationBundle` record in `src/Common/Sorcha.Register.Models/VerificationBundle.cs` (fields: Version, TransactionId, RegisterId, Credential, Receipt, RevocationStatus, ExportedAt, ValidatorPublicKeys[])

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Merkle proof generation capability — needed by US1 (receipts contain proofs) and US2 (standalone proofs).

**CRITICAL**: No user story work can begin until this phase is complete.

- [X] T007 Extend `MerkleTree.ComputeMerkleRoot()` to retain tree level structure as internal state in `src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs` — refactor the tree-building loop to store each level's hashes in a `List<List<string>>`
- [X] T008 Add `GenerateInclusionProof(int leafIndex, IReadOnlyList<string> transactionHashes)` method to `MerkleTree` in `src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs` — build tree, extract sibling path with Left/Right positions, return `MerkleInclusionProof`
- [X] T009 Add `GenerateAllProofs(IReadOnlyList<string> transactionHashes)` method to `MerkleTree` in `src/Common/Sorcha.Cryptography/Utilities/MerkleTree.cs` — build tree once, generate all proofs in one O(n log n) pass
- [X] T010 [P] Create unit tests for `GenerateInclusionProof` in `tests/Sorcha.Cryptography.Tests/Utilities/MerkleTreeInclusionProofTests.cs` — test single leaf, two leaves, odd count, large tree (100+ txs), boundary cases
- [X] T011 [P] Create unit tests for `GenerateAllProofs` in `tests/Sorcha.Cryptography.Tests/Utilities/MerkleTreeInclusionProofTests.cs` — test all proofs verify against root, round-trip with existing `VerifyMerkleProof()`

**Checkpoint**: Merkle proof generation works. All generated proofs pass verification via `VerifyMerkleProof()`.

---

## Phase 3: User Story 1 — Transaction Receipts (Priority: P1)

**Goal**: Generate signed receipts for every sealed transaction, retrievable by txId, pushed via SignalR.

**Independent Test**: Submit a transaction, wait for sealing, retrieve receipt, verify validator signature offline.

### Implementation for User Story 1

- [X] T012 [US1] Create `IReceiptGenerator` interface and `ReceiptGenerator` service in `src/Services/Sorcha.Validator.Service/Services/ReceiptGenerator.cs` — method `GenerateReceiptsForDocketAsync(Docket docket, CancellationToken ct)` that: computes all inclusion proofs via `MerkleTree.GenerateAllProofs()`, builds `TransactionReceipt` for each tx, signs each receipt hash with Validator system wallet via `IWalletServiceClient.SignDataAsync()`
- [X] T013 [US1] Register `IReceiptGenerator` in Validator Service DI in `src/Services/Sorcha.Validator.Service/Program.cs`
- [X] T014 [US1] Modify `DocketDistributor.SubmitToRegisterServiceAsync()` in `src/Services/Sorcha.Validator.Service/Services/DocketDistributor.cs` — after successful `WriteDocketAsync()`, call `_receiptGenerator.GenerateReceiptsForDocketAsync(docket)` and submit receipts via `POST /receipts/batch` to Register Service
- [X] T015 [US1] Add `POST /api/registers/{registerId}/receipts/batch` internal endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — accepts array of `TransactionReceipt`, stores in MongoDB `receipts` collection, publishes `receipt:generated` event to Redis Stream
- [X] T016 [US1] Add `GET /api/registers/{registerId}/transactions/{txId}/receipt` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — retrieves receipt from `receipts` collection by txId index, returns 404 if not yet sealed
- [X] T017 [US1] Add `GET /api/registers/{registerId}/dockets/{docketNumber}/receipts` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — list receipts for a docket with pagination
- [X] T018 [US1] Add `POST /api/registers/{registerId}/receipts/verify` public endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — stateless receipt verification (signature + inclusion proof check)
- [X] T019 [US1] Add receipt storage methods to `IRegisterRepository` and MongoDB implementation in `src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterRepository.cs` — `InsertReceiptsAsync()`, `GetReceiptByTxIdAsync()`, `GetReceiptsByDocketAsync()` with indexes on TransactionId (unique) and RegisterId+DocketNumber (compound)
- [X] T020 [US1] Add `IRegisterServiceClient` methods for receipt batch write in `src/Common/Sorcha.ServiceClients/Register/IRegisterServiceClient.cs` and implementation
- [X] T021 [US1] Add `TransactionReceipt` SignalR client method to `IRegisterHubClient` in `src/Services/Sorcha.Register.Service/Hubs/RegisterHub.cs`
- [X] T022 [US1] Subscribe to `receipt:generated` Redis Stream event in `RegisterEventBridgeService` in `src/Services/Sorcha.Register.Service/Services/RegisterEventBridgeService.cs` — push lightweight notification to `register:{registerId}` SignalR group
- [X] T023 [US1] Create `ReceiptValidator` in `src/Common/Sorcha.Validator.Core/ReceiptValidator.cs` — verify receipt signature against validator public key, verify embedded inclusion proof
- [X] T024 [P] [US1] Create unit tests for `ReceiptGenerator` in `tests/Sorcha.Validator.Service.Tests/Services/ReceiptGeneratorTests.cs` — test receipt generation for docket with 1, 5, 100 transactions; verify each receipt has valid proof path; mock wallet signing
- [X] T025 [P] [US1] Create unit tests for `ReceiptValidator` in `tests/Sorcha.Validator.Core.Tests/ReceiptValidatorTests.cs` — test valid signature passes, tampered receipt fails, tampered proof fails
- [ ] T026 [P] [US1] Create integration tests for receipt endpoints in `tests/Sorcha.Register.Service.Tests/Endpoints/ReceiptEndpointTests.cs` — test GET receipt by txId (200/404), GET docket receipts (pagination), POST verify (valid/invalid), POST batch (201), SignalR receipt notification push to register group

**Checkpoint**: Receipts are generated for every sealed docket, stored in MongoDB, retrievable by txId, pushed via SignalR, verifiable offline.

---

## Phase 4: User Story 2 — Merkle Inclusion Proofs (Priority: P1)

**Goal**: Expose standalone inclusion proof generation and verification endpoints, extend Validator.Core for portable verification.

**Independent Test**: Request inclusion proof for a sealed transaction, verify it recomputes to the correct Merkle root.

**Dependencies**: Phase 2 (MerkleTree proof generation). US1 receipts already embed proofs; this phase adds standalone access.

### Implementation for User Story 2

- [X] T027 [US2] Add `GET /api/registers/{registerId}/transactions/{txId}/inclusion-proof` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — fetch docket for the transaction, reconstruct tree, generate proof on-demand via `MerkleTree.GenerateInclusionProof()`
- [X] T028 [US2] Add `POST /api/registers/{registerId}/inclusion-proofs/verify` public endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — stateless proof verification via `MerkleTree.VerifyMerkleProof()`
- [X] T029 [US2] Create `InclusionProofValidator` in `src/Common/Sorcha.Validator.Core/InclusionProofValidator.cs` — portable proof verification that delegates to `MerkleTree.VerifyMerkleProof()`, returns structured result
- [X] T030 [P] [US2] Create unit tests for `InclusionProofValidator` in `tests/Sorcha.Validator.Core.Tests/InclusionProofValidatorTests.cs` — test valid proof, tampered hash, wrong root, empty proof path (single-leaf docket)
- [ ] T031 [P] [US2] Create integration tests for inclusion proof endpoints in `tests/Sorcha.Register.Service.Tests/Endpoints/InclusionProofEndpointTests.cs` — test GET proof (200/404), POST verify (valid/tampered)

**Checkpoint**: Standalone proofs can be generated and verified. Works alongside embedded receipt proofs.

---

## Phase 5: User Story 3 — Revocation Transactions (Priority: P2)

**Goal**: First-class revocation transaction type with validation, sealing, and status endpoint.

**Independent Test**: Seal a transaction, submit revocation, verify original status changes to "revoked".

**Dependencies**: Phase 2 (foundational). Independent of US1/US2 (revocations don't require receipts, though revocations themselves will get receipts once US1 is also complete).

### Implementation for User Story 3

- [X] T032 [US3] Create `RevocationValidator` in `src/Common/Sorcha.Validator.Core/RevocationValidator.cs` — validate revocation payload structure, reason enum, supersededByTxId consistency rules
- [X] T033 [US3] Add revocation validation path in `ValidationEngine.ValidateTransactionAsync()` in `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs` — detect `TransactionType.Revocation`, call `ValidateRevocationAsync()` which: parses `RevocationPayload`, queries Register for target tx existence, checks not already revoked, checks target is not a Revocation tx, performs authority check (original signer match OR governance roster Owner/Admin), validates reason + supersededByTxId consistency
- [X] T034 [US3] Add `POST /api/registers/{registerId}/transactions/revoke` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — accept revocation request, build `RevocationPayload`, create transaction with `TransactionType.Revocation`, submit to Validator pipeline
- [X] T035 [US3] Add `GET /api/registers/{registerId}/transactions/{txId}/status` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — query for revocation transactions referencing targetTxId, return `TransactionStatusResponse` (active/revoked/superseded)
- [X] T036 [US3] Add revocation-specific query method to `IRegisterRepository` and MongoDB implementation in `src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterRepository.cs` — `FindRevocationForTransactionAsync(registerId, targetTxId)` with index on `Metadata.OriginalTxId`
- [X] T037 [P] [US3] Create unit tests for `RevocationValidator` in `tests/Sorcha.Validator.Core.Tests/RevocationValidatorTests.cs` — test valid payload, invalid reason, missing supersededByTxId for Superseded reason, extra supersededByTxId for non-Superseded reason
- [X] T038 [P] [US3] Create unit tests for revocation validation in `tests/Sorcha.Validator.Service.Tests/Services/ValidationEngineRevocationTests.cs` — test authority checks (original signer passes, roster admin passes, non-member rejected), double-revocation rejected, self-revocation-of-revocation rejected
- [ ] T039 [P] [US3] Create integration tests for revocation and status endpoints in `tests/Sorcha.Register.Service.Tests/Endpoints/RevocationEndpointTests.cs` — test POST revoke (202/400), GET status (active/revoked/superseded/404), error codes (ALREADY_REVOKED, CANNOT_REVOKE_REVOCATION, UNAUTHORIZED_REVOKER)

**Checkpoint**: Revocations are validated, sealed, and persisted. Status endpoint returns correct lifecycle state.

---

## Phase 6: User Story 4 — Offline Verification Bundle (Priority: P2)

**Goal**: Export portable verification bundles, verify all four checks offline.

**Independent Test**: Export bundle, verify on air-gapped machine using `BundleVerifier`.

**Dependencies**: US1 (receipts), US2 (proofs), US3 (revocation status). This is the integration story.

### Implementation for User Story 4

- [X] T040 [US4] Create `BundleVerifier` in `src/Common/Sorcha.Validator.Core/BundleVerifier.cs` — orchestrate four checks: credential signature, inclusion proof (via `InclusionProofValidator`), receipt signature (via `ReceiptValidator`), revocation status at export time. Return structured `BundleVerificationResult` with per-check status and warnings
- [X] T041 [US4] Add `GET /api/registers/{registerId}/transactions/{txId}/verification-bundle` endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — assemble `VerificationBundle` from: transaction payload (credential), receipt, inclusion proof, revocation status snapshot, validator public keys. Return 404 if tx not found, 409 if not yet sealed
- [X] T042 [US4] Add `POST /api/registers/{registerId}/verification-bundles/verify` public endpoint in `src/Services/Sorcha.Register.Service/Program.cs` — accept bundle, verify all four checks via `BundleVerifier`, return structured result with warnings (e.g., stale revocation status)
- [X] T043 [P] [US4] Create unit tests for `BundleVerifier` in `tests/Sorcha.Validator.Core.Tests/BundleVerifierTests.cs` — test all-valid bundle, invalid credential signature, invalid inclusion proof, invalid receipt signature, revoked-after-export warning
- [ ] T044 [P] [US4] Create integration tests for bundle endpoints in `tests/Sorcha.Register.Service.Tests/Endpoints/VerificationBundleTests.cs` — test GET bundle (200/404/409), POST verify (all checks pass, partial failures)

**Checkpoint**: Bundles can be exported and verified offline. All four checks work independently and together.

---

## Phase 7: User Story 5 — Transaction Lifecycle Indicators (Priority: P2)

**Goal**: WhatsApp-style double-tick UX for transaction lifecycle: submitted (grey tick), sealed (blue tick), receipted (double blue ticks).

**Independent Test**: Submit a transaction, observe ticks progressing from grey → blue → double-blue via real-time notifications.

**Dependencies**: US1 (receipts must be generated and notified). Independent of US2/US3/US4.

### Implementation for User Story 5

- [X] T045 [US5] Create `WalletTransactionRecord` entity in `src/Core/Sorcha.Wallet.Core/Models/WalletTransactionRecord.cs` — fields: Id, WalletAddress, TransactionId, RegisterId, Direction (Outbound/Inbound), DocketNumber?, ReceiptId?, Status (Pending/Sealed/Receipted), SubmittedAt, SealedAt?, ReceiptedAt?, CounterpartyAddress?. Add EF Core config and migration. Outbound = wallet signed it, Inbound = wallet is a recipient.
- [X] T046 [US5] Create `TransactionLifecycleService` in `src/Services/Sorcha.Wallet.Service/Services/TransactionLifecycleService.cs` — records submitted transactions (called from existing CreateWallet/SignTransaction flows), updates status on docket:confirmed and receipt:generated events. Methods: RecordSubmissionAsync, MarkSealedAsync, MarkReceiptedAsync, GetStatusAsync.
- [X] T047 [US5] Create `TransactionLifecycleEventBridge` background service in `src/Services/Sorcha.Wallet.Service/Services/TransactionLifecycleEventBridge.cs` — subscribes to Redis Stream events (docket:confirmed, receipt:generated), calls TransactionLifecycleService to update records, pushes `TransactionLifecycleUpdate` event to ActionsHub wallet group via INotificationService or direct SignalR
- [X] T048 [US5] Add `OnTransactionLifecycleUpdate` event to `ActionsHubConnection` in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ActionsHubConnection.cs` — new event with TxId, Status (Pending/Sealed/Receipted), ReceiptId?, DocketNumber?
- [X] T049 [US5] Create `TransactionLifecycleTicks.razor` component in `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Wallet/TransactionLifecycleTicks.razor` — renders tick indicators: single grey tick (Pending), single blue tick (Sealed), double blue ticks (Receipted). Click on double-tick opens receipt detail. CSS isolation file for tick styling.
- [ ] T050 [US5] *(Deferred — requires running UI)* Integrate tick component into wallet transaction list page — add `<TransactionLifecycleTicks>` to each transaction row, subscribe to `OnTransactionLifecycleUpdate` events, wire up initial state from `GET /api/v1/wallets/{address}/transaction-status/{txId}`
- [X] T051 [P] [US5] Create unit tests for `TransactionLifecycleService` in `tests/Sorcha.Wallet.Service.Tests/Services/TransactionLifecycleServiceTests.cs` — test Pending→Sealed→Receipted progression, duplicate event handling, unknown txId handling

**Checkpoint**: Transaction list shows real-time tick progression. Receipt notifications arrive via wallet group.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, API docs, cleanup.

- [X] T052 Update Register Service README with receipt, revocation, status, bundle, and proof endpoints in `src/Services/Sorcha.Register.Service/README.md`
- [ ] T053 [P] Update Validator Service README with receipt generation and revocation validation in `src/Services/Sorcha.Validator.Service/README.md`
- [ ] T054 [P] Add OpenAPI `.WithSummary()` and `.WithDescription()` to all new endpoints in `src/Services/Sorcha.Register.Service/Program.cs`
- [ ] T055 [P] Add XML doc comments to all new public types in `src/Common/Sorcha.Register.Models/` and `src/Common/Sorcha.Validator.Core/`
- [X] T056 Update CLAUDE.md with new endpoints (receipt, revocation, status, bundle, proof, lifecycle ticks) in project root `CLAUDE.md`
- [X] T057 Update `.specify/MASTER-TASKS.md` — mark TRUST-3, TRUST-4, TRUST-5 as complete, add Feature 079 entry
- [ ] T058 [P] Update API documentation in `docs/reference/API-DOCUMENTATION.md` with new endpoints
- [ ] T059 *(Deferred — requires Docker stack)* Run quickstart.md scenarios end-to-end to validate all integration scenarios

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (needs `MerkleInclusionProof` model from T002)
- **US1 Receipts (Phase 3)**: Depends on Phase 2 (needs `GenerateAllProofs()`)
- **US2 Proofs (Phase 4)**: Depends on Phase 2 (needs `GenerateInclusionProof()`)
- **US3 Revocation (Phase 5)**: Depends on Phase 1 only (needs `TransactionType.Revocation`, `RevocationPayload` model)
- **US5 Lifecycle Ticks (Phase 7)**: Depends on US1 (needs receipt notifications flowing)
- **US4 Bundles (Phase 6)**: Depends on US1 + US2 + US3 (integration story)
- **Polish (Phase 8)**: Depends on all user stories being complete

### User Story Dependencies

```
Phase 1: Setup
    │
    ├──▶ Phase 2: Foundational (MerkleTree extension)
    │       │
    │       ├──▶ Phase 3: US1 — Receipts (P1) ──────┬──▶ Phase 7: US5 — Lifecycle Ticks (P2)
    │       │                                         │
    │       └──▶ Phase 4: US2 — Proofs (P1) ─────────┤
    │                                                  │
    └──▶ Phase 5: US3 — Revocation (P2) ──────────────┤
                                                       │
                                              Phase 6: US4 — Bundles (P2)
                                                       │
                                              Phase 8: Polish
```

### Parallel Opportunities

- **Phase 1**: T002, T003, T004, T005, T006 can all run in parallel (different files)
- **Phase 2**: T010, T011 tests can run in parallel while T007-T009 are sequential
- **Phase 3+4**: US1 (Receipts) and US2 (Proofs) can run in parallel after Phase 2
- **Phase 5**: US3 (Revocation) can start after Phase 1, in parallel with Phase 2/3/4
- **Within each phase**: Tests marked [P] can run in parallel with each other

---

## Parallel Example: US1 + US2 + US3 After Phase 2

```bash
# After Phase 2 completes, launch three streams in parallel:

# Stream A: US1 — Receipts
Task: T012-T026 (receipt generation, storage, endpoints, SignalR, tests)

# Stream B: US2 — Proofs (lighter workload)
Task: T027-T031 (proof endpoints, Validator.Core, tests)

# Stream C: US3 — Revocation (can start even earlier, after Phase 1)
Task: T032-T039 (revocation validation, endpoints, status, tests)

# Then: US4 — Bundles (after all three complete)
Task: T040-T044 (bundle assembly, offline verification, tests)

# Then: US5 — Lifecycle Ticks (after US1 receipts flow)
Task: T045-T049 (wallet-group notification, tracker service, tick component, tests)
```

---

## Implementation Strategy

### MVP First (US1 + US2 — Receipts & Proofs)

1. Complete Phase 1: Setup models and enums
2. Complete Phase 2: MerkleTree proof generation
3. Complete Phase 3: US1 — Transaction receipts
4. Complete Phase 4: US2 — Standalone inclusion proofs
5. **STOP and VALIDATE**: Receipts are generated, stored, retrievable, verifiable offline
6. Deploy/demo: "Here's cryptographic proof your transaction was sealed"

### Incremental Delivery

1. Setup + Foundational → Proof generation works
2. Add US1 (Receipts) → Participants get signed receipts → Deploy
3. Add US2 (Proofs) → Standalone proof endpoints → Deploy
4. Add US3 (Revocation) → Credentials can be revoked on-chain → Deploy
5. Add US4 (Bundles) → Portable offline verification → Deploy
6. Add US5 (Lifecycle Ticks) → WhatsApp-style double-tick UX → Deploy
7. Each increment adds trust capability without breaking previous ones

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US3 (Revocation) can start after Phase 1, not Phase 2 — it doesn't need MerkleTree changes
- US4 (Bundles) is the only story with cross-story dependencies
- All new endpoints need `.WithSummary()` and `.WithDescription()` for Scalar OpenAPI
- All new models need license headers: `// SPDX-License-Identifier: MIT`
- Receipt storage uses existing per-register MongoDB database pattern
- Revocation authority check reuses existing `RightsEnforcementService` roster reconstruction
