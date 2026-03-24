# Tasks: Verifiable Credentials & eIDAS-Aligned Attestation System

**Input**: Design documents from `/specs/031-verifiable-credentials/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/credential-endpoints.md, quickstart.md

**Tests**: Included — constitution requires >85% coverage for new code.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup

**Purpose**: SD-JWT dependency evaluation and directory structure

- [x] T001 Evaluate HeroSD-JWT NuGet package — add `HeroSD-JWT` 1.1.7 to `src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj` and verify Ed25519/P-256/RSA signing works with existing `Sorcha.Cryptography` key types. If evaluation fails, flag for custom implementation fallback.
- [x] T002 [P] Create `Credentials/` directory in `src/Common/Sorcha.Blueprint.Models/Credentials/` for new model classes
- [x] T003 [P] Create `SdJwt/` directory in `src/Common/Sorcha.Cryptography/SdJwt/` for SD-JWT implementation
- [x] T004 [P] Create `Credentials/` directory in `src/Core/Sorcha.Blueprint.Engine/Credentials/` for verification pipeline
- [x] T005 [P] Create `Credentials/` directory in `src/Services/Sorcha.Wallet.Service/Credentials/` for credential storage

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, SD-JWT service, and engine context modifications that ALL user stories depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 [P] Create `RevocationCheckPolicy` enum (`FailClosed`, `FailOpen`) in `src/Common/Sorcha.Blueprint.Models/Credentials/RevocationCheckPolicy.cs`
- [x] T007 [P] Create `ClaimConstraint` model (ClaimName, ExpectedValue) with validation in `src/Common/Sorcha.Blueprint.Models/Credentials/ClaimConstraint.cs`
- [x] T008 [P] Create `CredentialRequirement` model (Type, AcceptedIssuers, RequiredClaims, RevocationCheckPolicy, Description) with validation in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs`
- [x] T009 [P] Create `ClaimMapping` model (ClaimName, SourceField) in `src/Common/Sorcha.Blueprint.Models/Credentials/ClaimMapping.cs`
- [x] T010 [P] Create `CredentialIssuanceConfig` model (CredentialType, ClaimMappings, RecipientParticipantId, ExpiryDuration, RegisterId, Disclosable) with validation in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`
- [x] T011 [P] Create `CredentialPresentation` model (CredentialId, DisclosedClaims, RawPresentation, KeyBindingProof) in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialPresentation.cs`
- [x] T012 [P] Create `CredentialValidationError` model (RequirementType, FailureReason enum, Message) in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialValidationError.cs`
- [x] T013 [P] Create `CredentialRevocation` model (CredentialId, RevokedBy, RevokedAt, Reason, LedgerTxId) in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRevocation.cs`
- [x] T014 Add `CredentialRequirements` (IEnumerable<CredentialRequirement>) and `CredentialIssuanceConfig` (CredentialIssuanceConfig?) properties with JSON serialization to `src/Common/Sorcha.Blueprint.Models/Action.cs`
- [x] T015 Add `CredentialPresentations` (IEnumerable<CredentialPresentation>) init-only property to `src/Core/Sorcha.Blueprint.Engine/Models/ExecutionContext.cs`
- [x] T016 [P] Create `CredentialValidationResult` (IsValid, Errors, VerifiedCredentials) in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialValidationResult.cs`
- [x] T017 Add `CredentialValidation` (CredentialValidationResult) and `IssuedCredential` properties to `src/Core/Sorcha.Blueprint.Engine/Models/ActionExecutionResult.cs`
- [x] T018 Create `ISdJwtService` interface (CreateTokenAsync, VerifyTokenAsync, CreatePresentationAsync, VerifyPresentationAsync) in `src/Common/Sorcha.Cryptography/SdJwt/ISdJwtService.cs`
- [x] T019 [P] Create `SdJwtToken` model (Header, Payload, Disclosures, Signature, RawToken) in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtToken.cs`
- [x] T020 [P] Create `SdJwtPresentation` model (Token, SelectedDisclosures, KeyBindingJwt) in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtPresentation.cs`
- [x] T021 [P] Create `SdJwtVerificationResult` model (IsValid, Claims, Errors) in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtVerificationResult.cs`
- [x] T022 Implement `SdJwtService` — SD-JWT VC token creation with disclosure hashing, `_sd` arrays, and signing via existing Sorcha.Cryptography key types (Ed25519/P-256/RSA) in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs`. Use HeroSD-JWT (T001) or implement per RFC 9901.
- [x] T023 Register `ISdJwtService` in DI — add service registration to `src/Common/Sorcha.Cryptography/Extensions/CryptographyServiceExtensions.cs` (or create if needed)

### FluentValidation Validators

- [x] T024 [P] Create `CredentialRequirementValidator` (FluentValidation) — Type non-empty max 200 chars, AcceptedIssuers valid DID/wallet format, ClaimConstraint.ClaimName non-empty in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirementValidator.cs`
- [x] T025 [P] Create `CredentialIssuanceConfigValidator` (FluentValidation) — CredentialType non-empty, ClaimMappings non-empty with valid SourceField JSON Pointers, RecipientParticipantId non-empty, ExpiryDuration positive if set in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfigValidator.cs`
- [x] T026 [P] Create `CredentialPresentationValidator` (FluentValidation) — CredentialId non-empty valid DID URI, RawPresentation non-empty, DisclosedClaims non-null in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialPresentationValidator.cs`

### Foundational Tests

- [x] T027 [P] Unit tests for credential model validation (CredentialRequirement, ClaimConstraint, CredentialIssuanceConfig) including FluentValidation rules in `tests/Sorcha.Blueprint.Models.Tests/Credentials/CredentialModelsTests.cs`
- [x] T028 [P] Unit tests for SD-JWT token creation and verification round-trip in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtServiceTests.cs`
- [x] T029 [P] Unit tests verifying Action model serialization with new CredentialRequirements and CredentialIssuanceConfig properties in `tests/Sorcha.Blueprint.Models.Tests/Credentials/ActionCredentialSerializationTests.cs`

**Checkpoint**: Foundation ready — all models, SD-JWT service, and engine context modifications in place. User story implementation can begin.

---

## Phase 3: User Story 1 — Gate a Blueprint Action on a Credential (Priority: P1) MVP

**Goal**: Blueprint actions can require credentials as entry gates. Participants with valid credentials proceed; participants without are blocked with clear error messages.

**Independent Test**: Create a blueprint with a credential requirement on an action, execute with and without a valid credential, verify correct accept/reject behavior.

### Implementation for User Story 1

- [x] T030 [US1] Create `ICredentialVerifier` interface (VerifyAsync taking action requirements + presentations, returning CredentialValidationResult) in `src/Core/Sorcha.Blueprint.Engine/Credentials/ICredentialVerifier.cs`
- [x] T031 [US1] Implement `CredentialVerifier` — verifies signature (via ISdJwtService), checks expiry, checks claim matching against requirements. Revocation check deferred to US5. Returns CredentialValidationResult with specific error per FR-007/FR-008 in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs`
- [x] T032 [US1] Modify `ActionProcessor.ProcessAsync()` to insert Step 0: credential verification before schema validation. If action has CredentialRequirements and verification fails, set `result.Success = false` with credential errors. Skip for actions with no requirements in `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs`
- [x] T033 [US1] Register `ICredentialVerifier` in DI in `src/Core/Sorcha.Blueprint.Engine/Extensions/BlueprintEngineServiceExtensions.cs`
- [x] T034 [P] [US1] Create `CredentialRequirementBuilder` fluent API (OfType, FromIssuer, RequireClaim, WithRevocationCheck, WithDescription) in `src/Core/Sorcha.Blueprint.Fluent/CredentialRequirementBuilder.cs`
- [x] T035 [US1] Add `RequiresCredential(Action<CredentialRequirementBuilder>)` method to `ActionBuilder` in `src/Core/Sorcha.Blueprint.Fluent/ActionBuilder.cs`
- [x] T036 [P] [US1] Create `CredentialEntity` EF Core entity (Id, Type, IssuerDid, SubjectDid, Claims JSON, IssuedAt, ExpiresAt, RawToken, Status, IssuanceTxId, IssuanceBlueprintId, WalletAddress, CreatedAt) in `src/Services/Sorcha.Wallet.Service/Credentials/CredentialEntity.cs`
- [x] T037 [US1] Create EF Core migration `AddCredentialStore` — add `Credentials` table to wallet database in `src/Services/Sorcha.Wallet.Service/Migrations/`
- [x] T038 [US1] Create `ICredentialStore` interface (GetByWalletAsync, GetByIdAsync, StoreAsync, DeleteAsync, MatchAsync) in `src/Services/Sorcha.Wallet.Service/Credentials/ICredentialStore.cs`
- [x] T039 [US1] Implement `CredentialStore` using EF Core against PostgreSQL in `src/Services/Sorcha.Wallet.Service/Credentials/CredentialStore.cs`
- [x] T040 [US1] Implement `CredentialMatcher` — matches stored credentials against CredentialRequirement list (type, issuer, claims), returns matches per requirement in `src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs`
- [x] T041 [US1] Create wallet credential endpoints: GET list, GET by ID, POST match, DELETE, GET export per contracts in `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`
- [x] T042 [US1] Register credential services (ICredentialStore, CredentialMatcher) in wallet service DI in `src/Services/Sorcha.Wallet.Service/Extensions/WalletServiceExtensions.cs`
- [x] T043 [US1] Add YARP routes for wallet credential endpoints (`/api/v1/wallets/{walletAddress}/credentials/**`) in `src/Services/Sorcha.ApiGateway/` YARP configuration
- [x] T044 [US1] Modify `ActionExecutionService` to extract credential presentations from request, populate `ExecutionContext.CredentialPresentations`, and pass to engine in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [x] T045 [US1] Add blueprint publish-time validation for credential requirement definitions (FR-020): validate types non-empty, claim constraints well-formed, issuer references valid format in blueprint validation logic in `src/Services/Sorcha.Blueprint.Service/`

### UI for User Story 1

- [x] T046 [US1] Create `CredentialGatePanel` Blazor component — displays credential requirements on a gated action, shows matched/unmet status per requirement (FR-009b) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialGatePanel.razor`
- [x] T047 [US1] Create `CredentialSelectorModal` Blazor component — shows auto-matched credentials from wallet for participant to confirm/select, supports multi-requirement selection (FR-009a) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Components/Credentials/CredentialSelectorModal.razor`
- [x] T048 [US1] Wire credential UI into action execution page — call wallet `POST /match` endpoint on load, show CredentialGatePanel if requirements exist, open CredentialSelectorModal before action submission, include selected credential presentations in action execution request in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/` (action execution page)

### Tests for User Story 1

- [x] T049 [P] [US1] Unit tests for `CredentialVerifier` — valid credential accepted, expired rejected, issuer mismatch rejected, claim mismatch rejected, missing credential rejected in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialVerifierTests.cs`
- [x] T050 [P] [US1] Unit tests for `ActionProcessor` Step 0 integration — credentialed action with valid/invalid credentials, non-credentialed action unchanged in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/ActionProcessorCredentialTests.cs`
- [x] T051 [P] [US1] Unit tests for `CredentialRequirementBuilder` fluent API in `tests/Sorcha.Blueprint.Fluent.Tests/Credentials/CredentialRequirementBuilderTests.cs`
- [x] T052 [P] [US1] Unit tests for `CredentialStore` CRUD operations in `tests/Sorcha.Wallet.Service.Tests/Credentials/CredentialStoreTests.cs`
- [x] T053 [P] [US1] Unit tests for `CredentialMatcher` matching logic (type match, issuer filter, claim filter, no match) in `tests/Sorcha.Wallet.Service.Tests/Credentials/CredentialMatcherTests.cs`

**Checkpoint**: US1 complete — credential-gated actions work end-to-end. A blueprint with credential requirements correctly accepts/rejects participants based on their stored credentials. UI displays requirements and enables credential selection.

---

## Phase 4: User Story 2 — Issue a Credential from a Blueprint Flow (Priority: P2)

**Goal**: Blueprint actions can mint new verifiable credentials (SD-JWT VC format) signed by the authority participant's wallet key, delivered to the recipient, and recorded on the ledger.

**Independent Test**: Run a multi-step approval blueprint to completion and verify a valid SD-JWT VC credential is produced, properly signed, stored in the recipient's wallet, and the issuance event is on the ledger.

### Implementation for User Story 2

- [x] T054 [US2] Create `ICredentialIssuer` interface (IssueAsync taking CredentialIssuanceConfig + processed action data + issuer wallet + recipient wallet, returning issued credential) in `src/Core/Sorcha.Blueprint.Engine/Credentials/ICredentialIssuer.cs`
- [x] T055 [US2] Implement `CredentialIssuer` — extracts claims from action data via ClaimMappings, generates credential DID URI, calls ISdJwtService.CreateTokenAsync to mint SD-JWT VC, returns VerifiableCredential model in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialIssuer.cs`
- [x] T056 [US2] Modify `ActionProcessor.ProcessAsync()` to insert Step 5 (after disclosures): if action has CredentialIssuanceConfig, call ICredentialIssuer.IssueAsync and set `result.IssuedCredential` in `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs`
- [x] T057 [US2] Register `ICredentialIssuer` in DI in `src/Core/Sorcha.Blueprint.Engine/Extensions/BlueprintEngineServiceExtensions.cs`
- [x] T058 [P] [US2] Create `CredentialIssuanceBuilder` fluent API (OfType, MapClaim, ToRecipient, ExpiresAfter, RecordOnRegister, MakeDisclosable) in `src/Core/Sorcha.Blueprint.Fluent/CredentialIssuanceBuilder.cs`
- [x] T059 [US2] Add `IssuesCredential(Action<CredentialIssuanceBuilder>)` method to `ActionBuilder` in `src/Core/Sorcha.Blueprint.Fluent/ActionBuilder.cs`
- [x] T060 [US2] Modify `ActionExecutionService` to handle issued credential after engine execution: store in recipient's wallet via ICredentialStore, record issuance on ledger via transaction with credential metadata in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [x] T061 [US2] Add credential issuance metadata to transaction — include credential ID, type, issuer, recipient in transaction Metadata JSON field when recording issuance events in `src/Common/Sorcha.TransactionHandler/` (metadata helpers)

### Tests for User Story 2

- [x] T062 [P] [US2] Unit tests for `CredentialIssuer` — claim mapping from action data, DID URI generation, SD-JWT token creation, expiry calculation in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialIssuerTests.cs`
- [x] T063 [P] [US2] Unit tests for `CredentialIssuanceBuilder` fluent API in `tests/Sorcha.Blueprint.Fluent.Tests/Credentials/CredentialIssuanceBuilderTests.cs`
- [x] T064 [P] [US2] Unit tests for ActionProcessor Step 5 — issuance on credentialed action, no issuance on normal action in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/ActionProcessorIssuanceTests.cs`

**Checkpoint**: US2 complete — blueprint flows can issue credentials. Authority approves, recipient gets a signed credential in their wallet, issuance is on the ledger.

---

## Phase 5: User Story 3 — Compose Credential Flows Across Blueprints (Priority: P2)

**Goal**: Credentials issued by one blueprint flow are accepted as entry gates in a different blueprint flow. Verification is issuer-signature-based with no direct relationship required between blueprints.

**Independent Test**: Run Blueprint A (issues credential) then Blueprint B (requires that credential). Verify the credential from A is accepted by B.

**Dependencies**: Requires US1 (gating) + US2 (issuance) both complete.

### Implementation for User Story 3

- [x] T065 [US3] Add optional `registerId` to `CredentialIssuanceConfig` processing — when set, record issued credential as a transaction on the specified register (FR-014c) in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- [x] T066 [US3] Create example blueprint JSON: license-approval-template.json (3 actions, issues LicenseCredential) in `blueprints/license-approval-template.json`
- [x] T067 [P] [US3] Create example blueprint JSON: work-order-template.json (2 actions, requires LicenseCredential) in `blueprints/work-order-template.json`

### Tests for User Story 3

- [x] T068 [US3] Integration test: issue credential via simulated Blueprint A execution, then verify credential via simulated Blueprint B execution, asserting acceptance in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CrossBlueprintCredentialTests.cs`
- [x] T069 [P] [US3] Integration test: issue credential, then attempt cross-blueprint use with mismatched issuer — assert rejection in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CrossBlueprintCredentialTests.cs`

**Checkpoint**: US3 complete — the composable credential flow works. Output of one blueprint feeds into another as a trust chain.

---

## Phase 6: User Story 4 — Selective Disclosure of Credential Claims (Priority: P3)

**Goal**: Credential holders can present only a subset of claims when satisfying a credential requirement. Non-disclosed claims are cryptographically hidden.

**Independent Test**: Issue a credential with 5 claims, present with only 2 disclosed, verify acceptance and that undisclosed claims are not visible to the verifier.

### Implementation for User Story 4

- [x] T070 [US4] Implement `ISdJwtService.CreatePresentationAsync()` — takes full SD-JWT token and list of claims to disclose, returns SdJwtPresentation with only selected disclosures in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs`
- [x] T071 [US4] Implement `ISdJwtService.VerifyPresentationAsync()` — verifies partial disclosure presentation, extracts only disclosed claims, validates signature in `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs`
- [x] T072 [US4] Update `CredentialVerifier` to handle partial claim disclosure — verify only disclosed claims match requirements, ignore undisclosed claims in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs`
- [x] T073 [US4] Update `CredentialMatcher` to match against required claims only (not all stored claims) when checking if a credential satisfies a requirement in `src/Services/Sorcha.Wallet.Service/Credentials/CredentialMatcher.cs`

### Tests for User Story 4

- [x] T074 [P] [US4] Unit tests for SD-JWT selective disclosure: create token with 5 claims, present with 2, verify only 2 visible in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtSelectiveDisclosureTests.cs`
- [x] T075 [P] [US4] Unit tests for CredentialVerifier with partial disclosure: required claims present → accept, required claims missing → reject, claim value mismatch → reject in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialVerifierSelectiveDisclosureTests.cs`

**Checkpoint**: US4 complete — selective disclosure works. Holders reveal only what's needed.

---

## Phase 7: User Story 5 — Revoke a Previously Issued Credential (Priority: P3)

**Goal**: Issuing authorities can revoke credentials. Revoked credentials are rejected by all subsequent verification checks.

**Independent Test**: Issue a credential, verify it works, revoke it, verify it is rejected.

### Implementation for User Story 5

- [x] T076 [US5] Create revocation endpoint `POST /api/v1/credentials/{credentialId}/revoke` — validates caller is original issuer, records revocation on ledger, updates credential status in wallet store in `src/Services/Sorcha.Blueprint.Service/Endpoints/CredentialEndpoints.cs`
- [x] T077 [US5] Add YARP route for revocation endpoint (`/api/v1/credentials/**`) in `src/Services/Sorcha.ApiGateway/` YARP configuration
- [x] T078 [US5] Update `CredentialStore` with `UpdateStatusAsync(credentialId, status)` method for marking credentials as revoked in `src/Services/Sorcha.Wallet.Service/Credentials/CredentialStore.cs`
- [x] T079 [US5] Update `CredentialVerifier` to check revocation status — query credential status from wallet store or ledger, apply fail-closed or fail-open policy per `RevocationCheckPolicy` on the requirement in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs`
- [x] T080 [US5] Implement fail-open audit warning — when revocation status is unavailable and policy is FailOpen, add warning to `ActionExecutionResult.Warnings` and record audit entry in transaction metadata in `src/Core/Sorcha.Blueprint.Engine/Credentials/CredentialVerifier.cs`

### Tests for User Story 5

- [x] T081 [P] [US5] Unit tests for revocation endpoint: valid revocation by issuer → success, revocation by non-issuer → 403, already-revoked → idempotent in `tests/Sorcha.Blueprint.Service.Tests/Credentials/CredentialRevocationEndpointTests.cs`
- [x] T082 [P] [US5] Unit tests for CredentialVerifier revocation check: active → accept, revoked → reject, unavailable + fail-closed → block, unavailable + fail-open → accept with warning in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/CredentialVerifierRevocationTests.cs`

**Checkpoint**: US5 complete — full credential lifecycle works: issue → verify → revoke → reject.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, observability, and integration quality

- [x] T083 [P] Add OpenAPI/Scalar documentation (WithName, WithSummary, WithDescription) to all new wallet credential endpoints in `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`
- [x] T084 [P] Add OpenAPI/Scalar documentation to revocation endpoint in `src/Services/Sorcha.Blueprint.Service/Endpoints/CredentialEndpoints.cs`
- [x] T085 [P] Add structured logging (ILogger) to CredentialVerifier, CredentialIssuer, CredentialStore, and revocation endpoint — log credential operations with correlation IDs
- [x] T086 [P] Add XML documentation comments to all public interfaces and models in `Credentials/` directories across all projects
- [x] T087 Add health check for wallet credential store (PostgreSQL Credentials table accessible) in `src/Services/Sorcha.Wallet.Service/`
- [x] T088 Run quickstart.md validation — verify the license-approval and work-order blueprint examples from `specs/031-verifiable-credentials/quickstart.md` work against the implementation
- [x] T089 Update `docs/development-status.md` and `.specify/MASTER-TASKS.md` with credential system completion status

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup (T001 for SD-JWT library). BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (Phase 2) — No dependencies on other stories
- **US2 (Phase 4)**: Depends on Foundational (Phase 2) — Can run in parallel with US1
- **US3 (Phase 5)**: Depends on US1 + US2 — requires both gating and issuance
- **US4 (Phase 6)**: Depends on Foundational (Phase 2) — Can start after foundational, independent of other stories
- **US5 (Phase 7)**: Depends on Foundational (Phase 2) — Can start after foundational, but US1 recommended first
- **Polish (Phase 8)**: Depends on all user stories complete

### User Story Dependencies

```
Phase 1: Setup
    ↓
Phase 2: Foundational (BLOCKS ALL)
    ↓
    ├──▶ Phase 3: US1 (Gate Actions) ──────────────┐
    ├──▶ Phase 4: US2 (Issue Credentials) ──────────┤
    ├──▶ Phase 6: US4 (Selective Disclosure) ───┐   │
    └──▶ Phase 7: US5 (Revocation) ───────────┐ │   │
                                               │ │   │
                                               ▼ ▼   ▼
                                    Phase 5: US3 (Compose Flows)
                                               │
                                               ▼
                                    Phase 8: Polish
```

### Within Each User Story

- Models before services
- Interfaces before implementations
- Engine changes before service-layer changes
- Core implementation before endpoints
- Implementation before tests (tests validate completed work)

### Parallel Opportunities

- **Phase 2**: T006–T013 (all models) can run in parallel; T019–T021 (SD-JWT models) can run in parallel; T024–T026 (validators) can run in parallel
- **Phase 3**: T034 (fluent builder) and T036–T040 (wallet store) can run in parallel with T030–T032 (engine)
- **Phase 4**: T058 (fluent builder) can run in parallel with T054–T055 (engine issuer)
- **US1 and US2**: Can run in parallel after Phase 2 if staffed by different developers
- **US4 and US5**: Can run in parallel after Phase 2

---

## Parallel Example: Phase 2 (Foundational Models)

```
# All model files can be created simultaneously:
T006: RevocationCheckPolicy.cs
T007: ClaimConstraint.cs
T008: CredentialRequirement.cs
T009: ClaimMapping.cs
T010: CredentialIssuanceConfig.cs
T011: CredentialPresentation.cs
T012: CredentialValidationError.cs
T013: CredentialRevocation.cs
T019: SdJwtToken.cs
T020: SdJwtPresentation.cs
T021: SdJwtVerificationResult.cs

# Validators (parallel with each other, after their model):
T024: CredentialRequirementValidator.cs
T025: CredentialIssuanceConfigValidator.cs
T026: CredentialPresentationValidator.cs

# Then sequentially:
T014: Modify Action.cs (depends on T008, T010)
T015: Modify ExecutionContext.cs (depends on T011)
T018: ISdJwtService.cs (depends on T019-T021)
T022: SdJwtService.cs (depends on T018)
```

## Parallel Example: US1 + US2 Concurrent

```
# Developer A (US1 - Gating):
T030 → T031 → T032 → T044 → T045
T034 → T035 (parallel with above)
T036 → T037 → T038 → T039 → T040 → T041 → T042 → T043
T046 → T047 → T048 (UI, after endpoints ready)

# Developer B (US2 - Issuance):
T054 → T055 → T056 → T060 → T061
T058 → T059 (parallel with above)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T005)
2. Complete Phase 2: Foundational (T006–T029)
3. Complete Phase 3: User Story 1 (T030–T053)
4. **STOP and VALIDATE**: Create a blueprint with credential requirements, attempt action with/without credentials
5. Deploy/demo — credential gating is immediately useful

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. Add US1 (Gate Actions) → Test independently → **MVP!**
3. Add US2 (Issue Credentials) → Test independently → Credential lifecycle
4. Add US3 (Compose Flows) → Test independently → Full composability
5. Add US4 (Selective Disclosure) → Test independently → Privacy layer
6. Add US5 (Revocation) → Test independently → Trust completeness
7. Polish → Production-ready

### Suggested MVP Scope

**US1 only (Phase 3)** delivers immediate value: blueprint designers can gate actions on credentials, the wallet stores and matches credentials, and participants see clear errors when credentials are missing. This enables the core use case without requiring the full issuance/revocation lifecycle (credentials can be manually provisioned for testing).

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story is independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- SD-JWT library decision (T001) is the earliest risk — if HeroSD-JWT doesn't work, custom implementation adds ~3 tasks to Phase 2
- Existing `Participant.VerifiableCredential` and `Participant.DidUri` properties are already in the codebase — implementation should build on these
- All credential models use file-scoped namespaces and require SPDX license header per project conventions
