---
description: "Task list for feature 113 — storage provider audit and validator mempool durability"
---

# Tasks: Storage Provider Audit and Validator Mempool Durability

**Input**: Design documents from `/specs/113-storage-durability-audit/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: This feature explicitly requests cross-backend contract tests as a deliverable (Axis iv / US4) and concurrent-consume race tests for HAIP (US3). All test tasks below are first-class deliverables, not optional.

**Organisation**: Tasks are grouped by user story so each P1 story (US1, US2, US3) can be implemented and shipped as an independent PR sequence. US4 (P2 — contract tests) and US5 (P3 — observability) interleave with the implementation phases per the eight-PR rollout in the design.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps task to a user story (US1–US5) for traceability

## Path Conventions

Sorcha is a microservices monorepo. Source under `src/Common/`, `src/Services/`, `src/Apps/`. Tests under `tests/`. Solution file is `Sorcha.sln`.

## Pre-rebase context (master state when tasks were generated)

- Wallet, Blueprint, Tenant, Peer have already adopted `SorchaConnectionsExtensions` (master commits `b85eb982`, `4b5c1f5e`). Existing `hasResolverConfig`-guarded fallback pattern is preserved; registration-log calls insert at the existing fallback-decision points.
- `IRepository<T>` / `EFCoreRepository<T>` were deleted in master commit `50b8d93d`. Surviving abstractions (`IDocumentStore`, `IWormStore`, `ICacheStore`, `IVerifiedCache`) remain and are unaffected by this feature.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: New project skeletons and solution updates that every later phase depends on.

- [ ] T001 Create new csproj `src/Common/Sorcha.AtomicCache/Sorcha.AtomicCache.csproj` (net10.0, nullable enable, GlobalUsings, license header, references `StackExchange.Redis` and `Microsoft.Extensions.Logging.Abstractions`)
- [ ] T002 [P] Create new test csproj `tests/Sorcha.AtomicCache.Tests/Sorcha.AtomicCache.Tests.csproj` (xUnit 3.2.2, FluentAssertions 8.8.0, references `Sorcha.AtomicCache` and `Sorcha.Testing`)
- [ ] T003 [P] Create new test csproj `tests/Sorcha.ServiceDefaults.Tests/Sorcha.ServiceDefaults.Tests.csproj` (xUnit, FluentAssertions, Moq, references `Sorcha.ServiceDefaults`)
- [ ] T004 Add `Sorcha.AtomicCache`, `Sorcha.AtomicCache.Tests`, `Sorcha.ServiceDefaults.Tests` to `Sorcha.sln`
- [ ] T005 [P] Create folder `src/Services/Sorcha.Validator.Service/Storage/` with placeholder `.gitkeep` (will hold mempool interface and implementations from Phase 4)

**Checkpoint**: Solution builds cleanly with the empty new projects (`dotnet build` passes).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: `IStorageRegistrationLog` infrastructure in `Sorcha.ServiceDefaults` plus the audited-interface enforcement and health check. Every user story consumes this — no story work can begin until Phase 2 ships.

**⚠️ CRITICAL**: This phase corresponds to PR 1 of the eight-PR rollout. Merge before any service-adoption PR.

- [X] T006 Create `src/Common/Sorcha.ServiceDefaults/Storage/IStorageRegistrationLog.cs` per `specs/113-storage-durability-audit/contracts/IStorageRegistrationLog.cs` (interface + `StorageRegistrationRecord` record)
- [X] T007 Create `src/Common/Sorcha.ServiceDefaults/Storage/AuditedStorageInterfaces.cs` with the explicit allow-list per `data-model.md §2` (six interface FQNs)
- [X] T008 Create `src/Common/Sorcha.ServiceDefaults/Storage/StorageRegistrationLog.cs` — singleton implementation that captures records, logs Information for persistent and Warning with `[STORAGE-FALLBACK]` banner for in-memory, throws `InvalidOperationException` on duplicate registration of the same interface
- [X] T009 Create `src/Common/Sorcha.ServiceDefaults/Storage/StorageProvidersHealthCheck.cs` — implements `IHealthCheck`, reports `Healthy` if no audited interfaces are in-memory, `Degraded` (with `Description` listing each in-memory audited interface) otherwise
- [X] T010 Create `src/Common/Sorcha.ServiceDefaults/Storage/StorageRegistrationEnforcement.cs` static helper — `EnforcePersistentStorageInProduction(IStorageRegistrationLog, IHostEnvironment, bool allowOverride)` throws when `IsProduction()` or `IsStaging()` and any audited interface is in-memory unless `allowOverride` is true; logs `LogCritical` when override is in effect
- [X] T011 Create `src/Common/Sorcha.ServiceDefaults/Storage/StorageEnforcementHostedService.cs` — `IHostedService` that calls `EnforcePersistentStorageInProduction` once at `StartAsync` after DI container is built; reads `Storage:AllowInMemoryInProduction` from `IConfiguration`
- [X] T012 [P] Create `src/Common/Sorcha.ServiceDefaults/Storage/StorageRegistrationMetrics.cs` — uses `IMeterFactory.Create("Sorcha.Storage")` to obtain a `Meter`, registers two `ObservableGauge<long>` instruments (`sorcha_storage_provider_info`, `sorcha_storage_fallback_active`) with observation callbacks that read from the registration log snapshot. Tags match `data-model.md §9` / FR-025 / FR-026
- [X] T013 [P] Update `src/Common/Sorcha.ServiceDefaults/Extensions.cs` `ConfigureOpenTelemetry` to add `metrics.AddMeter("Sorcha.Storage")` alongside the existing `Sorcha.Peer.Service` and `Sorcha.Blueprint.Service.Presentation` meter registrations
- [X] T014 Update `src/Common/Sorcha.ServiceDefaults/Extensions.cs` (or equivalent) to register `IStorageRegistrationLog` as a singleton, register `StorageProvidersHealthCheck` under name `"storage-providers"`, register `StorageEnforcementHostedService`
- [X] T015 [P] [US4] Write `tests/Sorcha.ServiceDefaults.Tests/Storage/StorageRegistrationLogTests.cs` — covers RegisterPersistent emits Information, RegisterInMemory emits Warning, duplicate registration throws, snapshot returns immutable copy, audited-vs-cache flag set correctly
- [X] T016 [P] [US4] Write `tests/Sorcha.ServiceDefaults.Tests/Storage/StorageRegistrationEnforcementTests.cs` — covers Production with audited InMemory throws, Staging with audited InMemory throws, Development with audited InMemory does not throw, override flag bypasses with LogCritical, cache-store InMemory does not trigger throw
- [X] T017 [P] [US4] Write `tests/Sorcha.ServiceDefaults.Tests/Storage/StorageProvidersHealthCheckTests.cs` — covers Healthy when all audited persistent, Degraded when any audited InMemory, Description enumerates offenders

**Checkpoint**: PR 1 mergeable. `Sorcha.ServiceDefaults` exposes the registration log + fail-fast helper + health check; no service consumes them yet so no behavioural change anywhere.

---

## Phase 3: User Story 1 — Misconfigured Production deploy fails loudly (Priority: P1) 🎯 MVP

**Goal**: Wallet, Register, and Blueprint services adopt the registration log. A misconfigured Production deploy of any of them refuses to start with a clear error.

**Independent Test**: Boot Wallet Service in Production with `ConnectionStrings__Wallet__Postgres=` and `ConnectionStrings__Sorcha__Postgres=` both empty. Service must throw with the registration log message identifying `IWalletRepository → InMemoryWalletRepository`. Repeating in Development must succeed with a `[STORAGE-FALLBACK]` warning.

This phase corresponds to PRs 2, 3, and 4 of the eight-PR rollout — three independently-mergeable PRs, one per service.

### Wallet adoption (PR 2)

- [ ] T018 [US1] Edit `src/Services/Sorcha.Wallet.Service/Extensions/WalletServiceExtensions.cs:91-147` to inject `IStorageRegistrationLog` (resolve from `services.BuildServiceProvider()` is wrong — accept it as a parameter to `AddWalletDatabase` or resolve from a `static` accessor pattern; pick the cleanest option matching existing helpers); call `RegisterPersistent("IWalletRepository", "EfCoreWalletRepository", "postgres")` in the persistent branch and `RegisterInMemory("IWalletRepository", "InMemoryWalletRepository", "no Postgres connection string in ConnectionStrings:Wallet:Postgres or ConnectionStrings:Sorcha:Postgres")` in the fallback branch
- [ ] T019 [US1] Edit `src/Services/Sorcha.Wallet.Service/Program.cs` to invoke `builder.AddServiceDefaults()` (already present) plus the new registration-log wiring; ensure `StorageEnforcementHostedService` is registered automatically via `AddServiceDefaults`
- [ ] T020 [P] [US1] Write `tests/Sorcha.Wallet.Service.Tests/Startup/StorageRegistrationIntegrationTests.cs` — `WebApplicationFactory<Program>` boots in Production env with no connection string, asserts `InvalidOperationException` with the expected message; same factory in Development asserts service starts and `LogWarning` was emitted with `[STORAGE-FALLBACK]` banner
- [ ] T021 [P] [US1] Add `tests/Sorcha.Wallet.Service.Tests/Startup/StorageRegistrationIntegrationTests.cs` cases for `Storage:AllowInMemoryInProduction=true` bypass — service starts in Production with `LogCritical` recording the override

### Register adoption (PR 3)

- [ ] T022 [US1] Edit `src/Services/Sorcha.Register.Service/Program.cs:90-120` to call `RegisterPersistent("IRegisterRepository", "MongoRegisterRepository", "mongo")` in the persistent branch and `RegisterInMemory("IRegisterRepository", "InMemoryRegisterRepository", "no MongoDB connection string ...")` in the fallback branch (note: the cascade-resolver pattern may need to be adopted in the same PR since this service still uses pre-cascade config keys — confirm during implementation)
- [ ] T023 [P] [US1] Write `tests/Sorcha.Register.Service.Tests/Startup/StorageRegistrationIntegrationTests.cs` mirroring T020/T021 for Register Service
- [ ] T024 [P] [US1] Verify `tests/Sorcha.Register.Storage.Tests/InMemoryRegisterRepositoryContractTests.cs` and `CachedRegisterRepositoryContractTests.cs` still pass — Register already has the contract-test pattern this feature ports elsewhere

### Blueprint adoption (PR 4)

- [ ] T025 [US1] Edit `src/Services/Sorcha.Blueprint.Service/Program.cs:50-73` and `:147-157` to call `RegisterPersistent`/`RegisterInMemory` for `IBlueprintStore`, `IPublishedBlueprintStore`, `IInstanceStore`, `IActionStore`. `IBlueprintStore` and `IPublishedBlueprintStore` are cache-style — call `RegisterInMemory` with reason "rebuilds from register transaction log on cold start" so they get the warning but are NOT on the audited list (already excluded by `AuditedStorageInterfaces`)
- [ ] T026 [US1] Replace the existing `Serilog.Log.Logger.Warning("Blueprint Service using in-memory storage — data will be lost on restart")` at `src/Services/Sorcha.Blueprint.Service/Program.cs:72` with the registration-log call from T025 — single source of truth for the fallback warning
- [ ] T027 [P] [US1] Write `tests/Sorcha.Blueprint.Service.Tests/Startup/StorageRegistrationIntegrationTests.cs` mirroring T020/T021 for Blueprint Service; assert audited interfaces (`IInstanceStore`, `IActionStore`) trigger fail-fast and cache stores (`IBlueprintStore`, `IPublishedBlueprintStore`) do not

### Cross-cutting

- [ ] T028 [US1] Update `docker-compose.yml` and `docker-compose.n1.yml` health-check section comments to mention the new `storage-providers` health check (no new compose entries needed — health checks are exposed via existing `/health` endpoint)
- [ ] T029 [P] [US1] Update Wallet/Register/Blueprint service README files to document the `Storage:AllowInMemoryInProduction` bypass flag and the fail-fast behaviour

**Checkpoint**: PRs 2/3/4 mergeable independently and in any order after PR 1. Boot any of the three services in Production without their connection strings → service refuses to start with a clear log line. MVP delivered: misconfigured Production deploys fail loudly.

---

## Phase 4: User Story 2 — Validator mempool survives restart and replica failover (Priority: P1)

**Goal**: Replace the per-process `ConcurrentDictionary` mempool with a Redis-Sorted-Set-backed implementation behind a tightened `Claim`/`Confirm`/`Release` lease contract.

**Independent Test**: Enqueue verified transactions for a register, kill the validator process, restart it. Without re-validating, the validator claims and seals those transactions. With two replicas sharing one validator identity, kill the active replica mid-claim → standby completes the seal after lease expiry, no transaction sealed twice.

This phase corresponds to PRs 7 and 8 of the eight-PR rollout. PR 7 reshapes the contract and renames the in-memory implementation (no behavioural change); PR 8 adds the Redis backing and flips the registration.

### Contract reshape and InMemory rename (PR 7)

- [ ] T030 [US2] Move `src/Services/Sorcha.Validator.Service/Services/Interfaces/IVerifiedTransactionQueue.cs` to `src/Services/Sorcha.Validator.Service/Storage/IVerifiedTransactionQueue.cs` and replace contents with the contract from `specs/113-storage-durability-audit/contracts/IVerifiedTransactionQueue.cs` (remove `Dequeue`/`ReturnToQueue`; add `ClaimAsync`/`ConfirmAsync`/`ReleaseAsync` and the `VerifiedTransactionLease` record)
- [ ] T031 [US2] Rename `src/Services/Sorcha.Validator.Service/Services/VerifiedTransactionQueue.cs` to `src/Services/Sorcha.Validator.Service/Storage/InMemoryVerifiedTransactionQueue.cs`; update class name and namespace; adapt to the new lease-shaped contract (claim takes-and-tracks-claimed in `_claimed` SortedSet, confirm removes from claimed and `_byId`, release moves back to `_queue`); keep the existing per-register `ConcurrentDictionary` partitioning
- [ ] T032 [US2] Update `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs` (and any other in-service call sites) from the old `Dequeue` + `ReturnToQueue` pattern to `ClaimAsync` + `ConfirmAsync`-on-success / `ReleaseAsync`-on-failure; lease duration from `IOptions<ValidatorMempoolOptions>.LeaseDurationSeconds` (default 60)
- [ ] T033 [US2] Add `src/Services/Sorcha.Validator.Service/Configuration/ValidatorMempoolOptions.cs` POCO with `LeaseDurationSeconds` (default 60), `MaxClaimBatchSize` (default 100), `CleanupIntervalSeconds` (default 30); bind from `ValidatorMempool` config section in `Program.cs`
- [ ] T034 [US2] Update `src/Services/Sorcha.Validator.Service/Program.cs` registration of `IVerifiedTransactionQueue` to call `storageLog.RegisterInMemory("IVerifiedTransactionQueue", "InMemoryVerifiedTransactionQueue", "no Redis connection string ...")` — this PR registers as InMemory only (Redis backing comes in PR 8); audited-list entry already added in T007
- [ ] T035 [P] [US2] Write `tests/Sorcha.Validator.Service.Tests/Storage/Contracts/IVerifiedTransactionQueueContractTests.cs` abstract base — covers Enqueue → Peek returns transaction; ClaimAsync removes from Peek; Claimed transaction invisible to subsequent Claim until lease expires; ConfirmAsync makes transaction permanently absent; ReleaseAsync makes transaction Peek-able and Claim-able again; expired lease auto-releases on next ClaimAsync; CleanupExpired removes TTL-expired transactions
- [ ] T036 [P] [US2] Write `tests/Sorcha.Validator.Service.Tests/Storage/Contracts/InMemoryVerifiedTransactionQueueContractTests.cs` subclass — instantiates `InMemoryVerifiedTransactionQueue` with `Microsoft.Extensions.Time.Testing.FakeTimeProvider`-backed clock so lease-expiry tests can advance time deterministically
- [ ] T037 [US2] Update existing tests under `tests/Sorcha.Validator.Service.Tests/` that reference the old `Dequeue` / `ReturnToQueue` API — rewrite against the new lease-shaped API. Pre-existing compile errors in `Sorcha.Validator.Service.Tests` per MEMORY.md must be resolved or filtered around in this PR; do not let them mask new failures

### Redis backing (PR 8)

- [ ] T038 [P] [US2] Create `src/Services/Sorcha.Validator.Service/Storage/Lua/claim-and-release.lua` from `specs/113-storage-durability-audit/contracts/verified-transaction-queue.lua` — embedded resource in the project
- [ ] T039 [US2] Create `src/Services/Sorcha.Validator.Service/Storage/RedisVerifiedTransactionQueue.cs` — implements `IVerifiedTransactionQueue` using `IDatabase` from `IConnectionMultiplexer`; key layout per `data-model.md §5`; loads Lua from embedded resource at construction; uses `MULTI/EXEC` for non-claim multi-key writes
- [ ] T040 [US2] Create `src/Services/Sorcha.Validator.Service/Storage/VerifiedTransactionExpirySweep.cs` — `BackgroundService` that calls `CleanupExpired` every `CleanupIntervalSeconds` (default 30s)
- [ ] T041 [US2] Wire `RedisVerifiedTransactionQueue` registration in `src/Services/Sorcha.Validator.Service/Program.cs` — `if (hasRedisConfig) { AddSingleton<IVerifiedTransactionQueue, RedisVerifiedTransactionQueue>(); storageLog.RegisterPersistent(...); } else { AddSingleton<IVerifiedTransactionQueue, InMemoryVerifiedTransactionQueue>(); storageLog.RegisterInMemory(...); }`. Use the same `hasResolverConfig` cascade-pattern as Wallet/Blueprint (`ConnectionStrings:Validator:Redis` → `ConnectionStrings:Sorcha:Redis`)
- [ ] T042 [P] [US2] Create `src/Services/Sorcha.Validator.Service/Storage/ValidatorMempoolMetrics.cs` — uses `IMeterFactory.Create("Sorcha.Validator.Mempool")` to register `sorcha_validator_mempool_size` (observable gauge, tags `register_id`/`state`) and `sorcha_validator_mempool_lease_expired_total` (counter, tag `register_id`). Wired into `RedisVerifiedTransactionQueue` and the expiry sweep. Add `metrics.AddMeter("Sorcha.Validator.Mempool")` to `Sorcha.ServiceDefaults.Extensions.ConfigureOpenTelemetry` (or to `Sorcha.Validator.Service`'s own meter registration if scoped per-service)
- [ ] T043 [P] [US2] Write `tests/Sorcha.Validator.Service.Tests/Storage/Contracts/RedisVerifiedTransactionQueueContractTests.cs` subclass — uses `Sorcha.Testing.MockRedisBuilder` for the Redis backing; runs the same contract from T035
- [ ] T044 [P] [US2] Write `tests/Sorcha.Validator.Service.Tests/Storage/RedisVerifiedTransactionQueueLuaSmokeTests.cs` — Testcontainers Redis (one container per test class lifetime, real Redis 7); exercises the claim Lua script for: empty-queue → empty result; available-only → claim N highest priority; expired-claim auto-releases on next claim; orphaned claim with deleted payload is dropped silently
- [ ] T045 [P] [US2] Write `tests/Sorcha.Validator.Service.Tests/Storage/RedisVerifiedTransactionQueueCrashRecoveryTests.cs` — exercises the HA-replica scenario: client A claims, simulated crash (drop the lease without confirming), advance fake clock past lease, client B claims → returns the same transaction; subsequent confirm-from-A is a no-op (lease already gone)
- [ ] T046 [US2] Update `docker-compose.yml` and `docker-compose.n1.yml` to ensure the Validator service has `ConnectionStrings__Sorcha__Redis` injected (cascade default; per-service override available via `ConnectionStrings__Validator__Redis`); Aspire AppHost similarly references the existing Redis resource
- [ ] T047 [P] [US2] Update `src/Services/Sorcha.Validator.Service/README.md` documenting the lease pattern, restart durability, configuration options, and HA-replica deployment shape

**Checkpoint**: PRs 7 and 8 mergeable in sequence after PR 1. After PR 8 deploys, killing the validator mid-claim no longer loses transactions; the standby-replica deployment shape works end-to-end.

---

## Phase 5: User Story 3 — HAIP nonces cannot be replayed under concurrent consume (Priority: P1)

**Goal**: Introduce `IAtomicDistributedCache` shim with atomic GETDEL and CAS; migrate `NonceStore` and `PreAuthCodeStore` to single-round-trip consumption; audit and tighten `PresentationRequestStore` if needed.

**Independent Test**: 100 concurrent `NonceStore.ConsumeAsync(nonce)` calls against the same nonce → exactly one returns `true`. Same shape for `PreAuthCodeStore`. Terminal-state CAS race on `PresentationRequestStore` resolves to one winner.

This phase corresponds to PRs 5 and 6 of the eight-PR rollout. PR 5 introduces the atomic-cache infrastructure; PR 6 migrates HAIP.

### IAtomicDistributedCache infrastructure (PR 5)

- [ ] T048 [P] [US3] Create `src/Common/Sorcha.AtomicCache/IAtomicDistributedCache.cs` per `specs/113-storage-durability-audit/contracts/IAtomicDistributedCache.cs`
- [ ] T049 [P] [US3] Create `src/Common/Sorcha.AtomicCache/RedisAtomicDistributedCache.cs` — implements `IAtomicDistributedCache` over `IDatabase`; `GetAndRemoveAsync` calls `IDatabase.StringGetDeleteAsync`; `TryUpdateIfMatchAsync` is a Lua script `if GET KEYS[1] == ARGV[1] then SET KEYS[1] ARGV[2] EX ARGV[3]; return 1 else return 0 end`
- [ ] T050 [P] [US3] Create `src/Common/Sorcha.AtomicCache/InMemoryAtomicDistributedCache.cs` — `ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)>`; `GetAndRemoveAsync` uses `TryRemove(key, out value)` then expiry check; `TryUpdateIfMatchAsync` uses `_lock` over read-then-write; background sweep removes expired entries every 60s
- [ ] T051 [US3] Create `src/Common/Sorcha.AtomicCache/Extensions/AtomicCacheServiceExtensions.cs` — `AddAtomicDistributedCache(IServiceCollection, IConfiguration, string serviceName)` extension that registers Redis-backed when connection string resolves and InMemory otherwise; calls `IStorageRegistrationLog.RegisterPersistent`/`RegisterInMemory` accordingly
- [ ] T052 [P] [US3] [US4] Write `tests/Sorcha.AtomicCache.Tests/Contracts/IAtomicDistributedCacheContractTests.cs` abstract base — covers Set then Get returns value, RemoveAsync idempotent, GetAndRemoveAsync atomic (returns value once, null after), TryUpdateIfMatchAsync succeeds when expected matches, fails when expected mismatches, fails when key absent, refreshes TTL on success, TTL expiry honoured
- [ ] T053 [P] [US3] [US4] Write `tests/Sorcha.AtomicCache.Tests/Contracts/InMemoryAtomicDistributedCacheContractTests.cs` subclass
- [ ] T054 [P] [US3] [US4] Write `tests/Sorcha.AtomicCache.Tests/Contracts/RedisAtomicDistributedCacheContractTests.cs` subclass — uses `Sorcha.Testing.MockRedisBuilder`
- [ ] T055 [P] [US3] Write `tests/Sorcha.AtomicCache.Tests/RedisAtomicDistributedCacheLuaSmokeTests.cs` — Testcontainers Redis; covers the CAS Lua script against real Redis (the script is small but it's the only Lua in this project)
- [ ] T056 [P] [US3] Write `tests/Sorcha.AtomicCache.Tests/ConcurrentConsumeRaceTests.cs` — 100 concurrent `GetAndRemoveAsync` calls on the same key → exactly one returns the value, 99 return null. Run against both InMemory and MockRedis subclasses.

### HAIP migration (PR 6)

- [ ] T057 [US3] Edit `src/Services/Sorcha.Haip.Service/Services/NonceStore.cs:60-79` to inject `IAtomicDistributedCache` instead of (or in addition to) `IDistributedCache`; rewrite `ConsumeAsync` to a single `_atomicCache.GetAndRemoveAsync(key, ct)` call returning `value != null`. Remove the existing in-memory `ConcurrentDictionary` fallback (now lives in `InMemoryAtomicDistributedCache`)
- [ ] T058 [US3] Edit `src/Services/Sorcha.Haip.Service/Services/PreAuthCodeStore.cs` to make the same migration as T057 — inject `IAtomicDistributedCache`, rewrite `ConsumeAsync` to single round-trip `GetAndRemoveAsync`
- [ ] T059 [US3] Audit `src/Services/Sorcha.Haip.Service/Services/PresentationRequestStore.cs` for terminal-state-write CAS pattern. If already CAS-correct (uses `IDatabase.StringSetAsync(when: When.Equal)` or similar), add a comment justifying the existing implementation and a test asserting the CAS behaviour. If not CAS-correct, migrate the terminal-state writes to `_atomicCache.TryUpdateIfMatchAsync`
- [ ] T060 [US3] Edit `src/Services/Sorcha.Haip.Service/Program.cs` (or `Extensions/ServiceCollectionExtensions.cs`) to call `services.AddAtomicDistributedCache(builder.Configuration, "Haip")` before any HAIP store registration; remove the old `IDistributedCache` injection if no longer used elsewhere in HAIP
- [ ] T061 [P] [US3] Write `tests/Sorcha.Haip.Service.Tests/Services/NonceStoreConcurrentConsumeTests.cs` — 100 concurrent consumers of the same nonce → exactly one success; uses InMemoryAtomicDistributedCache so no Redis dependency
- [ ] T062 [P] [US3] Write `tests/Sorcha.Haip.Service.Tests/Services/PreAuthCodeStoreConcurrentConsumeTests.cs` — same shape as T061
- [ ] T063 [P] [US3] Write `tests/Sorcha.Haip.Service.Tests/Services/PresentationRequestStoreTerminalRaceTests.cs` — two callbacks racing to terminal-state, one wins, other observes terminal state and no-ops
- [ ] T064 [P] [US3] Create `src/Services/Sorcha.Haip.Service/Services/HaipNonceMetrics.cs` — uses `IMeterFactory.Create("Sorcha.Haip.Nonces")` to register the `sorcha_haip_nonce_consume_total` counter (tags `store ∈ {nonce, preauth, presentation}`, `outcome ∈ {success, miss}`); injected into `NonceStore`, `PreAuthCodeStore`, `PresentationRequestStore` consume sites. Add `metrics.AddMeter("Sorcha.Haip.Nonces")` to the OpenTelemetry meter registration
- [ ] T065 [P] [US3] Update `src/Services/Sorcha.Haip.Service/README.md` documenting the atomic-cache migration and the closed TOCTOU gap

**Checkpoint**: PRs 5 and 6 mergeable in sequence after PR 1. After PR 6, HAIP nonces and pre-auth codes are race-condition-free.

---

## Phase 6: User Story 4 — Cross-backend contract tests prevent drift (Priority: P2)

**Goal**: Add the contract-test base + per-implementation subclass pattern for the audited stores not already covered by US2/US3 phases — namely `IInstanceStore`, `IActionStore`, `IWalletRepository`. (Register already has the pattern; US2 covers `IVerifiedTransactionQueue`; US3 covers `IAtomicDistributedCache`.)

**Independent Test**: Modify a method on `InMemoryInstanceStore` without touching `EfCoreInstanceStore`. Run `dotnet test --filter "ContractTests"`. The `EfCoreInstanceStoreContractTests` fixture passes; `InMemoryInstanceStoreContractTests` fails with a clear assertion naming the divergence.

This phase ships interleaved with PR 4 (Blueprint adoption) for the Blueprint contract tests and as a standalone PR for the Wallet contract tests. Tasks below are written as standalone work items so they can also ship as a separate PR if Blueprint's PR 4 stays focused.

### IInstanceStore contract tests

- [ ] T066 [P] [US4] Write `tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/IInstanceStoreContractTests.cs` abstract base covering all `IInstanceStore` operations including the read-only-mirror guard (Feature 106): CreateAsync round-trip, UpdateAsync version concurrency throws on stale version, UpdateAsync on read-only-mirror throws, CreateMirrorAsync sets IsReadOnlyMirror, UpdateMirrorAsync increments version, GetByBlueprintAsync filters correctly, GetByRegisterAsync filters correctly, GetByParticipantWalletAsync handles multi-participant instances, GetPendingActionsByWalletAsync returns assigned-participant actions, DeleteAsync, CountAsync, CountByStateAsync
- [ ] T067 [P] [US4] Convert existing `tests/Sorcha.Blueprint.Service.Tests/Storage/InMemoryInstanceStoreTests.cs` into `InMemoryInstanceStoreContractTests.cs` subclass of T066 base; preserve any bespoke in-memory-only tests as additional test methods within the subclass
- [ ] T068 [P] [US4] Write `tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/EfCoreInstanceStoreContractTests.cs` subclass using Testcontainers Postgres pattern from `Sorcha.Auth.IntegrationTests`; runs migrations and exercises the contract suite

### IActionStore contract tests

- [ ] T069 [P] [US4] Write `tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/IActionStoreContractTests.cs` abstract base covering `IActionStore` operations
- [ ] T070 [P] [US4] Convert existing `tests/Sorcha.Blueprint.Service.Tests/Storage/InMemoryActionStoreTests.cs` into `InMemoryActionStoreContractTests.cs` subclass of T069 base
- [ ] T071 [P] [US4] Write `tests/Sorcha.Blueprint.Service.Tests/Storage/Contracts/EfCoreActionStoreContractTests.cs` subclass using Testcontainers Postgres

### IWalletRepository contract tests

- [ ] T072 [P] [US4] Write `tests/Sorcha.Wallet.Service.Tests/Repositories/Contracts/IWalletRepositoryContractTests.cs` abstract base covering `IWalletRepository` operations including HD-derivation-path lookups, address-indexed queries, paged listing, version concurrency
- [ ] T073 [P] [US4] Convert existing `InMemoryWalletRepository`-using tests in `tests/Sorcha.Wallet.Service.Tests/` into an `InMemoryWalletRepositoryContractTests.cs` subclass of T072 base; preserve bespoke tests
- [ ] T074 [P] [US4] Write `tests/Sorcha.Wallet.Service.Tests/Repositories/Contracts/EfCoreWalletRepositoryContractTests.cs` subclass using Testcontainers Postgres

### Documentation

- [ ] T075 [P] [US4] Add a section to `CLAUDE.md` under "Critical Patterns" describing the cross-backend contract-test pattern: abstract base + per-implementation subclass; reference `RegisterRepositoryContractTests` as the prior art and the new contract suites as the established pattern

**Checkpoint**: After this phase, every audited storage interface has cross-backend contract tests. Drift between in-memory and persistent paths fails the build before merge.

---

## Phase 7: User Story 5 — Operators can observe storage state and mempool depth (Priority: P3)

**Goal**: Ship the cross-cutting observability artefacts that depend on the metrics emitted in Phases 2/4/5 — operator dashboard guidance, alert rules, n1 deployment notes.

**Independent Test**: Boot any service post-feature, open the Aspire dashboard at `http://localhost:18888`, navigate to Metrics, and confirm all five instrument families (`sorcha_storage_provider_info`, `sorcha_storage_fallback_active`, `sorcha_validator_mempool_size`, `sorcha_validator_mempool_lease_expired_total`, `sorcha_haip_nonce_consume_total`) appear with the expected tags.

The instrument registration itself happens in Phases 2/4/5 (close to the code that emits them). This phase ships the observability layer above the metrics.

- [ ] T076 [P] [US5] Write a sample OTLP-backend alert rules document at `docs/observability/alerts-storage-durability.md` (format-agnostic — describes the alert intent and PromQL/MetricsQL/SignalFlow equivalents for downstream operators to implement against whatever metrics backend the deployment uses) covering: any `sorcha_storage_fallback_active > 0` for >5min in Staging/Production, `sorcha_validator_mempool_lease_expired_total` rate > 0 (lease leak), `sorcha_haip_nonce_consume_total{outcome="miss"}` rate spike (replay-attempt indicator)
- [ ] T077 [P] [US5] Add a section to `docs/observability/README.md` (create if absent) documenting the five OpenTelemetry instruments emitted by this feature, the meter source names (`Sorcha.Storage`, `Sorcha.Validator.Mempool`, `Sorcha.Haip.Nonces`), their cardinality, the Aspire-dashboard navigation path, and the alert rules from T076
- [ ] T078 [P] [US5] Write `tests/Sorcha.ServiceDefaults.Tests/Storage/StorageMetricsExposureTests.cs` — uses `MetricCollector<long>` from `Microsoft.Extensions.Diagnostics.Testing` (or `IMeterFactory` test harness) to capture observations from the `Sorcha.Storage` meter, assert `sorcha_storage_provider_info` and `sorcha_storage_fallback_active` are emitted with correct tags after a `WebApplicationFactory<Program>` boot
- [ ] T079 [P] [US5] Update n1 deployment manifest commentary in `scripts/n1-deploy.ps1` and any related env-var docs to mention `ConnectionStrings__Sorcha__Redis`, `ConnectionStrings__Validator__Redis` (cascade override for validator's mempool), the `Storage:AllowInMemoryInProduction` bypass flag, and the `OTEL_EXPORTER_OTLP_ENDPOINT` config used by `Sorcha.ServiceDefaults` to forward metrics

**Checkpoint**: All metrics observable, alertable, and documented for operations.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation sweeps, MASTER-TASKS updates, MEMORY.md entries, sorcha-architecture skill updates, and quickstart validation. Lands as the final PR (9 in the rollout, or folded into PR 8 if the diff is small).

- [ ] T080 [P] Update `CLAUDE.md` § Critical Patterns: add new entry "Storage registration must go through `IStorageRegistrationLog`. Audited interfaces fail-fast in Production." with example code snippet
- [ ] T081 [P] Update `.claude/skills/sorcha-architecture/SKILL.md` § Validator Key Roster: append a "Mempool durability (Feature 113)" subsection summarising the lease pattern, Redis key layout, and HA-replica deployment shape
- [ ] T082 [P] Update `.claude/skills/sorcha-architecture/SKILL.md`: add new "Storage Provider Audit (Feature 113)" section pointing at the registration-log helper, audited interface set, and `Storage:AllowInMemoryInProduction` bypass
- [ ] T083 [P] Update `MEMORY.md > Key Discoveries`: add short entry on the warn-on-fallback pattern and the lease-shaped mempool contract — surface for future-Claude in unrelated sessions
- [ ] T084 [P] Update `MEMORY.md > Active Work`: bump "Feature 113" status from in-progress to done after final PR merges; remove from the active list
- [ ] T085 [P] Update `.specify/MASTER-TASKS.md` to mark Feature 113 phases complete; archive to `MASTER-TASKS-ARCHIVE.md` if the milestone is closed
- [ ] T086 [P] Update `docs/reference/development-status.md` — bump validator durability and HAIP race-protection status entries
- [ ] T087 [P] Update `docs/reference/API-DOCUMENTATION.md` — note the new `Storage:AllowInMemoryInProduction` configuration knob and the `storage-providers` health check
- [ ] T088 Run `specs/113-storage-durability-audit/quickstart.md` end-to-end on a clean checkout against n1; record any deviations as follow-up tasks; update quickstart.md if any commands need adjustment based on real execution
- [ ] T089 Verify all eight rollout PRs landed in sequence; no PR depends on a later PR for its own tests to pass (SC-007)
- [ ] T090 Sweep for any remaining `Serilog.Log.Logger.Warning("...in-memory storage — data will be lost on restart")` style log lines that bypass the new registration log; replace with the canonical helper call

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies. Can start immediately. Required by all later phases (new csprojs need to exist).
- **Phase 2 (Foundational)**: Depends on Phase 1. **Blocks all user-story phases.** This is PR 1 of the rollout.
- **Phase 3 (US1)**: Depends on Phase 2. Three independent service-adoption PRs (PR 2 Wallet, PR 3 Register, PR 4 Blueprint) — can land in any order or in parallel.
- **Phase 4 (US2)**: Depends on Phase 2 (audited list + registration log). Two PRs in sequence: PR 7 (contract reshape) → PR 8 (Redis backing). Independent of US1 and US3.
- **Phase 5 (US3)**: Depends on Phase 2 (audited list + registration log). Two PRs in sequence: PR 5 (atomic-cache infra) → PR 6 (HAIP migration). Independent of US1 and US2.
- **Phase 6 (US4)**: Depends on Phase 3 PR 4 (Blueprint contract tests share the PR-4 surface) for T066–T071, and Phase 3 PR 2 (Wallet) for T072–T074. Standalone otherwise. The IVerifiedTransactionQueue and IAtomicDistributedCache contract tests in Phases 4 and 5 already satisfy US4 for those interfaces.
- **Phase 7 (US5)**: Depends on Phase 2 metrics (T012/T013), Phase 4 metrics (T042), Phase 5 metrics (T064). Documentation can start earlier and be revised as metrics ship.
- **Phase 8 (Polish)**: Depends on all earlier phases. Final PR.

### User Story Dependencies

- **US1**: Depends only on Phase 2. Three services adopt independently.
- **US2**: Depends only on Phase 2. PRs 7 + 8 in sequence.
- **US3**: Depends only on Phase 2. PRs 5 + 6 in sequence.
- **US4**: Implementation depends on whatever Ef* store it tests — for `IInstanceStore`/`IActionStore` that's Blueprint Service (Phase 3 PR 4); for `IWalletRepository` that's Wallet Service (Phase 3 PR 2). Tasks T066–T074 can land in PR 4 / PR 2 respectively, or as a follow-up PR 4b / 2b.
- **US5**: Metrics ship with the work that produces them (Phases 2/4/5). Dashboard/alert artefacts can ship at any time after the metrics they reference are emitted.

### Within Each Phase

- Contract test bases (T066, T069, T072) can be written in parallel — different files, different stores.
- Ef* contract test fixtures depend on Testcontainers Postgres setup; can run in parallel since each fixture has its own container.
- Lua-smoke tests are independent of the contract suite; they exercise the script directly against real Redis.

### Parallel Opportunities

- All Phase 1 csproj creation tasks marked [P] can run in parallel (T001–T005).
- Phase 2 metrics (T012, T013) and tests (T015–T017) parallel after T006–T011 land.
- Phase 3 service-adoption work runs in parallel across Wallet, Register, Blueprint (T018+T019+T020+T021 vs T022+T023+T024 vs T025+T026+T027).
- Phase 4 contract tests (T035, T036) parallel with implementation (T031, T032). Phase 5 contract tests (T052, T053, T054) parallel with implementation (T049, T050).
- Phase 6 contract test bases (T066, T069, T072) all parallel.
- Phase 8 documentation tasks (T080–T087) all parallel.

---

## Parallel Example: Phase 2 Foundational

```bash
# After T006–T011 land (interface, log impl, health check, enforcement helper, hosted service):

# Run tests in parallel:
dotnet test tests/Sorcha.ServiceDefaults.Tests/Storage/StorageRegistrationLogTests.cs &
dotnet test tests/Sorcha.ServiceDefaults.Tests/Storage/StorageRegistrationEnforcementTests.cs &
dotnet test tests/Sorcha.ServiceDefaults.Tests/Storage/StorageProvidersHealthCheckTests.cs &
wait
```

## Parallel Example: Phase 3 User Story 1

```bash
# After Phase 2 lands as PR 1:

# Three developers (or three Claude sessions) work in parallel:
# Developer A — Wallet adoption (PR 2): T018, T019, T020, T021
# Developer B — Register adoption (PR 3): T022, T023, T024
# Developer C — Blueprint adoption (PR 4): T025, T026, T027

# All three PRs are independently mergeable in any order.
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup (T001–T005) — empty new projects exist.
2. Complete Phase 2: Foundational (T006–T017) — registration log + fail-fast + health check shipped as PR 1.
3. Complete Phase 3: US1 (T018–T029) — three service-adoption PRs (Wallet, Register, Blueprint).
4. **STOP and VALIDATE**: Run quickstart Scenario A (US1). Misconfigured Production deploy fails fast. **MVP delivered.**
5. Deploy to n1; verify `storage-providers` health check reports `Healthy` for all three services.

### Incremental Delivery After MVP

6. **US2 (validator durability)**: PR 7 (contract reshape, no behavioural change) → PR 8 (Redis backing flip). Validate Scenario B from quickstart.
7. **US3 (HAIP TOCTOU closure)**: PR 5 (atomic-cache infra) → PR 6 (HAIP migration). Validate Scenario C.
8. **US4 (contract tests for remaining interfaces)**: T066–T075 either folded into PR 4 / PR 2 or as a follow-up. Validate Scenario D.
9. **US5 (observability)**: T076–T079. Validate Scenario E.
10. **Polish (T080–T090)**: Final docs sweep PR.

Each step is independently deployable and revertable. Steps 6/7/8/9 can re-order or run in parallel after Phase 2 lands.

### Parallel Team Strategy

With three developers (or three Claude sessions) after Phase 2:

- Developer A (or session A): US1 service-adoptions sequentially or in parallel (PRs 2, 3, 4).
- Developer B (or session B): US2 (PRs 7, 8) — sequential because PR 8 depends on PR 7's contract reshape.
- Developer C (or session C): US3 (PRs 5, 6) — sequential because PR 6 depends on PR 5's atomic-cache infra.

After all three streams land, contract tests for Wallet/Blueprint (US4) and observability docs (US5) ship as a final consolidation PR.

---

## Notes

- `[P]` tasks operate on different files with no incomplete-task dependencies; safe for parallel execution.
- `[Story]` label maps tasks to spec.md user stories for traceability.
- Each user story (US1, US2, US3) is independently completable and testable per spec.md acceptance criteria.
- The eight-PR rollout from the plan maps to: PR 1 (Phase 2), PR 2/3/4 (Phase 3 — US1), PR 5/6 (Phase 5 — US3), PR 7/8 (Phase 4 — US2). Final docs polish is PR 9 (Phase 8).
- Pre-existing flaky test issues per `MEMORY.md > Pre-existing Test Issues` (`Blueprint.Service.Tests` constructor NRE, `Validator.Service.Tests` compile errors, `link-check` workflow, `nuget-ci`) must be filtered around in test runs and merged with `--admin` where applicable; this feature does not own those fixes.
- Commit after each task or each [Story]-grouped logical chunk. Reference task IDs in commit messages: `feat(storage): [T018] Wallet adopts storage registration log`.
- After every PR merge, re-verify `sorcha_storage_fallback_active` reports 0 for all audited interfaces in n1 (SC-008).
