# Sorcha Platform - Master Task List

> **Archived phases:** See [MASTER-TASKS-ARCHIVE.md](MASTER-TASKS-ARCHIVE.md) for completed phases, historical updates, critical path, and task summaries.

**Version:** 6.2
**Last Updated:** 2026-02-28
**Status:** Active - Production Packaging & CI/CD
**Related:** [MASTER-PLAN.md](MASTER-PLAN.md) | [TASK-AUDIT-REPORT.md](TASK-AUDIT-REPORT.md)

---

## Overview

This document tracks **active work only**. Completed phases and historical updates have been archived.

**Total Tasks:** 386 (across all phases)
**Completed:** 240 (62%)
**In Progress:** 8 (2%)
**Not Started:** 138 (36%)

---

## P0 — Production Packaging & Version Control

> **Priority:** P0 (Production Readiness)
> **Status:** 🚧 In Progress
> **Design Doc:** [docs/plans/2026-02-27-shared-library-nuget-design.md](../docs/plans/2026-02-27-shared-library-nuget-design.md)

Establish production-grade NuGet packaging, Central Package Management, automated CI/CD, and code quality standards across the entire codebase.

### Phase A: Shared Libraries (src/Common/) — 🚧 IN PROGRESS

**Plan doc:** [docs/plans/2026-02-27-code-quality-fixes.md](../docs/plans/2026-02-27-code-quality-fixes.md)

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
| A13 | Fill test gaps (Storage.EFCore, Storage.MongoDB, Storage.Redis, Tenant.Models, Wallet.Core) | 📋 |
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
| A17 | Upgrade MudBlazor 8.15.0 → 9.0.0 (UI breaking changes) | 📋 |
| A18 | Upgrade Refit 9.0.2 → 10.0.1 (HTTP client generation changes) | 📋 |
| A19 | Upgrade ModelContextProtocol 0.7.0-preview.1 → 1.0.0 (MCP server GA) | 📋 |
| A20 | Upgrade Anthropic.SDK 4.0.0 → 5.10.0 (AI integration) | 📋 |
| A21 | Upgrade SimpleBase 4.0.2 → 5.6.0 (encoding API changes) | 📋 |
| A22 | Upgrade coverlet.collector 6.0.4 → 8.0.0 (test coverage) | 📋 |

### Phase B: Core Libraries (src/Core/) — 📋 NOT STARTED

| # | Task | Status |
|---|------|--------|
| B1 | Add Directory.Build.props for Core libraries (NuGet metadata, build settings) | 📋 |
| B2 | Add NuGet PackageId/Version to Core libraries (Blueprint.Engine, Blueprint.Fluent, Register.Core, Register.Storage.*) | 📋 |
| B3 | Code quality review across Core libraries | 📋 |
| B4 | Fill test gaps in Core libraries | 📋 |
| B5 | Add nuget-publish.yml path triggers for src/Core/** | 📋 |

### Phase C: Services (src/Services/) — 📋 NOT STARTED

| # | Task | Status |
|---|------|--------|
| C1 | Add Directory.Build.props for Services (shared build settings, Docker metadata) | 📋 |
| C2 | Standardize service project structure and naming | 📋 |
| C3 | Code quality review across all 7 services | 📋 |
| C4 | Create docker-ci.yml (PR Docker build validation) | 📋 |
| C5 | Create docker-publish.yml (auto Docker image build & push on merge) | 📋 |
| C6 | Service-level integration test improvements | 📋 |

### Phase D: Applications (src/Apps/) — 📋 NOT STARTED

| # | Task | Status |
|---|------|--------|
| D1 | Add Directory.Build.props for Apps (shared build settings) | 📋 |
| D2 | Code quality review across all applications | 📋 |
| D3 | Standardize app project configuration | 📋 |
| D4 | Create app-specific CI workflows (CLI publish, UI deploy) | 📋 |
| D5 | E2E test pipeline integration (Playwright in CI) | 📋 |

---

## P0 — Transaction Pipeline Audit

> **Priority:** P0 (MVD Blocker)
> **Status:** ✅ Complete
> **Research Doc:** [docs/transaction-submission-flow.md](../docs/transaction-submission-flow.md)

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

**Last Updated:** 2026-02-27
**Next Review:** Weekly
**Document Owner:** Sorcha Architecture Team
