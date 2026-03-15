# Tasks: System Register as Real Ledger

**Input**: Design documents from `/specs/057-system-register-ledger/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/system-register-api.yaml

**Tests**: Not explicitly requested in spec. Unit tests included where critical for correctness.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Prepare the codebase for migration — remove old infrastructure, add new derivation path

- [x] T001 Delete `src/Services/Sorcha.Register.Service/Repositories/MongoSystemRegisterRepository.cs` and `ISystemRegisterRepository.cs` — remove the separate MongoDB repository and interface for the old system register storage
- [x] T002 Delete `src/Services/Sorcha.Register.Service/Core/SystemRegisterEntry.cs` — remove the old BSON entry model that is replaced by standard `TransactionModel`
- [x] T003 Remove all references to `ISystemRegisterRepository` and `SystemRegisterEntry` from DI registration in `src/Services/Sorcha.Register.Service/Program.cs` — clean up service collection registrations that reference deleted types
- [x] T004 Add `"sorcha:blueprint-publish"` to the `AllowedDerivationPaths` list in system wallet signing configuration in `src/Services/Sorcha.Register.Service/appsettings.json` and Docker `appsettings.Docker.json` — enables signing blueprint transactions with a purpose-specific derivation path

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core refactoring of `SystemRegisterService` to query the real register instead of the deleted MongoDB collection

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T005 Refactor `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs` — replace all `ISystemRegisterRepository` calls with queries against the real system register's transactions via `IRegisterServiceClient` or direct MongoDB queries on the system register's per-register database. The service must: (a) check if system register exists via `IRegisterManager.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId)`, (b) query transactions with `Metadata["Type"] == "BlueprintPublish"` to find blueprints, (c) extract blueprint JSON from `Payloads[0].Data` (base64url decode), (d) derive version from transaction chain (count predecessors via `PrevTxId`), (e) implement `GetSystemRegisterInfoAsync` using real register metadata (height, transaction count), (f) implement `PublishBlueprintAsync` by constructing a `TransactionSubmission` with blueprint JSON as payload, signing with system wallet via `ISystemWalletSigningService` (derivation path: `sorcha:blueprint-publish`), and submitting via `IValidatorServiceClient.SubmitTransactionAsync`
- [x] T006 Update `SystemRegisterService` constructor DI — inject `IRegisterManager`, `IValidatorServiceClient`, `ISystemWalletSigningService`, `IHashProvider` (from `Sorcha.Cryptography`), and remove `ISystemRegisterRepository` dependency
- [x] T007 Update `src/Services/Sorcha.Register.Service/Services/SystemRegisterService.cs` method `GetSystemRegisterInfoAsync` — return `status: "initialized"` when the system register exists in the register registry (not based on blueprint count), include real register `Height` and transaction count in the response, map to `SystemRegisterInfo` record
- [x] T008 Verify `src/Services/Sorcha.Register.Service/Program.cs` compiles cleanly after removing old repository references and updating `SystemRegisterService` DI registration

**Checkpoint**: Foundation ready — `SystemRegisterService` now operates on the real register

---

## Phase 3: User Story 1 — Platform Bootstraps System Register on First Startup (Priority: P1) 🎯 MVP

**Goal**: On first startup, automatically create the system register as a real register and publish seed blueprints as transactions

**Independent Test**: Start fresh Docker deployment (`docker-compose down -v && docker-compose up -d`). Verify system register appears in registers list, has genesis docket, and contains 2 blueprint transactions.

### Implementation for User Story 1

- [x] T009 [US1] Rewrite `src/Services/Sorcha.Register.Service/Services/SystemRegisterBootstrapper.cs` — replace the old direct-MongoDB initialization with the following flow: (1) Check if system register already exists via `IRegisterManager.GetRegisterAsync(SystemRegisterConstants.SystemRegisterId)` — if exists, check for seed blueprints and skip/retry as needed. (2) If register does not exist, call `RegisterCreationOrchestrator.InitiateAsync` with `InitiateRegisterCreationRequest` using deterministic ID `SystemRegisterConstants.SystemRegisterId`, name `SystemRegisterConstants.SystemRegisterName`, tenant `"system"`, and system wallet as sole owner with role `Owner`. (3) Sign the attestation: get the `AttestationsToSign[0].DataToSign` hash, sign it with `ISystemWalletSigningService.SignAsync` using derivation path `"sorcha:register-control"`. (4) Call `RegisterCreationOrchestrator.FinalizeAsync` with the signed attestation (nonce from initiation response, `SignedAttestation` with public key, signature, algorithm). (5) After finalize succeeds, poll `IRegisterServiceClient.GetRegisterHeightAsync` until height > 0 (genesis docket sealed), polling every 1 second with 30-second timeout. (6) Submit seed blueprints (T010, T011).
- [x] T010 [US1] Implement seed blueprint submission in `SystemRegisterBootstrapper` — after genesis docket is sealed, call `SystemRegisterService.PublishBlueprintAsync` for `register-creation-v1` blueprint (same JSON content as the old `CreateRegisterCreationBlueprintDocument` method, but serialized as JSON string payload). Use `blueprintId = "register-creation-v1"`, no `previousTransactionId` (first version).
- [x] T011 [US1] Implement seed blueprint submission for `register-governance-v1` in `SystemRegisterBootstrapper` — same pattern as T010, using the governance blueprint JSON content from the old `CreateGovernanceBlueprintDocument` method. Use `blueprintId = "register-governance-v1"`, no `previousTransactionId`.
- [x] T012 [US1] Add retry logic with exponential backoff to `SystemRegisterBootstrapper` — wrap the bootstrap flow in a retry loop (max 3 attempts, delays: 2s → 4s → 8s). Log warnings on retry, error on final failure. Ensure partial progress is handled: if register exists but blueprints missing, only retry blueprint submission.
- [x] T013 [US1] Remove `SORCHA_SEED_SYSTEM_REGISTER` environment variable check from `SystemRegisterBootstrapper` — bootstrap should always run (idempotent), not be gated by an env var. Remove the `SystemRegisterConfiguration` class from `src/Services/Sorcha.Register.Service/Configuration/SystemRegisterConfiguration.cs` if it only served this purpose.
- [x] T014 [US1] Update `src/Services/Sorcha.Register.Service/Program.cs` — ensure `SystemRegisterBootstrapper` is still registered as a hosted service but no longer depends on the env var flag. Verify all new DI dependencies (`IRegisterCreationOrchestrator`, `ISystemWalletSigningService`, `IValidatorServiceClient`) are available.
- [x] T015 [US1] Write unit test in `tests/Sorcha.Register.Service.Tests/SystemRegisterBootstrapperTests.cs` — test idempotent bootstrap: (a) mock `IRegisterManager.GetRegisterAsync` returning null → verify InitiateAsync and FinalizeAsync are called, (b) mock returning existing register with blueprints → verify no calls made, (c) mock returning existing register without blueprints → verify only blueprint submission called.

**Checkpoint**: Fresh deployment auto-creates system register with seed blueprints. Restarts are idempotent.

---

## Phase 4: User Story 2 — Administrator Publishes a Blueprint (Priority: P2)

**Goal**: Enable blueprint publishing via API, where blueprints become transactions on the system register

**Independent Test**: Call `POST /api/system-register/publish` with a blueprint JSON. Verify it appears as a transaction on the system register and is queryable via `GET /api/system-register/blueprints`.

### Implementation for User Story 2

- [x] T016 [US2] Add `POST /api/system-register/publish` endpoint in `src/Services/Sorcha.Register.Service/Endpoints/SystemRegisterEndpoints.cs` — accepts `PublishBlueprintRequest` (blueprintId, blueprint JSON, optional previousTransactionId, optional metadata), calls `SystemRegisterService.PublishBlueprintAsync`, returns 201 with transactionId and blueprintId. Requires `CanManageRegisters` authorization. Add Scalar documentation with `.WithName("PublishBlueprint")`, `.WithSummary()`, `.WithDescription()`.
- [x] T017 [US2] Update `GET /api/system-register/blueprints` endpoint in `SystemRegisterEndpoints.cs` — wire to the refactored `SystemRegisterService.GetAllBlueprintsAsync` which now queries real register transactions. Map transaction data to `BlueprintSummaryResponse` (blueprintId from metadata, version from chain, publishedAt from timestamp, publishedBy from sender wallet).
- [x] T018 [US2] Update `GET /api/system-register/blueprints/{blueprintId}` endpoint — wire to `SystemRegisterService.GetBlueprintAsync` which finds the latest transaction with matching `Metadata["BlueprintId"]`. Return full blueprint document decoded from payload.
- [x] T019 [US2] Update `GET /api/system-register` status endpoint — return real register metadata: height from `IRegisterManager`, transaction count, blueprint count (filtered), createdAt from register entity. Map `status` field to `"initialized"` when register exists (regardless of blueprint count).

**Checkpoint**: Blueprints can be published and queried via the API, backed by real register transactions.

---

## Phase 5: User Story 3 — Administrator Views System Register in UI (Priority: P2)

**Goal**: Admin UI accurately reflects the real register's state

**Independent Test**: Navigate to `/admin/system-register` and `/registers` — verify system register appears with correct metadata.

### Implementation for User Story 3

- [x] T020 [US3] Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Models/Admin/SystemRegisterViewModels.cs` — add `Height` and `TransactionCount` properties to `SystemRegisterViewModel` with `[JsonPropertyName]` attributes matching the updated API response shape. (Note: `status` → `IsInitialized` mapping already fixed in earlier session work.)
- [x] T021 [US3] Update `src/Apps/Sorcha.UI/Sorcha.UI.Core/Components/Admin/SystemRegister/SystemRegisterDashboard.razor` — display `Height` and `TransactionCount` in the status card grid alongside existing Blueprint Count. Update the "Status" field to show "Active" when `IsInitialized` is true (based on register existence, not blueprint count).
- [x] T022 [US3] Verify the system register appears in the registers list page (`/registers`) — since it's now a real register, it should automatically appear via `RegisterService.GetRegistersAsync()`. No code change expected; this is a verification task. If it doesn't appear (e.g., filtered by tenant), update the query to include system tenant registers.

**Checkpoint**: Admin UI shows system register with real ledger metadata. System register visible in registers list.

---

## Phase 6: User Story 4 — Peer Nodes Replicate the System Register (Priority: P3)

**Goal**: System register replicates via peer network by default

**Independent Test**: With two peer nodes, publish a blueprint on one node and verify it appears on the other.

### Implementation for User Story 4

- [x] T023 [US4] Verify system register is advertised to peers — in `SystemRegisterBootstrapper`, after register creation, ensure `advertise: true` is set (already passed in `InitiateAsync` request). Verify `PeerServiceClient.AdvertiseRegisterAsync` is called for the system register. No code change expected if the orchestrator already handles this; verify and add if missing.
- [x] T024 [US4] Add system register to default replication list — if the peer service has a concept of "default registers to replicate", ensure the system register ID is included. Check `src/Services/Sorcha.Peer.Service/` for any default register configuration and add `SystemRegisterConstants.SystemRegisterId` if needed.

**Checkpoint**: System register replicates to peer nodes automatically.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup, documentation, and validation

- [x] T025 Remove old `sorcha_system_register_blueprints` MongoDB collection references — search codebase for any remaining references to the old collection name and remove them. Check docker-compose MongoDB init scripts if any.
- [x] T026 Update `src/Services/Sorcha.Register.Service/README.md` — document the system register as a real register, remove references to the separate MongoDB collection, update API documentation.
- [x] T027 Update `CLAUDE.md` — add system register bootstrap to the Quick Start section, remove `SORCHA_SEED_SYSTEM_REGISTER` env var from documentation.
- [x] T028 Update `docker-compose.yml` — remove `SORCHA_SEED_SYSTEM_REGISTER` environment variable from register-service configuration.
- [x] T029 Run full test suite (`dotnet test`) and fix any failures caused by removed types or changed DI registrations in register service tests.
- [ ] T030 Run quickstart validation — DEFERRED (requires fresh Docker deployment for end-to-end test) — start fresh Docker deployment, verify system register bootstrap, blueprint queries, and UI rendering per `specs/057-system-register-ledger/quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — delete old files first
- **Foundational (Phase 2)**: Depends on Phase 1 — refactor service after old types are removed
- **US1 Bootstrap (Phase 3)**: Depends on Phase 2 — bootstrapper uses refactored service
- **US2 Publish API (Phase 4)**: Depends on Phase 2 — endpoints use refactored service. Can run in parallel with US1.
- **US3 UI (Phase 5)**: Depends on Phase 2 — UI queries refactored API. Can run in parallel with US1/US2.
- **US4 Replication (Phase 6)**: Depends on US1 (bootstrap creates the register to replicate)
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Depends on Foundational only — core MVP
- **User Story 2 (P2)**: Depends on Foundational only — can parallel with US1
- **User Story 3 (P2)**: Depends on Foundational only — can parallel with US1/US2
- **User Story 4 (P3)**: Depends on US1 (register must exist to replicate)

### Within Each User Story

- Service refactoring before endpoint wiring
- Endpoint wiring before UI integration
- Core implementation before polish

### Parallel Opportunities

**Phase 1** (all parallel — independent file deletions):
- T001, T002 can run in parallel

**Phase 2** (sequential — same file):
- T005 → T006 → T007 → T008

**Phase 3-5** (parallel across stories after Phase 2):
- US1 (T009-T015), US2 (T016-T019), US3 (T020-T022) can all start after Phase 2

---

## Parallel Example: After Foundational Phase

```text
# These can all run in parallel (different files, different concerns):
Developer A: T009 [US1] Rewrite SystemRegisterBootstrapper
Developer B: T016 [US2] Add POST /publish endpoint
Developer C: T020 [US3] Update SystemRegisterViewModels
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Delete old files (T001-T004)
2. Complete Phase 2: Refactor SystemRegisterService (T005-T008)
3. Complete Phase 3: Bootstrap flow (T009-T015)
4. **STOP and VALIDATE**: `docker-compose down -v && docker-compose up -d` — verify system register appears with genesis + 2 blueprints
5. Deploy if ready

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Bootstrap) → Test independently → **MVP deployed**
3. Add US2 (Publish API) → Test independently → Blueprint management enabled
4. Add US3 (UI updates) → Test independently → Full admin visibility
5. Add US4 (Replication) → Test independently → Multi-node ready
6. Polish → Documentation, cleanup, final validation

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US1 is the clear MVP — system register bootstrap is the critical path
- US2 and US3 can be deferred without breaking US1
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
