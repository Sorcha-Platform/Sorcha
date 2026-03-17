# Tasks: Wallet Recovery

**Input**: Design documents from `/specs/060-wallet-recovery/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: New entities, enums, and shared recovery infrastructure

- [X] T001 [P] Add RecoveryPathType enum to src/Core/Sorcha.Wallet.Core/Domain/Enums.cs — values: Mnemonic, OrgManaged, Passkey
- [X] T002 [P] Create RecoveryKeyWrap entity in src/Core/Sorcha.Wallet.Core/Domain/Entities/RecoveryKeyWrap.cs — Id (Guid), WalletAddress (FK), RecoveryPath (RecoveryPathType), EncryptedRecoveryKey (string), RecipientKeyId (string), Algorithm (string), CreatedAt, RevokedAt
- [X] T003 [P] Create RecoveryAuditLog entity in src/Core/Sorcha.Wallet.Core/Domain/Entities/RecoveryAuditLog.cs — Id (Guid), UserId, TenantId, RecoveryPath, InitiatedBy, WalletsRecovered, DelegationsRevoked, DelegationsPreserved, IpAddress, Timestamp
- [X] T004 [P] Add EncryptedMasterKeyBlob (string?) and RecoveryEnabled (bool) properties to Wallet entity in src/Core/Sorcha.Wallet.Core/Domain/Entities/Wallet.cs
- [X] T005 Add RecoveryKeyWrap and RecoveryAuditLog DbSets to WalletDbContext in src/Core/Sorcha.Wallet.Core/Data/WalletDbContext.cs — configure RecoveryKeyWrap.WalletAddress FK, RecoveryAuditLog indexes on UserId+Timestamp

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Recovery key service and wallet creation modification — all user stories depend on these

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T006 Create EF Core migration for recovery entities — run `dotnet ef migrations add AddWalletRecovery` in src/Core/Sorcha.Wallet.Core/
- [X] T007 Create IRecoveryKeyService interface in src/Core/Sorcha.Wallet.Core/Services/Interfaces/IRecoveryKeyService.cs — methods: GenerateRecoveryKeyAsync() returns byte[], WrapRecoveryKeyAsync(recoveryKey, recipientPublicKey, algorithm) returns string, UnwrapRecoveryKeyAsync(encryptedRecoveryKey, recipientPrivateKey, algorithm) returns byte[], EncryptMasterKeyAsync(masterKey, recoveryKey) returns string, DecryptMasterKeyAsync(encryptedBlob, recoveryKey) returns byte[]
- [X] T008 Implement RecoveryKeyService in src/Core/Sorcha.Wallet.Core/Services/Implementation/RecoveryKeyService.cs — use Sorcha.Cryptography SymmetricCrypto (AES-256-GCM) for master key encryption and CryptoModule.EncryptAsync/DecryptAsync for asymmetric wrapping
- [X] T009 Register IRecoveryKeyService in Wallet Service DI in src/Services/Sorcha.Wallet.Service/Program.cs
- [X] T010 Modify WalletManager.CreateWalletAsync in src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs — after creating wallet: generate recovery key, encrypt master key to EncryptedMasterKeyBlob, create RecoveryKeyWrap for each available recovery path (passkey, org), set RecoveryEnabled=true
- [X] T011 [P] Add unit tests for RecoveryKeyService in tests/Sorcha.Wallet.Core.Tests/Services/RecoveryKeyServiceTests.cs — test: generate key returns 32 bytes, wrap/unwrap roundtrip succeeds, encrypt/decrypt master key roundtrip succeeds, invalid key fails gracefully

**Checkpoint**: Recovery key infrastructure ready — wallets created after this point have recovery enabled

---

## Phase 3: User Story 1 — Passkey-Bound Recovery (Priority: P1) MVP

**Goal**: Users recover all wallets by authenticating with their existing passkey on a new device

**Independent Test**: Create wallet with passkey recovery enabled → simulate passkey auth on new device → verify all wallets restored and signing works

### Implementation for User Story 1

- [X] T012 [P] [US1] Create IPasskeyServiceClient interface in src/Common/Sorcha.ServiceClients/Passkey/IPasskeyServiceClient.cs — method: GetRecoveryPublicKeyAsync(userId) returns PasskeyRecoveryKeyInfo (credentialId, publicKeyCose, algorithm)
- [X] T013 [P] [US1] Implement PasskeyServiceClient in src/Common/Sorcha.ServiceClients/Passkey/PasskeyServiceClient.cs — HTTP client calling Tenant Service GET /api/users/{userId}/passkeys/recovery-key
- [X] T014 [US1] Register PasskeyServiceClient in ServiceCollectionExtensions in src/Common/Sorcha.ServiceClients/Extensions/ServiceCollectionExtensions.cs
- [X] T015 [US1] Add GET /api/users/{userId}/passkeys/recovery-key endpoint in src/Services/Sorcha.Tenant.Service/Endpoints/PasskeyEndpoints.cs (or extend existing) — requires service-to-service auth, returns primary passkey's PublicKeyCose and CredentialId
- [X] T016 [US1] Wire passkey public key lookup into WalletManager.CreateWalletAsync — at creation, if user has passkey, wrap recovery key to passkey public key and save RecoveryKeyWrap with RecoveryPath=Passkey
- [X] T017 [P] [US1] Create RecoverPasskeyRequest model in src/Services/Sorcha.Wallet.Service/Models/RecoverPasskeyRequest.cs — passkeyCredentialId (string), challengeResponse (string)
- [X] T018 [P] [US1] Create RecoveryResult model in src/Services/Sorcha.Wallet.Service/Models/RecoveryResult.cs — walletsRecovered (int), walletAddresses (string[]), delegationsRevoked (int), delegationsPendingReview (DelegationReviewItem[])
- [X] T019 [US1] Create IPasskeyRecoveryService interface in src/Services/Sorcha.Wallet.Service/Services/Interfaces/IPasskeyRecoveryService.cs — method: RecoverAsync(userId, passkeyCredentialId, challengeResponse) returns RecoveryResult
- [X] T020 [US1] Implement PasskeyRecoveryService in src/Services/Sorcha.Wallet.Service/Services/Implementation/PasskeyRecoveryService.cs — verify passkey ownership, find RecoveryKeyWraps by credentialId, unwrap recovery key, decrypt master key blob for all user wallets, re-encrypt with new key ID, revoke delegations, log audit entry
- [X] T021 [US1] Add POST /api/v1/wallets/recover/passkey endpoint in src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs — requires auth, calls PasskeyRecoveryService.RecoverAsync, returns RecoveryResult
- [X] T022 [US1] Add GET /api/v1/wallets/recovery-status endpoint in src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs — returns RecoveryStatusResponse (mnemonicRecoveryAvailable, passkeyRecoveryAvailable, orgRecoveryAvailable, wallet counts)
- [X] T023 [P] [US1] Add unit tests for PasskeyRecoveryService in tests/Sorcha.Wallet.Service.Tests/Services/PasskeyRecoveryServiceTests.cs — test: successful recovery restores wallets, invalid passkey returns 401, no wraps returns 404, delegation revocation works, audit log created

**Checkpoint**: Passkey recovery is functional — users can recover wallets via their existing passkey

---

## Phase 4: User Story 2 — Organization-Managed Recovery (Priority: P1)

**Goal**: Org admins can recover wallets for organization members with MFA verification

**Independent Test**: Configure org recovery key → create wallet for org member → admin initiates recovery → verify member's wallets restored

### Implementation for User Story 2

- [X] T024 [P] [US2] Create OrgRecoveryConfig entity in src/Services/Sorcha.Tenant.Service/Models/OrgRecoveryConfig.cs — Id (Guid), OrganizationId (Guid, unique), RecoveryPublicKey (string), RecoveryKeyId (string), CreatedBy, CreatedAt, RotatedAt
- [X] T025 [US2] Add OrgRecoveryConfig DbSet to TenantDbContext in src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs — configure unique index on OrganizationId
- [X] T026 [US2] Create EF Core migration for OrgRecoveryConfig — run `dotnet ef migrations add AddOrgRecoveryConfig` in src/Services/Sorcha.Tenant.Service/
- [X] T027 [US2] Add POST /api/organizations/{orgId}/recovery-config endpoint in src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs — RequireAdministrator, accepts recoveryPublicKey, returns recoveryKeyId
- [X] T028 [US2] Add GET /api/organizations/{orgId}/recovery-config endpoint in src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs — RequireOrganizationMember, returns config status
- [X] T029 [US2] Wire org recovery public key lookup into WalletManager.CreateWalletAsync — at creation, if user's org has recovery config, wrap recovery key to org public key and save RecoveryKeyWrap with RecoveryPath=OrgManaged
- [X] T030 [P] [US2] Create RecoverOrgRequest model in src/Services/Sorcha.Wallet.Service/Models/RecoverOrgRequest.cs — userId (string), orgRecoveryKeySignature (string), skipDelegationRevocation (bool)
- [X] T031 [US2] Create IOrgRecoveryService interface in src/Services/Sorcha.Wallet.Service/Services/Interfaces/IOrgRecoveryService.cs — method: RecoverAsync(adminUserId, targetUserId, signature, skipDelegationRevocation) returns RecoveryResult
- [X] T032 [US2] Implement OrgRecoveryService in src/Services/Sorcha.Wallet.Service/Services/Implementation/OrgRecoveryService.cs — verify admin is org admin for target user, verify org recovery key signature, find RecoveryKeyWraps for OrgManaged path, unwrap and restore all wallets, optionally skip delegation revocation, log audit entry
- [X] T033 [US2] Add POST /api/v1/wallets/recover/org endpoint in src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs — requires admin auth, calls OrgRecoveryService.RecoverAsync
- [X] T034 [US2] Add API Gateway routes for org recovery config endpoints in src/Services/Sorcha.ApiGateway/appsettings.json — route /api/organizations/{orgId}/recovery-config to tenant-cluster with RequireAuthenticated
- [X] T035 [P] [US2] Add unit tests for OrgRecoveryService in tests/Sorcha.Wallet.Service.Tests/Services/OrgRecoveryServiceTests.cs — test: admin can recover member wallets, non-admin rejected, invalid signature rejected, skip delegation revocation works, audit log created

**Checkpoint**: Org admins can recover member wallets — both recovery paths functional

---

## Phase 5: User Story 3 — Delegation Revocation & Selective Preservation (Priority: P1)

**Goal**: Users review and selectively preserve delegations after recovery

**Independent Test**: Recover wallet with 5 delegations → verify all revoked by default → preserve 2 → verify 3 revoked, 2 active

### Implementation for User Story 3

- [X] T036 [P] [US3] Create DelegationReviewItem model in src/Services/Sorcha.Wallet.Service/Models/DelegationReviewItem.cs — delegationId (Guid), walletAddress, subject, accessRight, grantedAt, reason
- [X] T037 [P] [US3] Create PreserveDelegationsRequest model in src/Services/Sorcha.Wallet.Service/Models/PreserveDelegationsRequest.cs — walletAddress (string), delegationIds (Guid[])
- [X] T038 [US3] Add delegation revocation logic to PasskeyRecoveryService and OrgRecoveryService — after restoring wallets, call DelegationService.RevokeAccessAsync for all active WalletAccess grants, collect revoked grants as DelegationReviewItems in RecoveryResult.delegationsPendingReview
- [X] T039 [US3] Add POST /api/v1/wallets/recover/delegations/preserve endpoint in src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs — requires auth, re-grants specified delegations via DelegationService.GrantAccessAsync, updates RecoveryAuditLog.DelegationsPreserved count
- [X] T040 [P] [US3] Add unit tests for delegation revocation and preservation in tests/Sorcha.Wallet.Service.Tests/Services/DelegationRecoveryTests.cs — test: all delegations revoked by default, selective preservation re-grants, audit log counts accurate, org admin skip revocation works

**Checkpoint**: Full recovery flow including delegation management

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, OpenAPI, and final validation

- [X] T041 [P] Add OpenAPI documentation (.WithSummary/.WithDescription) to all new recovery endpoints in src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs
- [X] T042 [P] Add OpenAPI documentation to org recovery config endpoints in src/Services/Sorcha.Tenant.Service/Endpoints/OrganizationEndpoints.cs
- [X] T043 [P] Update Wallet Service README at src/Services/Sorcha.Wallet.Service/README.md — document recovery endpoints, RecoveryKeyWrap model, recovery flow
- [X] T044 [P] Update Tenant Service README at src/Services/Sorcha.Tenant.Service/README.md — document OrgRecoveryConfig entity and endpoints
- [X] T045 Update docs/reference/platform-service-analysis.md with wallet recovery capabilities
- [X] T046 Update .specify/MASTER-TASKS.md with Feature 060 completion status
- [X] T047 Add API Gateway routes for wallet recovery endpoints in src/Services/Sorcha.ApiGateway/appsettings.json — route /api/v1/wallets/recover/* to wallet-cluster with RequireAuthenticated

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (entities must exist for DbContext/migration)
- **US1 (Phase 3)**: Depends on Phase 2 (RecoveryKeyService must exist)
- **US2 (Phase 4)**: Depends on Phase 2 (RecoveryKeyService must exist), can run in parallel with US1
- **US3 (Phase 5)**: Depends on US1 and US2 (recovery services must exist to add delegation logic)
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (Passkey Recovery)**: Foundational only — independent
- **US2 (Org Recovery)**: Foundational only — can run in parallel with US1
- **US3 (Delegation Revocation)**: Depends on US1 + US2 — adds logic to both recovery services

### Within Each User Story

- Models before services
- Service client before service implementation
- Service implementation before endpoints
- Endpoints before tests (unless TDD)

### Parallel Opportunities

- T001, T002, T003, T004 can all run in parallel (different files)
- T012, T013, T017, T018 can run in parallel (different files)
- T024, T030 can run in parallel (different files)
- T036, T037 can run in parallel (different files)
- US1 and US2 can run in parallel after Phase 2
- T023 and T035 can run in parallel (different test files)
- All Phase 6 documentation tasks marked [P] can run in parallel

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1: Setup (T001-T005)
2. Complete Phase 2: Foundational (T006-T011)
3. Complete Phase 3: US1 — Passkey Recovery (T012-T023)
4. **STOP and VALIDATE**: Create wallet → recover via passkey → verify signing works
5. Deploy/demo — users can recover wallets with their passkey

### Incremental Delivery

1. Setup + Foundational → recovery key infrastructure ready
2. Add US1 → passkey recovery working → demo
3. Add US2 → org admin recovery working → demo
4. Add US3 → delegation management working → demo
5. Polish → documentation complete

---

## Notes

- No production instances exist — only new wallets need recovery support (no retroactive migration)
- Existing mnemonic recovery (Path 1) is unchanged — it continues to work as-is
- Social recovery (Path 2) is deferred to a later phase
- The PasskeyServiceClient is a new addition to Sorcha.ServiceClients following existing patterns
- Recovery key wrapping uses the same CryptoModule.EncryptAsync already used for payload encryption
- Org recovery key rotation (PUT endpoint) deferred to Polish or future iteration
