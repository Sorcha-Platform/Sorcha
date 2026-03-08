# Sorcha Platform - Master Task List (Archived Phases)

> **This file contains completed phases archived from [MASTER-TASKS.md](MASTER-TASKS.md).**
> **Archived:** 2026-03-08 (latest batch)

---

## Archived 2026-03-08: All Active Features & Production Packaging

All remaining active features moved to archive during release preparation cleanup.

### Feature 053: Peer Router App & Peer Service Completion — ✅ (56/56 tasks)

Standalone PeerRouter application for P2P network bootstrapping with gRPC peer discovery, heartbeat streaming, optional relay mode, SSE debug events, browser debug page. Peer Service hardened with circuit breaking (PeerConnectionPool), SQLite→PostgreSQL queue migration (PeerDbContext), EF Core integration. 77 tests. 8 phases delivered (T001-T056). Post-merge: self-registration guard, client IP logging, h2c transport. Deployed to Azure Container Apps at `n0.sorcha.dev`.

### Feature 052: Encryption Integration — ✅ (40/40 tasks)

End-to-end async encryption pipeline wired to UI and CLI. OperationId/IsAsync on ActionSubmissionResultViewModel, EncryptionProgressIndicator with SignalR push + polling fallback, retry on failure, cross-page toast notifications via EventsHub, paginated operations history, CLI `action execute` with blocking spinner and `--no-wait` mode. 63+ new tests.

### Feature 051: Operations & Monitoring Admin — ✅ (51/65 tasks, 14 deferred)

Dashboard auto-refresh, wallet access delegation, schema provider CLI, events admin, push notifications, encryption progress indicators, presentation admin UX improvements (chip inputs, auto-refresh), credential lifecycle typed errors. **14 deferred tasks** carried forward to remaining work: CLI tests (T019/T026/T031/T045), EncryptionProgress integration (T051), and 9 additional test/polish items.

### Feature 045: Encrypted Payload Integration — ✅ (67/67 tasks)

Envelope encryption for action transaction payloads — XChaCha20-Poly1305 symmetric + per-recipient asymmetric key wrapping, disclosure grouping, async pipeline with SignalR progress, recipient decryption. 10 phases (T001-T067).

### Feature 048: Register Policy Model & System Register — ✅ (54/54 tasks)

RegisterPolicy embedded in genesis control records, System Register singleton, approved validator lists, policy updates via control transactions, governance quorum formulas, policy-driven operational TTL. 9 phases.

### Feature 049: System Administration Tooling — ✅ (72/72 tasks)

Service Principal CRUD, Register Policy management, Validator Consent Queue, Validator Metrics Dashboard, System Register page, Threshold Signing & Config, 17 CLI subcommands, 29 new Blazor components. 10 phases.

### Feature 046: UI Polish & Blueprint Designer — ✅ (43/43 tasks)

Dashboard wizard/GUID fix, notification panel overflow, dark mode, stale labels, blueprint designer save/load, EventsHub real-time notifications, i18n wiring. 618 bUnit tests pass.

### P0: Transaction Pipeline Audit — ✅ (8/8 tasks)

All transactions routed through Validator mempool. Blueprint publish via validator, governance operations via validator. Legacy direct-write paths eliminated.

### P0: Production Packaging & Version Control — ✅ (Phases A-E)

- **Phase A** (58 tasks): CPM for 69 projects, 15 Common libraries NuGet v2.0.0, GitHub Actions overhaul, 25 critical/important code quality fixes, 6 major package upgrades
- **Phase B** (49 tasks): 8 Core libraries NuGet v2.0.0, 44 code quality fixes (10 critical, 14 important, 20 minor), 1,055 tests
- **Phase C** (6 tasks): 7 service csproj standardization, Docker CI/CD matrix, 21 service code quality findings fixed
- **Phase D** (5 tasks): Apps build standardization, Docker CI/CD, Playwright E2E pipeline
- **Phase E** (14 tasks): 98%→100% MVD feature completion — Tenant/CLI/Peer test stabilization, Peer EF Core, gRPC RPCs, YARP auth policies on all 48 routes

### Summary of Archived Work

| Feature/Phase | Tasks Complete | Tasks Deferred |
|---------------|---------------|----------------|
| 053 Peer Router | 56 | 0 |
| 052 Encryption Integration | 40 | 0 |
| 051 Operations & Monitoring | 51 | 14 |
| 045 Encrypted Payloads | 67 | 0 |
| 048 Register Policy | 54 | 0 |
| 049 System Admin Tooling | 72 | 0 |
| 046 UI Polish & Designer | 43 | 0 |
| P0 Transaction Audit | 8 | 0 |
| P0 Packaging Phase A | 58 | 0 |
| P0 Packaging Phase B | 49 | 0 |
| P0 Packaging Phase C | 6 | 0 |
| P0 Packaging Phase D | 5 | 0 |
| P0 Packaging Phase E | 14 | 0 |
| **Total** | **523** | **14** |

Combined with previously archived work (Features 031-043, Sprints 3-10, Wallet API, Register Service, Validator Service, Auth Integration, Quantum-Safe Crypto, Verifiable Presentations, Content-Type Payload, UI Modernization, etc.), this represents the full MVD development effort.

---

## Recent Updates

| Date | Summary |
|------|---------|
| 2026-02-27 | **Production Packaging & CI/CD** (Phase A: Shared Libraries): Central Package Management (Directory.Packages.props with 127 packages across 69 projects), Directory.Build.props for Common libraries (shared NuGet metadata, SourceLink, deterministic builds), all 15 Common libraries packaged as NuGet v2.0.0 with independent minor versioning. GitHub Actions overhaul: removed 6 old workflows, created nuget-ci.yml (PR build & test), nuget-publish.yml (auto minor bump & publish), claude-review-issues.yml (review feedback → GitHub issues). Complete branding purge across all source, docs, and specs. Code quality review across all 15 libraries. Phases B-D (Core, Services, Apps) scoped for follow-on work. |
| 2026-02-26 | **043-UI-CLI-Modernization** (66 tasks, 11 phases): UI polish and CLI expansion. Phase 1-3: Setup + UserPreferences backend (OtpNet, i18n) + Activity Log (EventEntity, EventService, EventHub SignalR, ActivityLogPanel with bell icon, 49 endpoint/service tests). Phase 4 (US2): Sidebar — consolidated ADMINISTRATION section, mini drawer with OpenMiniOnHover. Phase 5 (US3): StatusFooter with 30s health polling, MainLayout integration, 9 bunit tests. Phase 6 (US4): Wallet management — WalletList view toggle, default wallet star, QR dialog, PQC algorithm support in CreateWallet, WalletPreferenceService migration to server-side (14 tests). Phase 7 (US5): Dashboard wizard — conditional wizard/KPI on Home.razor, auto-set default wallet. Phase 8 (US6): Validator dashboard — ValidatorPanel with 3s polling, throughput visualization, register table (16 tests). Phase 9 (US7): Settings — TOTP 2FA backend (TotpConfiguration entity, TotpService, setup/verify/disable/status endpoints, rate limiting, loginToken flow), ThemeService (dark mode, OS detection), LocalizationService (JSON i18n, fr/de/es translations), TimeFormatService, TotpClientService, Settings.razor with 6 tabs, push notification service worker + subscription endpoints (TOTP endpoint tests, CLI command tests). Phase 10 (US8): CLI commands — BlueprintCommands, ParticipantCommands, CredentialCommands, ValidatorCommands, AdminCommands (all with Refit + Spectre.Console), CLI command tests. Phase 11: Polish — build fix, test verification, documentation. |
| 2026-02-26 | **041-Auth-Integration** (38 tasks, 8 phases): Authentication & authorization integration across all services. Phase 1-2: Setup + Foundational — TokenClaimConstants, configurable ServiceAuthClient scopes, Validator Service JWT auth + authorization policies. Phase 3 (US1): Service-to-service auth — verified client credentials flow, service token acquisition logging. Phase 4 (US2): User authentication — endpoint audit across all services, RequireAuthorization/AllowAnonymous coverage, anonymous endpoint verification. Phase 5 (US3): Delegation token flow — IDelegationTokenClient/DelegationTokenClient for on-behalf-of user flows, RequireDelegatedAuthority policy on Blueprint/Wallet/Register. Phase 6 (US4): Authorization policies — renamed Administrator→RequireAdministrator in Blueprint, PeerAuthInterceptor gRPC interceptor for JWT validation on peer connections, authenticated peer reputation bonus (+15 quality score). Phase 7 (US5): Token introspection & revocation — ITokenRevocationStore in JwtAuthenticationExtensions OnTokenValidated event, ITokenIntrospectionClient for /api/auth/token/introspect. Phase 8: Polish — AUTHENTICATION-SETUP.md docs update, security audit logging, mTLS config scaffolding, existing test suite fix (TestAdminAuthHandler for WebApplicationFactory tests). Tests: 39 new auth integration tests, 15 validator auth policy tests, 6 delegation client tests, 6 revocation tests. |
| 2026-02-25 | **040-Quantum-Safe-Crypto** (74 tasks, 10 phases): Post-quantum cryptography with CNSA 2.0 compliance. Phase 1-2: Setup + Foundational — PqcSignatureProvider (ML-DSA-65 via BouncyCastle), PqcEncapsulationProvider (ML-KEM-768), HybridSignature model, Bech32m address encoding, CryptoPolicy model, key material zeroization. Phase 3 (US1): Hybrid signing — classical+PQC dual signatures with CryptoModule extension. Phase 4 (US2): Per-register crypto policy governance — CryptoPolicyService, policy enforcement in Validator, Control TX updates. Phase 5 (US7): SLH-DSA-128s hash-based signatures — stateless fallback for lattice concerns. Phase 6 (US3): ws2-prefixed Bech32m wallet addresses for PQC keys. Phase 7 (US4): ML-KEM-768 payload encryption — quantum-safe KEM + AES-256-GCM hybrid, encapsulate/decapsulate endpoints. Phase 8 (US5): BLS12-381 threshold signatures — distributed docket validation with Shamir secret sharing (t-of-n). Phase 9 (US6): Zero-knowledge register verification — Pedersen commitments on secp256k1 with Schnorr proofs for inclusion, OR proofs for range [0, 2^n). Phase 10: Polish — performance benchmarks, document sizes, YARP routes for all PQC endpoints. Tests: 270+ new tests across 8 test projects. Algorithm support: ML-DSA-65, ML-KEM-768, SLH-DSA-128s, BLS12-381, secp256k1 ZK proofs. |
| 2026-02-22 | **P0-6 Governance Operations** — Wired governance operations through the validator pipeline. Added `POST /api/registers/{id}/governance/propose` endpoint (Add/Remove/Transfer) following the blueprint publish Control TX pattern: roster reconstruction → proposal validation → quorum check → apply operation → canonical JSON → deterministic TxId → system wallet signing → validator submission. Added `GET .../governance/proposals` endpoint (paginated governance history filtered from Control TXs via TrackingData). Service client: `ProposeGovernanceOperationAsync`, `GetGovernanceProposalsAsync` + `GovernanceClientModels.cs` DTOs. DocketSerializer updated to pass submission metadata through to `TransactionMetaData.TrackingData`. YARP routing covered by existing `registers-direct-route` catch-all. Tests: 7 new service client tests, all existing governance/serializer/service client tests pass (10+12+43). |
| 2026-02-21 | **038-Content-Type-Payload** (69 tasks, 9 phases): Content-Type Aware Payload Encoding. Added `ContentType` and `ContentEncoding` metadata fields to `PayloadModel`/`PayloadInfo`. Created `PayloadEncodingService` (Base64url/identity/Brotli/Gzip, 4KB compression threshold). Migrated ~128 call sites from legacy Base64 to Base64url (RFC 4648 §5). MongoDB BSON Binary storage: `MongoTransactionDocument` with `Base64UrlBinarySerializer`/`BinaryAwareStringSerializer` for dual-format reads (~33% storage reduction). `JsonTransactionSerializer` emits native JSON for identity-encoded payloads. 81+ new tests. Deferred: PayloadManager compression integration (T059), Blueprint/Validator wire-format identity encoding (T053/T054/T060/T061). |
| 2026-02-21 | **Organization Admin UI Upgrades** — 4 phases: (1) Fixed transaction type display — `TransactionViewModel.TransactionType` now uses `MetadataTransactionType` enum (0=Control, 1=Action, 2=Docket, 3=Participant) instead of falling through to "Transfer" for Participant TXs. Updated chip colors in TransactionRow/TransactionDetail. (2) Restructured `/admin/organizations` from flat list→user-list into tabbed `OrganizationDashboard` with Overview, Users, Participants, Published, Configuration tabs. Dual auth: system admins see org list, org admins auto-load their org via `org_id` claim. (3) Added `OrganizationConfiguration` tab with security policy stubs, external IdP placeholder, and branding editor (saves via existing API). (4) Full participant publishing UI: `IParticipantPublishingService` (publish/update/revoke via Tenant Service API), 3-step `PublishParticipantDialog` wizard (select register → review addresses → confirm+sign), "Publish" button on ParticipantList and ParticipantDetail, `PublishedParticipantsList` tab querying Register Service. Tests: 16 new (11 TransactionViewModel, 5 PublishingService). |
| 2026-02-21 | **Transaction Architecture Critical Review** — 10 research items (TRUST-1 to TRUST-10) added to [deferred-tasks.md](tasks/deferred-tasks.md). Critical analysis of transaction core: how transactions are built, signed, validated, sealed into dockets, and disclosed. Identified structural trust gaps: (1) Validator doesn't re-execute calculations — accepts whatever Blueprint Engine submits (TRUST-1). (2) Disclosure rules enforced at app layer, not cryptographically verified by Validator (TRUST-2). (3) No signed transaction receipts proving finality (TRUST-3). (4) Merkle inclusion proofs not generateable for lightweight offline verification (TRUST-4). (5) No first-class revocation/amendment transaction type (TRUST-5). (6) Consensus has no deterministic finality — simple >51% quorum (TRUST-6). (7) No cross-register cryptographic references (TRUST-7). (8) Transaction lifecycle audit trail discarded on persistence (TRUST-8). (9) Timestamps self-asserted, no authority (TRUST-9). (10) No key rotation/re-encryption for compromised keys (TRUST-10). Tier 1 priority: TRUST-1,2,3,4 (closes active trust gaps without architectural upheaval). |
| 2026-02-21 | **Medical Equipment Refurb Walkthrough** — All 3 scenarios PASS (12/12 steps, 266s). Scenario A: routine refurb (4 actions, riskCategory=routine, cost=2000, VC issued). Scenario B: safety-critical (5 actions, riskCategory=safety-critical, cost=7000, regulatory review, VC issued). Scenario C: rejection (3 actions, BER at quote stage). First walkthrough exercising participant publishing (spec 001), multi-org (3 orgs, 4 participants), conditional routing, calculations, rejection paths, and Refurbishment Certificate VC issuance. |
| 2026-02-20 | **001-Participant-Records** (50 tasks, 8 phases): Added `TransactionType.Participant = 3` for publishing participant identity records as transactions on a register. Phase 1: Shared models (ParticipantRecord, ParticipantRecordStatus, participant-record-v1.json schema). Phase 2: Validator accepts Participant TXs with lighter rules (schema validation, no governance check, no blueprint conformance). Phase 3: Tenant Service publishes participant records (deterministic TxId, canonical JSON, wallet signing, validator pipeline). Phase 4: Register Service indexes addresses in-memory (ConcurrentDictionary), query endpoints (list, by-address, by-id), service client methods. Phase 5-6: Update and revoke via new version publishing with PrevTxId chaining. Phase 7: Public key resolution with 410 Gone for revoked. Phase 8: MongoDB index on TransactionType, optional Redis cache write-through, wallet address uniqueness check (409 Conflict), documentation. Tests: 64 new tests (20 index, 12 service client, 32 publishing). |
| 2026-02-19 | **New Submission Flow Bugfixes** — Five bugs fixed in the end-to-end New Submission workflow: (1) **Form not rendering** — `Action.Form` defaults to non-null empty Layout in `Action.cs`; `Action.Form ?? AutoGenerateForm()` always took left side. Fix: check `Elements is { Count: > 0 }` before using explicit form. (2) **Empty instance ID** — API returns `"id"` but `WorkflowInstanceViewModel.InstanceId` had no `[JsonPropertyName]`. Fix: added `[JsonPropertyName("id")]`. (3) **State deserialization crash** — `state` field is numeric enum (0=Active), mapped to string `Status` via `[JsonPropertyName("state")]` caused `JsonException`. Fix: removed mapping, let `Status` default to `"active"`. (4) **403 Forbidden on execute** — `ValidateWalletOwnershipAsync` hard-failed when Participant Service returned 404 (no profile). Fix: graceful degradation — log warning and allow through when participant system is unavailable/unconfigured. (5) **Response deserialization crash** — `NextActionInfo.ActionId` was `string` but server returns `int`; property names mismatched. Fix: aligned `NextActionInfo` to match `NextActionResponse` (int ActionId, ActionTitle, ParticipantId). E2E test added: `NewSubmissionFormTests.cs` (4 Playwright tests). |
| 2026-02-19 | **Genesis TX Signature Fix** — Genesis docket now contains 1 transaction (was 0). Root cause: `RegisterCreationOrchestrator` included attestation signatures in `TransactionSubmission.Signatures`, but the Validator verifies all transaction-level signatures against `SHA256("{TxId}:{PayloadHash}")` — attestation sigs were signed against different data (SHA256 of canonical attestation JSON). Fix: only include system wallet signature at transaction level; attestation sigs remain embedded in the control record payload where they were already verified during `FinalizeAsync`. Also replaced hardcoded `"genesis"` with `GenesisConstants.BlueprintId`. Full register creation walkthrough passes: auth → wallet → initiate → sign attestations → finalize → genesis TX → validator accepts → docket with 1 TX → register height 1. |
| 2026-02-19 | **Construction Permit Walkthrough — ALL 3 SCENARIOS PASS** (11/11 steps, 14/14 actions). Four fixes: (1) PS1 `$actionSenderMap` Int64/Int32 type mismatch — `ConvertFrom-Json` returns Int64 for JSON integers but hashtable keys are Int32; fixed with `[int]` cast. (2) Calculation-before-routing reorder — calculations (e.g. `riskScore`) must be applied BEFORE route condition evaluation so JSON Logic conditions can reference them. (3) `Instance.AccumulatedData` fallback — Register Service doesn't support instance-based transaction queries (always 404), so accumulated payload+calculations are now persisted on the Instance object for cross-action state reconstruction. (4) Blueprint template disclosure fix — Action 4 environmental-assessor needed `/*` disclosure so sender's own submitted data is included in transaction (otherwise Validator rejects with VAL_SCHEMA_004). Scenario A: 5/5 low-risk residential (skips env review). Scenario B: 6/6 high-risk commercial (conditional routing to env review). Scenario C: 3/3 rejection at planning review. |
| 2026-02-19 | **Deterministic Canonical JSON Hashing** — Fixed payload hash verification (TX_012, VAL_HASH_001) across HTTP and Redis boundaries. Root cause: `System.Text.Json` default encoder escapes `+` to `\u002B` in DateTimeOffset/base64, and `GetRawText()` preserves transport encoding not canonical form. Fix: added `CanonicalJsonOptions` (compact, `UnsafeRelaxedJsonEscaping`) to Blueprint Service transaction builder, Validator Core `TransactionValidator`, and `ValidationEngine`. Both producer and verifier now re-canonicalize via `Serialize(payload, CanonicalJsonOptions)` before hashing. Ping-pong walkthrough: 20/20 rounds, 40/40 actions PASS. |
| 2026-02-18 | **037-New-Submission-Page** (31 tasks, 8 phases): Redesigned MyWorkflows.razor from workflow instance list to service directory grouped by register. Created WalletPreferenceService (localStorage-backed smart default), WalletSelector.razor, NewSubmissionDialog.razor (create instance + execute Action 0). Added GetAvailableBlueprintsAsync, CreateInstanceAsync, SubmitActionExecuteAsync (with X-Delegation-Token). Fixed Pending Actions: wired wallet into ActionForm, actual SubmitActionAsync call after dialog. Swapped nav order. Tests: 10 WalletPreferenceService tests pass, UI Core 0w/0e, Web Client 0w/0e. |
| 2026-02-18 | **036-Unified-Transaction-Submission** (26 tasks, 7 phases): Created ISystemWalletSigningService (singleton, whitelist, rate limit, audit logging), unified all transaction types through `POST /api/v1/transactions/validate`, migrated register creation + blueprint publish from legacy genesis endpoint, removed legacy genesis endpoint + models + client methods, renamed ActionTransactionSubmission → TransactionSubmission. Tests: ServiceClients 24 pass, Validator 627 pass (1 pre-existing fail), Register Service 9 pass (2 pre-existing), Blueprint Service 28 pass. |
| 2026-02-18 | P0 Transaction Pipeline Audit: documented full transaction lifecycle, fixed blueprint publish to submit through validator mempool (not direct store), extended genesis endpoint for Control transaction overrides. Changed TrackingData from SortedList to Dictionary for Blazor WASM compatibility. |
| 2026-02-17 | Architecture Validation improvements: genesis TX signature skip (attestation sigs use different contract), docket build log noise fix (WARNING→DEBUG for empty queue), blueprint cache TTL removal (immutable after publish). Added Register instance query endpoint to Phase 3 backlog. |
| 2026-02-17 | Architecture Validation: 8 pipeline fixes (signature contract, blueprint cache, schema extraction, default disclosure, wallet link idempotency, LastTransactionId fallback, PreviousTransactionId field, cyclic idempotency). 3-round ping-pong walkthrough PASS. |

**2026-02-17:**
- ✅ SCHEMA-LIBRARY (034): Centralised schema registry with server-side cache and index of formal data standards
  - **Phase 1-2 (Setup/Foundational):** SchemaFieldInfo model, SchemaFieldExtractor (recursive JSON Schema traversal with $ref, nested objects, arrays), SchemaIndexEntry/SchemaIndexEntryDetail DTOs, ISchemaLibraryApiService interface
  - **Phase 3 (US3 - Index & Cache):** InMemorySchemaIndex with ConcurrentDictionary, SchemaIndexRefreshService (IHostedService with configurable interval), JsonSchemaNormaliser for metadata extraction, search endpoint with text/sector filtering
  - **Phase 4 (US5 - Multiple Sources):** ISchemaSourceProvider abstraction, JsonSchemaStoreProvider (fetches from JSON Schema Store catalog), pluggable provider registration via DI
  - **Phase 5 (US1 - Browse & Search UI):** SchemaLibrary.razor page with MudTable, search/sector/status filters, SchemaCard.razor grid view, SchemaDetail.razor with full metadata + raw JSON
  - **Phase 6 (US2 - Admin Sector Config):** ISectorFilterService with in-memory org preferences, SectorConfiguration.razor admin page, 8 built-in sectors (Financial, Healthcare, Construction, Legal, Identity, Government, Supply Chain, General)
  - **Phase 7 (US6 - Metadata Enrichment):** SchemaFieldExtractor with hierarchical field tree (depth-based indentation, type coloring, constraints, enums), enhanced SchemaDetail.razor
  - **Phase 8 (US4 - Schema Picker):** SchemaPickerDialog.razor (MudDialog with search), SchemaFieldSubsetSelector.razor (checkbox list with required fields), PropertiesPanel integration for blueprint action data schemas
  - **Phase 9 (US7 - Form Preview):** SchemaFormPreview.razor using FormSchemaService.AutoGenerateForm() + ControlDispatcher, integrated into SchemaDetail.razor
  - **Phase 10 (Admin Health):** SchemaProviderHealth.razor admin page with provider cards, health status, refresh trigger
  - **Phase 11 (Polish):** YARP routes confirmed, structured logging confirmed, regression tests pass
  - 84 tasks across 11 phases, all complete
  - Test results: Blueprint Service 300 pass (29 pre-existing SignalR), Schemas 144 pass, UI Core 517 pass
  - Files changed: ~40 (new + modified); 14 SectorFilterService tests, 15 SchemaFieldExtractor tests, 5 SchemaFieldSubsetSelector tests

**2026-02-16:**
- ✅ BLUEPRINT-ENGINE-SERVICE-REVIEW: Comprehensive Blueprint Engine & Service review (4 phases, 17 tasks)
  - **Phase 1 (Reliability):** Fixed JSON Pointer escaping in DisclosureProcessor for `~` and `/` keys; fixed JsonLogicCache race condition with per-key SemaphoreSlim locking; added Instance.Version + ConcurrencyException for optimistic concurrency; added idempotency to ActionExecutionService via idempotency key tracking
  - **Phase 2 (Performance):** Added JsonSchemaCache (SHA256-keyed MemoryCache for parsed schemas); BlueprintExtensions.BuildActionIndex() for O(1) action lookups replacing O(n) scans; TransactionConfirmationOptions for configurable polling timeouts; ConcurrentDictionary action index cache in ActionResolverService; transaction confirmation SignalR notification
  - **Phase 3 (Validation):** Added 9 publish-time validation rules: starting actions, route targets, orphan detection, JSON Pointer syntax (RFC 6901), JSON Logic syntax, participant sender references, rejection targets, form schema validity; 10 new PublishService tests
  - **Phase 4 (Tests):** Fixed all 12 pre-existing engine failures — Action.Disclosures MinLength(0→1), malformed JSON depth test, JsonE ValidateTemplate catch (InterpreterException via base JsonEException, not TemplateException), JsonE nested context $let/in syntax, JsonLogic calculation decimal/JsonElement coercion, integration blueprints missing Routes; added 38 new service tests (DelegationTokenMiddleware, InMemoryInstanceStore, InMemoryActionStore)
  - Engine: 396 pass / 0 fail / 1 skipped (was 384/12). Service: 279 pass (was 241), 19 pre-existing SignalR.
  - Files changed: 26 (19 modified, 7 new); 1,747 insertions, 167 deletions

**2026-02-15:**
- ✅ TEST-SUITE-AUDIT: Full solution test audit with quick-win fixes
  - Clean Docker rebuild + reseed, full test run across 33 projects (~4,900 tests)
  - Fixed: Peer Redis mock overload for StackExchange.Redis 2.10.x (18/18 pass)
  - Fixed: JsonStringEnumConverter on 9 Tenant enums (11 unit tests fixed, 214/230 pass)
  - Fixed: BlueprintSerializationService YAML round-trip via JSON intermediary (499/499 pass)
  - E2E tests: 251/298 pass (47 fail — Polly timeout in test fixture setup + stale selectors)
  - Remaining pre-existing failures categorized: Blueprint.Engine (17), Blueprint.Service (19),
    CLI (36), Tenant integration (16), Validator integration (58), Gateway integration (11),
    cross-service integration (6), Wallet API (2), Validator unit (1)

**2026-02-14:**
- ✅ VALIDATOR-PIPELINE-FIX: Validator Pipeline Rewire + Blueprint Conformance Enforcement
  - Rewired disconnected validation pipeline: single path Ingestion → Unverified Pool → ValidationEngine → Verified Queue → DocketBuilder
  - Removed bypass `TransactionPoolPollerService` that fed unvalidated transactions to mempool
  - `ValidationEndpoints` + genesis endpoint now submit to `ITransactionPoolPoller` (unverified pool) instead of `IMemPoolManager`
  - `DocketBuilder` + `ValidatorOrchestrator` now read from `IVerifiedTransactionQueue` instead of `IMemPoolManager`
  - `DocketBuildTriggerService` uses verified queue counts + unverified pool cleanup
  - New `ValidateBlueprintConformanceAsync` in `ValidationEngine`: starting action validation (VAL_BP_001), sender authorization via wallet derivation (VAL_BP_002), action sequencing via routes (VAL_BP_003)
  - Genesis/control transactions bypass schema and blueprint conformance checks
  - `EnableBlueprintConformance` config toggle (default: true)
  - Consensus voter hardening: `ValidateAndVoteAsync` uses full `IValidationEngine` with fallback to structural validation
  - `UnverifiedPoolCleanupService` updated: `TransactionPoolPollerService` → `IRegisterMonitoringRegistry` for active register discovery
  - Files changed: 14 modified, 1 deleted (`TransactionPoolPollerService.cs`), 6 test files updated
  - Test results: Validator Service 634 pass / 3 skipped / 1 pre-existing failure, Validator Core 200 pass
- ✅ REDIS-EVENT-STREAMS: Redis Streams Event Infrastructure for Register domain
  - New project `Sorcha.Register.Storage.Redis` with publisher, subscriber, hosted service, DI extensions
  - `RedisStreamEventPublisher`: implements `IEventPublisher` using `XADD` with `MAXLEN ~` trimming, Polly circuit breaker
  - `RedisStreamEventSubscriber`: implements `IEventSubscriber` using `XREADGROUP` consumer groups, `XACK`, pending reclaim
  - `EventSubscriptionHostedService`: BackgroundService driving the subscriber processing loop
  - `InMemoryEventSubscriber`: test-friendly subscriber paired with updated `InMemoryEventPublisher` for dispatch
  - `RegisterEventBridgeService`: BackgroundService bridging 6 domain event topics to SignalR hub notifications
  - Added `RegisterStatusChangedEvent` to Register.Core; `RegisterManager.UpdateRegisterStatusAsync` publishes it
  - Decoupled SignalR: removed direct `IHubContext` calls from endpoints and `RegisterCreationOrchestrator`
  - Validator Service wired with `ConsumerGroup = "validator-service"` for future cross-service event consumption
  - Files changed: 14 new (9 source + 5 test), 10 modified
  - Test results: 25 new Redis storage tests pass, 5 bridge tests pass, Register.Core 234 pass, Validator 619 pass
- ✅ ENFORCE-READONLY-REGISTER: Enforce read-only register access in Validator Service
  - Created `IReadOnlyRegisterRepository` interface (read-only subset of `IRegisterRepository`)
  - `IRegisterRepository` now extends `IReadOnlyRegisterRepository` (no breaking changes for Register Service)
  - `GovernanceRosterService` constructor changed to accept `IReadOnlyRegisterRepository` (compile-time safety)
  - Added `AddReadOnlyMongoRegisterStorage()` extension method — registers only `IReadOnlyRegisterRepository`
  - Validator Service DI switched from `AddMongoRegisterStorage` to `AddReadOnlyMongoRegisterStorage` — `IRegisterRepository` is NOT resolvable
  - Deleted dead code: `DocketManager.cs` (write violations, never in DI), `ChainValidator.cs` (depends on DocketManager, never in DI)
  - Test results: Register Core 148 pass, Validator Core 200 pass, Validator Service 562 pass (33 pre-existing Protobuf failures), Register Storage 68 pass
- ✅ SECURITY-HARDENING: Comprehensive security hardening (QW7-10, T1-T5, T8, T10, T11, T13) — 13 items across auth, crypto, data flow, and infrastructure
  - **QW7**: AES-CBC deprecated with `[Obsolete]` attribute + runtime `NotSupportedException`; CBC encrypt/decrypt methods removed from SymmetricCrypto
  - **QW8**: Generic error messages — replaced wallet/credential ID leaks in Blueprint CredentialEndpoints, Wallet CredentialEndpoints, WalletEndpoints; fixed `ex.Message` leaks in SystemWallet and VerifySignature
  - **QW9**: JWT `ValidAlgorithms` restricted to HS256 in JwtAuthenticationExtensions
  - **QW10**: Bootstrap one-shot — `SystemConfiguration` entity + EF migration; BootstrapEndpoints returns 409 Conflict on re-bootstrap
  - **T1**: Org-scoped blueprint authorization — `OrganizationId` on Blueprint model, `ClaimExtensions` helper, org filtering in IBlueprintStore/BlueprintService, all CRUD endpoints extract `org_id` from claims
  - **T2**: SignalR wallet ownership validation — `IParticipantServiceClient` injected into ActionsHub, `ValidateWalletOwnershipAsync` checks linked wallets, fail-closed on service errors, service tokens bypass
  - **T3**: Post-sign transaction signature verification — `GetWalletAsync` + `VerifySignatureAsync` after signing, before Register submission
  - **T4**: Client secrets hashed with Argon2id via BouncyCastle — backward-compatible (32-byte = legacy SHA256, 48-byte = Argon2id salt+hash)
  - **T5**: Per-user login rate limiting — `ITokenRevocationService` wired into Login endpoint, 429 on brute force, reset on success
  - **T8**: Replay attack protection — idempotency key methods on `IActionStore`, auto-generated from request content, 24-hour TTL, 409 Conflict on duplicate
  - **T10**: Register chain validation — PrevTxId format validation (64 chars), chain continuity check, fork detection in `TransactionManager`
  - **T11**: Shell injection eliminated — `ArgumentList` instead of string interpolation in `MacOsKeychainEncryption`, regex-validated keyId in `LinuxSecretServiceEncryptionProvider`
  - **T13**: `IDisposable` on `KeyRing`, `SymmetricCiphertext`, `IEncryptionProvider`; DEK cache 30-min TTL with `Array.Clear` on eviction; `Dispose()` on all encryption providers
  - Updated 3 crypto tests (AES-CBC roundtrip → deprecation assertion tests)
  - Files changed: 31 (27 modified, 4 new); 1375 insertions, 179 deletions
  - Test results: Cryptography 97/97 pass, Blueprint Service Integration 43/43 pass, Register Storage 1/1 pass

- ✅ 039-VERIFIABLE-PRESENTATIONS: Verifiable Credential Lifecycle & Presentations (82 tasks, 11 phases)
  - W3C Bitstring Status List: GZip+Base64 compressed bitstrings, public unauthenticated GET for verifiers, allocate/set-bit internal endpoints
  - Credential lifecycle: Active→Suspended→Revoked state machine with lazy expiry detection, usage policies (SingleUse/LimitedUse/Reusable)
  - OID4VP presentation flow: create request (nonce, TTL, credential type), match credentials, submit VP token, poll verification result
  - QR code generation: openid4vp:// deep links for physical presentation scenarios
  - DID resolution: pluggable registry (did:sorcha, did:web, did:key) with ActivitySource tracing
  - Cross-blueprint credential issuance: usage policy, display config, status list allocation from CredentialIssuanceConfig
  - Wallet credential UI: styled cards, status filter, detail dialog, presentation inbox with badge count
  - YARP routes, structured logging, health check for status list subsystem
  - Test count: 23 engine (bitstring+issuance), 15 UI (card+API), 22 wallet credential tests

**2026-02-12:**
- ✅ 031-VERIFIABLE-CREDENTIALS: Verifiable Credentials & eIDAS-Aligned Attestation System (89 tasks, 8 phases)
  - SD-JWT VC format (eIDAS 2.0 / ARF): create, sign, verify, selective disclosure via SdJwtService
  - Credential requirements on blueprint actions: `credentialRequirements` property with type, issuer, claim constraints
  - Credential issuance from blueprint actions: `credentialIssuanceConfig` with claim mappings, expiry, register recording
  - Cross-blueprint composability: credential from Flow A gates entry to Flow B (license → work-order pattern)
  - Selective disclosure: holders choose which claims to reveal; verifier only checks disclosed claims
  - Credential revocation: POST /api/v1/credentials/{id}/revoke endpoint, fail-closed/fail-open policies
  - IRevocationChecker interface for pluggable revocation status lookup
  - Wallet credential store: CRUD endpoints, credential matching, status updates via CredentialStore (EF Core)
  - WalletServiceClient: IssueCredentialAsync, GetCredentialAsync, UpdateCredentialStatusAsync, StoreCredentialAsync
  - Blueprint templates: license-approval-template.json, work-order-template.json
  - YARP route: /api/v1/credentials/** → blueprint-cluster
  - Test count: 53 credential engine tests, 6 SD-JWT selective disclosure tests, 4 revocation endpoint tests
- ✅ TEMPLATES-UX-OVERHAUL: Blueprint Templates UX Overhaul (7 phases)
  - Added `Version` property to `TemplateListItemViewModel`
  - Removed `TemplateSeedingService` and `/api/templates/seed` endpoint — replaced with external `scripts/seed-blueprints.ps1`
  - Moved template JSON files from `examples/templates/` to `blueprints/` at repo root
  - Cleaned Dockerfile of template COPY layers
  - Flattened navigation: removed `Blueprints` NavGroup → flat links (My Blueprints, Visual Designer, AI Chat Designer, Catalogue, Data Schemas)
  - Redesigned template cards: version label, inline Use button, removed parameters count and MudCardActions
  - Template wizard view: full-page detail with Participants, Actions table, BlueprintViewerDiagram, "Use This Template" saves as draft blueprint and navigates to My Blueprints with highlight
  - Blueprints page renamed to "My Blueprints" with Visual/AI editor buttons per card and `?highlight=` query string card animation
  - 3 new unit tests (TemplateListItemViewModelTests), 5 new E2E tests (wizard, back, save), 2 new E2E tests (editor buttons, highlight param)
  - Blueprint Service tests: 238 pass (seeding tests removed)

**2026-02-11:**
- ✅ 031-REGISTER-GOVERNANCE: Genesis Blueprint — Register Governance (80 tasks, 9 phases)
  - Renamed TransactionType.Genesis → Control (value 0 preserved), removed System=3
  - Governance models: GovernanceOperation, ApprovalSignature, ControlTransactionPayload, AdminRoster
  - DID scheme: `did:sorcha:w:{walletAddress}` (wallet) + `did:sorcha:r:{registerId}:t:{txId}` (register)
  - GovernanceRosterService: roster reconstruction from Control TX chain, quorum validation, proposal validation, apply operations
  - DIDResolver: wallet + register DID resolution with cross-instance support
  - Quorum: floor(m/2)+1, removal excludes target from pool, Owner bypass for Add/Remove
  - Ownership transfer: Owner→Admin swap (always additive, target must be existing Admin)
  - RightsEnforcementService: validator pipeline stage 4b between signatures and chain validation
  - Governance endpoints: GET /api/registers/{id}/governance/roster, GET .../history (paginated)
  - Register governance blueprint JSON template (register-governance-v1)
  - Test counts: Register Core 234 pass, Validator Service 620 pass (3 skipped pre-existing)
- ✅ TOKEN-AUTO-REFRESH: Fix token refresh lifecycle in Blazor WASM UI
  - BUG FIX: `GetAccessTokenAsync` now attempts refresh when token is expired (was returning null without trying)
  - BUG FIX: `AuthenticatedHttpMessageHandler` 401 handler now attempts refresh even when initial token was null
  - Added `CloneRequestAsync` helper — `HttpRequestMessage` can't be resent, retry creates fresh request
  - New `TokenRefreshService` with timer-based proactive refresh (fires 5 min before expiry)
  - New `tokenLifecycle.js` — tab visibility change detection triggers immediate token check
  - Server-side logout call (best-effort POST to `/api/auth/logout` before clearing cache)
  - 14 new unit tests in `TokenRefreshServiceTests`
  - Added 4 future auth hardening tasks to deferred-tasks.md (rotation, cross-tab sync, expiry warning, sliding window)

**2026-02-10:**
- ✅ SEC-006: Enforce wallet-to-user binding in Blueprint Service
  - Added `ValidateWalletOwnershipAsync` in `ActionExecutionService` — validates sender wallet ownership via `IParticipantServiceClient`
  - Enforced on both execute and reject endpoints; returns 403 for unauthorized wallets
  - Service principal bypass (token_type=service) and null caller backward compat
  - 8 new unit tests; Blueprint Service test count: 244 pass

**2026-02-09:**
- ✅ 028-FIX-TRANSACTION-PIPELINE: Fix transaction submission pipeline — route action transactions through Validator Service (29 tasks, 5 phases)
  - CRITICAL: Action transactions now flow Blueprint Service → Validator mempool → docket sealing → Register write-back (was bypassing Validator entirely)
  - Added IValidatorServiceClient.SubmitTransactionAsync + BuiltTransaction.ToActionTransactionSubmission mapper
  - ActionExecutionService routes through Validator with 30s confirmation polling against Register
  - Register monitoring: IRegisterMonitoringRegistry.RegisterForMonitoring called after mempool addition
  - Direct POST /transactions endpoint restricted to CanWriteDockets (internal/diagnostic only)
  - Fixed PayloadHash computation to match Validator's canonical JSON serialization
  - Fixed RegisterServiceClient.GetTransactionAsync missing auth header
  - Fixed register height tracking: count-based (Height = DocketNumber + 1) so genesis docket properly increments
  - Fixed GetLatestDocket endpoint to use Height - 1 for count-based convention
  - Idempotent docket write-back: handles duplicate key errors on transaction/docket inserts
  - Ping-Pong walkthrough verified: 10/10 actions, 10 properly chained dockets (0-9), 11 transactions
  - Test count: Blueprint Service 235 pass, Validator Service 597 pass, ServiceClients 9 pass, Register Core 148 pass

**2026-02-08:**
- ✅ FIX-TEMPLATE-SEEDING: Fix Docker template seeding + persist template storage
  - Dockerfile now copies examples/templates/ into /app/templates for container runtime seeding
  - BlueprintTemplateService migrated from raw Dictionary to IDocumentStore<BlueprintTemplate, string> (thread-safe, swappable)
  - InMemoryDocumentStore registered in DI (consistent with Blueprint Service's in-memory storage pattern)
  - Test count: Blueprint Service 224 pass (unchanged)
- ✅ 027-BLUEPRINT-TEMPLATE-LIBRARY COMPLETE: Blueprint Template Library & Ping-Pong Blueprint (31 tasks, 7 phases)
  - Ping-Pong blueprint template (2 participants, cyclic routing) in examples/templates/
  - Cycle detection changed from hard rejection to warning — cyclic blueprints now publish with hasCycles metadata
  - TemplateSeedingService (IHostedService) seeds 4 built-in templates at startup
  - Templates.razor page enhanced with detail panel and improved empty state
  - POST /api/templates/seed admin endpoint for manual re-seeding
  - Ping-Pong walkthrough script for end-to-end pipeline verification
  - Test count: Blueprint Service 224 pass (was 214), Engine 323 pass (unchanged)
- ✅ 026-FIX-REGISTER-CREATION-PIPELINE COMPLETE: Fix 8 issues in register creation flow (38 tasks, 9 phases)
  - CRITICAL: Fixed payload data lost in docket write — DocketBuildTriggerService transaction mapping now populates Payloads, PayloadCount, SenderWallet, Signature, MetaData, PrevTxId, TimeStamp
  - CRITICAL: Fixed genesis write failure preventing retry — _genesisWritten flag only set on success, retry count with max 3 attempts before unregistering
  - HIGH: Advertise flag threaded through two-phase creation (InitiateRegisterCreationRequest → PendingRegistration → FinalizeAsync)
  - HIGH: Peer Service integration — IPeerServiceClient.AdvertiseRegisterAsync, POST /api/registers/{id}/advertise endpoint, fire-and-forget notifications from Register Service
  - HIGH: GenesisManager.NeedsGenesisDocketAsync no longer silently swallows exceptions
  - MEDIUM: Validator monitoring endpoint — GET /api/admin/validators/monitoring exposes RegisterMonitoringRegistry.GetAll()
  - MEDIUM: Register.Service.Tests restored — fixed 26 compilation errors across 4 files (xUnit v3 ValueTask, constructor changes, property renames)
  - MEDIUM: GenesisConstants.BlueprintId replaces magic string "genesis" in ValidationEndpoints.cs
  - AppHost.cs: peerService declared before registerService, WithReference(peerService) added
  - RegisterManager.CreateRegisterAsync made virtual for testability
  - Test results: Register Service Unit 18 pass, Validator Service baseline maintained
- ✅ BP-11.3 COMPLETE: Monitoring and alerting for System Health dashboard
  - AlertAggregationService in API Gateway evaluates validator/peer metrics against configurable thresholds (9 rules)
  - GET /api/alerts endpoint with AlertsResponse (severity counts, sorted alerts)
  - Wired HealthAggregationService stub to fetch real validator/peer metrics
  - AlertService in UI with change detection (new/resolved alert events)
  - AlertsPanel component, per-service alerts in ServiceHealthCard detail dialog
  - Active Alerts KPI card in KpiSummaryPanel, snackbar notifications for Warning+ alerts
  - 13 tests: 9 threshold evaluation (ApiGateway.Tests), 4 UI service (UI.Core.Tests)
- ✅ 023-CONSOLIDATE-TX-VERSIONING COMPLETE: Collapse TransactionVersion V1-V4 into single V1 with all V4 capabilities
  - Removed V2/V3/V4 from enum, factory, version detector
  - Updated 6 source files + 12 test/benchmark files across 4 projects
  - 127 TransactionHandler tests + 194 Blueprint Service tests passing
  - Zero remaining V2/V3/V4 references in source/test code
- ✅ PR-110-REVIEW-FIXES COMPLETE: Address 12 P2P code review issues (3 critical, 4 high, 5 medium)
  - CRITICAL: Race condition (Dictionary → ConcurrentDictionary), EF Core InitialPeerSchema migration, hardcoded password → env var
  - HIGH: JWT auth with CanManagePeers/RequireAuthenticated policies, gRPC idle timeout (5min), RegisterCache eviction (100K tx / 10K dockets), replication timeout (30min), batched docket pulls
  - MEDIUM: Named constants (MaxConsecutiveFailuresBeforeDisconnect etc.), seed node reconnection, gRPC 16MB message limits, idle connection cleanup in heartbeat loop
  - 504 tests passing (4 new eviction tests), 0 warnings
- ✅ UI-MODERNIZATION COMPLETE: Comprehensive overhaul of Sorcha.UI Blazor WASM application (92/94 tasks, 13 phases)
  - Admin: Organization management, validator admin panel (mempool, consensus), service principal management
  - Flattened navigation with direct links to admin tools (System Health, Validator, Service Principals)
  - Core Workflows: Replaced placeholder MyWorkflows and MyActions pages with real workflow instance management
  - Blueprint Cloud Persistence: Designer and Blueprints pages backed by Blueprint Service API, publishing flow, version management
  - Dashboard: Live stat cards wired to gateway /api/dashboard endpoint (DashboardService + IDashboardService)
  - User Pages: Real Wallet Service integration (CRUD, addresses, signing) and Register Service transaction queries
  - Template Library: Backend template API integration (CRUD, evaluate, validate) replacing hardcoded templates
  - Explorer: Docket/chain inspection, advanced OData query builder for cross-register searches
  - UX: Consistent TruncatedId component for all long identifiers — first 6 + last 6 chars with ellipsis (e.g. "0x3f8a...b4c2e1")
  - Shared: EmptyState, ServiceUnavailable reusable components with retry actions
  - 10 new services, 8+ new model files, 12+ new components
  - E2E: 7 new Docker test files, 4 new page objects, 0 warnings across all UI and E2E projects
  - LoadBlueprintDialog refactored to self-load via IBlueprintApiService
  - Fixed NUnit1033 warnings (42 occurrences) across pre-existing E2E test files

**2026-02-07:**
- ✅ P2P-TOPOLOGY-REFACTOR COMPLETE: Refactor Peer Service from hub-and-spoke to true P2P topology (10 phases)
  - Replaced 3-hardcoded-hub-node model with equal-peer architecture (all nodes equivalent)
  - Seed nodes serve only as bootstrap peers — authority from cryptographic attestations, not node identity
  - New domain models: SeedNodeConfiguration, RegisterSubscription, ReplicationMode (ForwardOnly/FullReplica), RegisterSyncState
  - PeerConnectionPool for multi-peer gRPC channel management
  - Gossip-style PeerExchangeService for mesh network discovery beyond seed nodes
  - Register-aware peering: peers track which registers others hold via RegisterAdvertisementService
  - RegisterCache + RegisterReplicationService for per-register sync with admin-configured replication mode
  - P2P heartbeat (PeerHeartbeatBackgroundService + PeerHeartbeatGrpcService) with per-register version exchange
  - PeerListManager migrated from SQLite to PostgreSQL (EF Core, consistent with Wallet/Tenant services)
  - New proto files: peer_heartbeat.proto, register_sync.proto; modified peer_discovery.proto
  - Deleted 20 hub-specific source files, 3 old proto files, 6 hub-specific test files
  - 162 net new tests; 433 pass / 29 pre-existing fail baseline
- ✅ RESOLVE-RUNTIME-STUBS COMPLETE: Eliminate all NotImplementedException stubs and resolve production-critical TODOs (62 tasks, 10 phases)
  - Zero NotImplementedException remaining in src/ (was 5: WalletManager, DelegationService, JsonTransactionSerializer x2, Transaction)
  - Auth/Security: JWT claim extraction in WalletEndpoints, DelegationEndpoints, BootstrapEndpoints
  - Crypto: RecoverKeySetAsync (ED25519/P-256/RSA-4096), KeyChain ExportAsync/ImportAsync with AES-256-GCM
  - Validator-Peer: ValidatorRegistry chain storage, SignatureCollector gRPC, RotatingLeaderElection heartbeat, DocketBuildTrigger consensus, ConsensusFailureHandler persistence
  - Peer Service: Replaced all hardcoded zeros with SystemRegisterCache real values (HubNodeConnectionService, HeartbeatService, PeriodicSyncService, HeartbeatMonitorService, ValidatorGrpcService)
  - Data Layer: PendingRegistrationStore rewritten from ConcurrentDictionary to Redis-backed with TTL expiry
  - Transaction Versioning: V1/V2/V3 adapters in TransactionFactory, binary serialization in Transaction and JsonTransactionSerializer
  - DelegationService.GetAccessById: Added repository method
  - Test results: TransactionHandler 135 pass, Cryptography 77 pass, Validator 594 pass, Register Core 148 pass, Engine 323 pass, Fluent 88 pass, Wallet 251 pass, Blueprint Service 214 pass
- ✅ BP-11.2 COMPLETE: Blueprint Service security hardening
  - Added `[Authorize]` to ActionsHub SignalR hub + `.RequireAuthorization()` on hub mapping
  - Secured file download endpoint (`/api/files/...`) with `CanExecuteBlueprints` policy
  - Added CORS policy (SEC-005) matching API Gateway/Tenant Service pattern
  - Added `.AllowAnonymous()` to health endpoint for explicit intent
  - Hardened 13 generic catch blocks — stopped leaking `ex.Message` to clients
  - Added `logger.LogWarning()` for all exception handling paths
  - All tests passing: 194 unit + 43 integration
- ✅ TRANSACTION-QUERY-API COMPLETE: Extend IRegisterServiceClient to support querying transactions by PrevTxId (26 tasks, 6 phases)
  - Added PrevTxId ascending index to MongoDB CreateTransactionIndexesAsync
  - Added GetTransactionsByPrevTxIdAsync to IRegisterRepository with MongoDB + InMemory implementations
  - Added GetTransactionsByPrevTxIdPaginatedAsync to QueryManager with pagination support
  - Added GET /api/query/previous/{prevTxId}/transactions endpoint to Register Service
  - Added GetTransactionsByPrevTxIdAsync to IRegisterServiceClient + RegisterServiceClient implementation
  - Added fork detection (VAL_CHAIN_FORK) to ValidationEngine.ValidateChainAsync
  - Created new Sorcha.ServiceClients.Tests project with 4 unit tests
  - New tests: 23 across Register Core (14), ServiceClients (4), Validator Service (5)
  - MongoDB integration tests: 5 new (PrevTxId queries + index verification via Testcontainers)
  - Test results: Register Core 139 pass, ServiceClients 4 pass, Validator Service 540 pass, MongoDB 5 pass

**2026-02-06:**
- ✅ BLUEPRINT-ENGINE-INTEGRATION COMPLETE: Wire Blueprint Engine into ActionExecutionService (36 tasks, 12 phases)
  - Replaced 4 stub methods in ActionExecutionService with real Engine delegation (validate, route, calculate, disclose)
  - Implemented Route-based routing in RoutingEngine with parallel branch support and legacy fallback
  - Extended RoutingResult with RoutedAction record, NextActions list, IsParallel, Parallel() factory
  - Added RouteBuilder and RejectionConfigBuilder to Fluent API with full ActionBuilder integration
  - Implemented graph cycle detection (DFS with coloring) in blueprint publish validation
  - Wired JsonLogicCache into JsonLogicEvaluator for expression caching
  - Implemented ExecutionMode.ValidationOnly short-circuit in ActionProcessor
  - Fixed TransactionBuilderServiceExtensions stubs (now produce real serialized transaction data)
  - Fixed POST /api/actions disclosure (now processes disclosure rules per participant)
  - New tests: 57 across Engine (14 RoutingResult + 7 Route routing + 3 ValidationOnly), Fluent (12 Route/Rejection), Service (8 cycle detection + 5 disclosure + 3 TransactionBuilder extension + 5 engine delegation)
  - Test results: Engine 323 pass (17 pre-existing failures), Fluent 88 pass, Service 214 pass
- ✅ PEER-TESTS COMPLETE: Peer Service unit test coverage (232 new tests, 11 test files)
  - `CircuitBreakerTests` (25 tests): State transitions, thresholds, fallbacks, HalfOpen recovery
  - `GossipProtocolEngineTests` (33 tests): ShouldGossip, RecordSeen, PrepareForNextRound, BloomFilter, cleanup
  - `PushNotificationHandlerTests` (29 tests): Subscriber management, stream notifications, failure cleanup
  - `HubNodeValidatorTests` (16 tests): Hostname pattern validation (n0-n2.sorcha.dev)
  - `HeartbeatValidatorTests` (10 tests): Timeout detection, failover thresholds, constants
  - `RetryBackoffValidatorTests` (14 tests): Exponential backoff, cap at 60s, attempt validation
  - `SyncValidatorTests` (8 tests): Sync timing, interval constants
  - `SystemRegisterValidatorTests` (6 tests): System register ID validation
  - `PeerNodeTests` (19 tests): Equality, hashing, HashSet behavior, defaults
  - `HubNodeInfoTests` (15 tests): Connection state, failure tracking, gRPC address
  - `TransactionNotificationTests` (11 tests): Default values, property round-trip
  - Peer Service test count: 139 → 371 (167% increase)

**2026-01-31:**
- ✅ VAL-9.49/VAL-9.50 COMPLETE: Performance testing for Validator Service
  - NBomber-based validation throughput testing (validator_throughput, validator_batch, validator_stress)
  - Consensus latency testing (validator_metrics, validator_consensus_metrics, validator_registry)
  - Memory pool statistics endpoint performance testing
  - Test suite selection parameter (--test-suite validator)
  - Integrated into Sorcha.Performance.Tests
  - Sprint 9G now 100% complete (7/7 tasks)
  - **Sprint 9 (Validator Service) 100% COMPLETE** (50/50 tasks)
- ✅ VAL-9.39 COMPLETE: Validator Approval flow (consent mode)
  - IValidatorRegistry methods: GetPendingValidatorsAsync, ApproveValidatorAsync, RejectValidatorAsync
  - ValidatorApprovalRequest/ValidatorApprovalResult types
  - REST endpoints: GET /{registerId}/pending, POST /{validatorId}/approve, POST /{validatorId}/reject
  - ValidatorListChangeType: ValidatorApproved, ValidatorRejected events
  - Registration flow updated to set Pending status in consent mode
  - 22 unit tests for approval flow
  - Sprint 9F now 100% complete (7/7 tasks)
- ✅ SETUP-001 COMPLETE: First-run setup wizard for fresh installations
  - `scripts/setup.ps1` - PowerShell setup wizard with 8-step process
  - `scripts/setup.sh` - Bash equivalent for Linux/macOS
  - `scripts/setup-config.yaml` - Configuration template with defaults
  - `scripts/validate-environment.ps1` - Comprehensive environment validation
  - `docs/FIRST-RUN-SETUP.md` - Complete setup documentation
  - Environment detection, configuration generation, volume creation
  - Infrastructure startup, service validation, health checks
  - Interactive and non-interactive modes supported

**2026-01-30:**
- ✅ VAL-9.41 COMPLETE: Control Docket Processor for governance transactions
  - `IControlDocketProcessor` interface and `ControlDocketProcessor` implementation
  - Extracts control transactions from dockets (7 action types supported)
  - Validates control transactions against control blueprint rules
  - Applies control actions: validator.register, validator.approve, validator.suspend, validator.remove
  - Applies control actions: config.update, blueprint.publish, register.updateMetadata
  - Refreshes GenesisConfigService and ValidatorRegistry on state changes
  - 36 unit tests with comprehensive coverage
- ✅ VAL-9.44 COMPLETE: Validator Service configuration system (memory limits, performance)
  - ValidationEngineConfiguration with batch size, parallel validation, timeout settings
  - MemPoolConfiguration with max size, TTL, priority quotas
  - ConsensusConfiguration with approval threshold, vote timeout, retry settings
  - DocketBuildConfiguration with time/size thresholds, max transactions per docket
- ✅ VAL-9.45 COMPLETE: Validator Service metrics API endpoints
  - `/api/metrics` - Aggregated metrics from all subsystems
  - `/api/metrics/validation` - Validation engine stats
  - `/api/metrics/consensus` - Consensus, distribution, failure stats
  - `/api/metrics/pools` - Verified queue metrics
  - `/api/metrics/caches` - Blueprint cache stats
  - `/api/metrics/config` - Current configuration (redacted)
- ✅ SEC-002 COMPLETE: API rate limiting and throttling for all services
  - Added RateLimiter extension methods to ServiceDefaults
  - 5 policy types: API (100/min), Authentication (10/min), Strict (5/min), Heavy (10 concurrent), Relaxed (1000/min)
  - Applied to all 7 services: API Gateway, Blueprint, Register, Validator, Wallet, Tenant, Peer
  - IP-based partitioning with X-Forwarded-For proxy support
  - Rate limit headers (Retry-After, X-RateLimit-Policy) on 429 responses
- ✅ SEC-003 COMPLETE: Input validation hardening (OWASP compliance)
  - Created InputValidationMiddleware with attack pattern detection
  - SQL injection protection with comprehensive regex patterns
  - XSS attack detection (script tags, event handlers, javascript: URLs)
  - Path traversal prevention (../, encoded variants)
  - Command injection detection (shell metacharacters, dangerous commands)
  - LDAP injection protection
  - Configurable via InputValidationOptions (max body size, query length, header length)
  - Applied to all 7 services: API Gateway, Blueprint, Register, Validator, Wallet, Tenant, Peer
  - Health/alive/scalar/openapi paths excluded from validation

**2026-01-29:**
- ✅ SEC-001 COMPLETE: HTTPS enforcement with HSTS for all services
  - Added UseHttpsEnforcement() extension method to ServiceDefaults
  - HSTS header (max-age=1yr, includeSubDomains, preload) in production
  - Applied to all 7 services: API Gateway, Blueprint, Register, Validator, Wallet, Tenant, Peer
- ✅ VAL-9.46/47/48 COMPLETE: Validator Service integration tests (133 tests passing)
  - ValidationEngineIntegrationTests: Transaction validation, batch processing, payload hash verification
  - ConsensusEngineIntegrationTests: Multi-validator consensus, docket publishing, failure handling
  - GenesisConfigServiceIntegrationTests: Genesis transaction, control record validation
  - DocketBuilderIntegrationTests: Docket construction, merkle tree, transaction ordering
  - MemPoolIntegrationTests: Transaction pool, expiration, priority, concurrency
  - ServiceClientIntegrationTests: Register, Blueprint, Peer, Wallet client integration
  - BlueprintCacheIntegrationTests: Caching, version resolution, invalidation
  - LeaderElectionIntegrationTests: Leader status, rotating election, failure handling
- ✅ AUTH-001 UPDATED: Tenant Service PostgreSQL repositories confirmed complete (95%)
  - All 3 repositories fully implemented: Organization, Identity, Participant
  - 67 integration tests passing
  - EF Core DbContext with multi-schema support

**2026-01-28:**
- ✅ UI-REGISTER-MANAGEMENT 100% COMPLETE: Register list, details, creation, query (70/70 tasks)
  - Enhanced CreateRegisterWizard with 4-step flow including wallet selection
  - Added RegisterSearchBar component for client-side filtering
  - Created TransactionQueryForm and Query page for cross-register wallet search
  - Added clipboard.js interop with snackbar confirmation
  - Full data-testid attributes for E2E testing
- ✅ CLI-REGISTER-COMMANDS 100% COMPLETE: Two-phase creation, dockets, queries
  - `sorcha register create/list/dockets/query` commands
  - System.CommandLine 2.0.2 with proper option naming conventions
  - Refit HTTP clients for API integration

**2026-01-26:**
- 📋 VALIDATOR-SERVICE-REQUIREMENTS.md UPDATED: Decentralized consensus architecture
  - **Leader election** for docket building (rotating/Raft mechanisms)
  - Dual-role validator (leader/initiator + confirmer)
  - Multi-validator consensus with configurable thresholds
  - **Consensus failure handling** (abandon docket, retry transactions)
  - Genesis blueprint integration for register governance
  - Blueprint versioning via transaction chain
  - Multi-blueprint registers (corrected from single-blueprint)
  - **gRPC communication** via Peer Service
- 📋 GENESIS-BLUEPRINT-SPEC.md CREATED: Genesis block and control blueprint specification
  - Register Control Blueprint schema
  - **Leader election configuration** (mechanism, heartbeat, timeout)
  - Validator registration models (public/consent)
  - Control actions for register governance
  - **Control blueprint versioning** for governance updates
  - Docket structure with multi-signature support
- 📋 Sprint 9 EXPANDED: 14 tasks → 50 tasks (560 hours estimated)
  - Split into 7 sub-sprints (9A-9G) for better tracking
  - Added leader election tasks (9C)
  - Added consensus failure handling, gRPC integration
  - Added control blueprint version resolver

**2026-01-21:**
- ✅ UI-CONSOLIDATION 100% COMPLETE: Admin to Main UI migration (35/35 tasks)
  - All Designer components migrated (ParticipantEditor, ConditionEditor, CalculationEditor)
  - Export/Import dialogs with JSON/YAML support
  - Offline sync service and components
  - Consumer pages: MyActions, MyWorkflows, MyTransactions, MyWallet, Templates
  - Settings page with profile management, Help page with documentation
  - Configuration service tests (59 tests)
  - Fixed Docker profile configuration (relative URLs)
  - **Sorcha.Admin removed from solution** (projects and directories deleted)
  - Documentation updated, deprecation notices added before removal

**2026-01-20:**
- ✅ BP-5.8 COMPLETE: Client-side SignalR integration for Main UI
  - ActionsHubConnection service for real-time action notifications
  - MyActions page with live connection status and snackbar alerts
  - API Gateway routes for /actionshub SignalR endpoint
- 🟡 SETUP-001 PARTIAL: Wallet encryption permissions fix implemented
  - Added `EnsureFallbackDirectoryIsWritable()` with clear error messages
  - Updated setup scripts to fix Docker volume permissions (UID 1654)
  - Created `fix-wallet-encryption-permissions.ps1/.sh` quick-fix scripts
  - Added helpful comments in docker-compose.yml
- ✅ System Schema Store feature complete (T001-T080)

**2026-01-01:**
- ✅ TENANT-SERVICE-001 COMPLETE: Bootstrap API endpoint implemented
- ✅ BOOTSTRAP SCRIPTS CREATED: PowerShell and Bash automation scripts

**2025-12-13:**
- ✅ WS-008/009 COMPLETE: Wallet Service EF Core repository and PostgreSQL migrations

**2025-12-12:**
- ✅ AUTH-003 COMPLETE: PostgreSQL + Redis deployment
- ✅ AUTH-004 COMPLETE: Bootstrap seed scripts
- ✅ AUTH-002 COMPLETE: Service authentication integration

**2025-12-09:**
- ✅ SEC-004 COMPLETE: Security headers added to all services
- ✅ REG-CODE-DUP COMPLETE: Resolved DocketManager/ChainValidator duplication
- ✅ Sprint 10 COMPLETE: All 16 orchestration tasks finished
- ✅ Sprint 8 COMPLETE: All 11 validation tasks finished

---

## Task Status Summary

### By Phase

| Phase | Total Tasks | Complete | In Progress | Not Started | % Complete | Details |
|-------|-------------|----------|-------------|-------------|------------|---------|
| **Phase 1: Blueprint-Action** | 118 | 70 | 0 | 48 | **59%** | [View Tasks](tasks/phase1-blueprint-service.md) |
| **Phase 2: Wallet Service** | 34 | 34 | 0 | 0 | **100%** ✅ | [View Tasks](tasks/phase2-wallet-service.md) |
| **Phase 3: Register Service** | 15 | 14 | 0 | 1 | **93%** ✅ | [View Tasks](tasks/phase3-register-service.md) |
| **Phase 4: Enhancements** | 25 | 0 | 0 | 25 | 0% | [View Tasks](tasks/phase4-enhancements.md) |
| **Production Readiness** | 10 | 7 | 0 | 3 | **70%** | [View Tasks](tasks/production-readiness.md) |
| **CLI Admin Tool** | 60 | 0 | 0 | 60 | 0% | [View Tasks](tasks/cli-admin-tool.md) |
| **Deferred** | 43 | 0 | 0 | 43 | 0% | [View Tasks](tasks/deferred-tasks.md) |
| **TOTAL** | **294** | **122** | **0** | **172** | **41%** | |

### By Priority

| Priority | Total | Complete | In Progress | Not Started |
|----------|-------|----------|-------------|-------------|
| **P0 - Critical (MVD Blocker)** | 40 | 7 | 0 | 33 ⚠️ |
| **P1 - High (Production Ready)** | 32 | 0 | 0 | 32 ⚠️ |
| **P2 - Medium (Enhancements)** | 65 | 59 | 0 | 6 |
| **P3 - Low (Post-MVD)** | 66 | 42 | 0 | 24 |

**Note:** Sprint 9 (Validator Service) expanded significantly for decentralized consensus architecture.

---

## Priority Definitions

**P0 - Critical (MVD Blocker):**
Tasks that must be completed for the MVD to function. Without these, the end-to-end workflow will not work.

**P1 - High (MVD Core):**
Important tasks that significantly enhance the MVD but have workarounds if delayed.

**P2 - Medium (MVD Nice-to-Have):**
Tasks that improve quality, performance, or developer experience but aren't essential for MVD launch.

**P3 - Low (Post-MVD):**
Enhancement tasks that can be deferred until after MVD is complete.

---

## Detailed Task Lists by Phase

| Phase | Description | Link |
|-------|-------------|------|
| [Phase 1: Blueprint-Action Service](tasks/phase1-blueprint-service.md) | Core execution engine, Sprints 1-11 | 118 tasks |
| [Phase 2: Wallet Service](tasks/phase2-wallet-service.md) | REST API, EF Core, integration | 34 tasks |
| [Phase 3: Register Service](tasks/phase3-register-service.md) | Transaction storage, OData | 15 tasks |
| [Phase 4: Post-MVD Enhancements](tasks/phase4-enhancements.md) | Quality, performance, advanced features | 25 tasks |
| [Production Readiness](tasks/production-readiness.md) | Security, auth, operations | 10 tasks |
| [CLI Admin Tool](tasks/cli-admin-tool.md) | Cross-platform CLI, 5 sprints | 60 tasks |
| [Deferred Tasks](tasks/deferred-tasks.md) | Post-launch features | 10 tasks |

**Key Specifications:**
| Document | Description |
|----------|-------------|
| [VALIDATOR-SERVICE-REQUIREMENTS.md](VALIDATOR-SERVICE-REQUIREMENTS.md) | Decentralized consensus validator |
| [GENESIS-BLUEPRINT-SPEC.md](GENESIS-BLUEPRINT-SPEC.md) | Genesis block and control blueprint |

---

## Critical Path (MVD Blocking)

```
BP-3.x (Service Layer) → BP-4.x (Action APIs) → BP-5.5 (SignalR) ✅
    ↓
WS-025 → WS-026.x (Wallet API) ✅
    ↓
WS-INT-x (Integration) ✅
    ↓
REG-001 → REG-005/006/007 (Register API) ✅
    ↓
REG-INT-x (Full Integration) ✅
    ↓
BP-7.1 (E2E Tests) ✅
    ↓
VAL-9.x (Validator Service - Decentralized Consensus) ✅ COMPLETE
    ├── VAL-9A: Core Infrastructure ✅
    ├── VAL-9B: Validation Engine ✅
    ├── VAL-9C: Initiator Role (Docket Building) ✅
    ├── VAL-9D: Confirmer Role ✅
    ├── VAL-9E: Service Integration (Peer, Register, Blueprint) ✅
    ├── VAL-9F: Validator Registration & Genesis ✅
    └── VAL-9G: Configuration & Testing ✅
    ↓
BP-11.x (Production Readiness) ⚠️ CURRENT BLOCKER
    ↓
PEER-023 (P2P Topology Refactor) ✅ COMPLETE
    ├── Phase 1-3: Domain models, PostgreSQL migration, proto files ✅
    ├── Phase 4-5: Connection pool, peer exchange ✅
    ├── Phase 6-7: Register-aware sync, advertisements ✅
    ├── Phase 8-9: P2P heartbeat, service integration ✅
    └── Phase 10: Hub code cleanup ✅
    ↓
PEER-024 (Peer Network Management & Observability) ✅ COMPLETE
    ├── Phase 1-2: Entity changes, service methods, unit tests ✅
    ├── Phase 3-8: REST endpoints, CLI commands, Blazor UI (6 user stories) ✅
    └── Phase 9: Polish, OpenAPI docs, endpoint tests ✅
```

---

## Task Management

### Weekly Review Process

1. **Monday:** Review completed tasks from previous week
2. **Wednesday:** Check in-progress tasks, identify blockers
3. **Friday:** Plan next week's tasks, update priorities

### Success Metrics

**Sprint Completion:**
- ✅ Sprint 1: 100% (7/7 tasks)
- ✅ Sprint 2: 100% (8/8 tasks)
- ✅ Sprint 3-7: 100% Complete
- ✅ Sprint 8: 100% (11/11 tasks)
- ✅ Sprint 9: 100% (50/50 tasks) - Validator Service Complete!
- ✅ Sprint 10: 100% (16/16 tasks)

**Code Quality:**
- Test coverage >85% for all new code
- Zero critical bugs
- All CI/CD checks passing

---

**Related Documents:**
- [MASTER-PLAN.md](MASTER-PLAN.md) - Overall implementation plan
- [Project Constitution](constitution.md) - Standards and principles
- [Project Specification](spec.md) - Requirements and architecture

---

**Last Updated:** 2026-02-27
**Document Owner:** Sorcha Architecture Team
