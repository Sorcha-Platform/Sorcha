# Sorcha Platform - Master Task List

> **Archived phases:** See [MASTER-TASKS-ARCHIVE.md](MASTER-TASKS-ARCHIVE.md) for completed phases, historical updates, critical path, and task summaries.

**Version:** 6.3
**Last Updated:** 2026-03-02
**Status:** Active - Production Packaging & CI/CD (Phase D complete)
**Related:** [MASTER-PLAN.md](MASTER-PLAN.md) | [TASK-AUDIT-REPORT.md](TASK-AUDIT-REPORT.md)

---

## Overview

This document tracks **active work only**. Completed phases and historical updates have been archived.

**Total Tasks:** 386 (across all phases)
**Completed:** 244 (63%)
**In Progress:** 4 (1%)
**Not Started:** 138 (36%)

---

## P0 — Production Packaging & Version Control

> **Priority:** P0 (Production Readiness)
> **Status:** 🚧 In Progress
> **Design Doc:** [docs/plans/2026-02-27-shared-library-nuget-design.md](../docs/archive/plans/2026-02-27-shared-library-nuget-design.md)

Establish production-grade NuGet packaging, Central Package Management, automated CI/CD, and code quality standards across the entire codebase.

### Phase A: Shared Libraries (src/Common/) — ✅ COMPLETE

**Plan doc:** [docs/plans/2026-02-27-code-quality-fixes.md](../docs/archive/plans/2026-02-27-code-quality-fixes.md)

| # | Task | Status |
|---|------|--------|
| A1 | Create Directory.Build.props for Common libraries (shared metadata) | ✅ |
| A2 | Create Directory.Packages.props (CPM for all 69 projects, 127 packages) | ✅ |
| A3 | Update all 80 .csproj files to use CPM (remove inline Version attributes) | ✅ |
| A4 | Add NuGet PackageId/Version/Description to all 15 Common libraries (v2.0.0) | ✅ |
| A5 | Standardize LanguageVersion to C# 13 across Common libraries | ✅ |
| A6 | Remove old GitHub Actions (main-ci-cd, pr-validation, release, docker-build-push, azure-deploy) | ✅ |
| A7 | Create nuget-ci.yml (PR build & test) | ✅ |
| A8 | Create nuget-publish.yml (auto minor version bump & publish to nuget.org) | ✅ |
| A9 | Create claude-review-issues.yml (review feedback → GitHub issues) | ✅ |
| A10 | Update codeql.yml branch references to master | ✅ |
| A11 | Purge all legacy branding from source code, docs, specs | ✅ |
| A12 | Code quality review across all 15 Common libraries | ✅ (review complete, fixes below) |
| A13 | Fill test gaps (Storage.EFCore, Storage.MongoDB, Storage.Redis, Tenant.Models, Wallet.Core) | ✅ |
| A14 | Update Directory.Packages.props — safe minor/patch package updates | ✅ |
| A15 | Validation: build, test, pack all 15 libraries | ✅ |
| A16 | Update git remote to sorcha-platform/sorcha | ✅ |

#### A12a: Critical Bug Fixes (5)

| # | Task | Status |
|---|------|--------|
| A12a-1 | Fix Redis `RemoveByPatternAsync` wildcard pattern corruption (`RedisCacheStore.cs`) | ✅ |
| A12a-2 | Fix `ComputeSHA256RIPEMD160` placeholder — implement or throw (`HashProvider.cs`) | ✅ |
| A12a-3 | Fix `DeleteWalletAsync` hardcoded `OldStatus = Active` → use `wallet.Status` (`WalletManager.cs:377`) | ✅ |
| A12a-4 | Fix `MongoSchemaRepository` constructor `.GetAwaiter().GetResult()` deadlock (`MongoSchemaRepository.cs:38-40`) | ✅ |
| A12a-5 | Fix `PeerServiceClient` `new HttpClient()` → use `IHttpClientFactory` injection (`PeerServiceClient.cs:62`) | ✅ |

#### A12b: Important Fixes (20)

| # | Task | Status |
|---|------|--------|
| A12b-1 | Remove TOCTOU race in `InMemoryWormStore.AppendAsync` — drop redundant `ContainsKey` | ✅ |
| A12b-2 | Remove MongoDB `InsertAsync` pre-check `GetAsync` — catch `DuplicateKey` instead | ✅ |
| A12b-3 | Make `VerifiedCache` hash verification probabilistic/background, not per-read | ✅ |
| A12b-4 | Fix `StorageProviderFactory.GetHealthAsync` fake healthy → `Unknown` status | ✅ |
| A12b-5 | Fix `EFCoreRepository.ExistsAsync` — use `AnyAsync` instead of `FindAsync` | ✅ |
| A12b-6 | Document EFCore unit-of-work contract on mutating methods (XML remarks) | ✅ |
| A12b-7 | Replace O(n) `List.RemoveAt(0)` latency tracking with `Queue` in Redis + VerifiedCache | ✅ |
| A12b-8 | Fix bare `catch {}` in 4 `WalletUtilities` crypto methods → catch specific exceptions | ✅ |
| A12b-9 | Consolidate algorithm-to-enum mapping (4 copies) → single `WalletNetworksParser` utility | ✅ |
| A12b-10 | Fix `TransactionFactory.GetSerializer` — unused version parameter (dead code) | ✅ |
| A12b-11 | Address `.GetAwaiter().GetResult()` in TransactionBuilder + serializers | ✅ |
| A12b-12 | Add PQC algorithms to `TransactionValidator.ValidateSignatures` valid list | ✅ |
| A12b-13 | Fix `SchemaStore.ListAsync` pagination with mixed system/repo schemas | ✅ |
| A12b-14 | Sanitize MongoDB regex input in `MongoSchemaRepository.ListAsync` (ReDoS) | ✅ |
| A12b-15 | Remove dead `_useGrpc` field from `RegisterServiceClient` | ✅ |
| A12b-16 | Extract shared `ServiceClientAuthHelper.SetAuthHeaderAsync` from duplicated code | ✅ |
| A12b-17 | Extract `ClientIpHelper.GetClientIp` from duplicated IP-parsing in ServiceDefaults | ✅ |
| A12b-18 | Create `AuthorizationPolicies` constants class for policy name strings | ✅ |
| A12b-19 | Replace `Console.WriteLine` with `ILogger` in `JwtAuthenticationExtensions` | ✅ |
| A12b-20 | Extract shared `EncryptionProviderBase` from duplicated AES-GCM code in Wallet.Core | ✅ |

#### A12c: Minor Fixes (15)

| # | Task | Status |
|---|------|--------|
| A12c-1 | Remove dead field `_jsonOptions` in `VerifiedCache` | ✅ |
| A12c-2 | Remove dead field `_idSetter` in `InMemoryWormStore` | ✅ |
| A12c-3 | Resolve duplicate config classes (`VerifiedCacheConfiguration` vs `RegisterCacheConfiguration`) | ✅ |
| A12c-4 | Add null connection string guard in `EFCoreServiceExtensions` | ✅ |
| A12c-5 | Rename or document `AddInMemoryStorageProviders` (only registers cache) | ✅ |
| A12c-6 | Remove `#region` blocks in Cryptography and TransactionHandler files | ✅ |
| A12c-7 | Add XML docs on public constructors in `TransactionValidator`, `DocketValidator` | ✅ |
| A12c-8 | Remove dead body from `[Obsolete(error:true)]` `GenerateAddressAsync` | ✅ |
| A12c-9 | Add XML docs on 4 core public methods in `Extensions.cs` | ✅ |
| A12c-10 | Extract multi-type files to separate files (JwtSettings, RateLimitPolicies, etc.) | ⏭️ Skipped (co-location is reasonable) |
| A12c-11 | Fix empty `new() { }` in `OpenApiExtensions` | ✅ |
| A12c-12 | Remove stale commented-out Aspire scaffold in `Extensions.cs` | ✅ |
| A12c-13 | Make `SerilogExtensions.AddSerilogLogging` generic (`TBuilder : IHostApplicationBuilder`) | ✅ (documented constraint) |
| A12c-14 | Standardize constructor parameter ordering across service clients | ✅ |
| A12c-15 | Set `UpdatedAt` in `DeleteWalletAsync` (consistency with `RecoverWalletAsync`) | ✅ |

#### Major Package Upgrades (Separate Effort — NOT JsonLogic or JsonSchema.Net)

| # | Task | Status |
|---|------|--------|
| A17 | Upgrade MudBlazor 8.15.0 → 9.0.0 (UI breaking changes) | ✅ |
| A18 | Upgrade Refit 9.0.2 → 10.0.1 (HTTP client generation changes) | ✅ |
| A19 | Upgrade ModelContextProtocol 0.7.0-preview.1 → 1.0.0 (MCP server GA) | ✅ |
| A20 | Upgrade Anthropic.SDK 4.0.0 → 5.10.0 (AI integration) | ✅ |
| A21 | Upgrade SimpleBase 4.0.2 → 5.6.0 (encoding API changes) | ✅ |
| A22 | Upgrade coverlet.collector 6.0.4 → 8.0.0 (test coverage) | ✅ |

### Phase B: Core Libraries (src/Core/) — ✅ COMPLETE

**Plan doc:** [docs/plans/2026-02-28-phase-b-core-libraries-plan.md](../docs/archive/plans/2026-02-28-phase-b-core-libraries-plan.md)

| # | Task | Status |
|---|------|--------|
| B1 | Add Directory.Build.props for Core libraries (NuGet metadata, build settings) | ✅ |
| B2 | Add NuGet PackageId/Version to Core libraries (8 projects, v2.0.0) | ✅ |
| B3 | Code quality review across Core libraries | ✅ (44 fixes: 10 critical, 14 important, 20 minor) |
| B4 | Fill test gaps in Core libraries | ✅ (1,055 tests passing, coverage verified) |
| B5 | Add nuget-publish.yml path triggers for src/Core/** | ✅ |

#### B3a: Critical Fixes (10)

| # | Task | Status |
|---|------|--------|
| B3a-1 | Fix `.GetAwaiter().GetResult()` in `MongoRegisterRepository` constructor — async index creation | ✅ |
| B3a-2 | Fix undisposed `JsonDocument` in `BitstringStatusListChecker.cs:48` | ✅ |
| B3a-3 | Fix bare `catch {}` in `BitstringStatusListChecker.cs:93-96` — catch specific exceptions | ✅ |
| B3a-4 | Fix bare `catch {}` in `CredentialVerifier.cs:199-203` — security-critical revocation check | ✅ |
| B3a-5 | Fix hardcoded `SignatureValid = true` in `CredentialVerifier.cs:154` — fail-safe to false | ✅ |
| B3a-6 | Fix `SemaphoreSlim` leak in `JsonLogicCache.cs` — implement IDisposable | ✅ |
| B3a-7 | Fix `SemaphoreSlim` leak in `JsonSchemaCache.cs` — implement IDisposable | ✅ |
| B3a-8 | Fix bare `catch {}` in `BlueprintServiceRepository.cs:233-236` | ✅ |
| B3a-9 | Fix bare `catch {}` in `BlueprintServiceRepository.cs:258-261` | ✅ |
| B3a-10 | Fix bare `catch {}` in `SchemaStoreRepository.cs:151-153` | ✅ |

#### B3b: Important Fixes (14)

| # | Task | Status |
|---|------|--------|
| B3b-1 | Fix TOCTOU race in `TransactionManager.cs:59-66` fork detection | ✅ |
| B3b-2 | Fix TOCTOU race in `InMemoryRegisterRepository.cs:65` UpdateRegisterAsync | ✅ |
| B3b-3 | Fix thread-unsafe `List<>` in `InMemoryEventPublisher.cs` — use ConcurrentBag | ✅ |
| B3b-4 | Fix thread-unsafe caching in `BlueprintServiceRepository.cs` — Dictionary, List | ✅ |
| B3b-5 | Fix thread-unsafe caching in `SchemaStoreRepository.cs` — List, bool, DateTime | ✅ |
| B3b-6 | Fix thread-unsafe `_memoryCache`/`_isLoaded` in `LocalStorageSchemaCacheService.cs` | ✅ |
| B3b-7 | Add `volatile` to `_initialized` in `BuiltInSchemaRepository.cs` | ✅ |
| B3b-8 | Fix `CancellationToken.None` in `DocketCacheWarmingService.cs` background task | ✅ |
| B3b-9 | Fix broad `catch (Exception)` in `ActionProcessor.cs:178-182` | ✅ |
| B3b-10 | Fix silent 365-day fallback in `CredentialIssuer.cs:114-125` — log warning | ✅ |
| B3b-11 | Fix `new MongoClient()` in `MongoRegisterRepository.cs` — use IMongoClient injection | ✅ |
| B3b-12 | Add `volatile` to `_classMapRegistered` in `MongoRegisterRepository.cs` | ✅ |
| B3b-13 | Fix double JSON parse in `BuiltInSchemaRepository.cs:82,117` | ✅ |
| B3b-14 | Fix O(n²) `Array.IndexOf` in `RedisStreamEventSubscriber.cs:108` | ✅ |

#### B3c: Minor Fixes (20)

| # | Task | Status |
|---|------|--------|
| B3c-1 | Replace `Console.WriteLine` with ILogger in `BuiltInSchemaRepository.cs` | ✅ |
| B3c-2 | Replace `Console.WriteLine` with ILogger in `SchemaStoreRepository.cs` (2 locations) | ✅ |
| B3c-3 | Replace `Console.WriteLine` with ILogger in `LocalStorageSchemaCacheService.cs` (2 locations) | ✅ |
| B3c-4 | Add logging to bare `catch {}` in `LocalStorageSchemaCacheService.cs:118-121` | ✅ |
| B3c-5 | Fix serialize-then-parse round-trip in `DataSchemaBuilder.cs:133-134` | ✅ |
| B3c-6 | Fix `JsonDocument.Parse("{}")` placeholder in `BlueprintServiceRepository.cs:302` | ✅ |
| B3c-7 | Add XML docs on `RegisterStorageConfiguration` defaults | ✅ |
| B3c-8 | Make 2-second startup delay configurable in `EventSubscriptionHostedService.cs` | ✅ |
| B3c-9 | Make Polly resilience parameters configurable in `RedisStreamEventPublisher.cs` | ✅ |
| B3c-10 | Make cache size limit configurable in `JsonLogicCache.cs` | ✅ |
| B3c-11 | Make cache size limit configurable in `JsonSchemaCache.cs` | ✅ |
| B3c-12 | Remove default connection string in `MongoRegisterStorageConfiguration.cs` | ✅ |
| B3c-13 | Fix `int` cast of `EstimatedDocumentCount` in `MongoRegisterRepository.cs` | ✅ |
| B3c-14 | Remove unnecessary `async` from 5 methods in `InMemoryRegisterRepository.cs` | ✅ |
| B3c-15 | Document `Dictionary` vs `ConcurrentDictionary` choice in `RedisStreamEventSubscriber.cs` | ✅ |
| B3c-16 | Document hardcoded `"revocation"` default in `BitstringStatusListChecker.cs` | ✅ |
| B3c-17 | Make 24-hour cache expiry configurable in `SchemaStoreRepository.cs` | ✅ |
| B3c-18 | Make 5-minute cache duration configurable in `BlueprintServiceRepository.cs` | ✅ |
| B3c-19 | Fix undisposed `JsonDocument` placeholder in `BlueprintServiceRepository.cs:302` | ✅ |
| B3c-20 | Add XML docs on hardcoded defaults in `RegisterStorageConfiguration.cs` | ✅ |

### Phase C: Services (src/Services/) — ✅ COMPLETE

**Plan doc:** [docs/plans/2026-02-28-phase-c-code-quality-findings.md](../docs/archive/plans/2026-02-28-phase-c-code-quality-findings.md)

| # | Task | Status |
|---|------|--------|
| C1 | Add Directory.Build.props for Services (shared build settings, no NuGet packaging) | ✅ |
| C2 | Standardize 7 service csproj files, Dockerfiles, gitignore, Peer data→Data rename | ✅ |
| C3 | Code quality review across all 7 services (21 findings: 7 critical, 9 important, 5 minor) | ✅ |
| C4 | Create docker-ci.yml (PR Docker build validation, matrix of 7 services) | ✅ |
| C5 | Create docker-publish.yml (auto Docker image build & push on merge to Docker Hub) | ✅ |
| C6 | Integration test improvements (Register.Service.IntegrationTests, remove stubs) | ✅ |

### Phase D: Applications (src/Apps/) — ✅ COMPLETE

**Plan doc:** [docs/plans/2026-02-28-phase-d-code-quality-findings.md](../docs/archive/plans/2026-02-28-phase-d-code-quality-findings.md)

| # | Task | Status |
|---|------|--------|
| D1 | Add Directory.Build.props for Apps (shared build settings) | ✅ |
| D2 | Code quality review across all applications (31 bare catches, 7 #region files, 11 Console.WriteLine) | ✅ |
| D3 | Standardize app project configuration (7 csproj, 2 Dockerfiles) | ✅ |
| D4 | Add Apps to Docker CI/CD + CLI publish workflow | ✅ |
| D5 | E2E test pipeline integration (Playwright in CI) | ✅ |

### Phase E: Feature Completion (98% → 100% MVD) — ✅ COMPLETE

| # | Task | Status |
|---|------|--------|
| E1a | Stabilize Tenant test suite (BCrypt→Argon2id in TestDataSeeder) | ✅ |
| E1b | Stabilize CLI test suite (unified xUnit collection for env var isolation) | ✅ |
| E1c | Stabilize Peer integration tests (rewrite factory + 5 test files) | ✅ |
| E2a | Register PeerDbContext in DI (PostgreSQL + InMemory fallback) | ✅ |
| E2b | Wire TransactionDistributionService.ProcessQueueAsync in PeerService loop | ✅ |
| E2c | Wire live subscription loop in RegisterSyncBackgroundService | ✅ |
| E2d | Align appsettings.json structure with PeerServiceConfiguration model | ✅ |
| E3a | Implement RegisterSyncGrpcService (4 RPCs: PullDocketChain, PullDocketTransactions, SubscribeToRegister, GetRegisterSyncStatus) | ✅ |
| E3b | Implement TransactionDistributionGrpcService (3 RPCs: NotifyTransaction, GetTransaction, StreamTransaction) | ✅ |
| E3c | Fix Tenant GetOrganizationStats TotalUsers (add GetTotalActiveUserCountAsync) | ✅ |
| E3d | Fix Tenant ListServicePrincipals includeInactive (add GetAllServicePrincipalsAsync) | ✅ |
| E3e | Fix Validator duplicate detection cross-check (Redis mempool O(1) set index) | ✅ |
| E3f | Add YARP auth policies to 48 API routes + rate limiting on 7 write routes | ✅ |

---

## 045 — Encrypted Payload Integration

> **Priority:** P0 (Security)
> **Status:** ✅ Complete
> **Branch:** `045-encrypted-payload-integration`
> **Spec:** [specs/045-encrypted-payload-integration/](../specs/045-encrypted-payload-integration/)

Envelope encryption for action transaction payloads — XChaCha20-Poly1305 symmetric encryption with per-recipient asymmetric key wrapping, disclosure grouping, async pipeline with SignalR progress, and recipient decryption.

| Phase | Description | Tasks | Status |
|-------|-------------|-------|--------|
| 1 | Setup (models, interfaces) | T001-T006 | ✅ |
| 2 | Foundational (P-256 ECIES, ML-KEM fix, batch keys, size limits) | T007-T016 | ✅ |
| 3 | US1: Core envelope encryption | T017-T026 | ✅ |
| 4 | US2: Disclosure grouping | T027-T031 | ✅ |
| 5 | US3: Algorithm completeness | T032-T035 | ✅ |
| 6 | US4: Public key resolution | T036-T038 | ✅ |
| 7 | US5: Async pipeline + SignalR | T039-T048 | ✅ |
| 8 | US6: Pre-flight size estimation | T049-T052 | ✅ |
| 9 | US7: Recipient decryption | T053-T058 | ✅ |
| 10 | Polish (telemetry, docs, YARP) | T059-T067 | ✅ |

---

## P0 — Transaction Pipeline Audit

> **Priority:** P0 (MVD Blocker)
> **Status:** ✅ Complete
> **Research Doc:** [docs/transaction-submission-flow.md](../docs/reference/transaction-submission-flow.md)

All transactions MUST go through the Validator Service mempool before being sealed into dockets. Direct writes to the register ledger are NOT permitted.

| # | Task | Status |
|---|------|--------|
| 1 | Document current transaction submission flows (genesis, action, control, governance) | ✅ |
| 2 | Ensure blueprint publish submits through validator, not directly to register | ✅ |
| 3 | Investigate validator initialization loop (no transaction processing observed) | ✅ |
| 4 | Test end-to-end: publish blueprint → validator mempool → validation → docket → register | ✅ |
| 5 | Audit all register write paths for direct-store bypasses | ✅ |
| 6 | Wire governance operations through validator pipeline | ✅ |
| 7 | Clean up orphan transaction from prior direct-write (MongoDB) | ✅ |
| 8 | ~~Consider dedicated `/api/validator/control` endpoint vs reusing genesis~~ | ✅ |

---

---

## 046 — UI Polish & Blueprint Designer

> **Priority:** P2 (Polish)
> **Status:** ✅ Complete
> **Branch:** `046-ui-polish-designer`
> **Spec:** [specs/046-ui-polish-designer/](../specs/046-ui-polish-designer/)

43 tasks across 7 user stories: dashboard wizard/GUID bug (US1), notification panel overflow (US2), dark mode hardcoded colors (US3), stale coming-soon labels (US4), blueprint designer save/load (US5), EventsHub real-time notifications (US6), i18n wiring for Home/Settings/MainLayout (US7). All 3 UI projects build with 0 errors. 618 bUnit tests pass.

**Last Updated:** 2026-03-02
**Next Review:** Weekly
**Document Owner:** Sorcha Architecture Team
