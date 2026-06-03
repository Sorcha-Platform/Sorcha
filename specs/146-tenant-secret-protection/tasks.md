---
description: "Task list for Tenant Service At-Rest Secret Protection (feature 146)"
---

# Tasks: Tenant Service At-Rest Secret Protection

**Input**: Design documents from `/specs/146-tenant-secret-protection/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/secret-protection-provider.md, quickstart.md
**Authoritative design**: `docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md`

**Tests**: INCLUDED (TDD) — required by the spec's acceptance scenarios, plan.md, and Constitution Principle IV (>85% new code).

**Organization**: Foundational phase (the shared protection seam) blocks all three user stories. Within each story, write the failing test first.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete dependencies)
- **[Story]**: US1 / US2 / US3 (Foundational, Setup, Polish carry no story label)

## Path Conventions

Single microservice: `src/Services/Sorcha.Tenant.Service/` + `tests/Sorcha.Tenant.Service.Tests/`.

---

## Phase 1: Setup

**Purpose**: Establish a clean baseline before changes.

- [ ] T001 Confirm a clean baseline: build `src/Services/Sorcha.Tenant.Service` and run `tests/Sorcha.Tenant.Service.Tests` green before any change, and re-read the authoritative design doc + the clean-break constraint (NO new EF migration — squash into `20260513152714_InitialCreate`).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared secret-protection seam, key resolution, DI wiring, and schema columns. **No user story can begin until this phase is complete.**

**⚠️ CRITICAL**: Blocks US1, US2, and US3.

- [X] T002 [P] Create `ISecretProtectionProvider` (with the convergence XML-doc note mirroring Wallet `IOrgKeyProtectionProvider`) in `src/Services/Sorcha.Tenant.Service/Services/Interfaces/ISecretProtectionProvider.cs` per `contracts/secret-protection-provider.md`.
- [X] T003 [P] Write FAILING unit tests for `SoftwareSecretProtectionProvider` (encrypt→decrypt round-trip; tamper→`AuthenticationTagMismatchException`; input `<28` bytes→`ArgumentException`; envelope is `nonce(12)∥ct∥tag(16)`) in `tests/Sorcha.Tenant.Service.Tests/Services/SoftwareSecretProtectionProviderTests.cs`.
- [X] T004 Implement `SoftwareSecretProtectionProvider` (AES-256-GCM, body byte-identical to Wallet `SoftwareKeyProtectionProvider`; key+KeyId injected; `ProviderName="Software"`) in `src/Services/Sorcha.Tenant.Service/Services/Implementation/SoftwareSecretProtectionProvider.cs` — make T003 pass. (Depends on T002, T003.)
- [X] T005 [P] Write FAILING unit tests for `TenantSecretKeyResolver` (HKDF determinism: same JWT key ⇒ same 32-byte key/KeyId; `Tenant:SecretProtection:Key` override precedence + 32-byte validation; Production/Staging with no key ⇒ throws at startup; non-prod derives from dev JWT key) in `tests/Sorcha.Tenant.Service.Tests/Services/TenantSecretKeyResolverTests.cs`.
- [X] T006 Implement `TenantSecretKeyResolver` (`HKDF-SHA256(JwtConfiguration.SigningKey, info "sorcha:tenant:secret-protection:v1")` default → `KeyId "jwt-derived-v1"`; override → `"config-v1"`; fail-closed) in `src/Services/Sorcha.Tenant.Service/Services/Implementation/TenantSecretKeyResolver.cs` — make T005 pass. (Depends on T005.)
- [X] T007 Wire DI in `src/Services/Sorcha.Tenant.Service/Program.cs` (and/or `Extensions/ServiceCollectionExtensions.cs`): register `ISecretProtectionProvider`→`SoftwareSecretProtectionProvider` as singleton via the resolver; derive + register the login-token HMAC key singleton (`HKDF-SHA256(SigningKey, info "sorcha:tenant:login-token-hmac:v1")`); run the fail-closed key resolution at startup. (Depends on T004, T006.)
- [X] T008 [P] *(done across US1 (TOTP) + US2 (IdP `ClientSecretKeyId`))* Apply data-model changes per `data-model.md`: `TotpConfiguration.EncryptedSecret` `string→byte[]` + add `EncryptionKeyId` (`Models/TotpConfiguration.cs`); add `ClientSecretKeyId` to `Models/IdentityProviderConfiguration.cs`; update both `TenantDbContext` column configs (TOTP entity + IdP entity at ~lines 358 and 466) in `src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs`.
- [X] T009 *(done across US1 (TOTP) + US2 (IdP `ClientSecretKeyId`))* Squash the column changes into the existing migration (NO new migration): edit `Migrations/20260513152714_InitialCreate.cs`, `Migrations/20260513152714_InitialCreate.Designer.cs`, and `Migrations/TenantDbContextModelSnapshot.cs` so they match the model; verify EF reports no model drift at startup. (Depends on T008.)

**Checkpoint**: Provider + resolver + DI + schema ready. User stories can begin.

---

## Phase 3: User Story 1 - Citizen 2FA secrets unreadable at rest (Priority: P1) 🎯 MVP

**Goal**: TOTP secrets stored as AES-GCM ciphertext; enrolment/verification unchanged for users.

**Independent Test**: Enrol TOTP, read `EncryptedSecret` from the DB → not the secret / not Base64-decodable to it; a valid code still verifies, an invalid one fails.

### Tests for User Story 1 ⚠️ (write first, ensure they FAIL)

- [X] T010 [P] [US1] Write FAILING tests in `tests/Sorcha.Tenant.Service.Tests/Services/TotpServiceTests.cs`: setup→validate round-trip through the real provider; the persisted `EncryptedSecret` is neither plaintext nor Base64-decodable to the Base32 secret; `EncryptionKeyId` persisted; a tampered stored secret → verification returns invalid (not an exception/500).

### Implementation for User Story 1

- [X] T011 [US1] Update `src/Services/Sorcha.Tenant.Service/Services/TotpService.cs`: inject `ISecretProtectionProvider`; `SetupAsync` encrypts the Base32 secret (`EncryptAsync(utf8(secret))`) and stores `(EncryptedSecret, EncryptionKeyId)`; `VerifyAndEnableAsync`/`ValidateCodeAsync` decrypt via `DecryptAsync`; **delete** the `v1:`-Base64 `EncryptSecret`/`DecryptSecret`. — makes T010 pass.
- [X] T012 [US1] Ensure decrypt failure handling is safe in `TotpService` (tamper/corrupt → treated as invalid code, never an unhandled error) per FR-010.

**Checkpoint**: TOTP secrets protected at rest end-to-end (the CRITICAL fix) — independently testable.

---

## Phase 4: User Story 2 - OIDC client secrets protected and usable (Priority: P2)

**Goal**: Client secrets stored as reversible AEAD and the OIDC token exchange recovers the real secret.

**Independent Test**: Save IdP config with a known client secret; stored column ≠ plaintext; the token-exchange path presents the original secret to the provider.

### Tests for User Story 2 ⚠️ (write first, ensure they FAIL)

- [X] T013 [P] [US2] Write FAILING tests in `tests/Sorcha.Tenant.Service.Tests/Services/IdpConfigurationServiceTests.cs`: save→read recovers the original client secret (regression guard for the SHA-256 bug); stored `ClientSecretEncrypted` ≠ plaintext; `ClientSecretKeyId` persisted.

### Implementation for User Story 2

- [X] T014 [US2] Update `src/Services/Sorcha.Tenant.Service/Services/IdpConfigurationService.cs`: inject `ISecretProtectionProvider`; encrypt the client secret on create/update (store `ClientSecretEncrypted` + `ClientSecretKeyId`); decrypt on read; **delete** the SHA-256 `EncryptSecret`/`DecryptSecret`; convert the static helpers to instance/async members as needed. — makes T013 pass.
- [X] T015 [US2] Update `src/Services/Sorcha.Tenant.Service/Services/OidcExchangeService.cs` (~line 127) to obtain the real decrypted client secret via `IdpConfigurationService`/`ISecretProtectionProvider` for the token exchange.
- [X] T016 [US2] *(NO-OP — corrected: `DatabaseInitializer.cs:479` seeds a **`ServicePrincipal`** (Argon2id-hashed, correct), NOT an OIDC IdP config. No IdP config is seeded — admins create them at runtime. The audit misattributed this line.)*

**Checkpoint**: OIDC client secrets protected AND the exchange path fixed — independently testable.

---

## Phase 5: User Story 3 - 2FA works across instances and restarts (Priority: P3)

**Goal**: The 2FA intermediate-token HMAC key is stable/shared (derived), not per-process random.

**Independent Test**: Issue a token under the derived key, re-derive (simulated restart / second instance with same root) → token still validates.

> **Note**: T018 edits `TotpService.cs` (same file as US1 T011) — sequence US3 after US1 to avoid same-file conflicts; not parallelizable with US1.

### Tests for User Story 3 ⚠️ (write first, ensure they FAIL)

- [ ] T017 [P] [US3] Write FAILING test in `tests/Sorcha.Tenant.Service.Tests/Services/TotpServiceTests.cs` (or a dedicated `LoginTokenSigningTests.cs`): a login token signed with the derived HMAC key validates after the key is re-derived from the same root (proves stability across restart/replica); two resolvers from the same root produce the same key.

### Implementation for User Story 3

- [ ] T018 [US3] In `src/Services/Sorcha.Tenant.Service/Services/TotpService.cs`, replace the `static readonly LoginTokenSigningKey = GenerateStableKey()` field with the injected derived HMAC key from T007; **delete** `GenerateStableKey`. — makes T017 pass.

**Checkpoint**: 2FA intermediate token stable across instances/restarts.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T019 [P] Add a clean-break guard test (or extend an existing repo-convention test) asserting no surviving `v1:`-Base64 TOTP storage path and no `SHA256.HashData`-based `EncryptSecret` remain in `src/Services/Sorcha.Tenant.Service` (SC-006).
- [ ] T020 [P] Documentation: document the optional `Tenant:SecretProtection:Key` in `src/Services/Sorcha.Tenant.Service/appsettings*.json` comments + `docs/guides/AUTHENTICATION-SETUP.md` + the Tenant Service README; ensure XML docs on all new public members (incl. the convergence note).
- [ ] T021 Run the full Tenant test suite + `dotnet build` (no warnings, Release) and execute the `quickstart.md` verification checklist (SC-001..SC-006).
- [ ] T022 Confirm >85% coverage for the new code (Constitution Principle IV); add unit tests for any uncovered branch.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (P1)**: none.
- **Foundational (P2)**: depends on Setup; **BLOCKS US1/US2/US3**.
- **US1 (P3)**, **US2 (P4)**: depend on Foundational; independent of each other.
- **US3 (P5)**: depends on Foundational; **sequence after US1** (shares `TotpService.cs`).
- **Polish (P6)**: after all desired stories.

### Within Each User Story

- Test first (must FAIL) → implementation → safe-failure/integration.
- Foundational: interface (T002) before impl (T004); tests (T003/T005) before their impls; DI (T007) after impls; migration (T009) after model (T008).

### Parallel Opportunities

- T002, T003, T005, T008 can run in parallel (different files).
- US1 (T010-T012) and US2 (T013-T016) can proceed in parallel once Foundational is done (different files/consumers).
- US3 must follow US1 (same file).

---

## Parallel Example: Foundational

```text
# In parallel (different files):
T002  Create ISecretProtectionProvider interface
T003  Failing tests for SoftwareSecretProtectionProvider
T005  Failing tests for TenantSecretKeyResolver
T008  Apply model/column changes
# Then: T004 (impl provider), T006 (impl resolver), T007 (DI), T009 (squash migration)
```

## Parallel Example: After Foundational

```text
# Two developers / two parallel tracks:
Track A (US1): T010 → T011 → T012        # TOTP at rest (MVP)
Track B (US2): T013 → T014 → T015 → T016 # OIDC client secret
# Then (single track, same file as US1): US3 T017 → T018
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → Phase 2 Foundational (CRITICAL).
2. Phase 3 US1 (TOTP at rest — the CRITICAL security fix).
3. **STOP & VALIDATE** US1 independently (DB inspection + verify flow).
4. The MVP already closes finding C1.

### Incremental Delivery

Foundational → US1 (MVP, closes CRITICAL) → US2 (fixes OIDC security + the broken exchange) → US3 (multi-replica 2FA stability) → Polish. Each story is independently testable and adds value without breaking the prior ones.

---

## Notes

- [P] = different files, no incomplete dependency.
- Verify every test FAILS before implementing it.
- Commit after each task or logical group.
- Pre-release clean break: **never** add a new EF migration — squash into `20260513152714_InitialCreate`; DB is cleared on rollout.
- Never log secret/key material.
