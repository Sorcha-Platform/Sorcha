# Sorcha Platform Service Analysis

**Generated:** 2026-03-16 | **Branch:** master @ 41c3a2d8

This document provides a comprehensive analysis of every Sorcha service: test coverage, functionality delivered, deferred work, and production gaps.

---

## Executive Summary

| Service | MVD % | Tests | Pass | Fail | Skip | Key Gap |
|---------|-------|-------|------|------|------|---------|
| **Blueprint** | 100% | 1,387 | 1,385 | 1 | 1 | In-memory storage (no persistence) |
| **Register** | 100% | 672 | 669 | 3 | 0 | Test drift from refactoring |
| **Validator** | 100% | 1,211 | 1,211 | 0 | 0 | Distributed consensus deferred (P3) |
| **Tenant** | 95% | 778 | 772 | 5 | 1 | SMTP stub, IDP test drift |
| **Wallet** | 95% | 791 | 761 | 30 | 0 | HSM/Key Vault missing, integration tests broken |
| **Peer** | 95% | 697 | 691 | 0 | 6 | mTLS, transaction gossip not wired |
| **API Gateway** | 100% | 27 | 27 | 0 | 0 | CORS/HTTPS hardening |
| **Shared Libs** | -- | 194 | 194 | 0 | 0 | -- |
| **Auth Integration** | -- | 39 | 39 | 0 | 0 | -- |
| **UI Core** | -- | ~63 files | -- | BUILD FAIL | -- | Read-only property assignment |
| **UI E2E** | -- | ~179 | -- | -- | -- | Requires Docker |
| **TOTAL** | -- | **5,796+** | **5,549+** | **39** | **7+** | |

**Platform Production Readiness: ~30%** -- All services are MVD feature-complete but lack security hardening, deployment automation, and operational tooling.

---

## 1. Blueprint Service

**Purpose:** Workflow orchestration engine -- blueprint lifecycle, action execution, template system, schema store, verifiable credentials, real-time notifications.

**Completion:** 100% MVD

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Blueprint.Service.Tests | 397 | 0 | 0 | 397 |
| Blueprint.Service.IntegrationTests | 42 | 1 | 0 | 43 |
| Blueprint.Engine.Tests | 419 | 0 | 1 | 420 |
| Blueprint.Models.Tests | 265 | 0 | 0 | 265 |
| Blueprint.Fluent.Tests | 106 | 0 | 0 | 106 |
| Blueprint.Schemas.Tests | 129 | 0 | 0 | 129 |
| Blueprint.Schemas.Core.Tests | 27 | 0 | 0 | 27 |
| **Total** | **1,385** | **1** | **1** | **1,387** |

### Test Failures

| Test | Issue |
|------|-------|
| `SearchExternalSchemas_WithQuery_ReturnsResults` | Expects `SchemaStore.org` provider but gets `IFC` -- search ordering changed when new provider was added. Test expectation fix needed. |

### Endpoints (55 REST + 1 SignalR hub)

- **Blueprint CRUD** (`/api/blueprints`) -- 9 endpoints: list, get, create, update, delete, validate, publish, versions
- **Templates** (`/api/templates`) -- 8 endpoints: CRUD, evaluate, validate, examples, usage tracking
- **Actions** (`/api/actions`) -- 5 endpoints: list available, submit, reject, get details
- **Instances** (`/api/instances`) -- 6 endpoints: create, get, execute action, reject, state reconstruction, next-actions
- **Execution Helpers** (`/api/execution`) -- 4 endpoints: validate, calculate, route, disclose
- **Schema Store** (`/api/v1/schemas`) -- 12 endpoints: CRUD, external search/import, deprecate, activate, publish
- **Schema Library** -- 7 endpoints: index search, sector preferences, provider management
- **Credentials** (`/api/v1/credentials`) -- 4 endpoints: revoke, suspend, reinstate, refresh
- **Status Lists** -- 3 endpoints: W3C Bitstring Status Lists (get, allocate, set bit)
- **Operations/Notifications/Files** -- 4 endpoints
- **SignalR Hub** at `/actionshub` for real-time action notifications

### Key Functionality

1. Blueprint CRUD with version tracking and register integration
2. Multi-party action orchestration with transaction signing
3. Stateful workflow instances with state reconstruction
4. JSON-e template system with parameter evaluation
5. Portable execution engine (schema validation, JSON Logic, routing, disclosure)
6. Schema store with external import (SchemaStore.org, IFC)
7. SD-JWT VC lifecycle with W3C Bitstring Status Lists
8. SignalR real-time notifications with Redis backplane
9. Async encryption pipeline with operation tracking
10. AI integration (Anthropic provider, chat orchestration, tool execution)

### Deferred Work

| ID | Task | Priority | Effort |
|----|------|----------|--------|
| GAP-008 | Blueprint/Validator wire-format identity encoding | P2 | 8h |
| TRUST-001 | Verifiable calculations (Validator re-executes JSON Logic) | P2 | 32h |

### Production Gaps

- **In-memory storage** for blueprints, templates, documents, encryption operations -- data lost on restart. Needs EF Core + PostgreSQL migration.
- Stale README (claims 37 integration tests, actual is 43)
- Status list persistence is in-memory

---

## 2. Register Service

**Purpose:** Distributed ledger -- immutable transaction storage with blockchain-style chain integrity, OData queries, governance, ZK proofs.

**Completion:** 100% MVD

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Register.Service.Tests | 219 | 2 | 0 | 221 |
| Register.Service.IntegrationTests | 9 | 0 | 0 | 9 |
| Register.Core.Tests | 271 | 0 | 0 | 271 |
| Register.Models.Tests | 124 | 0 | 0 | 124 |
| Register.Storage.MongoDB.Tests | 46 | 1 | 0 | 47 |
| **Total** | **669** | **3** | **0** | **672** |

### Test Failures

| Test | Issue |
|------|-------|
| `SystemRegisterBootstrapTests.ExecuteAsync_WhenRegisterMissing_InitiatesAndFinalizes` | Mock expects `FinalizeAsync` but only `InitiateAsync` is called. Two-phase creation flow was refactored; test not updated. |
| `QueryApiTests.GetTransactionsByWallet_WithoutRegisterId_ShouldReturn400BadRequest` | Expects 400 but gets 200. Cross-register wallet query (PR #49) changed behavior to allow querying across all registers. |
| `MongoRegisterRepositoryIntegrationTests.GetRegistersAsync_ReturnsAllRegisters` | Environment-dependent -- requires running MongoDB with test data. |

### Endpoints (50 REST + 1 SignalR hub + gRPC)

- **Register Management** (`/api/registers`) -- 5 endpoints: list, get, update, delete, count
- **Register Creation** -- 2 endpoints: two-phase initiate/finalize with genesis
- **Transactions** -- 10 endpoints: CRUD, query by sender/recipient/wallet/blueprint/instance, cross-register wallet query
- **Dockets** -- 4 endpoints: list, get, latest, create/seal
- **Governance** -- 2 endpoints: roster, history
- **Published Participants** -- 6 endpoints: list, get by ID/wallet, resolve public key, publish, revoke
- **ZK Proofs** -- 2 endpoints: generate inclusion proof, verify
- **Crypto Policy** -- 2 endpoints: get policy, validate algorithm
- **Register Policy** -- 5 endpoints: effective policy, history, propose update, approved/operational validators
- **System Register** -- 6 endpoints: status, initialize, publish blueprint, list/get blueprints
- **Recovery** -- 1 endpoint: sync health status
- **SignalR Hub** at `/hubs/register`, gRPC `RegisterAddressGrpcService`, OData at `/odata/`

### Key Functionality

1. Two-phase register creation with genesis docket
2. Immutable transaction storage (MongoDB)
3. Blockchain-style docket chaining with Merkle roots
4. Cross-register wallet transaction queries
5. Governance roster reconstruction from control transactions
6. Published participant identity records
7. ZK inclusion proofs (generate/verify)
8. Per-register crypto policy enforcement
9. System register for platform-wide blueprint catalog
10. SignalR real-time register event notifications
11. Recovery service for docket gap detection

### Deferred Work

No Register-specific tasks remain in MASTER-TASKS.md. Cross-cutting items that affect Register:

| ID | Task | Priority |
|----|------|----------|
| TRUST-001 | Verifiable calculations | P2 |
| TRUST-002 | Validator-enforced disclosure | P2 |
| TRUST-003 | Transaction receipts (signed finality proofs) | P2 |
| TRUST-004 | Merkle inclusion proofs (lightweight offline) | P2 |
| TRUST-005 | Revocation/amendment model | P2 |

### Production Gaps

- Operational validator endpoint is a stub (returns empty)
- No production MongoDB tuning
- No backup/disaster recovery procedures
- 3 test failures from refactoring drift

---

## 3. Validator Service

**Purpose:** Transaction validation, consensus, chain integrity verification, docket building.

**Completion:** 100% MVD

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Validator.Core.Tests | 200 | 0 | 0 | 200 |
| Validator.Service.Tests | 891 | 0 | 3 | 894 |
| Validator.Service.IntegrationTests | 120 | 0 | 0 | 120 |
| **Total** | **1,211** | **0** | **3** | **1,214** |

### Test Failures

None. All 1,211 tests pass (fixed in this session -- PR #53).

### Endpoints (8+ REST + gRPC)

- **Validation** (`/api/v1/transactions`) -- 2 endpoints: validate transaction, mempool stats
- **Admin** (`/api/admin`) -- 4 endpoints: start/stop validator, status, manual pipeline
- **Registration** (`/api/validators`) -- 5 endpoints: register, list, get, count, refresh
- **Threshold** (`/api/v1/validators/threshold`) -- BLS threshold signing
- **gRPC**: RequestVote, ValidateDocket, GetHealthStatus

### Key Functionality

1. Enclave-safe core validation library (stateless, no I/O)
2. Memory pool with per-register isolation and priority queues
3. Docket building with Merkle trees and hybrid triggers
4. Distributed consensus (quorum-based, parallel gRPC voting)
5. Governance rights enforcement (roster reconstruction)
6. Control docket processing (policy updates, validator management)
7. Blueprint version resolution with caching
8. Bad actor detection and tracking

### Deferred Work (P3, ~146h total)

| ID | Task | Effort |
|----|------|--------|
| P2P-004 | BLS12-381 threshold coordination | 24h |
| P2P-005 | Fork detection & chain recovery | 16h |
| P2P-006 | Decentralized consensus / leader election | 32h |
| P2P-007 | Enclave support (SGX/SEV) | 24h |
| P2P-008 | Multi-validator coordination | 20h |

### Production Gaps

- Single-validator mode only (distributed consensus deferred)
- No fork detection/recovery
- No enclave integration (core is ready)
- One TODO: ControlBlueprintVersionResolver historical state replay

---

## 4. Tenant Service

**Purpose:** Multi-tenant authentication, authorization, organization management, participant identity registry.

**Completion:** 95%

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Tenant.Service.Tests | 572 | 5 | 1 | 578 |
| Tenant.Service.IntegrationTests | 81 | 0 | 0 | 81 |
| Tenant.Models.Tests | 119 | 0 | 0 | 119 |
| **Total** | **772** | **5** | **1** | **778** |

### Test Failures

All 5 failures in `IdpConfigurationEndpointTests`:

| Test | Issue |
|------|-------|
| `PutIdpConfiguration_ValidRequest_Returns200` | Test expects `IsEnabled = false` default, service returns `true`. |
| `PostTest_NoConfig_Returns404` | Service now returns 200 with defaults instead of 404. |
| `PostToggle_NoConfig_Returns404` | Same as above. |
| `DeleteIdpConfiguration_NoConfig_Returns404` | Same as above. |
| `GetIdpConfiguration_NoConfig_Returns404` | Same as above. |

Root cause: `IdpConfigurationService` was changed to return a default config (200) rather than 404 for missing configs. Tests were not updated to match.

### Endpoints (20 endpoint groups)

- **Bootstrap** -- Initial system setup
- **Organizations** -- CRUD, user management, lifecycle
- **Participants** -- Identity registry bridging users to wallets
- **Auth** -- Login, register, logout, token ops, 2FA
- **Passkeys** -- FIDO2/WebAuthn registration
- **Public Auth** -- Passkey signup/sign-in, social login
- **Service Auth** -- Service-to-service client_credentials
- **TOTP** -- Setup, verify, validate, disable
- **IDP Configuration** -- CRUD, discover, test, toggle
- **OIDC** -- Initiate, callback, profile completion
- **Org Settings** -- Self-registration, audit retention
- **Domain Restrictions** -- Email domain allowlist
- **Audit** -- Query and retention config
- **Invitations** -- Email invitations with role assignment
- **Dashboard** -- Admin KPIs
- **Custom Domains** -- CNAME management
- **Internal** -- Domain resolution for API Gateway
- **Push Subscriptions** -- Push notification management
- **Events** -- Activity event log
- **Razor Pages** -- Server-rendered auth UI (`/auth/*`)

### Key Functionality

1. Multi-tenant organization management with subdomain routing
2. Local auth with NIST SP 800-63B password policy and progressive lockout
3. OIDC federation (Entra, Google, Okta, Apple, Cognito presets)
4. TOTP 2FA with backup codes
5. FIDO2/WebAuthn passkeys for org and public auth
6. Social login (Google, Microsoft, GitHub, Apple)
7. Server-rendered Razor auth pages (no WASM download for unauthenticated users)
8. JWT issuance (RS256) with refresh and revocation (Redis-backed)
9. Service-to-service auth via client_credentials
10. Email invitations with role assignment
11. Participant identity registry with wallet link verification
12. 5-role authorization model (SystemAdmin, Administrator, Designer, Auditor, Member)

### Deferred Work

| ID | Task | Priority |
|----|------|----------|
| AUTH-002 | Refresh token rotation | P2 |
| AUTH-003 | Cross-tab token sync | P3 |
| AUTH-004 | Session expiry warning UI | P3 |
| AUTH-006 | Production SMTP (replace stub) | P1 |
| AUTH-007 | Breach password list (HIBP API) | P2 |
| AUTH-008 | Custom domain DNS automation | P2 |
| AUTH-009 | Social login real credential testing | P2 |
| AUTH-010 | OIDC token exchange load testing | P2 |

### Production Gaps

- **SMTP is a stub** (P1) -- emails don't send
- No refresh token rotation (replay risk)
- No HIBP breach list integration
- Custom domain DNS verification is manual
- Social login untested with real OAuth credentials
- 5 test failures from IDP config behavior drift

---

## 5. Wallet Service

**Purpose:** HD wallet management, multi-algorithm cryptography, transaction signing, encryption, verifiable credentials, OID4VP presentations.

**Completion:** 95%

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Wallet.Service.Tests | 388 | 0 | 0 | 388 |
| Wallet.Core.Tests | 77 | 0 | 0 | 77 |
| Cryptography.Tests | 296 | 1 | 0 | 297 |
| Wallet.Service.IntegrationTests | 0 | 29 | 0 | 29 |
| **Total** | **761** | **30** | **0** | **791** |

### Test Failures

| Category | Count | Issue |
|----------|-------|-------|
| Integration tests | 29 | All fail with `No endpoints specified. Ensure a valid connection string was provided in 'ConnectionStrings:redis'`. WebApplicationFactory missing Redis mock -- same class of bug we fixed for Validator. |
| Crypto performance | 1 | `SC006_SlhDsa128sOperations_WithinAcceptableLimits` -- SLH-DSA post-quantum benchmark exceeds time limit under CPU contention. Passes when run individually. |

### Endpoints (35 REST + 2 gRPC services)

- **Wallet Management** (`/api/v1/wallets`) -- 10 endpoints: create, recover, list, get, update, delete, sign, encrypt, decrypt, system wallet
- **HD Addresses** -- register, list, get, update, mark-used, accounts, gap-status
- **Delegation** (`/api/v1/wallets/{addr}/access`) -- 4 endpoints: grant, list, revoke, check
- **Credentials** (`/api/v1/wallets/{addr}/credentials`) -- 8 endpoints: list, get, match, delete, export, store, update status, issue SD-JWT VC
- **Presentations** (`/api/v1/presentations`) -- 5 endpoints: create request, get, submit, deny, get result (OID4VP)
- **gRPC**: `WalletGrpcService` (wallet ops), `WalletNotificationGrpcService` (inbound transaction notifications)

### Key Functionality

1. HD wallet lifecycle (BIP39/BIP32/BIP44)
2. Multi-algorithm crypto: ED25519, NISTP256, RSA-4096, ML-DSA-65, ML-KEM-768, SLH-DSA-128s, BLS12-381
3. Transaction signing and payload encryption/decryption
4. Access delegation (Owner/ReadWrite/ReadOnly)
5. SD-JWT VC issuance, storage, matching, export
6. OID4VP presentation flow with selective disclosure
7. Notification pipeline (gRPC inbound, Redis pub/sub, rate limiting, digest batching)
8. EF Core + PostgreSQL persistence

### Deferred Work

| ID | Task | Priority | Effort |
|----|------|----------|--------|
| SEC-002 | Azure Key Vault / HSM integration | P0 | 16h |
| GAP-001 | CLI wallet delegation command tests | P2 | 4h |

Also deferred: AWS KMS, hardware wallet (Ledger/Trezor), audit logging, backup/restore.

### Production Gaps

- **Key storage is critical** (P0) -- private keys use `LocalEncryptionProvider`, lost on restart. No HSM integration.
- All 29 integration tests broken (Redis mock missing in test fixture)
- No audit logging for cryptographic operations
- No wallet backup/restore mechanism
- No migration path from LocalEncryptionProvider to HSM

---

## 6. Peer Service

**Purpose:** P2P networking layer -- hub-spoke topology, register replication, heartbeat monitoring, gossip protocol, transaction distribution.

**Completion:** 95%

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| Peer.Service.Tests | 572 | 0 | 0 | 572 |
| Peer.Service.Integration.Tests | 35 | 0 | 6 | 41 |
| PeerRouter.Tests | 84 | 0 | 0 | 84 |
| **Total** | **691** | **0** | **6** | **697** |

### Test Failures

None. All 691 tests pass. 6 skipped integration tests require external infrastructure.

### Endpoints (19 REST + 5 gRPC services)

- **Peers** -- 8 endpoints: list, get, stats, health, quality, connected count, ban, unban, reset
- **Registers** -- 8 endpoints: subscriptions, cache, available, advertise, bulk-advertise, subscribe, unsubscribe, purge cache
- **Health** -- 1 endpoint with metrics
- **Info** -- 1 endpoint: service info
- **gRPC**: PeerDiscovery, PeerHeartbeat, RegisterSync, TransactionDistribution, DocketSync

### Key Functionality

1. Hub-spoke topology with priority-based failover (n0 -> n1 -> n2)
2. System register replication (full + incremental gRPC streaming)
3. Heartbeat monitoring (30s interval, failover after 2 missed)
4. Push notifications for blueprint publication events
5. Isolated mode (graceful degradation when hub unreachable)
6. Gossip protocol engine for transaction distribution
7. Register advertisement with Redis persistence
8. Circuit breaking per-peer with configurable threshold/cooldown
9. PostgreSQL transaction queue
10. Connection quality tracking

### Deferred Work (P3)

| ID | Task | Effort |
|----|------|--------|
| P2P-001 | Transaction processing loop | 12h |
| P2P-002 | Transaction distribution via gossip | 10h |
| P2P-003 | gRPC streaming communication | 8h |
| P2P-004 | BLS12-381 threshold coordination | 24h |

Also deferred: mTLS, certificate rotation, multi-node E2E validation.

### Production Gaps

- No mTLS for gRPC communication
- Transaction gossip path scaffolded but not executing real propagation
- Several monitoring endpoints are unauthenticated
- Bulk advertise endpoint lacks auth
- No automated TLS certificate rotation

---

## 7. API Gateway

**Purpose:** YARP reverse proxy -- routes all external traffic to backend services.

**Completion:** 100% MVD

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| ApiGateway.Tests | 27 | 0 | 0 | 27 |

### Endpoints

- ~80 YARP route definitions across 8 clusters (admin, blueprint, register, wallet, tenant, validator, peer, aspire-dashboard)
- Gateway-owned: `/api/health` (aggregated), `/api/stats`, `/api/dashboard`, `/api/alerts`, `/api/docs`, `/api/client/*`, `/gateway`
- `UrlResolutionMiddleware` for multi-tenant routing (subdomain/path/custom domain)
- OpenAPI aggregation from all backend services

### Production Gaps

- CORS currently permissive for development
- HTTPS enforcement needed for Docker deployment
- Rate limiting tuning for production load

---

## 8. Shared Libraries & Cross-Cutting

### Test Coverage

| Project | Pass | Fail | Skip | Total |
|---------|------|------|------|-------|
| ServiceDefaults.Tests | 46 | 0 | 0 | 46 |
| ServiceClients.Tests | 109 | 0 | 0 | 109 |
| Auth.IntegrationTests | 39 | 0 | 0 | 39 |
| **Total** | **194** | **0** | **0** | **194** |

### UI Tests

| Project | Status | Notes |
|---------|--------|-------|
| UI.Core.Tests | **BUILD FAILURE** | `SystemRegisterServiceTests.cs:56` assigns to read-only computed property `IsInitialized`. Fix: set `Status = "initialized"` instead. |
| UI.E2E.Tests | Builds OK | ~179 test methods across 23 Docker test files. Requires Docker to run. |

---

## Cross-Cutting Deferred Work

### P0 -- Release Blockers

| ID | Task | Impact |
|----|------|--------|
| SEC-001 | HTTPS enforcement (Docker) | All services |
| SEC-002 | Azure Key Vault for Wallet key storage | Wallet Service |
| SEC-004 | Security audit (OWASP Top 10) | All services |
| SEC-005 | Secret management review | All services |
| SEC-006 | CORS policy hardening | API Gateway |

### P1 -- Important

| ID | Task | Impact |
|----|------|--------|
| AUTH-006 | Production SMTP | Tenant Service |
| OPS-001 | Production deployment documentation | All services |
| OPS-003 | Monitoring and alerting dashboards | All services |
| OPS-004 | Backup and disaster recovery | Register, Tenant, Wallet |
| OPS-005 | Database migration release process | Tenant, Wallet, Peer |

### P2 -- Enhancement

| ID | Task | Impact |
|----|------|--------|
| AUTH-002 | Refresh token rotation | Tenant Service |
| AUTH-007 | Breach password list (HIBP) | Tenant Service |
| GAP-008 | Wire-format identity encoding | Blueprint, Validator |
| TRUST-001 | Verifiable calculations | Blueprint, Validator, Register |
| TRUST-002-005 | Trust model enhancements | Register |
| OPS-006-009 | Operational tooling | All services |

### P3 -- Post-Release

| ID | Task | Effort | Impact |
|----|------|--------|--------|
| P2P-001 through P2P-008 | Distributed consensus & P2P | ~146h | Validator, Peer |
| AUTH-003, AUTH-004 | Token sync, session UI | -- | Tenant, UI |

---

## Test Health Summary

### Broken Tests Requiring Fixes

| Priority | Service | Count | Root Cause | Fix Effort |
|----------|---------|-------|------------|------------|
| **High** | Wallet Integration | 29 | Missing Redis mock in WebApplicationFactory | 1h (same pattern as Validator fix) |
| **Medium** | Tenant Unit | 5 | IDP config behavior drift (200 vs 404, IsEnabled default) | 1h |
| **Medium** | Register Unit | 2 | Refactoring drift (bootstrap flow, cross-register query) | 1h |
| **Medium** | UI Core | BUILD FAIL | Read-only property assignment in test | 15min |
| **Low** | Blueprint Integration | 1 | Schema search provider ordering | 15min |
| **Low** | Register MongoDB | 1 | Requires running MongoDB | Environment |
| **Low** | Cryptography | 1 | SLH-DSA benchmark timing (CPU contention) | Environment |

**Total broken: 39 tests + 1 build failure**
**Estimated fix effort: ~4 hours**

### Green Test Suites (No Action Needed)

- Validator (all 3 projects) -- 1,211 pass
- Peer (all 3 projects) -- 691 pass
- API Gateway -- 27 pass
- ServiceDefaults -- 46 pass
- ServiceClients -- 109 pass
- Auth Integration -- 39 pass
- Blueprint (6 of 7 projects) -- 1,342 pass
- Register (3 of 5 projects) -- 404 pass
- Tenant (2 of 3 projects) -- 200 pass
- Wallet (2 of 4 projects) -- 465 pass
