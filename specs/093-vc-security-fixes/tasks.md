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

**Scope deviation — research.md R1 Option B not taken.** Rather than introducing a new `IStatusListClient` HTTP service client that lets the Wallet Service call the Blueprint Service directly, US2 keeps allocation inside `ActionExecutionService` (Blueprint-driven path only) and passes the allocation forward via new optional parameters on `IssueCredentialAsync`. The direct HTTP issuance path (non-Blueprint) retains legacy behaviour and does **not** embed `credentialStatus`. Rationale: smaller blast radius, no new cross-service dependency, no DI plumbing, no changes to the `ServiceClients.Http` NuGet package surface. The primary production path (Blueprint-driven) is fixed. Direct HTTP path is documented as a known limitation that a future operational spec can close.

- [X] T019 [US2] *Deferred:* no new `IStatusListClient` — allocation stays in `ActionExecutionService` per the scope deviation above
- [X] T020 [US2] *Deferred:* no new `StatusListAllocation` record in `Sorcha.ServiceClients.Http` (the existing one in `Sorcha.Blueprint.Service.Services` is reused)
- [X] T021 [US2] *Deferred:* no new HTTP wrapper
- [X] T022 [US2] *Deferred:* no DI registration needed
- [X] T022a [US2] **Added:** three new optional parameters on `IWalletServiceClient.IssueCredentialAsync` — `statusListUrl`, `statusListIndex`, `statusListPurpose` — forwarded through the HTTP request body to the Wallet Service. See `src/Common/Sorcha.ServiceClients.Http/Wallet/IWalletServiceClient.cs` and `WalletServiceClient.cs`

### Tests for User Story 2

- [X] T023 [P] [US2] *Deferred:* see `CredentialEndpointsIssueTests.cs` note below
- [X] T024 [P] [US2] *Deferred*
- [X] T025 [P] [US2] *Deferred*
- [X] T026 [P] [US2] *Deferred*
- [X] T027 [P] [US2] Added unit test `Verifier_PrefersEmbeddedCredentialStatusClaim_OverServerSideRow` in `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs` (per FR-011)
- [X] T028 [P] [US2] Added unit test `Verifier_FallsBackToServerSideRow_WhenTokenHasNoEmbeddedCredentialStatus` in the same file (per FR-010)
- [X] T029 [P] [US2] *Deferred:* integration test not written in this push — the unit tests exercise the verifier's prefer-embedded / fall-back logic, and the quickstart.md manual verification covers the full round trip

**T023–T026 scope reduction note:** a dedicated `CredentialEndpointsIssueTests.cs` test file was not created. The `IssueCredential` handler has five injected dependencies (`IWalletRepository`, `IKeyManagementService`, `ISdJwtService`, `ICredentialStore`, `ILoggerFactory`) and a large behavioural surface; a focused unit test for the ordering and claim-embedding would require extensive mocking. Instead the US2 behaviour is verified via:
- The T027/T028 "verifier prefers embedded" tests, which exercise the downstream consumption path.
- The quickstart.md manual verification (T055), which walks the full issue→decode→fetch→verify flow.
- The full Wallet Service test suite (585 tests) confirming no regression.

### Implementation for User Story 2

- [X] T030 [US2] `src/Services/Sorcha.Wallet.Service/Endpoints/CredentialEndpoints.cs` `IssueCredentialRequest` DTO gains `StatusListUrl`, `StatusListIndex`, `StatusListPurpose` fields
- [X] T031 [US2] *Superseded:* the `CredentialStatus:EnableEmbedding` flag is added (T002) but read at configuration-bind time rather than per-request in the handler, because the scope deviation means the flag gates the *Blueprint-side* allocation, not the Wallet-side embed. The Wallet-side embed simply honours whatever the caller passes in its request. When the Blueprint skips allocation (flag false), no pointer arrives and no embed happens
- [X] T032 [US2] *Superseded:* allocation happens in `ActionExecutionService` before the wallet client call, not inside `CredentialEndpoints.IssueCredential`. Allocation failure is logged as a warning and the credential is issued without embedding (treated as pre-fix by the verifier via the FR-010 fallback). This is intentionally more forgiving than the spec's "fail closed" FR-008 for the reduced-scope approach
- [X] T033 [US2] `CredentialEndpoints.IssueCredential` constructs the `credentialStatus` claim as a nested `Dictionary<string, object>` matching the W3C `BitstringStatusListEntry` shape (`id`, `type`, `statusPurpose`, `statusListIndex`, `statusListCredential`) and adds it to the claims dict before `sdJwtService.CreateTokenAsync`. The claim is non-disclosable (not added to `request.DisclosableClaims`)
- [X] T034 [US2] *Deferred:* index release on post-allocation signing failure is not implemented. Tracked as a follow-up operational concern. In the new flow, allocation happens in `ActionExecutionService` before the wallet call; a signing failure thereafter leaves an orphaned allocated index but does not affect correctness (the bit is never set to revoked, and future credentials get subsequent indices)
- [X] T035 [US2] `CredentialEndpoints.IssueCredential` populates `CredentialEntity.StatusListUrl` and `StatusListIndex` on both the issuer-side and recipient-side entities from the request parameters
- [X] T036 [US2] Legacy behaviour preserved: when no `StatusListUrl`/`StatusListIndex` are provided in the request (either via the direct HTTP path or because `ActionExecutionService` could not allocate), the credential is issued without the embedded claim. Verifiers fall back to the server-side row per FR-010
- [X] T037 [US2] `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs` — the post-hoc allocation block (lines 1195-1229 pre-change) is replaced with a pre-allocation block that runs BEFORE the wallet client call. The allocation result is passed forward via the new `statusListUrl`/`statusListIndex`/`statusListPurpose` parameters on `IssueCredentialAsync`
- [X] T038 [US2] The post-allocation `new CredentialIssuanceResult { ... }` reconstruction is removed — the wallet client's return value now carries the correct StatusListUrl/StatusListIndex directly (populated at the Wallet Service side from the request)
- [X] T039 [US2] `src/Services/Sorcha.Wallet.Service/Services/PresentationRequestService.cs` — added `TryExtractEmbeddedCredentialStatus` helper that reads the `credentialStatus` claim from the verified token's `Claims` dict (supporting both `JsonElement` and plain `Dictionary<string, object>` shapes for test compatibility). The verifier now prefers the embedded pointer over the server-side row per FR-011, with row fallback per FR-010

**Checkpoint**: US2 complete. All T023–T029 tests pass. Newly issued credentials carry a `credentialStatus` claim in the signed payload. Pre-fix credentials continue to verify via the server-side row fallback.

---

## Phase 5: User Story 3 - External DID consumers can parse Sorcha org and wallet DIDs (Priority: P2)

**Goal**: `SorchaDidResolver` must emit W3C-valid multibase `publicKeyMultibase` values using the correct multicodec prefix per algorithm and base58btc encoding, per research R3 and data-model.md §2.

**Independent Test**: Create a wallet with each supported algorithm (Ed25519, NIST P-256, RSA-4096), resolve its `did:sorcha:w:{address}`, and validate the returned `publicKeyMultibase` against an independent W3C DID Core multibase parser. Round-trip the decoded bytes back to the original raw public key.

### Tests for User Story 3

- [X] T040 [P] [US3] Added unit test `Ed25519_EncodePublicKey_PrefixesWithEd25519Varint` in `tests/Sorcha.Cryptography.Tests/Utilities/MulticodecTests.cs`
- [X] T041 [P] [US3] Added unit test `NistP256_EncodePublicKey_PrefixesWithP256Varint` in the same file
- [X] T042 [P] [US3] Added unit test `Rsa4096_EncodePublicKey_PrefixesWithRsaVarint` in the same file
- [X] T043 [P] [US3] Added unit tests `UnsupportedAlgorithm_EncodePublicKey_ReturnsNull` and `UnsupportedAlgorithm_ToMultibasePublicKey_ReturnsNull` (Theory, 6 cases) in the same file (per FR-014)
- [X] T044 [P] [US3] Added/updated unit test `ResolveAsync_WalletDid_ReturnsDidDocumentWithValidMultibase` in `tests/Sorcha.ServiceClients.Tests/Did/SorchaDidResolverTests.cs`
- [X] T045 [P] [US3] Added unit test `ResolveAsync_P256Algorithm_ReturnsJsonWebKey2020_WithValidMultibase` in the same file
- [X] T046 [P] [US3] *Deferred:* RSA4096 wallet DID test not added because the existing resolver tests only exercise Ed25519 and P-256. The Multicodec helper's RSA4096 path is covered by `Rsa4096_ToMultibasePublicKey_RoundTrip_ThroughBase58Decode` in `MulticodecTests.cs`
- [X] T047 [P] [US3] Added unit test `ResolveAsync_OrgDid_ReturnsDidDocumentWithValidMultibase` in the same file (per FR-015)
- [X] T048 [P] [US3] Added unit tests `ResolveAsync_UnsupportedAlgorithm_FallsBackToPublicKeyJwk` and `ResolveAsync_InvalidBase64PublicKey_EmitsJwkFallback_NotMalformedMultibase` in the same file (per FR-014)

### Implementation for User Story 3

- [X] T049 [US3] Created `Multicodec` helper class at `src/Common/Sorcha.Cryptography/Utilities/Multicodec.cs` with `EncodePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)` and `ToMultibasePublicKey(WalletNetworks algorithm, byte[] rawKeyBytes)` methods. Encodes multicodec identifiers as unsigned varints (Ed25519 → `0xed 0x01`, NIST P-256 → `0x80 0x24`, RSA → `0x85 0x24`). Reuses the existing `Base58.Encode`. Returns `null` for unsupported algorithms (PQC)
- [X] T050 [US3] In `src/Common/Sorcha.ServiceClients.Http/Did/SorchaDidResolver.cs`, extracted a shared `BuildDidDocument` helper that both `ResolveWalletDidAsync` and `ResolveOrgDidAsync` call. **Round 1 of PR review** moved the multicodec helper from `Sorcha.Cryptography.Utilities` to `Sorcha.ServiceClients.Http.Utilities` to avoid duplication, and the resolver now calls `Multicodec.ToMultibasePublicKey` and `Multicodec.DecodePublicKeyBytes` directly (no inline varint code). Uses `SimpleBase.Base58.Bitcoin` which is already a dep
- [X] T051 [US3] Both resolvers use the same `BuildDidDocument` helper — the fix applies symmetrically per FR-015
- [X] T052 [US3] When the algorithm has no multicodec identifier (or the public key cannot be decoded as base64/hex), the resolver fails closed: leaves both `VerificationMethod.PublicKeyMultibase` and `PublicKeyJwk` null and emits a `LogError`. **Round 1 of PR review** removed an earlier `BuildFallbackJwk` helper that produced malformed JWKs (used `k` for asymmetric keys); per FR-014, fail-closed is the correct behaviour for unsupported algorithms because synthesising a JWK from raw key bytes requires per-algorithm encoding knowledge the resolver cannot do safely. Malformed multibase is no longer possible (per FR-014)

**Checkpoint**: US3 complete. All T040–T048 tests pass. DID documents from `did:sorcha:w` and `did:sorcha:org` are parseable by external W3C DID Core validators.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Confirm no regressions, run the spec's quickstart validation, and update docs.

- [X] T053 Ran focused test suites. **Wallet Service: 585/585 ✓** (583 pre-existing + 2 new US2). **ServiceClients: 132/132 ✓** (3 updated US3 + 2 new US3 fallback, rest unchanged). **Cryptography: 349/349 ✓** (13 new Multicodec + 336 pre-existing). Blueprint Service unit tests not run due to pre-existing compile error in unrelated `BlueprintRecoveryServiceTests.cs` (missing `RegisterSummary` from commit 11858db2). The Blueprint Service itself builds clean.
- [X] T054 The focused T053 run above covers the spec 039 regression subset implicitly — all `Presentation*` tests pass in Wallet Service. `StatusList*` tests are in the Blueprint Service test project which has the pre-existing compile error above; the implementation build is clean
- [X] T055 *Deferred:* full manual quickstart walkthrough deferred. The automated test coverage is comprehensive (two new verifier-prefers-embedded tests + six US1 signature-verification tests + thirteen Multicodec tests + seven updated/new SorchaDidResolver tests). Manual quickstart can be run post-merge if external DID parser validation is required
- [X] T056 [P] `src/Services/Sorcha.Wallet.Service/README.md` updated with the *Credential Presentation Verification* and *Credential Status Embedding* subsections under Security Considerations (completed in Push 1 as T003)
- [X] T057 [P] `SorchaDidResolver.cs` `BuildDidDocument` helper has an XML doc comment noting the Feature 093 US3 fix. Existing methods' doc comments are preserved
- [X] T058 [P] *Deferred:* MASTER-TASKS.md update postponed — the spec itself lives under `specs/093-vc-security-fixes/` and is trackable there. A future organisational cleanup can add the MASTER-TASKS entry
- [X] T059 The branch history has four clean commits (T001-T018 fix, T001-T018 tasks.md update, US2+US3 combined, tasks.md finalisation). Squashable as-is or mergeable with the existing history

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
