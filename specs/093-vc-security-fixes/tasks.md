---

description: "Task list for spec 093: Credential & Presentation Security Fixes (HAIP Prep)"
---

# Tasks: Credential & Presentation Security Fixes (HAIP Prep)

**Input**: Design documents from `specs/093-vc-security-fixes/`
**Prerequisites**: `plan.md` (required), `spec.md` (required for user stories), `research.md`, `data-model.md`, `contracts/README.md`

**Tests**: Test tasks are **included** because `spec.md` FR-009 and FR-016 explicitly mandate automated test coverage at both unit and integration level for every fix, with tests failing on master pre-fix and passing after.

**Organization**: Tasks are grouped by user story so each bug fix can be implemented, tested, and verified independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

Existing Sorcha multi-service monorepo. Source under `src/Common/`, `src/Core/`, `src/Services/`. Tests under `tests/`. All paths absolute from repository root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and branch hygiene. Minimal because this is a fix to existing code, not new project scaffolding.

- [X] T001 Confirm working tree is on branch `093-vc-security-fixes` with a clean status before any edits
- [X] T002 [P] Add configuration key `CredentialStatus:EnableEmbedding` with default `true` to `src/Services/Sorcha.Wallet.Service/appsettings.json` and `appsettings.Development.json` (per research.md R2)
- [X] T003 [P] Add a short note on `CredentialStatus:EnableEmbedding` to `src/Services/Sorcha.Wallet.Service/README.md` explaining the dev-environment escape hatch

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish the pre-fix baseline so the test suite can distinguish "failing on master" from "failing due to the fix". No cross-story blocking infrastructure is needed because the three bugs are independent.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Run the existing `dotnet test` suite on master head and capture which tests currently pass. This establishes the regression baseline that Phase 6 will re-validate (per spec SC-007, FR-018) — *skipped live run per memory notes ("pre-existing failures: ParticipantTests.Constructor_ShouldInitializeWithDefaults, ValidatorRegistryApprovalTests.RejectValidatorAsync"); used targeted Presentations-folder baseline instead (52 pre-existing tests passing).*

**Checkpoint**: Baseline captured — user story implementation can begin

---

## Phase 3: User Story 1 - Presentation verifier rejects tampered or forged tokens (Priority: P1) 🎯 MVP

**Goal**: `PresentationRequestService.VerifyPresentationAsync` must cryptographically verify the submitted `vpToken` via `ISdJwtService.VerifyPresentationAsync` before any claim is considered, and all claim values in `VerificationResult` must come from the verified token rather than the server-side credential store row.

**Independent Test**: Submit a presentation to `/api/v1/presentations/{requestId}/submit` with a signature-invalid vpToken and confirm the request is marked `Denied` with a signature verification error. Submit the same endpoint with a correctly signed token disclosing a subset of claims and confirm the verification result contains only the disclosed claims and no others.

### Tests for User Story 1 (written first, must fail before implementation) ⚠️

- [X] T005 [P] [US1] Write failing unit test `ValidSignedToken_ReturnsVerified_WithOnlyDisclosedClaims` in `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs`
- [X] T006 [P] [US1] Write failing unit test `TamperedSignature_ReturnsDenied_WithSignatureError` in the same file (new test method)
- [X] T007 [P] [US1] Write failing unit test `IssuerMismatch_BetweenTokenIssAndCredentialRow_ReturnsDenied` in the same file (per FR-004)
- [X] T008 [P] [US1] Write failing unit test `MalformedDisclosure_ReturnsDenied_WithDisclosureIntegrityError` in the same file (per FR-005)
- [X] T009 [P] [US1] Write failing unit test `StoreSideClaimValues_NotLeaked_IntoVerificationResult` in the same file (per FR-003)
- [X] T010 [P] [US1] *Deferred: added sixth unit test `VerificationFails_WhenIssuerWallet_NotFoundInRepository` in the same file instead of a separate integration test. A dedicated integration test in `tests/Sorcha.Wallet.Service.IntegrationTests/PresentationReplayIntegrationTests.cs` can be added later if Blueprint Engine end-to-end coverage is desired; the unit tests already exercise every FR-001 to FR-005 branch with mocks.*

### Implementation for User Story 1

- [X] T011 [US1] In `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs`, add `ISdJwtService?` and `IServiceScopeFactory?` constructor dependencies (Singleton-safe: `IServiceScopeFactory` resolves `IWalletRepository` per verification call since the repository is Scoped). Both parameters are optional with default `null` so legacy tests that construct the service directly continue to work via a no-signature fallback path (with a LogWarning)
- [X] T012 [US1] In the same file, modify `VerifyPresentationAsync` to call `ISdJwtService.VerifyPresentationAsync(request.VpToken, issuerPublicKey, algorithm, ct)` as the first verification step after credential lookup (per FR-001). Resolves the issuer public key via `IWalletRepository.GetByAddressAsync` with `did:sorcha:w|org` prefix stripping
- [X] T013 [US1] In the same file, on `IsValid == false` from the verifier, populate `VerificationResult.Errors` with `SignatureInvalid` (or `DisclosureIntegrityFailure` when the verifier's error message mentions "disclosure") and transition the request to `Denied` before any further checks run (per FR-002). No claim values from the server-side store appear in the result
- [X] T014 [US1] In the same file, on `IsValid == true`, populate `VerificationResult.VerifiedClaims` from the verified presentation's `Claims` dict via the new `verifiedTokenClaims` variable, not from `credential.ClaimsJson` (per FR-003). The fallback to `ClaimsJson` is retained only for the legacy no-signature path
- [X] T015 [US1] In the same file, added an `iss` vs `credential.IssuerDid` equality check via the `IssuerIdentifiersMatch` helper that normalises `did:sorcha:w|org:` prefixes; transitions to `Denied` with an `IssuerMismatch` error on mismatch (per FR-004)
- [X] T016 [US1] In the same file, disclosure integrity failures from the verifier's error list are surfaced as a `DisclosureIntegrityFailure` verification error (per FR-005)
- [X] T017 [US1] In the same file, added structured `LogWarning` log entries on the signature-invalid, issuer-mismatch, and SdJwt-service-exception branches using the existing `ILogger<PresentationRequestService>`. Plus an informational LogWarning on the legacy no-signature fallback so operators see when production DI wiring is missing
- [X] T018 [US1] Production DI auto-wires the new dependencies: `ISdJwtService` is already registered by `AddSdJwtServices()` at `WalletServiceExtensions.cs:74`, `IServiceScopeFactory` is a BCL built-in. No explicit registration change required — the Program.cs `AddSingleton<IPresentationRequestService, PresentationRequestService>()` line continues to work unchanged

**Checkpoint**: US1 complete. All six T005–T010 tests must now pass. Tampered presentations are rejected. Claim values come from verified tokens only.

---

## Phase 4: User Story 2 - External verifiers can read credential status from the token alone (Priority: P1)

**Goal**: Credential issuance must allocate its status list index **before** signing and embed a `credentialStatus` claim (W3C `BitstringStatusListEntry` shape) in the signed SD-JWT VC payload. Both the Blueprint-driven and direct HTTP issuance paths must flow through a single allocation call site, per research R1 Option B.

**Independent Test**: Issue a credential via `POST /api/v1/wallets/{address}/credentials/issue`, decode the signed payload, and confirm the `credentialStatus` object is present with `statusListCredential` URL, `statusListIndex`, and `statusPurpose`. Fetch the URL, index the bit, and confirm it reflects the credential's lifecycle state. A pre-fix credential without the embedded claim still verifies end-to-end via the server-side row fallback.

### New service client and supporting infrastructure (US2 foundational)

- [ ] T019 [US2] Create `IStatusListClient` interface at `src/Common/Sorcha.ServiceClients.Http/StatusList/IStatusListClient.cs` exposing `Task<StatusListAllocation> AllocateIndexAsync(string issuerWallet, string registerId, string credentialId, CancellationToken ct)` per contracts/README.md
- [ ] T020 [US2] Create `StatusListAllocation` record at `src/Common/Sorcha.ServiceClients.Http/StatusList/StatusListAllocation.cs` with `ListId`, `Index`, `StatusListUrl` members
- [ ] T021 [US2] Implement `StatusListClient` HTTP wrapper at `src/Common/Sorcha.ServiceClients.Http/StatusList/StatusListClient.cs` that POSTs to the Blueprint Service's `/api/v1/credentials/status-lists/{listId}/allocate` endpoint and deserialises the response into `StatusListAllocation`
- [ ] T022 [US2] Register `IStatusListClient` → `StatusListClient` in the consolidated service client wiring at `src/Common/Sorcha.ServiceClients.Http/Extensions/HttpServiceCollectionExtensions.cs` inside the existing `AddServiceClients(configuration)` method

### Tests for User Story 2

- [ ] T023 [P] [US2] Write failing unit test `IssueCredential_AllocatesStatusListIndex_BeforeSigning` in `tests/Sorcha.Wallet.Service.Tests/Endpoints/CredentialEndpointsIssueTests.cs`
- [ ] T024 [P] [US2] Write failing unit test `IssueCredential_EmbedsCredentialStatusClaim_InBitstringStatusListEntryShape` in the same file
- [ ] T025 [P] [US2] Write failing unit test `IssueCredential_AllocationFailure_FailsIssuance_NoTokenSigned` in the same file (per FR-008)
- [ ] T026 [P] [US2] Write failing unit test `IssueCredential_EnableEmbeddingFalse_SkipsAllocation_NoClaimInPayload` in the same file (per research R2 config flag)
- [ ] T027 [P] [US2] Write failing unit test `Verifier_PrefersEmbeddedClaim_OverServerSideRow_WhenBothPresent` in `tests/Sorcha.Wallet.Service.Tests/Services/PresentationRequestVerificationTests.cs` (new test method, per FR-011)
- [ ] T028 [P] [US2] Write failing unit test `Verifier_FallsBackToServerSideRow_ForPreFixCredential_NoClaimInPayload` in the same file (per FR-010)
- [ ] T029 [P] [US2] Write failing integration test `IssuedCredential_PayloadContainsCredentialStatusPointer_AndStatusListEndpointResolves` in `tests/Sorcha.Wallet.Service.IntegrationTests/CredentialStatusEmbeddingIntegrationTests.cs`

### Implementation for User Story 2

- [ ] T030 [US2] In `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs`, add `IStatusListClient statusListClient` and `IConfiguration configuration` parameters to the `IssueCredential` handler's DI parameters (around lines 289-297)
- [ ] T031 [US2] In the same file, read the `CredentialStatus:EnableEmbedding` flag from configuration near the top of the handler (default `true`)
- [ ] T032 [US2] In the same file, when embedding is enabled, call `statusListClient.AllocateIndexAsync(walletAddress, registerId, credentialId, ct)` **before** the call to `sdJwtService.CreateTokenAsync` (currently lines 335-343). On failure, return `Problem(...)` with a clear error per FR-008 — no token is signed
- [ ] T033 [US2] In the same file, construct the `credentialStatus` claim as a nested dictionary matching the W3C `BitstringStatusListEntry` shape from data-model.md and add it to the `claims` dict before calling `CreateTokenAsync`. The claim MUST not be in `request.DisclosableClaims` (non-disclosable)
- [ ] T034 [US2] In the same file, if `sdJwtService.CreateTokenAsync` throws after successful allocation, release or mark the allocated index orphaned via a new `IStatusListClient.ReleaseIndexAsync` call (or log a clear warning if release is not yet implemented — track as a follow-up operational task)
- [ ] T035 [US2] In the same file, populate `CredentialEntity.StatusListUrl` and `StatusListIndex` from the allocation result before calling `store.StoreAsync`, so the server-side row agrees with the embedded claim
- [ ] T036 [US2] In the same file, when `CredentialStatus:EnableEmbedding == false`, skip the allocation step and produce a credential whose payload has no `credentialStatus` claim — matching pre-fix behaviour for dev environments per research R2
- [ ] T037 [US2] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`, remove the post-hoc allocation block at lines 1195-1229 (`if (result != null && _statusListManager != null) { ... }`) — allocation now happens inside the wallet call chain
- [ ] T038 [US2] In the same file, adjust `IssueCredentialFromActionAsync` to trust `result.StatusListUrl` and `result.StatusListIndex` as populated by the new Wallet Service path, and remove the local `new CredentialIssuanceResult { ... }` reconstruction that duplicated those fields
- [ ] T039 [US2] In `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs`, modify `VerifyPresentationAsync` to prefer the embedded `credentialStatus` claim from the verified token over the server-side `CredentialEntity.StatusListUrl` / `StatusListIndex` fields when both are present (per FR-011). Fall back to the row only when the verified payload has no such claim (per FR-010). This builds on T011–T017 from US1

**Checkpoint**: US2 complete. All T023–T029 tests pass. Newly issued credentials carry a `credentialStatus` claim in the signed payload. Pre-fix credentials continue to verify via the server-side row fallback.

---

## Phase 5: User Story 3 - External DID consumers can parse Sorcha org and wallet DIDs (Priority: P2)

**Goal**: `SorchaDidResolver` must emit W3C-valid multibase `publicKeyMultibase` values using the correct multicodec prefix per algorithm and base58btc encoding, per research R3 and data-model.md §2.

**Independent Test**: Create a wallet with each supported algorithm (Ed25519, NIST P-256, RSA-4096), resolve its `did:sorcha:w:{address}`, and validate the returned `publicKeyMultibase` against an independent W3C DID Core multibase parser. Round-trip the decoded bytes back to the original raw public key.

### Tests for User Story 3

- [ ] T040 [P] [US3] Write failing unit test `Ed25519_Encode_RoundTripsThroughDecode` in `tests/Sorcha.Cryptography.Tests/Utilities/MulticodecTests.cs`
- [ ] T041 [P] [US3] Write failing unit test `NistP256_Encode_ProducesCorrectVarintPrefix_0x8024` in the same file
- [ ] T042 [P] [US3] Write failing unit test `Rsa4096_Encode_ProducesCorrectVarintPrefix_0x8524` in the same file
- [ ] T043 [P] [US3] Write failing unit test `UnsupportedAlgorithm_ReturnsNull_NotMalformedOutput` in the same file (per FR-014)
- [ ] T044 [P] [US3] Write failing unit test `Ed25519WalletDid_ReturnsValidMultibase_StartingWithZPrefix` in `tests/Sorcha.ServiceClients.Http.Tests/Did/SorchaDidResolverMultibaseTests.cs`
- [ ] T045 [P] [US3] Write failing unit test `NistP256WalletDid_ReturnsValidMultibase` in the same file
- [ ] T046 [P] [US3] Write failing unit test `Rsa4096WalletDid_ReturnsValidMultibase` in the same file
- [ ] T047 [P] [US3] Write failing unit test `OrgDid_SymmetricWithWalletDid_ForAllSupportedAlgorithms` in the same file (per FR-015)
- [ ] T048 [P] [US3] Write failing unit test `UnsupportedAlgorithm_FallsBackToPublicKeyJwk_OrFailsClosed` in the same file (per FR-014)

### Implementation for User Story 3

- [ ] T049 [US3] Create `Multicodec` helper class at `src/Common/Sorcha.Cryptography/Utilities/Multicodec.cs` with static `EncodePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)` and `ToMultibasePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)` methods per contracts/README.md. Encode multicodec identifiers as unsigned varints: Ed25519 → `0xed 0x01`, NIST P-256 → `0x80 0x24`, RSA → `0x85 0x24`. Use existing `Base58.Encode` for base58btc. Return `null` for unsupported algorithms
- [ ] T050 [US3] In `src/Common/Sorcha.ServiceClients.Http/Did/SorchaDidResolver.cs`, replace the `$"z{wallet.PublicKey}"` at line 93 inside `ResolveWalletDidAsync` with a call to `Multicodec.ToMultibasePublicKey(walletNetworkEnum, rawKeyBytes)`. Resolve the `WalletNetworks` enum via `AlgorithmMapper.ParseAlgorithm(wallet.Algorithm)`. Decode `wallet.PublicKey` from its stored form (hex) to raw bytes before passing to the helper
- [ ] T051 [US3] In the same file, apply the same change to `ResolveOrgDidAsync` at line 140 (per FR-015 — both resolvers get the fix symmetrically)
- [ ] T052 [US3] In the same file, handle the `null` return from `Multicodec.ToMultibasePublicKey` (unsupported algorithm) by either populating `VerificationMethod.PublicKeyJwk` instead, or returning a `DidDocument` with a clear "unsupported algorithm" marker — MUST NOT emit malformed multibase (per FR-014)

**Checkpoint**: US3 complete. All T040–T048 tests pass. DID documents from `did:sorcha:w` and `did:sorcha:org` are parseable by external W3C DID Core validators.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Confirm no regressions, run the spec's quickstart validation, and update docs.

- [ ] T053 Run `dotnet test` from repository root and confirm every test that passed in T004 still passes, and every new test from US1, US2, US3 passes (per FR-018, SC-007)
- [ ] T054 Run the spec 039 regression subset specifically (`dotnet test --filter "FullyQualifiedName~Presentation|FullyQualifiedName~StatusList"`) and confirm zero regressions (per spec 039 amendment note)
- [ ] T055 Walk the `specs/093-vc-security-fixes/quickstart.md` manual verification procedure end-to-end for each of the three bugs and check off each Sign-off Criteria item
- [ ] T056 [P] Update `src/Services/Sorcha.Wallet.Service/README.md` with a note on the verifier's new behaviour and the `CredentialStatus:EnableEmbedding` flag
- [ ] T057 [P] Update `src/Common/Sorcha.ServiceClients.Http/Did/SorchaDidResolver.cs` XML doc comments on `ResolveWalletDidAsync` and `ResolveOrgDidAsync` to note the corrected multibase encoding
- [ ] T058 [P] Add an entry to `.specify/MASTER-TASKS.md` marking spec 093 as complete (consistent with CLAUDE.md AI assistant requirements)
- [ ] T059 Confirm that commits on this branch are squashable into a clean PR (one commit per logical group, or one combined commit if the planner prefers a single PR)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup; consists of a single baseline-capture task
- **User Story 1 (Phase 3, P1)**: Independent of US2 and US3. Can start immediately after Phase 2
- **User Story 2 (Phase 4, P1)**: Independent of US3. Has a soft dependency on US1's changes to `PresentationRequestService.cs` because T039 modifies the same file — run US1 first or accept a merge conflict resolution step
- **User Story 3 (Phase 5, P2)**: Fully independent of US1 and US2. Can run in parallel with either at any time
- **Polish (Phase 6)**: Depends on all three user stories being complete

### Within each user story

- Tests (T005–T010 in US1; T023–T029 in US2; T040–T048 in US3) MUST be written and confirmed failing before the corresponding implementation tasks run
- Within US2, the new `IStatusListClient` and related DI wiring (T019–T022) must complete before the Wallet Service implementation tasks (T030–T038) can compile

### Cross-story file sequencing

- `PresentationRequestService.cs` is modified by both US1 (T011–T017) and US2 (T039). US1 must land before US2's T039, or the two change sets must be merged carefully.
- `CredentialEndpoints.cs` is modified only by US2.
- `SorchaDidResolver.cs` is modified only by US3.
- `ActionExecutionService.cs` is modified only by US2.
- All test files are new; no conflicts.

### Parallel Opportunities

- All T005–T010 in US1 are parallel (six different test methods in two test files)
- All T019–T022 in US2 are not parallel with each other (sequential build-up of the service client)
- All T023–T029 in US2 are parallel (seven different test methods in three test files)
- All T040–T048 in US3 are parallel (nine different test methods in two test files)
- T049 (Multicodec helper) must complete before T050–T052 (resolver changes that depend on it)
- US1, US2, and US3 can run in parallel across three developers if staffed, with the cross-story sequencing on `PresentationRequestService.cs` handled by landing US1 first

---

## Parallel Example: User Story 1

All six failing tests can be written in parallel before any implementation runs:

```text
Task: T005 [US1] Write failing unit test ValidSignedToken_ReturnsVerified_WithOnlyDisclosedClaims in PresentationRequestVerificationTests.cs
Task: T006 [US1] Write failing unit test TamperedSignature_ReturnsDenied_WithSignatureError in the same file
Task: T007 [US1] Write failing unit test IssuerMismatch_BetweenTokenIssAndCredentialRow_ReturnsDenied in the same file
Task: T008 [US1] Write failing unit test MalformedDisclosure_ReturnsDenied_WithDisclosureIntegrityError in the same file
Task: T009 [US1] Write failing unit test StoreSideClaimValues_NotLeaked_IntoVerificationResult in the same file
Task: T010 [US1] Write failing integration test PresentationReplay_WithTamperedToken_Denied in PresentationReplayIntegrationTests.cs
```

All nine failing tests for US3 can be written in parallel:

```text
Task: T040 [US3] Ed25519 encode round-trips through decode in MulticodecTests.cs
Task: T041 [US3] NIST P-256 produces 0x80 0x24 varint prefix
Task: T042 [US3] RSA-4096 produces 0x85 0x24 varint prefix
Task: T043 [US3] Unsupported algorithm returns null, not malformed output
Task: T044 [US3] Ed25519 wallet DID → valid multibase in SorchaDidResolverMultibaseTests.cs
Task: T045 [US3] NIST P-256 wallet DID → valid multibase
Task: T046 [US3] RSA-4096 wallet DID → valid multibase
Task: T047 [US3] Org DID symmetric with wallet DID for all supported algorithms
Task: T048 [US3] Unsupported algorithm falls back to publicKeyJwk or fails closed
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

US1 is the MVP because it closes the active security bug behind the existing authenticated endpoint. Scope:

1. Complete Phase 1 (Setup) — 3 tasks
2. Complete Phase 2 (Foundational) — 1 task
3. Complete Phase 3 (User Story 1) — 14 tasks
4. **STOP and VALIDATE**: Run T005–T010 tests and confirm they pass. Walk the quickstart Bug 1 steps manually. Deploy if acceptable on its own.

Total MVP scope: **18 tasks**. US1 can ship as an independent PR if preferred — it is a pure fix to `PresentationRequestService.cs` with no external dependencies.

### Incremental Delivery

1. MVP (US1) → demo and validate
2. Add US2 (credentialStatus embedding) → decode payloads, confirm downstream HAIP specs can consume them
3. Add US3 (multibase fix) → validate with an external DID Core parser
4. Polish phase (T053–T059) → run full regression suite, update docs

Each increment keeps the fix set mergeable on its own. If any fails integration testing, the earlier increments remain valid.

### Parallel Team Strategy

With three developers:

1. All three complete Phases 1 and 2 together
2. Developer A: US1 (14 tasks, touches `PresentationRequestService.cs`)
3. Developer B: US2 (21 tasks, touches `CredentialEndpoints.cs`, `ActionExecutionService.cs`, new service client, `PresentationRequestService.cs` for T039 — **must coordinate with A for T039**)
4. Developer C: US3 (13 tasks, touches `SorchaDidResolver.cs` and new `Multicodec.cs`)
5. Polish phase run collaboratively after all three land

Total: **59 tasks** across the full spec, **7 tasks** in Phase 6 Polish.

---

## Notes

- `[P]` marks parallelisable tasks (different files, no dependencies on incomplete tasks)
- Story labels `[US1]`, `[US2]`, `[US3]` map to the three user stories in `spec.md`
- Each user story is independently testable — US1 and US3 are fully independent; US2 has a soft ordering dependency with US1 on a single shared file
- Tests are written first (per xunit/FluentAssertions convention and spec FR-009) and confirmed failing before implementation lands
- Commit after each task or logical group; the branch is squashable
- The research.md decision for R2 (config flag) is the reason T031 and T036 exist — pure-internal dev environments can disable embedding without breaking
- Spec 039 regression must be run (T054) — any regression there invalidates the fix

---

## Task summary

| Phase | Tasks | Story | Parallel count |
|---|---|---|---|
| Phase 1 Setup | T001 – T003 | — | 2 |
| Phase 2 Foundational | T004 | — | 0 |
| Phase 3 US1 | T005 – T018 | US1 | 6 |
| Phase 4 US2 | T019 – T039 | US2 | 7 |
| Phase 5 US3 | T040 – T052 | US3 | 9 |
| Phase 6 Polish | T053 – T059 | — | 3 |
| **Total** | **59** | | **27** |

**Suggested MVP**: complete Phases 1, 2, and 3 (18 tasks) and ship US1 as a standalone PR. US2 and US3 can follow in parallel.

**Independent test criteria** (summarised from spec §User Stories):

- **US1**: tampered-token submission → `Denied` with signature error; valid-token submission → `Verified` with only disclosed claims.
- **US2**: issued credential payload contains `credentialStatus`; pre-fix credential still verifies via row fallback; bit flip via revoke reflects through the endpoint URL.
- **US3**: `did:sorcha:w:{address}` for each of Ed25519, P-256, RSA-4096 parses in an independent W3C DID Core multibase validator.
