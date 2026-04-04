# Tasks: Wallet Key Derivation & UI Transaction Lifecycle

**Input**: Design documents from `/specs/083-wallet-key-derivation/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — constitution requires >85% coverage on new code.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New entity definitions, enums, interfaces, and database migration

- [ ] T001 [P] Create `KeyUsage` enum (Identity=0, VCIssuance=1, Governance=2, Communications=3, ServiceAuth=4) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/KeyUsage.cs`
- [ ] T002 [P] Create `CustodyMode` enum (Custodial=0, CoSigned=1, SelfCustody=2) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/CustodyMode.cs`
- [ ] T003 [P] Create `OrgMasterKeyStatus` enum (Active=0, Rotated=1, Revoked=2) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/OrgMasterKeyStatus.cs`
- [ ] T004 [P] Create `DerivedKeyStatus` enum (Active=0, Rotated=1, Revoked=2) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/DerivedKeyStatus.cs`
- [ ] T005 [P] Create `ThresholdKeyGroupStatus` enum (Pending=0, Active=1, Revoked=2) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/ThresholdKeyGroupStatus.cs`
- [ ] T006 [P] Create `SigningSessionState` enum (Initializing=0, Round1=1, Round2=2, Complete=3, Failed=4) in `src/Core/Sorcha.Wallet.Core/Domain/Enums/SigningSessionState.cs`
- [ ] T007 [P] Create `OrgMasterKey` entity per data-model.md in `src/Core/Sorcha.Wallet.Core/Domain/Entities/OrgMasterKey.cs`
- [ ] T008 [P] Create `DerivedKeyRecord` entity per data-model.md in `src/Core/Sorcha.Wallet.Core/Domain/Entities/DerivedKeyRecord.cs`
- [ ] T009 [P] Create `ThresholdKeyGroup` entity (schema only) per data-model.md in `src/Core/Sorcha.Wallet.Core/Domain/Entities/ThresholdKeyGroup.cs`
- [ ] T010 [P] Create `SigningKeyShare` entity (schema only) per data-model.md in `src/Core/Sorcha.Wallet.Core/Domain/Entities/SigningKeyShare.cs`
- [ ] T011 [P] Create `SigningSession` entity (schema only) per data-model.md in `src/Core/Sorcha.Wallet.Core/Domain/Entities/SigningSession.cs`
- [ ] T012 Add `DerivedKeyRecordId` (Guid? FK) and `CustodyMode` (enum, default Custodial) to existing `Wallet` entity in `src/Core/Sorcha.Wallet.Core/Domain/Entities/Wallet.cs`
- [ ] T013 [P] Create `IOrgKeyProtectionProvider` interface (`EncryptSeedAsync`, `DecryptSeedAsync`, `ProviderName`) in `src/Core/Sorcha.Wallet.Core/Services/Interfaces/IOrgKeyProtectionProvider.cs`
- [ ] T014 [P] Create `IOrgKeyDerivationService` interface (`ProvisionMasterKeyAsync`, `DeriveUserKeyAsync`, `RotateKeyAsync`, `RevokeKeyAsync`) in `src/Core/Sorcha.Wallet.Core/Services/Interfaces/IOrgKeyDerivationService.cs`
- [ ] T015 Register all new entities in Wallet Service EF Core DbContext with entity configurations, indexes (unique on OrgMasterKey.OrganizationId, composite unique on DerivedKeyRecord path tuple, composite unique on SigningKeyShare group+index), and FK relationships in `src/Services/Sorcha.Wallet.Service/Data/`
- [ ] T016 Generate single squashed EF Core migration for all new entities and Wallet modifications — name: `AddOrgKeyDerivationAndThresholdSchema` in `src/Services/Sorcha.Wallet.Service/Data/Migrations/`

**Checkpoint**: All entities, enums, interfaces, and migration in place. Database schema ready.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core services that multiple user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T017 Implement `SoftwareKeyProtectionProvider` (AES-256-GCM encryption/decryption of seed bytes, key from `IConfiguration["OrgKeyProtection:EncryptionKey"]`) in `src/Services/Sorcha.Wallet.Service/Services/Implementation/SoftwareKeyProtectionProvider.cs`
- [ ] T018 Implement derivation path builder — `DerivationPathBuilder.Build(orgId, deptId, userId, usage, index)` producing `m/0x534F52'/orgHash'/deptId'/userHash'/usage/index` with GUID-to-uint31 mapping via SHA-256 in `src/Services/Sorcha.Wallet.Service/Services/Implementation/DerivationPathBuilder.cs`
- [ ] T019 [P] Unit tests for `DerivationPathBuilder`: verify path format, GUID determinism, hardened levels, collision avoidance with BIP44 paths in `tests/Sorcha.Wallet.Core.Tests/DerivationPathBuilderTests.cs`
- [ ] T020 [P] Unit tests for `SoftwareKeyProtectionProvider`: encrypt/decrypt round-trip, different keys produce different ciphertext, null/empty input handling in `tests/Sorcha.Wallet.Service.Tests/SoftwareKeyProtectionProviderTests.cs`
- [ ] T021 Register `IOrgKeyProtectionProvider` → `SoftwareKeyProtectionProvider` and `IOrgKeyDerivationService` → `OrgKeyDerivationService` in Wallet Service DI in `src/Services/Sorcha.Wallet.Service/Program.cs`

**Checkpoint**: Foundation ready — key protection and derivation path infrastructure tested and registered.

---

## Phase 3: User Story 1 — Organisation Admin Provisions Key Hierarchy (Priority: P1) 🎯 MVP

**Goal**: Admin provisions org master key, identity wallets auto-created for all org members, new members get wallets automatically.

**Independent Test**: Provision master key → verify mnemonic returned once → confirm identity wallets created for existing members → add user → verify auto-derivation.

### Tests for User Story 1

- [ ] T022 [P] [US1] Unit tests for `OrgKeyDerivationService.ProvisionMasterKeyAsync`: mnemonic generation, seed encryption, master public key stored, duplicate provision rejected, existing members get identity keys in `tests/Sorcha.Wallet.Service.Tests/OrgKeyDerivationServiceTests.cs`
- [ ] T023 [P] [US1] Unit tests for `OrgKeyDerivationService.DeriveUserKeyAsync`: deterministic derivation, idempotent return, correct path construction, wallet creation in `tests/Sorcha.Wallet.Service.Tests/OrgKeyDerivationServiceTests.cs`
- [ ] T024 [P] [US1] Integration tests for `POST /api/wallets/org/{orgId}/master-key` and `POST /api/wallets/org/{orgId}/derive-key`: auth enforcement, 201/409 responses, mnemonic in response in `tests/Sorcha.Wallet.Service.IntegrationTests/OrgKeyEndpointsTests.cs`

### Implementation for User Story 1

- [ ] T025 [US1] Implement `OrgKeyDerivationService.ProvisionMasterKeyAsync` — generate BIP39 mnemonic (24 words), derive seed, encrypt via `IOrgKeyProtectionProvider`, store `OrgMasterKey`, derive identity keys for all existing org members (query Tenant Service for member list), return mnemonic once in `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgKeyDerivationService.cs`
- [ ] T026 [US1] Implement `OrgKeyDerivationService.DeriveUserKeyAsync` — construct derivation path via `DerivationPathBuilder`, decrypt master seed, derive child key via NBitcoin `ExtKey.Derive()`, create `Wallet` + `DerivedKeyRecord`, return existing if path already derived (idempotent) in `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgKeyDerivationService.cs`
- [ ] T027 [US1] Create `OrgKeyEndpoints` with `POST /api/wallets/org/{orgId}/master-key` (RequireAdministrator) and `POST /api/wallets/org/{orgId}/derive-key` (RequireService or RequireAdministrator) per OpenAPI contract in `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs`
- [ ] T028 [US1] Implement auto-derivation hook — subscribe to Tenant Service "user added to organisation" event (SignalR or internal API), call `DeriveUserKeyAsync(orgId, userId, 0, KeyUsage.Identity)` when fired, log warning and skip if org has no master key in `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgKeyDerivationService.cs`

**Checkpoint**: Org master key provisioning works end-to-end. Identity wallets auto-created. Core MVP complete.

---

## Phase 4: User Story 2 — Wallet User Views Transaction Lifecycle (Priority: P1)

**Goal**: Transaction list shows tick indicators (grey/blue/✓✓), clicking opens detail panel with timeline and receipt proof, updates in real time via SignalR.

**Independent Test**: Submit transaction → see grey tick → wait for seal → see blue tick → wait for receipt → see ✓✓ → click row → verify timeline, details, receipt proof displayed.

### Tests for User Story 2

- [ ] T029 [P] [US2] E2E test for transaction tick rendering: verify grey/blue/double-blue icons appear based on transaction state in `tests/Sorcha.UI.E2E.Tests/TransactionTickTests.cs`
- [ ] T030 [P] [US2] E2E test for transaction detail drawer: click row → verify timeline, details grid, receipt proof section in `tests/Sorcha.UI.E2E.Tests/TransactionDetailDrawerTests.cs`

### Implementation for User Story 2

- [ ] T031 [P] [US2] Create `TransactionTickIcon.razor` component — renders grey ✓ (Pending), blue ✓ (Sealed), blue ✓✓ (Receipted) based on `TransactionTickStatus` enum input in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Wallet/TransactionTickIcon.razor`
- [ ] T032 [P] [US2] Create `ReceiptProofCard.razor` component — displays receipt ID, Merkle root, validator address, signature; "Verify Receipt" button calls existing `/registers/{registerId}/receipts/verify` endpoint; "Download Bundle" button calls existing `/registers/{registerId}/transactions/{txId}/verification-bundle` endpoint in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Wallet/ReceiptProofCard.razor`
- [ ] T033 [US2] Create `TransactionDetailDrawer.razor` — MudDrawer slide-out with three sections: (1) vertical lifecycle timeline with timestamps and relative timing, (2) details grid (register, direction, counterparty, sequence, docket, block height), (3) `ReceiptProofCard` for receipted transactions in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Wallet/TransactionDetailDrawer.razor`
- [ ] T034 [US2] Modify `WalletDetail.razor` Transactions tab — add Status column using `TransactionTickIcon`, add row click handler to open `TransactionDetailDrawer`, add legend row at bottom of table in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor`
- [ ] T035 [US2] Wire SignalR subscription in `WalletDetail.razor` — subscribe to wallet-scoped group for `docket:confirmed` and `receipt:generated` events, update transaction tick status in-place, update detail drawer if open for affected transaction in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Wallets/WalletDetail.razor`

**Checkpoint**: Transaction ticks render correctly, detail panel works, real-time updates functional. Both P1 stories complete.

---

## Phase 5: User Story 3 — Admin Derives Purpose-Specific Keys (Priority: P2)

**Goal**: Admin derives additional keys (VCIssuance, Governance, etc.) for users. Idempotent. Department-scoped.

**Independent Test**: Derive VC issuance key for user → verify new wallet created → re-derive → verify same wallet returned → derive with department ID → verify path includes department.

### Tests for User Story 3

- [ ] T036 [P] [US3] Unit tests for purpose-specific derivation: each KeyUsage value produces unique path, department ID included in path, idempotent return for duplicate requests in `tests/Sorcha.Wallet.Service.Tests/OrgKeyDerivationServiceTests.cs`

### Implementation for User Story 3

- [ ] T037 [US3] No new service code needed — `DeriveUserKeyAsync` already supports all KeyUsage values and departmentId. Verify the derive-key endpoint accepts all 5 usage types and non-zero departmentId values. Add request validation (KeyUsage must be valid enum, departmentId >= 0) in `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs`

**Checkpoint**: Purpose-specific key derivation verified across all 5 key usage types.

---

## Phase 6: User Story 4 — Admin Rotates a Key (Priority: P2)

**Goal**: Admin rotates a key — new key at next index, old key marked Rotated (decrypt only, no signing).

**Independent Test**: Derive key at index 0 → rotate → verify old key status is Rotated → verify new key at index 1 is Active → attempt sign with old key → rejected → attempt decrypt with old key → succeeds.

### Tests for User Story 4

- [ ] T038 [P] [US4] Unit tests for `RotateKeyAsync`: new key at index+1, old key status Rotated, old wallet rejects signing, old wallet allows decryption in `tests/Sorcha.Wallet.Service.Tests/OrgKeyDerivationServiceTests.cs`
- [ ] T039 [P] [US4] Integration test for `POST /api/wallets/org/{orgId}/keys/{id}/rotate`: auth enforcement, 200 response, verify old/new key states in `tests/Sorcha.Wallet.Service.IntegrationTests/OrgKeyEndpointsTests.cs`

### Implementation for User Story 4

- [ ] T040 [US4] Implement `OrgKeyDerivationService.RotateKeyAsync` — find current DerivedKeyRecord, verify status is Active, derive new key at `keyIndex + 1`, mark old record as Rotated, set old wallet status to prevent signing (but allow decrypt), return new DerivedKeyResult in `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgKeyDerivationService.cs`
- [ ] T041 [US4] Add `POST /api/wallets/org/{orgId}/keys/{derivedKeyId}/rotate` endpoint (RequireAdministrator) to `OrgKeyEndpoints` per OpenAPI contract in `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs`
- [ ] T042 [US4] Enforce signing block on rotated wallets — modify signing endpoint to check `DerivedKeyRecord.Status` and reject if Rotated (allow decrypt) in `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs`

**Checkpoint**: Key rotation works end-to-end. Old keys decrypt-only. Both P2 stories complete.

---

## Phase 7: User Story 5 — Admin Revokes a Key (Priority: P3)

**Goal**: Admin permanently revokes a key. Wallet locked. DID revocation published for identity keys.

**Independent Test**: Revoke key → verify status Revoked → verify wallet locked → attempt sign → rejected → attempt decrypt → rejected → verify DID event published (identity key only).

### Tests for User Story 5

- [ ] T043 [P] [US5] Unit tests for `RevokeKeyAsync`: status set to Revoked, wallet locked, revokedAt set, DID revocation event published for identity keys only in `tests/Sorcha.Wallet.Service.Tests/OrgKeyDerivationServiceTests.cs`
- [ ] T044 [P] [US5] Integration test for `DELETE /api/wallets/org/{orgId}/keys/{id}`: auth enforcement, 200 response, wallet locked confirmed in `tests/Sorcha.Wallet.Service.IntegrationTests/OrgKeyEndpointsTests.cs`

### Implementation for User Story 5

- [ ] T045 [US5] Implement `OrgKeyDerivationService.RevokeKeyAsync` — find DerivedKeyRecord, verify not already revoked, set status to Revoked, set revokedAt, lock associated wallet (set `WalletStatus.Locked`), if keyUsage == Identity publish DID revocation event in `src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgKeyDerivationService.cs`
- [ ] T046 [US5] Add `DELETE /api/wallets/org/{orgId}/keys/{derivedKeyId}` endpoint (RequireAdministrator) to `OrgKeyEndpoints` per OpenAPI contract in `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs`
- [ ] T047 [US5] Enforce full block on revoked wallets — modify signing and decryption endpoints to check `DerivedKeyRecord.Status` and reject both operations if Revoked in `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs`

**Checkpoint**: Key revocation works. Wallet fully locked on revoke. DID event fires for identity keys.

---

## Phase 8: User Story 6 — Schema Readiness for Threshold Signing (Priority: P3)

**Goal**: Threshold signing tables exist with correct constraints. No service code references them.

**Independent Test**: Run migration → inspect DB for ThresholdKeyGroup, SigningKeyShare, SigningSession tables → verify constraints and indexes → grep codebase for references → confirm zero service/endpoint references.

### Tests for User Story 6

- [ ] T048 [US6] Verification test: confirm ThresholdKeyGroup, SigningKeyShare, SigningSession tables exist after migration with correct columns, FKs, and indexes. Confirm no service code or endpoints reference these tables (grep for class names in Services/ and Endpoints/ directories) in `tests/Sorcha.Wallet.Service.IntegrationTests/ThresholdSchemaVerificationTests.cs`

### Implementation for User Story 6

- [ ] T049 [US6] No additional implementation needed — entities created in T009-T011, migration in T016, DbContext registration in T015. This phase is verification only.

**Checkpoint**: Threshold signing schema confirmed. All P3 stories complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Integration, documentation, and cross-cutting improvements

- [ ] T050 [P] Add YARP gateway route for `/api/wallets/org/**` → Wallet Service in `src/Services/Sorcha.ApiGateway/appsettings.json`
- [ ] T051 [P] Add org key derivation methods to `IWalletServiceClient` interface and `WalletServiceClient` implementation (ProvisionMasterKeyAsync, DeriveKeyAsync, RotateKeyAsync, RevokeKeyAsync) in `src/Common/Sorcha.ServiceClients/Wallet/IWalletServiceClient.cs` and `src/Common/Sorcha.ServiceClients/Wallet/WalletServiceClient.cs`
- [ ] T052 [P] Add Scalar OpenAPI documentation (WithSummary, WithDescription) to all 4 org key endpoints in `src/Services/Sorcha.Wallet.Service/Endpoints/OrgKeyEndpoints.cs`
- [ ] T053 [P] Update CLAUDE.md with Org Key Derivation API table (4 endpoints) and key models
- [ ] T054 [P] Update Wallet Service README with org key derivation feature documentation
- [ ] T055 [P] Update `docs/reference/API-DOCUMENTATION.md` with org key endpoints
- [ ] T056 [P] Update `docs/reference/development-status.md` with Feature 083 completion
- [ ] T057 [P] Update `.specify/tasks/deferred-tasks.md` to mark WALLET-R1 through R5 and R9 as completed, note threshold schema (R6-R7) as schema-ready
- [ ] T058 Add `OrgKeyProtection:EncryptionKey` configuration to docker-compose.yml environment for Wallet Service in `docker-compose.yml`
- [ ] T059 Run `quickstart.md` validation — execute all API examples from quickstart.md against running Docker stack and verify expected responses

**Checkpoint**: Feature complete. Documentation synced. Gateway routing configured. Ready for PR.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — core MVP
- **US2 (Phase 4)**: Depends on Phase 1 only (uses existing backend, no org key dependency) — CAN RUN IN PARALLEL WITH Phase 3
- **US3 (Phase 5)**: Depends on Phase 3 (uses derive endpoint from US1)
- **US4 (Phase 6)**: Depends on Phase 3 (uses derived keys from US1)
- **US5 (Phase 7)**: Depends on Phase 3 (uses derived keys from US1)
- **US6 (Phase 8)**: Depends on Phase 1 only (schema verification)
- **Polish (Phase 9)**: Depends on Phases 3-8 completion

### User Story Independence

- **US1 + US2 can run in parallel** — US1 is backend (Wallet Service), US2 is frontend (Sorcha.UI). No shared code.
- **US3, US4, US5 are sequential** — each builds on US1's derive/provision capabilities
- **US6 is independent** — can run after Phase 1

### Within Each User Story

- Tests written first (fail before implementation)
- Entities/models before services
- Services before endpoints
- Core implementation before integration

### Parallel Opportunities

- Phase 1: All 16 tasks are [P] parallelizable (separate files)
- Phase 2: T019 + T020 parallel (test files), T017 + T018 parallel (implementation files)
- Phase 3 + Phase 4: Entire phases can run in parallel (backend vs frontend)
- Phase 8: Can run as soon as Phase 1 completes
- Phase 9: All 10 tasks are [P] parallelizable (separate files)

---

## Parallel Example: Phase 1 (Setup)

```
# All entity and enum files can be created simultaneously:
T001-T006: All enum files (6 files, zero dependencies)
T007-T011: All entity files (5 files, zero dependencies)
T013-T014: Interface files (2 files, zero dependencies)
# Then sequentially:
T012: Modify existing Wallet entity
T015: DbContext registration (depends on all entities)
T016: Migration (depends on T015)
```

## Parallel Example: Phase 3 + Phase 4 (US1 + US2)

```
# Backend and frontend can run simultaneously:
Developer A (backend): T022-T028 (Org key provisioning + derivation)
Developer B (frontend): T029-T035 (Transaction ticks + detail panel)
```

---

## Implementation Strategy

### MVP First (User Stories 1 + 2)

1. Complete Phase 1: Setup (entities, migration)
2. Complete Phase 2: Foundational (protection provider, path builder)
3. Complete Phase 3: US1 — Org key provisioning (backend MVP)
4. Complete Phase 4: US2 — Transaction ticks (frontend MVP)
5. **STOP and VALIDATE**: Both P1 stories independently functional
6. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Schema and infrastructure ready
2. US1 (P1) → Org keys work → Deploy/Demo (Backend MVP)
3. US2 (P1) → Transaction ticks work → Deploy/Demo (Frontend MVP)
4. US3 + US4 (P2) → Purpose keys + rotation → Deploy/Demo
5. US5 + US6 (P3) → Revocation + threshold schema → Deploy/Demo
6. Polish → Docs, gateway, service clients → Final PR

---

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 59 |
| Phase 1 (Setup) | 16 tasks |
| Phase 2 (Foundational) | 5 tasks |
| US1 — Org Key Provisioning (P1) | 7 tasks |
| US2 — Transaction Ticks (P1) | 7 tasks |
| US3 — Purpose-Specific Keys (P2) | 2 tasks |
| US4 — Key Rotation (P2) | 5 tasks |
| US5 — Key Revocation (P3) | 5 tasks |
| US6 — Threshold Schema (P3) | 2 tasks |
| Phase 9 (Polish) | 10 tasks |
| Parallel opportunities | Phase 1 (all), Phase 3+4 (full parallel), Phase 9 (all) |
| MVP scope | US1 + US2 (Phases 1-4) |
