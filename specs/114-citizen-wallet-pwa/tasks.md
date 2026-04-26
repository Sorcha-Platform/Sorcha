---
description: "Task list for the Citizen Wallet PWA implementation (Feature 114)"
---

# Tasks: Citizen Wallet PWA

**Input**: Design documents from `/specs/114-citizen-wallet-pwa/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md (all present)
**Companion**: [`docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md`](../../docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md)

**Tests**: Per CLAUDE.md and Constitution IV, unit + integration tests are required for every server-side change (>85% target on new code). E2E tests are deferred to the Polish phase per the user's task-ordering guidance ("E2E tests last").

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. The MVP is **US1 (Present credential offline)**.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1, US2, US3, US4, US5)
- Setup and Foundational tasks have no story label
- Polish tasks have no story label
- Each task includes the exact file path

## Path Conventions

This is a multi-app .NET 10 monorepo. New code lives in `src/Apps/Sorcha.Citizen.Wallet/`, `src/Apps/Sorcha.Citizen.Verifier/`, `src/Common/Sorcha.CitizenWallet.Abstractions/`. Existing services extended in-place under `src/Services/`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding, AppHost registration, Docker plumbing, gateway routing, JWT audience config. Establishes the empty shell that all subsequent phases populate.

- [ ] T001 Create `src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj` as a Blazor WebAssembly standalone project targeting `net10.0`, with `<ServiceWorkerAssetsManifest>service-worker-assets.js</ServiceWorkerAssetsManifest>` and PWA template files (`manifest.webmanifest`, `service-worker.js`, `service-worker.published.js`, `index.html` shell).
- [ ] T002 [P] Create `src/Apps/Sorcha.Citizen.Verifier/Sorcha.Citizen.Verifier.csproj` as a Blazor Server project targeting `net10.0`, with `Program.cs` wiring `AddRazorComponents().AddInteractiveServerComponents()` and ASP.NET Core minimal-API host.
- [ ] T003 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj` as a `net10.0` library, nullable enabled, with the standard Sorcha SPDX header.
- [ ] T004 [P] Create `tests/Sorcha.Citizen.Wallet.Tests/Sorcha.Citizen.Wallet.Tests.csproj` (xUnit + FluentAssertions + Moq, references `Sorcha.Citizen.Wallet`).
- [ ] T005 [P] Create `tests/Sorcha.Citizen.Verifier.Tests/Sorcha.Citizen.Verifier.Tests.csproj` (xUnit, references `Sorcha.Citizen.Verifier`).
- [ ] T006 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Sorcha.Citizen.Wallet.E2E.Tests.csproj` (Playwright NUnit), references `tests/Sorcha.UI.E2E.Tests/Infrastructure/` for `DockerTestBase` reuse.
- [ ] T007 Add the four new projects (`Sorcha.Citizen.Wallet`, `Sorcha.Citizen.Verifier`, `Sorcha.CitizenWallet.Abstractions`, three test projects) to `Sorcha.sln` and confirm `dotnet build` produces no warnings.
- [ ] T008 Modify `src/Apps/Sorcha.AppHost/AppHost.cs` to register `Sorcha.Citizen.Wallet` on Aspire HTTPS port 7400 and `Sorcha.Citizen.Verifier` on 7401, both behind the existing API gateway, with health-check endpoints `/health` and `/alive`.
- [ ] T009 Modify `docker/Dockerfile.citizen-wallet` (NEW) — multi-stage `dotnet publish -c Release` then `nginx:alpine` serving `wwwroot/` with PWA-friendly `Cache-Control` headers (long TTL on hashed asset filenames, short TTL on `index.html` and `service-worker.js`).
- [ ] T010 [P] Modify `docker/Dockerfile.citizen-verifier` (NEW) — standard ASP.NET 10 runtime image pattern matching the other services.
- [ ] T011 Modify `docker-compose.yml` to add `sorcha-citizen-wallet` and `sorcha-citizen-verifier` services with `depends_on: [api-gateway]` and `expose` directives matching plan §7.5.
- [ ] T012 [P] Modify `src/Services/Sorcha.ApiGateway/appsettings.json` to add YARP cluster definitions for `/wallet/*` (→ wallet PWA static host), `/verify/*` (→ reference verifier), `/api/v1/wallet/*` (→ Sorcha.Wallet.Service), `/hubs/wallet` (→ Sorcha.Wallet.Service with WebSocket support).
- [ ] T013 [P] Modify `src/Services/Sorcha.Wallet.Service/appsettings.json` and the JWT validation extension to add `sorcha:citizen-wallet` as an accepted audience for citizen-wallet endpoints.
- [ ] T014 [P] Modify `src/Services/Sorcha.Tenant.Service/appsettings.json` to add `sorcha:citizen-wallet` as an accepted audience for `/me/devices/*` endpoints.

**Checkpoint**: All projects compile, AppHost runs, the new wallet PWA serves a placeholder page on `http://localhost/wallet/`, the reference verifier serves a placeholder page on `http://localhost/verify/`. No business logic yet.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Cross-cutting primitives that every user story depends on — derivation contexts, shared abstractions, base entities, JS-interop bridges, common services. **No user story may begin until this phase is complete.**

### Derivation paths and constants

- [ ] T015 Modify `src/Core/Sorcha.Wallet.Portable/Constants/SorchaDerivationPaths.cs` — add `CitizenHolder = "sorcha:citizen-holder"` + `CitizenHolderPath = "m/44'/0'/0'/0/108"` + `CitizenStatusSigning = "sorcha:citizen-status-signing"` + `CitizenStatusSigningPath = "m/44'/0'/0'/0/109"`. Extend `ResolvePath` switch to handle both. Add full XML doc per existing pattern.
- [ ] T016 [P] Add unit tests `tests/Sorcha.Wallet.Core.Tests/Constants/SorchaDerivationPathsTests.cs` for both new slot resolutions and the full set of existing slots remaining unchanged.

### Shared abstractions library

- [ ] T017 Create `src/Common/Sorcha.CitizenWallet.Abstractions/Constants/DerivationContexts.cs` re-exporting the citizen-holder and citizen-status-signing context strings (consumed by the PWA without taking a dependency on `Sorcha.Wallet.Portable`).
- [ ] T018 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Constants/DelegatedCapabilities.cs` with `public const string PresentationHolderKeyBinding = "presentation.holder-key-binding";`.
- [ ] T019 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Constants/VctUris.cs` with `public const string CitizenDeviceDelegationV1 = "https://sorcha.dev/vc/citizen-device-delegation/v1";`.
- [ ] T020 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Constants/JwtAudiences.cs` with `public const string CitizenWallet = "sorcha:citizen-wallet";`.
- [ ] T021 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/EcP256PublicJwk.cs` — record with `Kty`, `Crv`, `X`, `Y` properties + FluentValidation validator enforcing `Kty == "EC"`, `Crv == "P-256"`, `X` and `Y` non-empty base64url.
- [ ] T022 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DeviceEnrolmentRequest.cs` matching `contracts/openapi-wallet-service.yaml` schema.
- [ ] T023 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DeviceEnrolmentResponse.cs` matching the contract.
- [ ] T024 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DeviceSummary.cs` and `DeviceListResponse.cs` matching contracts.
- [ ] T025 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DeviceLabelUpdateRequest.cs`.
- [ ] T026 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DelegationRenewalRequest.cs` and `DelegationRenewalResponse.cs`.
- [ ] T027 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/SyncResponse.cs` plus nested types `CredentialChanges`, `RevokedCredentialEntry`, `ReplacedCredentialEntry`.
- [ ] T028 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/CachedCredentialPayload.cs`.
- [ ] T029 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/CredentialListResponse.cs`.
- [ ] T030 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/PresentationLogEntry.cs` and `PresentationLogReportRequest.cs`.
- [ ] T031 [P] Create `src/Common/Sorcha.CitizenWallet.Abstractions/Models/DeviceDelegationCredential.cs` — typed wrapper around the SD-JWT VC payload (claims as properties, JSON serialisation matching `device-delegation-credential.schema.json`).
- [ ] T032 [P] Embed `specs/114-citizen-wallet-pwa/contracts/device-delegation-credential.schema.json` as `src/Common/Sorcha.CitizenWallet.Abstractions/Schemas/device-delegation-credential.v1.json` (`<EmbeddedResource>` in csproj).
- [ ] T033 [P] Create FluentValidation validators for every request DTO above, in `src/Common/Sorcha.CitizenWallet.Abstractions/Validators/`.
- [ ] T034 Add unit tests `tests/Sorcha.CitizenWallet.Abstractions.Tests/` (NEW project — also create csproj) covering serialisation round-trips for every model and validator pass/fail cases.

### Service clients

- [ ] T035 Create `src/Common/Sorcha.ServiceClients.Http/CitizenWallet/ICitizenWalletClient.cs` with method signatures matching `openapi-wallet-service.yaml` paths (used by reference verifier and tests). Implementation `CitizenWalletClient.cs` using `HttpClient` per existing service-client pattern.
- [ ] T036 Modify `src/Common/Sorcha.ServiceClients.Http/Extensions/ServiceCollectionExtensions.cs` (`AddServiceClients`) to register `ICitizenWalletClient`.

### Tenant Service — PlatformUserDevice entity

- [ ] T037 Create `src/Services/Sorcha.Tenant.Service/Persistence/Entities/PlatformUserDevice.cs` matching data-model §A1.
- [ ] T038 Create `src/Services/Sorcha.Tenant.Service/Persistence/Entities/PlatformUserDeviceStatus.cs` enum.
- [ ] T039 Create `src/Services/Sorcha.Tenant.Service/Persistence/Configurations/PlatformUserDeviceConfiguration.cs` (EF Core fluent config) — indexes `IX_PlatformUserDevices_PlatformUserId_Status`, `IX_PlatformUserDevices_DevicePublicJwkThumbprint`, `IX_PlatformUserDevices_StatusListIndex`; cascade delete from `PlatformUser`.
- [ ] T040 Modify `src/Services/Sorcha.Tenant.Service/Persistence/TenantDbContext.cs` to register `DbSet<PlatformUserDevice>` and apply the configuration.
- [ ] T041 Generate EF migration `AddPlatformUserDevice` via `dotnet ef migrations add AddPlatformUserDevice --project src/Services/Sorcha.Tenant.Service --context TenantDbContext`. Commit the generated migration files.

### Wallet Service — citizen entities + migrations

- [ ] T042 Create `src/Services/Sorcha.Wallet.Service/Persistence/Entities/CitizenDeviceStatusList.cs` matching data-model §A2.
- [ ] T043 [P] Create `src/Services/Sorcha.Wallet.Service/Persistence/Entities/CitizenWalletSyncCursor.cs` matching data-model §A3, with unique constraint on `(PlatformUserId, PlatformUserDeviceId)`.
- [ ] T044 Create `src/Services/Sorcha.Wallet.Service/Persistence/Configurations/CitizenDeviceStatusListConfiguration.cs`.
- [ ] T045 [P] Create `src/Services/Sorcha.Wallet.Service/Persistence/Configurations/CitizenWalletSyncCursorConfiguration.cs`.
- [ ] T046 Modify `src/Services/Sorcha.Wallet.Service/Persistence/WalletDbContext.cs` to register both new DbSets and apply configurations.
- [ ] T047 Generate EF migration `AddCitizenWalletEntities` via `dotnet ef migrations add AddCitizenWalletEntities --project src/Services/Sorcha.Wallet.Service --context WalletDbContext`. Commit the generated migration files.

### Wallet Service — holder key derivation + base services

- [ ] T048 Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IHolderKeyService.cs` with `Task<HolderKeyMaterial> DeriveOrFetchAsync(Guid platformUserId, CancellationToken ct)` and `Task<JsonWebKey> GetPublicKeyAsync(Guid platformUserId, CancellationToken ct)`.
- [ ] T049 Create `src/Services/Sorcha.Wallet.Service/Services/Implementation/HolderKeyService.cs` using `IWalletDerivation` with `SorchaDerivationPaths.CitizenHolder`. Caches derived holder material in Redis under `sorcha:wallet:holder-key:{platformUserId}` with 24h TTL.
- [ ] T050 [P] Add unit tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/HolderKeyServiceTests.cs` covering derivation determinism, cache hit/miss, and concurrent-derivation safety.
- [ ] T051 Modify `src/Services/Sorcha.Wallet.Service/Extensions/ServiceCollectionExtensions.cs` to register `IHolderKeyService` as scoped.

### Wallet Service — SignalR hub skeleton

- [ ] T052 Create `src/Services/Sorcha.Wallet.Service/Hubs/WalletHub.cs` — Hub class with `OnConnectedAsync` adding the connection to a group keyed by the authenticated `PlatformUserId`. Methods are added in later phases as features need them.
- [ ] T053 Modify `src/Services/Sorcha.Wallet.Service/Program.cs` to call `MapHub<WalletHub>("/hubs/wallet")` with the `RequireService` or citizen-wallet auth policy as appropriate.

### PWA — JS interop bridges

- [ ] T054 Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/indexeddb-bridge.js` exposing `openDb`, `get`, `put`, `delete`, `cursor` for the five object stores (`device`, `delegation`, `credentials`, `statusLists`, `syncQueue`) per data-model §B.
- [ ] T055 [P] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/webcrypto-bridge.js` with helpers: `generateNonExtractableEcdsaP256()`, `generateNonExtractableHmacSha256()`, `signEcdsa(keyHandle, bytes)`, `signHmac(keyHandle, bytes)`, `hkdfSha256(ikm, salt, info, length)`, `aesGcm{Encrypt,Decrypt}(key, nonce, data, aad)`.
- [ ] T056 [P] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/libsodium-bridge.js` loading libsodium-js (from npm via wwwroot static asset or CDN-pinned) and exposing `xchacha20poly1305{Encrypt,Decrypt}(key, nonce, data, aad)`.
- [ ] T057 [P] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/qr-scanner-bridge.js` wrapping `nimiq/qr-scanner` (or equivalent vetted library) with `start(videoElementId, onResult)` and `stop()`.
- [ ] T058 Reference all four JS modules from `src/Apps/Sorcha.Citizen.Wallet/wwwroot/index.html` as `<script type="module">` imports.

### PWA — base services and DI

- [ ] T059 Create `src/Apps/Sorcha.Citizen.Wallet/Services/IDeviceKeyService.cs` and `Services/Implementation/DeviceKeyService.cs` — wraps the WebCrypto bridge, persists key handles in IndexedDB `device` store, exposes signing operations and the JWK thumbprint.
- [ ] T060 [P] Create `src/Apps/Sorcha.Citizen.Wallet/Services/ICredentialCache.cs` and `Services/Implementation/CredentialCache.cs` — wraps IndexedDB `credentials` store, encrypts/decrypts via XChaCha20-Poly1305 with content key from `IDeviceKeyService`.
- [ ] T061 [P] Create `src/Apps/Sorcha.Citizen.Wallet/Services/IDelegationStore.cs` and `Services/Implementation/DelegationStore.cs` — wraps IndexedDB `delegation` store.
- [ ] T062 [P] Create `src/Apps/Sorcha.Citizen.Wallet/Services/IStatusListService.cs` and `Services/Implementation/StatusListService.cs` — wraps IndexedDB `statusLists` store, fetches signed status list JWTs over HTTP, validates signature against issuer's public key, exposes `IsRevoked(uri, idx)`.
- [ ] T063 [P] Create `src/Apps/Sorcha.Citizen.Wallet/Services/ICitizenAuthService.cs` and `Services/Implementation/CitizenAuthService.cs` — wraps the existing Sorcha auth client to acquire JWTs scoped with audience `sorcha:citizen-wallet`. Caches token in `sessionStorage`; uses refresh tokens via existing flow.
- [ ] T064 Create `src/Apps/Sorcha.Citizen.Wallet/Auth/CitizenAuthorizationMessageHandler.cs` — `DelegatingHandler` that attaches `Authorization: Bearer {token}` from `ICitizenAuthService` to all outgoing `HttpClient` requests.
- [ ] T065 Create `src/Apps/Sorcha.Citizen.Wallet/Extensions/ServiceCollectionExtensions.cs` — `AddCitizenWalletServices` registering all IPWA services as scoped (per CLAUDE.md DI lifetime guidance) plus the typed HTTP client with the auth handler.
- [ ] T066 Create `src/Apps/Sorcha.Citizen.Wallet/Program.cs` minimal Blazor WASM startup — `WebAssemblyHostBuilder`, configure root component, register services, configure logging.

### PWA — shared layout + theming

- [ ] T067 Create `src/Apps/Sorcha.Citizen.Wallet/Components/Layout/MainLayout.razor` — wallet shell using `Sorcha.UI.Core` MudBlazor primitives, top app bar with lock + sign-out, bottom nav (Home / Devices / Activity / Settings).
- [ ] T068 [P] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/manifest.webmanifest` with `name=Sorcha Wallet`, `display=standalone`, theme-colour matching `identity-navy`, icons (placeholder pending T120 design pass), `scope=/wallet/`, `start_url=/wallet/`.
- [ ] T069 [P] Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/icons/` with placeholder 192x192, 512x512, and 192x192-maskable icons (final assets in T120).

### PWA — service worker scaffolding

- [ ] T070 Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.js` (dev) — passthrough no-cache.
- [ ] T071 Create `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.published.js` (prod) — Blazor PWA template precache + custom handlers for `sync` events (delegation-renewal, status-list-refresh, presentation-log-flush) registered in later phases.

### Reference verifier — base scaffolding

- [ ] T072 Create `src/Apps/Sorcha.Citizen.Verifier/Services/IStatusListCache.cs` and `Services/Implementation/StatusListCache.cs` — fetches signed status lists, caches with TTL = `iat + 24h` per status list JWT, `IsRevoked(uri, idx)` evaluates against cached bitstrings.
- [ ] T073 Create `src/Apps/Sorcha.Citizen.Verifier/Extensions/ServiceCollectionExtensions.cs` registering verifier services as scoped.
- [ ] T074 Create `src/Apps/Sorcha.Citizen.Verifier/Program.cs` minimal Blazor Server startup, MapRazorComponents, MapEndpoints, AddOpenTelemetry per Sorcha pattern.

**Checkpoint**: All projects compile, all migrations apply cleanly to a fresh dev database, the PWA loads its shell with the bottom nav rendering, the verifier serves a placeholder index, no business endpoints yet.

---

## Phase 3: User Story 1 — Present a credential to a verifier with no network (Priority: P1) 🎯 MVP

**Goal**: A pre-enrolled citizen with a cached credential can scan a verifier's QR, approve the disclosure, and have the verifier accept the presentation — entirely offline.

**Independent Test**: With a fixture-seeded device key + fixture-seeded credential + reference verifier running, scanning the verifier's QR completes the full present flow with both browser contexts in `setOffline(true)` mode and the verifier reports acceptance.

### Server side — issuance of delegation credentials

- [ ] T075 [US1] Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IDeviceDelegationIssuer.cs` with `Task<DeviceDelegationResult> IssueAsync(Guid platformUserId, EcP256PublicJwk devicePublicJwk, string deviceLabel, string platform, CancellationToken ct)`.
- [ ] T076 [US1] Create `src/Services/Sorcha.Wallet.Service/Services/Implementation/DeviceDelegationIssuer.cs` — composes the SD-JWT VC payload per data-model §C1, signs with the holder key (ES256), bounded to 12 months, populates `status.status_list` from the allocated bit (T079).
- [ ] T077 [P] [US1] Add unit tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/DeviceDelegationIssuerTests.cs` — verify schema conformance, signature validity, claim shapes, and status-list URI population.

### Server side — status list publisher

- [ ] T078 [US1] Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenStatusListPublisher.cs` with `Task<int> AllocateIndexAsync(Guid orgId, CancellationToken ct)`, `Task FlipAsync(Guid orgId, int index, CancellationToken ct)`, `Task<string> GetSignedListAsync(Guid orgId, int listId, CancellationToken ct)`, `Task RegenerateAsync(Guid orgId, int listId, CancellationToken ct)`.
- [ ] T079 [US1] Create `src/Services/Sorcha.Wallet.Service/Services/Implementation/CitizenStatusListPublisher.cs` implementing the IETF Token Status List 2024 format per research §R-003. Bitstring stored in Postgres `CitizenDeviceStatusList`; signed JWT regenerated on every flip + on hourly cadence; signed with `sorcha:citizen-status-signing` (slot 109).
- [ ] T080 [P] [US1] Create `src/Services/Sorcha.Wallet.Service/Hosted/CitizenStatusListPublisherService.cs` — `BackgroundService` that scans active orgs every hour and re-signs each org's lists if they're within 1h of `exp`. Uses `IServiceScopeFactory` per CLAUDE.md singleton-DI guidance.
- [ ] T081 [P] [US1] Add unit tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenStatusListPublisherTests.cs` — bit allocation monotonicity, regeneration on flip, JWT signature verification, capacity-overflow rolling to next list.
- [ ] T082 [US1] Create `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenStatusListEndpoints.cs` — public `GET /api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt` returning `application/statuslist+jwt`, `Cache-Control: public, max-age=<exp-now>`. No auth.
- [ ] T083 [P] [US1] Add WebApplicationFactory integration tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenStatusListEndpointsTests.cs` — list fetch, 404 on unknown list, content-type, cache headers.

### Server side — device enrolment endpoint (needed so US1 fixture devices can be created)

- [ ] T084 [US1] Create `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` skeleton (Minimal API mapping group `/api/v1/wallet`). Initial endpoint: `POST /devices/enrol` — calls `IHolderKeyService` + `IDeviceDelegationIssuer` + Tenant Service (writes `PlatformUserDevice`) + status list allocate + returns `DeviceEnrolmentResponse`. Wired with FluentValidation on the request DTO. `RequireRateLimiting(RateLimitPolicies.Strict)` per existing pattern. OpenAPI `.WithSummary` + `.WithDescription` per Constitution III.
- [ ] T085 [P] [US1] Add WebApplicationFactory integration tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/EnrolEndpointTests.cs` — happy path, duplicate JWK thumbprint (409), invalid JWK shape (400), JWT audience mismatch (403).
- [ ] T086 [US1] Modify `src/Services/Sorcha.Tenant.Service/Services/Implementation/PlatformUserDeviceService.cs` (NEW — interface + impl) — single method `Task<PlatformUserDevice> RegisterAsync(...)` called by Wallet Service over service-to-service auth. Idempotent on `(PlatformUserId, DevicePublicJwkThumbprint)`.
- [ ] T087 [P] [US1] Add unit tests `tests/Sorcha.Tenant.Service.Tests/PlatformUserDevice/PlatformUserDeviceServiceTests.cs` for register-idempotency.

### Reference verifier — full pipeline (server side)

- [ ] T088 [US1] Create `src/Apps/Sorcha.Citizen.Verifier/Services/IPresentationRequestBuilder.cs` and `Services/Implementation/PresentationRequestBuilder.cs` — builds OID4VP presentation requests per research §R-008. Output: a serialised `openid4vp://` deep link with inline `request` JWT (offline mode) or `request_uri` (online mode).
- [ ] T089 [US1] Create `src/Apps/Sorcha.Citizen.Verifier/Services/IVerifiablePresentationValidator.cs` and `Services/Implementation/VerifiablePresentationValidator.cs` — validates the complete chain offline: issuer signature (cached or pinned), holder→device delegation signature, KB-JWT signature, status-list bits via `IStatusListCache`, claim disclosure consistency.
- [ ] T090 [P] [US1] Add unit tests `tests/Sorcha.Citizen.Verifier.Tests/Services/VerifiablePresentationValidatorTests.cs` — golden VP samples, malformed VP (rejected), revoked-device VP (rejected), expired-delegation VP (rejected), tampered KB-JWT (rejected), stale status list within window (accepted).
- [ ] T091 [US1] Create `src/Apps/Sorcha.Citizen.Verifier/Endpoints/PresentationResponseEndpoints.cs` — `POST /verify/r/{sessionId}/response` accepts the wallet's POSTed VP, validates via `IVerifiablePresentationValidator`, stores the outcome in an in-memory + Redis-backed session store, returns 204.

### Reference verifier — UI

- [ ] T092 [US1] Create `src/Apps/Sorcha.Citizen.Verifier/Pages/VerifierSession.razor` — `/verify/{verifierOrgId}/{purpose}` page, requests presentation definition for the purpose, generates request via `IPresentationRequestBuilder`, renders QR via `qrcode.js`, polls or SignalR-listens for the outcome.
- [ ] T093 [P] [US1] Create `src/Apps/Sorcha.Citizen.Verifier/Pages/Outcome.razor` — displays accepted/rejected outcome with disclosed claims summary (the one screen the verifier operator sees post-verification).

### PWA — presentation engine

- [ ] T094 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Services/IPresentationEngine.cs` and `Services/Implementation/PresentationEngine.cs` with: `Task<ParsedPresentationRequest> ParseAsync(string openid4vpDeepLink)`, `IReadOnlyList<CredentialMatch> Match(ParsedPresentationRequest req)`, `Task<string> BuildPresentationAsync(CredentialMatch chosen, IReadOnlyList<string> approvedClaims, ParsedPresentationRequest req)`. Internally: PEX/DCQL matching, selective-disclosure salt redaction, KB-JWT signing via `IDeviceKeyService`.
- [ ] T095 [P] [US1] Add unit tests `tests/Sorcha.Citizen.Wallet.Tests/Services/PresentationEngineTests.cs` — parse golden requests, match against fixture credentials, KB-JWT signature validity, replay-cache rejection of duplicate `(nonce, aud)`.

### PWA — present page + consent UX

- [ ] T096 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/Present.razor` — `/wallet/present` route, mounts QR scanner via `qr-scanner-bridge.js`, shows live camera, on QR detect routes to consent screen.
- [ ] T097 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Components/ConsentSheet.razor` — receives `ParsedPresentationRequest` + matched credential, renders mandatory claims (pre-checked + locked) + optional claims (off by default, citizen-toggleable), exposes "Hold to share" gesture (per FR-015 explicit deliberate confirmation).
- [ ] T098 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Components/CredentialPickerDialog.razor` — shown when multiple credentials match (FR-019); citizen selects one before consent sheet.
- [ ] T099 [US1] Create `src/Apps/Sorcha.Citizen.Wallet/Components/NoMatchingCredentialDialog.razor` — shown when no credential matches (FR-018); offers Cancel and "Why doesn't this work?" explanation.
- [ ] T100 [US1] Wire `Present.razor` end-to-end: scan → parse → match → (picker if needed) → consent → build VP → POST to `response_uri` (or display QR back if `direct_post.qr` mode) → success/failure feedback.

### PWA — clock skew + expiry safety

- [ ] T101 [US1] Add device-clock-skew check in `Present.razor` (compare device clock to last server response time during sync; if delta > 5 min, banner per FR-020 "your wallet thinks the time is wrong"); refuses to present expired credentials with explicit warning.

**Checkpoint**: With a fixture-seeded device + credential, the citizen can scan the reference verifier's QR offline and complete a successful presentation. US1 acceptance scenarios 1, 2, 3, 4, 5 from spec all demonstrable.

---

## Phase 4: User Story 2 — Enrol a new device and load credentials onto it (Priority: P2)

**Goal**: A citizen with an existing platform account can install the wallet, sign in with their normal credentials, complete enrolment, and see their credentials available offline within 5 minutes.

**Independent Test**: From a clean install on a clean device with a pre-existing platform account holding ≥1 credential, complete the enrolment wizard and verify all credentials appear in the wallet home screen and remain available after `setOffline(true)`.

### Server side — sync + credentials list endpoints

- [ ] T102 [US2] Modify `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` to add `GET /api/v1/wallet/credentials` (full snapshot — used for first-load) and `GET /api/v1/wallet/sync` (delta since opaque token).
- [ ] T103 [US2] Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenSyncService.cs` and `Services/Implementation/CitizenSyncService.cs` — composes the credential delta from the user's credential-events stream, the delegation-renewal status, and the status-list refresh hints. Sync token is a JWT signed by the Wallet Service per research §R-006.
- [ ] T104 [P] [US2] Add unit tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenSyncServiceTests.cs` — first-sync (no token), incremental sync, sync-token-too-old (410), delegation-near-expiry triggers renewal in response, replaced credential delta shape.
- [ ] T105 [P] [US2] Add WebApplicationFactory integration tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/SyncEndpointTests.cs`.
- [ ] T106 [US2] Modify `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` to add `POST /api/v1/wallet/devices/renew-delegation` — silent renewal endpoint, idempotent within renewal window.

### PWA — sync service

- [ ] T107 [US2] Create `src/Apps/Sorcha.Citizen.Wallet/Services/ISyncService.cs` and `Services/Implementation/SyncService.cs` — calls `/wallet/sync` on focus regain, persists new sync token, applies deltas to `ICredentialCache` and `IDelegationStore` and `IStatusListService`.
- [ ] T108 [P] [US2] Add unit tests `tests/Sorcha.Citizen.Wallet.Tests/Services/SyncServiceTests.cs` covering merge logic, sync-token persistence, 410 → full re-sync recovery.

### PWA — enrolment wizard

- [ ] T109 [US2] Create `src/Apps/Sorcha.Citizen.Wallet/Components/EnrolmentWizard.razor` — multi-step wizard: (1) sign-in (delegate to existing Sorcha auth flow), (2) device label entry (default suggestion per research §R-005), (3) generate device keys via `IDeviceKeyService`, (4) call `/devices/enrol`, (5) initial `/credentials` pull, (6) success.
- [ ] T110 [US2] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/Home.razor` — `/wallet/` route. If not enrolled → routes to `EnrolmentWizard`. If enrolled → renders credential list using the Feature 107 `IdCardLayout` component (referenced via `Sorcha.UI.Core`).
- [ ] T111 [P] [US2] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/CredentialDetail.razor` — `/wallet/credentials/{id}` route, full id-card detail view with all attributes, expiry, issuer info.
- [ ] T112 [P] [US2] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/Settings.razor` — `/wallet/settings` route, exposes Lock now, Sign out, Storage usage (`navigator.storage.estimate()`), local data clear.

**Checkpoint**: A new citizen can install, sign in, enrol, and see their credentials. The credentials persist across app close + reopen; offline browse works; the device shows up in the user's `PlatformUserDevices` list.

---

## Phase 5: User Story 3 — Recover after losing a device (Priority: P2)

**Goal**: A citizen who has lost their device can sign in elsewhere, revoke the device, and immediately enrol a new device with all their credentials available — without re-issuance and without any wallet-specific recovery secret.

**Independent Test**: Enrol a wallet on a test device, simulate loss (clear browser data), sign in via the existing Sorcha web UI on a fresh browser, revoke the lost device, enrol on a new device, confirm credentials are present and the lost device's presentations are subsequently rejected by the verifier.

### Server side — device list + revoke endpoints

- [ ] T113 [US3] Create `src/Services/Sorcha.Tenant.Service/Endpoints/PlatformUserDeviceEndpoints.cs` — `GET /api/v1/me/devices` (list) + `DELETE /api/v1/me/devices/{deviceId}` (revoke) per `contracts/openapi-tenant-service.yaml`. Cross-checks ownership; 404 if not the caller's device.
- [ ] T114 [US3] Modify `src/Services/Sorcha.Tenant.Service/Services/Implementation/PlatformUserDeviceService.cs` to add `Task RevokeAsync(Guid platformUserId, Guid deviceId, CancellationToken ct)` — sets `Status=Revoked`, `RevokedAt=now`, `RevokedByPlatformUserId=caller`, then dispatches a service-to-service call to Wallet Service to flip the status-list bit.
- [ ] T115 [P] [US3] Add WebApplicationFactory integration tests `tests/Sorcha.Tenant.Service.Tests/PlatformUserDevice/PlatformUserDeviceEndpointsTests.cs` — list + revoke happy paths, ownership 404, double-revoke idempotency, revocation propagation invocation.
- [ ] T116 [US3] Modify `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` to add `DELETE /api/v1/wallet/devices/{deviceId}` — invokes `ICitizenStatusListPublisher.FlipAsync` + regenerates the signed list + publishes a `deviceRevoked` SignalR event via `WalletHub`.
- [ ] T117 [P] [US3] Add WebApplicationFactory integration tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/DeviceRevokeEndpointTests.cs` — bit flips, list re-signs, SignalR group receives event.
- [ ] T118 [US3] Modify `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` to add `GET /api/v1/wallet/devices` (list, mirror of Tenant's `/me/devices` — used by the wallet PWA itself which talks to the Wallet Service rather than Tenant) and `PUT /api/v1/wallet/devices/{deviceId}/label`.
- [ ] T119 [US3] Modify `src/Services/Sorcha.Wallet.Service/Hubs/WalletHub.cs` to add a typed event `DeviceRevoked(Guid deviceId)` published to the user's group. The wallet PWA listens and locks itself if its own device id matches.

### PWA — device manager page

- [ ] T120 [US3] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/Devices.razor` — `/wallet/devices` route, lists devices with label / platform / enrolledAt / status. Per-row actions: rename (calls `PUT /devices/{id}/label`), revoke (calls `DELETE /devices/{id}`, with confirm dialog).

### Sorcha.UI.Web — additive My Devices page

- [ ] T121 [US3] Create `src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Pages/MyDevices.razor` — new additive page in the existing Sorcha.UI.Web (does NOT modify any existing page; FR-035 holds). Calls `GET /api/v1/me/devices`, surfaces the same list + revoke action. Linked from the existing MyProfile menu (one new menu entry).
- [ ] T122 [P] [US3] Add E2E coverage for the additive MyDevices page in `tests/Sorcha.UI.E2E.Tests/Docker/MyDevicesTests.cs` (extends existing UI test pattern, not the wallet test pattern).

**Checkpoint**: Recovery flow works end to end. Citizen can revoke from either wallet or main UI; lost device is rejected by verifier within the documented status-list refresh interval; new device enrols + recovers credentials with no re-issuance.

---

## Phase 6: User Story 4 — Receive a newly-issued credential automatically (Priority: P3)

**Goal**: When a credential is issued to a citizen via existing Sorcha flows, it appears in the wallet automatically (via SignalR push if the wallet is open, else on next app open).

**Independent Test**: With a wallet open and online, complete an issuance flow in the existing Sorcha web UI; the new credential appears in the wallet without explicit refresh within a short delay (< 5 seconds). Repeat with the wallet closed; the credential appears on next open.

### Server side — push notification on issuance

- [ ] T123 [US4] Identify all current Sorcha credential-issuance code paths (Feature 097 OpenID4VCI issuer, Feature 107 Assured Identity issuance, Feature 103 Verified Citizen issuance, generic `Wallet.Service` issuance pipeline). Add a single notification call after each successful issuance: `await _walletHub.NotifyCredentialAvailable(platformUserId, credentialId, ct);`.
- [ ] T124 [US4] Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/IWalletHubNotifier.cs` and implementation that wraps `IHubContext<WalletHub>` and broadcasts `CredentialAvailable(credentialId)` to the user's group (uses `IServiceScopeFactory` per CLAUDE.md singleton-DI guidance).
- [ ] T125 [P] [US4] Add unit tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/WalletHubNotifierTests.cs` — group routing, fire-and-forget safety on disconnected groups.
- [ ] T126 [P] [US4] Add integration test `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/SignalRPushIntegrationTests.cs` using a `TestServer`-hosted hub and a SignalR test client to confirm the push reaches a connected group member.

### PWA — SignalR client + sync trigger

- [ ] T127 [US4] Add `Microsoft.AspNetCore.SignalR.Client` PackageReference to `Sorcha.Citizen.Wallet.csproj`.
- [ ] T128 [US4] Create `src/Apps/Sorcha.Citizen.Wallet/Services/IWalletHubClient.cs` and `Services/Implementation/WalletHubClient.cs` — connects to `/hubs/wallet` with the citizen JWT, subscribes to `CredentialAvailable` and `DeviceRevoked` events, exposes `IObservable<HubEvent>` for the app to consume.
- [ ] T129 [US4] Wire `WalletHubClient` into `Home.razor` — on `CredentialAvailable`, invoke `ISyncService.SyncAsync()`; on `DeviceRevoked` matching the local device id, lock the wallet and route to a "this device was revoked" terminal screen.

### Service worker — background sync (Chromium)

- [ ] T130 [US4] Modify `src/Apps/Sorcha.Citizen.Wallet/wwwroot/service-worker.published.js` to register `periodicSync` events with tag `wallet-sync-tick` (1h interval where supported). Handler invokes a fetch to `/api/v1/wallet/sync` with the stored sync token and writes the response back into IndexedDB.
- [ ] T131 [P] [US4] Document in `quickstart.md` Troubleshooting section that Safari/Firefox fall back to "sync runs on app open" (already in v1 quickstart; verify it remains accurate after this implementation).

**Checkpoint**: New credentials appear in the wallet without explicit refresh — within seconds when SignalR-connected, on next open otherwise. Wallet locks itself if the user revokes its own device from another surface.

---

## Phase 7: User Story 5 — Review what was presented, when, to whom (Priority: P3)

**Goal**: Citizen has a complete chronological log of their presentation activity, viewable offline, synced to the platform when online so the same history appears across surfaces.

**Independent Test**: Make 3 presentations (some online, some offline), open the wallet's Activity view, verify all 3 appear with correct credential / claims / verifier label / timestamp. After regaining network, confirm the same 3 entries are reported back to the platform's lifecycle records on the originating registers.

### Server side — Blueprint Service offline consumer

- [ ] T132 [US5] Create `src/Services/Sorcha.Blueprint.Service/Services/Implementation/OfflinePresentationConsumer.cs` implementing `IPresentationConsumer` per `contracts/presentation-lifecycle-offline-extension.md`. Writes `PresentationInitiated` + `PresentationOutcome` transactions on the originating register, preserving offline timestamps; tags `kind` with `-late` suffix when older than `AcceptOfflinePresentationsWithinSeconds`.
- [ ] T133 [US5] Modify `src/Common/Sorcha.Blueprint.Models/PresentationConfig.cs` to add `public int AcceptOfflinePresentationsWithinSeconds { get; set; } = 600;` (additive — existing blueprints unchanged).
- [ ] T134 [US5] Modify `src/Services/Sorcha.Blueprint.Service/Extensions/ServiceCollectionExtensions.cs` to register `OfflinePresentationConsumer` against the existing consumer registry under name `offline-oid4vp`.
- [ ] T135 [P] [US5] Add unit tests `tests/Sorcha.Blueprint.Service.Tests/OfflinePresentation/OfflinePresentationConsumerTests.cs` — happy-path lifecycle write, late-arrival tagging, idempotency on duplicate `presentationLogEntryId`, decline outcome path.

### Server side — Wallet Service forwarding

- [ ] T136 [US5] Create `src/Services/Sorcha.Wallet.Service/Services/Interfaces/ICitizenPresentationLogReporter.cs` and `Services/Implementation/CitizenPresentationLogReporter.cs` — receives reports from the wallet, forwards each entry to Blueprint Service via the existing service-to-service auth, performs Redis SET-NX dedupe under `sorcha:wallet:presentation-log-dedupe:{logEntryId}` with 24h TTL.
- [ ] T137 [US5] Modify `src/Services/Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs` to add `POST /api/v1/wallet/presentations/log` accepting `PresentationLogReportRequest`, returning 202 Accepted; dispatches to `ICitizenPresentationLogReporter` async via `IServiceScopeFactory`.
- [ ] T138 [P] [US5] Add WebApplicationFactory integration tests `tests/Sorcha.Wallet.Service.Tests/CitizenWallet/PresentationLogEndpointTests.cs` — accepts batch, dedupes duplicates, malformed entries return 400.

### PWA — presentation log writer + sync queue

- [ ] T139 [US5] Create `src/Apps/Sorcha.Citizen.Wallet/Services/IPresentationLog.cs` and `Services/Implementation/PresentationLog.cs` — appends entries to IndexedDB `credentials`-store-adjacent `presentationLog` table (data-model §B5 syncQueue + a separate `presentationLog` for human-visible entries; clarify in implementation), exposes `IReadOnlyList<PresentationLogEntry> GetRecent(int count)`, `Task DeleteAsync(Guid id)`.
- [ ] T140 [US5] Modify `Present.razor` (T100) to call `IPresentationLog.AppendAsync(...)` after every successful presentation, and to enqueue a sync-queue entry for later upload.
- [ ] T141 [US5] Modify `ISyncService` (T107) to drain the `presentationLog` sync queue on every successful sync — POST batch to `/api/v1/wallet/presentations/log`, mark entries as `syncedToServer=true` on 202.

### PWA — Activity page

- [ ] T142 [US5] Create `src/Apps/Sorcha.Citizen.Wallet/Pages/Activity.razor` — `/wallet/activity` route, lists local entries chronologically with credential / disclosed claims / verifier label / timestamp / sync status.
- [ ] T143 [P] [US5] Per-row delete action with explicit messaging that platform-side records are unaffected (per FR-031 + spec User Story 5 acceptance scenario 3).

**Checkpoint**: All 5 user stories functionally complete. Wallet is feature-complete for v1 scope.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: E2E test coverage (per user instruction "E2E tests last"), documentation propagation, observability hardening, performance verification, and security review.

### End-to-end Playwright coverage

- [ ] T144 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Infrastructure/CitizenWalletDockerTestBase.cs` extending `DockerTestBase` from `tests/Sorcha.UI.E2E.Tests/Infrastructure/`. Adds two-context support (citizen + verifier), helper methods for offline toggling.
- [ ] T145 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Infrastructure/TestFixtures.cs` — seeded citizen account, seeded credentials, helper for pre-enrolled device state.
- [ ] T146 [P] Create page objects in `tests/Sorcha.Citizen.Wallet.E2E.Tests/PageObjects/`: `CitizenWalletPage.cs`, `EnrolmentPage.cs`, `PresentPage.cs`, `DevicesPage.cs`, `ActivityPage.cs` per the `sorcha-ui` skill pattern.
- [ ] T147 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/PageObjects/Verifier/VerifierSessionPage.cs`.
- [ ] T148 Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/EnrolmentTests.cs` — covers US2 acceptance scenarios end-to-end (install → sign in → enrol → credentials present after offline).
- [ ] T149 Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/PresentationFlowTests.cs` — covers US1 acceptance scenarios end-to-end. **Includes the canonical offline E2E test from quickstart §7**: `setOffline(true)` on both contexts, citizen scans verifier QR, full present flow completes, both regain network, lifecycle event written to register with offline timestamps preserved.
- [ ] T150 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/RecoveryFlowTests.cs` — covers US3: enrol device A, revoke device A from device B, attempt presentation from device A → rejected by verifier (with refreshed status list), enrol device C, credentials present.
- [ ] T151 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/AutoReceiveTests.cs` — covers US4: open wallet, issue credential via existing UI, credential appears in wallet within 5 seconds via SignalR push.
- [ ] T152 [P] Create `tests/Sorcha.Citizen.Wallet.E2E.Tests/Docker/ActivityLogTests.cs` — covers US5: make 3 presentations (mix of online/offline), assert all 3 appear in Activity, regain network, assert lifecycle records appear server-side.

### Documentation propagation

- [ ] T153 [P] Modify `docs/reference/PORT-CONFIGURATION.md` to add ports 7400 (Sorcha.Citizen.Wallet) and 7401 (Sorcha.Citizen.Verifier), plus the new gateway routes.
- [ ] T154 [P] Modify `docs/reference/API-DOCUMENTATION.md` to append the new `/api/v1/wallet/*`, `/api/v1/me/devices/*`, and `/api/v1/wallet/status/*` endpoints. Reference the OpenAPI YAML files in `specs/114-citizen-wallet-pwa/contracts/` as canonical.
- [ ] T155 [P] Modify `docs/reference/architecture.md` to add the wallet topology (citizen device + reference verifier + the three extended services), updating the diagram if present.
- [ ] T156 [P] Modify `docs/reference/development-status.md` — set Citizen Wallet to "100% v1 complete" once all prior tasks are done.
- [ ] T157 [P] Modify `.claude/skills/sorcha-architecture/SKILL.md` to add a new "Citizen Wallet PWA (Feature 114)" section: holder key derivation context, device delegation credential VCT, status list URL pattern, OfflinePresentationConsumer name. Cross-reference the design doc and spec.
- [ ] T158 [P] Modify `.claude/skills/verifiable-credentials/SKILL.md` to add OID4VP holder + offline presentation guidance pointing at the new `IPresentationEngine` and `IVerifiablePresentationValidator`.
- [ ] T159 [P] Modify `.claude/skills/blazor/SKILL.md` to add a PWA + service worker scope subsection referencing `Sorcha.Citizen.Wallet` as the canonical example.
- [ ] T160 [P] Modify `.claude/skills/sorcha-ui/SKILL.md` to add citizen-wallet E2E test patterns (cross-context two-browser harness, offline toggling).

### Observability + performance + security

- [ ] T161 [P] Add OpenTelemetry traces and metrics to all new endpoints — span names per Sorcha convention, custom metrics for sync delta size, presentation success rate, status list refresh cadence.
- [ ] T162 [P] Add structured-logging conventions to all new services — `ILogger<T>` with named placeholders, no string interpolation, consistent event ids.
- [ ] T163 [P] Performance verification — measure WASM bundle size after `dotnet publish -c Release`. Target < 5 MB gzipped first load. If exceeded, audit JS bridge dependencies + consider AOT for Phase 4 only.
- [ ] T164 [P] Security review — manually walk through OWASP Top 10 against the new endpoints + the JS interop bridges. Document findings in `specs/114-citizen-wallet-pwa/SECURITY-REVIEW.md`. Specific checks: JWT audience scoping, IndexedDB at-rest verification (try to read without device key), QR replay-cache effectiveness, status-list signature trust pinning.
- [ ] T165 [P] Run `dotnet test` over the full solution and confirm >85% coverage on all new code paths per Constitution IV. Generate coverage report and attach to PR.
- [ ] T166 Run the full quickstart (`specs/114-citizen-wallet-pwa/quickstart.md`) end-to-end manually on at least one Chromium-based desktop browser and one WebKit (iOS Safari) device. Document any observed deviations as follow-up tasks.

### Final integration

- [ ] T167 Open PR from `114-citizen-wallet-pwa` → `master` once all prior tasks complete and CI is green. PR description must reference this tasks.md, the spec, the plan, the design doc, and confirm CLAUDE.md "Documentation Sync Policy" items have been addressed (per T153–T160).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion. **Blocks every user story phase.**
- **User Story 1 (Phase 3 — MVP)**: Depends on Foundational. Independent of US2–US5.
- **User Story 2 (Phase 4)**: Depends on Foundational. Soft dependency on US1 in that the enrolment endpoint surface is built incrementally — US1 introduces `POST /devices/enrol`, US2 adds `GET /credentials` and `GET /sync` to the same endpoints file.
- **User Story 3 (Phase 5)**: Depends on Foundational + US2 (you can only revoke devices that have enrolled).
- **User Story 4 (Phase 6)**: Depends on Foundational + US2 (sync-on-push requires the sync infrastructure US2 builds).
- **User Story 5 (Phase 7)**: Depends on Foundational + US1 (presentation log entries are written by the present flow).
- **Polish (Phase 8)**: Depends on every desired user story phase being complete.

### Within Each User Story

- Server entity → Server service → Server endpoint → Client service → Client UI page/component → Wire-up.
- Tests for a server change can be written in parallel with the implementation of the next layer (per CLAUDE.md "Write tests alongside code").

### Parallel Opportunities

- All `[P]` tasks within a phase can run in parallel given multiple developers / agents.
- Phase 2 has heavy parallelism — DTOs (T021–T032), JS bridges (T054–T057), entity configurations (T039, T044, T045) all independent.
- Phase 3 has parallel server-side test tasks (T077, T081, T083, T085, T087, T090) running alongside the next implementation step.
- Phase 8 documentation tasks (T153–T160) are all independent and parallelizable.

---

## Parallel Execution Examples

### Phase 2 — Foundational, parallel batch:

```bash
Task: "Create EcP256PublicJwk.cs in src/Common/Sorcha.CitizenWallet.Abstractions/Models/"
Task: "Create DeviceEnrolmentRequest.cs in src/Common/Sorcha.CitizenWallet.Abstractions/Models/"
Task: "Create DeviceEnrolmentResponse.cs in src/Common/Sorcha.CitizenWallet.Abstractions/Models/"
Task: "Create indexeddb-bridge.js in src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/"
Task: "Create webcrypto-bridge.js in src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/"
Task: "Create libsodium-bridge.js in src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/"
Task: "Create qr-scanner-bridge.js in src/Apps/Sorcha.Citizen.Wallet/wwwroot/js/"
```

### Phase 3 — US1 server-side tests parallel batch:

```bash
Task: "Add unit tests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/DeviceDelegationIssuerTests.cs"
Task: "Add unit tests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenStatusListPublisherTests.cs"
Task: "Add WebApplicationFactory tests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/CitizenStatusListEndpointsTests.cs"
Task: "Add WebApplicationFactory tests in tests/Sorcha.Wallet.Service.Tests/CitizenWallet/EnrolEndpointTests.cs"
Task: "Add unit tests in tests/Sorcha.Tenant.Service.Tests/PlatformUserDevice/PlatformUserDeviceServiceTests.cs"
```

### Phase 8 — documentation parallel batch:

```bash
Task: "Update docs/reference/PORT-CONFIGURATION.md"
Task: "Update docs/reference/API-DOCUMENTATION.md"
Task: "Update docs/reference/architecture.md"
Task: "Update .claude/skills/sorcha-architecture/SKILL.md"
Task: "Update .claude/skills/verifiable-credentials/SKILL.md"
Task: "Update .claude/skills/blazor/SKILL.md"
Task: "Update .claude/skills/sorcha-ui/SKILL.md"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only — Phases 1, 2, 3)

1. Complete Phase 1 (Setup) — projects compile, AppHost runs.
2. Complete Phase 2 (Foundational) — derivation slots, abstractions, base entities + migrations, JS bridges, base services.
3. Complete Phase 3 (US1) — present-offline flow, with a fixture-seeded device + credential.
4. **STOP and VALIDATE**: Run the canonical `PresentationFlowTests` E2E test (build a minimal version inline if Phase 8 not started). Demo to stakeholders.

This is the MVP "wow" moment — citizen + verifier both offline, present completes successfully.

### Incremental delivery

1. MVP (above).
2. Add US2 (Enrol) — citizens can self-enrol; remove fixture dependency.
3. Add US3 (Recover) — full lifecycle including loss/recovery.
4. Add US4 (Auto-receive) — push-driven sync.
5. Add US5 (Activity log) — full audit surface.
6. Polish — E2E test suite, docs, observability, security review.
7. Open PR.

### Parallel team strategy

With multiple developers (or agents):

1. All hands on Phase 1 + Phase 2 (must complete together).
2. Once Phase 2 done:
   - Developer A: US1 (Phase 3) — the MVP, owns the presentation engine + verifier reference app
   - Developer B: US2 (Phase 4) — enrolment + sync
   - (Once US2 lands) Developer A or C: US3 (Phase 5) — needs US2's enrolment endpoints
   - Developer B or C: US4 (Phase 6) — push notifications, depends on US2's sync
   - Developer A or D: US5 (Phase 7) — depends on US1's present flow
3. Developer (any): Phase 8 documentation in parallel with US3–US5 (low coupling).
4. Final: integration sweep, PR.

---

## Notes

- Every `[P]` task touches a different file from its parallel siblings; verify before launching concurrently.
- The Foundational phase is large because the wallet introduces a substantial new client-side runtime (PWA + JS interop bridges + IndexedDB schema). This investment is paid once and amortised across all stories.
- Per CLAUDE.md "Documentation Sync Policy", any task that adds/changes endpoints MUST update `docs/reference/API-DOCUMENTATION.md` (T154 covers this for Phase 8; smaller updates can happen per-task if preferred).
- Follow the `sorcha-ui` skill pattern strictly for E2E tests (Phase 8) — `data-testid` selectors, page objects, category-tagged tests.
- All commits should be atomic — one task = one commit where possible. Per CLAUDE.md commit format with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>`.
- Stop at any checkpoint to validate; the architecture is deliberately designed so that pausing at any user-story boundary leaves a coherent, demoable system.

---

## Task summary

| Phase | Tasks | Parallelizable | Story |
|---|---|---|---|
| Phase 1 — Setup | 14 | T002, T003, T004, T005, T006, T010, T012, T013, T014 | — |
| Phase 2 — Foundational | 60 | many (DTOs, bridges, services) | — |
| Phase 3 — US1 (MVP) | 27 | server-side tests | US1 |
| Phase 4 — US2 | 11 | tests + UI pages | US2 |
| Phase 5 — US3 | 10 | tests + additive UI | US3 |
| Phase 6 — US4 | 9 | tests + bridge | US4 |
| Phase 7 — US5 | 12 | tests + UI | US5 |
| Phase 8 — Polish | 24 | nearly all | — |
| **Total** | **167** | ~70 marked [P] | |

**MVP scope** (Phases 1+2+3): 101 tasks → first demoable end-to-end offline presentation.

**Format validation**: All tasks above carry the strict format `- [ ] T### [P?] [USx?] Description with src/... or tests/... path`. Setup, Foundational, and Polish tasks omit the `[USx]` label as required.
