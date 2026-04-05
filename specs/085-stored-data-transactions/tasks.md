# Tasks: Stored Data Transactions

**Input**: Design documents from `/specs/085-stored-data-transactions/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included per constitution (>85% coverage target for new code).

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Models, schema extensions, and core utilities shared across all user stories

- [x] T001 [P] Create FileReference model in src/Common/Sorcha.Blueprint.Models/FileReference.cs (fileName, contentType, size, hash, salt, chunkTransactionIds, masterKeyId)
- [x] T002 [P] Create FileSchemaExtension model and parser for x-file in src/Common/Sorcha.Blueprint.Models/FileSchemaExtension.cs (accept, maxSizePerFile, maxChunks)
- [x] T003 [P] Create FileChunkMetadata model in src/Common/Sorcha.TransactionHandler/Chunking/FileChunkMetadata.cs (type, parentActionId, fileHash, chunkIndex, totalChunks, contentType, chunkSize)
- [x] T004 [P] Implement HKDF chunk key derivation using System.Security.Cryptography.HKDF in src/Common/Sorcha.TransactionHandler/Encryption/HkdfKeyDerivation.cs (DeriveChunkKey with masterFileKey, random salt, chunkIndex)
- [x] T005 [P] Implement stream-based file chunker with ArrayPool in src/Common/Sorcha.TransactionHandler/Chunking/FileChunker.cs (ChunkFileAsync returning IAsyncEnumerable<ChunkData>, 4MB default, ReadExactlyOrRemainderAsync)
- [x] T006 [P] Write unit tests for HkdfKeyDerivation in tests/Sorcha.TransactionHandler.Tests/HkdfKeyDerivationTests.cs (derive deterministic keys, different indices produce different keys, span-based overload)
- [x] T007 [P] Write unit tests for FileChunker in tests/Sorcha.TransactionHandler.Tests/FileChunkerTests.cs (single chunk under 4MB, multi-chunk split, remainder handling, empty stream, exact 4MB boundary)
- [x] T008 [P] Write unit tests for FileSchemaExtension parser in tests/Sorcha.Blueprint.Models.Tests/FileSchemaExtensionTests.cs (parse accept list, parse maxSizePerFile string to bytes, maxChunks default, invalid values)

**Checkpoint**: Core models and utilities ready — user story implementation can begin

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Schema validation and validator rules that ALL file-related stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T009 Implement file-reference format validation in blueprint engine in src/Core/Sorcha.Blueprint.Engine/Validation/FileReferenceValidator.cs (validate format:"file-reference" fields, parse x-file extension, check accept/maxSizePerFile/maxChunks against schema)
- [x] T010 [P] Write unit tests for FileReferenceValidator in tests/Sorcha.Blueprint.Engine.Tests/FileReferenceValidatorTests.cs (valid single file schema, valid array schema, missing x-file, invalid MIME types, maxSizePerFile exceeds platform 40MB limit, optional file field with null value passes)
- [x] T011 Implement file chunk validation rule in src/Core/Sorcha.Validator.Core/Rules/FileChunkValidationRule.cs (chunk existence check, fileHash matching, contiguous indices, size compliance per chunk and total, MIME type check against schema accept list, same-docket enforcement, reject chunks sealed in other dockets, skip validation for null/absent optional file fields)
- [x] T012 [P] Write unit tests for FileChunkValidationRule in tests/Sorcha.Validator.Core.Tests/FileChunkValidationRuleTests.cs (all chunks present passes, missing chunk rejects, non-contiguous indices rejects, oversized chunk rejects, total size exceeds max rejects, wrong MIME type rejects, chunk in other docket rejects, optional null file field passes, schema maxSize exceeds platform 40MB cap rejects)

**Checkpoint**: Foundation ready — file schema validation and validator rules operational

---

## Phase 3: User Story 1 — Upload File Attachment During Workflow Action (Priority: P1) MVP

**Goal**: Participant can attach a file to a blueprint action, system chunks and encrypts it server-side, submits chunk transactions, then submits the action referencing those chunks. All sealed in the same docket. Includes UI upload component.

**Independent Test**: Create a blueprint with a file field, execute the action with a file attachment via UI, verify file reference in sealed action payload with valid chunk transaction IDs.

### Tests for User Story 1

- [ ] T013 [P] [US1] Write unit tests for TransactionBuilderService file chunking in tests/Sorcha.Blueprint.Service.Tests/TransactionBuilderServiceFileTests.cs (build single chunk tx for small file, build multiple chunk txs for large file, chunk metadata has correct indices/hashes, master key wrapped per recipient, random salt generated per file)
- [ ] T014 [P] [US1] Write integration test for chunk submission endpoint in tests/Sorcha.Blueprint.Service.IntegrationTests/FileChunkSubmissionTests.cs (submit chunk returns tx ID, reject chunk over 4MB, reject invalid metadata)

### Implementation for User Story 1

- [x] T015 [US1] Add file chunk submission endpoint POST /api/file-chunks in src/Services/Sorcha.Blueprint.Service/Endpoints/FileChunkEndpoints.cs (accept FileChunkSubmissionRequest with raw chunk bytes, validate size/metadata, encrypt chunk server-side using HKDF-derived key via TransactionBuilderService, build chunk transaction with PayloadType.Document, submit to validator, return chunkTransactionId)
- [x] T016 [US1] Modify TransactionBuilderService to support chunk-aware file transactions in src/Services/Sorcha.Blueprint.Service/Services/Implementation/TransactionBuilderService.cs (generate MasterFileKey and random salt per file, encrypt each chunk with HKDF-derived keys via HkdfKeyDerivation, wrap MasterFileKey per recipient in action payload Challenges)
- [x] T017 [US1] Modify action submission flow to accept file references in action payload in src/Services/Sorcha.Blueprint.Service/Program.cs (validate file-reference fields in payload, verify all referenced chunkTransactionIds exist, include file references in serialized action payload)
- [x] T018 [US1] Add file chunk submission client method to IBlueprintServiceClient in src/Common/Sorcha.ServiceClients/IBlueprintServiceClient.cs and src/Common/Sorcha.ServiceClients.Http/BlueprintServiceHttpClient.cs
- [x] T019 [US1] Wire chunk submission endpoint through API Gateway YARP config in src/Services/Sorcha.ApiGateway/appsettings.json (route /api/file-chunks to Blueprint Service)
- [x] T020 [US1] Add OpenAPI documentation (WithSummary, WithDescription, XML docs) to file chunk endpoint in src/Services/Sorcha.Blueprint.Service/Endpoints/FileChunkEndpoints.cs
- [x] T021 [US1] Create FileReferenceField.razor component in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Forms/FileReferenceField.razor (MudBlazor UI with file picker button via InputFile, IBrowserFile handling, client-side MIME type and size validation against x-file schema, upload progress bar per file using MudProgressLinear, disable action submit until all uploads complete, support single and array file fields)
- [x] T022 [US1] Implement file upload logic in FileReferenceField.razor (read IBrowserFile.OpenReadStream in chunks, POST raw chunk bytes to /api/file-chunks endpoint via BlueprintServiceClient, server encrypts, build FileReference object with returned tx IDs and metadata, track bytes-read for progress)
- [x] T023 [US1] Add data-testid attributes to FileReferenceField component (file-upload-btn, file-progress-{index}, file-item-{index})

**Checkpoint**: File upload flow works end-to-end — UI → chunks submitted → server encrypts → action references them → validator seals together

---

## Phase 4: User Story 2 — Download File Attachment from Completed Action (Priority: P1)

**Goal**: Authorised participant downloads a file. Wallet Service fetches chunks, unwraps master key, derives chunk keys via HKDF, decrypts, reassembles, verifies hash, streams plaintext file.

**Independent Test**: View a sealed action with file attachments, download a file, verify it matches the original.

### Tests for User Story 2

- [ ] T024 [P] [US2] Write unit tests for FileReassemblyService in tests/Sorcha.Wallet.Core.Tests/FileReassemblyServiceTests.cs (fetch and decrypt single chunk, fetch and decrypt multi-chunk, verify hash passes for valid file, verify hash fails for corrupted chunk, unauthorised wallet rejected)
- [ ] T025 [P] [US2] Write integration test for file download endpoint in tests/Sorcha.Wallet.Service.IntegrationTests/FileDownloadEndpointTests.cs (download single-chunk file, download multi-chunk file, 403 for unauthorised wallet, 404 for missing action, 422 for hash mismatch)

### Implementation for User Story 2

- [ ] T026 [US2] Implement FileReassemblyService in src/Common/Sorcha.Wallet.Core/FileReassembly/FileReassemblyService.cs (fetch action payload to get FileReference, unwrap MasterFileKey from Challenges, fetch each chunk transaction payload from Register Service, derive per-chunk key via HKDF using salt from FileReference, decrypt with XChaCha20-Poly1305, concatenate in order, verify SHA-256 hash, return IAsyncEnumerable<byte[]> for streaming)
- [ ] T027 [US2] Implement file download endpoint GET /api/wallets/{address}/files/download in src/Services/Sorcha.Wallet.Service/Endpoints/FileDownloadEndpoints.cs (JWT auth, verify wallet ownership, parse query params actionTxId/fieldName/fileIndex, call FileReassemblyService, stream via Results.Stream callback with Content-Disposition and Content-Type headers)
- [ ] T028 [US2] Add file download client method to IWalletServiceClient in src/Common/Sorcha.ServiceClients/IWalletServiceClient.cs and src/Common/Sorcha.ServiceClients.Http/WalletServiceHttpClient.cs (GetFileDownloadStreamAsync returning HttpResponseMessage for streaming)
- [ ] T029 [US2] Wire file download endpoint through API Gateway YARP config in src/Services/Sorcha.ApiGateway/appsettings.json (route /api/wallets/*/files/download to Wallet Service)
- [ ] T030 [US2] Add OpenAPI documentation to file download endpoint in src/Services/Sorcha.Wallet.Service/Endpoints/FileDownloadEndpoints.cs
- [ ] T031 [US2] Implement file display component for viewing actions with attachments in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Forms/FileReferenceDisplay.razor (show filename, MIME type icon via MudIcon, human-readable size, download link calling Wallet Service file download endpoint via WalletServiceClient, data-testid="file-download-{index}")

**Checkpoint**: File round-trip works — upload via US1, download via US2, verify integrity

---

## Phase 5: User Story 5 — Validator Enforces File Chunk Integrity (Priority: P1)

**Goal**: Validator rejects actions with invalid file references (missing chunks, wrong types, exceeded limits, chunks in other dockets). Orphaned chunks cleaned up.

**Independent Test**: Submit actions with various invalid chunk references and verify validator rejects each.

### Tests for User Story 5

- [ ] T032 [P] [US5] Write integration tests for validator file chunk enforcement in tests/Sorcha.Validator.Service.IntegrationTests/FileChunkValidatorIntegrationTests.cs (action with valid chunks accepted, action with missing chunk rejected, action with oversized file rejected, action with wrong MIME type rejected, action referencing already-sealed chunks rejected)
- [ ] T033 [P] [US5] Write unit tests for OrphanChunkCleanupService in tests/Sorcha.Blueprint.Service.Tests/OrphanChunkCleanupServiceTests.cs (chunks older than 30 min without action reference discarded, chunks with action reference preserved, recently submitted chunks preserved)

### Implementation for User Story 5

- [ ] T034 [US5] Wire FileChunkValidationRule into validator pipeline in src/Services/Sorcha.Validator.Service/ (register rule, invoke during action validation when payload contains file-reference fields)
- [ ] T035 [US5] Implement same-docket sealing logic for file-bearing actions in src/Core/Sorcha.Validator.Core/ (validator holds action until all referenced chunks available, seals action + chunks in single docket)
- [ ] T036 [US5] Implement OrphanChunkCleanupService as IHostedService in src/Services/Sorcha.Blueprint.Service/Services/Implementation/OrphanChunkCleanupService.cs (background timer, query chunks without action reference older than 30 min, discard with structured logging)
- [ ] T037 [US5] Register OrphanChunkCleanupService in Blueprint Service DI in src/Services/Sorcha.Blueprint.Service/Program.cs

**Checkpoint**: Validator enforces all file chunk integrity rules, orphaned chunks cleaned up

---

## Phase 6: User Story 3 — Upload Multiple Files in Array Field (Priority: P2)

**Goal**: Participant uploads multiple files to an array file field (minItems/maxItems). Each file independently chunked and encrypted.

**Independent Test**: Create blueprint with array file field, upload 3 files of varying sizes, verify each has independent file references.

### Implementation for User Story 3

- [ ] T038 [US3] Extend FileReferenceValidator to handle array file fields (validate minItems/maxItems, each item is valid FileReference) in src/Core/Sorcha.Blueprint.Engine/Validation/FileReferenceValidator.cs
- [ ] T039 [US3] Extend action submission to handle array file references in payload in src/Services/Sorcha.Blueprint.Service/Program.cs (iterate array items, validate each, verify all chunk tx IDs across all files)
- [ ] T040 [US3] Extend FileChunkValidationRule for multi-file actions (validate all files' chunks present, total chunks across all files within reason) in src/Core/Sorcha.Validator.Core/Rules/FileChunkValidationRule.cs
- [ ] T041 [P] [US3] Write unit tests for array file field validation in tests/Sorcha.Blueprint.Engine.Tests/FileReferenceValidatorTests.cs (valid array with 3 items, below minItems rejected, above maxItems rejected, mixed valid/invalid items)

**Checkpoint**: Array file fields work end-to-end — multiple files per field with independent chunking

---

## Phase 7: User Story 4 — Camera Capture on Mobile Device (Priority: P2)

**Goal**: Mobile participant taps "Take Photo" to open device camera, captured photo attaches to file field and begins uploading.

**Independent Test**: On mobile device, tap camera button, take photo, verify photo attached with upload progress.

### Implementation for User Story 4

- [ ] T042 [US4] Add camera capture button to FileReferenceField.razor in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Forms/FileReferenceField.razor (second hidden InputFile with accept="image/*" capture="environment", MudButton with PhotoCamera icon, data-testid="camera-capture-btn", only shown when schema accept includes image types)

**Checkpoint**: Camera capture works on mobile — taps button, camera opens, photo attached and uploads

---

## Phase 8: User Story 6 — View File Metadata Without Downloading (Priority: P3)

**Goal**: Participant viewing a completed action sees file metadata (filename, icon, size) inline without triggering a download.

**Independent Test**: View action with file attachments, verify metadata displayed without file content requests.

### Implementation for User Story 6

- [ ] T043 [US6] Ensure FileReferenceDisplay.razor renders file metadata from action payload only (no API calls for content on initial render) — verify in src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Forms/FileReferenceDisplay.razor
- [ ] T044 [US6] Add MIME type to icon mapping utility (image/* → ImageIcon, application/pdf → PdfIcon, etc.) in src/Apps/Sorcha.UI/Sorcha.UI.Core/Helpers/MimeTypeIconHelper.cs
- [ ] T045 [P] [US6] Write unit test for MimeTypeIconHelper in tests/Sorcha.UI.Core.Tests/MimeTypeIconHelperTests.cs (jpeg maps to image icon, pdf maps to document icon, unknown type maps to generic file icon)

**Checkpoint**: File metadata displays instantly from action payload data

---

## Phase 9: E2E Tests

**Purpose**: End-to-end Playwright tests covering file upload and download flows against Docker

- [ ] T046 [P] Create FileReferenceFieldPage page object in tests/Sorcha.UI.E2E.Tests/PageObjects/FileReferenceFieldPage.cs (locators for file-upload-btn, camera-capture-btn, file-progress, file-item, file-download)
- [ ] T047 Write E2E test for file upload flow in tests/Sorcha.UI.E2E.Tests/Docker/FileAttachmentTests.cs (navigate to action with file field, upload a test file, verify progress bar appears, verify file reference populated, submit action, verify action sealed)
- [ ] T048 Write E2E test for file download flow in tests/Sorcha.UI.E2E.Tests/Docker/FileAttachmentTests.cs (navigate to completed action with file attachment, verify metadata displayed, click download, verify file downloaded)

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, and cross-cutting improvements

- [ ] T049 [P] Update CLAUDE.md with Stored Data Transaction API endpoints and models
- [ ] T050 [P] Update docs/reference/API-DOCUMENTATION.md with file chunk and file download endpoints
- [ ] T051 [P] Update Wallet Service README.md with file download endpoint documentation
- [ ] T052 [P] Update Blueprint Service README.md with file chunk submission endpoint documentation
- [ ] T053 Update .specify/MASTER-TASKS.md with feature 085 status
- [ ] T054 [P] Add structured logging (ILogger) to FileChunkEndpoints, FileDownloadEndpoints, OrphanChunkCleanupService, FileReassemblyService
- [ ] T055 Run quickstart.md validation — test the API examples from specs/085-stored-data-transactions/quickstart.md against Docker environment

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 Upload (Phase 3)**: Depends on Phase 2
- **US2 Download (Phase 4)**: Depends on Phase 2 (and Phase 3 for integration testing)
- **US5 Validator (Phase 5)**: Depends on Phase 2 (can run in parallel with US1/US2)
- **US3 Array Fields (Phase 6)**: Depends on Phase 3 (extends single-file upload)
- **US4 Camera (Phase 7)**: Depends on Phase 3 (extends FileReferenceField.razor)
- **US6 Metadata Display (Phase 8)**: Depends on Phase 4 (extends FileReferenceDisplay.razor)
- **E2E Tests (Phase 9)**: Depends on Phases 3, 4, 7
- **Polish (Phase 10)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: After Foundational — no dependencies on other stories. Includes core UI upload component.
- **US2 (P1)**: After Foundational — includes file display component. Integration test needs US1.
- **US5 (P1)**: After Foundational — independent of US1/US2, tests validator rules in isolation.
- **US3 (P2)**: After US1 — extends single-file to array (engine + service changes).
- **US4 (P2)**: After US1 — adds camera capture button to existing FileReferenceField.razor.
- **US6 (P3)**: After US2 — extends FileReferenceDisplay.razor with icon mapping.

### Within Each User Story

- Tests written first (fail before implementation)
- Models/utilities before services
- Services before endpoints
- Endpoints before UI integration
- Story complete before moving to next priority

### Parallel Opportunities

- Phase 1: T001-T008 all parallelisable (different files)
- Phase 2: T010/T012 parallelisable with T009/T011 respectively
- Phase 3-5: US1, US2, US5 can start in parallel after Phase 2 (different services)
- Phase 6-7: US3 and US4 can run in parallel (different areas: engine vs UI)

---

## Parallel Example: Phase 1 Setup

```bash
# All setup tasks target different files — run all in parallel:
Task T001: FileReference.cs
Task T002: FileSchemaExtension.cs
Task T003: FileChunkMetadata.cs
Task T004: HkdfKeyDerivation.cs
Task T005: FileChunker.cs
Task T006: HkdfKeyDerivationTests.cs
Task T007: FileChunkerTests.cs
Task T008: FileSchemaExtensionTests.cs
```

## Parallel Example: P1 User Stories

```bash
# After Phase 2, all three P1 stories can start in parallel:
# Stream 1: US1 (Blueprint Service — chunk submission + UI upload)
# Stream 2: US2 (Wallet Service — file download + UI display)
# Stream 3: US5 (Validator Service — chunk integrity rules)
```

---

## Implementation Strategy

### MVP First (US1 + US2 + US5)

1. Complete Phase 1: Setup (models + utilities)
2. Complete Phase 2: Foundational (schema validation + validator rules)
3. Complete Phase 3: US1 — File Upload + UI component (chunk submission + upload UI)
4. Complete Phase 4: US2 — File Download + display component (wallet-mediated retrieval)
5. Complete Phase 5: US5 — Validator Enforcement (integrity + orphan cleanup)
6. **STOP and VALIDATE**: Test file round-trip via UI (upload → seal → download → verify hash)

### Incremental Delivery

1. Setup + Foundational → Core infrastructure ready
2. US1 → File upload works via API + UI (MVP)
3. US2 → File download works via API + UI (full round-trip)
4. US5 → Validator enforces integrity (production-safe)
5. US3 → Array file fields (multi-file support)
6. US4 → Camera capture (mobile UX enhancement)
7. US6 → File metadata icons (polish)
8. E2E + Polish → Production-ready

---

## Notes

- [P] tasks target different files with no dependencies
- [Story] labels map tasks to spec.md user stories for traceability
- Each user story is independently completable and testable
- Tests written first, verified to fail before implementation
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Encryption happens server-side in Blueprint Service (TransactionBuilderService), not in Blazor WASM client
- Client uploads raw chunk bytes over authenticated HTTPS; server encrypts with HKDF-derived keys
- Total tasks: 55
