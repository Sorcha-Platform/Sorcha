# MongoDB Modules Reference

## Contents
- Storage Projects Structure
- MongoDocumentStore
- MongoWormStore
- MongoRegisterRepository
- Service Extensions
- Multi-Tier Architecture

---

## Storage Projects Structure

```
src/Common/Sorcha.Storage.MongoDB/
├── MongoDocumentStore.cs           # Generic mutable document store
├── MongoWormStore.cs               # Generic WORM (immutable) store
└── Extensions/
    └── MongoServiceExtensions.cs   # DI registration helpers

src/Core/Sorcha.Register.Storage.MongoDB/
├── MongoRegisterRepository.cs      # Domain-specific repository
├── MongoRegisterStorageConfiguration.cs
└── MongoRegisterStorageServiceExtensions.cs

src/Services/Sorcha.Register.Service/
└── Repositories/
    └── MongoSystemRegisterRepository.cs  # Blueprint storage
```

---

## MongoDocumentStore

Generic implementation for mutable documents (`src/Common/Sorcha.Storage.MongoDB/MongoDocumentStore.cs`).

### Constructor

```csharp
public MongoDocumentStore(
    IMongoDatabase database,
    string collectionName,
    Func<TDocument, TId> idSelector,
    Expression<Func<TDocument, TId>> idExpression)
{
    _collection = database.GetCollection<TDocument>(collectionName);
    _idSelector = idSelector;
    _idExpression = idExpression;
}
```

### Key Operations

| Method | MongoDB Operation | Use Case |
|--------|------------------|----------|
| `GetAsync(id)` | `Find().FirstOrDefaultAsync()` | Single document lookup |
| `GetManyAsync(ids)` | `Find(Filter.In())` | Batch retrieval |
| `InsertAsync(doc)` | `InsertOneAsync()` | New document |
| `ReplaceAsync(id, doc)` | `ReplaceOneAsync(IsUpsert=false)` | Update existing only |
| `UpsertAsync(id, doc)` | `ReplaceOneAsync(IsUpsert=true)` | Insert or update |
| `DeleteAsync(id)` | `DeleteOneAsync()` | Remove document |
| `ExistsAsync(id)` | `CountDocumentsAsync(limit=1)` | Existence check |

---

## MongoWormStore

Immutable append-only storage (`src/Common/Sorcha.Storage.MongoDB/MongoWormStore.cs`).

### Critical Difference from DocumentStore

```csharp
// NO UPDATE OR DELETE METHODS - enforces immutability
public interface IWormStore<TDocument, TId>
{
    Task<TDocument?> GetAsync(TId id, CancellationToken ct);
    Task AppendAsync(TDocument document, CancellationToken ct);  // Insert only
    Task AppendBatchAsync(IEnumerable<TDocument> documents, CancellationToken ct);
    Task<TId> GetCurrentSequenceAsync(CancellationToken ct);
    Task<bool> VerifyIntegrityAsync(TId startId, TId endId, CancellationToken ct);
}
```

### AppendAsync Implementation

```csharp
public async Task AppendAsync(TDocument document, CancellationToken ct)
{
    var id = _idSelector(document);
    
    // Check existence first - WORM semantics
    if (await ExistsAsync(id, ct))
    {
        throw new InvalidOperationException(
            $"Document with ID {id} already exists. WORM store is append-only.");
    }
    
    await _collection.InsertOneAsync(document, cancellationToken: ct);
}
```

### WARNING: Bypassing WORM Protection

**The Problem:**

```csharp
// BAD - Direct collection access bypasses WORM protection
var collection = database.GetCollection<Transaction>("transactions");
await collection.ReplaceOneAsync(filter, modifiedTx);  // Breaks immutability!
```

**Why This Breaks:**
1. Destroys audit trail integrity
2. Breaks blockchain hash chain
3. Violates DAD security model

**The Fix:**
Always use `IWormStore<T>` interface. Never access underlying collection directly for ledger data.

---

## MongoRegisterRepository

Domain-specific repository combining multiple collections (`src/Core/Sorcha.Register.Storage.MongoDB/MongoRegisterRepository.cs`).

### Constructor

```csharp
public MongoRegisterRepository(
    IOptions<MongoRegisterStorageConfiguration> options,
    ILogger<MongoRegisterRepository> logger)
{
    var client = new MongoClient(options.Value.ConnectionString);
    var database = client.GetDatabase(options.Value.DatabaseName);
    
    _registers = database.GetCollection<Register>(options.Value.RegisterCollectionName);
    _transactions = database.GetCollection<TransactionModel>(options.Value.TransactionCollectionName);
    _dockets = database.GetCollection<Docket>(options.Value.DocketCollectionName);
    
    if (options.Value.CreateIndexesOnStartup)
    {
        Task.Run(() => CreateIndexesAsync()).Wait();
    }
}
```

### Key Domain Operations

```csharp
// Register operations
Task<Register?> GetRegisterAsync(string registerId, CancellationToken ct);
Task InsertRegisterAsync(Register register, CancellationToken ct);
Task UpdateRegisterHeightAsync(string registerId, uint height, CancellationToken ct);

// Transaction operations
Task InsertTransactionAsync(TransactionModel tx, CancellationToken ct);
Task<TransactionModel?> GetTransactionAsync(string registerId, string txId, CancellationToken ct);
Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(string registerId, ulong docketId, CancellationToken ct);

// Docket operations
Task InsertDocketAsync(Docket docket, CancellationToken ct);
Task<IEnumerable<Docket>> GetDocketsAsync(string registerId, CancellationToken ct);
```

---

## Service Extensions

### Registration Pattern

```csharp
// From MongoServiceExtensions.cs
public static class MongoServiceExtensions
{
    public static IServiceCollection AddMongoClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IMongoClient>(sp =>
        {
            var connectionString = configuration.GetConnectionString("MongoDB");
            return new MongoClient(connectionString);
        });
        return services;
    }

    public static IServiceCollection AddMongoDatabase(
        this IServiceCollection services,
        string databaseName)
    {
        services.AddSingleton<IMongoDatabase>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(databaseName);
        });
        return services;
    }
}
```

### Storage Type Switching

```csharp
// From Register.Service Program.cs
var storageType = builder.Configuration["RegisterStorage:Type"] ?? "InMemory";

if (storageType.Equals("MongoDB", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.Configure<MongoRegisterStorageConfiguration>(
        builder.Configuration.GetSection("RegisterStorage:MongoDB"));
    builder.Services.AddSingleton<IRegisterRepository, MongoRegisterRepository>();
}
else
{
    builder.Services.AddSingleton<IRegisterRepository, InMemoryRegisterRepository>();
}
```

---

## Multi-Tier Architecture

### Sorcha's Storage Tiers

| Tier | Interface | Purpose | Collections |
|------|-----------|---------|-------------|
| Warm | `IDocumentStore<T>` | Mutable schemas, config | `registers`, `blueprints` |
| Cold | `IWormStore<T>` | Immutable ledger | `transactions`, `dockets` |

### Integration with Other Storage

See the **redis** skill for caching patterns and **entity-framework** skill for PostgreSQL integration.

```csharp
// Typical service might use multiple storage types
public class WorkflowService
{
    private readonly IDocumentStore<Blueprint, string> _blueprintStore;  // MongoDB
    private readonly IWalletRepository _walletRepo;                       // PostgreSQL via EF (service-specific repo; no generic IRepository<T>)
    private readonly IDistributedCache _cache;                           // Redis
}
```