# Entity Framework Core Patterns

## Contents
- DbContext Configuration
- Entity Configuration
- Repository Patterns
- Query Optimization
- Anti-Patterns

---

## DbContext Configuration

### Schema-Isolated DbContext

Sorcha uses PostgreSQL schemas to isolate tenant data. The `TenantDbContext` accepts a schema parameter:

```csharp
// src/Services/Sorcha.Tenant.Service/Data/TenantDbContext.cs
public class TenantDbContext : DbContext
{
    private readonly string _schema;

    public TenantDbContext(DbContextOptions<TenantDbContext> options, string schema = "public")
        : base(options)
    {
        _schema = schema;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(_schema);
        // Entity configurations...
    }
}
```

### Design-Time Factory for Migrations

Required for `dotnet ef` CLI commands when DbContext needs constructor parameters:

```csharp
// src/Core/Sorcha.Wallet.Core/Data/WalletDbContextFactory.cs
public class WalletDbContextFactory : IDesignTimeDbContextFactory<WalletDbContext>
{
    public WalletDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WalletDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Database=sorcha_wallet;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "wallet"));
        return new WalletDbContext(optionsBuilder.Options);
    }
}
```

---

## Entity Configuration

### Soft Delete with Query Filter

```csharp
// DO: Apply filter at DbContext level
entity.HasQueryFilter(e => e.DeletedAt == null);

// DO: Cascade filter to related entities
entity.HasQueryFilter(e => e.Wallet!.DeletedAt == null);

// DON'T: Check DeletedAt in every query manually
var active = await _context.Wallets.Where(w => w.DeletedAt == null).ToListAsync(); // Redundant!
```

### PostgreSQL JSONB Columns

```csharp
// DO: Use JSONB for flexible metadata
entity.Property(e => e.Metadata)
    .HasColumnType("jsonb")
    .HasDefaultValueSql("'{}'::jsonb");

// DO: Use owned entities for structured JSON
entity.OwnsOne(e => e.Branding, b => b.ToJson());

// DON'T: Store JSON as string and deserialize manually
entity.Property(e => e.Data).HasConversion(
    v => JsonSerializer.Serialize(v),
    v => JsonSerializer.Deserialize<T>(v)); // Loses query support
```

### Enum Storage as String

```csharp
// DO: Store enums as strings for readability
entity.Property(e => e.Status)
    .HasConversion<string>()
    .HasMaxLength(50);

// Result: "Active", "Suspended", "Deleted" in database
```

### Composite and Partial Indexes

```csharp
// Multi-column index for common query patterns
entity.HasIndex(e => new { e.Owner, e.Tenant })
    .HasDatabaseName("IX_Wallets_Owner_Tenant");

// Partial unique index (PostgreSQL-specific)
entity.HasIndex(e => e.ExternalIdpUserId)
    .IsUnique()
    .HasFilter("\"ExternalIdpUserId\" IS NOT NULL");
```

---

## Repository Patterns

### Concrete repository (no generic base)

There is **no** generic `IRepository<T>` / `EFCoreRepository<T>` base in Sorcha. Each service defines a
focused repository interface and a concrete EF Core implementation over its own `DbContext`. The audited
storage interfaces (`IWalletRepository`, `IRegisterRepository`, …) are registered via
`IStorageRegistrationLog` (Feature 113), so the backend choice (EF Core / Mongo / Redis / in-memory) is
visible at startup and fails closed in Production/Staging on an in-memory fallback.

```csharp
// src/Core/Sorcha.Wallet.Core/Repositories/EfCoreWalletRepository.cs
public sealed class EfCoreWalletRepository(WalletDbContext context) : IWalletRepository
{
    // Focused, domain-specific query methods over WalletDbContext — not a generic CRUD surface.
    // (See "Specialized Repository with Eager Loading" below for the include/AsNoTracking idiom.)
}
```

### Specialized Repository with Eager Loading

```csharp
// DO: Use optional parameters for includes
public async Task<Wallet?> GetAsync(string address, bool includeAddresses = false, bool includeTransactions = false)
{
    IQueryable<WalletEntity> query = _context.Wallets;
    
    if (includeAddresses)
        query = query.Include(w => w.Addresses);
    if (includeTransactions)
        query = query.Include(w => w.Transactions.OrderByDescending(t => t.CreatedAt));
    
    return await query.AsNoTracking().FirstOrDefaultAsync(w => w.Address == address);
}

// DON'T: Always load all relationships
return await _context.Wallets
    .Include(w => w.Addresses)
    .Include(w => w.Transactions)
    .Include(w => w.Delegates)
    .FirstOrDefaultAsync(w => w.Address == address); // Over-fetching!
```

---

## Query Optimization

### Bulk Updates Without Loading

```csharp
// DO: Use ExecuteUpdateAsync for single-column updates
await _context.Wallets
    .Where(w => w.Address == address)
    .ExecuteUpdateAsync(s => s.SetProperty(w => w.LastAccessedAt, DateTime.UtcNow));

// DON'T: Load entity just to update one field
var wallet = await _context.Wallets.FindAsync(address);
wallet.LastAccessedAt = DateTime.UtcNow;
await _context.SaveChangesAsync(); // Unnecessary round-trip
```

### Read-Only Queries

```csharp
// DO: Use AsNoTracking for read-only operations
var wallets = await _context.Wallets.AsNoTracking().ToListAsync();

// DON'T: Track entities you won't modify (wastes memory)
var wallets = await _context.Wallets.ToListAsync();
```

---

## Anti-Patterns

### WARNING: N+1 Queries

**The Problem:**

```csharp
// BAD - Executes N+1 queries
var wallets = await _context.Wallets.ToListAsync();
foreach (var wallet in wallets)
{
    var addresses = await _context.WalletAddresses
        .Where(a => a.ParentWalletAddress == wallet.Address).ToListAsync();
}
```

**Why This Breaks:** Each iteration executes a separate database query. With 100 wallets, you execute 101 queries instead of 1-2.

**The Fix:**

```csharp
// GOOD - Single query with Include
var wallets = await _context.Wallets
    .Include(w => w.Addresses)
    .ToListAsync();
```

---

### WARNING: Tracking Entities in Read-Only Scenarios

**The Problem:**

```csharp
// BAD - Tracking 10,000 entities for a report
var allTransactions = await _context.Transactions.ToListAsync();
return allTransactions.Sum(t => t.Amount);
```

**Why This Breaks:** Change tracker holds references to all entities, consuming memory and slowing down SaveChanges.

**The Fix:**

```csharp
// GOOD - Project to scalar, no tracking
var total = await _context.Transactions.SumAsync(t => t.Amount);
```

---

### WARNING: Missing Retry Configuration

**The Problem:**

```csharp
// BAD - No retry for transient failures
options.UseNpgsql(connectionString);
```

**Why This Breaks:** Cloud databases experience transient connection issues. Without retry, a single network blip causes request failure.

**The Fix:**

```csharp
// GOOD - Retry with exponential backoff
options.UseNpgsql(connectionString, npgsql =>
{
    npgsql.EnableRetryOnFailure(
        maxRetryCount: 10,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorCodesToAdd: null);
});