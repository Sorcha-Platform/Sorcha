---

description: "Task list for spec 098: OpenID4VP Verifier Endpoint (HAIP)"
---

# Tasks: OpenID4VP Verifier Endpoint (HAIP)

**Input**: Design documents from `specs/098-openid4vp-verifier/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — spec mandates unit + integration coverage.

## Phase 1: Setup

- [ ] T001 Confirm `098-openid4vp-verifier` branch is rebased onto master with specs 093-097 all merged
- [ ] T002 Verify `Sorcha.Haip.Service` builds and its 21 existing tests pass
- [ ] T003 [P] Add `PresentationSource` enum (`SorchaInternal`, `HaipExternalWallet`) to `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs`

## Phase 2: Foundational

- [ ] T004 Create `src/Services/Sorcha.Haip.Service/Models/VerifierModels.cs` with PresentationRequest, AuthorizationRequestPayload, PresentationSubmission, VerificationResult entities per data-model.md
- [ ] T005 [P] Create `src/Services/Sorcha.Haip.Service/Services/PresentationRequestStore.cs` — Redis-backed store with `CreateAsync`, `GetAsync`, `MarkCompletedAsync` (TTL-based expiry, ConcurrentDictionary fallback)
- [ ] T006 Baseline: run `dotnet test tests/Sorcha.Haip.Service.Tests` to confirm 21 existing tests still pass

## Phase 3: User Story 2 — Authorization Request creation (Priority: P1) 🎯 MVP

**Goal**: Blueprint Service can create a Presentation Request and receive an Authorization Request URI for QR rendering.

### Tests for US2

- [ ] T007 [P] [US2] Write failing test `CreateRequest_ReturnsRequestUri` in `tests/Sorcha.Haip.Service.Tests/Endpoints/VerifierEndpointTests.cs`
- [ ] T008 [P] [US2] Write failing test `GetRequestObject_ReturnsSignedJwt` in the same file
- [ ] T009 [P] [US2] Write failing test `GetRequestObject_ExpiredRequest_Returns410` in the same file
- [ ] T010 [P] [US2] Write failing test `RequestStore_Create_StoresWithTtl` in `tests/Sorcha.Haip.Service.Tests/Services/PresentationRequestStoreTests.cs`
- [ ] T011 [P] [US2] Write failing test `RequestStore_MarkCompleted_UpdatesState` in the same file

### Implementation

- [ ] T012 [US2] Create `src/Services/Sorcha.Haip.Service/Services/PresentationRequestManager.cs` — creates PresentationRequest with nonce, builds the signed Request Object JWT (response_type=vp_token, response_mode=direct_post, presentation_definition, request_uri, client_id)
- [ ] T013 [US2] Create `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs` with:
  - `POST /api/v1/verifier/requests` (internal, auth required) — creates a presentation request
  - `GET /api/v1/verifier/requests/{requestId}/request-object` (public) — serves the signed Request Object JWT
- [ ] T014 [US2] Register PresentationRequestManager and PresentationRequestStore in Program.cs, map verifier endpoints

## Phase 4: User Story 1 — direct_post submission and verification (Priority: P1)

**Goal**: A HAIP wallet can submit a vp_token via direct_post and Sorcha verifies it end-to-end.

### Tests for US1

- [ ] T015 [P] [US1] Write failing test `DirectPost_ValidPresentation_ReturnsVerified` in `tests/Sorcha.Haip.Service.Tests/Endpoints/VerifierEndpointTests.cs`
- [ ] T016 [P] [US1] Write failing test `DirectPost_InvalidNonce_RejectsWithNonceMismatch` in the same file
- [ ] T017 [P] [US1] Write failing test `DirectPost_ExpiredRequest_Returns410` in the same file
- [ ] T018 [P] [US1] Write failing test `GetResult_AfterVerification_ReturnsVerifiedClaims` in the same file
- [ ] T019 [P] [US1] Write failing test `HaipVerifier_ValidatesKbJwt_AgainstCnf` in `tests/Sorcha.Haip.Service.Tests/Services/HaipPresentationVerifierTests.cs`
- [ ] T020 [P] [US1] Write failing test `HaipVerifier_InvalidKbJwt_RejectsWithBindingError` in the same file

### Implementation

- [ ] T021 [US1] Create `src/Services/Sorcha.Haip.Service/Services/HaipPresentationVerifier.cs` — orchestrates the verification pipeline: parse vp_token → verify issuer signature (SdJwtService) → validate KB-JWT against cnf → check IETF/W3C status → match claims against presentation_definition
- [ ] T022 [US1] Add `POST /api/v1/verifier/requests/{requestId}/direct-post` endpoint in VerifierEndpoints.cs — accepts form-encoded vp_token + presentation_submission, validates nonce, calls HaipPresentationVerifier, stores result
- [ ] T023 [US1] Add `GET /api/v1/verifier/requests/{requestId}/result` endpoint (internal, auth required) — returns VerificationResult

## Phase 5: Blueprint integration (Priority: P1)

### Tests for US3

- [ ] T024 [P] [US3] Write failing test `CredentialRequirement_WithHaipSource_RoutesToVerifier` in `tests/Sorcha.Haip.Service.Tests/Services/PresentationRequestManagerTests.cs`

### Implementation

- [ ] T025 [US3] Add `ForExternalWallet()` method to the Blueprint fluent API for credential requirements (mirrors issuance-side `ForExternalWallet` from spec 097)
- [ ] T026 [US3] In ActionExecutionService, extend the credential requirement check: when `PresentationSource == HaipExternalWallet`, call the HAIP verifier service to create a Presentation Request instead of matching against internal credentials, and suspend the action in `AwaitingExternalPresentation` state

## Phase 6: Polish

- [ ] T027 Run `dotnet test tests/Sorcha.Haip.Service.Tests` — confirm all tests pass
- [ ] T028 Run `dotnet test tests/Sorcha.Cryptography.Tests` — confirm no SD-JWT regression
- [ ] T029 [P] Update `src/Services/Sorcha.Haip.Service/README.md` with verifier endpoint documentation
- [ ] T030 [P] Update `docs/reference/API-DOCUMENTATION.md` with the verifier endpoint paths

## Dependencies

- Phase 1 → Phase 2 → Phase 3 (request creation MVP) → Phase 4 (direct_post + verification) → Phase 5 (Blueprint integration) → Phase 6
- Phase 3 is independent of Phase 4 — request creation doesn't need the verifier
- Phase 4 depends on Phase 3 (needs request objects to exist)
- Phase 5 depends on Phase 4 (needs verification results before Blueprint can consume them)

## Parallel opportunities

- Phase 1: T003 independent
- Phase 2: T004, T005 parallel
- Phase 3: T007-T011 parallel (test files)
- Phase 4: T015-T020 parallel (test files)
- Phase 6: T029-T030 parallel

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T003 | 3 |
| Phase 2 Foundational | T004-T006 | 3 |
| Phase 3 US2 Request creation | T007-T014 | 8 |
| Phase 4 US1 Verification | T015-T023 | 9 |
| Phase 5 US3 Blueprint integration | T024-T026 | 3 |
| Phase 6 Polish | T027-T030 | 4 |
| **Total** | | **30** |

**Suggested MVP**: Phase 1 + 2 + 3 = 14 tasks. Ships the request creation and request object serving — proves the verifier side of the HAIP boundary works and wallets can fetch presentation definitions.
