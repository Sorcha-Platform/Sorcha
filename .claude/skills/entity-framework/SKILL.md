---
name: entity-framework
description: |
  Handles Entity Framework Core database access, migrations, and repository patterns for PostgreSQL.
  Use when: Creating DbContext classes, writing migrations, implementing repositories, configuring entity relationships, or optimizing database queries.
allowed-tools: Read, Edit, Write, Glob, Grep, Bash, mcp__context7__resolve-library-id, mcp__context7__query-docs
---

# Entity Framework Core Skill

Sorcha uses EF Core 9+ with PostgreSQL (Npgsql) as the primary relational data store. The codebase uses **service-specific** repositories (each a focused interface + a concrete EF Core implementation — there is no generic `IRepository<T>` base), soft delete via query filters, and automatic migrations on startup.

## Quick Start

### Register DbContext with PostgreSQL

```csharp
// src/Services/Sorcha.Wallet.Service/Extensions/WalletServiceExtensions.cs (AddWalletDatabase)
services.AddDbContext<WalletDbContext>((sp, options) =>
{
    var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource, npgsql =>
    {
        npgsql.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30), errorCodesToAdd: null);
        npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "wallet");
    });
});
```

### Create a Migration

```bash
# From project directory containing DbContext
dotnet ef migrations add InitialSchema --project src/Core/Sorcha.Wallet.Core --startup-project src/Services/Sorcha.Wallet.Service
```

### Clean-start migration convention (pre-release, no data preservation)

This repo is pre-release and does **not** preserve existing data across schema changes. The convention is to **squash all migrations into a single `InitialCreate` per service** rather than accumulate incremental migrations — fold the new schema into the existing `*_InitialCreate.cs` + `.Designer.cs` + `*ModelSnapshot.cs`, keeping the **same migration ID** (don't regenerate the timestamp). Every environment re-creates its DB (`down -v`) to pick up the change. (E.g. `Sorcha.Blueprint.Service` absorbed the F142 RehearsalPass migration and the F145 `+LastAppliedTxId` / `−IsReadOnlyMirror` change into one `…_InitialCreate`.)

Implications:
- A **plain image swap is NOT enough** for a schema change on a *standing* node — `MigrateAsync` sees the `InitialCreate` ID already applied and skips it, so the new column never lands (symptom: `column "X" of relation "Y" does not exist`). Recreate the DB (`down -v`, or drop+recreate the one database) on existing nodes. A fresh-volume node is fine — it applies the full `InitialCreate`.
- **Verify the single migration is in sync with the model** before relying on it (no need to regenerate if clean):

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/Services/Sorcha.Blueprint.Service --startup-project src/Services/Sorcha.Blueprint.Service
# "No changes have been made to the model since the last migration." = in sync, nothing to squash.
```

### Apply Migrations Programmatically

```csharp
// Startup pattern used in Sorcha services
var pending = await dbContext.Database.GetPendingMigrationsAsync();
if (pending.Any())
    await dbContext.Database.MigrateAsync();
```

## Key Concepts

| Concept | Usage | Example |
|---------|-------|---------|
| DbContext | Schema definition + change tracking | `WalletDbContext`, `TenantDbContext` |
| Repository | Data access abstraction | `EFCoreRepository<T, TId, TContext>` |
| Soft Delete | Query filter on `DeletedAt` | `.HasQueryFilter(e => e.DeletedAt == null)` |
| JSONB | PostgreSQL JSON columns | `.HasColumnType("jsonb")` |
| ExecuteUpdate | Bulk updates without loading | `ExecuteUpdateAsync(s => s.SetProperty(...))` |

## Common Patterns

### Repository with Optional Eager Loading

```csharp
// src/Core/Sorcha.Wallet.Core/Repositories/EfCoreWalletRepository.cs
public async Task<WalletEntity?> GetByAddressAsync(string address, bool includeAddresses = false)
{
    IQueryable<WalletEntity> query = _context.Wallets;
    
    if (includeAddresses)
        query = query.Include(w => w.Addresses);
    
    return await query.AsNoTracking().FirstOrDefaultAsync(w => w.Address == address);
}
```

### Soft Delete with Filter Bypass

```csharp
// Bypass query filter for admin operations
var deleted = await _context.Wallets
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(w => w.Address == address);
```

## See Also

- [patterns](references/patterns.md) - DbContext configuration, entity mapping, query optimization
- [workflows](references/workflows.md) - Migration commands, testing patterns, deployment

## Related Skills

- See the **postgresql** skill for connection configuration and PostgreSQL-specific features
- See the **aspire** skill for service registration and health checks
- See the **xunit** skill for testing DbContext with InMemory provider

## Documentation Resources

> Fetch latest EF Core documentation with Context7.

**Library ID:** `/dotnet/entityframework.docs`

**Recommended Queries:**
- "DbContext pooling configuration dependency injection"
- "migrations code-first apply production deployment"
- "query filters soft delete global filters"
- "bulk operations ExecuteUpdate ExecuteDelete"