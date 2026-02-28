// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed memory pool manager for pending transactions.
/// Persists across container restarts.
/// </summary>
public class MemPoolManager : IMemPoolManager
{
    private const string RedisKeyPrefix = "validator:mempool:";
    private const string PayloadHashIndexKey = "validator:mempool:payload-hashes";

    private readonly IConnectionMultiplexer _redis;
    private readonly MemPoolConfiguration _config;
    private readonly ILogger<MemPoolManager> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public MemPoolManager(
        IConnectionMultiplexer redis,
        IOptions<MemPoolConfiguration> config,
        ILogger<MemPoolManager> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Adds a transaction to the memory pool (Redis-backed)
    /// </summary>
    public Task<bool> AddTransactionAsync(string registerId, Transaction transaction, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(registerId))
            throw new ArgumentException("Register ID is required", nameof(registerId));

        if (transaction == null)
            throw new ArgumentNullException(nameof(transaction));

        try
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{RedisKeyPrefix}{registerId}:transactions";

            // Check if transaction already exists
            if (db.HashExists(hashKey, transaction.TransactionId))
            {
                _logger.LogWarning("Transaction {TransactionId} already exists in memory pool for register {RegisterId}",
                    transaction.TransactionId, registerId);
                return Task.FromResult(false);
            }

            // Set added timestamp
            transaction.AddedToPoolAt = DateTimeOffset.UtcNow;

            // Serialize and store in Redis
            var json = JsonSerializer.Serialize(transaction, JsonOptions);
            db.HashSet(hashKey, transaction.TransactionId, json);

            // Add payload hash to secondary index for fast duplicate detection
            if (!string.IsNullOrWhiteSpace(transaction.PayloadHash))
            {
                db.SetAdd(PayloadHashIndexKey, transaction.PayloadHash);
            }

            _logger.LogInformation("Added transaction {TransactionId} to memory pool for register {RegisterId} (persisted to Redis)",
                transaction.TransactionId, registerId);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add transaction {TransactionId} to memory pool for register {RegisterId}",
                transaction.TransactionId, registerId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Removes a transaction from the memory pool
    /// </summary>
    public Task<bool> RemoveTransactionAsync(string registerId, string transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{RedisKeyPrefix}{registerId}:transactions";

            // Retrieve the transaction to get its payload hash before removing
            var existingJson = db.HashGet(hashKey, transactionId);
            var removed = db.HashDelete(hashKey, transactionId);

            if (removed && existingJson.HasValue)
            {
                try
                {
                    var transaction = JsonSerializer.Deserialize<Transaction>(existingJson.ToString(), JsonOptions);
                    if (transaction != null && !string.IsNullOrWhiteSpace(transaction.PayloadHash))
                    {
                        db.SetRemove(PayloadHashIndexKey, transaction.PayloadHash);
                    }
                }
                catch (JsonException)
                {
                    // If we can't deserialize, just skip index cleanup
                }

                _logger.LogInformation("Removed transaction {TransactionId} from memory pool for register {RegisterId}",
                    transactionId, registerId);
            }

            return Task.FromResult(removed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove transaction {TransactionId} from memory pool for register {RegisterId}",
                transactionId, registerId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Gets pending transactions for docket building
    /// </summary>
    public Task<List<Transaction>> GetPendingTransactionsAsync(string registerId, int maxCount, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{RedisKeyPrefix}{registerId}:transactions";

            var entries = db.HashGetAll(hashKey);
            var transactions = new List<Transaction>();

            foreach (var entry in entries.Take(maxCount))
            {
                try
                {
                    var transaction = JsonSerializer.Deserialize<Transaction>(entry.Value.ToString(), JsonOptions);
                    if (transaction != null)
                    {
                        transactions.Add(transaction);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize transaction from Redis for register {RegisterId}", registerId);
                }
            }

            _logger.LogDebug("Retrieved {Count} transactions from memory pool for register {RegisterId}",
                transactions.Count, registerId);

            return Task.FromResult(transactions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transactions from memory pool for register {RegisterId}", registerId);
            return Task.FromResult(new List<Transaction>());
        }
    }

    /// <summary>
    /// Gets the transaction count for a register
    /// </summary>
    public Task<int> GetTransactionCountAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{RedisKeyPrefix}{registerId}:transactions";
            var count = (int)db.HashLength(hashKey);
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get transaction count for register {RegisterId}", registerId);
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Gets memory pool statistics
    /// </summary>
    public Task<MemPoolStats> GetStatsAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var hashKey = $"{RedisKeyPrefix}{registerId}:transactions";
            var count = (int)db.HashLength(hashKey);

            var stats = new MemPoolStats
            {
                RegisterId = registerId,
                TotalTransactions = count,
                MaxSize = _config.MaxSize
            };

            return Task.FromResult(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get stats for register {RegisterId}", registerId);
            return Task.FromResult(new MemPoolStats
            {
                RegisterId = registerId,
                MaxSize = _config.MaxSize
            });
        }
    }

    /// <summary>
    /// Checks if a transaction with the given payload hash exists in any memory pool
    /// </summary>
    public Task<bool> TransactionExistsAsync(string payloadHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payloadHash))
            return Task.FromResult(false);

        try
        {
            var db = _redis.GetDatabase();
            var exists = db.SetContains(PayloadHashIndexKey, payloadHash);
            return Task.FromResult(exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if transaction with payload hash {PayloadHash} exists in memory pool", payloadHash);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Cleans up expired transactions from all memory pools
    /// </summary>
    public Task CleanupExpiredTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints()[0]);
            var totalExpired = 0;

            // Find all mempool keys
            foreach (var key in server.Keys(pattern: $"{RedisKeyPrefix}*:transactions"))
            {
                var entries = db.HashGetAll(key);
                var now = DateTimeOffset.UtcNow;

                foreach (var entry in entries)
                {
                    try
                    {
                        var transaction = JsonSerializer.Deserialize<Transaction>(entry.Value.ToString(), JsonOptions);
                        if (transaction != null && transaction.AddedToPoolAt.Add(_config.DefaultTTL) < now)
                        {
                            db.HashDelete(key, entry.Name);

                            // Remove payload hash from secondary index
                            if (!string.IsNullOrWhiteSpace(transaction.PayloadHash))
                            {
                                db.SetRemove(PayloadHashIndexKey, transaction.PayloadHash);
                            }

                            totalExpired++;
                        }
                    }
                    catch (JsonException)
                    {
                        // Remove corrupted entry
                        db.HashDelete(key, entry.Name);
                        totalExpired++;
                    }
                }
            }

            if (totalExpired > 0)
            {
                _logger.LogInformation("Cleanup removed {TotalExpired} expired transactions from memory pools", totalExpired);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired transactions");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Returns transactions to the memory pool (after failed consensus)
    /// </summary>
    public async Task ReturnTransactionsAsync(string registerId, List<Transaction> transactions, CancellationToken cancellationToken = default)
    {
        if (transactions == null || transactions.Count == 0)
            return;

        _logger.LogInformation("Returning {Count} transactions to memory pool for register {RegisterId}",
            transactions.Count, registerId);

        foreach (var transaction in transactions)
        {
            // Preserve original timestamp and priority
            await AddTransactionAsync(registerId, transaction, cancellationToken);
        }
    }
}
