---

description: "Task list for spec 096: X.509 Organisation Trust Integration"
---

# Tasks: X.509 Organisation Trust Integration

**Input**: Design documents from `specs/096-x509-org-trust/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — spec FR-038 mandates unit + integration coverage.

## Phase 1: Setup

- [ ] T001 Confirm `096-x509-org-trust` branch is rebased onto master. Spec 093 must be merged; spec 094 must be merged because Org Cert binding needs `IHaipIssuerCoKeyService`
- [ ] T002 Verify BCL `System.Security.Cryptography.X509Certificates.CertificateRequest` and `CertificateRevocationListBuilder` are available in .NET 10 target
- [ ] T003 [P] Add config keys `Trust:BaseUrl`, `Trust:DefaultCaAlgorithm`, `Trust:DefaultCaValidityYears`, `Trust:DefaultOrgCertValidityYears`, `Trust:CrlRefreshHours` to `src/Services/Sorcha.Tenant.Service/appsettings.json`
- [ ] T004 [P] Add new purpose constant `sorcha:tenant-ca-signing` to the shared constants file from spec 094 T003

## Phase 2: Foundational

- [ ] T005 Baseline: run `dotnet test tests/Sorcha.Tenant.Service.Tests` to confirm the branch builds and pre-change tests pass
- [ ] T006 Create EF migration skeleton for `TenantRootCa`, `OrgCertEnrolment`, `TenantCrl` tables in `src/Services/Sorcha.Tenant.Service/Data/Migrations/`. Folded into the pre-release consolidated migration per user guidance — no runtime migration created

## Phase 3: User Story 1 - Tenant Root CA provisioning (Priority: P1) 🎯 MVP

### Tests for US1

- [ ] T007 [P] [US1] Write failing unit test `BuildSelfSignedRoot_Ed25519_ValidCertDerOutput` in `tests/Sorcha.Tenant.Service.Tests/Trust/X509CertificateBuilderTests.cs`
- [ ] T008 [P] [US1] Write failing unit test `BuildSelfSignedRoot_Es256_ValidCertDerOutput` in the same file
- [ ] T009 [P] [US1] Write failing unit test `BuildSelfSignedRoot_ValidityPeriod_RespectsInput` in the same file
- [ ] T010 [P] [US1] Write failing unit test `Provision_IsIdempotent_ReturnsExistingRoot` in `tests/Sorcha.Tenant.Service.Tests/Trust/InternalCaTrustProviderTests.cs`
- [ ] T011 [P] [US1] Write failing unit test `Provision_LocalSigningMode_StoresKeyLocally` in the same file
- [ ] T012 [P] [US1] Write failing unit test `Provision_KmsResidentSigningMode_DelegatesToKmsProvider` in the same file

### Implementation

- [ ] T013 [US1] Create `TenantRootCa`, `OrgCertEnrolment`, `TenantCrl`, `TrustProviderMode` domain entities in `src/Common/Sorcha.Tenant.Models/Trust/`
- [ ] T014 [US1] Create `ICaKeyProtection` interface and `LocalCaKeyProtection`, `KmsResidentCaKeyProtection` implementations in `src/Services/Sorcha.Tenant.Service/Trust/`
- [ ] T015 [US1] Create `X509CertificateBuilder.cs` with `BuildSelfSignedRoot(algorithm, subject, validityYears)` method using BCL `CertificateRequest.CreateSelfSigned`
- [ ] T016 [US1] Create `ITrustProvider` interface per contracts/README.md
- [ ] T017 [US1] Create `InternalCaTrustProvider` implementing `ProvisionTrustAnchorAsync` — stores the resulting `TenantRootCa` via EF Core, stores the private key via `ICaKeyProtection`
- [ ] T018 [US1] Create `TrustProviderRegistry` for resolving a provider per tenant (default `InternalCaTrustProvider`)
- [ ] T019 [US1] Create `TrustEndpoints.cs` in `src/Services/Sorcha.Tenant.Service/Endpoints/` with `POST /api/v1/trust/tenants/{tenantId}/provision` and `GET /api/v1/trust/tenants/{tenantId}/trust-anchor`
- [ ] T020 [US1] Wire `ITrustProvider`, `TrustProviderRegistry`, `X509CertificateBuilder`, `ICaKeyProtection` in the Tenant Service DI container
- [ ] T021 [US1] Map the new endpoints in `Program.cs`

**Checkpoint**: US1 done. Tenants can provision a self-signed root. Trust anchor URL returns valid DER-encoded cert.

## Phase 4: User Story 2 - Organisation cert enrolment (Priority: P1)

### Tests for US2

- [ ] T022 [P] [US2] Write failing unit test `BuildOrgCert_BindsClassicalHaipIssuerCoKey` in `tests/Sorcha.Tenant.Service.Tests/Trust/X509CertificateBuilderTests.cs`
- [ ] T023 [P] [US2] Write failing unit test `BuildOrgCert_EmbedsSanUri_WithDidSorchaOrgPrefix` in the same file
- [ ] T024 [P] [US2] Write failing unit test `BuildOrgCert_CrlDistributionPoints_PointsAtTenantCrl` in the same file
- [ ] T025 [P] [US2] Write failing unit test `BuildOrgCert_NoEkuExtension_Q42RulingD` in the same file
- [ ] T026 [P] [US2] Write failing unit test `IssueOrgCert_WalletLacksHaipIssuerCapability_FailsWithPrereqError` in `tests/Sorcha.Tenant.Service.Tests/Trust/InternalCaTrustProviderTests.cs`
- [ ] T027 [P] [US2] Write failing integration test `EnrolOrgCert_RoundTrip_FromProvisionToIssue` in `tests/Sorcha.Tenant.Service.IntegrationTests/TrustRoundTripTests.cs`

### Implementation

- [ ] T028 [US2] Extend `X509CertificateBuilder` with `BuildOrgCert(rootCa, rootPrivateKey, publicKey, subject, sanUri, crlDp, validityYears)`
- [ ] T029 [US2] Extend `InternalCaTrustProvider.IssueOrgCertAsync`: check `HaipIssuer` capability, call `IHaipIssuerCoKeyService.GetSigningKeyForHaipIssuanceAsync`, build cert, persist `OrgCertEnrolment`
- [ ] T030 [US2] Add enrolment endpoint `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/enrol` in `TrustEndpoints.cs`
- [ ] T031 [US2] Add `IOrgCertChainProvider` service in `src/Common/Sorcha.ServiceClients.Http/` that fetches the chain (leaf + root) for a given org wallet — used by Wallet Service during HAIP-path issuance

## Phase 5: User Story 3 - x5c embedding on HAIP-path issuance (Priority: P1)

### Tests for US3

- [ ] T032 [P] [US3] Write failing unit test `CreateTokenAsync_WithX5cChain_EmbedsChainInJwsHeader` in `tests/Sorcha.Cryptography.Tests/SdJwt/SdJwtX5cHeaderTests.cs`
- [ ] T033 [P] [US3] Write failing unit test `IssueCredential_HaipPath_FetchesOrgCertChain_AndPassesToSdJwt` in `tests/Sorcha.Wallet.Service.Tests/Endpoints/CredentialEndpointsX5cTests.cs`
- [ ] T034 [P] [US3] Write failing integration test `HaipCredential_X5cChain_VerifiableAgainstTenantRoot` in `tests/Sorcha.Tenant.Service.IntegrationTests/TrustRoundTripTests.cs`

### Implementation

- [ ] T035 [US3] Extend `ISdJwtService.CreateTokenAsync` with a new optional `x5cChain` parameter accepting a list of DER-encoded cert bytes. When present, the chain is serialised into the JWS header's `x5c` array (base64-encoded DER per RFC 7515 §4.1.6)
- [ ] T036 [US3] In `CredentialEndpoints.IssueCredential`, when HAIP-path issuance is in effect, call `IOrgCertChainProvider.GetChainForAsync(walletAddress)` and pass the chain into the SD-JWT service call
- [ ] T037 [US3] Update `IWalletServiceClient.IssueCredentialAsync` and its implementation to forward a new `x5cChain` parameter (or derive it server-side from the wallet address + HAIP flag — planner picks during task execution)

## Phase 6: User Story 4 - CRL publication and revocation (Priority: P1)

### Tests for US4

- [ ] T038 [P] [US4] Write failing unit test `BuildTenantCrl_SignedByRootCa` in `tests/Sorcha.Tenant.Service.Tests/Trust/TenantCrlBuilderTests.cs`
- [ ] T039 [P] [US4] Write failing unit test `BuildTenantCrl_IncludesRevokedSerialNumbers` in the same file
- [ ] T040 [P] [US4] Write failing unit test `RevokeOrgCert_RegeneratesCrl_IncludesNewSerial` in `tests/Sorcha.Tenant.Service.Tests/Trust/InternalCaTrustProviderTests.cs`
- [ ] T041 [P] [US4] Write failing integration test `CrlFetch_AfterRevocation_ReturnsUpdatedCrl_WithinCacheTtl` in `tests/Sorcha.Tenant.Service.IntegrationTests/TrustRoundTripTests.cs`

### Implementation

- [ ] T042 [US4] Create `TenantCrlBuilder.cs` using BCL `CertificateRevocationListBuilder`
- [ ] T043 [US4] Extend `InternalCaTrustProvider.RevokeOrgCertAsync`: mark `OrgCertEnrolment.RevokedAt`, regenerate CRL, persist new version
- [ ] T044 [US4] Extend `InternalCaTrustProvider.PublishCrlAsync`: return the current `TenantCrl` or regenerate if stale
- [ ] T045 [US4] Add endpoints `POST /api/v1/trust/tenants/{tenantId}/orgs/{orgWalletAddress}/revoke` and `GET /api/v1/trust/tenants/{tenantId}/crl` in `TrustEndpoints.cs`. CRL endpoint uses `CachedResult` wrapper with 1-hour TTL
- [ ] T046 [US4] After revocation, the Wallet Service must refuse HAIP-path issuance until re-enrolment. Add a check in `CredentialEndpoints.IssueCredential` (or the chain provider) that calls the Tenant Service to verify the Org Cert is still active

## Phase 7: User Story 5 - Pluggable trust provider swap (Priority: P2)

### Tests for US5

- [ ] T047 [P] [US5] Write failing unit test `MockExternalProvider_IsResolvedByRegistry_WhenRegistered` in `tests/Sorcha.Tenant.Service.Tests/Trust/TrustProviderRegistryTests.cs`
- [ ] T048 [P] [US5] Write failing integration test `ProvisionWithExternalProvider_ReturnsExternallyRootedCert` in `tests/Sorcha.Tenant.Service.IntegrationTests/TrustRoundTripTests.cs`

### Implementation

- [ ] T049 [US5] Verify `TrustProviderRegistry` correctly resolves custom providers from DI when registered before the default
- [ ] T050 [US5] Document the swap mechanism in `src/Services/Sorcha.Tenant.Service/README.md`

## Phase 8: Verifier chain-walk integration (Priority: P1)

### Tests for US6

- [ ] T051 [P] [US6] Write failing unit test `Verifier_WalksX5cChain_AgainstTrustStore_AcceptsValidChain` in `tests/Sorcha.Wallet.Service.Tests/Presentations/PresentationRequestVerificationTests.cs` (extend existing file)
- [ ] T052 [P] [US6] Write failing unit test `Verifier_X5cChain_RootNotInTrustStore_Fails` in the same file
- [ ] T053 [P] [US6] Write failing unit test `Verifier_X5cChain_RevokedOrgCert_Fails_AfterCrlCheck` in the same file
- [ ] T054 [P] [US6] Write failing unit test `Verifier_NoX5c_FallsBackToDidBasedTrust_Unchanged_Spec093Regression` in the same file

### Implementation

- [ ] T055 [US6] Add `ITrustStore` service in `src/Services/Sorcha.Wallet.Service/Services/Interfaces/`
- [ ] T056 [US6] Implement `ConfigurableTrustStore` reading accepted root certs from deployment config
- [ ] T057 [US6] Extend `PresentationRequestService.VerifyPresentationAsync` to detect `x5c` in the JWS header, walk the chain against the trust store, check the CRL for each cert in the chain, and fall back to DID-based trust when no `x5c` is present

## Phase 9: Polish

- [ ] T058 Run `dotnet test tests/Sorcha.Tenant.Service.Tests`
- [ ] T059 Run `dotnet test tests/Sorcha.Tenant.Service.IntegrationTests`
- [ ] T060 Run `dotnet test tests/Sorcha.Wallet.Service.Tests` and confirm spec 093 verifier tests still pass
- [ ] T061 Walk the quickstart.md manual verification procedure
- [ ] T062 [P] Update `src/Services/Sorcha.Tenant.Service/README.md` with the trust anchor and CRL endpoints
- [ ] T063 [P] Update `docs/reference/API-DOCUMENTATION.md` with the new `/api/v1/trust/*` endpoints

## Dependencies

- Phase 1 → Phase 2 → Phase 3 (US1 MVP — provisioning) → Phase 4 (US2 — enrolment) → Phase 5 (US3 — x5c embed) → Phase 6 (US4 — revocation) → Phase 7 (US5 — pluggable) → Phase 8 (US6 — verifier)
- US3 depends on US2 (needs Org Cert to embed)
- US4 depends on US2 (revokes Org Certs)
- US5 can run in parallel with US2-US4 once US1 completes
- US6 depends on US3 (needs `x5c` to be embedded before the verifier has anything to walk)

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T004 | 4 |
| Phase 2 Foundational | T005-T006 | 2 |
| Phase 3 US1 provisioning (MVP) | T007-T021 | 15 |
| Phase 4 US2 org enrolment | T022-T031 | 10 |
| Phase 5 US3 x5c embed | T032-T037 | 6 |
| Phase 6 US4 CRL + revocation | T038-T046 | 9 |
| Phase 7 US5 pluggable provider | T047-T050 | 4 |
| Phase 8 US6 verifier chain walk | T051-T057 | 7 |
| Phase 9 Polish | T058-T063 | 6 |
| **Total** | | **63** |

**Suggested MVP**: Phase 1 + 2 + 3 + 4 + 5 = 37 tasks. Ships provisioning, enrolment, and x5c embedding. Revocation, pluggable swap, and verifier chain-walk can follow as separate increments.
