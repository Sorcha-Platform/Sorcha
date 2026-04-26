# Implementation Plan: Citizen Wallet PWA

**Branch**: `114-citizen-wallet-pwa` | **Date**: 2026-04-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/114-citizen-wallet-pwa/spec.md`
**Companion**: design rationale at [`docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md`](../../docs/superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md)

## Summary

Deliver a standards-aligned **citizen wallet** as a Blazor WebAssembly Progressive Web App, plus a Blazor Server reference verifier, plus targeted server extensions to Wallet / Tenant / Blueprint services. The wallet holds Sorcha-issued SD-JWT verifiable credentials offline in encrypted on-device storage, and presents them to verifiers via OpenID for Verifiable Presentations cross-device QR — no platform contact during the exchange. A server-anchored holder key (newly-allocated derivation slot 108) is paired with revocable on-device delegation credentials, giving citizens device-loss recovery via existing platform login (no recovery phrases) while keeping verifier-side trust evaluation entirely offline. The feature is purely additive — `Sorcha.UI.Web` is unchanged; citizens still apply for credentials there, the wallet only holds and presents them.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (per Constitution Tech Stack)
**Primary Dependencies**:
- Server: ASP.NET Core 10 Minimal APIs, Scalar 2.10 (OpenAPI), EF Core 10 (Tenant + Wallet PG), FluentValidation 11.10, OpenTelemetry 1.12, Sorcha.Cryptography (HD derivation, SD-JWT signing), Sorcha.ServiceClients (consolidated HTTP clients), SignalR (existing pattern, new `WalletHub`), YARP 2.2 (existing gateway, new clusters), .NET Aspire 13 AppHost
- Client (PWA): Blazor WebAssembly 10 (standalone, no server prerender), `Microsoft.JSInterop` for IndexedDB + WebCrypto bridges, libsodium-js for XChaCha20-Poly1305 (~100 KB), `qr-scanner` JS module (~30 KB), MudBlazor for shared UI primitives via `Sorcha.UI.Core` reuse
- Client (Verifier): Blazor Server (interactive server render mode), `qrcode.js` for QR rendering

**Storage**:
- PostgreSQL (Tenant Service): new `PlatformUserDevices` table
- PostgreSQL (Wallet Service): new `CitizenDeviceStatusLists` and `CitizenWalletSyncCursors` tables
- Redis (Wallet Service): existing instance, new key namespaces `sorcha:wallet:status-list:*` and `sorcha:wallet:presentation-log-dedupe:*`
- IndexedDB (citizen device): five object stores under DB `sorcha-wallet` v1 (see data-model §B)

**Testing**:
- Unit: xUnit + FluentAssertions + Moq (per Constitution IV); minimum 80%, target >85%
- Integration: WebApplicationFactory + xUnit per existing service patterns
- E2E: Playwright (NUnit) extending `tests/Sorcha.UI.E2E.Tests` patterns from the `sorcha-ui` skill — new `Sorcha.Citizen.Wallet.E2E.Tests` test project, two-context offline-aware harness using `BrowserContext.SetOffline(true)` per research §R-011

**Target Platform**:
- Server: existing Sorcha deployment topology (Aspire AppHost local; Container Apps prod; n1.sorcha.dev)
- PWA: latest two major versions of Chromium, WebKit, Gecko on mobile + desktop (per spec SC-009 + research §R-010)

**Project Type**: Multi-app additive — two new Blazor projects under `src/Apps/`, one new shared library under `src/Common/`, three existing services extended.

**Performance Goals**:
- Cold-start present flow: < 30 seconds end-to-end (per spec SC-001)
- Enrolment flow: < 5 minutes from URL open to credentials cached (per spec SC-002)
- Recovery flow: < 10 minutes from new-device login to fully usable (per spec SC-003)
- WASM bundle: target < 5 MB gzipped first load; AOT off in v1
- Sync delta latency: < 2 seconds for typical citizen (10–50 credentials)

**Constraints**:
- Offline-first: full present flow MUST function with `setOffline(true)` on both wallet and verifier
- No recovery phrases or wallet-specific secrets shown to citizens (per spec FR-003)
- 30-day cached-credential availability without network (per spec SC-004)
- 24h status-list refresh window for verifiers (per spec FR-024)
- Existing Sorcha.UI.Web behaviour preserved exactly (per spec SC-010, FR-035)

**Scale/Scope**:
- Per-citizen: 10s of credentials (typical), upper bound for v1 cache scope = 200
- Per-org: status list capacity 32 768 bits/list, scaled by adding lists
- Per-platform: bounded by existing Sorcha deployment; wallet adds two static-content services (negligible compute) and modest DB/Redis pressure

## Constitution Check

*Gate: must pass before Phase 0 research. Re-evaluated after Phase 1 design. All gates pass with no required complexity-tracking entries.*

| Principle | Compliance | Notes |
|---|---|---|
| **I. Microservices-First** | PASS | Two new apps (wallet PWA, reference verifier) are independently deployable Aspire children. Existing services extended only with additive endpoints + DI registrations; no upward dependencies. Wallet PWA depends only on the API Gateway, no service-to-service direct calls. |
| **II. Security First** | PASS | At-rest: WebCrypto non-extractable keys (browser-managed) + AES-GCM-256 wrapping + XChaCha20-Poly1305 bulk per research §R-002. In-transit: TLS via existing API Gateway. Auth: bearer JWT with new audience `sorcha:citizen-wallet`. Status-list signing key in dedicated derivation slot 109 (least privilege per research §R-004). Input validation via FluentValidation on every new DTO. JSON Schema validation for the device delegation credential per `contracts/device-delegation-credential.schema.json`. |
| **III. API Documentation** | PASS | All new endpoints use Minimal APIs + Scalar OpenAPI (NOT Swagger). XML doc on every public method. OpenAPI exposed at existing `/openapi/v1.json`. Examples included for complex payloads. Two openapi.yaml contracts in `contracts/` formalise the surface for review. |
| **IV. Testing Requirements** | PASS | xUnit primary; >85% target on new code. Per-layer: unit (PWA service interfaces), integration (Wallet/Tenant/Blueprint extensions via WebApplicationFactory), E2E (Playwright cross-context offline). Arrange-Act-Assert pattern. Determinism via `BrowserContext.SetOffline` and seeded test fixtures. |
| **V. Code Quality** | PASS | Async/await throughout. DI throughout. Nullable reference types enabled. Zero compiler warnings target. Standard Sorcha file/code naming conventions. Test naming `MethodName_Scenario_ExpectedBehavior` per CLAUDE.md. |
| **VI. Blueprint Standards** | N/A | Feature 114 does not author blueprints. The reference verifier's request templates are OID4VP `presentation_definition` documents (PEX/DCQL), not Sorcha blueprints. |
| **VII. Domain-Driven Design** | PASS | New ubiquitous terms documented in spec §Key Entities (Wallet Installation, Holder Identity, Device Enrolment, Cached Credential, Presentation Request, Verifiable Presentation, Presentation Log Entry, Revocation Status Record). Existing terms (Participant, Disclosure, Publish) used consistently. |
| **VIII. Observability by Default** | PASS | OpenTelemetry traces from every new endpoint; metrics for sync latency, presentation success rate, status-list refresh cadence; structured logging (no string interpolation). Health checks on the wallet's static host (HTTP 200 on `/health`). |

**Post-design re-evaluation (after Phase 1)**: PASS — no constitution violations introduced by the data model, contracts, or quickstart.

## Project Structure

### Documentation (this feature)

```text
specs/114-citizen-wallet-pwa/
├── plan.md                                              # this file
├── spec.md                                              # SpecKit specification
├── research.md                                          # Phase 0 — open-question resolution
├── data-model.md                                        # Phase 1 — entities (server + on-device)
├── quickstart.md                                        # Phase 1 — happy-path walkthrough
├── checklists/
│   └── requirements.md                                  # spec quality checklist
└── contracts/                                           # Phase 1 — API + schema contracts
    ├── openapi-wallet-service.yaml                      # /api/v1/wallet/* additions
    ├── openapi-tenant-service.yaml                      # /api/v1/me/devices additions
    ├── presentation-lifecycle-offline-extension.md      # Feature 111 consumer contract
    └── device-delegation-credential.schema.json         # SD-JWT VC payload schema
```

### Source Code (repository root)

The feature touches two layers: **new projects** and **existing service extensions**. Layout follows the existing Sorcha topology under `src/`.

```text
src/
├── Apps/
│   ├── Sorcha.UI/                                       # EXISTING — unchanged
│   │   ├── Sorcha.UI.Core/                              # extended (display reuse only)
│   │   ├── Sorcha.UI.Web/                               # unchanged
│   │   └── Sorcha.UI.Web.Client/                        # unchanged
│   ├── Sorcha.Admin/                                    # EXISTING — unchanged
│   ├── Sorcha.AppHost/                                  # MODIFIED — register new apps + ports 7400/7401
│   ├── Sorcha.Citizen.Wallet/                           # NEW — Blazor WASM PWA
│   │   ├── wwwroot/
│   │   │   ├── manifest.webmanifest
│   │   │   ├── service-worker.js                        # dev — no-cache passthrough
│   │   │   ├── service-worker.published.js              # prod — precache + sync handlers
│   │   │   ├── icons/
│   │   │   ├── js/
│   │   │   │   ├── indexeddb-bridge.js                  # IndexedDB wrapper exposed via JSInterop
│   │   │   │   ├── webcrypto-bridge.js                  # WebCrypto sign/HMAC/HKDF wrappers
│   │   │   │   ├── libsodium-bridge.js                  # XChaCha20-Poly1305 via libsodium-js
│   │   │   │   └── qr-scanner-bridge.js                 # camera scanner integration
│   │   │   └── index.html
│   │   ├── Pages/
│   │   │   ├── Home.razor                               # credential list
│   │   │   ├── CredentialDetail.razor                   # id-card detail (uses Feature 107 component)
│   │   │   ├── Present.razor                            # QR scan + consent + present
│   │   │   ├── Devices.razor                            # device manager
│   │   │   ├── Activity.razor                           # local presentation log
│   │   │   └── Settings.razor                           # lock, sign-out, storage estimate
│   │   ├── Components/
│   │   │   ├── EnrolmentWizard.razor
│   │   │   ├── ConsentSheet.razor
│   │   │   ├── CredentialCardList.razor
│   │   │   └── DeviceLockBanner.razor
│   │   ├── Services/
│   │   │   ├── ICitizenAuthService.cs                   # JWT acquisition + refresh
│   │   │   ├── IDeviceKeyService.cs                     # WebCrypto wrapper
│   │   │   ├── ICredentialCache.cs                      # IndexedDB wrapper for credentials
│   │   │   ├── IDelegationStore.cs                      # IndexedDB wrapper for delegation
│   │   │   ├── ISyncService.cs                          # pull/push reconciliation
│   │   │   ├── IPresentationEngine.cs                   # OID4VP request handling, VP construction
│   │   │   ├── IStatusListService.cs                    # cached status-list checks
│   │   │   ├── IPresentationLog.cs                      # local activity log
│   │   │   └── Implementation/
│   │   ├── Auth/
│   │   │   └── CitizenAuthorizationMessageHandler.cs    # bearer token attach
│   │   ├── Extensions/
│   │   │   └── ServiceCollectionExtensions.cs
│   │   ├── Program.cs
│   │   └── Sorcha.Citizen.Wallet.csproj
│   ├── Sorcha.Citizen.Verifier/                         # NEW — Blazor Server reference verifier
│   │   ├── Pages/
│   │   │   ├── Index.razor
│   │   │   ├── VerifierSession.razor                    # /verify/{verifierOrgId}/{purpose}
│   │   │   └── Outcome.razor
│   │   ├── Services/
│   │   │   ├── IPresentationRequestBuilder.cs
│   │   │   ├── IVerifiablePresentationValidator.cs
│   │   │   ├── IStatusListCache.cs                      # verifier-side cache, 24h TTL
│   │   │   └── Implementation/
│   │   ├── Endpoints/
│   │   │   └── PresentationResponseEndpoints.cs         # POST /verify/r/{sessionId}/response
│   │   ├── Extensions/
│   │   ├── Program.cs
│   │   └── Sorcha.Citizen.Verifier.csproj
│   └── Sorcha.Cli/                                      # EXISTING — unchanged
├── Common/
│   ├── Sorcha.CitizenWallet.Abstractions/               # NEW — shared DTOs + constants
│   │   ├── Models/
│   │   │   ├── DeviceEnrolmentRequest.cs
│   │   │   ├── DeviceEnrolmentResponse.cs
│   │   │   ├── DeviceSummary.cs
│   │   │   ├── DeviceLabelUpdateRequest.cs
│   │   │   ├── DelegationRenewalRequest.cs / Response.cs
│   │   │   ├── SyncResponse.cs (+ Added/Revoked/Replaced shapes)
│   │   │   ├── CachedCredentialPayload.cs
│   │   │   ├── PresentationLogEntry.cs
│   │   │   ├── PresentationLogReportRequest.cs
│   │   │   ├── DeviceDelegationCredential.cs            # typed wrapper around the SD-JWT VC payload
│   │   │   └── EcP256PublicJwk.cs
│   │   ├── Constants/
│   │   │   ├── DerivationContexts.cs                    # CitizenHolder + CitizenStatusSigning string consts
│   │   │   ├── DelegatedCapabilities.cs                 # "presentation.holder-key-binding"
│   │   │   ├── VctUris.cs                               # "https://sorcha.dev/vc/citizen-device-delegation/v1"
│   │   │   └── JwtAudiences.cs                          # "sorcha:citizen-wallet"
│   │   ├── Schemas/
│   │   │   └── device-delegation-credential.v1.json     # canonical embedded schema
│   │   └── Sorcha.CitizenWallet.Abstractions.csproj
│   ├── Sorcha.ServiceClients.Http/                      # MODIFIED — new ICitizenWalletClient
│   │   └── CitizenWallet/
│   │       └── ICitizenWalletClient.cs                  # used by reference verifier + tests
│   └── (other existing common libraries unchanged)
├── Core/
│   ├── Sorcha.Wallet.Portable/Constants/
│   │   └── SorchaDerivationPaths.cs                     # MODIFIED — add slot 108 + 109 entries
│   └── (other existing core libraries unchanged)
├── Services/
│   ├── Sorcha.Wallet.Service/                           # MODIFIED
│   │   ├── Endpoints/
│   │   │   ├── CitizenWalletEndpoints.cs                # NEW — /devices/*, /sync, /credentials, /presentations/log
│   │   │   └── CitizenStatusListEndpoints.cs            # NEW — public /status/{orgId}/citizen-devices/{listId}.statuslist+jwt
│   │   ├── Services/
│   │   │   ├── Interfaces/
│   │   │   │   ├── IHolderKeyService.cs                 # NEW
│   │   │   │   ├── IDeviceDelegationIssuer.cs           # NEW
│   │   │   │   ├── ICitizenSyncService.cs               # NEW
│   │   │   │   ├── ICitizenStatusListPublisher.cs       # NEW
│   │   │   │   └── ICitizenPresentationLogReporter.cs   # NEW
│   │   │   └── Implementation/
│   │   ├── Hubs/
│   │   │   └── WalletHub.cs                             # NEW — SignalR hub at /hubs/wallet
│   │   ├── Hosted/
│   │   │   └── CitizenStatusListPublisherService.cs     # NEW — hourly regeneration + on-demand
│   │   ├── Persistence/
│   │   │   ├── Entities/
│   │   │   │   ├── CitizenDeviceStatusList.cs
│   │   │   │   └── CitizenWalletSyncCursor.cs
│   │   │   └── Migrations/                              # add 2 new tables
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs           # MODIFIED — wire new types + hub
│   ├── Sorcha.Tenant.Service/                           # MODIFIED
│   │   ├── Endpoints/
│   │   │   └── PlatformUserDeviceEndpoints.cs           # NEW — /me/devices/*
│   │   ├── Services/
│   │   │   ├── Interfaces/
│   │   │   │   └── IPlatformUserDeviceService.cs        # NEW
│   │   │   └── Implementation/
│   │   ├── Persistence/
│   │   │   ├── Entities/
│   │   │   │   └── PlatformUserDevice.cs                # NEW
│   │   │   ├── Configurations/
│   │   │   │   └── PlatformUserDeviceConfiguration.cs   # NEW EF config
│   │   │   └── Migrations/                              # AddPlatformUserDevice
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs           # MODIFIED
│   ├── Sorcha.Blueprint.Service/                        # MODIFIED
│   │   ├── Services/Implementation/
│   │   │   └── OfflinePresentationConsumer.cs           # NEW — IPresentationConsumer
│   │   └── Extensions/
│   │       └── ServiceCollectionExtensions.cs           # MODIFIED — register consumer
│   ├── Sorcha.ApiGateway/                               # MODIFIED — YARP cluster definitions
│   │   └── appsettings.json                             # add /wallet/*, /verify/*, /api/v1/wallet/*, /hubs/wallet
│   └── (other services unchanged)
└── (rest of repo unchanged)

tests/
├── Sorcha.Citizen.Wallet.Tests/                         # NEW — xUnit unit tests for PWA services
│   ├── Services/
│   │   ├── DeviceKeyServiceTests.cs
│   │   ├── CredentialCacheTests.cs
│   │   ├── SyncServiceTests.cs
│   │   ├── PresentationEngineTests.cs
│   │   └── StatusListServiceTests.cs
│   └── Sorcha.Citizen.Wallet.Tests.csproj
├── Sorcha.Citizen.Verifier.Tests/                       # NEW — xUnit unit tests for verifier
│   ├── Services/
│   │   └── VerifiablePresentationValidatorTests.cs
│   └── Sorcha.Citizen.Verifier.Tests.csproj
├── Sorcha.Citizen.Wallet.E2E.Tests/                     # NEW — Playwright cross-context tests
│   ├── Infrastructure/
│   │   ├── CitizenWalletDockerTestBase.cs               # extends DockerTestBase from sorcha-ui
│   │   └── TestFixtures.cs
│   ├── PageObjects/
│   │   ├── CitizenWalletPage.cs
│   │   ├── EnrolmentPage.cs
│   │   ├── PresentPage.cs
│   │   ├── DevicesPage.cs
│   │   └── ActivityPage.cs
│   ├── PageObjects/Verifier/
│   │   └── VerifierSessionPage.cs
│   ├── Docker/
│   │   ├── EnrolmentTests.cs
│   │   ├── PresentationFlowTests.cs
│   │   ├── RecoveryFlowTests.cs
│   │   └── ActivityLogTests.cs
│   └── Sorcha.Citizen.Wallet.E2E.Tests.csproj
├── Sorcha.Wallet.Service.Tests/                         # EXTENDED
│   └── CitizenWallet/
│       ├── HolderKeyServiceTests.cs
│       ├── DeviceDelegationIssuerTests.cs
│       ├── CitizenSyncServiceTests.cs
│       ├── CitizenStatusListPublisherTests.cs
│       └── CitizenWalletEndpointsTests.cs               # WebApplicationFactory
├── Sorcha.Tenant.Service.Tests/                         # EXTENDED
│   └── PlatformUserDevice/
│       ├── PlatformUserDeviceServiceTests.cs
│       └── PlatformUserDeviceEndpointsTests.cs
├── Sorcha.Blueprint.Service.Tests/                      # EXTENDED
│   └── OfflinePresentation/
│       └── OfflinePresentationConsumerTests.cs
└── (existing test projects unchanged)

docker/                                                  # MODIFIED — two new images
├── Dockerfile.citizen-wallet                            # nginx + WASM static
└── Dockerfile.citizen-verifier                          # ASP.NET 10 runtime

docker-compose.yml                                       # MODIFIED — two new services
docs/
├── superpowers/specs/2026-04-26-citizen-wallet-pwa-design.md  # source design (already committed)
├── reference/PORT-CONFIGURATION.md                      # MODIFIED — add 7400, 7401
├── reference/API-DOCUMENTATION.md                       # MODIFIED — append wallet endpoints
└── reference/architecture.md                            # MODIFIED — add wallet topology
.claude/skills/                                          # MODIFIED — propagate per task #9
├── sorcha-architecture/SKILL.md                         # add Citizen Wallet PWA section
├── verifiable-credentials/SKILL.md                      # add OID4VP holder + offline section
├── blazor/SKILL.md                                      # add PWA + service-worker scoping
└── sorcha-ui/SKILL.md                                   # add citizen-wallet test patterns
```

**Structure Decision**: This is a multi-app additive feature in an existing microservices codebase. The structure reuses Sorcha's established `src/Apps/`, `src/Common/`, `src/Core/`, `src/Services/`, and `tests/` layout. New code lives in clearly-named new projects (`Sorcha.Citizen.Wallet`, `Sorcha.Citizen.Verifier`, `Sorcha.CitizenWallet.Abstractions`) and new sub-folders within existing services (`Sorcha.Wallet.Service/Endpoints/CitizenWalletEndpoints.cs`, `Sorcha.Tenant.Service/Persistence/Entities/PlatformUserDevice.cs`, etc.). No existing files are deleted; all changes to existing projects are additive (new endpoint files, new entity classes, new DI registrations, EF migration).

## Complexity Tracking

*No Constitution violations identified. No complexity-tracking entries required.*

The feature does introduce two new browser-side JS dependencies (libsodium-js for XChaCha20-Poly1305, qr-scanner for camera input). These are standard, well-audited libraries; their inclusion is justified by the at-rest encryption requirement (FR-011) and the QR scan requirement (FR-013) respectively. No simpler alternatives within the WebCrypto / browser standard surface exist for either capability.
