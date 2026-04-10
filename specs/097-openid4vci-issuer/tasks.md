---

description: "Task list for spec 097: OpenID4VCI Issuer Endpoint (HAIP)"
---

# Tasks: OpenID4VCI Issuer Endpoint (HAIP)

**Input**: Design documents from `specs/097-openid4vci-issuer/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/README.md

**Tests**: Included — spec FR-042 mandates unit + integration coverage for all new behaviour.

## Phase 1: Setup

- [ ] T001 Confirm `097-openid4vci-issuer` branch is rebased onto master with specs 093, 094, 095, 096 all merged
- [ ] T002 Create new project `src/Services/Sorcha.Haip.Service/Sorcha.Haip.Service.csproj` targeting net10.0 with references to Sorcha.ServiceDefaults, Sorcha.ServiceClients, Sorcha.Cryptography, Sorcha.Blueprint.Models, Sorcha.Tenant.Models
- [ ] T003 Create `src/Services/Sorcha.Haip.Service/Program.cs` with Aspire ServiceDefaults, Scalar OpenAPI, Redis client, JWT auth, rate limiting, and health check boilerplate
- [ ] T004 Create `src/Services/Sorcha.Haip.Service/appsettings.json` with HAIP config (IssuerUrl, TokenLifetimeSeconds, NonceLifetimeSeconds, PreAuthCodeLifetimeSeconds)
- [ ] T005 Create `src/Services/Sorcha.Haip.Service/Dockerfile` following the pattern from existing services (multi-stage build, net10.0 runtime)
- [ ] T006 [P] Add `Sorcha.Haip.Service` resource to `src/Apps/Sorcha.AppHost/Program.cs` with Redis dependency and service discovery
- [ ] T007 [P] Add `haip-service` container to `docker-compose.yml` with port 5300 and Redis dependency
- [ ] T008 [P] Add YARP route for `/haip/*` in `src/Services/Sorcha.ApiGateway/appsettings.json` proxying to the HAIP service
- [ ] T009 Create test project `tests/Sorcha.Haip.Service.Tests/Sorcha.Haip.Service.Tests.csproj` with xUnit, FluentAssertions, Moq references
- [ ] T010 Verify the new service builds and starts: `dotnet build src/Services/Sorcha.Haip.Service/`

## Phase 2: Foundational

- [ ] T011 Baseline: confirm all existing test suites that 097 depends on still pass (Sorcha.Cryptography.Tests, Sorcha.Wallet.Service.Tests)
- [ ] T012 Create `src/Services/Sorcha.Haip.Service/Models/CredentialOffer.cs` with all fields per data-model.md (Id, PreAuthorizedCode, IssuerWalletAddress, CredentialType, Claims, DisclosablePaths, TargetTenantId, ExpiresAt, Status enum)
- [ ] T013 [P] Create `src/Services/Sorcha.Haip.Service/Models/TokenRequest.cs` and `TokenResponse.cs` per data-model.md
- [ ] T014 [P] Create `src/Services/Sorcha.Haip.Service/Models/CredentialRequest.cs` per data-model.md (format, proof JWT)
- [ ] T015 [P] Create `src/Services/Sorcha.Haip.Service/Models/IssuerMetadata.cs` per data-model.md
- [ ] T016 Create `src/Services/Sorcha.Haip.Service/Services/PreAuthCodeStore.cs` — Redis-backed store with `StoreAsync(code, offerId, ttl)`, `RedeemAsync(code)` (one-time-use), `GetAsync(code)`
- [ ] T017 [P] Create `src/Services/Sorcha.Haip.Service/Services/NonceStore.cs` — Redis-backed store with `CreateAsync(ttl)`, `ConsumeAsync(nonce)` (one-time-use)
- [ ] T018 Add `TargetAudience` enum (`SorchaInternal`, `HaipExternalWallet`) and field to `src/Common/Sorcha.Blueprint.Models/Credentials/CredentialIssuanceConfig.cs`

## Phase 3: User Story 2 — Issuer metadata endpoint (Priority: P1) 🎯 MVP

**Goal**: A HAIP wallet can discover the issuer's capabilities by fetching `.well-known/openid-credential-issuer`.

### Tests for US2

- [ ] T019 [P] [US2] Write failing test `GetMetadata_ReturnsValidHaipMetadata` in `tests/Sorcha.Haip.Service.Tests/Endpoints/MetadataEndpointTests.cs`
- [ ] T020 [P] [US2] Write failing test `GetMetadata_ContainsCorrectEndpointUrls` in the same file
- [ ] T021 [P] [US2] Write failing test `GetOAuthMetadata_ContainsPreAuthCodeGrantType` in the same file

### Implementation

- [ ] T022 [US2] Create `src/Services/Sorcha.Haip.Service/Endpoints/MetadataEndpoints.cs` with `GET /.well-known/openid-credential-issuer` returning `IssuerMetadata` JSON (credential_issuer, credential_endpoint, token_endpoint, nonce_endpoint, credentials_supported)
- [ ] T023 [US2] Add `GET /.well-known/oauth-authorization-server` returning OAuth AS metadata with grant_types_supported including `urn:ietf:params:oauth:grant-type:pre-authorized_code`
- [ ] T024 [US2] Map metadata endpoints in Program.cs — both are public, AllowAnonymous, cached

## Phase 4: User Story 1 — Pre-authorized code flow (Priority: P1)

**Goal**: A HAIP wallet can exchange a pre-authorized code for an access token, then fetch a credential.

### Tests for US1

- [ ] T025 [P] [US1] Write failing test `TokenEndpoint_ValidPreAuthCode_ReturnsAccessTokenAndCNonce` in `tests/Sorcha.Haip.Service.Tests/Endpoints/TokenEndpointTests.cs`
- [ ] T026 [P] [US1] Write failing test `TokenEndpoint_InvalidPreAuthCode_Returns400` in the same file
- [ ] T027 [P] [US1] Write failing test `TokenEndpoint_ExpiredPreAuthCode_Returns400` in the same file
- [ ] T028 [P] [US1] Write failing test `TokenEndpoint_ReusedPreAuthCode_Returns400` in the same file
- [ ] T029 [P] [US1] Write failing test `NonceEndpoint_ReturnsFreshCNonce` in `tests/Sorcha.Haip.Service.Tests/Endpoints/NonceEndpointTests.cs`
- [ ] T030 [P] [US1] Write failing test `CredentialEndpoint_ValidProof_ReturnsSdJwtVc` in `tests/Sorcha.Haip.Service.Tests/Endpoints/CredentialEndpointTests.cs`
- [ ] T031 [P] [US1] Write failing test `CredentialEndpoint_InvalidProof_Returns400` in the same file
- [ ] T032 [P] [US1] Write failing test `CredentialEndpoint_ExpiredCNonce_Returns400` in the same file
- [ ] T033 [P] [US1] Write failing test `CredentialEndpoint_MissingAccessToken_Returns401` in the same file

### Implementation — Token endpoint

- [ ] T034 [US1] Create `src/Services/Sorcha.Haip.Service/Endpoints/TokenEndpoints.cs` with `POST /token` handler: validate pre-auth code, mark as redeemed, generate access token + c_nonce, store in Redis, return TokenResponse
- [ ] T035 [US1] Register token endpoint in Program.cs — public, AllowAnonymous, rate limited

### Implementation — Nonce endpoint

- [ ] T036 [US1] Create `src/Services/Sorcha.Haip.Service/Endpoints/NonceEndpoints.cs` with `POST /nonce` handler: generate and store a fresh c_nonce, return as JSON
- [ ] T037 [US1] Register nonce endpoint in Program.cs — requires Bearer token

### Implementation — Credential endpoint

- [ ] T038 [US1] Create `src/Services/Sorcha.Haip.Service/Services/JwtProofValidator.cs` — validates the wallet's JWT proof of possession: signature, c_nonce binding, iat clock skew (±60s)
- [ ] T039 [US1] Create `src/Services/Sorcha.Haip.Service/Services/HaipCredentialMinter.cs` — orchestrates: extract holder JWK from proof → call IHaipIssuerCoKeyService for signing key → call ITrustProvider for x5c chain → call ISdJwtService.CreateTokenAsync with holderJwk + x5c → embed IETF status.status_list claim → return signed SD-JWT VC
- [ ] T040 [US1] Create `src/Services/Sorcha.Haip.Service/Endpoints/CredentialEndpoints.cs` with `POST /credential` handler: validate Bearer token, validate JWT proof, call HaipCredentialMinter, return credential response
- [ ] T041 [US1] Register credential endpoint in Program.cs — requires Bearer token, rate limited
- [ ] T042 [US1] Extend `ISdJwtService.CreateTokenAsync` with optional `x5cChain` parameter (list of base64-encoded DER certs) — when present, embed in JWS header per RFC 7515 §4.1.6

## Phase 5: User Story 3 — Blueprint-triggered Credential Offer (Priority: P1)

**Goal**: A Blueprint author adds `TargetAudience: HaipExternalWallet` and the system creates a Credential Offer automatically.

### Tests for US3

- [ ] T043 [P] [US3] Write failing test `CreateOffer_ReturnsOfferUri` in `tests/Sorcha.Haip.Service.Tests/Services/CredentialOfferServiceTests.cs`
- [ ] T044 [P] [US3] Write failing test `CreateOffer_StoresPreAuthCode_InRedis` in the same file
- [ ] T045 [P] [US3] Write failing test `GetOfferStatus_ReturnsCurrentStatus` in the same file

### Implementation — Internal offer API

- [ ] T046 [US3] Create `src/Services/Sorcha.Haip.Service/Services/CredentialOfferService.cs` — creates CredentialOffer with pre-auth code, stores in Redis, returns offer URI (`openid-credential-offer://?credential_offer_uri=...`)
- [ ] T047 [US3] Create internal endpoints in `src/Services/Sorcha.Haip.Service/Endpoints/OfferEndpoints.cs`: `POST /api/v1/offers` (create offer, service-to-service auth) and `GET /api/v1/offers/{offerId}` (get status)
- [ ] T048 [US3] Register offer endpoints in Program.cs — requires service auth

### Implementation — Service client + Blueprint integration

- [ ] T049 [US3] Add `IHaipServiceClient` interface to `src/Common/Sorcha.ServiceClients/` with `CreateCredentialOfferAsync(request)` and `GetOfferStatusAsync(offerId)`
- [ ] T050 [US3] Implement `HaipServiceClient` in `src/Common/Sorcha.ServiceClients.Http/` using the consolidated service client pattern
- [ ] T051 [US3] In `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`, extend the credential issuance path: when `TargetAudience == HaipExternalWallet`, call `IHaipServiceClient.CreateCredentialOfferAsync` instead of the internal Wallet Service issue path, and return the offer URI in the action result
- [ ] T052 [US3] Add `CredentialOfferUri` field to the action execution result model so the UI can render the QR code
- [ ] T053 [US3] Add `MakeDisclosablePath` and `TargetAudience` to `CredentialIssuanceBuilder.cs` fluent API if not already present (spec 094 added `MakeDisclosablePath`; add `ForExternalWallet()` method)

## Phase 6: Polish

- [ ] T054 Run `dotnet test tests/Sorcha.Haip.Service.Tests`
- [ ] T055 Run `dotnet test tests/Sorcha.Cryptography.Tests` — confirm spec 094 SD-JWT tests still pass with x5c extension
- [ ] T056 Run `dotnet test tests/Sorcha.Wallet.Service.Tests` — confirm no regression
- [ ] T057 Walk the quickstart.md manual verification procedure
- [ ] T058 [P] Update `src/Services/Sorcha.Haip.Service/README.md` with service overview, endpoints, and configuration
- [ ] T059 [P] Update `docs/reference/API-DOCUMENTATION.md` with the HAIP endpoint paths
- [ ] T060 [P] Update `docs/getting-started/PORT-CONFIGURATION.md` with port 5300 for HAIP service

## Dependencies

- Phase 1 → Phase 2 → Phase 3 (metadata, MVP baseline) → Phase 4 (token + credential flow) → Phase 5 (Blueprint integration) → Phase 6 (polish)
- Phase 3 (US2 metadata) is independent of Phase 4 (US1 flow) but provides discovery for wallet clients
- Phase 4 (US1) is the core flow and depends on Phase 2 models + Redis stores
- Phase 5 (US3) depends on Phase 4 (needs the credential endpoint to exist before Blueprint can generate offers that wallets redeem)
- T042 (x5c in ISdJwtService) is a cross-cutting change needed by Phase 4 but can be done as a parallel task

## Parallel opportunities

- Phase 1: T006, T007, T008 parallel (AppHost, docker-compose, YARP)
- Phase 2: T013, T014, T015, T017 parallel (independent model files)
- Phase 3: T019-T021 parallel (test files)
- Phase 4: T025-T033 parallel (test files), T034-T037 partially parallel (token vs nonce endpoints)
- Phase 5: T043-T045 parallel (test files)
- Phase 6: T058-T060 parallel (doc updates)

## Task summary

| Phase | Tasks | Count |
|---|---|---|
| Phase 1 Setup | T001-T010 | 10 |
| Phase 2 Foundational | T011-T018 | 8 |
| Phase 3 US2 Metadata | T019-T024 | 6 |
| Phase 4 US1 Pre-auth flow | T025-T042 | 18 |
| Phase 5 US3 Blueprint integration | T043-T053 | 11 |
| Phase 6 Polish | T054-T060 | 7 |
| **Total** | | **60** |

**Suggested MVP**: Phase 1 + 2 + 3 = 24 tasks. Ships the new service scaffold with issuer metadata — proves the service starts, serves discovery, and is routable through the gateway. Phase 4 (the actual credential flow) follows as a separate increment.

**Alternative MVP** (more impactful): Phase 1 + 2 + 3 + 4 = 42 tasks. Ships metadata + the full pre-authorized code → token → credential flow. Blueprint integration (Phase 5) can follow separately.
