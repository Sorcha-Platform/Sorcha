---

description: "Task list for spec 095: IETF Token Status List (Parallel to W3C)"
---

# Tasks: IETF Token Status List (Parallel to W3C)

**Input**: Design documents from `specs/095-ietf-token-status-list/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — spec FR-024 mandates unit + integration coverage.

## Phase 1: Setup

- [ ] T001 Confirm `095-ietf-token-status-list` branch is rebased onto master with spec 093 merged. Spec 094 does not need to be merged first — the `IHaipIssuerCoKeyService` interface stub can be mocked in tests, and the real implementation is wired when 094 lands
- [ ] T002 Verify `System.IO.Compression.ZLibStream` is usable in the Blueprint Service target framework (.NET 10 — yes, BCL)
- [ ] T003 [P] Add a config key `StatusList:IetfBaseUrl` to `src/Services/Sorcha.Blueprint.Service/appsettings.json` (defaults to the same base URL as the W3C endpoint with a different path segment)

## Phase 2: Foundational

- [ ] T004 Baseline: run targeted tests on `tests/Sorcha.Blueprint.Service.Tests/Services/` and `tests/Sorcha.Wallet.Service.Tests/Presentations/` to establish the pre-change green baseline
- [ ] T005 Extract `GetRawBitstringBytesAsync(listId, ct)` accessor on `IStatusListManager` in `src/Services/Sorcha.Blueprint.Service/Services/StatusListManager.cs`. Internal refactor — no behaviour change
- [ ] T006 Refactor the existing W3C endpoint handler at `StatusListEndpoints.cs:57-93` to use the new accessor and gzip the raw bytes locally. Confirm the on-the-wire output is byte-identical before and after

## Phase 3: User Story 1 - IETF Token Status List endpoint (Priority: P1)

**Goal**: A HAIP-conformant verifier can fetch `/api/v1/credentials/ietf-status-lists/{listId}` and read a signed JWT with `status_list: { bits, lst }`.

### Tests for US1

- [ ] T007 [P] [US1] Write failing unit test `Serialize_ReturnsSignedJwt_WithStatusListPayload` in `tests/Sorcha.Blueprint.Service.Tests/Services/IetfTokenStatusListSerializerTests.cs`
- [ ] T008 [P] [US1] Write failing unit test `Serialize_JwtHeader_HasStatuslistPlusJwtType` in the same file
- [ ] T009 [P] [US1] Write failing unit test `Serialize_LstField_IsZlibCompressedBase64Url` in the same file
- [ ] T010 [P] [US1] Write failing unit test `Endpoint_ReturnsContentTypeStatuslistPlusJwt` in `tests/Sorcha.Blueprint.Service.IntegrationTests/` (or equivalent integration test home)
- [ ] T011 [P] [US1] Write failing unit test `Endpoint_RespectsCacheControlHeader` in the same file

### Implementation

- [ ] T012 [US1] Create `src/Services/Sorcha.Blueprint.Service/Services/IetfTokenStatusListSerializer.cs` implementing `IIetfTokenStatusListSerializer` per contracts/README.md
- [ ] T013 [US1] Implement the zlib compression step using `System.IO.Compression.ZLibStream`
- [ ] T014 [US1] Implement JWT construction: header + payload + base64url-encoded signing input + signature from the supplied signing delegate
- [ ] T015 [US1] Register `IIetfTokenStatusListSerializer` in Blueprint Service DI wiring
- [ ] T016 [US1] Add new endpoint handler `GetIetfStatusList` in `src/Services/Sorcha.Blueprint.Service/Endpoints/StatusListEndpoints.cs`, route `GET /api/v1/credentials/ietf-status-lists/{listId}`, anonymous, `AllowAnonymous()`, `Content-Type: application/statuslist+jwt`
- [ ] T017 [US1] Wire the endpoint to call the serialiser, return the JWT as `Results.Text` with the custom content type, apply the same `CachedResult` wrapper used by the W3C endpoint

## Phase 4: User Story 2 - Byte-identity between W3C and IETF decompressed bytes (Priority: P1)

### Tests for US2

- [ ] T018 [P] [US2] Write failing unit test `DualEnvelope_EmptyList_DecompressedBytesMatch` in `tests/Sorcha.Blueprint.Service.Tests/Services/StatusListDualEnvelopeIdentityTests.cs`
- [ ] T019 [P] [US2] Write failing unit test `DualEnvelope_AfterAllocation_DecompressedBytesMatch` in the same file
- [ ] T020 [P] [US2] Write failing unit test `DualEnvelope_AfterRevocation_DecompressedBytesMatch` in the same file
- [ ] T021 [P] [US2] Write failing unit test `DualEnvelope_AfterReinstate_DecompressedBytesMatch` in the same file
- [ ] T022 [P] [US2] Write failing unit test `DualEnvelope_MassAllocationToCapacity_DecompressedBytesMatch` in the same file

### Implementation

No additional implementation — byte-identity is a consequence of the Phase 2 refactor (T005, T006) combined with the Phase 3 serializer. US2 is a test-only phase that asserts the invariant.

## Phase 5: User Story 3 - `status.status_list` credential claim on HAIP-path issuance (Priority: P1)

### Tests for US3

- [ ] T023 [P] [US3] Write failing unit test `IssueCredential_WithIetfClaimForm_EmbedsStatusStatusListClaim` in `tests/Sorcha.Wallet.Service.Tests/Endpoints/CredentialEndpointsIetfStatusTests.cs` (create file)
- [ ] T024 [P] [US3] Write failing unit test `IssueCredential_WithW3cClaimForm_EmbedsCredentialStatusClaim_Unchanged` in the same file (regression against spec 093)
- [ ] T025 [P] [US3] Write failing unit test `IssueCredential_DefaultClaimForm_IsW3c` in the same file

### Implementation

- [ ] T026 [US3] Add `StatusClaimForm` enum (`W3cBitstringStatusListEntry`, `IetfTokenStatusList`) in `src/Services/Sorcha.Wallet.Service/Models/StatusClaimForm.cs`
- [ ] T027 [US3] Add optional `StatusClaimForm` field to `IssueCredentialRequest` in `CredentialEndpoints.cs`
- [ ] T028 [US3] In `CredentialEndpoints.IssueCredential`, branch on `request.StatusClaimForm`: W3C path builds the existing `credentialStatus` claim (spec 093 behaviour), IETF path builds a `status.status_list` object instead and adds it under the `status` top-level claim
- [ ] T029 [US3] Update `IWalletServiceClient.IssueCredentialAsync` to forward the new field through its HTTP body
- [ ] T030 [US3] Update `WalletServiceClient.IssueCredentialAsync` implementation accordingly

## Phase 6: User Story 4 - Presentation verifier reads either claim form (Priority: P1)

### Tests for US4

- [ ] T031 [P] [US4] Write failing unit test `Verifier_PrefersIetfStatusClaim_OverW3cCredentialStatus` in `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs` (new test method, extending the existing file)
- [ ] T032 [P] [US4] Write failing unit test `Verifier_FallsBackToW3cClaim_WhenIetfClaimAbsent` in the same file
- [ ] T033 [P] [US4] Write failing unit test `Verifier_FallsBackToServerSideRow_WhenBothClaimsAbsent` in the same file (spec 093 FR-010 regression)
- [ ] T034 [P] [US4] Write failing unit test `Verifier_FetchesIetfEnvelope_VerifiesSignature_ReadsBit` in the same file (mock HttpClient)

### Implementation

- [ ] T035 [US4] Add `TryExtractIetfStatusList` helper to `PresentationRequestService.cs` mirroring the existing `TryExtractEmbeddedCredentialStatus` (spec 093)
- [ ] T036 [US4] In `VerifyPresentationAsync`, extend the status-check section to try IETF first, then W3C, then the server-side row. Log which path was taken
- [ ] T037 [US4] Add a new helper `CheckIetfStatusListAsync(uri, idx, ct)` that fetches the IETF endpoint, verifies the JWT envelope signature (against the list issuer's resolved DID), decompresses the zlib `lst`, and reads the bit at `idx`
- [ ] T038 [US4] Update the `VerificationResult.StatusListCheck` string to distinguish W3C vs IETF source for diagnostics

## Phase 7: Polish

- [ ] T039 Run full `tests/Sorcha.Blueprint.Service.Tests/Services/StatusList*` suite
- [ ] T040 Run full `tests/Sorcha.Wallet.Service.Tests/Presentations/` suite
- [ ] T041 Run the quickstart.md manual verification procedure
- [ ] T042 [P] Update `docs/reference/API-DOCUMENTATION.md` with the new IETF endpoint path
- [ ] T043 [P] Update `src/Services/Sorcha.Blueprint.Service/README.md` with a note on the parallel-envelope design

## Dependencies

- Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7
- US1 (IETF endpoint) must land before US2 (byte-identity tests)
- US3 (claim form issuance) is independent of US1/US2 and can run in parallel
- US4 (verifier consumer) depends on US3's claim-form embedding existing for end-to-end tests, but the helper and unit tests can be written against mocked tokens independently

## Parallel opportunities

- Phase 3 test tasks T007-T011 parallel
- Phase 4 test tasks T018-T022 parallel
- Phase 5 test tasks T023-T025 parallel
- Phase 6 test tasks T031-T034 parallel
- Phase 7 T042-T043 parallel

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T003 | 3 |
| Phase 2 Foundational refactor | T004-T006 | 3 |
| Phase 3 US1 IETF endpoint | T007-T017 | 11 |
| Phase 4 US2 byte-identity | T018-T022 | 5 |
| Phase 5 US3 claim form issuance | T023-T030 | 8 |
| Phase 6 US4 verifier consumer | T031-T038 | 8 |
| Phase 7 Polish | T039-T043 | 5 |
| **Total** | | **43** |

**Suggested MVP**: Phase 1 + 2 + 3 + 4 = 22 tasks. Ships the IETF endpoint with byte-identity guarantees; claim-form issuance and verifier consumer can follow as a separate increment.
