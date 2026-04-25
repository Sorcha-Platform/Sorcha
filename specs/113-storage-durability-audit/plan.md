# Implementation Plan: Storage Provider Audit and Validator Mempool Durability

**Branch**: `113-storage-durability-audit` | **Date**: 2026-04-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/113-storage-durability-audit/spec.md`

## Summary

Eliminate silent in-memory fallbacks in containerised Sorcha deployments by
adding a startup-time storage registration log that warns on every in-memory
registration and fails-fast in Production/Staging for an explicit set of
audited interfaces. Replace the validator's per-process `ConcurrentDictionary`
mempool (`VerifiedTransactionQueue`) with a Redis-Sorted-Set backed
implementation behind a tightened `Claim`/`Confirm`/`Release` lease contract,
unlocking restart durability and HA-replica failover. Close the documented
TOCTOU gap in HAIP nonce consumption by introducing an `IAtomicDistributedCache`
shim over `StackExchange.Redis.IDatabase` that exposes `GETDEL` and
compare-and-set primitives. Add cross-backend contract tests for the audited
store interfaces so in-memory and persistent paths cannot drift silently.

Eight independently-mergeable PRs in sequence. No new infrastructure
dependencies. No behavioural change for end users.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: .NET Aspire 13, StackExchange.Redis (existing
`IConnectionMultiplexer`), `IDistributedCache` Redis provider, EF Core 10,
MongoDB.Driver, FluentValidation, OpenTelemetry, prometheus-net (existing in
ServiceDefaults)
**Storage**: PostgreSQL (Wallet, Blueprint, Tenant), MongoDB (Register), Redis
(cache, streams, new mempool + atomic cache), all already provisioned in every
deployment
**Testing**: xUnit 3.2.2, FluentAssertions 8.8.0, Moq 4.20.72,
`Sorcha.Testing.MockRedisBuilder`, Testcontainers (Postgres + Redis) — pattern
established in `Sorcha.Auth.IntegrationTests` and `Sorcha.Peer.Service.Integration.Tests`
**Target Platform**: Linux containers, .NET Aspire AppHost local + n1
production
**Project Type**: Microservices monorepo (`src/Services/`, `src/Core/`,
`src/Common/`, `src/Apps/`, `tests/`)
**Performance Goals**: Validator mempool claim under 5ms p95 against local
Redis. Storage registration log + fail-fast adds <50ms to service startup
(one-time cost). Atomic cache GETDEL is one round-trip.
**Constraints**: No new infrastructure. No end-user behavioural change. Each
PR independently mergeable and revertable. Eight-PR sequence cannot stretch
beyond a single development cycle. Pre-existing flaky test classes
(`Blueprint.Service.Tests` constructor NRE, `Validator.Service.Tests` compile
errors) must be filtered around, not fixed by this feature.
**Scale/Scope**: 6 audited interfaces, 5 services touched (Wallet, Register,
Blueprint, Validator, HAIP), ~5 contract test bases with 10–12 subclass
fixtures total, 5 Prometheus metric series, 1 new shared helper in
`Sorcha.ServiceDefaults`, 1 new common project (`Sorcha.AtomicCache` or shim
inside `Sorcha.ServiceDefaults`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Sorcha Project Constitution v1.1.0 — eight principles. Pre-Phase-0 evaluation:

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | Microservices-First Architecture | ✅ Pass | All changes are within-service or in `Sorcha.ServiceDefaults` (shared helper). No new upward dependencies; Core layers untouched. |
| II | Security First | ✅ Pass + reinforces | Closes documented HAIP TOCTOU gap (replay protection). No new secret-handling. No new external boundaries. |
| III | API Documentation | ✅ N/A | No new public REST/gRPC endpoints. The atomic-cache and mempool interfaces are internal contracts; XML docs required per V (covered there). |
| IV | Testing Requirements | ✅ Pass + reinforces | Cross-backend contract tests are the central deliverable of Axis (iv). New code targets >85% per the existing standard. xUnit primary framework. |
| V | Code Quality | ✅ Pass | C# 14 / .NET 10, async/await, DI throughout, nullable reference types stay on, no compiler warnings. |
| VI | Blueprint Creation Standards | ✅ N/A | No blueprint changes. |
| VII | Domain-Driven Design | ✅ Pass | No domain-model changes. Lease and registration-record are infrastructure types, not domain types. |
| VIII | Observability by Default | ✅ Pass + reinforces | Five new Prometheus metric families. Structured logging (no string interpolation). Health check `storage-providers` joins existing endpoints. |

**Result**: PASS. No violations. Complexity Tracking section can be omitted.

**Re-evaluation after Phase 1 design**: see post-design check at the end of
this document.

## Project Structure

### Documentation (this feature)

```text
specs/113-storage-durability-audit/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 — decisions and rationale
├── data-model.md        # Phase 1 — entities, lease shape, registration record
├── quickstart.md        # Phase 1 — smoke-test walkthrough
├── contracts/           # Phase 1 — interface contracts
│   ├── IStorageRegistrationLog.cs
│   ├── IVerifiedTransactionQueue.cs
│   ├── IAtomicDistributedCache.cs
│   └── verified-transaction-queue.lua
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 — created by /speckit.tasks (not /speckit.plan)
```

### Source Code (repository root)

The Sorcha repository is a microservices monorepo. This feature touches
specific services and one shared infrastructure project. Layout for the
work:

```text
src/
├── Common/
│   ├── Sorcha.ServiceDefaults/                  # Shared startup helpers — registration log + fail-fast helper added here
│   │   └── Storage/
│   │       ├── IStorageRegistrationLog.cs       (new)
│   │       ├── StorageRegistrationLog.cs        (new)
│   │       ├── StorageProvidersHealthCheck.cs   (new)
│   │       └── EnforcePersistentStorageInProduction.cs (new)
│   ├── Sorcha.AtomicCache/                      # New common project (or folder inside ServiceDefaults — see research.md)
│   │   ├── IAtomicDistributedCache.cs           (new)
│   │   ├── RedisAtomicDistributedCache.cs       (new)
│   │   └── InMemoryAtomicDistributedCache.cs    (new)
│   └── Sorcha.Storage.Abstractions/             # No changes
├── Core/
│   ├── Sorcha.Wallet.Core/                      # Repositories — no changes (registrations live in Wallet.Service)
│   ├── Sorcha.Register.Storage/                 # No changes
│   └── Sorcha.Register.Storage.MongoDB/         # No changes
└── Services/
    ├── Sorcha.Wallet.Service/                   # PR 2 — adopt registration log
    ├── Sorcha.Register.Service/                 # PR 3 — adopt registration log
    ├── Sorcha.Blueprint.Service/                # PR 4 — adopt registration log + IInstanceStore/IActionStore contract tests
    ├── Sorcha.Haip.Service/                     # PR 6 — migrate to IAtomicDistributedCache
    │   └── Services/
    │       ├── NonceStore.cs                    (rewrite ConsumeAsync)
    │       ├── PreAuthCodeStore.cs              (rewrite ConsumeAsync)
    │       └── PresentationRequestStore.cs      (audit; tighten if needed)
    └── Sorcha.Validator.Service/
        └── Storage/                             (new folder)
            ├── IVerifiedTransactionQueue.cs     (moved + reshaped — PR 7)
            ├── InMemoryVerifiedTransactionQueue.cs (renamed — PR 7)
            ├── RedisVerifiedTransactionQueue.cs (new — PR 8)
            └── Lua/
                └── claim-and-release.lua        (new — PR 8)

tests/
├── Sorcha.ServiceDefaults.Tests/                # New — registration log + fail-fast helper
├── Sorcha.AtomicCache.Tests/                    # New — IAtomicDistributedCacheContractTests + 2 subclasses
├── Sorcha.Wallet.Service.Tests/
│   └── Repositories/Contracts/                  # New — IWalletRepositoryContractTests + InMemory + EfCore subclasses
├── Sorcha.Blueprint.Service.Tests/
│   └── Storage/Contracts/                       # New — IInstanceStoreContractTests, IActionStoreContractTests + InMemory + EfCore subclasses
├── Sorcha.Validator.Service.Tests/              # Existing — see Pre-existing Test Issues note
│   └── Storage/Contracts/                       # New — IVerifiedTransactionQueueContractTests + InMemory + Redis subclasses + Testcontainers Lua smoke
└── Sorcha.Haip.Service.Tests/                   # Concurrent-consume race tests
```

**Structure Decision**: This feature follows the existing Sorcha microservices
monorepo layout. The shared infrastructure (`IStorageRegistrationLog`,
`IAtomicDistributedCache`) lives under `src/Common/` so every service can
depend on it without inverting the dependency graph (Constitution principle I).
Service-specific store implementations stay inside their owning service
(`src/Services/`). Test projects mirror the source projects; new contract-test
base classes live under a `Contracts/` subfolder within each test project so
they sit alongside any pre-existing in-memory-only tests for that store.

The choice between adding a new `Sorcha.AtomicCache` common project versus
folding the atomic-cache shim inside `Sorcha.ServiceDefaults` is documented in
research.md (Decision R-04). The plan above lists it as a separate project; if
research concludes folder-inside-ServiceDefaults is cleaner, the layout
collapses by one project but no other change is needed.

## Constitution Check (post-design re-evaluation)

After producing research.md, data-model.md, contracts/, and quickstart.md,
re-evaluating the eight constitution principles:

| # | Principle | Post-design status | Notes |
|---|-----------|--------------------|-------|
| I | Microservices-First Architecture | ✅ Pass | Confirmed: shared helpers live in `Sorcha.ServiceDefaults` (downstream of every service) and `Sorcha.AtomicCache` (new sibling under `src/Common/`). No upward dependencies introduced. R-04 explicitly justifies the new common project to avoid forcing `StackExchange.Redis` into every consumer of `ServiceDefaults`. |
| II | Security First | ✅ Pass | Reinforced. The atomic-cache contract makes the GETDEL pattern explicit, replacing the documented TOCTOU at the call site. CAS via `TryUpdateIfMatchAsync` closes the secondary race in presentation-state transitions. No new external trust boundaries. |
| III | API Documentation | ✅ N/A | No new public REST/gRPC endpoints. The new internal contracts (`IStorageRegistrationLog`, `IVerifiedTransactionQueue`, `IAtomicDistributedCache`) carry XML doc comments per principle V. |
| IV | Testing Requirements | ✅ Pass | Reinforced. Five new contract-test bases with parallel subclass fixtures across InMemory + Persistent. Concurrent-consume race tests for HAIP. Lease-expiry, crash-recovery, and Lua-script smoke tests for the validator mempool. The contract-test pattern raises the floor on parity testing for every audited interface. |
| V | Code Quality | ✅ Pass | Confirmed: C# 14 / .NET 10, async/await on all I/O, DI throughout, nullable reference types stay on. No compiler warnings expected — the new code is greenfield with the existing project's analyzer settings inherited. |
| VI | Blueprint Creation Standards | ✅ N/A | No blueprint changes. |
| VII | Domain-Driven Design | ✅ Pass | Confirmed: `StorageRegistrationRecord`, `VerifiedTransactionLease`, atomic-cache entries are infrastructure types, not domain types. Existing domain language (Register, Transaction, Docket) untouched. |
| VIII | Observability by Default | ✅ Pass | Reinforced. Five new Prometheus metric families documented in data-model.md §9 with cardinality boundaries. Structured logging via `[STORAGE-FALLBACK]` banner field. Health check `storage-providers` joins existing `/health` endpoints. |

**Result**: PASS. No new violations introduced by Phase 1 design. Complexity
Tracking remains empty.

## Complexity Tracking

> Constitution Check passed with no violations at both pre-research and
> post-design checkpoints. This section is intentionally empty.
