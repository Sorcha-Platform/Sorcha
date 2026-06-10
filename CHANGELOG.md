# Changelog

All notable changes to the Sorcha project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.8.1] - 2025-11-16

### Added
- **SignalR Integration Tests for Blueprint Service**
  - 14 comprehensive tests (520+ lines) covering all hub functionality
  - Hub connection/disconnection lifecycle tests
  - Wallet subscription/unsubscription with error handling
  - All notification types: ActionAvailable, ActionConfirmed, ActionRejected
  - Multi-client broadcast scenarios
  - Wallet-specific notification isolation
  - Post-unsubscribe notification filtering

### Changed
- Blueprint-Action Service completion upgraded from 95% to 100%
- Overall platform completion increased from 90% to 92%
- Test coverage for Blueprint Service increased from 85% to >90%
- Resolved Issue #3: Missing SignalR Integration Tests

## [0.8.0] - 2025-11-16

### Added - Major Features
- **Wallet Service (90% Complete)**
  - Complete core implementation with HD wallet support (BIP32/BIP39/BIP44)
  - Domain model: Wallet, WalletAddress, WalletAccess, WalletTransaction
  - Service layer: WalletManager, KeyManagementService, TransactionService, DelegationService
  - Infrastructure: InMemoryRepository, LocalEncryptionProvider, EventPublisher
  - REST API endpoints (Phase 2): create, get, sign, decrypt, generate address
  - Comprehensive unit and integration tests (WS-030, WS-031)
  - Integration with Sorcha.Cryptography for all crypto operations

- **Portable Blueprint Execution Engine (100% Complete)**
  - Stateless engine for client-side (Blazor WASM) and server-side execution
  - JSON Schema validation (Draft 2020-12)
  - JSON Logic evaluation for calculations and conditions
  - Selective data disclosure using JSON Pointers (RFC 6901)
  - Conditional routing between participants
  - Thread-safe, immutable design pattern
  - 93 unit tests + 9 integration tests
  - Real-world scenarios: loan applications, purchase orders, multi-step surveys

- **Unified Blueprint-Action Service (Sprints 3-5 Complete)**
  - Sprint 3: Service layer foundation
    - ActionResolverService - Action resolution from blueprints
    - PayloadResolverService - Encryption/decryption orchestration
    - TransactionBuilderService - Transaction building
    - Redis caching layer
  - Sprint 4: Action API Endpoints
    - GET /api/actions/{wallet}/{register}/blueprints
    - GET /api/actions/{wallet}/{register} (paginated)
    - GET /api/actions/{wallet}/{register}/{tx}
    - POST /api/actions (submit)
    - POST /api/actions/reject
    - GET /api/files/{wallet}/{register}/{tx}/{fileId}
  - Sprint 5: Execution Helpers & SignalR
    - POST /api/execution/validate
    - POST /api/execution/calculate
    - POST /api/execution/route
    - POST /api/execution/disclose
    - SignalR ActionsHub for real-time notifications
    - Redis backplane for scalability

- **Validator Service Design**
  - Complete design and implementation plan
  - Core validation library specification (Sorcha.Validator.Core)
  - Service infrastructure design
  - Consensus engine design (Simple Quorum)
  - 10-week implementation roadmap

- **Register Service Integration**
  - Infrastructure integration with Wallet and Blueprint services
  - Stub implementation for graceful degradation
  - Transaction submission and retrieval interfaces

### Added - Infrastructure
- SignalR real-time notifications with Redis backplane
- Enhanced health check endpoints for Blueprint and Peer services
- API Gateway enhancements (health aggregation, client download, OpenAPI aggregation)
- Comprehensive integration tests across services
- Performance testing with NBomber

### Changed
- Blueprint Service evolved to Unified Blueprint-Action Service
- Overall project completion: 70% → 80%
- Enhanced test coverage across all components
- Updated all .NET projects to target .NET 10 only (removed multi-targeting)

### Fixed
- Multiple build errors across Blueprint Engine and tests
- Type conversion errors in ExecutionEngineTests
- Namespace conflicts in Blueprint Engine tests
- FluentAssertions method name corrections
- Port binding permission errors (changed to safer port range)
- Cryptography library updates for .NET 10 compatibility

## [Unreleased] — v0.8.1 → June 2026 (Feature 128 / ~F149)

Highlights from the eighteen months of development between the last tagged release (v0.8.1, November 2025) and the current pre-v1 codebase. Grouped by Keep-a-Changelog conventions; exhaustive per-sprint detail lives in the individual feature specs under `specs/`.

### Added

- **Verifiable Credentials (F031/F039/F093/F094/F103/F107)** — SD-JWT VC format, selective disclosure with JSON Pointer paths, credential gating on blueprint actions, blueprint-as-issuer, holder key binding, cross-blueprint composability, revocation via IETF Token Status List. The `AssuredIdentityCredential` is the canonical identity primitive replacing earlier split credentials.
- **OpenID4VCI / OpenID4VP (HAIP) issuer and verifier (F097/F098/F101/F102)** — HAIP 1.0-conformant credential issuance and presentation endpoints; both SD-JWT VC and ISO mso_mdoc formats (F135). OID4VP `direct_post` callback with DCQL query.
- **Post-quantum cryptography (F040)** — ML-DSA-65 (FIPS 204), ML-KEM-768 (FIPS 203), and SLH-DSA-128s implemented in `Sorcha.Cryptography`; used for internal action signatures, docket signatures, and per-recipient key encapsulation. PQC is core, not a branch feature.
- **Trust hardening — receipts, Merkle proofs, revocation (F079)** — transaction receipts with Merkle inclusion proofs; revocation transactions on the distributed ledger; cross-tenant register interaction guards.
- **Tiered JWT audiences + issuer hardening (F136)** — installation-namespaced, tier-scoped audiences (`consumer | platform | service | enrol-session`) from `SorchaAudiences`; `SorchaIssuer.Resolve` fails closed in Production/Staging; per-endpoint tier policies (`RequireConsumerAudience`, `RequirePlatformAudience`, `RequireService`).
- **Notifications architecture (F118)** — `AddSorchaHub<THub,TClient>` unified hub registration; Redis backplane with per-service channel-prefix isolation; thin-signal contract (opaque IDs + timestamps, no domain payload); `TenantHub`, `BlueprintHub`, `WalletHub`, `RegisterHub`. Snackbar retired from all user-facing and PWA surfaces; replaced by `IInlineFeedback` + server-side inbox writer.
- **Transactional email (F112)** — `ITransactionalEmailService` facade; Scriban templates (`verify`, `invite`, `reset`, `welcome-public`, `welcome-invited`, shared `base`); per-org branding on invitations; `WelcomeEmailDispatcher` (one-shot, non-throwing).
- **Storage registration audit + fail-fast (F113)** — `IStorageRegistrationLog` records every storage-interface binding at startup; six audited interfaces fail-fast in Production/Staging if on an in-memory backend; `storage-providers` health check and OTel meter surface the same state.
- **Citizen Wallet PWA (F114)** — Blazor WASM progressive web app; holder/device delegation; status-list publisher + worker; enrolment endpoint; Tenant device registry (`PlatformUserDevice`).
- **Enrolment session + council-page gate (F126)** — `EnrolGateComponent`, `IEnrolPairingSignal`, short-lived enrol-session JWT (scope `enrol`), QR code surface, `ReturnToAllowlist` open-redirect guard.
- **Credential-gated service (F127)** — `SorchaWalletPresentationConsumer`, `CredentialGateComponent`, claims-fetch endpoint; extends F111's timebound presentation lifecycle with the first non-HAIP consumer.
- **Cold-start onboarding + pairing resumption (F128)** — `PairingTakeover`, `PairingHandoffSurface`, `PairingNagBanner`, `IHasPairedDeviceProbe`, `has-any-device` aggregate endpoint; pairing short-code API (6-digit human-typeable token, 5-minute TTL); "Email me a link" resumption flow (magic-link → re-authenticate → `/setup/add-device`); `PairingHandoffSurface` enrol-session `mode` discriminator.
- **EUDI credential format + unified trust (F135)** — `ITrustEvaluator`; `TrustPolicy` replaces flat `acceptedIssuers`; `mso_mdoc` format handler (ES256/P-256-only, issuer `x5chain`, MSO verification, COSE_Sign1 DeviceAuth); trust-list snapshot management endpoints (`PUT/GET /api/v1/trust/trustlists/{id}`).
- **Assured Identity demo environment (F144)** — n1 deployment bootstrapped through genesis ceremony; reference walkthrough at `walkthroughs/AssuredIdentity/`.
- **Unified versioning (build-time derived)** — single `Major.Minor.Patch` from root `Directory.Build.props`; Minor = `GITHUB_RUN_NUMBER`, Patch = `GITHUB_RUN_ATTEMPT`; local builds are `2.0.0-dev`. No per-csproj `<Version>` tags.
- Device management API (`GET/DELETE /api/v1/me/devices`, `GET /api/v1/me/devices/has-any`) for citizen-initiated wallet-device revocation from the web UI.
- Consensus vote-signature verification (`ConsensusEngine` calls `VerifySignatureAsync`; unsigned votes rejected).
- Per-sender sequence numbers for transaction replay protection (`WalletSequence` / `WalletSequenceRepository`, error code `VAL_REPLAY`).
- `SorchaHub`-backed system register governance (F087/F099/F121) — genesis ceremony CLI, programmable validation rule-set (genesis-embedded, governance-updateable).
- MCP server (F139/F140) — AI tool surface for blueprints, registers, wallets, and citizen tools.
- CLI modernisation (F080/F133) — full command coverage: `blueprint`, `credential`, `docket`, `validator`, `audit`, `schema`, `platform`, `system-register`, `participant`, `invitation`, `verify`, `event-watch` groups added alongside the original `auth`/`config`/`org`/`wallet`/`register`/`tx`/`peer` commands.

### Changed

- Blueprint designer consolidated to `/designer/blueprint` (F109/F142) — Describe → Understand → Rehearse → Go Live shell; Rehearsal Pass gate on go-live; legacy `/designer` and `/designer/chat` routes are redirect shims.
- `Sorcha.UI.Core` partitioned into `Services/User/`, `Services/Admin/`, `Services/Shared/` (F123); shared user-facing components extracted to `Sorcha.UI.Components.User` (F122).
- Centralised rate limiting via `ServiceDefaults.AddRateLimiting()` across all services (SEC-002); no per-service `AddRateLimiter`.
- `SorchaAudiences` and `SorchaIssuer` are the single source of truth for token minting and validation; hand-built audience strings are a build error.
- `Sorcha.PeerRouter` standalone app retired (F143); capability folded into `Sorcha.Peer.Service` via reverse-stream rendezvous.
- Blueprint publish pipeline produces coded validation errors aligned with the AI-chat tool validator (D3 / F147 follow-up; in progress).

### Security

- **F146 — Tenant at-rest secret protection** — TOTP secrets (previously reversible Base64, doc-comment claimed AES-256-GCM) are now encrypted with real AES-256-GCM via `ISecretProtectionProvider`; OIDC client secrets and the 2FA intermediate-token signing key also hardened. Resolves CRITICAL finding C1 from the 2026-06-02 architecture review.
- **F147 — Authorization-gap closure** — system-wallet create/recover endpoints gated (`RequireService` / `CanRecoverSystemWallet`); `CanManageBlueprints` policy now enforces platform-tier audience, closing the citizen→blueprint-authoring privilege escalation path. Resolves HIGH findings H1 and H2 from the 2026-06-02 architecture review.
- **F148 — Verification correctness** — OIDC ID-token JWS verified against provider JWKS on every social-login exchange; PWA on-device verifier now distinguishes holder-verified-only from fully-issuer-verified results rather than returning a flat "valid". Resolves HIGH finding H3 (partially) and MEDIUM M3a from the 2026-06-02 architecture review.

### Fixed

- Wallet service `WalletSequence` closes transaction replay vulnerability (§4.2 of the March 2026 audit).
- Validator vote-signature verification closes unsigned-consensus-vote vulnerability (§4.1 of the March 2026 audit).

## [0.1.0] - TBD

### Planned
- [ ] Core blueprint schema implementation
- [ ] Blueprint validation engine
- [ ] Basic execution engine
- [ ] Visual designer prototype
- [ ] Unit test coverage
- [ ] Integration tests
- [ ] API documentation
- [ ] Docker support
- [ ] Kubernetes manifests

---

## Version History

### Versioning Strategy

- **Major version (X.0.0)**: Breaking changes
- **Minor version (0.X.0)**: New features, backwards compatible
- **Patch version (0.0.X)**: Bug fixes

### Release Cycle

- **Alpha**: Internal testing, frequent changes
- **Beta**: Public preview, feature complete
- **RC**: Release candidate, production ready
- **Stable**: General availability

---

[Unreleased]: https://github.com/Sorcha-Platform/Sorcha/compare/v0.8.1...HEAD
[0.8.1]: https://github.com/Sorcha-Platform/Sorcha/releases/tag/v0.8.1
[0.8.0]: https://github.com/Sorcha-Platform/Sorcha/releases/tag/v0.8.0
[0.1.0]: https://github.com/Sorcha-Platform/Sorcha/releases/tag/v0.1.0
