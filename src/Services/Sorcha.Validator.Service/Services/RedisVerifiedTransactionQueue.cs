// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.Extensions.Options;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services.Interfaces;
using StackExchange.Redis;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Redis-backed <see cref="IVerifiedTransactionQueue"/> implementation
/// providing restart durability and HA-replica coordination.
/// </summary>
/// <remarks>
/// <para>
/// Per-register state lives in four Redis structures, all keyed with the
/// register id wrapped in cluster-slot braces so multi-key operations stay
/// slot-local:
/// </para>
/// <list type="bullet">
///   <item><c>sorcha:vtq:{registerId}:available</c> — sorted set of transactions awaiting claim (score = priority composite, lower = higher priority).</item>
///   <item><c>sorcha:vtq:{registerId}:claimed</c> — sorted set of claimed transactions (score = lease expiry unix-ms; expired leases auto-release on next claim).</item>
///   <item><c>sorcha:vtq:{registerId}:payload</c> — hash of <c>txId → JSON(VerifiedTransaction)</c>.</item>
///   <item><c>sorcha:vtq:{registerId}:scores</c> — hash of <c>txId → numeric score</c> for restoration on auto-release / Release.</item>
/// </list>
/// <para>
/// The claim+auto-release operation runs as a single Lua script so the
/// HA-replica race "two validators try to claim the same transaction" is
/// impossible: the script atomically promotes expired leases back to
/// available (using the dedicated scores hash for the original priority
/// ordering) and then takes the top N, all under one Redis-side lock.
/// </para>
/// <para>
/// Enqueue is also a Lua script so it's all-or-nothing — no chance of
/// payload-without-sorted-set strandedness if the Redis connection drops
/// between writes.
/// </para>
/// </remarks>
public class RedisVerifiedTransactionQueue : IVerifiedTransactionQueue
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly VerifiedQueueConfiguration _config;
    private readonly ValidatorMempoolMetrics _metrics;
    private readonly ILogger<RedisVerifiedTransactionQueue> _logger;

    private const double PriorityScale = 1e13;

    // Atomic enqueue: writes to all three keys (available, payload, scores)
    // together. KEYS[1]=available, KEYS[2]=payload, KEYS[3]=scores.
    // ARGV[1]=txId, ARGV[2]=score, ARGV[3]=payload-json.
    // Returns 1 on insert, 0 if duplicate.
    private const string EnqueueScript = """
        if redis.call('HEXISTS', KEYS[2], ARGV[1]) == 1 then
            return 0
        end
        redis.call('HSET', KEYS[2], ARGV[1], ARGV[3])
        redis.call('HSET', KEYS[3], ARGV[1], ARGV[2])
        redis.call('ZADD', KEYS[1], ARGV[2], ARGV[1])
        return 1
        """;

    // Atomic claim with auto-release of expired leases.
    // KEYS[1]=available, KEYS[2]=claimed, KEYS[3]=payload, KEYS[4]=scores.
    // ARGV[1]=now-ms, ARGV[2]=leaseExpiresAt-ms, ARGV[3]=maxClaim.
    // Returns: { expiredCount, payload1, payload2, ... }
    private const string ClaimAndAutoReleaseScript = """
        local now = tonumber(ARGV[1])
        local leaseExpiresAt = tonumber(ARGV[2])
        local maxClaim = tonumber(ARGV[3])

        -- Step 1: walk claimed set for expired leases, return them to available
        -- using the original priority score from the dedicated scores hash.
        local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', now)
        local expiredCount = 0
        for _, txId in ipairs(expired) do
            redis.call('ZREM', KEYS[2], txId)
            local score = tonumber(redis.call('HGET', KEYS[4], txId))
            if score ~= nil and redis.call('HEXISTS', KEYS[3], txId) == 1 then
                redis.call('ZADD', KEYS[1], score, txId)
                expiredCount = expiredCount + 1
            else
                -- Orphan (payload or score missing) — drop the score key too.
                redis.call('HDEL', KEYS[4], txId)
            end
        end

        -- Step 2: claim up to maxClaim highest-priority transactions.
        local toClaim = redis.call('ZRANGE', KEYS[1], 0, maxClaim - 1)
        local results = {}
        table.insert(results, tostring(expiredCount))
        for _, txId in ipairs(toClaim) do
            redis.call('ZREM', KEYS[1], txId)
            redis.call('ZADD', KEYS[2], leaseExpiresAt, txId)
            local payload = redis.call('HGET', KEYS[3], txId)
            if payload then
                table.insert(results, payload)
            end
        end
        return results
        """;

    public RedisVerifiedTransactionQueue(
        IConnectionMultiplexer multiplexer,
        IOptions<VerifiedQueueConfiguration> config,
        ValidatorMempoolMetrics metrics,
        ILogger<RedisVerifiedTransactionQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(logger);
        _multiplexer = multiplexer;
        _config = config.Value;
        _metrics = metrics;
        _logger = logger;
    }

    private IDatabase Db => _multiplexer.GetDatabase();

    private static RedisKey AvailableKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:available";
    private static RedisKey ClaimedKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:claimed";
    private static RedisKey PayloadKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:payload";
    private static RedisKey ScoresKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:scores";

    /// <inheritdoc />
    public bool Enqueue(string registerId, Transaction transaction, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(transaction.TransactionId);

        var enqueuedAt = DateTimeOffset.UtcNow;
        var expiresAt = enqueuedAt.Add(_config.TransactionTtl);
        var score = ComputeScore(priority, enqueuedAt);

        var verifiedTx = new VerifiedTransaction
        {
            Transaction = transaction,
            EnqueuedAt = enqueuedAt,
            Priority = priority,
            ExpiresAt = expiresAt,
        };
        var payload = SerializePayload(verifiedTx);

        var keys = new RedisKey[]
        {
            AvailableKey(registerId),
            PayloadKey(registerId),
            ScoresKey(registerId),
        };
        var values = new RedisValue[] { transaction.TransactionId, score, payload };

        // Lua atomicity — Redis treats the whole script as one operation, so a
        // network blip between the HSETs and ZADD can't leave half-state on disk.
        var result = Db.ScriptEvaluate(EnqueueScript, keys, values);
        return (long)result == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerifiedTransactionLease>> ClaimAsync(
        string registerId, int maxCount, TimeSpan leaseDuration, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        if (maxCount <= 0) return [];
        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        ct.ThrowIfCancellationRequested();

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var leaseExpiresAt = DateTimeOffset.UtcNow.Add(leaseDuration).ToUnixTimeMilliseconds();

        var keys = new RedisKey[]
        {
            AvailableKey(registerId),
            ClaimedKey(registerId),
            PayloadKey(registerId),
            ScoresKey(registerId),
        };
        var values = new RedisValue[] { now, leaseExpiresAt, maxCount };

        var result = await Db.ScriptEvaluateAsync(ClaimAndAutoReleaseScript, keys, values).ConfigureAwait(false);
        var array = (RedisResult[])result!;
        if (array.Length == 0) return [];

        var expiredCount = (int)(long)array[0];
        if (expiredCount > 0)
        {
            _metrics.RecordLeaseExpired(registerId, expiredCount);
            _logger.LogDebug("Auto-released {Count} expired lease(s) for register {RegisterId}", expiredCount, registerId);
        }

        var leases = new List<VerifiedTransactionLease>(array.Length - 1);
        for (var i = 1; i < array.Length; i++)
        {
            var json = (string?)array[i];
            if (string.IsNullOrEmpty(json)) continue;
            var verified = DeserializePayload(json);
            if (verified is null) continue;
            leases.Add(new VerifiedTransactionLease
            {
                RegisterId = registerId,
                Transaction = verified,
                LeaseExpiresAt = DateTimeOffset.FromUnixTimeMilliseconds(leaseExpiresAt),
            });
        }
        return leases;
    }

    /// <inheritdoc />
    public async Task ConfirmAsync(string registerId, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactionIds);
        ct.ThrowIfCancellationRequested();

        var ids = transactionIds.Where(id => !string.IsNullOrEmpty(id)).Select(id => (RedisValue)id).ToArray();
        if (ids.Length == 0) return;

        var db = Db;
        var batch = db.CreateBatch();
        var t1 = batch.SortedSetRemoveAsync(ClaimedKey(registerId), ids);
        var t2 = batch.HashDeleteAsync(PayloadKey(registerId), ids);
        var t3 = batch.HashDeleteAsync(ScoresKey(registerId), ids);
        var t4 = batch.SortedSetRemoveAsync(AvailableKey(registerId), ids);
        batch.Execute();
        await Task.WhenAll(t1, t2, t3, t4).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string registerId, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactionIds);
        ct.ThrowIfCancellationRequested();

        var ids = transactionIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();
        if (ids.Length == 0) return;
        var redisIds = ids.Select(id => (RedisValue)id).ToArray();

        var db = Db;
        // One multiget for all scores, then one pipelined batch to remove from claimed
        // and re-add to available with their original priority scores.
        var scores = await db.HashGetAsync(ScoresKey(registerId), redisIds).ConfigureAwait(false);

        var batch = db.CreateBatch();
        var removeTask = batch.SortedSetRemoveAsync(ClaimedKey(registerId), redisIds);
        var addTasks = new List<Task<bool>>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            if (!scores[i].TryParse(out double score)) continue;
            addTasks.Add(batch.SortedSetAddAsync(AvailableKey(registerId), ids[i], score));
        }
        batch.Execute();
        await Task.WhenAll(addTasks.Cast<Task>().Append(removeTask)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyList<VerifiedTransaction> Peek(string registerId, int maxCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        if (maxCount <= 0) return [];

        var db = Db;
        var members = db.SortedSetRangeByRank(AvailableKey(registerId), 0, maxCount - 1);
        if (members.Length == 0) return [];

        var payloads = db.HashGet(PayloadKey(registerId), members.Select(m => (RedisValue)m).ToArray());
        var result = new List<VerifiedTransaction>(payloads.Length);
        foreach (var p in payloads)
        {
            if (p.IsNullOrEmpty) continue;
            var verified = DeserializePayload(p!);
            if (verified is not null) result.Add(verified);
        }
        return result;
    }

    /// <inheritdoc />
    public bool Remove(string registerId, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        var db = Db;
        var batch = db.CreateBatch();
        var availableRemove = batch.SortedSetRemoveAsync(AvailableKey(registerId), transactionId);
        var claimedRemove = batch.SortedSetRemoveAsync(ClaimedKey(registerId), transactionId);
        var hashDelete = batch.HashDeleteAsync(PayloadKey(registerId), transactionId);
        var scoreDelete = batch.HashDeleteAsync(ScoresKey(registerId), transactionId);
        batch.Execute();
        return availableRemove.GetAwaiter().GetResult()
            || claimedRemove.GetAwaiter().GetResult()
            || hashDelete.GetAwaiter().GetResult()
            || scoreDelete.GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public bool Contains(string registerId, string transactionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        return Db.HashExists(PayloadKey(registerId), transactionId);
    }

    /// <inheritdoc />
    public int GetCount(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        var db = Db;
        return (int)(db.SortedSetLength(AvailableKey(registerId)) + db.SortedSetLength(ClaimedKey(registerId)));
    }

    /// <inheritdoc />
    public int GetTotalCount() => 0; // see GetTotalCount note in InMemoryVerifiedTransactionQueue

    /// <inheritdoc />
    public int Clear(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        var db = Db;
        var count = GetCount(registerId);
        var batch = db.CreateBatch();
        var t1 = batch.KeyDeleteAsync(AvailableKey(registerId));
        var t2 = batch.KeyDeleteAsync(ClaimedKey(registerId));
        var t3 = batch.KeyDeleteAsync(PayloadKey(registerId));
        var t4 = batch.KeyDeleteAsync(ScoresKey(registerId));
        batch.Execute();
        Task.WaitAll(t1, t2, t3, t4);
        return count;
    }

    /// <inheritdoc />
    public int ClearAll()
    {
        // Cross-register iteration would require a cluster-wide SCAN; not implemented.
        // Audited via grep before merge — only test code calls this. Production paths
        // that need a full reset use redis-cli FLUSHDB or per-register Clear.
        throw new NotSupportedException(
            "ClearAll is not supported by the Redis implementation. " +
            "Use Clear(registerId) per known register, or flush the Redis instance in test environments.");
    }

    /// <inheritdoc />
    public int CleanupExpired() => 0; // lease auto-release happens inside the claim Lua script

    /// <inheritdoc />
    public VerifiedQueueStats GetStats() => new()
    {
        // Cross-register stats not implemented for the Redis path — operators use the
        // sorcha_validator_mempool_lease_expired_total counter and per-register
        // GetRegisterStats reads when needed.
        TotalTransactions = 0,
        ActiveRegisters = 0,
        AverageTransactionsPerRegister = 0,
    };

    /// <inheritdoc />
    public RegisterQueueStats GetRegisterStats(string registerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        return new RegisterQueueStats
        {
            RegisterId = registerId,
            TransactionCount = GetCount(registerId),
        };
    }

    private static double ComputeScore(int priority, DateTimeOffset enqueuedAt)
    {
        // Higher domain priority sorts earlier under ZRANGE → lower score.
        return (-(double)priority * PriorityScale) + enqueuedAt.ToUnixTimeMilliseconds();
    }

    private static string SerializePayload(VerifiedTransaction tx)
    {
        var doc = new
        {
            tx.Transaction,
            EnqueuedAt = tx.EnqueuedAt,
            tx.Priority,
            ExpiresAt = tx.ExpiresAt,
        };
        return JsonSerializer.Serialize(doc);
    }

    private static VerifiedTransaction? DeserializePayload(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (!doc.TryGetProperty("Transaction", out var txElem)) return null;
            var transaction = txElem.Deserialize<Transaction>();
            if (transaction is null) return null;
            return new VerifiedTransaction
            {
                Transaction = transaction,
                EnqueuedAt = doc.GetProperty("EnqueuedAt").GetDateTimeOffset(),
                Priority = doc.GetProperty("Priority").GetInt32(),
                ExpiresAt = doc.GetProperty("ExpiresAt").GetDateTimeOffset(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
