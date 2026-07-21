# Sorcha.Register.Storage

Register Service storage implementation using the multi-tier storage abstraction layer with verified cache for dockets.

## Overview

This library provides the storage layer integration for the Sorcha Register Service, implementing:

- **Verified Cache for Dockets**: WORM-backed cache with hash verification for immutable ledger entries
- **Standard Cache for Registers**: Fast access to register metadata
- **Standard Cache for Transactions**: Cache-aside pattern for transaction queries
- **Cache Warming**: Progressive cache population on service startup

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                   Register Service                           │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│              CachedRegisterRepository                        │
│  ┌──────────────────────────────────────────────────────┐   │
│  │ IVerifiedCache<Docket>  - Docket cache with WORM     │   │
│  │ ICacheStore             - Register/Transaction cache  │   │
│  │ IRegisterRepository     - Inner repository (storage)  │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────┬───────────────────────────────────┘
                          │
┌─────────────────────────▼───────────────────────────────────┐
│                    Storage Layer                             │
│  ┌─────────────┐ ┌─────────────┐ ┌─────────────────────────┐│
│  │    Redis    │ │   MongoDB   │ │    MongoDB WORM         ││
│  │   (Cache)   │ │ (Documents) │ │    (Dockets)            ││
│  └─────────────┘ └─────────────┘ └─────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## Installation

```xml
<PackageReference Include="Sorcha.Register.Storage" Version="1.0.0" />
```

## Configuration

### appsettings.json

```json
{
  "RegisterStorage": {
    "RedisConnectionString": "localhost:6379",
    "MongoConnectionString": "mongodb://localhost:27017",
    "MongoDatabaseName": "sorcha_register",
    "DocketCollectionName": "dockets",
    "TransactionCollectionName": "transactions",
    "RegisterCollectionName": "registers",
    "UseInMemoryStorage": false,
    "EnableCacheWarming": true,
    "DocketCacheConfiguration": {
      "KeyPrefix": "register:docket:",
      "CacheTtlSeconds": 86400,
      "EnableHashVerification": true,
      "StartupStrategy": "Progressive",
      "BlockingThreshold": 100,
      "WarmingBatchSize": 1000
    },
    "TransactionCacheConfiguration": {
      "KeyPrefix": "register:tx:",
      "CacheTtlSeconds": 3600,
      "EnableHashVerification": true,
      "WarmingBatchSize": 500
    }
  }
}
```

### Dependency Injection

```csharp
// In Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Register storage with configuration from appsettings
builder.Services.AddRegisterStorage(builder.Configuration);

// Or with explicit configuration
builder.Services.AddRegisterStorage(config =>
{
    config.RedisConnectionString = "localhost:6379";
    config.MongoConnectionString = "mongodb://localhost:27017";
    config.EnableCacheWarming = true;
    config.DocketCacheConfiguration.StartupStrategy = CacheStartupStrategy.Progressive;
});

// For testing - use in-memory storage
builder.Services.AddInMemoryRegisterStorage();
```

## Usage

### Basic Repository Operations

```csharp
public class RegisterManager
{
    private readonly IRegisterRepository _repository;

    public RegisterManager(IRegisterRepository repository)
    {
        _repository = repository;
    }

    public async Task<Register> CreateRegisterAsync(string name)
    {
        var register = new Register
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Status = RegisterStatus.Online
        };

        return await _repository.InsertRegisterAsync(register);
    }

    public async Task<Docket> SealDocketAsync(string registerId, List<string> transactionIds)
    {
        var register = await _repository.GetRegisterAsync(registerId);
        var previousDocket = await _repository.GetDocketAsync(registerId, register.Height);

        var docket = new Docket
        {
            Id = register.Height + 1,
            RegisterId = registerId,
            PreviousHash = previousDocket?.Hash ?? "",
            Hash = ComputeDocketHash(transactionIds, previousDocket?.Hash),
            TransactionIds = transactionIds,
            State = DocketState.Sealed
        };

        // Uses verified cache - writes to both cache and WORM store
        var sealed = await _repository.InsertDocketAsync(docket);

        await _repository.UpdateRegisterHeightAsync(registerId, (uint)sealed.Id);

        return sealed;
    }
}
```

### Cache Statistics and Monitoring

```csharp
public class StorageHealthService
{
    private readonly CachedRegisterRepository _repository;

    public async Task<StorageHealthReport> GetHealthReportAsync()
    {
        // Get docket cache statistics
        var stats = await _repository.GetDocketCacheStatisticsAsync();

        return new StorageHealthReport
        {
            CacheHitRate = stats?.HitRate ?? 0,
            CacheHits = stats?.CacheHits ?? 0,
            CacheMisses = stats?.CacheMisses ?? 0,
            WormFetches = stats?.WormFetches ?? 0,
            VerificationFailures = stats?.VerificationFailures ?? 0,
            AverageLatencyMs = stats?.AverageLatencyMs ?? 0
        };
    }

    public async Task<bool> VerifyIntegrityAsync()
    {
        var result = await _repository.VerifyDocketCacheIntegrityAsync();
        return result?.IsValid ?? true;
    }
}
```

### Manual Cache Warming

```csharp
public class CacheMaintenanceService
{
    private readonly CachedRegisterRepository _repository;

    public async Task WarmCacheForRegisterAsync(string registerId)
    {
        var register = await _repository.GetRegisterAsync(registerId);
        if (register == null) return;

        var progress = new Progress<CacheWarmingProgress>(p =>
        {
            Console.WriteLine($"Warming: {p.PercentComplete:F1}% ({p.DocumentsLoaded}/{p.TotalDocuments})");
        });

        await _repository.WarmDocketCacheAsync(register.Height, progress);
    }

    public async Task InvalidateCacheForRegisterAsync(string registerId)
    {
        await _repository.InvalidateRegisterCachesAsync(registerId);
    }
}
```

## Cache Strategies

### Docket Cache (Verified)

Dockets are immutable ledger entries that use verified caching:

1. **Read Path**:
   - Check cache first
   - If cache hit and hash verification enabled, verify against WORM store
   - If cache miss, fetch from WORM store and populate cache

2. **Write Path**:
   - Write to WORM store first (source of truth)
   - Populate cache after successful WORM write

3. **Hash Verification**:
   - Optional per-read verification that cached hash matches WORM hash
   - Detects cache corruption or tampering

### Register Cache (Standard)

Registers are mutable metadata:

1. **Cache TTL**: 1 hour default
2. **Write-through**: Updates go to repository and cache
3. **Invalidation**: Explicit invalidation on delete

### Transaction Cache (Standard)

Transactions use cache-aside pattern:

1. **Cache TTL**: Configurable (default 1 hour)
2. **Cache on read**: Populate cache on cache miss
3. **Cache on write**: Populate cache after insert

## Startup Strategies

### Blocking Strategy

Service waits until cache is warmed before accepting requests:

```csharp
config.DocketCacheConfiguration.StartupStrategy = CacheStartupStrategy.Blocking;
config.DocketCacheConfiguration.BlockingThreshold = 1000; // Max documents to wait for
```

Best for:
- Small registers (< 10,000 dockets)
- Services requiring immediate cache hits
- Development/testing environments

### Progressive Strategy

Service starts immediately, cache warms in background:

```csharp
config.DocketCacheConfiguration.StartupStrategy = CacheStartupStrategy.Progressive;
config.DocketCacheConfiguration.WarmingBatchSize = 1000;
```

Best for:
- Large registers (> 10,000 dockets)
- Production environments
- Services that can tolerate initial cache misses

## Testing

Use in-memory storage for unit tests:

```csharp
public class RegisterManagerTests
{
    private readonly IRegisterRepository _repository;

    public RegisterManagerTests()
    {
        var services = new ServiceCollection();
        services.AddInMemoryRegisterStorage();
        var provider = services.BuildServiceProvider();
        _repository = provider.GetRequiredService<IRegisterRepository>();
    }

    [Fact]
    public async Task Should_Seal_Docket_With_Verified_Cache()
    {
        // Arrange
        var register = await _repository.InsertRegisterAsync(new Register
        {
            Id = "test-register",
            Name = "Test"
        });

        var docket = new Docket
        {
            Id = 1,
            RegisterId = "test-register",
            Hash = "abc123",
            State = DocketState.Sealed
        };

        // Act
        var sealed = await _repository.InsertDocketAsync(docket);

        // Assert
        sealed.Should().NotBeNull();
        sealed.Id.Should().Be(1);

        // Verify retrievable from cache
        var retrieved = await _repository.GetDocketAsync("test-register", 1);
        retrieved.Should().NotBeNull();
        retrieved.Hash.Should().Be("abc123");
    }
}
```

## Performance Considerations

1. **Docket Cache TTL**: Set to 24 hours (dockets are immutable)
2. **Transaction Cache TTL**: Set based on query patterns (1 hour default)
3. **Hash Verification**: Disable for read-heavy workloads with trusted cache
4. **Warming Batch Size**: Balance memory usage vs warming speed

## Monitoring

Expose cache statistics via health endpoints:

```csharp
app.MapGet("/health/storage", async (CachedRegisterRepository repo) =>
{
    var stats = await repo.GetDocketCacheStatisticsAsync();
    var integrity = await repo.VerifyDocketCacheIntegrityAsync();

    return Results.Ok(new
    {
        CacheHitRate = stats?.HitRate ?? 0,
        IntegrityValid = integrity?.IsValid ?? true,
        DocumentsVerified = integrity?.DocumentsVerified ?? 0
    });
});
```

## Related Packages

- `Sorcha.Storage.Abstractions` - Core storage interfaces
- `Sorcha.Storage.InMemory` - In-memory implementations
- `Sorcha.Register.Core` - Register domain logic
- `Sorcha.Register.Models` - Register domain models

## License

MIT License - See LICENSE file for details.
