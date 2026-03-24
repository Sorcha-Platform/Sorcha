# Research: Blueprint Service Persistence & Validator Crash Recovery

**Feature**: 068-blueprint-persistence | **Date**: 2026-03-24

## R1: Blueprint Store Interface Abstraction

**Decision**: Implement EF Core-backed stores behind existing interfaces — no caller changes needed.

**Rationale**: All four store interfaces (`IBlueprintStore`, `IPublishedBlueprintStore`, `IActionStore`, `IInstanceStore`) are well-abstracted. The in-memory implementations use `ConcurrentDictionary` and return `Task<T>`. EF Core implementations can implement the same interfaces with `DbContext` queries.

**Key Finding**: Stores are registered as **Singletons** in DI. EF Core DbContext is Scoped. The EF Core implementations must use `IDbContextFactory<BlueprintDbContext>` (same pattern as Peer Service) to create short-lived contexts within singleton-scoped stores.

**Alternatives Considered**:
- Change stores to Scoped — rejected (would require updating all consumers)
- Use `IServiceScopeFactory` — rejected (DbContextFactory is the standard pattern for this)

---

## R2: Template Storage Migration Strategy

**Decision**: Use EF Core for templates with seed-on-first-run from JSON files.

**Rationale**: Templates currently use `IDocumentStore<BlueprintTemplate, string>` with `InMemoryDocumentStore`. The `TemplateSeedService` (IHostedService) already implements idempotent seeding — it checks existing version before upserting. This logic remains the same; only the backing store changes from in-memory to EF Core.

**Key Finding**: `TemplateSeedService` uses version comparison — if DB template version >= file version, skip. This naturally prevents overwriting user modifications while allowing version bumps from JSON files.

**Resolution**: Create `EfCoreBlueprintTemplateStore` implementing `IDocumentStore<BlueprintTemplate, string>` backed by `BlueprintDbContext`. `TemplateSeedService` continues unchanged.

---

## R3: Published Blueprint Cache Strategy

**Decision**: Reuse the existing two-level cache pattern from Validator's `BlueprintCache` — L1 in-memory + L2 Redis.

**Rationale**: The Validator Service already has a production-tested `IBlueprintCache` with L1 (ConcurrentDictionary) + L2 (Redis), pub/sub invalidation, Polly resilience, and cache stats. The Blueprint Service should use the same pattern for published blueprint lookups.

**Key Finding**: Cache keys must include blueprint version. Current Validator cache keys use blueprint ID only. Need to extend key format to `blueprint:{blueprintId}:v:{version}` for version-concurrent access.

**Gap**: The Blueprint Service's `IPublishedBlueprintStore` interface is richer than `IBlueprintCache` (it has `GetByRegisterAsync`, `GetVersionsAsync`). The Redis cache backs the hot path (`GetVersionAsync`); the store interface methods that list/query can fall back to register queries.

---

## R4: Instance State Cache Strategy

**Decision**: Redis-backed instance state cache with register-based reconstruction on miss.

**Rationale**: Instance state is already cached in-memory via `InMemoryInstanceStore`. Moving to Redis gives durability across restarts while maintaining fast access. On cache miss, the existing `StateReconstructionService` + `AccumulatedData` pattern rebuilds state from register transactions.

**Key Finding**: `Instance` has optimistic concurrency (Version field). Redis implementation must preserve this — use Redis WATCH/MULTI or conditional SET with version check.

**Simplification**: For MVP, use Redis serialized JSON with TTL for active instances. The `IInstanceStore` interface has complex query methods (`GetByParticipantWalletAsync`, `GetPendingActionsByWalletAsync`) — these can use Redis secondary indexes or fall back to scanning. Alternatively, keep a PostgreSQL-backed `IInstanceStore` for query richness and use Redis only for hot instance state.

**Decision Refined**: Use PostgreSQL for instance metadata (queryable) + Redis for hot execution state (AccumulatedData). The `IInstanceStore` implementation queries PostgreSQL for metadata and merges AccumulatedData from Redis.

---

## R5: Validator Crash Recovery

**Decision**: Extend existing `DocketBuildTriggerService.ReconcileGenesisStateAsync()` to also drain the unverified pool.

**Rationale**: The validator already reconciles genesis state on startup — it queries register heights for all monitored registers. The missing piece is: after confirming heights, poll the unverified pool for pending transactions and trigger validation.

**Key Finding**: `ValidationEngineService` already polls the unverified pool in its main loop. The reconciliation just needs to trigger an immediate poll cycle rather than waiting for the next timer tick.

**Resolution**: Add a reconciliation step after `ReconcileGenesisStateAsync` that signals `ValidationEngineService` to run an immediate batch, or directly call `ProcessRegisterAsync` for each monitored register with pending transactions.

---

## R6: Database Schema Design

**Decision**: Single `BlueprintDbContext` with four entity sets + one future-ready join table.

**Entities**:
- `BlueprintDraftEntity` — drafts with OwnerId, JSON content, status
- `BlueprintTemplateEntity` — templates with category, source, JSON content
- `ActionEntity` — action transactions with wallet/register indexes
- `InstanceEntity` — instance metadata with state, participant wallets (JSONB)
- `BlueprintDraftAccessEntity` — empty table for future delegation (schema only, no logic)

**Schema**: `blueprint` (matches Tenant's `public` and Wallet's `wallet` schema patterns)

**Connection String Key**: `BlueprintDb` (matches Peer's `PeerDb` pattern)
