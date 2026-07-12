# Tasks: EUDI Conformance — Protocol Alignment & External Trust Rail

**Input**: Design documents from `/specs/181-eudi-conformance/`

**Prerequisites**: plan.md, spec.md, research.md (R1–R15), data-model.md, contracts/, quickstart.md

**Tests**: Included — the constitution mandates tests alongside code (>85% new-code coverage), and the
spec's SC-008 requires a deliberate red-test of the CI gate.

**Organization**: Phases 3–8 map 1:1 to spec user stories US1–US6. US3 (trust rail) is independent of
US1/US2 and can run in parallel after Phase 2. US4→US5 share the certificate foundation; US6 composes
US1+US3 and goes last.

## Format: `[ID] [P?] [Story] Description`

---

## Phase 1: Setup

**Purpose**: Wiring changes every story builds on. (No new projects — plan Structure Decision.)

- [X] T001 Add ProjectReference `Sorcha.Verifier.Engine` to `src/Services/Sorcha.Haip.Service/Sorcha.Haip.Service.csproj` (R1 — HAIP is the only DCQL consumer not already referencing the engine)
- [X] T002 [P] Create CI dialect gate `scripts/check-presentation-dialect.ps1` + `.presentation-dialect-allowlist` seeded with every current PE site from research R4/R15, and wire a step into the CI workflow (`.github/workflows/`) per the `check-trust-clean-break.ps1` precedent (FR-009; the allowlist ratchets to empty in T028)
- [X] T003 [P] Test fixtures: signed ETSI TS 119 612 XML generator + test CA helper in `tests/Sorcha.Tenant.Service.Tests/Fixtures/TrustLists/TrustListFixture.cs` (generates a minimal `TrustServiceStatusList` with configurable sequence/dates/service entries, XMLDSig-signs it with a fixture cert; also exposes the test CA for US3/US4 credential-issuance tests — R5, quickstart US3/US4)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared DCQL dialect and media-type flip that US1/US2/US6 all consume.

**⚠️ CRITICAL**: T004–T009 block all presentation-dialect stories. US3 is NOT blocked by this phase
(only by Setup) — it may start after T003.

- [X] T004 [P] DCQL wire model records (`DcqlQuery`, `DcqlCredentialQuery`, `DcqlCredentialMeta`, `DcqlClaimQuery`, `DcqlCredentialSetQuery`, `DcqlVpToken`) in `src/Common/Sorcha.Verifier.Engine/Dcql/DcqlModels.cs` per data-model.md §1 (exact JSON property names, id-regex + uniqueness validation)
- [X] T005 `DcqlRequestBuilder` in `src/Common/Sorcha.Verifier.Engine/Dcql/DcqlRequestBuilder.cs` — builds `dcql_query` from (format, vct/doctype, required claims, optional claims, alternative groups); owns the required/optional → `claims`+`claim_sets` mapping (R2); depends on T004
- [X] T006 `DcqlRequestParser` in `src/Common/Sorcha.Verifier.Engine/Dcql/DcqlRequestParser.cs` — inverse of T005 in the same review unit (FR-008); typed errors for malformed queries; rejects PE shapes with `LEGACY_DIALECT`; depends on T004
- [X] T007 [P] Unit tests: builder⇄parser round-trip, id validation, credential_sets referential integrity, legacy-shape rejection in `tests/Sorcha.Verifier.Engine.Tests/Dcql/DcqlRoundTripTests.cs` (depends on T005/T006)
- [X] T008 Flip SD-JWT typ to `dc+sd-jwt` at `src/Common/Sorcha.Cryptography/SdJwt/SdJwtService.cs:199-203`; audit verify paths to confirm no strict typ check breaks stored `vc+sd-jwt` credentials (R3 — grep found none; add an explicit dual-accept test to lock it in) in `tests/Sorcha.Cryptography.Tests/SdJwt/`
- [X] T009 [P] (HAIP test assertions deliberately deferred to T013 — they assert endpoint behaviour that only changes there) Update the six `vc+sd-jwt`-asserting test files (R3): `tests/Sorcha.Wallet.Service.Tests/Services/DeviceDelegationIssuerTests.cs:109,142`, `tests/Sorcha.Wallet.Pwa.Tests/Services/Presentation/PresentationEngineTests.cs:226`, `tests/Sorcha.Verifier.Tests/Services/TestVpFactory.cs:79,103,185,209`, `tests/Sorcha.Haip.Service.Tests/Endpoints/MetadataEndpointTests.cs:42,60,64`, `tests/Sorcha.Haip.Service.Tests/Endpoints/CredentialEndpointTests.cs:20,28,39` (depends on T008)

**Checkpoint**: shared dialect exists and round-trips; new issuance carries `dc+sd-jwt`; old credentials still verify.

---

## Phase 3: User Story 1 — Standards-conformant presentation dialect (Priority: P1) 🎯 MVP

**Goal**: Every producer emits DCQL in place of Presentation Exchange; responses use the object-keyed
`vp_token`; all OpenID4VP routes byte-stable (D1/FR-002); legacy shapes rejected with `LEGACY_DIALECT`.

**Independent Test**: quickstart §US1 — decode a live request object (dcql_query present, PD absent);
run AssuredIdentity walkthroughs + AIAS rehearse unchanged (SC-002); PE-shaped direct_post → 400.

### Implementation for User Story 1

- [X] T010 [US1] HAIP verifier request objects emit `dcql_query` via `DcqlRequestBuilder`, drop the inline PE dict, keep route/signing unchanged, in `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs:141-179` (contract §1; client_id value unchanged until US6)
- [X] T011 [US1] `HandleDirectPost` accepts the object-keyed `vp_token` for BOTH formats, dispatches per query id, drops `presentation_submission` (present or bare-compact-string vp_token → 400 `LEGACY_DIALECT` problem+json), in `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs:190-277` + generalise `TryExtractMdocDeviceResponse` into the shared envelope reader (contract §2, FR-003/FR-007); depends on T010
- [X] T012 [P] [US1] `VerifiablePresentationValidator` consumes a single presentation entry from the envelope (per-query call shape) in `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs:94-342` — KB-JWT `aud` check stays against the request's client_id value (prefix flip is US6)
- [X] T013 [P] [US1] HAIP issuer metadata + credential endpoint format identifiers → `dc+sd-jwt` in `src/Services/Sorcha.Haip.Service/Endpoints/MetadataEndpoints.cs` and `CredentialEndpoints.cs` (FR-004)
- [X] T014 [US1] `SorchaWalletPresentationConsumer.BuildInitiationAsync` emits the `request_uri` deep-link form (converging on the F164 transport) with the DCQL body served by the F111 request-object route, in `src/Services/Sorcha.Blueprint.Service/Services/Implementation/SorchaWalletPresentationConsumer.cs:210-215` (R4); update `tests/Sorcha.Blueprint.Service.Tests/Services/SorchaWalletPresentationConsumerTests.cs`
- [X] T015 [US1] PWA `PresentationEngine`: `Parse` fetches `request_uri` (unsigned-inline form refused `LEGACY_DIALECT`), decodes the request-object JWT payload WITHOUT signature verification yet (US6 adds it), parses `dcql_query` via `DcqlRequestParser`; `BuildVpTokenAsync` emits the object-keyed envelope; in `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs` (single-query flow preserved; multi-query is US2)
- [X] T016 [US1] `Present.razor` + `ParsedPresentationRequest` model migration (single-query shape backed by `DcqlQuery`) in `src/Apps/Sorcha.Wallet.Pwa/Pages/Present.razor` and `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Presentation/PresentationModels.cs`; depends on T015
- [X] T017 [P] [US1] `Sorcha.Agent` present command drops `presentation_submission`, posts object-keyed `vp_token`, in `src/Apps/Sorcha.Agent/Commands/HaipPresentCommand.cs:137-145`
- [X] T018 [P] [US1] Regenerate the static PE fixture as DCQL: `demos/Membership/presentations/membership-pos.presentation.json` (+ any generator script `demos/Membership/Render-MembershipBlueprint.ps1`) per R15
- [X] T019 [US1] Unit/handler tests for T010–T012: request-object payload asserts `dcql_query` + no `presentation_definition`; direct_post accepts object envelope, rejects legacy (400 code asserted); in `tests/Sorcha.Haip.Service.Tests/Endpoints/VerifierEndpointTests.cs`, `tests/Sorcha.Verifier.Tests/Services/VerifiablePresentationValidatorTests.cs`, `tests/Sorcha.Wallet.Pwa.Tests/Services/Presentation/PresentationEngineTests.cs`
- [X] T020 [US1] SC-001 schema conformance test: validate generated request bodies against a checked-in JSON Schema for the final OpenID4VP request shape in `tests/Sorcha.Haip.Service.Tests/Conformance/DcqlRequestSchemaTests.cs`
- [X] T021 (phase-1 PASS on rebuilt images; phase-2 is a pre-existing deferred stub; live SC-001 wire check + LEGACY_DIALECT rejection PASS; AIAS rehearse deferred to T062 — needs its own run-demo.ps1 bootstrap; its path (issuance+agent) is dialect-untouched and issuance was live-validated by phase-1) [US1] Run walkthrough/demo regression (SC-002): `walkthroughs/AssuredIdentity/run-phase1-identity.ps1` + phase-2 licence + `demos/AIAS/rehearse.ps1` against local Docker; fix fallout; record results in the PR description
- [X] T022 [US1] Ratchet `.presentation-dialect-allowlist` to empty and add the deliberate red-test demo (temporarily reintroduce a PE token, assert gate fails, revert — capture in PR per SC-008); depends on T010–T018
- [X] T023 [US1] `sorcha_dialect_rejection_total{surface}` counter on the HAIP meter, recorded at every `LEGACY_DIALECT` return (FR-028); depends on T011

**Checkpoint**: whole platform speaks DCQL end-to-end; MVP demonstrable; CI gate live and empty-allowlisted.

---

## Phase 4: User Story 2 — Multi-credential and alternative asks (Priority: P2)

**Goal**: One request carries N credential queries + `credential_sets` alternatives; per-query consent;
per-query verification outcomes (FR-005/FR-006).

**Independent Test**: quickstart §US2 — two-query request blocks on a missing credential with a clear
unsatisfiable indicator; two-option alternative completes with the held option; result reports per query.

### Implementation for User Story 2

- [X] T024 [P] [US2] Query-set matching: `DcqlMatchResult`/`DcqlQueryMatch` model + `Match()` generalisation (per-query candidates, credential_sets solving, unsatisfied-required detection) in `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs` + `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Models/User/Presentation/PresentationModels.cs` (data-model §1); unit tests in `tests/Sorcha.Wallet.Pwa.Tests/Services/Presentation/DcqlMatchTests.cs`
- [X] T025 [US2] Consent surface per-query sections (each ask listed separately, per-query claim approval, unsatisfiable asks flagged, no partial submit) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/ConsentSheet.razor`; bUnit tests in `tests/Sorcha.UI.Components.User.Tests/` (FR-006a/b/d); depends on T024
- [X] T026 [US2] `CredentialPickerDialog` alternative choice (citizen picks among satisfiable options; no auto-pick when several match — edge case) in `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/CredentialPickerDialog.razor`; depends on T024
- [X] T027 [US2] Multi-presentation `vp_token` build (one entry per consented query) + `Present.razor` orchestration in `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs` + `Pages/Present.razor`; depends on T024–T026
- [X] T028 [US2] Verifier side: per-query verification loop + `VerificationResult.perQuery` + overall-success rule (every required query/set satisfied) + `DCQL_UNKNOWN_QUERY_ID` failure in `src/Services/Sorcha.Haip.Service/Endpoints/VerifierEndpoints.cs` and `src/Services/Sorcha.Haip.Service/Models/VerifierModels.cs` (contract §3, FR-003/FR-005); handler tests in `tests/Sorcha.Haip.Service.Tests/Endpoints/VerifierEndpointTests.cs`
- [X] T029 [US2] Blueprint authoring surface: optional `anyOf` grouping on `credentialRequirements` mapped to `credential_sets` (contract §4) in `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialRequirement.cs` + the requirement→DCQL mapping in `SorchaWalletPresentationConsumer`/request creation; tests in `tests/Sorcha.Blueprint.Service.Tests/`
- [X] T030 [US2] End-to-end integration test of quickstart §US2 scenarios (two-query AND, two-option OR, missing-credential block) in `tests/Sorcha.Haip.Service.Tests/Integration/MultiCredentialPresentationTests.cs` (SC-003)

**Checkpoint**: multi-credential + alternatives work end-to-end; US1 flows unaffected.

---

## Phase 5: User Story 3 — Trusted-list snapshot rail (Priority: P2)

**Goal**: Operator imports an ETSI TS 119 612 trusted list; anchors back the `x509-lotl`/`trustlist`
trust source; evidence names the snapshot; fail-closed everywhere (FR-011..FR-017).

**Independent Test**: quickstart §US3 — fixture import → vouched verification with evidence naming the
snapshot; delete → `TRUSTLIST_UNAVAILABLE`; tampered XML → `TRUSTLIST_SIGNATURE_INVALID`.

**Dependencies**: Setup only (T003 fixture) — runs in parallel with Phases 3–4.

### Implementation for User Story 3

- [X] T031 [P] [US3] EF entities `TrustedListSnapshot` + `TrustedListAnchor` (data-model §2) in `src/Services/Sorcha.Tenant.Service/Models/TrustedListSnapshot.cs`, DbContext config + squashed into `Migrations/…InitialCreate.cs` per the pre-release convention, `IStorageRegistrationLog` registration (warn-tier) in the Tenant storage block
- [X] T032 [P] [US3] Multibase status decode: accept `u`-prefixed base64url at `src/Core/Sorcha.Blueprint.Engine/Credentials/BitstringStatusListChecker.cs:86` (R7, FR-010); unit tests both encodings in `tests/Sorcha.Blueprint.Engine.Tests/Credentials/BitstringStatusListCheckerTests.cs`
- [X] T033 [US3] `TrustedListImportService` in `src/Services/Sorcha.Tenant.Service/Trust/TrustedListImportService.cs`: XMLDSig core verification (`SignedXml`), TS 119 612 parse (scheme info, sequence, dates, territory, signer identity), CA/QC+granted anchor extraction with extraction summary, sequence-regression check (R5, FR-011/FR-012/FR-013); depends on T031; unit tests over the T003 fixture (valid / tampered / unsigned / odd-but-valid edge cases) in `tests/Sorcha.Tenant.Service.Tests/Trust/TrustedListImportServiceTests.cs`
- [X] T034 [US3] Trust endpoints per `contracts/trustlist-admin.openapi.yaml`: `POST /api/v1/trust/trustlists/import` (multipart upload | sourceUrl fetch-once), `GET /trustlists`, `GET /trustlists/{id}` (detail + anchors + summary), `DELETE /trustlists/{id}`, `GET /trustlists/{id}/anchors` (service-tier anchor read); REPLACE the F135 placeholder `PUT /trustlists/{id}` (clean break); FluentValidation per VAL-001; in `src/Services/Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs`; depends on T033; handler tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/TrustEndpointTests.cs`
- [X] T035 [US3] HTTP-backed caching `ITrustListProvider` (15-min cache over `GET …/anchors`) replacing the in-memory singleton as the verifying-services read path, in `src/Common/Sorcha.ServiceClients.Http/Trust/TrustListProvider.cs`; wire into Blueprint + HAIP DI; `TrustAnchorSet.AnchorSetId = "{trustListId}#{seq}"` so `TrustEvidence.TrustListId` carries the snapshot identity (FR-014/FR-015); tests with a stub handler in `tests/Sorcha.ServiceClients.Http.Tests/`
- [X] T036 [US3] Freshness semantics: computed Fresh/Stale, warn-mode evidence flag + `sorcha_trustlist_stale_evaluation_total` + log, strict mode `Trust:TrustListStrictFreshness` fail-closed `TRUSTLIST_STALE`; no-snapshot → `TRUSTLIST_UNAVAILABLE`; boundary-deterministic via `TimeProvider` (FR-016, edge cases); implemented across T033/T035 seams; unit tests in `tests/Sorcha.Tenant.Service.Tests/Trust/TrustListFreshnessTests.cs`
- [X] T037 [US3] Platform-admin trusted-lists panel (import w/ inline guidance, list w/ freshness chips, detail anchors view, delete w/ confirm; `IInlineFeedback` not Snackbar) in `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/TrustedLists.razor` + client service under `Sorcha.UI.Core/Services/Admin/` (SC-009); depends on T034
- [X] T038 [US3] Integration test of SC-004: issue under fixture CA → present under `trustPolicy{kind:trustlist}` → verified with evidence snapshot id; delete snapshot → fail closed; in `tests/Sorcha.Tenant.Service.Tests/Integration/TrustListVerificationTests.cs` (or Blueprint engine test host); depends on T033–T036
- [X] T039 [US3] `sorcha_trustlist_snapshot_info` gauge + import/delete audit logging on the `Sorcha.Trust` meter (FR-028, data-model §7); depends on T034

**Checkpoint**: `x509-lotl` is real; external credentials verifiable against imported anchors.

---

## Phase 6: User Story 4 — Externally-verifiable issuance identity (Priority: P3)

**Goal**: CSR out → external cert in → issued credentials chain to the external root; fail-closed when
absent/expired/mismatched (FR-018..FR-021). Includes the CA-persistence prerequisite (R8).

**Independent Test**: quickstart §US4 — credential issued on `x509-lotl` anchor verifies with ONLY the
test CA root trusted (SC-005); deleting the imported cert fails issuance closed.

**Dependencies**: T003 (test CA). Benefits from US3 for the verify leg of SC-005 but the issuance-side
tasks are independent.

### Implementation for User Story 4

- [X] T040 [US4] CA persistence: `TenantRootCa` (AES-256-GCM-encrypted private key), `OrgCertificateRecord`, `CsrRecord` entities + DbContext + squashed migration + storage-log registration (data-model §3, R8) in `src/Services/Sorcha.Tenant.Service/Models/` + `Data/TenantDbContext.cs`; convert `InternalCaTrustProvider` to a write-through cache over the EF store in `src/Services/Sorcha.Tenant.Service/Trust/InternalCaTrustProvider.cs` (existing behaviour preserved — regression-guard with current trust-endpoint tests)
- [X] T041 [P] [US4] `WalletBackedSignatureGenerator : X509SignatureGenerator` delegating to `IWalletServiceClient.SignTransactionAsync(…, isPreHashed: true)` with raw `r‖s` → DER `ECDSA-Sig-Value` conversion (R10) in `src/Services/Sorcha.Tenant.Service/Trust/WalletBackedSignatureGenerator.cs`; unit tests with known vectors + a mocked wallet client in `tests/Sorcha.Tenant.Service.Tests/Trust/WalletBackedSignatureGeneratorTests.cs`
- [X] T042 [US4] `OrgCertificateService` in `src/Services/Sorcha.Tenant.Service/Trust/OrgCertificateService.cs`: P-256 key resolution (primary ES256 else HAIP co-key via Wallet Service — R9), eligibility verdict, CSR generation (T041), import validation per R11 (key match / chain build over uploaded set / validity / suitability; chain-with-root and without), supersede semantics, key-rotation `KeyMismatch` flagging; depends on T040/T041; unit tests covering every FR-019 failure mode with distinct codes in `tests/Sorcha.Tenant.Service.Tests/Trust/OrgCertificateServiceTests.cs`
- [X] T043 [US4] Endpoints per `contracts/org-certificates.openapi.yaml`: `GET …/certificates`, `POST …/csr`, `POST …/certificates/import`, `DELETE …/certificates/{id}` in `src/Services/Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs`; FluentValidation; depends on T042; handler tests in `tests/Sorcha.Tenant.Service.Tests/Endpoints/OrgCertificateEndpointTests.cs`
- [X] T044 [US4] Chain-attach by anchor: `x509-lotl` → Active imported chain (fail closed `CERT_EXTERNAL_ANCHOR_UNAVAILABLE` when absent/expired/mismatched — never tenant-root fallback), `x509-tenant` unchanged; in the Wallet Service chain resolver (`IssueCredentialChainResolver`) + `src/Core/Sorcha.Blueprint.Engine/Credentials/MdocFormatHandler.cs` issuance path (FR-020/FR-021); depends on T042; tests in `tests/Sorcha.Wallet.Service.Tests/` + `tests/Sorcha.Blueprint.Engine.Tests/`
- [~] T045 [US4] SC-005: the trust-service half is proven end-to-end in `OrgCertificateServiceTests` (CSR bound to org key → fixture-CA-signed leaf → import validated → `ResolveActiveImportedChainAsync` returns the external chain; negatives: delete → fail closed, key-rotation → `KeyMismatch` fail closed) and the anchor fail-closed at the issuance seam in `IssueCredentialChainResolverTests`. The full credential-engine issue-on-`x509-lotl` → verify-with-only-external-root E2E rides the T062 quickstart Docker pass.
- [X] T046 [P] [US4] `sorcha_org_cert_issuance_total{provenance,outcome,reason}` metric + structured audit logs (FR-028); depends on T042

**Checkpoint**: an org with an imported cert issues externally-verifiable credentials.

---

## Phase 7: User Story 5 — Certificate lifecycle without footguns (Priority: P3)

**Goal**: Auto-enrol on org creation, backfill, admin surface, auditable re-issue, typed Ed25519/no-P-256
exclusion replacing the ASN.1 500 (FR-022..FR-024).

**Independent Test**: quickstart §US5 — new org has a cert with zero manual steps; ineligible org gets
typed 422 with zero unhandled errors (SC-006).

**Dependencies**: T040/T042 (persistence + OrgCertificateService).

### Implementation for User Story 5

- [X] T047 [US5] Eligibility guard replacing the ASN.1 failure: `X509CertificateBuilder`/`TenantCrlBuilder` guarded by the T042 eligibility check; enrol path returns typed 422 `CERT_KEY_NOT_ELIGIBLE` (never an unhandled `CryptographicException`) in `src/Services/Sorcha.Tenant.Service/Trust/X509CertificateBuilder.cs:32-40,77` + `TrustEndpoints.cs` (FR-024); tests: ineligible-org path asserts typed code + no error-level log in `tests/Sorcha.Tenant.Service.Tests/Trust/CertEligibilityTests.cs`
- [X] T048 [US5] Rework `POST …/enrol` semantics per contract: server resolves the P-256 key itself (drop caller-supplied `OrgPublicKeyBase64`, closing the unvalidated-key TODO at `TrustEndpoints.cs:228`), re-issue supersedes with history (FR-023d), doubles as backfill; in `src/Services/Sorcha.Tenant.Service/Endpoints/TrustEndpoints.cs`; depends on T042/T047; update the HAIP walkthrough setup script that calls enrol (R15)
- [X] T049 [US5] Auto-enrol hook: post-wallet-provision in `src/Services/Sorcha.Tenant.Service/Services/OrganizationService.cs` (non-fatal on failure — org creation never fails, FR-022) + retry ride-along in `Services/OrgWalletReconciliationService.cs`; operator-visible failure log + metric; depends on T048; tests: creation-with-enrol-failure still succeeds, reconciliation retries, in `tests/Sorcha.Tenant.Service.Tests/Services/OrgAutoEnrolTests.cs`
- [X] T050 [US5] Org-admin certificates panel (list with status/validity/chain summary + eligibility state, re-issue, CSR download, import upload, expiry warning) as a section of `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/Admin/Settings/OrgSettings.razor` + client service under `Sorcha.UI.Core/Services/Admin/`; depends on T043/T048
- [X] T051 [US5] SC-006 integration test: new P-256-resolvable org → Active internal cert zero-touch (`boundKeySource=HaipCoKey` for ED25519-primary); no-P-256 org → typed ineligibility everywhere a cert op is attempted; in `tests/Sorcha.Tenant.Service.Tests/Integration/CertLifecycleTests.cs`

**Checkpoint**: every eligible org reaches an X.509 identity without manual steps; no ASN.1 500 remains.

---

## Phase 8: User Story 6 — Verifier authentication for wallets (Priority: P4)

**Goal**: Prefixed `x509_san_dns:` client_id, x5c-carrying signed request objects, wallet-side signature
+ SAN + anchor verification with the three-state consent verdict; unsigned inline form retired
(FR-025..FR-027).

**Independent Test**: quickstart §US6 — tampered signature refused; SAN mismatch refused; verifier state
renders Trusted / AuthenticUntrusted correctly (SC-007).

**Dependencies**: US1 (dialect + request_uri path), US3 (anchors for the Trusted state).

### Implementation for User Story 6

- [ ] T052 [P] [US6] Verifier certificate config (`Haip:VerifierCertificate`(+Password), `Haip:PublicHost`) with startup SAN==host validation (fail-fast Production/Staging, mirrors `SorchaIssuer`) + dev fallback minting a tenant-root cert with SAN dNSName = PublicHost (R12) in `src/Services/Sorcha.Haip.Service/Extensions/` + config; tests in `tests/Sorcha.Haip.Service.Tests/`
- [ ] T053 [US6] `RequestObjectSigner` signs with the verifier certificate key and embeds the `x5c` chain header; client_id flips to prefixed `x509_san_dns:{PublicHost}` at request creation (retires `TODO(098)` at `VerifierEndpoints.cs:100`); in `src/Services/Sorcha.Haip.Service/Services/RequestObjectSigner.cs` + `Endpoints/VerifierEndpoints.cs`; depends on T052
- [ ] T054 [US6] KB-JWT `aud` = full prefixed client_id on BOTH sides in the same task (mint: `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/PresentationEngine.cs:170`; verify: `src/Common/Sorcha.Verifier.Engine/VerifiablePresentationValidator.cs` aud check) so presentations never self-reject; depends on T053; round-trip test in `tests/Sorcha.Verifier.Tests/`
- [ ] T055 [US6] `RequestObjectValidator` in `src/Common/Sorcha.Verifier.Engine/RequestObjectValidator.cs` (BouncyCastle — WASM-safe, R13): JWS ES256 verify via x5c leaf, SAN dNSName == client_id host (`REQUEST_OBJECT_INVALID`/`REQUEST_HOST_MISMATCH`), chain→anchor check against a supplied `TrustAnchorSet` ⇒ `VerifierAuthState` (Trusted / AuthenticUntrusted / Unverifiable; anchors-absent never blocks — FR-027); unit tests incl. tamper + mismatch vectors in `tests/Sorcha.Verifier.Engine.Tests/RequestObjectValidatorTests.cs`
- [ ] T056 [US6] PWA integration: `PresentationEngine.Parse` runs T055 validation (refusal with citizen-comprehensible errors), anchors fetched from the home installation's `GET /trustlists/{id}/anchors` and cached in IndexedDB (evict-and-continue on undecryptable rows per the established cache rule); `ConsentSheet` renders the three-state verifier identity; in `src/Apps/Sorcha.Wallet.Pwa/Services/Presentation/` + `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Presentation/ConsentSheet.razor`; depends on T055 (+US3 anchors endpoint)
- [ ] T057 [US6] Retire the unsigned inline-parameter deep-link form from all internal producers and the desk/open verifier + Blueprint consumers adopt server-side `RequestObjectValidator` (FR-026 "same signed-request path"); sweep `src/Apps/Sorcha.Verifier/`, `src/Services/Sorcha.Blueprint.Service/`, `Sorcha.UI.Components.User` QR services; depends on T053/T055
- [ ] T058 [US6] `sorcha_request_auth_total{state}` metric (FR-028) + SC-007 integration test (tampered/mismatched refusal, correct state rendering) in `tests/Sorcha.Wallet.Pwa.Tests/Integration/VerifierAuthTests.cs`; depends on T055–T057

**Checkpoint**: wallets authenticate verifiers; all six stories complete.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T059 [P] STANDARDS.md rows: OpenID4VP/OpenID4VCI/HAIP → final-profile versions with honest status; SD-JWT VC note `dc+sd-jwt`; NEW ETSI TS 119 612 `partial` row (snapshot import; live LOTL deferred); verify `scripts/check-discoverability.sh` passes (FR-029)
- [ ] T060 [P] Documentation sync: `docs/reference/API-DOCUMENTATION.md` (trust-list + org-cert + changed verifier surfaces), Tenant/HAIP service READMEs, `.claude/skills/sorcha-architecture/SKILL.md` Feature 181 section, `.claude/skills/verifiable-credentials/SKILL.md` typ/dialect updates, `docs/openid4vc-haip-integration.md`
- [ ] T061 [P] `.specify/MASTER-TASKS.md` status update + spec checklists closed
- [ ] T062 Full quickstart.md validation pass against local Docker (all six US sections + cross-cutting checks); record evidence in the final PR
- [ ] T063 Coverage + warning gate: `dotnet build` warning-free, `dotnet test` green, >85% coverage on new code (`dotnet test --collect:"XPlat Code Coverage"` on the touched test projects)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: none.
- **Phase 2 (Foundational)**: after T001; blocks US1/US2/US6 only — **US3 needs only T003**.
- **US1 (Phase 3)**: after Phase 2. **US2 (Phase 4)**: after US1 (extends its surfaces).
- **US3 (Phase 5)**: after T003 — fully parallel with Phases 3–4.
- **US4 (Phase 6)**: after T003; SC-005 verify leg benefits from US3 (T038 fixtures/anchors).
- **US5 (Phase 7)**: after US4's T040/T042.
- **US6 (Phase 8)**: after US1 + US3 (anchors) — last, as planned.
- **Phase 9**: after all desired stories.

### Story dependency graph

```
Setup ─┬─ Foundational ── US1 ── US2 ──┐
       │                        └──────┼── US6 ── Polish
       └─ (T003) ── US3 ───────────────┤
       └─ (T003) ── US4 ── US5 ────────┘
```

### Parallel Opportunities

- **Two independent tracks after Setup**: dialect track (Phase 2→US1→US2→US6-prep) and trust track
  (US3 ∥ US4→US5). Matches the plan's delivery order.
- Within phases: all [P]-marked tasks touch disjoint files (e.g. T004∥T008∥T002∥T003; T012∥T013∥T017∥T018;
  T031∥T032; T041 alongside T040's EF work; T052 alongside US3 tail).

### Parallel Example: kick-off after T001

```
Track A (dialect):  T004 → T005/T006 → T007 → T010…        (US1)
Track B (trust):    T003 → T031 ∥ T032 → T033 → T034…      (US3)
Track C (certs):    T040 ∥ T041 → T042 → …                 (US4, after T003)
```

---

## Implementation Strategy

**MVP = Phase 1 + Phase 2 + US1** (SC-001/SC-002 demonstrable: the platform speaks the final dialect with
zero user-visible change). Then incremental, checkpoint-validated delivery: US2 → (US3, US4/US5 in
parallel) → US6 → Polish. Each phase lands as its own PR(s) per the repo's branch+PR policy, with the CI
dialect gate ratcheting at US1 completion.

**Per-PR cadence** (user preference): create PR → await review → apply one round of critical fixes → merge
→ next phase.

---

## Notes

- R9 direction confirmed by the platform owner 2026-07-10: certificates bind the org's P-256 key
  (primary ES256 else HAIP co-key); typed exclusion applies only when no P-256 key is resolvable.
- Task count: 63. US1: 14 (T010–T023) · US2: 7 · US3: 9 · US4: 7 · US5: 5 · US6: 7 · Setup/Foundational: 9 · Polish: 5.
- Every task cites exact file paths from research R4/R15 inventories; where a line number is cited it was
  verified at branch time — re-grep if drifted.
