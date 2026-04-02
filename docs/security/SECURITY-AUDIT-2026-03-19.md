# Sorcha Platform Security Audit Report

**Date:** 2026-03-19
**Scope:** Full-stack code, infrastructure, blueprint execution, cryptography, consensus
**Platform Status:** 100% MVD Complete | Production Readiness: 30%
**Auditor:** Claude Opus 4.6 (automated static analysis)

---

## Executive Summary

The Sorcha decentralised register platform demonstrates **strong foundational security** in cryptography, input validation, and authentication design. However, **critical gaps in consensus integrity, replay protection, and production configuration** must be addressed before any production deployment.

**Finding Summary:**

| Severity | Count | Key Areas |
|----------|-------|-----------|
| CRITICAL | 7 | Consensus votes unsigned, replay protection missing, secrets exposed, Redis unauthenticated |
| HIGH | 14 | Blueprint cache poisoning, participant auth bypass, CORS wide open, docket ordering races |
| MEDIUM | 16 | State poisoning, cycle detection, cache invalidation, fork detection logic |
| LOW | 4 | Logging PII, config validation, documentation mismatches |

---

## Table of Contents

1. [Authentication & Authorization](#1-authentication--authorization)
2. [Cryptography](#2-cryptography)
3. [Blueprint Execution Engine](#3-blueprint-execution-engine)
4. [Consensus & Data Integrity](#4-consensus--data-integrity)
5. [Infrastructure & Configuration](#5-infrastructure--configuration)
6. [Production Readiness Checklist](#6-production-readiness-checklist)
7. [Remediation Roadmap](#7-remediation-roadmap)

---

## 1. Authentication & Authorization

### 1.1 [MEDIUM] Fail-Open Token Revocation

**File:** `src/Services/Sorcha.Tenant.Service/Services/TokenRevocationService.cs:103-106`

When Redis is unavailable, token revocation checks silently pass ("fail open"). A revoked token continues to be accepted if Redis is down.

**Recommendation:** Make fail-open configurable. Default to fail-closed in production. Implement circuit breaker pattern.

### 1.2 [MEDIUM] Bootstrap Endpoint Lacks Cryptographic Protection

**File:** `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs:29`

The `AllowAnonymous` bootstrap endpoint relies on a one-shot database guard. If the guard flag is cleared (DB corruption, restore from backup), the platform can be re-bootstrapped by any attacker.

**Recommendation:** Add optional `BootstrapSecret` environment variable validation. Require a one-time token for bootstrap in production.

### 1.3 [LOW] Social Login Redirect URI Validation

**File:** `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs:302`

Social login doesn't explicitly validate `redirect_uri` against a whitelist. PKCE mitigates this, but explicit validation would add defense-in-depth.

### 1.4 [LOW] Password Validation Documentation Mismatch

**File:** `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs:38-41`

Bootstrap endpoint description mentions "NIST policy and HIBP breach list" validation not visible in handler code. Either implement or update documentation.

### 1.5 Confirmed Strengths

- JWT generation with unique JTI, HMAC-SHA256 signing, proper validation parameters
- Redis-backed token revocation with TTL matching token expiry
- Tenant isolation enforced at query level (org-scoped filters)
- RBAC policies consistently applied via YARP middleware (46 routes reviewed)
- OAuth state parameter with PKCE (RFC 7636) correctly implemented
- BCrypt password hashing with built-in salt
- Client-side tokens encrypted before localStorage storage
- Switch-org validates membership + org status + user identity

---

## 2. Cryptography

### 2.1 [MEDIUM] Mnemonic Phrases Returned in HTTP Responses

**File:** `src/Services/Sorcha.Wallet.Service/Endpoints/WalletEndpoints.cs:291-303`

BIP39 mnemonic words are returned as plain strings in the wallet creation response. If HTTPS is compromised or responses are logged/cached, mnemonics are exposed.

**Recommendation:** Implement client-side key derivation, or ensure response caching headers prevent storage. Consider one-time-view pattern.

### 2.2 [MEDIUM] P-256 Keys Not Deterministically Derived from Seed

**File:** `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs:551-575`

NIST P-256 key generation ignores the seed parameter and generates random keys. Users cannot recover P-256 wallets from their mnemonic — only ED25519 wallets support deterministic recovery.

**Recommendation:** Document this limitation prominently, or implement seed-based P-256 derivation.

### 2.3 [LOW] Field-Level KEM/KMS Integration Unclear

Per-recipient key derivation or key exchange for field-level encryption architecture is not fully visible. Disclosure groups use SHA256-based grouping, but the KEM integration path for field-level keys needs documentation.

### 2.4 Confirmed Strengths

- CSPRNG (`RandomNumberGenerator.GetBytes()`) used for all entropy — no weak RNG detected
- No MD5, SHA1, or DES — deprecated AES-CBC explicitly throws `NotSupportedException`
- Authenticated encryption only: AES-256-GCM, ChaCha20-Poly1305, XChaCha20-Poly1305
- Fresh random nonces per encryption operation — no nonce reuse
- Constant-time hash comparison via `CryptographicOperations.FixedTimeEquals()`
- Key material zeroization after use (shared secrets, symmetric keys)
- PBKDF2-HMAC-SHA512 with 2048 iterations for BIP39 seed derivation
- Password-based key export uses PBKDF2 (100K iterations) + AES-256-GCM
- PQC algorithms (ML-DSA-65, SLH-DSA-128s, ML-KEM-768) use BouncyCastle SecureRandom
- ECIES implementation uses ephemeral key pairs with HKDF-SHA256
- Transaction signature contract `SHA256("{TxId}:{PayloadHash}")` is strong against reuse/tampering

---

## 3. Blueprint Execution Engine

### 3.1 [HIGH] Participant Role Not Validated in Action Execution

**File:** `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:152-156`

When executing an action, the code validates the action is "current" but does **not** verify the sender wallet belongs to the action's designated participant role. Any wallet linked to any participant in the org can execute any action.

**Attack:** User A (role: "reviewer") could execute actions designated for User B (role: "approver") if they control any wallet in the system.

**Recommendation:** Add explicit validation that the sender wallet maps to the action's designated sender participant role.

### 3.2 [HIGH] Exception Swallowing in ActionProcessor

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs:178-186`

All exceptions (including `NullReferenceException`, `ObjectDisposedException`) are caught and converted to generic error messages. No `ILogger` is available. Malicious blueprints could trigger silent failures that hide security violations.

**Recommendation:** Add `ILogger` to `ActionProcessor`. Log full exception details at Error level.

### 3.3 [HIGH] Blueprint Cache Staleness in Validator

**Reference:** Validator blueprint cache (L1: 5min in-memory, L2: 15min Redis) has no invalidation mechanism. If a blueprint is updated after caching, the validator validates transactions against stale rules.

**Attack:** Publish blueprint V1 with permissive rules → transactions validated → update to V2 with stricter rules → validator still uses cached V1 for up to 15 minutes.

**Recommendation:** Include blueprint version hash in transaction metadata. Verify cached blueprint hash matches. Add explicit invalidation endpoint.

### 3.4 [MEDIUM-HIGH] SignalR Service Token Bypasses Wallet Validation

**File:** `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs:98-105`

Service tokens bypass wallet ownership validation entirely. If a service JWT is compromised, an attacker can subscribe to any wallet's notifications and monitor all workflow activity.

**Recommendation:** Require `org_id` claim in service token. Add audit logging for service token subscriptions.

### 3.5 [MEDIUM-HIGH] JSON Logic Complexity Limits Only at Validation Time

**File:** `src/Core/Sorcha.Blueprint.Engine/Validation/JsonLogicValidator.cs:23-24`

Complexity limits (MaxDepth=10, MaxNodeCount=100) are checked during blueprint publishing only, not during execution. Complex expressions could cause DoS at runtime.

**Recommendation:** Add execution-time complexity guards with iteration counting and abort threshold.

### 3.6 [MEDIUM] Instance State Poisoning via AccumulatedData

**File:** `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs:501-506`

Accumulated data is stored on the workflow instance without field validation. A malicious participant could inject arbitrary fields that affect subsequent routing/calculation decisions.

**Recommendation:** Whitelist fields allowed in `AccumulatedData`. Only store fields from original submission or explicit calculations.

### 3.7 [MEDIUM] Disclosure Filter Escaped Pointer Bypass

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/DisclosureProcessor.cs:249-256`

JSON Pointer escape decoding (`~0` → `~`, `~1` → `/`) doesn't validate against path traversal after decoding. Double-encoding could bypass disclosure scope checks.

**Recommendation:** Validate field names against whitelist AFTER decoding. Reject pointers containing `..` after decoding.

### 3.8 [MEDIUM] No Cycle Detection in Blueprint Routing

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs`

No cycle detection exists. A malicious blueprint could define Action 0 → Action 1 → Action 0 causing infinite workflow loops.

**Recommendation:** Add topological sort validation during publishing. Implement max-execution-depth per instance.

### 3.9 [MEDIUM] Routing Bypass via Impossible Conditions

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs:47-99`

If no routes match (all conditions false and no default route), the routing engine returns `RoutingResult.Complete()`, terminating the workflow prematurely.

**Recommendation:** Require explicit default routes or validate all actions have a path forward during publishing.

### 3.10 [MEDIUM] Calculation Side Effects Enable Information Leakage

**File:** `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs:86-119`

Calculations execute sequentially with full read access to prior calculated fields. A malicious blueprint could extract sensitive fields via calculations, then disclose them to unauthorized participants.

**Recommendation:** Validate calculations against allowed field whitelists.

---

## 4. Consensus & Data Integrity

### 4.1 [CRITICAL] Consensus Votes Not Cryptographically Verified

**Files:**
- `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs:111-158`
- `src/Services/Sorcha.Validator.Service/Services/SignatureCollector.cs:50-200`

Incoming consensus votes are counted based on `response.Approved && response.Signature != null` without verifying the signature belongs to the responding validator. A malicious peer could impersonate validators and cast fake approval votes.

**Attack:** Impersonate N validators → push consensus over threshold → corrupt dockets.

**Recommendation:** Cryptographically verify ALL incoming votes. Each vote must be signed by the responding validator's registered key.

### 4.2 [CRITICAL] Transaction Replay Protection Missing

**Files:**
- `src/Services/Sorcha.Validator.Service/Models/Transaction.cs`
- `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:659-791`

Transactions lack per-sender sequence numbers or nonces. `PreviousTransactionId` is used for blueprint routing, not replay protection. The same transaction could theoretically be resubmitted.

**Recommendation:** Implement per-sender monotonic sequence numbers bound to sender wallet + register.

### 4.3 [HIGH] Docket Ordering Race Condition

**Files:**
- `src/Services/Sorcha.Validator.Service/Services/DocketBuilder.cs:106-107`
- `src/Core/Sorcha.Register.Core/Storage/IRegisterRepository.cs:40-41`

DocketNumber is computed as `latestDocket.DocketNumber + 1` without atomic compare-and-swap. Two validators could build docket N simultaneously, causing duplicate or conflicting dockets.

**Recommendation:** Enforce unique compound index `(RegisterId, DocketNumber)` in MongoDB. Use atomic operations for docket insertion.

### 4.4 [HIGH] Transaction Pool Lacks Priority Ordering

**File:** `src/Services/Sorcha.Validator.Service/Services/TransactionPoolPoller.cs:84-92`

Unverified pool uses FIFO (`ListLeftPush`/`ListRightPop`). Attacker can flood with low-priority junk to delay legitimate high-priority transactions.

**Recommendation:** Use Redis sorted set (ZSET) keyed by `(priority, createdAt)`.

### 4.5 [HIGH] Validator Registry Missing Approval Workflow

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs:99-128`

No visible governance for validator admission. If registration is open, an attacker can register a rogue validator and participate in consensus.

**Recommendation:** Implement approval workflow: pending → approved (quorum vote) → active. Store in persistent layer.

### 4.6 [HIGH] Single-Validator Auto-Approve Config Risk

**File:** `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs:78-96`

`SingleValidatorAutoApprove = true` auto-approves dockets when no peers found. If accidentally enabled in production and a network partition occurs, consensus is bypassed.

**Recommendation:** Remove from production config. Gate behind explicit deployment mode flag.

### 4.7 [HIGH] OData Query Field Name Injection

**File:** `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/ODataQueryService.cs:57-100`

`filter.Field` is used directly in OData query string without validation. Only `filter.Value` is escaped. An attacker could inject malicious syntax via field names.

**Recommendation:** Whitelist allowed field names. Use typed OData query builders.

### 4.8 [MEDIUM-HIGH] Participant Resolution Fails Open

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:929-960`

If Register Service is unreachable during participant resolution, validation logs "skipping sender authorization" and continues. Invalid transactions with unresolved participants get enqueued.

**Recommendation:** Cache participant records with TTL. Implement timeout + circuit breaker. Fail hard when participant cannot be resolved.

### 4.9 [MEDIUM-HIGH] Redis Transaction Atomicity

**File:** `src/Services/Sorcha.Validator.Service/Services/TransactionPoolPoller.cs:137-160`

Multi-step Redis operations (StringSet, ListLeftPush, SortedSetAdd) lack verification of complete success. Partial failures cause queue/data inconsistency.

**Recommendation:** Verify `ExecuteAsync()` result. Use Lua scripts for atomic multi-step operations.

### 4.10 [MEDIUM] Genesis Docket Skips All Validation

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:356-365`

Genesis/control transactions skip signature, schema, and conformance validation entirely. If system wallet key is compromised, attacker can inject arbitrary genesis dockets.

**Recommendation:** Require m-of-n quorum approval for genesis docket creation.

### 4.11 [MEDIUM] Fork Detection Null MetaData Bug

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:700-718`

`previousTx.MetaData?.TransactionType == TransactionType.Control` returns false when MetaData is null, incorrectly triggering fork detection for legitimate control transaction children.

**Recommendation:** Explicitly set `TransactionType = Control` in genesis/control creation. Add null MetaData handling.

### 4.12 [MEDIUM] Peer Replication No Checksum Verification

**File:** `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs:128-170`

Checksums from peer responses are stored as-is without local verification. Rogue peers can inject corrupted data with valid-looking checksums.

**Recommendation:** Compute checksum locally from received data. Compare against peer-provided checksum.

### 4.13 [MEDIUM] Transaction Expiry Race Between Pools

**Files:**
- `src/Services/Sorcha.Validator.Service/Services/TransactionPoolPoller.cs:433-483`
- `src/Services/Sorcha.Validator.Service/Services/VerifiedTransactionQueue.cs:467-482`

Unverified and verified pool cleanups run independently with no coordination. A transaction moving between pools during cleanup can be missed or double-counted.

**Recommendation:** Use centralized TTL management. Mark expired status in persistent layer.

### 4.14 [MEDIUM] Memory Pool No Payload Size Limits

**File:** `src/Services/Sorcha.Validator.Service/Services/TransactionPoolPoller.cs:95-172`

No per-transaction size limit. Attacker can submit megabyte-sized payloads to exhaust Redis memory.

**Recommendation:** Add `MaxTransactionPayloadSize` config (e.g., 1MB). Reject oversized with HTTP 413.

### 4.15 [MEDIUM] Validator Cache No Invalidation Events

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs:34-35, 107-120`

L1 local cache returns immediately without checking Redis for updates. Revoked validators remain in cache until TTL expires.

**Recommendation:** Use Redis pub/sub to broadcast revocation events for immediate cache invalidation.

### 4.16 [LOW] Consensus Threshold Uses Strict Greater-Than

**File:** `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs:149`

Uses `>` not `>=`. At exactly 50% approval, consensus fails. Documented semantics needed.

### 4.17 [LOW] Public Key Fragment Logged

**File:** `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs:562`

Truncated public key hex logged in error messages. Could enable correlation attacks if logs are aggregated.

---

## 5. Infrastructure & Configuration

### 5.1 [CRITICAL] Anthropic API Key Committed to Repository

**File:** `.env:82`

A real `sk-ant-api03-*` API key is present in the `.env` file. Even though `.env` is in `.gitignore`, it may exist in git history.

**Action Required:** Revoke immediately from Anthropic console. Generate new key. Store in vault. Scan git history with `truffleHog` or `git-secrets`.

### 5.2 [CRITICAL] Redis Exposed Without Authentication

**File:** `docker-compose.yml:30-31, .env:63`

Redis is published to host on port 16379 with no password. Any local application can read/write all cache data, poison blueprints, manipulate transaction pools, and hijack SignalR sessions.

**Recommendation:** Set `requirepass` in Redis config. Use Azure Cache for Redis with TLS in production.

### 5.3 [CRITICAL] gRPC Peer Communication Over Cleartext

**File:** `docker-compose.yml:346`

`PeerService__EnableTls: "false"` — all peer-to-peer communication is unencrypted. Register sync, consensus votes, and transaction gossip are visible on the network.

**Recommendation:** Set `EnableTls: "true"` and provision TLS certificates for production.

### 5.4 [HIGH] JWT Signing Key Hardcoded as Default

**File:** `docker-compose.yml:19`

Default JWT key is `sorcha-docker-dev-key-2025-min-256-bits-secure` (base64 encoded). All default installations share the same signing key. Any token from one installation is valid on another.

**Recommendation:** Remove default value. Require explicit `JWT_SIGNING_KEY` environment variable. Use Azure Key Vault.

### 5.5 [HIGH] Service-to-Service Secrets Hardcoded

**File:** `docker-compose.yml:132,176,242,288,339,394`

Six service client secrets (`blueprint-service-secret`, `wallet-service-secret`, etc.) are hardcoded in the compose file.

**Recommendation:** Use Azure Managed Identity or vault-sourced secrets.

### 5.6 [HIGH] Database Credentials in Compose File

**File:** `docker-compose.yml:50,72,121,174,221,224,282,336`

PostgreSQL and MongoDB credentials embedded in environment variables and connection strings.

**Recommendation:** Use Docker secrets or vault integration for production.

### 5.7 [HIGH] CORS AllowAnyOrigin() Across All Services

**File:** `src/Common/Sorcha.ServiceDefaults/CorsExtensions.cs:21-28`

All services use `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()`. Comment says "Production CORS handled at API Gateway" but this violates defense-in-depth — if gateway is bypassed, CORS is wide open.

**Recommendation:** Configure per-service CORS with specific origins. Add CORS enforcement at YARP level.

### 5.8 [HIGH] Certificate Password Hardcoded

**File:** `docker-compose.yml:485`

`SorchaDev2025` certificate password visible in compose file.

### 5.9 [MEDIUM] gRPC Reflection Enabled

**File:** `src/Services/Sorcha.Peer.Service/Program.cs:99`

gRPC reflection exposes all service definitions to any client. Disable in production or require authentication.

### 5.10 [MEDIUM] Anonymous Peers Allowed in P2P Network

**File:** `src/Services/Sorcha.Peer.Service/GrpcServices/PeerAuthInterceptor.cs:102-108`

Unauthenticated peers are allowed through with "lower trust" but no actual trust-based filtering is enforced downstream.

**Recommendation:** Implement actual trust-level enforcement. Require authentication for transaction submission and register sync.

### 5.11 [MEDIUM] Aspire Dashboard Unauthenticated

**File:** `docker-compose.yml`

`DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true` — dashboard accessible without authentication.

### 5.12 Confirmed Strengths

- Input validation middleware: SQL injection, XSS, path traversal, command injection, LDAP injection detection
- Rate limiting on auth endpoints (5 attempts/minute)
- Non-root containers (UID 1654) with multi-stage builds
- Serilog structured logging without body logging
- CSP, X-Frame-Options, HSTS headers configured
- EF Core parameterized queries (no SQL injection)
- MongoDB driver parameterized operations (no NoSQL injection)
- All NuGet packages current stable versions
- Central package management via `Directory.Packages.props`
- `Path.GetFullPath()` used for file operations (no path traversal)

---

## 6. Production Readiness Checklist

| Control | Status | Notes |
|---------|--------|-------|
| JWT key management | :x: | Hardcoded default key |
| Service-to-service auth | :x: | Hardcoded secrets |
| Redis authentication | :x: | No password |
| Redis encryption (TLS) | :x: | Plaintext |
| gRPC TLS | :x: | Disabled |
| CORS restriction | :x: | AllowAnyOrigin |
| Secrets vault integration | :x: | No Azure Key Vault |
| PostgreSQL per-service RBAC | :x: | Single shared user |
| MongoDB per-service RBAC | :x: | Single root user |
| Consensus vote verification | :x: | Signatures not verified |
| Transaction replay protection | :x: | No nonce/sequence |
| Blueprint cache invalidation | :x: | No invalidation mechanism |
| Rate limiting (all endpoints) | Partial | Auth endpoints only |
| HTTPS enforcement | Conditional | Only in non-dev |
| HSTS header | :white_check_mark: | 1 year, includeSubDomains |
| Input validation | :white_check_mark: | Comprehensive regex |
| Container security | :white_check_mark: | Non-root, multi-stage |
| Logging/monitoring | :white_check_mark: | OpenTelemetry |
| Dependency updates | :white_check_mark: | Current stable versions |
| Password hashing | :white_check_mark: | BCrypt |
| Authenticated encryption | :white_check_mark: | AES-256-GCM, ChaCha20 |

---

## 7. Remediation Roadmap

### Phase 1 — CRITICAL (Sprint 1)

| # | Finding | Effort | Section |
|---|---------|--------|---------|
| 1 | Revoke exposed Anthropic API key | 30 min | 5.1 |
| 2 | Implement Redis authentication | 2 hrs | 5.2 |
| 3 | Enable gRPC TLS for peer communication | 4 hrs | 5.3 |
| 4 | Cryptographically verify consensus votes | 2 days | 4.1 |
| 5 | Implement transaction sequence numbers | 3 days | 4.2 |
| 6 | Restrict CORS to specific origins | 2 hrs | 5.7 |
| 7 | Externalize JWT signing key (remove default) | 2 hrs | 5.4 |

### Phase 2 — HIGH (Sprint 2)

| # | Finding | Effort | Section |
|---|---------|--------|---------|
| 8 | Add participant role validation to action execution | 1 day | 3.1 |
| 9 | Add exception logging to ActionProcessor | 2 hrs | 3.2 |
| 10 | Implement blueprint cache TTL + invalidation | 1 day | 3.3 |
| 11 | Enforce atomic docket numbering (unique index) | 1 day | 4.3 |
| 12 | Implement validator approval workflow | 2 days | 4.5 |
| 13 | Migrate secrets to Azure Key Vault | 2 days | 5.5, 5.6 |
| 14 | Add OData field name whitelist | 4 hrs | 4.7 |
| 15 | Remove SingleValidatorAutoApprove from prod config | 1 hr | 4.6 |
| 16 | Priority sorting in unverified transaction pool | 1 day | 4.4 |

### Phase 3 — MEDIUM (Sprint 3-4)

| # | Finding | Effort | Section |
|---|---------|--------|---------|
| 17 | Fail-closed token revocation (configurable) | 4 hrs | 1.1 |
| 18 | Bootstrap endpoint protection | 4 hrs | 1.2 |
| 19 | SignalR service token scoping | 4 hrs | 3.4 |
| 20 | JSON Logic execution-time guards | 1 day | 3.5 |
| 21 | AccumulatedData field whitelisting | 1 day | 3.6 |
| 22 | Blueprint routing cycle detection | 1 day | 3.8 |
| 23 | Participant resolution circuit breaker | 4 hrs | 4.8 |
| 24 | Peer replication checksum verification | 4 hrs | 4.12 |
| 25 | Genesis docket quorum approval | 1 day | 4.10 |
| 26 | Transaction payload size limits | 2 hrs | 4.14 |
| 27 | Fork detection null MetaData fix | 2 hrs | 4.11 |
| 28 | Validator cache pub/sub invalidation | 4 hrs | 4.15 |
| 29 | Redis transaction atomicity (Lua scripts) | 1 day | 4.9 |

### Phase 4 — LOW (Backlog)

| # | Finding | Effort | Section |
|---|---------|--------|---------|
| 30 | P-256 deterministic derivation or documentation | 4 hrs | 2.2 |
| 31 | Mnemonic exposure mitigation | 4 hrs | 2.1 |
| 32 | Consensus threshold documentation | 1 hr | 4.16 |
| 33 | Log redaction for public keys | 2 hrs | 4.17 |
| 34 | Social login redirect URI whitelist | 2 hrs | 1.3 |
| 35 | Disclosure pointer validation post-decode | 4 hrs | 3.7 |
| 36 | Routing default route enforcement | 2 hrs | 3.9 |
| 37 | Calculation field whitelist | 4 hrs | 3.10 |
| 38 | Transaction expiry coordination | 4 hrs | 4.13 |
| 39 | Anonymous peer trust enforcement | 1 day | 5.10 |
| 40 | Password validation implementation | 4 hrs | 1.4 |
| 41 | gRPC reflection security | 1 hr | 5.9 |

---

## Appendix: Files Analyzed

### Authentication & Authorization
- `src/Services/Sorcha.Tenant.Service/Services/TokenService.cs`
- `src/Services/Sorcha.Tenant.Service/Services/TokenRevocationService.cs`
- `src/Services/Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs`
- `src/Services/Sorcha.Tenant.Service/Endpoints/BootstrapEndpoints.cs`
- `src/Services/Sorcha.Tenant.Service/Services/SocialLoginService.cs`
- `src/Services/Sorcha.ApiGateway/Program.cs`
- `src/Services/Sorcha.ApiGateway/appsettings.json`
- `src/Services/Sorcha.ApiGateway/Services/UrlResolutionMiddleware.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Authentication/BrowserTokenCache.cs`
- `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Http/AuthenticatedHttpMessageHandler.cs`

### Cryptography
- `src/Common/Sorcha.Cryptography/Core/CryptoModule.cs`
- `src/Common/Sorcha.Cryptography/Core/KeyManager.cs`
- `src/Common/Sorcha.Cryptography/Core/SymmetricCrypto.cs`
- `src/Common/Sorcha.Cryptography/Core/HashProvider.cs`
- `src/Common/Sorcha.Cryptography/Core/PqcSignatureProvider.cs`
- `src/Common/Sorcha.Cryptography/Core/PqcEncapsulationProvider.cs`
- `src/Common/Sorcha.Cryptography/Models/KeyChain.cs`
- `src/Common/Sorcha.Cryptography/Models/SymmetricCiphertext.cs`
- `src/Common/Sorcha.TransactionHandler/Encryption/EncryptionPipelineService.cs`
- `src/Common/Sorcha.TransactionHandler/Encryption/DisclosureGroupBuilder.cs`

### Blueprint Execution
- `src/Core/Sorcha.Blueprint.Engine/Implementation/ActionProcessor.cs`
- `src/Core/Sorcha.Blueprint.Engine/Implementation/DisclosureProcessor.cs`
- `src/Core/Sorcha.Blueprint.Engine/Implementation/RoutingEngine.cs`
- `src/Core/Sorcha.Blueprint.Engine/Implementation/JsonLogicEvaluator.cs`
- `src/Core/Sorcha.Blueprint.Engine/Validation/JsonLogicValidator.cs`
- `src/Services/Sorcha.Blueprint.Service/Services/Implementation/ActionExecutionService.cs`
- `src/Services/Sorcha.Blueprint.Service/Hubs/ActionsHub.cs`

### Consensus & Data Integrity
- `src/Services/Sorcha.Validator.Service/Services/ValidationEngine.cs`
- `src/Services/Sorcha.Validator.Service/Services/ConsensusEngine.cs`
- `src/Services/Sorcha.Validator.Service/Services/SignatureCollector.cs`
- `src/Services/Sorcha.Validator.Service/Services/DocketBuilder.cs`
- `src/Services/Sorcha.Validator.Service/Services/TransactionPoolPoller.cs`
- `src/Services/Sorcha.Validator.Service/Services/VerifiedTransactionQueue.cs`
- `src/Services/Sorcha.Validator.Service/Services/GenesisManager.cs`
- `src/Services/Sorcha.Validator.Service/Services/ValidatorRegistry.cs`
- `src/Core/Sorcha.Register.Core/Managers/RegisterManager.cs`
- `src/Services/Sorcha.Peer.Service/Replication/RegisterReplicationService.cs`

### Infrastructure
- `docker-compose.yml`
- `.env`
- `src/Common/Sorcha.ServiceDefaults/CorsExtensions.cs`
- `src/Common/Sorcha.ServiceDefaults/Extensions.cs`
- `src/Common/Sorcha.ServiceDefaults/InputValidationMiddleware.cs`
- `src/Common/Sorcha.ServiceDefaults/JwtAuthenticationExtensions.cs`
- `src/Common/Sorcha.ServiceDefaults/AuthorizationPolicyExtensions.cs`
- `src/Services/Sorcha.Peer.Service/GrpcServices/PeerAuthInterceptor.cs`
- `Directory.Packages.props`

---

*This report was generated by automated static analysis. Runtime/penetration testing is recommended to validate findings. Some line numbers may shift with ongoing development.*
