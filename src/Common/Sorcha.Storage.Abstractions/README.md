# Sorcha.Storage.Abstractions

Core storage abstraction interfaces for the Sorcha platform multi-tier storage layer.

## Overview

This library provides a unified abstraction layer for multi-tier storage in the Sorcha decentralised register platform. It implements a three-tier architecture:

- **Hot Tier (Cache)**: Redis-backed high-performance cache for frequently accessed data
- **Warm Tier (Operational)**: PostgreSQL/MongoDB for operational queries and mutable data
- **Cold Tier (WORM)**: MongoDB append-only store for immutable ledger entries

## Key Components

### Interfaces

| Interface | Purpose | Tier |
|-----------|---------|------|
| `ICacheStore` | High-performance key-value cache | Hot |
| `IRepository<TEntity, TId>` | CRUD operations for relational data | Warm |
| `IDocumentStore<TDocument, TId>` | Flexible schema document storage | Warm |
| `IWormStore<TDocument, TId>` | Append-only immutable storage | Cold |
| `IVerifiedCache<TDocument, TId>` | Cache with WORM verification | Hot + Cold |

### Verified Cache

The `IVerifiedCache` combines hot-tier cache performance with cold-tier integrity guarantees:

```csharp
// Register verified cache for dockets
services.AddVerifiedCache<Docket, ulong>(
    idSelector: d => d.Id,
    hashSelector: d => d.Hash);

// Use in your service
public class DocketService
{
    private readonly IVerifiedCache<Docket, ulong> _cache;

    public async Task<Docket?> GetDocketAsync(ulong id)
    {
        // Automatically checks cache, falls back to WORM store
        // Optionally verifies hash integrity
        return await _cache.GetAsync(id);
    }
}
```

### Cache Startup Strategies

Two strategies for warming the cache on service startup:

1. **Blocking**: Service waits until cache is fully warmed (small datasets)
2. **Progressive**: Service starts immediately, cache warms in background (large datasets)

```csharp
services.Configure<VerifiedCacheConfiguration>(config =>
{
    config.StartupStrategy = CacheStartupStrategy.Progressive;
    config.BlockingThreshold = 1000; // Only block for first 1000 entries
    config.WarmingBatchSize = 100;
});
```

## Installation

```xml
<PackageReference Include="Sorcha.Storage.Abstractions" Version="1.0.0" />
```

## Configuration

### appsettings.json

```json
{
  "Storage": {
    "Hot": {
      "Provider": "Redis",
      "ConnectionString": "localhost:6379",
      "DefaultTtlSeconds": 3600
    },
    "Warm": {
      "Relational": {
        "Provider": "PostgreSQL",
        "ConnectionString": "Host=localhost;Database=sorcha"
      },
      "Documents": {
        "Provider": "MongoDB",
        "ConnectionString": "mongodb://localhost:27017",
        "DatabaseName": "sorcha"
      }
    },
    "Cold": {
      "Provider": "MongoDB",
      "ConnectionString": "mongodb://localhost:27017",
      "DatabaseName": "sorcha_worm"
    }
  },
  "VerifiedCache": {
    "KeyPrefix": "sorcha:",
    "CacheTtlSeconds": 86400,
    "EnableHashVerification": true,
    "StartupStrategy": "Progressive",
    "BlockingThreshold": 100,
    "WarmingBatchSize": 1000
  }
}
```

### Dependency Injection

```csharp
// In Program.cs or Startup.cs
services.AddStorageAbstractions(configuration);

// Or with explicit configuration
services.AddStorageAbstractions(config =>
{
    config.Hot.Provider = StorageProvider.Redis;
    config.Hot.ConnectionString = "localhost:6379";
});
```

## Usage Examples

### Cache Store

```csharp
public class MyService
{
    private readonly ICacheStore _cache;

    public async Task<User?> GetUserAsync(string userId)
    {
        // Try cache first
        var user = await _cache.GetAsync<User>($"user:{userId}");
        if (user != null) return user;

        // Fetch from database
        user = await _database.GetUserAsync(userId);

        // Cache for 1 hour
        await _cache.SetAsync($"user:{userId}", user, TimeSpan.FromHours(1));

        return user;
    }
}
```

### Repository (Relational Data)

```csharp
public class WalletService
{
    private readonly IRepository<Wallet, Guid> _repository;

    public async Task<Wallet> CreateWalletAsync(CreateWalletRequest request)
    {
        var wallet = new Wallet
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Algorithm = request.Algorithm
        };

        await _repository.AddAsync(wallet);
        await _repository.SaveChangesAsync();

        return wallet;
    }

    public async Task<PagedResult<Wallet>> GetWalletsAsync(int page, int pageSize)
    {
        return await _repository.GetPagedAsync(page, pageSize);
    }
}
```

### Document Store (Flexible Schema)

```csharp
public class BlueprintService
{
    private readonly IDocumentStore<Blueprint, string> _store;

    public async Task<Blueprint> SaveBlueprintAsync(Blueprint blueprint)
    {
        await _store.UpsertAsync(blueprint);
        return blueprint;
    }

    public async Task<IEnumerable<Blueprint>> QueryByTenantAsync(string tenantId)
    {
        return await _store.QueryAsync(
            b => b.TenantId == tenantId,
            orderBy: b => b.CreatedAt,
            descending: true);
    }
}
```

### WORM Store (Immutable Ledger)

```csharp
public class RegisterService
{
    private readonly IWormStore<Docket, ulong> _wormStore;

    public async Task<Docket> SealDocketAsync(Docket docket)
    {
        // Append-only - cannot update or delete
        return await _wormStore.AppendAsync(docket);
    }

    public async Task<IntegrityCheckResult> VerifyChainAsync()
    {
        return await _wormStore.VerifyIntegrityAsync();
    }
}
```

### Verified Cache (Cache + WORM)

```csharp
public class DocketQueryService
{
    private readonly IVerifiedCache<Docket, ulong> _cache;

    public async Task<Docket?> GetDocketAsync(ulong height)
    {
        // Checks cache first, falls back to WORM store
        // Optionally verifies hash matches
        return await _cache.GetAsync(height);
    }

    public async Task<VerifiedCacheStatistics> GetCacheStatsAsync()
    {
        var stats = await _cache.GetStatisticsAsync();
        // stats.HitRate, stats.CacheHits, stats.CacheMisses, etc.
        return stats;
    }
}
```

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Application Layer                         │
│  (Blueprint Service, Wallet Service, Register Service)       │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│              Storage Abstractions Layer                      │
│  ┌──────────────┐ ┌──────────────┐ ┌──────────────────────┐ │
│  │ ICacheStore  │ │ IRepository  │ │ IVerifiedCache       │ │
│  │ IDocumentStore│ │ IWormStore  │ │ (Cache + WORM)       │ │
│  └──────────────┘ └──────────────┘ └──────────────────────┘ │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                  Implementation Layer                        │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────────┐│
│  │    Redis    │ │ PostgreSQL  │ │       MongoDB           ││
│  │  (Hot Tier) │ │(Warm-Rel)   │ │ (Warm-Doc / Cold-WORM)  ││
│  └─────────────┘ └─────────────┘ └─────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## WORM Semantics

The WORM (Write-Once-Read-Many) store enforces strict immutability:

- **Append Only**: Documents can only be added, never updated or deleted
- **Sequence Tracking**: Each document has a monotonically increasing sequence number
- **Hash Verification**: Optional cryptographic hash verification
- **Integrity Checks**: Built-in chain integrity verification

```csharp
// Attempting to append duplicate ID throws exception
await wormStore.AppendAsync(existingDocket); // Throws WormDuplicateException

// Verify integrity of the entire chain
var result = await wormStore.VerifyIntegrityAsync();
if (!result.IsValid)
{
    foreach (var violation in result.Violations)
    {
        logger.LogError("Integrity violation: {Type} at {Id}",
            violation.ViolationType, violation.DocumentId);
    }
}
```

## Testing

Use the in-memory implementations for unit testing:

```csharp
public class MyServiceTests
{
    private readonly InMemoryCacheStore _cache;
    private readonly InMemoryWormStore<Docket, ulong> _wormStore;

    public MyServiceTests()
    {
        _cache = new InMemoryCacheStore();
        _wormStore = new InMemoryWormStore<Docket, ulong>(d => d.Id);
    }

    [Fact]
    public async Task Should_Cache_Docket()
    {
        // Arrange
        var docket = new Docket { Id = 1, Hash = "abc123" };

        // Act
        await _wormStore.AppendAsync(docket);
        var result = await _wormStore.GetAsync(1);

        // Assert
        result.Should().NotBeNull();
        result.Hash.Should().Be("abc123");
    }
}
```

## Performance Considerations

1. **Cache TTL**: Set appropriate TTL based on data volatility
2. **Batch Operations**: Use batch methods for multiple documents
3. **Progressive Warming**: For large datasets, use progressive warming
4. **Connection Pooling**: Redis and MongoDB connection pooling is managed automatically

## Related Packages

- `Sorcha.Storage.InMemory` - In-memory implementations for testing
- `Sorcha.Storage.Redis` - Redis cache implementation
- `Sorcha.Storage.EFCore` - Entity Framework Core repository
- `Sorcha.Storage.MongoDB` - MongoDB document and WORM stores
- `Sorcha.Register.Storage` - Register Service storage integration

## License

MIT License - See LICENSE file for details.
