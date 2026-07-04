# MongoDB Patterns Reference

## Contents
- Filter Builder Patterns
- Update Builder Patterns
- Index Creation Strategy
- Two-Tier Storage Pattern
- Query Optimization
- Testing with Testcontainers

---

## Filter Builder Patterns

### Simple Equality

```csharp
var filter = Builders<Register>.Filter.Eq(r => r.Id, registerId);
var register = await _collection.Find(filter).FirstOrDefaultAsync(ct);
```

### Composite Filters (AND)

```csharp
// Transaction lookup by RegisterId + TxId
var filter = Builders<TransactionModel>.Filter.And(
    Builders<TransactionModel>.Filter.Eq(t => t.RegisterId, registerId),
    Builders<TransactionModel>.Filter.Eq(t => t.TxId, transactionId)
);
```

### Range Queries

```csharp
// Get dockets in range
var filter = Builders<Docket>.Filter.And(
    Builders<Docket>.Filter.Eq(d => d.RegisterId, registerId),
    Builders<Docket>.Filter.Gte(d => d.Id, startId),
    Builders<Docket>.Filter.Lte(d => d.Id, endId)
);
```

### Array Element Query (AnyEq)

```csharp
// Find transactions where wallet is in recipients array
var filter = Builders<TransactionModel>.Filter.AnyEq(
    t => t.RecipientsWallets, 
    walletAddress
);
```

### WARNING: LINQ Where vs Filter Builders

**The Problem:**

```csharp
// BAD - In-memory filtering after full collection scan
var results = _collection.AsQueryable()
    .Where(t => t.RegisterId == registerId)
    .ToList();
```

**Why This Breaks:**
1. Downloads entire collection before filtering
2. No index utilization
3. Memory explosion on large collections

**The Fix:**

```csharp
// GOOD - Server-side filtering with index usage
var filter = Builders<TransactionModel>.Filter.Eq(t => t.RegisterId, registerId);
var results = await _collection.Find(filter).ToListAsync();
```

---

## Update Builder Patterns

### Atomic Field Updates

```csharp
// Update register height atomically
var filter = Builders<Register>.Filter.Eq(r => r.Id, registerId);
var update = Builders<Register>.Update
    .Set(r => r.Height, newHeight)
    .Set(r => r.UpdatedAt, DateTime.UtcNow);

await _registers.UpdateOneAsync(filter, update, cancellationToken: ct);
```

### Upsert Pattern

```csharp
// Insert or update based on existence
var options = new ReplaceOptions { IsUpsert = true };
await _collection.ReplaceOneAsync(filter, document, options, ct);
```

### WARNING: Replace vs Update

**The Problem:**

```csharp
// BAD - Full document replacement when only one field changed
var doc = await _collection.Find(filter).FirstOrDefaultAsync();
doc.Status = RegisterStatus.Online;
await _collection.ReplaceOneAsync(filter, doc);
```

**Why This Breaks:**
1. Race condition window between read and write
2. Overwrites concurrent changes to other fields
3. More network traffic than necessary

**The Fix:**

```csharp
// GOOD - Atomic single-field update
var update = Builders<Register>.Update.Set(r => r.Status, RegisterStatus.Online);
await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
```

---

## Index Creation Strategy

### Startup Index Creation

```csharp
// From MongoRegisterRepository.cs - CreateIndexesAsync()
var transactionIndexes = new List<CreateIndexModel<TransactionModel>>
{
    // Primary lookup - UNIQUE composite
    new(Builders<TransactionModel>.IndexKeys
        .Ascending(t => t.RegisterId)
        .Ascending(t => t.TxId),
        new CreateIndexOptions { Unique = true }),

    // Sender queries
    new(Builders<TransactionModel>.IndexKeys
        .Ascending(t => t.RegisterId)
        .Ascending(t => t.SenderWallet)),

    // Time-based queries (descending for recent-first)
    new(Builders<TransactionModel>.IndexKeys
        .Ascending(t => t.RegisterId)
        .Descending(t => t.TimeStamp)),
};

await _transactions.Indexes.CreateManyAsync(transactionIndexes);
```

### WARNING: Missing Index Detection

**When You Might Be Tempted:**
Adding a new query pattern without corresponding index.

**The Fix:**

```markdown
Copy this checklist:
- [ ] New filter field identified
- [ ] Check if existing index covers query
- [ ] Add index to CreateIndexesAsync()
- [ ] Test query performance with .explain()
```

---

## Two-Tier Storage Pattern

Sorcha uses two storage abstractions based on data mutability:

### IDocumentStore (Warm-Tier - Mutable)

```csharp
// For schemas, configurations, registers
public interface IDocumentStore<TDocument, TId>
{
    Task<TDocument?> GetAsync(TId id, CancellationToken ct);
    Task InsertAsync(TDocument document, CancellationToken ct);
    Task ReplaceAsync(TId id, TDocument document, CancellationToken ct);
    Task UpsertAsync(TId id, TDocument document, CancellationToken ct);
    Task DeleteAsync(TId id, CancellationToken ct);
}
```

### IWormStore (Cold-Tier - Immutable)

```csharp
// For transactions, dockets - NO UPDATE/DELETE
public interface IWormStore<TDocument, TId>
{
    Task<TDocument?> GetAsync(TId id, CancellationToken ct);
    Task AppendAsync(TDocument document, CancellationToken ct);  // Insert only
    Task<TId> GetCurrentSequenceAsync(CancellationToken ct);
    // NO ReplaceAsync, NO DeleteAsync - enforced immutability
}
```

---

## Query Optimization

### Existence Check (Optimized)

```csharp
// GOOD - Limit to 1 document for existence check
public async Task<bool> ExistsAsync(TId id, CancellationToken ct)
{
    var filter = Builders<TDocument>.Filter.Eq(_idExpression, id);
    return await _collection.CountDocumentsAsync(filter, 
        new CountOptions { Limit = 1 }, ct) > 0;
}
```

### Count Estimation

```csharp
// Use estimated count for large collections (no filter)
var total = await _collection.EstimatedDocumentCountAsync(ct);

// Use precise count only when filtering
var filtered = await _collection.CountDocumentsAsync(filter, options, ct);
```

### Push the query down — never materialise-then-LINQ

The single worst read anti-pattern: load the whole collection, then filter/sort/page/count in memory.
Every read becomes O(collection) — fetching one document, the latest page, or a count all stream +
deserialize everything first. On an append-only collection it only gets worse.

```csharp
// BAD — materialises the entire collection, then LINQ-to-Objects (O(collection) per read).
// This is exactly the register T1 bottleneck (2026-07-04): GetTransactionsAsync did
// Find(Empty).ToList().AsQueryable() and ~18 call sites filtered/sorted/paged/counted in memory.
var all = (await coll.Find(FilterDefinition<T>.Empty).ToListAsync(ct)).AsQueryable();
var page = all.Where(x => x.Type == t).OrderByDescending(x => x.Ts).Skip(s).Take(n).ToList();
var total = all.Count();

// GOOD — filter + sort + skip/limit + count all pushed to the server (index-backed).
var page = await coll.Find(Builders<T>.Filter.Eq(x => x.Type, t))
    .SortByDescending(x => x.Ts).Skip(s).Limit(n).ToListAsync(ct);
var total = await coll.CountDocumentsAsync(Builders<T>.Filter.Eq(x => x.Type, t), cancellationToken: ct);
```

- **Expose intent-revealing repo methods** (`GetLatestTransactions(skip, take)`,
  `GetTransactionsByType(type, sort, skip, take)`, `CountTransactions`, `CountTransactionsBefore`,
  cursor `GetTransactionsBefore(before, take)`), not a blanket `Task<IQueryable<T>>` that leaks the
  materialise-all shape to callers. See `IReadOnlyRegisterRepository` +
  `docs/audits/2026-07-04-register-read-pushdown-plan.md`.
- **Cursor pagination:** `Find(Lt(Ts, cursor)).SortByDescending(Ts).Limit(n)` + a `CountDocuments(Lt)`
  for `hasMore` — no full scan, no offset skip cost.
- **A pushed-down filter is only cheap on an indexed field** — align the predicate/sort to an existing
  index or you've just moved the COLLSCAN server-side (see the index section above).
- **Genuine full-scans** (e.g. "every doc not in set X", cross-doc aggregate) can't be eliminated — but
  still `.Project(...)` to only the needed fields and stream (`IAsyncCursor`/`IAsyncEnumerable`) rather
  than building a giant in-memory `List`; or move the aggregate into a `$group` pipeline.

---

## Testing with Testcontainers

See the **xunit** skill for test patterns.

```csharp
// Integration test setup
public class MongoIntegrationTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder()
        .WithImage("mongo:7.0")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase($"test_{Guid.NewGuid():N}");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
```