# Quickstart: Blueprint Service Persistence & Validator Crash Recovery

**Feature**: 068-blueprint-persistence | **Date**: 2026-03-24

## Implementation Order

### Phase 1: Infrastructure (US6)

1. Create `BlueprintDbContext` with entity configurations
2. Add `sorcha_blueprint` database to AppHost, docker-compose, postgres-init.sql
3. Add auto-migration startup logic (same pattern as Tenant/Wallet)
4. Add InMemory fallback when no connection string
5. Create initial EF Core migration

### Phase 2: Drafts & Templates (US1, US2)

6. Create `EfCoreBlueprintStore` implementing `IBlueprintStore` using `IDbContextFactory`
7. Create `EfCoreTemplateStore` implementing `IDocumentStore<BlueprintTemplate, string>`
8. Update DI registration — swap InMemory for EF Core when connection string present
9. Verify `TemplateSeedService` works with EF Core store (should work unchanged)
10. Write tests for draft persistence across simulated restarts

### Phase 3: Published Blueprint Cache (US3)

11. Create `RedisCachedPublishedBlueprintStore` implementing `IPublishedBlueprintStore`
12. Implement version-aware cache keys (`bp:pub:{id}:v:{version}`)
13. Implement cache miss → register fetch → cache populate flow
14. Wire into DI (always use Redis, register is source of truth)
15. Write tests for cache hit/miss/version-concurrent scenarios

### Phase 4: Instance & Action Persistence (US4)

16. Create `EfCoreActionStore` implementing `IActionStore`
17. Create `EfCoreInstanceStore` implementing `IInstanceStore`
18. Add Redis cache layer for hot instance state (AccumulatedData)
19. Implement register-based state reconstruction on cache miss
20. Write tests for instance persistence and reconstruction

### Phase 5: Validator Reconciliation (US5)

21. Extend `DocketBuildTriggerService.ReconcileGenesisStateAsync` to drain unverified pool
22. Trigger immediate `ValidationEngineService.ProcessRegisterAsync` for monitored registers
23. Write tests for startup reconciliation with pending transactions

## Key Files

| File | Phase | Changes |
|------|-------|---------|
| `src/Services/Sorcha.Blueprint.Service/Data/BlueprintDbContext.cs` | 1 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Data/Entities/*.cs` | 1 | NEW (5 entity classes) |
| `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreBlueprintStore.cs` | 2 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreTemplateStore.cs` | 2 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreActionStore.cs` | 4 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Storage/EfCoreInstanceStore.cs` | 4 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Storage/RedisCachedPublishedBlueprintStore.cs` | 3 | NEW |
| `src/Services/Sorcha.Blueprint.Service/Program.cs` | 1-4 | MODIFY — DI registration |
| `src/Apps/Sorcha.AppHost/AppHost.cs` | 1 | MODIFY — add BlueprintDb |
| `docker-compose.yml` | 1 | MODIFY — add connection string |
| `docker/postgres-init.sql` | 1 | MODIFY — add database |
| `src/Services/Sorcha.Validator.Service/Services/DocketBuildTriggerService.cs` | 5 | MODIFY — add reconciliation |

## Build & Test

```bash
# After each phase:
dotnet build --force
dotnet test --filter "FullyQualifiedName~Blueprint"

# Full suite:
dotnet test
```
