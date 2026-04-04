# Tasks: Cloud KMS Key Management

**Input**: Design documents from `/specs/082-cloud-kms/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — project requires >85% coverage per constitution.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: Create new projects, define interfaces, add EF migration

- [ ] T001 Create `IKeyProtectionProvider` interface in src/Core/Sorcha.Wallet.Core/Encryption/Interfaces/IKeyProtectionProvider.cs with methods: CreateKeyAsync, WrapKeyAsync, UnwrapKeyAsync, KeyExistsAsync
- [ ] T002 [P] Create `ISigningProvider` interface in src/Core/Sorcha.Wallet.Core/Encryption/Interfaces/ISigningProvider.cs with methods: CreateSigningKeyAsync, SignAsync, VerifyAsync, GetPublicKeyAsync
- [ ] T003 [P] Create `KmsKeyInfo` model in src/Core/Sorcha.Wallet.Core/Encryption/Models/KmsKeyInfo.cs with KeyId, PublicKey, Algorithm, CreatedAt
- [ ] T004 [P] Create `SigningMode` enum in src/Core/Sorcha.Wallet.Core/Domain/Enums/SigningMode.cs with values Local and KmsResident
- [ ] T005 Add `SigningMode` and `KmsKeyId` columns to Wallet entity in src/Core/Sorcha.Wallet.Core/Domain/Entities/Wallet.cs
- [ ] T006 Create EF Core migration for SigningMode and KmsKeyId columns (default SigningMode=0 for existing rows)
- [ ] T007 Create `Sorcha.Wallet.Providers.Azure` project under src/Providers/ with references to Azure.Security.KeyVault.Keys, Azure.Identity, and Sorcha.Wallet.Core
- [ ] T008 [P] Create `Sorcha.Wallet.Providers.Azure.Tests` project under tests/ with references to xUnit, FluentAssertions, Moq

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Refactor existing encryption provider to new interface, create config and policy infrastructure

**CRITICAL**: No user story work can begin until this phase is complete

- [ ] T009 Create `WalletKeyManagementOptions` config model in src/Core/Sorcha.Wallet.Core/Encryption/Configuration/WalletKeyManagementOptions.cs with DefaultSigningMode, KmsResidentPaths, AllowSigningModeOverride
- [ ] T010 [P] Create `SigningModePolicy` in src/Core/Sorcha.Wallet.Core/Encryption/Configuration/SigningModePolicy.cs that resolves signing mode from API override, path match, then default
- [ ] T011 Refactor `EncryptionProviderBase` into `LocalKeyProtectionProvider` implementing `IKeyProtectionProvider` in src/Core/Sorcha.Wallet.Core/Encryption/Providers/LocalKeyProtectionProvider.cs — extract DEK wrap/unwrap from the existing AES-256-GCM encrypt/decrypt, delegate AES operations to KeyManagementService
- [ ] T012 Move AES-256-GCM encrypt/decrypt logic from EncryptionProviderBase into KeyManagementService in src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs — use IKeyProtectionProvider for DEK wrap/unwrap only
- [ ] T013 Add DEK in-memory cache with configurable TTL and grace period to KeyManagementService (migrate cache from EncryptionProviderBase)
- [ ] T014 Update DI registration in src/Services/Sorcha.Wallet.Service/Extensions/WalletServiceExtensions.cs — replace `IEncryptionProvider` singleton with `IKeyProtectionProvider` and optional `ISigningProvider`, add WalletKeyManagementOptions binding
- [ ] T015 Update `WindowsDpapiEncryptionProvider` to implement `IKeyProtectionProvider` as `WindowsDpapiKeyProtectionProvider` in src/Core/Sorcha.Wallet.Core/Encryption/Providers/WindowsDpapiKeyProtectionProvider.cs
- [ ] T016 [P] Update `LinuxSecretServiceEncryptionProvider` to implement `IKeyProtectionProvider` as `LinuxSecretServiceKeyProtectionProvider` in src/Core/Sorcha.Wallet.Core/Encryption/Providers/LinuxSecretServiceKeyProtectionProvider.cs
- [ ] T017 Refactor existing tests in tests/Sorcha.Wallet.Core.Tests/ to use IKeyProtectionProvider instead of IEncryptionProvider — ensure all existing tests pass
- [ ] T018 [P] Write unit tests for SigningModePolicy in tests/Sorcha.Wallet.Core.Tests/Encryption/SigningModePolicyTests.cs — test path matching, default fallback, override enabled/disabled
- [ ] T019 Remove old `IEncryptionProvider` interface and `EncryptionProviderBase` after all consumers migrated. Update `LocalEncryptionProvider` to implement `IKeyProtectionProvider` directly.
- [ ] T020 Run full test suite — verify zero regressions from interface refactor

**Checkpoint**: Foundation ready — existing functionality preserved with new interface. User story implementation can begin.

---

## Phase 3: User Story 1 — Envelope Encryption with Cloud KMS (Priority: P1) MVP

**Goal**: Wallet DEKs protected by Azure Key Vault wrap/unwrap. All wallets benefit from hardware-backed key protection.

**Independent Test**: Deploy with Azure Key Vault configured. Create a wallet, sign a transaction, verify signing succeeds. Inspect Key Vault audit logs for wrap/unwrap operations.

### Tests for User Story 1

- [ ] T021 [P] [US1] Write unit tests for AzureKeyProtectionProvider in tests/Sorcha.Wallet.Providers.Azure.Tests/AzureKeyProtectionProviderTests.cs — mock KeyClient and CryptographyClient, test CreateKeyAsync, WrapKeyAsync, UnwrapKeyAsync, KeyExistsAsync
- [ ] T022 [P] [US1] Write unit tests for KeyManagementService DEK cache with grace period in tests/Sorcha.Wallet.Core.Tests/Services/KeyManagementServiceCacheTests.cs — test cache hit, cache miss, cache expiry, grace period during outage, fail closed after grace

### Implementation for User Story 1

- [ ] T023 [P] [US1] Create `AzureKmsOptions` config model in src/Providers/Sorcha.Wallet.Providers.Azure/AzureKmsOptions.cs — reuse fields from existing AzureKeyVaultOptions (VaultUri, UseManagedIdentity, ManagedIdentityClientId, DekCacheTtlMinutes, AllowStaleDeksOnOutage)
- [ ] T024 [US1] Implement `AzureKeyProtectionProvider` in src/Providers/Sorcha.Wallet.Providers.Azure/AzureKeyProtectionProvider.cs — use KeyClient for key creation, CryptographyClient for wrap (RsaOaep256) and unwrap operations, DefaultAzureCredential/ManagedIdentityCredential auth
- [ ] T025 [US1] Create DI extension in src/Providers/Sorcha.Wallet.Providers.Azure/Extensions/ServiceCollectionExtensions.cs — register AzureKeyProtectionProvider as IKeyProtectionProvider singleton
- [ ] T026 [US1] Add "AzureKeyVault" case to provider factory switch in src/Services/Sorcha.Wallet.Service/Extensions/WalletServiceExtensions.cs — resolve AzureKeyProtectionProvider when EncryptionProvider:Type is "AzureKeyVault"
- [ ] T027 [US1] Add audit logging for all KMS operations in AzureKeyProtectionProvider — log wrap/unwrap/create key with timing, key ID, and success/failure status using existing EncryptionAuditLogger pattern
- [ ] T028 [US1] Add project reference from Sorcha.Wallet.Service.csproj to Sorcha.Wallet.Providers.Azure.csproj
- [ ] T029 [US1] Integration test: create wallet with AzureKeyVault provider configured (mock Azure SDK), verify DEK is wrapped via KMS, verify signing works end-to-end

**Checkpoint**: Envelope encryption with Azure Key Vault fully functional. All wallets get cloud-protected DEKs.

---

## Phase 4: User Story 2 — KMS-Resident Signing (Priority: P2)

**Goal**: High-security wallets where the private key lives entirely within Azure Key Vault. Signing performed by KMS.

**Independent Test**: Create a wallet with KmsResident mode. Sign a transaction. Verify no private key in database. Verify signature is valid.

**Depends on**: Phase 3 (Azure provider project exists)

### Tests for User Story 2

- [ ] T030 [P] [US2] Write unit tests for AzureSigningProvider in tests/Sorcha.Wallet.Providers.Azure.Tests/AzureSigningProviderTests.cs — mock KeyClient/CryptographyClient, test CreateSigningKeyAsync (P-256), SignAsync, VerifyAsync, GetPublicKeyAsync
- [ ] T031 [P] [US2] Write unit tests for WalletManager KMS-resident creation path in tests/Sorcha.Wallet.Core.Tests/Services/WalletManagerKmsTests.cs — test KmsResident wallet creation skips local key derivation, stores KmsKeyId, rejects non-P256 algorithm

### Implementation for User Story 2

- [ ] T032 [US2] Implement `AzureSigningProvider` in src/Providers/Sorcha.Wallet.Providers.Azure/AzureSigningProvider.cs — use KeyClient.CreateEcKeyAsync (P-256), CryptographyClient.SignAsync (ES256), VerifyAsync, GetPublicKeyAsync
- [ ] T033 [US2] Register AzureSigningProvider as ISigningProvider in src/Providers/Sorcha.Wallet.Providers.Azure/Extensions/ServiceCollectionExtensions.cs
- [ ] T034 [US2] Add `SignWithKmsAsync` method to KeyManagementService in src/Core/Sorcha.Wallet.Core/Services/Implementation/KeyManagementService.cs — delegates to ISigningProvider.SignAsync
- [ ] T035 [US2] Modify WalletManager.CreateWalletAsync in src/Core/Sorcha.Wallet.Core/Services/Implementation/WalletManager.cs — branch on resolved SigningMode: KmsResident calls ISigningProvider.CreateSigningKeyAsync, Local uses existing HD derivation
- [ ] T036 [US2] Modify WalletManager signing path to branch on wallet.SigningMode — KmsResident calls KeyManagementService.SignWithKmsAsync, Local uses existing decrypt-and-sign flow
- [ ] T037 [US2] Add validation: reject KmsResident with non-P256 algorithm in WalletManager.CreateWalletAsync, return 400 with clear error
- [ ] T038 [US2] Add validation: reject KmsResident when no ISigningProvider is registered (Local provider only), return 400 with clear error
- [ ] T039 [US2] Add audit logging for KMS signing operations — log sign request with wallet ID, key ID, timing, success/failure
- [ ] T040 [US2] Integration test: create KmsResident wallet, verify no EncryptedPrivateKey in DB, verify KmsKeyId populated, sign transaction and verify valid signature

**Checkpoint**: KMS-resident signing fully functional. System wallets can use cloud HSM for signing.

---

## Phase 5: User Story 3 — Signing Mode Policy and Override (Priority: P3)

**Goal**: Automatic signing mode assignment based on derivation path policy with API override.

**Independent Test**: Configure KMS-resident paths. Create wallets at system and user paths. Verify correct mode assignment. Override via API and verify.

**Depends on**: Phase 4 (KMS-resident wallets exist)

### Implementation for User Story 3

- [ ] T041 [US3] Inject `SigningModePolicy` into WalletManager — resolve signing mode using policy before wallet creation
- [ ] T042 [US3] Update wallet creation API endpoint in src/Services/Sorcha.Wallet.Service/ to accept optional `signingMode` parameter in request body
- [ ] T043 [US3] Pass signingMode override from API through to WalletManager.CreateWalletAsync — policy resolves: (1) API override if AllowSigningModeOverride, (2) path match, (3) default
- [ ] T044 [US3] Write unit tests for policy integration in WalletManager — test system path gets KmsResident, user path gets Local, API override works, API override rejected when disabled
- [ ] T045 [US3] Update wallet creation API response to include `signingMode` and `kmsKeyId` fields
- [ ] T046 [US3] Add OpenAPI documentation (.WithSummary, .WithDescription) for the new signingMode parameter on the wallet creation endpoint

**Checkpoint**: All three user stories functional. Policy-driven signing mode with full override support.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, cleanup, final validation

- [ ] T047 [P] Update Wallet Service README.md with cloud KMS configuration section
- [ ] T048 [P] Update docs/reference/development-status.md — mark Wallet Service key management as production-ready
- [ ] T049 Update MASTER-TASKS.md — mark SEC-002 complete, reference Feature 082
- [ ] T050 Remove deprecated `IEncryptionProvider` interface file if not already removed in T019
- [ ] T051 Run full test suite — verify all tests pass, >85% coverage on new code
- [ ] T052 Run quickstart.md validation — verify Azure setup instructions are accurate
- [ ] T053 Update CLAUDE.md if architecture patterns changed

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 - Envelope Encryption)**: Depends on Phase 2
- **Phase 4 (US2 - KMS-Resident Signing)**: Depends on Phase 3 (Azure provider project)
- **Phase 5 (US3 - Policy & Override)**: Depends on Phase 4 (KMS-resident wallets)
- **Phase 6 (Polish)**: Depends on all desired stories complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational. Independent — delivers envelope encryption.
- **US2 (P2)**: Depends on US1 (Azure provider project must exist). Adds KMS-resident signing.
- **US3 (P3)**: Depends on US2 (KMS-resident wallets must work). Adds policy layer.

### Within Each User Story

- Tests written and fail before implementation
- Config/models before services
- Services before endpoint changes
- Core implementation before integration tests
- Commit after each task

### Parallel Opportunities

Phase 1:
```
T001 (IKeyProtectionProvider) — sequential (other tasks reference it)
T002 (ISigningProvider)       ─┐
T003 (KmsKeyInfo)             ├─ parallel (independent files)
T004 (SigningMode enum)       ─┘
T007 (Azure project)          ─┐
T008 (Azure test project)     ─┘ parallel
```

Phase 2:
```
T015 (Windows provider)       ─┐
T016 (Linux provider)         ├─ parallel (independent providers)
T018 (Policy tests)           ─���
```

Phase 3:
```
T021 (Azure provider tests)   ─┐
T022 (Cache tests)            ─┘ parallel
T023 (Azure config)           ─ before T024
```

Phase 4:
```
T030 (Signing provider tests) ─┐
T031 (WalletManager tests)    ─┘ parallel
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (interfaces, migration, projects)
2. Complete Phase 2: Foundational (refactor to IKeyProtectionProvider)
3. Complete Phase 3: User Story 1 (Azure envelope encryption)
4. **STOP and VALIDATE**: Test with Azure Key Vault — all wallets get cloud-protected DEKs
5. This alone delivers the core SEC-002 security improvement

### Incremental Delivery

1. Setup + Foundational → Existing functionality preserved with new interfaces
2. Add US1 (Envelope Encryption) → MVP: cloud KMS protects all DEKs
3. Add US2 (KMS-Resident Signing) → High-security wallets with HSM-resident keys
4. Add US3 (Policy & Override) → Automatic mode assignment, operator flexibility
5. Each story adds value without breaking previous stories

---

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 53 |
| Phase 1 (Setup) | 8 tasks |
| Phase 2 (Foundational) | 12 tasks |
| Phase 3 (US1 — Envelope Encryption) | 9 tasks |
| Phase 4 (US2 — KMS-Resident Signing) | 11 tasks |
| Phase 5 (US3 — Policy & Override) | 6 tasks |
| Phase 6 (Polish) | 7 tasks |
| Parallel opportunities | 14 tasks marked [P] |
| MVP scope | Phases 1-3 (29 tasks) |

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- Each user story is independently testable after completion
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
