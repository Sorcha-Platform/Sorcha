# Sorcha Data Persistence Architecture

**Version:** 1.0
**Date:** 2025-12-07
**Status:** Proposal
**Author:** Architecture Team

---

## Executive Summary

This document proposes a unified data persistence architecture for the Sorcha platform that supports:

1. **Multiple storage tiers** - Hot (cache), Warm (operational), Cold (WORM)
2. **Provider abstraction** - Pluggable backends (PostgreSQL, MongoDB, Redis, cloud services)
3. **Deployment flexibility** - Local Docker, Azure, AWS, on-premises
4. **Data sovereignty** - Clear boundaries for sensitive data

---

## Storage Tiers

### Overview

| Tier | Purpose | Retention | Consistency | Examples |
|------|---------|-----------|-------------|----------|
| **Hot** | Ephemeral/Cache | Seconds to hours | Eventual | Session tokens, rate limits, JWKS cache |
| **Warm** | Operational | Days to years | Strong | Tenant config, wallet metadata, workflow state |
| **Cold** | Immutable WORM | Forever | Immutable | Register dockets, sealed transactions |

### Tier Characteristics

#### Hot Tier (Cache)
- **Purpose**: High-speed, ephemeral data with automatic expiration
- **Latency**: Sub-millisecond reads
- **Durability**: Not guaranteed - data can be lost on restart
- **Use Cases**:
  - JWT token validation cache
  - JWKS public key cache
  - Rate limiting counters
  - Session state
  - API response caching

#### Warm Tier (Operational)
- **Purpose**: Transactional business data requiring ACID guarantees
- **Latency**: Single-digit millisecond reads
- **Durability**: Full persistence with backup/recovery
- **Use Cases**:
  - Organization configuration
  - User identities and credentials
  - Wallet metadata (not keys - those are in HSM/Key Vault)
  - Blueprint definitions
  - Workflow instance state
  - Audit logs

#### Cold Tier (WORM - Write Once, Read Many)
- **Purpose**: Immutable ledger data that must never be modified
- **Latency**: Acceptable for batch operations
- **Durability**: Maximum - with replication and archival
- **Use Cases**:
  - Sealed register dockets (blocks)
  - Committed transactions
  - Cryptographic proofs
  - Historical snapshots

---

## Storage Provider Strategy

### Why Not EF Core Exclusively?

Entity Framework Core excels at relational data with complex queries but has limitations for Sorcha's requirements:

| Requirement | EF Core Fit | Alternative |
|-------------|-------------|-------------|
| Relational data with ACID | Excellent | - |
| Multi-tenant schemas | Good | - |
| Document storage (JSON) | Partial (JSONB columns) | MongoDB native |
| Append-only ledger | Poor (designed for mutability) | Custom WORM abstraction |
| Schema evolution | Requires migrations | Document stores handle natively |
| Cloud provider switching | Provider-specific | Direct abstraction better |
| High-throughput caching | Not designed for this | Redis |

### Recommended Provider Matrix

| Tier | Local Development | Production (Azure) | Production (AWS) |
|------|-------------------|-------------------|------------------|
| **Hot** | Redis (Docker) | Azure Cache for Redis | ElastiCache |
| **Warm (Relational)** | PostgreSQL (Docker) | Azure PostgreSQL | RDS PostgreSQL |
| **Warm (Documents)** | MongoDB (Docker) | Cosmos DB (MongoDB API) | DocumentDB |
| **Cold (WORM)** | MongoDB (Docker) | Cosmos DB + Blob Storage | DocumentDB + S3 |

---

## Core Abstractions

### 1. Cache Store Interface (Hot Tier)

```csharp
namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Interface for hot-tier cache operations.
/// Implementations: Redis, MemoryCache, Azure Cache
/// </summary>
public interface ICacheStore
{
    /// <summary>
    /// Gets a cached value by key.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a cached value with optional expiration.
    /// </summary>
    Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a cached value.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a key exists in cache.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a value or creates it if not present (cache-aside pattern).
    /// </summary>
    Task<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all keys matching a pattern.
    /// </summary>
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increments a counter (for rate limiting).
    /// </summary>
    Task<long> IncrementAsync(
        string key,
        long delta = 1,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);
}
```

### 2. Repository Interface (Warm Tier - Relational)

```csharp
namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Generic repository interface for warm-tier relational data.
/// Implementations: EF Core (PostgreSQL, SQL Server), etc.
/// </summary>
public interface IRepository<TEntity, TId> where TEntity : class
{
    /// <summary>
    /// Gets an entity by its identifier.
    /// </summary>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all entities (use with caution - prefer queries).
    /// </summary>
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries entities using a predicate.
    /// </summary>
    Task<IEnumerable<TEntity>> QueryAsync(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new entity.
    /// </summary>
    Task<TEntity> AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing entity.
    /// </summary>
    Task<TEntity> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entity by identifier.
    /// </summary>
    Task DeleteAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an entity exists.
    /// </summary>
    Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts entities matching a predicate.
    /// </summary>
    Task<int> CountAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paged result.
    /// </summary>
    Task<PagedResult<TEntity>> GetPagedAsync(
        int page,
        int pageSize,
        Expression<Func<TEntity, bool>>? predicate = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Paged result wrapper.
/// </summary>
public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
```

### 3. Document Store Interface (Warm Tier - Documents)

```csharp
namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Interface for document storage operations.
/// Implementations: MongoDB, Cosmos DB, etc.
/// </summary>
public interface IDocumentStore<TDocument, TId> where TDocument : class
{
    /// <summary>
    /// Gets a document by identifier.
    /// </summary>
    Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries documents using a filter expression.
    /// </summary>
    Task<IEnumerable<TDocument>> QueryAsync(
        Expression<Func<TDocument, bool>> filter,
        int? limit = null,
        int? skip = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new document.
    /// </summary>
    Task<TDocument> InsertAsync(TDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts multiple documents in a batch.
    /// </summary>
    Task InsertManyAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces an existing document (full replacement).
    /// </summary>
    Task<TDocument> ReplaceAsync(
        TId id,
        TDocument document,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a document by identifier.
    /// </summary>
    Task DeleteAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts documents matching a filter.
    /// </summary>
    Task<long> CountAsync(
        Expression<Func<TDocument, bool>>? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a document exists.
    /// </summary>
    Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default);
}
```

### 4. WORM Store Interface (Cold Tier)

```csharp
namespace Sorcha.Storage.Abstractions;

/// <summary>
/// Interface for Write-Once-Read-Many (WORM) storage.
/// Used for immutable ledger data.
/// Implementations: MongoDB with immutable collections, Cosmos DB, etc.
/// </summary>
public interface IWormStore<TDocument, TId> where TDocument : class
{
    /// <summary>
    /// Appends a new document to the store. Cannot be modified after append.
    /// </summary>
    Task<TDocument> AppendAsync(TDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends multiple documents in a batch.
    /// </summary>
    Task AppendBatchAsync(
        IEnumerable<TDocument> documents,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a document by identifier.
    /// </summary>
    Task<TDocument?> GetAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets documents in a sequential range (for ledger traversal).
    /// </summary>
    Task<IEnumerable<TDocument>> GetRangeAsync(
        TId startId,
        TId endId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries documents (read-only).
    /// </summary>
    Task<IEnumerable<TDocument>> QueryAsync(
        Expression<Func<TDocument, bool>> filter,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Seals a batch and triggers archival (if configured).
    /// </summary>
    Task SealBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current sequence/height of the store.
    /// </summary>
    Task<ulong> GetCurrentSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies integrity of stored documents (checksums, etc.).
    /// </summary>
    Task<bool> VerifyIntegrityAsync(
        TId? startId = default,
        TId? endId = default,
        CancellationToken cancellationToken = default);
}
```

---

## Service Storage Mapping

### Tenant Service

| Data Type | Tier | Interface | Provider | Notes |
|-----------|------|-----------|----------|-------|
| JWKS Cache | Hot | `ICacheStore` | Redis | 1-hour TTL |
| Session Tokens | Hot | `ICacheStore` | Redis | Short-lived |
| Rate Limits | Hot | `ICacheStore` | Redis | Counter with TTL |
| Organizations | Warm | `IRepository<Organization, Guid>` | PostgreSQL (EF) | Multi-tenant |
| User Identities | Warm | `IRepository<UserIdentity, Guid>` | PostgreSQL (EF) | Per-org schema |
| Audit Logs | Warm | `IDocumentStore<AuditLog, Guid>` | PostgreSQL/MongoDB | JSONB or document |

### Wallet Service

| Data Type | Tier | Interface | Provider | Notes |
|-----------|------|-----------|----------|-------|
| Key Material | - | - | HSM/Key Vault | Never stored in DB |
| Nonce Tracking | Hot | `ICacheStore` | Redis | Replay prevention |
| Wallet Metadata | Warm | `IRepository<Wallet, string>` | PostgreSQL (EF) | Address is key |
| Derived Addresses | Warm | `IRepository<WalletAddress, Guid>` | PostgreSQL (EF) | FK to wallet |
| Access Grants | Warm | `IRepository<WalletAccess, Guid>` | PostgreSQL (EF) | Delegation |
| Transaction History | Cold | `IWormStore<WalletTx, string>` | MongoDB | Append-only |

### Blueprint Service

| Data Type | Tier | Interface | Provider | Notes |
|-----------|------|-----------|----------|-------|
| Published Cache | Hot | `ICacheStore` | Redis | Frequently accessed |
| Blueprint Defs | Warm | `IDocumentStore<Blueprint, Guid>` | MongoDB | Complex JSON |
| Published Versions | Warm | `IDocumentStore<PublishedBlueprint, Guid>` | MongoDB | Immutable versions |
| Workflow Instances | Warm | `IDocumentStore<Instance, Guid>` | MongoDB | State machine |
| Sealed Actions | Cold | `IWormStore<Action, string>` | MongoDB | After execution |

### Register Service - Verified Cache Architecture

The Register Service has a fundamentally different storage model than other services. The **docket data is the single source of truth** and must be cryptographically verified on every load.

#### Security Model

**Threat**: An attacker modifies data in cold storage (disk, database, or cloud storage).

**Defense**: All data loaded from cold storage must be cryptographically verified before use:
1. Transaction signatures verified against sender wallet public keys
2. Docket hashes verified against chain integrity (previous hash linkage)
3. Corrupted/invalid data triggers peer recovery, not error

#### Storage Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Register Service Storage Model                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                   VERIFIED CACHE (In-Memory)                     │    │
│  │                                                                  │    │
│  │  • Single source of truth for active operations                 │    │
│  │  • Rebuilt from cold storage on startup                         │    │
│  │  • All data cryptographically verified on load                  │    │
│  │  • Queries always served from verified cache                    │    │
│  │                                                                  │    │
│  │  Contents:                                                       │    │
│  │  ├── Register metadata (configuration)                          │    │
│  │  ├── Docket chain (verified hash linkage)                       │    │
│  │  └── Transaction index (verified signatures)                    │    │
│  │                                                                  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ Load & Verify                             │
│                              │ (Startup / On-Demand)                     │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                 COLD STORAGE (WORM - Append Only)                │    │
│  │                                                                  │    │
│  │  • Durable persistence (MongoDB, filesystem, cloud blob)        │    │
│  │  • Append-only - no updates or deletes                          │    │
│  │  • NOT trusted - verification required on load                  │    │
│  │  • Corruption triggers peer recovery                            │    │
│  │                                                                  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                              │                                           │
│                              │ Recovery (if verification fails)          │
│                              ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                      PEER NETWORK                                │    │
│  │                                                                  │    │
│  │  • Request replacement dockets from peers                       │    │
│  │  • Consensus on valid chain state                               │    │
│  │  • Re-sync corrupted ranges                                     │    │
│  │                                                                  │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Interface Definition

```csharp
namespace Sorcha.Register.Core.Storage;

/// <summary>
/// Verified cache for register data. Data is cryptographically verified
/// before being added to the cache. The cache is the authoritative source
/// for all read operations.
/// </summary>
public interface IVerifiedRegisterCache
{
    /// <summary>
    /// Initializes the cache by loading and verifying data from cold storage.
    /// Invalid data is skipped and marked for peer recovery.
    /// </summary>
    /// <param name="registerId">Register to initialize</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Initialization result with any corruption detected</returns>
    Task<CacheInitializationResult> InitializeAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a verified docket from cache. Never reads directly from cold storage.
    /// </summary>
    Task<Docket?> GetDocketAsync(
        string registerId,
        ulong height,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a verified transaction from cache.
    /// </summary>
    Task<TransactionModel?> GetTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new docket to the cache after verification.
    /// Also persists to cold storage.
    /// </summary>
    /// <param name="docket">Docket to add (must pass verification)</param>
    /// <param name="transactions">Transactions in the docket</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Verification result</returns>
    Task<VerificationResult> AddVerifiedDocketAsync(
        Docket docket,
        IEnumerable<TransactionModel> transactions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current verified chain height.
    /// </summary>
    Task<uint> GetVerifiedHeightAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries transactions from the verified cache.
    /// </summary>
    Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ranges that need recovery from peers.
    /// </summary>
    Task<IEnumerable<CorruptionRange>> GetCorruptedRangesAsync(
        string registerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a range as recovered after peer sync.
    /// </summary>
    Task MarkRangeRecoveredAsync(
        string registerId,
        ulong startHeight,
        ulong endHeight,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of cache initialization.
/// </summary>
public record CacheInitializationResult(
    string RegisterId,
    uint VerifiedHeight,
    uint TotalDockets,
    IReadOnlyList<CorruptionRange> CorruptedRanges,
    TimeSpan LoadDuration)
{
    public bool HasCorruption => CorruptedRanges.Count > 0;
    public bool IsFullyVerified => !HasCorruption;
}

/// <summary>
/// Represents a range of corrupted/invalid dockets.
/// </summary>
public record CorruptionRange(
    ulong StartHeight,
    ulong EndHeight,
    CorruptionType Type,
    string Details);

/// <summary>
/// Types of corruption detected.
/// </summary>
public enum CorruptionType
{
    /// <summary>Docket hash doesn't match computed hash</summary>
    InvalidDocketHash,

    /// <summary>Previous hash linkage broken</summary>
    BrokenChainLink,

    /// <summary>Transaction signature invalid</summary>
    InvalidTransactionSignature,

    /// <summary>Data missing from storage</summary>
    MissingData,

    /// <summary>Data format/schema invalid</summary>
    MalformedData
}

/// <summary>
/// Result of verification when adding new data.
/// </summary>
public record VerificationResult(
    bool IsValid,
    string? ErrorMessage = null,
    IReadOnlyList<string>? InvalidTransactionIds = null);
```

#### Verification Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Docket Verification Flow                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Load Docket from Cold Storage                                          │
│         │                                                                │
│         ▼                                                                │
│  ┌─────────────────────────┐                                            │
│  │ 1. Verify Docket Hash   │                                            │
│  │    SHA256(docket_data)  │                                            │
│  │    == stored_hash?      │──── NO ───► Mark as Corrupted              │
│  └─────────────────────────┘              Request from Peers            │
│         │ YES                                                            │
│         ▼                                                                │
│  ┌─────────────────────────┐                                            │
│  │ 2. Verify Chain Link    │                                            │
│  │    docket.PreviousHash  │                                            │
│  │    == prev_docket.Hash? │──── NO ───► Mark as Corrupted              │
│  └─────────────────────────┘              (fork detected)               │
│         │ YES                                                            │
│         ▼                                                                │
│  ┌─────────────────────────┐                                            │
│  │ 3. Load Transactions    │                                            │
│  │    for each tx in       │                                            │
│  │    docket.TransactionIds│                                            │
│  └─────────────────────────┘                                            │
│         │                                                                │
│         ▼                                                                │
│  ┌─────────────────────────┐                                            │
│  │ 4. Verify Each Tx       │                                            │
│  │    - Signature valid?   │                                            │
│  │    - Sender key exists? │──── NO ───► Skip transaction               │
│  │    - Payload integrity? │              Log for audit                 │
│  └─────────────────────────┘                                            │
│         │ YES                                                            │
│         ▼                                                                │
│  ┌─────────────────────────┐                                            │
│  │ 5. Add to Verified      │                                            │
│  │    Cache                │                                            │
│  │    (In-Memory)          │                                            │
│  └─────────────────────────┘                                            │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Implementation Notes

1. **Startup Behavior**: On service startup, the cache is populated by loading and verifying all dockets from cold storage. This may take time for large registers.

2. **Lazy Loading Option**: For very large registers, implement lazy verification where only recent N blocks are verified on startup, with older blocks verified on-demand.

3. **Background Verification**: A background task continuously verifies older blocks and reports corruption.

4. **Write Path**: New dockets are verified before being added to both cache and cold storage atomically.

5. **No Direct Cold Storage Reads**: All queries MUST go through the verified cache. The cold storage is only for persistence and recovery.

#### Data Mapping

| Data Type | Cache (In-Memory) | Cold Storage | Notes |
|-----------|-------------------|--------------|-------|
| Register Config | ConcurrentDictionary | MongoDB/File | Mutable config (name, status) |
| Docket Chain | ConcurrentDictionary | MongoDB/File | Verified hash chain |
| Transactions | ConcurrentDictionary + Indexes | MongoDB/File | Verified signatures |
| Corruption Log | In-Memory List | Audit Log | For peer recovery |

---

## Implementation Strategy

### Phase 1: Core Abstractions (Week 1)

1. Create `Sorcha.Storage.Abstractions` library with interfaces
2. Create `Sorcha.Storage.InMemory` with in-memory implementations
3. Migrate existing in-memory repositories to use new interfaces

### Phase 2: Redis Implementation (Week 2)

1. Create `Sorcha.Storage.Redis` library
2. Implement `ICacheStore` with StackExchange.Redis
3. Add Redis to .NET Aspire orchestration
4. Migrate hot-tier usage in Tenant Service

### Phase 3: PostgreSQL Implementation (Week 3-4)

1. Create `Sorcha.Storage.EFCore` library
2. Implement generic `EFCoreRepository<T, TId>`
3. Enable Tenant Service EF Core (already designed)
4. Implement Wallet Service EF Core repositories
5. Add migrations infrastructure

### Phase 4: MongoDB Implementation (Week 5-6)

1. Create `Sorcha.Storage.MongoDB` library
2. Implement `IDocumentStore<T, TId>`
3. Implement `IWormStore<T, TId>` with immutability controls
4. Migrate Blueprint Service storage
5. Migrate Register Service storage

### Phase 5: Cloud Provider Adapters (Future)

1. Azure Cosmos DB adapter (MongoDB API compatible)
2. Azure Blob Storage for cold archival
3. AWS DocumentDB adapter
4. AWS S3 for cold archival

---

## Configuration

### appsettings.json Structure

```json
{
  "Storage": {
    "Hot": {
      "Provider": "Redis",
      "Redis": {
        "ConnectionString": "localhost:6379",
        "InstanceName": "sorcha:",
        "DefaultTtlSeconds": 3600
      }
    },
    "Warm": {
      "Relational": {
        "Provider": "PostgreSQL",
        "PostgreSQL": {
          "ConnectionString": "Host=localhost;Port=5432;Database=sorcha;Username=sorcha_user;Password=${DB_PASSWORD}"
        }
      },
      "Documents": {
        "Provider": "MongoDB",
        "MongoDB": {
          "ConnectionString": "mongodb://localhost:27017",
          "DatabaseName": "sorcha"
        }
      }
    },
    "Cold": {
      "Provider": "MongoDB",
      "MongoDB": {
        "ConnectionString": "mongodb://localhost:27017",
        "DatabaseName": "sorcha_ledger"
      },
      "Archival": {
        "Enabled": false,
        "Provider": "AzureBlob",
        "AzureBlob": {
          "ConnectionString": "${AZURE_STORAGE_CONNECTION}"
        }
      }
    }
  }
}
```

### Dependency Injection Registration

```csharp
// In ServiceDefaults or each service's Program.cs
builder.Services.AddSorchaStorage(options =>
{
    // Hot tier
    options.UseRedisCache(builder.Configuration.GetConnectionString("Redis")!);

    // Warm tier (relational)
    options.UsePostgreSql<TenantDbContext>(
        builder.Configuration.GetConnectionString("TenantDatabase")!);

    // Warm tier (documents)
    options.UseMongoDB(
        builder.Configuration.GetConnectionString("MongoDB")!,
        "sorcha");

    // Cold tier (WORM)
    options.UseMongoDBWorm(
        builder.Configuration.GetConnectionString("MongoDB")!,
        "sorcha_ledger");
});
```

---

## WORM Implementation Details

### Immutability Enforcement

```csharp
/// <summary>
/// MongoDB WORM store with immutability enforcement.
/// </summary>
public class MongoWormStore<TDocument, TId> : IWormStore<TDocument, TId>
    where TDocument : class, IImmutableDocument<TId>
{
    private readonly IMongoCollection<TDocument> _collection;

    public async Task<TDocument> AppendAsync(TDocument document, CancellationToken ct = default)
    {
        // Set immutability metadata
        document.SealedAt = DateTime.UtcNow;
        document.Hash = ComputeHash(document);

        // Insert with write concern: majority
        await _collection.InsertOneAsync(document, new InsertOneOptions(), ct);

        return document;
    }

    // Update and Delete throw NotSupportedException
    // This is enforced at the interface level
}

/// <summary>
/// Marker interface for immutable documents.
/// </summary>
public interface IImmutableDocument<TId>
{
    TId Id { get; }
    DateTime SealedAt { get; set; }
    string Hash { get; set; }
}
```

### MongoDB Collection Configuration

```javascript
// Create immutable collection with validator
db.createCollection("dockets", {
  validator: {
    $jsonSchema: {
      bsonType: "object",
      required: ["_id", "registerId", "height", "sealedAt", "hash"],
      properties: {
        _id: { bsonType: "string" },
        sealedAt: { bsonType: "date" },
        hash: { bsonType: "string" }
      }
    }
  }
});

// Add unique index on hash to prevent tampering
db.dockets.createIndex({ "hash": 1 }, { unique: true });

// Time-series index for efficient range queries
db.dockets.createIndex({ "registerId": 1, "height": 1 });
```

---

## Summary

This architecture provides:

1. **Clear separation of concerns** - Three tiers with distinct responsibilities
2. **Flexibility** - Pluggable providers for different deployment scenarios
3. **Performance** - Hot caching for frequently accessed data
4. **Security** - Cryptographic verification for ledger data (Register Service)
5. **Compliance** - WORM storage for immutable audit trails
6. **Resilience** - Peer recovery for corrupted data

### Key Architectural Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Tenant/Wallet Storage** | PostgreSQL (EF Core) | Relational data, ACID transactions, schema migrations |
| **Blueprint Storage** | MongoDB | Complex nested JSON documents, schema flexibility |
| **Register Storage** | Verified In-Memory Cache + Cold WORM | Cryptographic verification required, single source of truth |
| **Hot Tier** | Redis | Industry standard, .NET Aspire native support |
| **Cold Archival** | Cloud Blob (Azure/S3) | Cost-effective long-term storage |

### Register Service - Unique Model

The Register Service does NOT follow the traditional cache pattern:

| Aspect | Traditional Cache | Verified Cache (Register) |
|--------|-------------------|---------------------------|
| **Trust Model** | Trust storage, cache for speed | Don't trust storage, verify everything |
| **Cache Miss** | Read from storage | Should not happen (all verified data in cache) |
| **Corruption** | Return error | Request from peer network |
| **Write Path** | Write to storage, invalidate cache | Verify, then write to both atomically |
| **Startup** | Lazy load on demand | Load and verify entire chain |

---

## Next Steps

1. Review and approve this proposal
2. Create `Sorcha.Storage.Abstractions` project with core interfaces
3. Implement `IVerifiedRegisterCache` for Register Service
4. Implement `ICacheStore` with Redis for Tenant/Wallet/Blueprint
5. Implement warm tier repositories (EF Core for relational, MongoDB for documents)
