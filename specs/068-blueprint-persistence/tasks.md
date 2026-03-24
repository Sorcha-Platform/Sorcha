# Tasks: Blueprint Service Persistence & Validator Crash Recovery

**Input**: Design documents from `/specs/068-blueprint-persistence/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: Included — spec requires tests for persistence, cache, and reconstruction.

**Organization**: Tasks grouped by user story. US6 (Infrastructure) is Phase 2 (foundational), not a separate story phase, since all other stories depend on it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Include exact file paths in descriptions

## Phase 1: Setup (Entity Definitions)

**Purpose**: Create EF Core entity classes and enums shared across stories

- [ ] T001 [P] Create `DraftStatus` enum (Draft=0, Archived=1) in `src/Services/Sorcha.Blueprint.Service/Data/Entities/DraftStatus.cs`
- [ ] T002 [P] Create `TemplateSource` enum (Seed=0, UserCreated=1) in `src/Services/Sorcha.Blueprint.Service/Data/Entities/TemplateSource.cs`
- [ ] T003 [P] Create `BlueprintDraftEntity` class with Id, OwnerId, Name, Description, Content (JSONB), OrganizationId, Status, timestamps in `src/Services/Sorcha.Blueprint.Service/Data/Entities/BlueprintDraftEntity.cs`
- [ ] T004 [P] Create `BlueprintDraftAccessEntity` class (schema-only placeholder for future collaboration: Id, DraftId, UserId, AccessLevel, GrantedAt, GrantedBy) in `src/Services/Sorcha.Blueprint.Service/Data/Entities/BlueprintDraftAccessEntity.cs`
- [ ] T005 [P] Create `BlueprintTemplateEntity` class with Id, Name, Description, Category, Content (JSONB), Version, Source, Published, UsageCount, timestamps in `src/Services/Sorcha.Blueprint.Service/Data/Entities/BlueprintTemplateEntity.cs`
- [ ] T006 [P] Create `ActionEntity` class with TransactionHash (PK), WalletAddress, RegisterAddress, Content (JSONB), IdempotencyKey, IdempotencyExpiry, CreatedAt in `src/Services/Sorcha.Blueprint.Service/Data/Entities/ActionEntity.cs`
- [ ] T007 [P] Create `FileMetadataEntity` class with Id, TransactionHash (FK), FileName, ContentType, Size, Content (byte[]) in `src/Services/Sorcha.Blueprint.Service/Data/Entities/FileMetadataEntity.cs`
- [ ] T008 [P] Create `InstanceEntity` class with Id, BlueprintId, BlueprintVersion, RegisterId, State, CurrentActionIds (JSONB), ParticipantWallets (JSONB), FirstTransactionId, LastTransactionId, CompletedActionCount, AccumulatedData (JSONB), ActiveBranches (JSONB), Metadata (JSONB), Version, timestamps in `src/Services/Sorcha.Blueprint.Service/Data/Entities/InstanceEntity.cs`

**Checkpoint**: All entity classes compile

---

## Phase 2: Foundational — Infrastructure Wiring (US6, blocking)

**Purpose**: BlueprintDbContext, auto-migration, AppHost/Docker wiring. MUST complete before any story.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T009 Create `BlueprintDbContext` with entity configurations (indexes, JSONB columns, relationships) in `src/Services/Sorcha.Blueprint.Service/Data/BlueprintDbContext.cs` — configure `blueprint` schema, all indexes from data-model.md
- [ ] T010 Add EF Core NuGet packages (`Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`) to `src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj`
- [ ] T011 Add `BlueprintDb` PostgreSQL database resource to AppHost in `src/Apps/Sorcha.AppHost/AppHost.cs` — add `.WithReference(blueprintDb)` to Blueprint Service
- [ ] T012 [P] Add `sorcha_blueprint` database creation to `docker/postgres-init.sql`
- [ ] T013 [P] Add `ConnectionStrings__BlueprintDb` environment variable to Blueprint Service in `docker-compose.yml`
- [ ] T014 Add conditional DI registration in `src/Services/Sorcha.Blueprint.Service/Program.cs` — if `BlueprintDb` connection string present: register `IDbContextFactory<BlueprintDbContext>` with Npgsql; else: keep in-memory stores with log warning
- [ ] T015 Add auto-migration startup logic in `src/Services/Sorcha.Blueprint.Service/Program.cs` — apply pending migrations on startup using same pattern as Tenant/Wallet services (retry with backoff)
- [ ] T016 Create initial EF Core migration via `dotnet ef migrations add InitialCreate` in Blueprint Service project — verify migration includes all 6 tables (BlueprintDrafts, BlueprintDraftAccess, BlueprintTemplates, Actions, FileMetadata, Instances) with correct indexes and FK relationships
- [ ] T017 Build and verify AppHost starts with Blueprint Service connected to PostgreSQL

**Checkpoint**: Blueprint Service connects to PostgreSQL on startup, schema created, falls back to InMemory without connection string

---

## Phase 3: User Story 1 — Blueprint Draft Persistence (Priority: P1) 🎯 MVP

**Goal**: Drafts survive service restarts, scoped to owner

**Independent Test**: Create draft, restart service, verify draft persists

### Tests for User Story 1

- [ ] T018 [P] [US1] Unit test `EfCoreBlueprintStore` — CRUD operations, owner filtering, persistence across DbContext recreations in `tests/Sorcha.Blueprint.Service.Tests/Storage/EfCoreBlueprintStoreTests.cs`

### Implementation for User Story 1

- [ ] T019 [US1] Create `EfCoreBlueprintStore` implementing `IBlueprintStore` using `IDbContextFactory<BlueprintDbContext>` — map between `BlueprintModel` and `BlueprintDraftEntity`, enforce owner filtering in `GetAllByOrgAsync` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreBlueprintStore.cs`
- [ ] T020 [US1] Update DI registration in Program.cs — register `EfCoreBlueprintStore` as singleton when connection string present, keep `InMemoryBlueprintStore` as fallback in `src/Services/Sorcha.Blueprint.Service/Program.cs`

**Checkpoint**: Drafts persist across restarts, owner-scoped

---

## Phase 4: User Story 2 — Template Library Persistence (Priority: P1)

**Goal**: Templates persist across restarts, seed from JSON only on first run

**Independent Test**: Add template, restart, verify it persists; fresh DB gets seeded templates

### Tests for User Story 2

- [ ] T021 [P] [US2] Unit test `EfCoreTemplateStore` — CRUD, query by category, version-aware upsert in `tests/Sorcha.Blueprint.Service.Tests/Storage/EfCoreTemplateStoreTests.cs`

### Implementation for User Story 2

- [ ] T022 [US2] Create `EfCoreTemplateStore` implementing `IDocumentStore<BlueprintTemplate, string>` using `IDbContextFactory<BlueprintDbContext>` — map between `BlueprintTemplate` model and `BlueprintTemplateEntity` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreTemplateStore.cs`
- [ ] T023 [US2] Update DI registration in Program.cs — register `EfCoreTemplateStore` when connection string present, keep `InMemoryDocumentStore` as fallback in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T024 [US2] Verify `TemplateSeedService` works unchanged with EF Core backing store — version comparison prevents overwriting user edits. No code changes expected, just integration verification.

**Checkpoint**: Templates persist across restarts, seed data populates on first run only

---

## Phase 5: User Story 3 — Published Blueprint Caching (Priority: P1)

**Goal**: Published blueprints cached in Redis from register (source of truth), version-aware keys

**Independent Test**: Publish blueprint, restart service, request it — served from cache or fetched from register

### Tests for User Story 3

- [ ] T025 [P] [US3] Unit test `RedisCachedPublishedBlueprintStore` — cache hit returns cached data, cache miss fetches from register, version-concurrent access in `tests/Sorcha.Blueprint.Service.Tests/Storage/RedisCachedPublishedBlueprintStoreTests.cs`

### Implementation for User Story 3

- [ ] T026 [US3] Create `RedisCachedPublishedBlueprintStore` implementing `IPublishedBlueprintStore` in `src/Services/Sorcha.Blueprint.Service/Storage/RedisCachedPublishedBlueprintStore.cs` — use Redis with version-aware keys (`bp:pub:{blueprintId}:v:{version}`), TTL 15 min. On cache miss, query register via `IRegisterServiceClient.GetTransactionsAsync` filtering by `MetaData.TransactionType == "BlueprintPublish"` and `MetaData.BlueprintId`, then deserialize blueprint payload from transaction. Active instances refresh TTL on access (implicit LRU).
- [ ] T027 [US3] Implement `AddAsync` — cache the already-published blueprint in Redis (does NOT trigger register write — that is handled by `PublishService`). Serialize blueprint JSON, store with version key, set TTL.
- [ ] T028 [US3] Implement `GetVersionAsync` — check Redis first, on miss query register for the specific blueprint version transaction, deserialize payload, populate cache, return result
- [ ] T029 [US3] Implement `GetByRegisterAsync` — query register for published blueprints, cache results
- [ ] T030 [US3] Update DI registration — always use `RedisCachedPublishedBlueprintStore` (register is source of truth, Redis is cache), remove `InMemoryPublishedBlueprintStore` registration in `src/Services/Sorcha.Blueprint.Service/Program.cs`

**Checkpoint**: Published blueprints cached with version awareness, survive restarts via register fetch

---

## Phase 6: User Story 4 — Instance & Action Persistence (Priority: P2)

**Goal**: Actions and instances persist in PostgreSQL, instance execution state cached in Redis

**Independent Test**: Start instance, execute actions, restart, verify state reconstructed

### Tests for User Story 4

- [ ] T031 [P] [US4] Unit test `EfCoreActionStore` — store/retrieve actions, idempotency key lookup, file metadata in `tests/Sorcha.Blueprint.Service.Tests/Storage/EfCoreActionStoreTests.cs`
- [ ] T032 [P] [US4] Unit test `EfCoreInstanceStore` — CRUD, state filtering, participant wallet queries, optimistic concurrency in `tests/Sorcha.Blueprint.Service.Tests/Storage/EfCoreInstanceStoreTests.cs`

### Implementation for User Story 4

- [ ] T033 [US4] Create `EfCoreActionStore` implementing `IActionStore` using `IDbContextFactory<BlueprintDbContext>` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreActionStore.cs` — map `ActionDetailsResponse` to `ActionEntity`, handle file metadata and idempotency keys
- [ ] T034 [US4] Create `EfCoreInstanceStore` implementing `IInstanceStore` using `IDbContextFactory<BlueprintDbContext>` in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` — map `Instance` to `InstanceEntity`, preserve optimistic concurrency via Version field, serialize JSONB fields
- [ ] T035 [US4] Update DI registration — register EF Core stores when connection string present in `src/Services/Sorcha.Blueprint.Service/Program.cs`
- [ ] T035a [US4] Implement cache-miss reconstruction in `EfCoreInstanceStore.GetAsync` — if instance not found in DB (e.g., after data loss), call existing `IStateReconstructionService` to rebuild from register transactions, then persist the reconstructed instance in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs`
- [ ] T035b [US4] Ensure `EfCoreInstanceStore.UpdateAsync` writes AccumulatedData to PostgreSQL on every action execution (FR-011) — no separate Redis cache needed, PostgreSQL is durable and register is ultimate fallback in `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs`
- [ ] T036 [US4] Update existing Blueprint Service tests that depend on in-memory stores to work with either implementation in `tests/Sorcha.Blueprint.Service.Tests/`

**Checkpoint**: Actions and instances persist across restarts, queries work correctly

---

## Phase 7: User Story 5 — Validator Startup Reconciliation (Priority: P2)

**Goal**: After crash, validator drains unverified pool for monitored registers

**Independent Test**: Submit transactions, stop validator, restart, verify transactions processed

### Tests for User Story 5

- [ ] T037 [P] [US5] Unit test reconciliation — mock pool with pending transactions, verify `ProcessRegisterAsync` called on startup in `tests/Sorcha.Validator.Service.Tests/`

### Implementation for User Story 5

- [ ] T038 [US5] Add `ReconcileUnverifiedPoolAsync` method to `DocketBuildTriggerService` — for each monitored register, check unverified pool count, if > 0 trigger `ValidationEngineService.ProcessRegisterAsync` in `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs`
- [ ] T039 [US5] Call `ReconcileUnverifiedPoolAsync` after existing `ReconcileGenesisStateAsync` in `ExecuteAsync` in `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs`
- [ ] T040 [US5] Add structured logging for reconciliation — log monitored register count, pending transaction count per register, processing time in `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs`

**Checkpoint**: Validator processes stranded transactions on startup

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, final verification

- [ ] T041 [P] Update Blueprint Service README with persistence architecture documentation in `src/Services/Sorcha.Blueprint.Service/README.md`
- [ ] T042 [P] Update `docs/reference/development-status.md` with Blueprint Service persistence status change
- [ ] T043 Run full test suite (`dotnet test`) — verify no regressions
- [ ] T044 Build entire solution (`dotnet build --force`) — verify 0 errors, 0 new warnings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — entities can be created immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — MVP
- **US2 (Phase 4)**: Depends on Phase 2 — can parallel with US1
- **US3 (Phase 5)**: Depends on Phase 2 + Redis — can parallel with US1/US2
- **US4 (Phase 6)**: Depends on Phase 2 — can parallel with US1-US3
- **US5 (Phase 7)**: Independent of Blueprint Service phases — only touches Validator Service
- **Polish (Phase 8)**: Depends on all stories complete

### Parallel Opportunities

**Maximum parallelism after Phase 2:**
- US1 + US2 + US3 + US4 (all Blueprint Service stories)
- US5 (Validator — completely independent)

---

## Parallel Example: After Phase 2

```text
Agent 1: US1 — T018-T020 (Draft persistence)
Agent 2: US2 — T021-T024 (Template persistence)
Agent 3: US3 — T025-T030 (Published blueprint cache)
Agent 4: US4 — T031-T036 (Instance/action persistence)
Agent 5: US5 — T037-T040 (Validator reconciliation — independent)
```

---

## Implementation Strategy

### MVP First (US1 + US6 Infrastructure)

1. Phase 1: Entity definitions (T001-T008)
2. Phase 2: Infrastructure wiring (T009-T017)
3. Phase 3: Draft persistence (T018-T020)
4. **STOP and VALIDATE**: Drafts survive restarts

### Incremental Delivery

1. Setup + Foundational → Database connected
2. US1 → Drafts persist (MVP)
3. US2 → Templates persist (no more JSON re-seeding)
4. US3 → Published blueprint cache (register as source of truth)
5. US4 → Instance/action persistence (full durability)
6. US5 → Validator crash recovery (operational resilience)
7. Polish → Docs, final verification

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story
- US6 (Infrastructure) is in Phase 2 (foundational), not a separate story phase
- In-memory implementations are KEPT as fallback — not deleted
- Total: 44 tasks across 8 phases
