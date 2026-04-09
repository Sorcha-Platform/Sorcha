---

description: "Task list for spec 094: SD-JWT VC HAIP Hardening"
---

# Tasks: SD-JWT VC HAIP Hardening

**Input**: Design documents from `specs/094-sdjwt-haip-hardening/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — spec 094 FR-039 mandates unit + integration coverage for all new behaviour.

## Phase 1: Setup

- [ ] T001 Confirm `094-sdjwt-haip-hardening` branch is rebased onto current master that includes spec 093 merged
- [ ] T002 Verify the BIP32 purpose-derivation primitive from Features 086/092 is exposed via `IKeyManagementService` or equivalent in `src/Common/Sorcha.Cryptography`
- [ ] T003 [P] Add two new purpose constants `sorcha:credential-holder-binding` and `sorcha:haip-issuer-signing` to `src/Common/Sorcha.Cryptography/SorchaDerivationPaths.cs` (or the equivalent shared constants file)

## Phase 2: Foundational

- [ ] T004 Baseline: run `dotnet test tests/Sorcha.Cryptography.Tests` and `dotnet test tests/Sorcha.Wallet.Service.Tests` and confirm both pass on the rebased branch before any edits

## Phase 3: User Story 1 - cnf holder key binding at issuance and replay prevention (Priority: P1) 🎯 MVP

**Goal**: `ISdJwtService.CreateTokenAsync` accepts a holder JWK and embeds `cnf.jwk` in the signed payload.

### Tests for US1

- [ ] T005 [P] [US1] Write failing unit test `CreateTokenAsync_WithHolderJwk_EmbedsCnfClaim` in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtCnfBindingTests.cs`
- [ ] T006 [P] [US1] Write failing unit test `CreateTokenAsync_WithoutHolderJwk_EmitsNoCnf` in the same file (legacy path)
- [ ] T007 [P] [US1] Write failing unit test `VerifyTokenAsync_ExtractsCnfClaim` in the same file
- [ ] T008 [P] [US1] Write failing unit test `CreateTokenAsync_CnfIsNonDisclosable_NotInSdArray` in the same file

### Implementation

- [ ] T009 [US1] Extend `SdJwtToken` with an optional `Cnf` property in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtToken.cs`
- [ ] T010 [US1] Add overloaded `CreateTokenAsync` in `ISdJwtService.cs` accepting a `JsonWebKey? holderJwk` parameter
- [ ] T011 [US1] Implement the new overload in `SdJwtService.cs`: when `holderJwk` is not null, add `cnf: { jwk: ... }` to the payload dict before signing. Keep the existing overload as a wrapper delegating with `holderJwk: null`
- [ ] T012 [US1] Add a `Cnf` extraction step in `VerifyTokenAsync` that surfaces the claim on `SdJwtVerificationResult`
- [ ] T013 [US1] Add XML doc comments on all new methods per Sorcha standards

## Phase 4: User Story 2 - Nested and array-element disclosure (Priority: P1)

**Goal**: JSON Pointer paths like `/address/locality` and `/qualifications/0` are disclosable without forcing the rest of the parent container to reveal.

### Tests for US2

- [ ] T014 [P] [US2] Write failing unit test `Translate_NestedObjectField_ProducesScopedSdArray` in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtNestedDisclosureTests.cs`
- [ ] T015 [P] [US2] Write failing unit test `Translate_ArrayElement_ProducesPlaceholderDict` in the same file
- [ ] T016 [P] [US2] Write failing unit test `Translate_MixedTopLevelAndNested_BothWork` in the same file
- [ ] T017 [P] [US2] Write failing unit test `Reconstruct_DisclosedSubset_ReturnsCorrectClaims` in the same file
- [ ] T018 [P] [US2] Write failing unit test `Translate_UnknownPath_ThrowsWithClearError` in the same file
- [ ] T019 [P] [US2] Write failing unit test `ExistingBlueprints_TopLevelNameKeyed_ByteIdenticalToPreSpec094Output` in the same file

### Implementation

- [ ] T020 [US2] Create `src/Common/Sorcha.Cryptography/SdJwt/NestedDisclosure.cs` with the `Translate` and `Reconstruct` static methods per contracts/README.md
- [ ] T021 [US2] In `SdJwtService.CreateTokenAsync`, route the disclosable list through `NestedDisclosure.Translate` when it contains any JSON Pointer paths; keep the current top-level-name-only path when all entries are bare names (for byte-identical legacy output)
- [ ] T022 [US2] In `SdJwtService.VerifyTokenAsync` / `VerifyPresentationAsync`, route decoded disclosures through `NestedDisclosure.Reconstruct` before populating the result's `Claims` dict

## Phase 5: User Story 3 - Key Binding JWT at presentation (Priority: P1)

**Goal**: Presentations of `cnf`-bearing credentials must include a KB-JWT that the verifier checks.

### Tests for US3

- [ ] T023 [P] [US3] Write failing unit test `CreatePresentation_WithCnf_AppendsKbJwt` in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtKeyBindingJwtTests.cs`
- [ ] T024 [P] [US3] Write failing unit test `CreatePresentation_WithoutCnf_OmitsKbJwt_LegacyPath` in the same file
- [ ] T025 [P] [US3] Write failing unit test `VerifyPresentation_ValidKbJwt_HolderKeyVerifiedTrue` in the same file
- [ ] T026 [P] [US3] Write failing unit test `VerifyPresentation_KbJwtAudienceMismatch_Fails` in the same file
- [ ] T027 [P] [US3] Write failing unit test `VerifyPresentation_KbJwtNonceMismatch_Fails` in the same file
- [ ] T028 [P] [US3] Write failing unit test `VerifyPresentation_KbJwtClockSkewBeyondWindow_Fails` in the same file
- [ ] T029 [P] [US3] Write failing unit test `VerifyPresentation_SdHashMismatch_Fails` in the same file
- [ ] T030 [P] [US3] Write failing unit test `VerifyPresentation_CnfPresentButNoKbJwt_Fails` in the same file
- [ ] T031 [P] [US3] Write failing unit test `VerifyPresentation_NoCnf_IgnoresTrailingKbJwtIfPresent` in the same file (legacy compat)

### Implementation

- [ ] T032 [US3] Add the `KbJwtSigningDelegate` delegate type in `ISdJwtService.cs` per contracts/README.md
- [ ] T033 [US3] Add the new `CreatePresentationAsync` overload signature to `ISdJwtService.cs`
- [ ] T034 [US3] In `SdJwtService.cs`, implement KB-JWT construction: build header + payload + signing input, call the supplied delegate for the signature, append to the serialised presentation after the last `~`. Compute `sd_hash` as `base64url(sha256(presentationBytesWithoutKbJwt))`
- [ ] T035 [US3] In `SdJwtService.VerifyPresentationAsync`, split the presentation at the final `~`; if the issuer JWT's payload contains `cnf`, require and verify the trailing KB-JWT (signature against `cnf.jwk`, `aud`, `nonce`, `iat` ±60s, `sd_hash`). Populate `HolderKeyVerified`
- [ ] T036 [US3] Add `HolderKeyVerified` boolean to `SdJwtVerificationResult`

## Phase 6: User Story 4 - Holder binding key derivation for Sorcha-internal holders (Priority: P1)

**Goal**: Every Sorcha wallet has a deterministic holder binding key that the Wallet Service can use to sign KB-JWTs without the caller managing key material.

### Tests for US4

- [ ] T037 [P] [US4] Write failing unit test `GetPublicJwkAsync_ReturnsDeterministicKey_AcrossCalls` in `tests/Sorcha.Wallet.Service.Tests/Services/HolderBindingKeyServiceTests.cs`
- [ ] T038 [P] [US4] Write failing unit test `GetPublicJwkAsync_SameSeed_SameKey_OnDifferentMachines` in the same file (via fixed seed round-trip)
- [ ] T039 [P] [US4] Write failing unit test `SignKbJwtAsync_SignatureVerifies_AgainstPublicJwk` in the same file
- [ ] T040 [P] [US4] Write failing integration test `HolderBindingKey_SurvivesMnemonicRecovery` in `tests/Sorcha.Wallet.Service.IntegrationTests/HaipIssuanceRoundTripTests.cs`

### Implementation

- [ ] T041 [US4] Create `src/Core/Sorcha.Wallet.Portable/Domain/ValueObjects/HolderBindingKey.cs` per data-model.md
- [ ] T042 [US4] Create `IHolderBindingKeyService` interface in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IHolderBindingKeyService.cs`
- [ ] T043 [US4] Implement `HolderBindingKeyService` in `src/Services/Sorcha.Wallet.Service/Services/Implementation/HolderBindingKeyService.cs` using the BIP32 derivation primitive from Phase 1 T002
- [ ] T044 [US4] Register `IHolderBindingKeyService` in the Wallet Service DI wiring (`WalletServiceExtensions.cs`)
- [ ] T045 [US4] Add HTTP endpoints `GET /api/v1/wallets/{address}/holder-binding-key` and `POST /api/v1/wallets/{address}/holder-binding-key/sign-kb-jwt` in `WalletEndpoints.cs`
- [ ] T046 [US4] Update `IssueCredentialRequest` in `CredentialEndpoints.cs` to accept an optional `HolderJwk` field
- [ ] T047 [US4] In `IssueCredential`, pass the supplied `HolderJwk` through to `ISdJwtService.CreateTokenAsync`'s new overload
- [ ] T048 [US4] Update `ActionExecutionService.IssueCredentialFromActionAsync` to call `IHolderBindingKeyService.GetPublicJwkAsync(recipientWallet)` before invoking the Wallet Service issue call, and pass the JWK forward

## Phase 7: User Story 5 - Classical co-key for PQC-primary HAIP issuer wallets (Priority: P2)

### Tests for US5

- [ ] T049 [P] [US5] Write failing unit test `PqcPrimaryWallet_WithHaipIssuerFlag_DerivesClassicalCoKey` in `tests/Sorcha.Wallet.Service.Tests/Services/HaipIssuerCoKeyServiceTests.cs`
- [ ] T050 [P] [US5] Write failing unit test `ClassicalPrimaryWallet_WithHaipIssuerFlag_UsesPrimaryKeyDirectly` in the same file
- [ ] T051 [P] [US5] Write failing unit test `PqcPrimaryWallet_WithoutHaipIssuerFlag_RefusesWithCapabilityError` in the same file
- [ ] T052 [P] [US5] Write failing integration test `HaipIssuance_FromPqcPrimaryWallet_SignsWithEs256` in `HaipIssuanceRoundTripTests.cs`

### Implementation

- [ ] T053 [US5] Add `HaipIssuer` boolean field to `Wallet` entity in `src/Core/Sorcha.Wallet.Portable/Domain/Entities/Wallet.cs`. No EF migration — folded into the consolidated pre-release migration
- [ ] T054 [US5] Create `HaipIssuerCoKey` value object per data-model.md
- [ ] T055 [US5] Create `IHaipIssuerCoKeyService` interface
- [ ] T056 [US5] Implement `HaipIssuerCoKeyService` per research.md R5
- [ ] T057 [US5] Register in DI wiring
- [ ] T058 [US5] In `IssueCredential` handler, when the request indicates HAIP-path issuance (pending signalling mechanism — likely the presence of `HolderJwk`), call `IHaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync(walletAddress)` instead of reading the wallet's primary key directly. Use the returned `(privateKey, algorithm)` pair when calling `SdJwtService.CreateTokenAsync`

## Phase 8: User Story 6 - Blueprint author ergonomics (Priority: P2)

### Tests for US6

- [ ] T059 [P] [US6] Write failing unit test `MakeDisclosablePath_AddsJsonPointerToConfig` in `tests/Sorcha.Blueprint.Fluent.Tests/CredentialIssuanceBuilderTests.cs` (create file if missing)
- [ ] T060 [P] [US6] Write failing unit test `MakeDisclosable_AndMakeDisclosablePath_CoexistInSameConfig` in the same file

### Implementation

- [ ] T061 [US6] Add `MakeDisclosablePath(string jsonPointer)` method to `CredentialIssuanceBuilder.cs`
- [ ] T062 [US6] Update `CredentialIssuanceConfig.Disclosable` documentation in `Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs` to note that entries may be either bare names or JSON Pointer paths

## Phase 9: Polish

- [ ] T063 Run full `tests/Sorcha.Cryptography.Tests` suite and confirm all tests pass
- [ ] T064 Run full `tests/Sorcha.Wallet.Service.Tests` suite and confirm no regression from spec 093
- [ ] T065 Run `tests/Sorcha.Wallet.Service.IntegrationTests/HaipIssuanceRoundTripTests.cs` end to end
- [ ] T066 Run the quickstart.md manual verification procedure for each user story
- [ ] T067 [P] Update `src/Services/Sorcha.Wallet.Service/README.md` with a new section on holder binding keys and HAIP issuer co-keys
- [ ] T068 [P] Update `CLAUDE.md` (or project-level docs) with a mention of the two new BIP32 derivation purposes

## Dependencies

- Phase 1 → Phase 2 → Phases 3-8 (user stories) → Phase 9 (polish)
- Within user stories: tests before implementation; models/services before endpoints
- US1 and US2 are independent of each other but both use the same `SdJwtService` file — run sequentially to avoid merge conflicts
- US3 depends on US1 landing (KB-JWT requires `cnf` to be present)
- US4 depends on US1 + US3 (holder binding key path needs `cnf` + KB-JWT plumbing)
- US5 is independent of US1-US4 but touches the same Wallet Service DI wiring
- US6 is fully independent; can run in parallel with any other user story

## Parallel opportunities

- Phase 1 T003 is parallelisable
- All test tasks within each user story are parallelisable across different test files
- US5 and US6 can run in parallel with US1-US4 if staffed

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T003 | 3 |
| Phase 2 Foundational | T004 | 1 |
| Phase 3 US1 (cnf binding) | T005-T013 | 9 |
| Phase 4 US2 (nested disclosure) | T014-T022 | 9 |
| Phase 5 US3 (KB-JWT) | T023-T036 | 14 |
| Phase 6 US4 (holder binding key) | T037-T048 | 12 |
| Phase 7 US5 (HAIP issuer co-key) | T049-T058 | 10 |
| Phase 8 US6 (Blueprint ergonomics) | T059-T062 | 4 |
| Phase 9 Polish | T063-T068 | 6 |
| **Total** | | **68** |

**Suggested MVP**: Phases 1 + 2 + 3 + 5 (cnf binding + KB-JWT path) = 27 tasks. Nested disclosure, holder binding key service, and HAIP co-key can follow as independent increments.
