// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Storage.MongoDB.Mappers;
using Sorcha.Register.Storage.MongoDB.Serialization;
using MongoModels = Sorcha.Register.Storage.MongoDB.Models;
using RegisterEntity = Sorcha.Register.Models.Register;

namespace Sorcha.Register.Storage.MongoDB;

/// <summary>
/// MongoDB implementation of IRegisterRepository.
/// Supports two architectures:
/// 1. Per-Register Databases (recommended): Each register gets its own database for isolation
/// 2. Single Database: All data in one database (legacy, for testing)
/// </summary>
public class MongoRegisterRepository : IRegisterRepository
{
    private static readonly object ClassMapLock = new();
    private static volatile bool _classMapRegistered;

    private readonly IMongoClient _client;
    private readonly IMongoCollection<RegisterEntity> _registers;
    private readonly MongoRegisterStorageConfiguration _config;
    private readonly ILogger<MongoRegisterRepository> _logger;

    // Legacy: used only when UseDatabasePerRegister = false
    private readonly IMongoCollection<TransactionModel>? _legacyTransactions;
    private readonly IMongoCollection<MongoModels.MongoTransactionDocument>? _legacyMongoTransactions;
    private readonly IMongoCollection<Docket>? _legacyDockets;

    // Lazy async index creation to avoid .GetAwaiter().GetResult() deadlock risk in constructor
    private readonly SemaphoreSlim _indexInitLock = new(1, 1);
    private bool _indexesCreated;

    /// <summary>
    /// Initializes a new instance of the MongoRegisterRepository.
    /// </summary>
    /// <param name="client">The MongoDB client instance, provided by DI.</param>
    /// <param name="options">MongoDB storage configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public MongoRegisterRepository(
        IMongoClient client,
        IOptions<MongoRegisterStorageConfiguration> options,
        ILogger<MongoRegisterRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(options?.Value);

        RegisterBsonClassMaps();

        _config = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _client = client ?? throw new ArgumentNullException(nameof(client));

        // Registry database holds register metadata
        var registryDatabase = _client.GetDatabase(_config.DatabaseName);
        _registers = registryDatabase.GetCollection<RegisterEntity>(_config.RegisterCollectionName);

        // Legacy single-database mode
        if (!_config.UseDatabasePerRegister)
        {
            _legacyTransactions = registryDatabase.GetCollection<TransactionModel>(_config.TransactionCollectionName);
            _legacyMongoTransactions = registryDatabase.GetCollection<MongoModels.MongoTransactionDocument>(_config.TransactionCollectionName);
            _legacyDockets = registryDatabase.GetCollection<Docket>(_config.DocketCollectionName);
        }

        // Index creation is deferred to the first async operation via EnsureIndexesCreatedAsync()
        // to avoid .GetAwaiter().GetResult() deadlock risk in constructor.

        _logger.LogInformation(
            "MongoRegisterRepository initialized. Mode: {Mode}, Registry: {DatabaseName}",
            _config.UseDatabasePerRegister ? "Per-Register Databases" : "Single Database",
            _config.DatabaseName);
    }

    /// <summary>
    /// Initializes a new instance for testing with explicit database (legacy single-database mode).
    /// </summary>
    public MongoRegisterRepository(
        IMongoDatabase database,
        string registerCollection,
        string transactionCollection,
        string docketCollection,
        ILogger<MongoRegisterRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(database);

        RegisterBsonClassMaps();

        _config = new MongoRegisterStorageConfiguration
        {
            UseDatabasePerRegister = false, // Legacy mode for tests
            RegisterCollectionName = registerCollection,
            TransactionCollectionName = transactionCollection,
            DocketCollectionName = docketCollection
        };

        _client = database.Client;
        _registers = database.GetCollection<RegisterEntity>(registerCollection);
        _legacyTransactions = database.GetCollection<TransactionModel>(transactionCollection);
        _legacyMongoTransactions = database.GetCollection<MongoModels.MongoTransactionDocument>(transactionCollection);
        _legacyDockets = database.GetCollection<Docket>(docketCollection);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers BsonClassMaps for TransactionModel, PayloadModel, and Challenge
    /// so that read operations can deserialize both BSON Binary (new) and BSON String (legacy) fields.
    /// Thread-safe, idempotent.
    /// </summary>
    private static void RegisterBsonClassMaps()
    {
        if (_classMapRegistered) return;

        lock (ClassMapLock)
        {
            if (_classMapRegistered) return;

            if (!BsonClassMap.IsClassMapRegistered(typeof(TransactionModel)))
            {
                BsonClassMap.RegisterClassMap<TransactionModel>(cm =>
                {
                    cm.AutoMap();
                    // Override convention: TransactionModel.Id is a JSON-LD field, NOT the MongoDB _id.
                    // MongoTransactionDocument has its own MongoId mapped to _id.
                    cm.SetIdMember(null);
                    cm.GetMemberMap(x => x.Id)
                        .SetElementName("Id");
                    // Ignore _id and other MongoDB-internal fields when reading back
                    cm.SetIgnoreExtraElements(true);
                    cm.GetMemberMap(x => x.Signature)
                        .SetSerializer(new BinaryAwareStringSerializer());
                });
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(PayloadModel)))
            {
                BsonClassMap.RegisterClassMap<PayloadModel>(cm =>
                {
                    cm.AutoMap();
                    cm.GetMemberMap(x => x.Data)
                        .SetSerializer(new BinaryAwareStringSerializer());
                    cm.GetMemberMap(x => x.Hash)
                        .SetSerializer(new BinaryAwareStringSerializer());
                });
            }

            if (!BsonClassMap.IsClassMapRegistered(typeof(Challenge)))
            {
                BsonClassMap.RegisterClassMap<Challenge>(cm =>
                {
                    cm.AutoMap();
                    cm.GetMemberMap(x => x.Data)
                        .SetSerializer(new BinaryAwareStringSerializer());
                });
            }

            _classMapRegistered = true;
        }
    }

    /// <summary>
    /// Gets the MongoDB database for a specific register's data.
    /// </summary>
    private IMongoDatabase GetRegisterDatabase(string registerId)
    {
        if (!_config.UseDatabasePerRegister)
        {
            // Legacy: all data in one database
            return _client.GetDatabase(_config.DatabaseName);
        }

        // Per-register database: sorcha_register_{registerId}
        var dbName = $"{_config.DatabaseNamePrefix}{registerId}";
        return _client.GetDatabase(dbName);
    }

    /// <summary>
    /// Gets the transactions collection for a specific register (TransactionModel view for reads/queries).
    /// </summary>
    private IMongoCollection<TransactionModel> GetTransactionsCollection(string registerId)
    {
        if (!_config.UseDatabasePerRegister)
        {
            return _legacyTransactions ?? throw new InvalidOperationException("Legacy transactions collection not initialized");
        }

        var db = GetRegisterDatabase(registerId);
        return db.GetCollection<TransactionModel>(_config.TransactionCollectionName);
    }

    /// <summary>
    /// Gets the transactions collection for a specific register (MongoTransactionDocument view for inserts).
    /// Points to the same underlying MongoDB collection but uses BSON Binary serialization.
    /// </summary>
    private IMongoCollection<MongoModels.MongoTransactionDocument> GetMongoTransactionsCollection(string registerId)
    {
        if (!_config.UseDatabasePerRegister)
        {
            return _legacyMongoTransactions ?? throw new InvalidOperationException("Legacy mongo transactions collection not initialized");
        }

        var db = GetRegisterDatabase(registerId);
        return db.GetCollection<MongoModels.MongoTransactionDocument>(_config.TransactionCollectionName);
    }

    /// <summary>
    /// Gets the dockets collection for a specific register.
    /// </summary>
    private IMongoCollection<Docket> GetDocketsCollection(string registerId)
    {
        if (!_config.UseDatabasePerRegister)
        {
            return _legacyDockets ?? throw new InvalidOperationException("Legacy dockets collection not initialized");
        }

        var db = GetRegisterDatabase(registerId);
        return db.GetCollection<Docket>(_config.DocketCollectionName);
    }

    /// <summary>
    /// Ensures indexes are created exactly once, lazily on the first async operation.
    /// Uses a SemaphoreSlim for thread-safe one-time initialization.
    /// </summary>
    private async Task EnsureIndexesCreatedAsync()
    {
        if (_indexesCreated || !_config.CreateIndexesOnStartup) return;

        await _indexInitLock.WaitAsync();
        try
        {
            if (_indexesCreated) return;
            await CreateIndexesAsync();
            _indexesCreated = true;
        }
        finally
        {
            _indexInitLock.Release();
        }
    }

    /// <summary>
    /// Creates indexes for optimal query performance.
    /// </summary>
    private async Task CreateIndexesAsync()
    {
        _logger.LogInformation("Creating MongoDB indexes for Register storage");

        // Register indexes (in registry database)
        var registerIndexes = new List<CreateIndexModel<RegisterEntity>>
        {
            new(Builders<RegisterEntity>.IndexKeys.Ascending(r => r.TenantId)),
            new(Builders<RegisterEntity>.IndexKeys.Ascending(r => r.Status)),
            new(Builders<RegisterEntity>.IndexKeys.Ascending(r => r.Name))
        };
        await _registers.Indexes.CreateManyAsync(registerIndexes);

        // For legacy mode, create indexes in single database
        if (!_config.UseDatabasePerRegister && _legacyTransactions != null && _legacyDockets != null)
        {
            await CreateTransactionIndexesAsync(_legacyTransactions);
            await CreateDocketIndexesAsync(_legacyDockets);
        }

        _logger.LogInformation("MongoDB indexes created successfully");
    }

    /// <summary>
    /// Creates indexes for a register's data (called when register is first created).
    /// </summary>
    private async Task CreateRegisterIndexesAsync(string registerId)
    {
        if (!_config.UseDatabasePerRegister)
        {
            return; // Indexes already created in legacy mode
        }

        _logger.LogDebug("Creating indexes for register {RegisterId}", registerId);

        var transactions = GetTransactionsCollection(registerId);
        var dockets = GetDocketsCollection(registerId);

        await CreateTransactionIndexesAsync(transactions);
        await CreateDocketIndexesAsync(dockets);

        _logger.LogDebug("Indexes created for register {RegisterId}", registerId);
    }

    private static async Task CreateTransactionIndexesAsync(IMongoCollection<TransactionModel> collection)
    {
        var transactionIndexes = new List<CreateIndexModel<TransactionModel>>
        {
            // Index for txId lookups
            new(Builders<TransactionModel>.IndexKeys.Ascending(t => t.TxId),
                new CreateIndexOptions { Unique = true }),

            // Index for sender address queries
            new(Builders<TransactionModel>.IndexKeys.Ascending(t => t.SenderWallet)),

            // Index for timestamp-based queries
            new(Builders<TransactionModel>.IndexKeys.Descending(t => t.TimeStamp)),

            // Index for docket number queries
            new(Builders<TransactionModel>.IndexKeys.Ascending(t => t.DocketNumber)),

            // Index for blueprint queries
            new(Builders<TransactionModel>.IndexKeys
                .Ascending("MetaData.BlueprintId")
                .Ascending("MetaData.InstanceId")),

            // Index for previous transaction ID queries (fork detection)
            new(Builders<TransactionModel>.IndexKeys.Ascending(t => t.PrevTxId)),

            // Index for transaction type queries (participant record lookups)
            new(Builders<TransactionModel>.IndexKeys.Ascending("MetaData.TransactionType"))
        };
        await collection.Indexes.CreateManyAsync(transactionIndexes);
    }

    private static async Task CreateDocketIndexesAsync(IMongoCollection<Docket> collection)
    {
        var docketIndexes = new List<CreateIndexModel<Docket>>
        {
            // Note: Id maps to _id which is already unique by default in MongoDB
            // No need to create a separate unique index on it

            // Index for hash lookups
            new(Builders<Docket>.IndexKeys.Ascending(d => d.Hash)),

            // Index for state queries
            new(Builders<Docket>.IndexKeys.Ascending(d => d.State))
        };
        await collection.Indexes.CreateManyAsync(docketIndexes);
    }

    // ===========================
    // Register Operations
    // ===========================

    /// <inheritdoc/>
    public async Task<bool> IsLocalRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        await EnsureIndexesCreatedAsync();
        var filter = Builders<RegisterEntity>.Filter.Eq(r => r.Id, registerId);
        var count = await _registers.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<RegisterEntity>> GetRegistersAsync(CancellationToken cancellationToken = default)
    {
        await EnsureIndexesCreatedAsync();
        return await _registers.Find(FilterDefinition<RegisterEntity>.Empty)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<RegisterEntity>> QueryRegistersAsync(
        Func<RegisterEntity, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        // For simple predicates, we need to fetch and filter in memory
        // For production, consider using expression-based predicates
        var all = await GetRegistersAsync(cancellationToken);
        return all.Where(predicate);
    }

    /// <inheritdoc/>
    public async Task<RegisterEntity?> GetRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        await EnsureIndexesCreatedAsync();
        var filter = Builders<RegisterEntity>.Filter.Eq(r => r.Id, registerId);
        return await _registers.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RegisterEntity> InsertRegisterAsync(RegisterEntity newRegister, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newRegister);
        await EnsureIndexesCreatedAsync();

        await _registers.InsertOneAsync(newRegister, new InsertOneOptions(), cancellationToken);
        _logger.LogDebug("Inserted register {RegisterId}", newRegister.Id);

        // Create indexes for this register's database
        if (_config.CreateIndexesOnStartup)
        {
            await CreateRegisterIndexesAsync(newRegister.Id);
        }

        return newRegister;
    }

    /// <inheritdoc/>
    public async Task<RegisterEntity> UpdateRegisterAsync(RegisterEntity register, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(register);

        register.UpdatedAt = DateTime.UtcNow;
        var filter = Builders<RegisterEntity>.Filter.Eq(r => r.Id, register.Id);
        await _registers.ReplaceOneAsync(filter, register, new ReplaceOptions { IsUpsert = false }, cancellationToken);
        _logger.LogDebug("Updated register {RegisterId}", register.Id);
        return register;
    }

    /// <inheritdoc/>
    public async Task DeleteRegisterAsync(string registerId, CancellationToken cancellationToken = default)
    {
        if (_config.UseDatabasePerRegister)
        {
            // Drop the entire register database
            var dbName = $"{_config.DatabaseNamePrefix}{registerId}";
            await _client.DropDatabaseAsync(dbName, cancellationToken);
            _logger.LogInformation("Dropped database {DatabaseName} for register {RegisterId}", dbName, registerId);
        }
        else
        {
            // Legacy: Delete documents from collections
            var txFilter = Builders<TransactionModel>.Filter.Eq(t => t.RegisterId, registerId);
            await _legacyTransactions!.DeleteManyAsync(txFilter, cancellationToken);

            var docketFilter = Builders<Docket>.Filter.Eq(d => d.RegisterId, registerId);
            await _legacyDockets!.DeleteManyAsync(docketFilter, cancellationToken);
        }

        // Delete the register metadata
        var registerFilter = Builders<RegisterEntity>.Filter.Eq(r => r.Id, registerId);
        await _registers.DeleteOneAsync(registerFilter, cancellationToken);

        _logger.LogInformation("Deleted register {RegisterId} with all associated data", registerId);
    }

    /// <inheritdoc/>
    public async Task<int> CountRegistersAsync(CancellationToken cancellationToken = default)
    {
        var count = await _registers.EstimatedDocumentCountAsync(cancellationToken: cancellationToken);
        return (int)count;
    }

    // ===========================
    // Docket Operations
    // ===========================

    /// <inheritdoc/>
    public async Task<IEnumerable<Docket>> GetDocketsAsync(string registerId, CancellationToken cancellationToken = default)
    {
        var dockets = GetDocketsCollection(registerId);
        return await dockets.Find(FilterDefinition<Docket>.Empty)
            .SortBy(d => d.Id)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Docket?> GetDocketAsync(string registerId, ulong docketId, CancellationToken cancellationToken = default)
    {
        var dockets = GetDocketsCollection(registerId);
        var filter = Builders<Docket>.Filter.Eq(d => d.Id, docketId);
        return await dockets.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Docket> InsertDocketAsync(Docket docket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(docket);

        var dockets = GetDocketsCollection(docket.RegisterId);
        await dockets.InsertOneAsync(docket, new InsertOneOptions(), cancellationToken);
        _logger.LogDebug("Inserted docket {DocketId} for register {RegisterId}", docket.Id, docket.RegisterId);
        return docket;
    }

    /// <inheritdoc/>
    public async Task UpdateRegisterHeightAsync(string registerId, uint newHeight, CancellationToken cancellationToken = default)
    {
        var filter = Builders<RegisterEntity>.Filter.Eq(r => r.Id, registerId);
        var update = Builders<RegisterEntity>.Update
            .Set(r => r.Height, newHeight)
            .Set(r => r.UpdatedAt, DateTime.UtcNow);

        await _registers.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        _logger.LogDebug("Updated register {RegisterId} height to {Height}", registerId, newHeight);
    }

    // ===========================
    // Transaction Operations
    // ===========================

    /// <inheritdoc/>
    public async Task<IQueryable<TransactionModel>> GetTransactionsAsync(string registerId, CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var allTransactions = await transactions.Find(FilterDefinition<TransactionModel>.Empty).ToListAsync(cancellationToken);
        return allTransactions.AsQueryable();
    }

    /// <inheritdoc/>
    public async Task<TransactionModel?> GetTransactionAsync(string registerId, string transactionId, CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.Eq(t => t.TxId, transactionId);
        return await transactions.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<TransactionModel> InsertTransactionAsync(TransactionModel transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        // Generate DID URI if not set
        if (string.IsNullOrEmpty(transaction.Id))
        {
            transaction.Id = transaction.GenerateDidUri();
        }

        // Convert to MongoTransactionDocument for BSON Binary storage (~33% savings for binary fields)
        var mongoDocument = MongoDocumentMapper.ToMongoDocument(transaction);
        var mongoCollection = GetMongoTransactionsCollection(transaction.RegisterId);
        await mongoCollection.InsertOneAsync(mongoDocument, new InsertOneOptions(), cancellationToken);

        _logger.LogDebug("Inserted transaction {TxId} in register {RegisterId}", transaction.TxId, transaction.RegisterId);
        return transaction;
    }

    /// <inheritdoc/>
    public async Task DeleteTransactionAsync(
        string registerId,
        string txId,
        CancellationToken cancellationToken = default)
    {
        var collection = GetMongoTransactionsCollection(registerId);
        var filter = Builders<MongoModels.MongoTransactionDocument>.Filter.Eq(t => t.TxId, txId);
        var result = await collection.DeleteOneAsync(filter, cancellationToken);
        _logger.LogDebug("Deleted transaction {TxId} from register {RegisterId} (matched: {Count})",
            txId, registerId, result.DeletedCount);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TransactionModel>> QueryTransactionsAsync(
        string registerId,
        Expression<Func<TransactionModel, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.Where(predicate);
        return await transactions.Find(filter).ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByDocketAsync(
        string registerId,
        ulong docketId,
        CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.Eq(t => t.DocketNumber, docketId);
        return await transactions.Find(filter)
            .SortBy(t => t.TimeStamp)
            .ToListAsync(cancellationToken);
    }

    // ===========================
    // Advanced Queries
    // ===========================

    /// <inheritdoc/>
    public async Task<IEnumerable<TransactionModel>> GetAllTransactionsByRecipientAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.AnyEq(t => t.RecipientsWallets, address);
        return await transactions.Find(filter)
            .SortByDescending(t => t.TimeStamp)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TransactionModel>> GetAllTransactionsBySenderAddressAsync(
        string registerId,
        string address,
        CancellationToken cancellationToken = default)
    {
        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.Eq(t => t.SenderWallet, address);
        return await transactions.Find(filter)
            .SortByDescending(t => t.TimeStamp)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<TransactionModel>> GetTransactionsByPrevTxIdAsync(
        string registerId,
        string prevTxId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(prevTxId))
        {
            return Enumerable.Empty<TransactionModel>();
        }

        var transactions = GetTransactionsCollection(registerId);
        var filter = Builders<TransactionModel>.Filter.Eq(t => t.PrevTxId, prevTxId);
        return await transactions.Find(filter)
            .SortByDescending(t => t.TimeStamp)
            .ToListAsync(cancellationToken);
    }
}
