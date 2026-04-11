---

description: "Task list for spec 101: HAIP Walkthroughs"
---

# Tasks: HAIP Walkthroughs

**Input**: Design documents from `specs/101-haip-walkthroughs/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — FR-009 mandates clear error reporting, agent commands need unit coverage.

## Phase 1: Setup

- [ ] T001 Verify `101-haip-walkthroughs` branch is rebased onto master with all HAIP specs (093-098) and infrastructure (#237-248) merged
- [ ] T002 Verify Sorcha.Agent project builds: `dotnet build src/Apps/Sorcha.Agent/Sorcha.Agent.csproj`
- [ ] T003 Add project reference to `Sorcha.Cryptography` in `src/Apps/Sorcha.Agent/Sorcha.Agent.csproj` (needed for SdJwtService, ECDsa operations)
- [ ] T004 [P] Create directory structure: `src/Apps/Sorcha.Agent/Haip/` for new HAIP wallet files

## Phase 2: Foundational — Holder Key Manager + Credential Wallet

- [ ] T005 Create `src/Apps/Sorcha.Agent/Haip/HolderKeyManager.cs` — P-256 key pair generation, PEM persistence (`holder-key.pem`), JWK export (`holder-key.jwk.json`), load-or-create pattern per FR-002
- [ ] T006 [P] Create `src/Apps/Sorcha.Agent/Haip/CredentialWallet.cs` — file-based SD-JWT storage per FR-004: `StoreAsync(type, rawSdJwt)`, `LoadAsync(type)`, `ListTypes()`, organised in `wallet/credentials/` directory
- [ ] T007 [P] Write failing test `HolderKeyManager_GenerateOrLoad_ReturnsDeterministicKey` in `tests/Sorcha.Agent.Tests/Haip/HolderKeyManagerTests.cs`
- [ ] T008 [P] Write failing test `CredentialWallet_StoreAndLoad_RoundTrips` in `tests/Sorcha.Agent.Tests/Haip/CredentialWalletTests.cs`
- [ ] T009 Write failing test `HolderKeyManager_ExportJwk_ValidP256Format` in the same file as T007
- [ ] T010 Write failing test `CredentialWallet_ListTypes_ReturnsStoredTypes` in the same file as T008

## Phase 3: User Story 1 — `haip receive` command (Priority: P1) 🎯 MVP

**Goal**: Agent can receive a credential via the OID4VCI pre-authorized code flow.

### Tests for US1

- [ ] T011 [P] [US1] Write failing test `JwtProofBuilder_Build_ContainsRequiredClaims` in `tests/Sorcha.Agent.Tests/Haip/JwtProofBuilderTests.cs`
- [ ] T012 [P] [US1] Write failing test `JwtProofBuilder_Build_SignatureVerifies` in the same file
- [ ] T013 [P] [US1] Write failing test `JwtProofBuilder_Build_BindsCNonce` in the same file

### Implementation

- [ ] T014 [US1] Create `src/Apps/Sorcha.Agent/Haip/JwtProofBuilder.cs` — builds JWT proof of possession per OpenID4VCI: header with `alg`, `typ: openid4vci-proof+jwt`, `jwk` (holder public key); payload with `iss`, `aud` (credential_issuer), `iat`, `nonce` (c_nonce). Signs with holder's ECDsa private key per FR-003
- [ ] T015 [US1] Create `src/Apps/Sorcha.Agent/Commands/HaipReceiveCommand.cs` using System.CommandLine with `--offer-uri`, `--key-file`, `--wallet-dir` options per contracts/README.md
- [ ] T016 [US1] Implement the receive flow in HaipReceiveCommand: parse offer URI → fetch issuer metadata from `.well-known/openid-credential-issuer` (FR-010) → extract pre-auth code → POST /token (exchange code, FR-001) → POST /nonce (get c_nonce) → build JWT proof (FR-003) → POST /credential → parse response → store credential (FR-004) → print summary
- [ ] T017 [US1] Register `haip receive` as a subcommand in `src/Apps/Sorcha.Agent/Program.cs`
- [ ] T018 [US1] Handle error cases per FR-009: expired code (exit 2), proof rejected (exit 3), network error (exit 4), invalid offer URI (exit 1)

## Phase 4: User Story 2 — `haip present` command (Priority: P1)

**Goal**: Agent can present a stored credential via the OID4VP direct_post flow.

### Tests for US2

- [ ] T019 [P] [US2] Write failing test `KbJwtBuilder_Build_ContainsAudNonceSdHash` in `tests/Sorcha.Agent.Tests/Haip/KbJwtBuilderTests.cs`
- [ ] T020 [P] [US2] Write failing test `KbJwtBuilder_Build_SignatureVerifies` in the same file
- [ ] T021 [P] [US2] Write failing test `KbJwtBuilder_Build_SdHashMatchesPresentationPrefix` in the same file

### Implementation

- [ ] T022 [US2] Create `src/Apps/Sorcha.Agent/Haip/KbJwtBuilder.cs` — builds Key Binding JWT per SD-JWT spec: header with `alg`, `typ: kb+jwt`; payload with `aud`, `nonce`, `iat`, `sd_hash`. Signs with holder's ECDsa private key per FR-006
- [ ] T023 [US2] Create `src/Apps/Sorcha.Agent/Commands/HaipPresentCommand.cs` using System.CommandLine with `--request-uri`, `--credential`, `--disclose`, `--key-file`, `--wallet-dir` options per contracts/README.md
- [ ] T024 [US2] Implement the present flow: load credential from wallet (FR-005) → fetch request object from request_uri → extract nonce, audience, required claims → select disclosures matching `--disclose` (FR-008) → build presentation with selected disclosures → build KB-JWT (FR-006) → POST direct_post with vp_token + state (FR-007) → print result
- [ ] T025 [US2] Register `haip present` as a subcommand in `src/Apps/Sorcha.Agent/Program.cs`
- [ ] T026 [US2] Handle error cases per FR-009: credential not found (exit 1), request expired (exit 2), verification failed (exit 3), network error (exit 4)

## Phase 5: User Story 3 — HaipIdentityAttestation walkthrough (Priority: P1)

**Goal**: A complete walkthrough issues a verified identity credential to a citizen.

- [ ] T027 [US3] Create `walkthroughs/HaipIdentityAttestation/actors/citizen.json` — actor definition with `haip` section: `holderKeyAlgorithm: ES256`, `walletDir: ./wallet`
- [ ] T028 [US3] Create `walkthroughs/HaipIdentityAttestation/setup.ps1` — idempotent setup per FR-011/FR-014: check service health, authenticate as platform admin, create tenant (or reuse), provision trust anchor, create Government Identity Authority org, enrol as HAIP issuer, create citizen user with persona data (givenName: Alice, familyName: O'Brien, email: alice@example.com, dateOfBirth: 1990-03-15, address: 42 Grafton St, Dublin, Leinster, D02 Y1K8, Ireland). Save state.json per FR-015
- [ ] T029 [US3] Create `walkthroughs/HaipIdentityAttestation/run.ps1` — execution per FR-012/FR-013: load state.json, create credential offer via HAIP service internal API (POST /api/v1/offers with persona claims, disclosable paths for all fields + nested `/address/*`), invoke `sorcha-agent haip receive --offer-uri <uri> --wallet-dir ./wallet`, verify credential file exists, print summary banner
- [ ] T030 [US3] Update `walkthroughs/run-all.ps1` to include HaipIdentityAttestation
- [ ] T031 [US3] Update `walkthroughs/README.md` with HaipIdentityAttestation entry in the walkthrough table

## Phase 6: User Story 4 — HaipDrivingLicence walkthrough (Priority: P2)

**Goal**: A complete walkthrough verifies an identity credential then issues a driving licence.

- [ ] T032 [US4] Create `walkthroughs/HaipDrivingLicence/blueprints/driving-licence.json` — Blueprint with 2 actions: Action 1 requires VerifiedIdentityCredential (PresentationSource: HaipExternalWallet, requiredClaims: givenName, familyName, dateOfBirth, address.locality), Action 2 issues DrivingLicenceCredential (TargetAudience: HaipExternalWallet, claims: licenceNumber, vehicleClass, issuedDate, expiryDate, holder name + address, disclosable: all + `/address/locality`) per FR-017/FR-021
- [ ] T033 [US4] Create `walkthroughs/HaipDrivingLicence/actors/citizen.json` — actor definition reusing wallet keys from HaipIdentityAttestation
- [ ] T034 [US4] Create `walkthroughs/HaipDrivingLicence/setup.ps1` — per FR-016: check for HaipIdentityAttestation/state.json (run it inline if missing), create Council Licensing Authority org, enrol as HAIP issuer, publish driving-licence.json Blueprint. Save state.json
- [ ] T035 [US4] Create `walkthroughs/HaipDrivingLicence/run.ps1` — per FR-018/FR-019/FR-020: load state, start Blueprint instance, Action 1 creates presentation request → invoke `sorcha-agent haip present --request-uri <uri> --credential VerifiedIdentityCredential --disclose "givenName,familyName,dateOfBirth,address.locality"` → verify acceptance, Action 2 creates credential offer → invoke `sorcha-agent haip receive --offer-uri <uri>` → verify both credentials in wallet → print summary banner
- [ ] T036 [US4] Update `walkthroughs/run-all.ps1` to include HaipDrivingLicence (after HaipIdentityAttestation)
- [ ] T037 [US4] Update `walkthroughs/README.md` with HaipDrivingLicence entry

## Phase 7: Polish

- [ ] T038 Run `dotnet test tests/Sorcha.Agent.Tests` — confirm all new tests pass
- [ ] T039 Run `dotnet build src/Apps/Sorcha.Agent/` — confirm clean build
- [ ] T040 Run HaipIdentityAttestation against Docker stack and verify credential
- [ ] T041 Run HaipDrivingLicence against Docker stack and verify both credentials
- [ ] T042 [P] Update `src/Apps/Sorcha.Agent/README.md` with HAIP commands documentation
- [ ] T043 [P] Update `walkthroughs/ORGANIZATION-SUMMARY.md` if it tracks walkthrough metadata

## Dependencies

- Phase 1 → Phase 2 → Phase 3 (US1 receive) → Phase 4 (US2 present) → Phase 5 (US3 identity walkthrough) → Phase 6 (US4 licence walkthrough) → Phase 7
- US1 and US2 share foundational code (Phase 2) but are otherwise independent
- US3 depends on US1 (needs `haip receive` command)
- US4 depends on US1 + US2 + US3 (needs both commands + identity credential)

## Parallel opportunities

- Phase 2: T006, T007, T008 parallel (different files)
- Phase 3: T011-T013 parallel (test files)
- Phase 4: T019-T021 parallel (test files)
- Phase 7: T042-T043 parallel (doc files)

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T004 | 4 |
| Phase 2 Foundational | T005-T010 | 6 |
| Phase 3 US1 haip receive | T011-T018 | 8 |
| Phase 4 US2 haip present | T019-T026 | 8 |
| Phase 5 US3 Identity walkthrough | T027-T031 | 5 |
| Phase 6 US4 Licence walkthrough | T032-T037 | 6 |
| Phase 7 Polish | T038-T043 | 6 |
| **Total** | | **43** |

**Suggested MVP**: Phase 1 + 2 + 3 = 18 tasks. Ships `haip receive` with holder key management, JWT proof construction, and credential storage. Can be verified against the Docker stack immediately.
