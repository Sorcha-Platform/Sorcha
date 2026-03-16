# Entity Framework Core Workflows

## Contents
- Migration Workflow
- Testing with InMemory
- Production Deployment
- Troubleshooting

---

## Migration Workflow

### Creating a New Migration

```bash
# 1. Make changes to entity classes or DbContext configuration

# 2. Generate migration (from solution root)
dotnet ef migrations add AddWalletTags \
    --project src/Core/Sorcha.Wallet.Core \
    --startup-project src/Services/Sorcha.Wallet.Service

# 3. Review generated migration in Migrations/ folder

# 4. Apply locally
dotnet ef database update \
    --project src/Core/Sorcha.Wallet.Core \
    --startup-project src/Services/Sorcha.Wallet.Service
```

### Migration Checklist

Copy this checklist and track progress:
- [ ] Entity/DbContext changes complete
- [ ] Migration generated with `dotnet ef migrations add`
- [ ] Review migration SQL (use `--verbose` flag)
- [ ] Test migration on local database
- [ ] Test rollback: `dotnet ef database update PreviousMigration`
- [ ] Commit migration files to source control

### Automatic Migrations on Startup

Sorcha applies migrations automatically in development:

```csharp
// src/Services/Sorcha.Tenant.Service/Data/DatabaseInitializer.cs
public async Task InitializeAsync()
{
    if (_context.Database.IsInMemory())
    {
        await _context.Database.EnsureCreatedAsync();
    }
    else
    {
        var pending = await _context.Database.GetPendingMigrationsAsync();
        if (pending.Any())
        {
            _logger.LogInformation("Applying {Count} pending migrations", pending.Count());
            await _context.Database.MigrateAsync();
        }
    }
}
```

### Rolling Back Migrations

```bash
# Rollback to specific migration
dotnet ef database update InitialCreate --project src/Core/Sorcha.Wallet.Core

# Remove last migration (if not applied)
dotnet ef migrations remove --project src/Core/Sorcha.Wallet.Core
```

---

## Testing with InMemory Provider

### Test DbContext Factory

```csharp
// tests/Sorcha.Tenant.Service.Tests/Helpers/InMemoryDbContextFactory.cs
public static class InMemoryDbContextFactory
{
    public static TenantDbContext Create(string? databaseName = null)
    {
        databaseName ??= Guid.NewGuid().ToString(); // Isolation per test
        
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;
        
        var context = new TenantDbContext(options, "public");
        context.Database.EnsureCreated();
        return context;
    }
}
```

### Repository Test Pattern

```csharp
public class WalletRepositoryTests : IDisposable
{
    private readonly WalletDbContext _context;
    private readonly EfCoreWalletRepository _repository;

    public WalletRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<WalletDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new WalletDbContext(options);
        _repository = new EfCoreWalletRepository(_context);
    }

    [Fact]
    public async Task GetByAddressAsync_ExistingWallet_ReturnsWallet()
    {
        // Arrange
        var wallet = new WalletEntity { Address = "test-address", Owner = "owner" };
        _context.Wallets.Add(wallet);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByAddressAsync("test-address");

        // Assert
        result.Should().NotBeNull();
        result!.Owner.Should().Be("owner");
    }

    public void Dispose() => _context.Dispose();
}
```

### InMemory Limitations

| Feature | InMemory Support | Alternative |
|---------|-----------------|-------------|
| JSONB queries | ❌ | Use SQLite for integration tests |
| Query filters | ✅ | Works correctly |
| Transactions | ❌ | Mocked at service layer |
| Raw SQL | ❌ | Avoid in unit tests |
| Computed columns | ❌ | Test at integration level |

---

## Production Deployment

### Migration Bundle (Recommended)

```bash
# Build self-contained migration executable
dotnet ef migrations bundle \
    --project src/Core/Sorcha.Wallet.Core \
    --startup-project src/Services/Sorcha.Wallet.Service \
    --output ./artifacts/efbundle.exe

# Execute in production
./efbundle.exe --connection "Host=prod;Database=sorcha;..."
```

### Docker Deployment Pattern

```csharp
// Wait for database availability before migrating
public async Task WaitForDatabaseAsync(int maxRetries = 30)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            await _context.Database.CanConnectAsync();
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Database not ready, retry {Attempt}/{Max}", i + 1, maxRetries);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
    throw new InvalidOperationException("Database connection timeout");
}
```

### Production Checklist

Copy this checklist and track progress:
- [ ] Test migrations on staging with production data copy
- [ ] Backup database before migration
- [ ] Deploy migration bundle (not auto-migrate in production)
- [ ] Monitor for migration completion
- [ ] Verify application connectivity
- [ ] Test critical paths after deployment

---

## Troubleshooting

### Connection Failures

**Symptom:** `Npgsql.NpgsqlException: Failed to connect`

**Diagnosis:**
```csharp
// Check connection string format
// PostgreSQL: "Host=;Database=;Username=;Password="
// NOT: "Server=" (SQL Server format)
```

**Solution:** Ensure Npgsql connection string format. See the **postgresql** skill.

### Migration Conflicts

**Symptom:** `The migration has already been applied`

**Fix workflow:**
1. Check `__EFMigrationsHistory` table
2. Remove conflicting migration: `dotnet ef migrations remove`
3. Regenerate with new name

### Query Filter Not Applied

**Symptom:** Soft-deleted records appearing in queries

**Diagnosis:**
```csharp
// Check if IgnoreQueryFilters was called
var deleted = await _context.Wallets.IgnoreQueryFilters().ToListAsync();
```

**Solution:** Ensure filter is configured in `OnModelCreating`:
```csharp
entity.HasQueryFilter(e => e.DeletedAt == null);
```

### Slow SaveChanges with Large Change Sets

**Symptom:** `SaveChangesAsync` taking multiple seconds

**Diagnosis:**
```csharp
var trackedCount = _context.ChangeTracker.Entries().Count();
_logger.LogDebug("Tracked entities: {Count}", trackedCount);
```

**Solution:** Use `ExecuteUpdateAsync` for bulk operations or batch saves:
```csharp
// Batch insert
await _context.BulkInsertAsync(entities); // Requires EF Extensions

// Or chunk manually
foreach (var chunk in entities.Chunk(100))
{
    _context.AddRange(chunk);
    await _context.SaveChangesAsync();
    _context.ChangeTracker.Clear();
}