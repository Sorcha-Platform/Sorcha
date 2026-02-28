// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed transaction pool poller for unverified transactions.
/// Uses Redis Lists for FIFO ordering and Hashes for quick lookup.
/// </summary>
public class TransactionPoolPoller : ITransactionPoolPoller
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly TransactionPoolPollerConfiguration _config;
    private readonly ILogger<TransactionPoolPoller> _logger;
    private readonly ResiliencePipeline _pipeline;
    private readonly JsonSerializerOptions _jsonOptions;

    // Statistics (per-register stats stored in Redis, global stats in memory)
    private long _totalSubmitted;
    private long _totalPolled;
    private long _totalReturned;
    private long _totalExpired;

    // Redis key patterns
    // Queue: {prefix}{registerId}:queue - List for FIFO ordering
    // Data:  {prefix}{registerId}:data:{txId} - String for transaction data
    // Expiry: {prefix}{registerId}:expiry - Sorted set for expiration tracking

    public TransactionPoolPoller(
        IConnectionMultiplexer redis,
        IOptions<TransactionPoolPollerConfiguration> config,
        ILogger<TransactionPoolPoller> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _database = _redis.GetDatabase();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // CRITICAL: Use relaxed encoding so characters like '+' in DateTimeOffset and base64
            // are stored literally, not as \u002B. ValidationEngine hashes GetRawText() — escaping
            // differences between serialization boundaries would break payload hash verification.
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        _pipeline = BuildResiliencePipeline();
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new Polly.Retry.RetryStrategyOptions
            {
                MaxRetryAttempts = _config.MaxRetries,
                Delay = _config.RetryDelay,
                BackoffType = DelayBackoffType.Exponential
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30)
            })
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();
    }


    private string GetQueueKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:queue";

    private string GetDataKey(string registerId, string transactionId) =>
        $"{_config.KeyPrefix}{registerId}:data:{transactionId}";

    private string GetExpiryKey(string registerId) =>
        $"{_config.KeyPrefix}{registerId}:expiry";


    /// <inheritdoc/>
    public async Task<bool> SubmitTransactionAsync(
        string registerId,
        Transaction transaction,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transaction);

        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var queueKey = GetQueueKey(registerId);
                var dataKey = GetDataKey(registerId, transaction.TransactionId);
                var expiryKey = GetExpiryKey(registerId);

                // Check if transaction already exists
                if (await _database.KeyExistsAsync(dataKey))
                {
                    _logger.LogWarning(
                        "Transaction {TransactionId} already exists in unverified pool for register {RegisterId}",
                        transaction.TransactionId, registerId);
                    return false;
                }

                // Serialize transaction
                var json = JsonSerializer.Serialize(transaction, _jsonOptions);

                // Calculate expiry timestamp
                var expiresAt = transaction.ExpiresAt ?? DateTimeOffset.UtcNow.Add(_config.TransactionTtl);
                var expiryScore = expiresAt.ToUnixTimeSeconds();

                // Store transaction data with TTL
                var ttl = expiresAt - DateTimeOffset.UtcNow;
                if (ttl <= TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        "Transaction {TransactionId} already expired, not adding to pool",
                        transaction.TransactionId);
                    return false;
                }

                // Use transaction to ensure atomicity
                var redisTransaction = _database.CreateTransaction();

                // Store transaction data
                _ = redisTransaction.StringSetAsync(dataKey, json, ttl);

                // Add to queue (LPUSH for FIFO - poll from right with RPOP)
                _ = redisTransaction.ListLeftPushAsync(queueKey, transaction.TransactionId);

                // Add to expiry tracking sorted set
                _ = redisTransaction.SortedSetAddAsync(expiryKey, transaction.TransactionId, expiryScore);

                var committed = await redisTransaction.ExecuteAsync();

                if (committed)
                {
                    Interlocked.Increment(ref _totalSubmitted);
                    _logger.LogDebug(
                        "Submitted transaction {TransactionId} to unverified pool for register {RegisterId}",
                        transaction.TransactionId, registerId);
                }

                return committed;
            }, ct);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker open, cannot submit transaction {TransactionId}", transaction.TransactionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit transaction {TransactionId} to unverified pool", transaction.TransactionId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Transaction>> PollTransactionsAsync(
        string registerId,
        int maxCount,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        if (maxCount <= 0)
            return [];

        var transactions = new List<Transaction>();

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var queueKey = GetQueueKey(registerId);

                for (var i = 0; i < maxCount; i++)
                {
                    // Pop transaction ID from queue
                    var transactionId = await _database.ListRightPopAsync(queueKey);

                    if (transactionId.IsNullOrEmpty)
                        break;

                    var txId = transactionId.ToString();
                    var dataKey = GetDataKey(registerId, txId);

                    // Get transaction data
                    var json = await _database.StringGetAsync(dataKey);

                    if (json.IsNullOrEmpty)
                    {
                        // Transaction data expired or was removed, skip
                        _logger.LogDebug(
                            "Transaction {TransactionId} data not found (expired?), skipping",
                            txId);
                        continue;
                    }

                    // Deserialize transaction
                    var transaction = JsonSerializer.Deserialize<Transaction>(json.ToString(), _jsonOptions);

                    if (transaction != null)
                    {
                        transactions.Add(transaction);

                        // Remove from data store (we've polled it)
                        await _database.KeyDeleteAsync(dataKey);

                        // Remove from expiry tracking
                        var expiryKey = GetExpiryKey(registerId);
                        await _database.SortedSetRemoveAsync(expiryKey, txId);
                    }
                }
            }, ct);

            if (transactions.Count > 0)
            {
                Interlocked.Add(ref _totalPolled, transactions.Count);
                _logger.LogDebug(
                    "Polled {Count} transactions from unverified pool for register {RegisterId}",
                    transactions.Count, registerId);
            }
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker open, cannot poll transactions for register {RegisterId}", registerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to poll transactions from unverified pool for register {RegisterId}", registerId);
        }

        return transactions;
    }

    /// <inheritdoc/>
    public async Task ReturnTransactionsAsync(
        string registerId,
        IReadOnlyList<Transaction> transactions,
        CancellationToken ct = default)
    {
        if (transactions == null || transactions.Count == 0)
            return;

        _logger.LogInformation(
            "Returning {Count} transactions to unverified pool for register {RegisterId}",
            transactions.Count, registerId);

        var returned = 0;
        foreach (var transaction in transactions)
        {
            // Increment retry count
            transaction.RetryCount++;

            if (await SubmitTransactionAsync(registerId, transaction, ct))
            {
                returned++;
            }
        }

        Interlocked.Add(ref _totalReturned, returned);

        _logger.LogInformation(
            "Returned {Returned}/{Total} transactions to unverified pool for register {RegisterId}",
            returned, transactions.Count, registerId);
    }

    /// <inheritdoc/>
    public async Task<long> GetUnverifiedCountAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var queueKey = GetQueueKey(registerId);
                return await _database.ListLengthAsync(queueKey);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unverified count for register {RegisterId}", registerId);
            return 0;
        }
    }

    /// <inheritdoc/>
    public async Task<UnverifiedPoolStats> GetStatsAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var totalTransactions = await GetUnverifiedCountAsync(registerId, ct);

        DateTimeOffset? oldestTime = null;
        double averageAgeMs = 0;

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var expiryKey = GetExpiryKey(registerId);

                // Get oldest entry from sorted set (lowest score = earliest expiry)
                var oldest = await _database.SortedSetRangeByRankWithScoresAsync(expiryKey, 0, 0);
                if (oldest.Length > 0)
                {
                    var expiryTimestamp = oldest[0].Score;
                    var expiryTime = DateTimeOffset.FromUnixTimeSeconds((long)expiryTimestamp);

                    // Calculate when it was added (assuming default TTL)
                    oldestTime = expiryTime.Subtract(_config.TransactionTtl);

                    // Calculate average age from all entries
                    var all = await _database.SortedSetRangeByRankWithScoresAsync(expiryKey, 0, -1);
                    if (all.Length > 0)
                    {
                        var now = DateTimeOffset.UtcNow;
                        var totalAgeMs = all.Sum(entry =>
                        {
                            var entryExpiry = DateTimeOffset.FromUnixTimeSeconds((long)entry.Score);
                            var entryCreated = entryExpiry.Subtract(_config.TransactionTtl);
                            return (now - entryCreated).TotalMilliseconds;
                        });
                        averageAgeMs = totalAgeMs / all.Length;
                    }
                }
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pool stats for register {RegisterId}", registerId);
        }

        return new UnverifiedPoolStats
        {
            RegisterId = registerId,
            TotalTransactions = totalTransactions,
            TotalSubmitted = Interlocked.Read(ref _totalSubmitted),
            TotalPolled = Interlocked.Read(ref _totalPolled),
            TotalReturned = Interlocked.Read(ref _totalReturned),
            TotalExpired = Interlocked.Read(ref _totalExpired),
            OldestTransactionTime = oldestTime,
            AverageAgeMs = averageAgeMs
        };
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(
        string registerId,
        string transactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var dataKey = GetDataKey(registerId, transactionId);
                return await _database.KeyExistsAsync(dataKey);
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check existence of transaction {TransactionId}", transactionId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveTransactionAsync(
        string registerId,
        string transactionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        try
        {
            return await _pipeline.ExecuteAsync(async token =>
            {
                var queueKey = GetQueueKey(registerId);
                var dataKey = GetDataKey(registerId, transactionId);
                var expiryKey = GetExpiryKey(registerId);

                // Remove from all structures
                var removed = await _database.ListRemoveAsync(queueKey, transactionId) > 0;
                removed |= await _database.KeyDeleteAsync(dataKey);
                await _database.SortedSetRemoveAsync(expiryKey, transactionId);

                if (removed)
                {
                    _logger.LogDebug(
                        "Removed transaction {TransactionId} from unverified pool for register {RegisterId}",
                        transactionId, registerId);
                }

                return removed;
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove transaction {TransactionId}", transactionId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<int> CleanupExpiredAsync(
        string registerId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var expiredCount = 0;

        try
        {
            await _pipeline.ExecuteAsync(async token =>
            {
                var expiryKey = GetExpiryKey(registerId);
                var queueKey = GetQueueKey(registerId);
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // Get all expired entries (score less than now)
                var expired = await _database.SortedSetRangeByScoreAsync(
                    expiryKey,
                    double.NegativeInfinity,
                    now);

                foreach (var txId in expired)
                {
                    var transactionId = txId.ToString();
                    var dataKey = GetDataKey(registerId, transactionId);

                    // Remove from all structures
                    await _database.ListRemoveAsync(queueKey, transactionId);
                    await _database.KeyDeleteAsync(dataKey);
                    await _database.SortedSetRemoveAsync(expiryKey, transactionId);

                    expiredCount++;
                }
            }, ct);

            if (expiredCount > 0)
            {
                Interlocked.Add(ref _totalExpired, expiredCount);
                _logger.LogInformation(
                    "Cleaned up {Count} expired transactions from unverified pool for register {RegisterId}",
                    expiredCount, registerId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup expired transactions for register {RegisterId}", registerId);
        }

        return expiredCount;
    }
}
