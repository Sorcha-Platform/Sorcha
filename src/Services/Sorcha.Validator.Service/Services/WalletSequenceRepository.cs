// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Interface for wallet sequence number storage (SEC-AUDIT 4.2).
/// </summary>
public interface IWalletSequenceRepository
{
    /// <summary>
    /// Get the current sequence number for a wallet on a register.
    /// Returns 0 if no transactions have been recorded.
    /// </summary>
    Task<ulong> GetSequenceNumberAsync(string registerId, string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Atomically validate and increment the sequence number.
    /// Returns true if the provided sequence number is valid (lastKnown + 1).
    /// Only increments on success — rejected transactions do not affect the counter.
    /// </summary>
    Task<bool> ValidateAndIncrementAsync(string registerId, string walletAddress, ulong expectedSequence, CancellationToken ct = default);
}

/// <summary>
/// MongoDB-backed wallet sequence number repository.
/// Uses atomic findOneAndUpdate for concurrency safety.
/// </summary>
public class WalletSequenceRepository : IWalletSequenceRepository
{
    private readonly IMongoCollection<WalletSequenceDocument> _collection;
    private readonly ILogger<WalletSequenceRepository> _logger;
    private bool _indexesCreated;

    public WalletSequenceRepository(
        IMongoClient mongoClient,
        IOptions<ValidatorRegistryConfiguration> config,
        ILogger<WalletSequenceRepository> logger)
    {
        var cfg = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var database = mongoClient.GetDatabase(cfg.MongoDatabase);
        _collection = database.GetCollection<WalletSequenceDocument>(cfg.WalletSequencesCollection);
    }

    private async Task EnsureIndexesAsync()
    {
        if (_indexesCreated) return;

        try
        {
            var indexes = new List<CreateIndexModel<WalletSequenceDocument>>
            {
                new(Builders<WalletSequenceDocument>.IndexKeys
                        .Ascending(d => d.RegisterId)
                        .Ascending(d => d.WalletAddress),
                    new CreateIndexOptions { Unique = true })
            };
            await _collection.Indexes.CreateManyAsync(indexes);
            _indexesCreated = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create wallet sequence indexes — will retry");
        }
    }

    /// <inheritdoc/>
    public async Task<ulong> GetSequenceNumberAsync(string registerId, string walletAddress, CancellationToken ct = default)
    {
        await EnsureIndexesAsync();

        var filter = Builders<WalletSequenceDocument>.Filter.And(
            Builders<WalletSequenceDocument>.Filter.Eq(d => d.RegisterId, registerId),
            Builders<WalletSequenceDocument>.Filter.Eq(d => d.WalletAddress, walletAddress));

        var doc = await _collection.Find(filter).FirstOrDefaultAsync(ct);
        return doc?.LastSequenceNumber ?? 0;
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateAndIncrementAsync(string registerId, string walletAddress, ulong expectedSequence, CancellationToken ct = default)
    {
        if (expectedSequence == 0) return false; // Guard against ulong underflow

        await EnsureIndexesAsync();

        var expectedPrevious = expectedSequence - 1;

        // Atomic: only increment if current value matches expectedPrevious
        var filter = Builders<WalletSequenceDocument>.Filter.And(
            Builders<WalletSequenceDocument>.Filter.Eq(d => d.RegisterId, registerId),
            Builders<WalletSequenceDocument>.Filter.Eq(d => d.WalletAddress, walletAddress),
            Builders<WalletSequenceDocument>.Filter.Eq(d => d.LastSequenceNumber, expectedPrevious));

        var update = Builders<WalletSequenceDocument>.Update
            .Set(d => d.LastSequenceNumber, expectedSequence)
            .Set(d => d.LastUpdatedAt, DateTimeOffset.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);

        if (result.MatchedCount > 0)
            return true;

        // If no match, check if this is a first transaction (sequence 1, no existing document)
        if (expectedSequence == 1)
        {
            try
            {
                await _collection.InsertOneAsync(new WalletSequenceDocument
                {
                    Id = $"{registerId}:{walletAddress}",
                    RegisterId = registerId,
                    WalletAddress = walletAddress,
                    LastSequenceNumber = 1,
                    LastUpdatedAt = DateTimeOffset.UtcNow
                }, cancellationToken: ct);
                return true;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Another transaction beat us — sequence conflict
                _logger.LogWarning("Sequence conflict for wallet {WalletAddress} on register {RegisterId} — concurrent first transaction",
                    walletAddress, registerId);
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// MongoDB document for wallet sequence storage.
    /// </summary>
    internal class WalletSequenceDocument
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;
        public string RegisterId { get; set; } = string.Empty;
        public string WalletAddress { get; set; } = string.Empty;
        public ulong LastSequenceNumber { get; set; }
        public DateTimeOffset LastUpdatedAt { get; set; }
    }
}
