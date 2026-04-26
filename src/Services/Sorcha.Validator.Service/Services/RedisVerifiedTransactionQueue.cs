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
/// Per-register state lives in three Redis structures, keyed by the
/// register id wrapped in cluster-slot braces so multi-key operations
/// stay on the same slot:
/// </para>
/// <list type="bullet">
///   <item><c>sorcha:vtq:{registerId}:available</c> — sorted set of transactions awaiting claim (score = priority * 1e13 + enqueue time, lower = higher priority).</item>
///   <item><c>sorcha:vtq:{registerId}:claimed</c> — sorted set of claimed transactions (score = lease expiry unix-ms; expired claims auto-release on next claim).</item>
///   <item><c>sorcha:vtq:{registerId}:payload</c> — hash of <c>txId → JSON(VerifiedTransaction)</c>.</item>
/// </list>
/// <para>
/// The claim+auto-release operation runs as a single Lua script so the
/// HA-replica race "two validators try to claim the same transaction" is
/// impossible: the script atomically promotes expired leases back to
/// available and then takes the top N, all under one Redis-side lock.
/// </para>
/// </remarks>
public class RedisVerifiedTransactionQueue : IVerifiedTransactionQueue
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly VerifiedQueueConfiguration _config;
    private readonly ILogger<RedisVerifiedTransactionQueue> _logger;

    // Score base for FIFO-tiebreak inside a priority class. Large enough that no
    // realistic enqueue-time delta can bridge two priority classes.
    private const double PriorityScale = 1e13;

    private const string ClaimAndAutoReleaseScript = """
        local now = tonumber(ARGV[1])
        local leaseExpiresAt = tonumber(ARGV[2])
        local maxClaim = tonumber(ARGV[3])

        -- Step 1: walk claimed set for expired leases, return them to available.
        local expired = redis.call('ZRANGEBYSCORE', KEYS[2], '-inf', now)
        local expiredCount = 0
        for _, txId in ipairs(expired) do
            local payload = redis.call('HGET', KEYS[3], txId)
            redis.call('ZREM', KEYS[2], txId)
            if payload then
                local score = tonumber(string.match(payload, '"_score":([%-%.0-9eE]+)')) or now
                redis.call('ZADD', KEYS[1], score, txId)
                expiredCount = expiredCount + 1
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
        ILogger<RedisVerifiedTransactionQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        _multiplexer = multiplexer;
        _config = config.Value;
        _logger = logger;
    }

    private IDatabase Db => _multiplexer.GetDatabase();

    private static RedisKey AvailableKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:available";
    private static RedisKey ClaimedKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:claimed";
    private static RedisKey PayloadKey(string registerId) => $"sorcha:vtq:{{{registerId}}}:payload";

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
        var payload = SerializePayload(verifiedTx, score);

        var db = Db;
        var batch = db.CreateBatch();
        var hsetTask = batch.HashSetAsync(PayloadKey(registerId), transaction.TransactionId, payload, when: When.NotExists);
        var zaddTask = batch.SortedSetAddAsync(AvailableKey(registerId), transaction.TransactionId, score, when: When.NotExists);
        batch.Execute();
        var hashSet = hsetTask.GetAwaiter().GetResult();
        var sortedAdded = zaddTask.GetAwaiter().GetResult();

        if (!hashSet || !sortedAdded)
        {
            // Already enqueued — duplicate.
            return false;
        }
        return true;
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

        var keys = new RedisKey[] { AvailableKey(registerId), ClaimedKey(registerId), PayloadKey(registerId) };
        var values = new RedisValue[] { now, leaseExpiresAt, maxCount };

        var result = await Db.ScriptEvaluateAsync(ClaimAndAutoReleaseScript, keys, values).ConfigureAwait(false);
        var array = (RedisResult[])result!;
        if (array.Length == 0) return [];

        var expiredCount = (int)(long)array[0];
        if (expiredCount > 0)
        {
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
        var zremTask = batch.SortedSetRemoveAsync(ClaimedKey(registerId), ids);
        var hdelTask = batch.HashDeleteAsync(PayloadKey(registerId), ids);
        // Also remove from available in case caller is confirming a transaction that
        // was released back rather than going through the claimed path. Cheap insurance.
        var zremAvailableTask = batch.SortedSetRemoveAsync(AvailableKey(registerId), ids);
        batch.Execute();
        await Task.WhenAll(zremTask, hdelTask, zremAvailableTask).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string registerId, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(transactionIds);
        ct.ThrowIfCancellationRequested();

        var ids = transactionIds.Where(id => !string.IsNullOrEmpty(id)).ToArray();
        if (ids.Length == 0) return;

        // For each id: remove from claimed, look up its score from payload, re-add to
        // available at that score. Wrap in a transaction for cluster-slot safety.
        var db = Db;
        var tasks = new List<Task>(ids.Length);
        foreach (var txId in ids)
        {
            var payload = await db.HashGetAsync(PayloadKey(registerId), txId).ConfigureAwait(false);
            if (payload.IsNullOrEmpty) continue;
            var score = ExtractScore(payload!) ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var batch = db.CreateBatch();
            tasks.Add(batch.SortedSetRemoveAsync(ClaimedKey(registerId), txId));
            tasks.Add(batch.SortedSetAddAsync(AvailableKey(registerId), txId, score));
            batch.Execute();
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
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
        batch.Execute();
        return availableRemove.GetAwaiter().GetResult() || claimedRemove.GetAwaiter().GetResult() || hashDelete.GetAwaiter().GetResult();
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
    public int GetTotalCount()
    {
        // Without an active-registers index, we can't sum across all registers in O(1).
        // Keep this best-effort by returning zero — the size metric uses GetStats() which
        // is similarly bounded. Operators wanting per-register size should use the
        // sorcha_validator_mempool_size gauge.
        return 0;
    }

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
        batch.Execute();
        Task.WaitAll(t1, t2, t3);
        return count;
    }

    /// <inheritdoc />
    public int ClearAll()
    {
        // Cross-register iteration would require a cluster-wide SCAN; not implemented.
        // Operators run this via direct redis-cli when the platform's mempool needs
        // a hard reset — typically only during test environments.
        throw new NotSupportedException(
            "ClearAll is not supported by the Redis implementation. " +
            "Use Clear(registerId) per known register, or flush the Redis instance in test environments.");
    }

    /// <inheritdoc />
    public int CleanupExpired()
    {
        // The auto-release-on-claim path inside the Lua script handles lease expiry
        // for active registers. TTL-based payload cleanup is a no-op here — Redis
        // can be configured with key-level TTLs if operators want passive eviction
        // of fully abandoned registers. Returns 0 for symmetry with the contract.
        return 0;
    }

    /// <inheritdoc />
    public VerifiedQueueStats GetStats() => new()
    {
        TotalTransactions = 0, // see GetTotalCount note
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

    private static string SerializePayload(VerifiedTransaction tx, double score)
    {
        // Embed the original score in the payload so Release/auto-release can rescore
        // without round-tripping through deserialised priority + enqueued-at.
        var doc = new
        {
            tx.Transaction,
            EnqueuedAt = tx.EnqueuedAt,
            tx.Priority,
            ExpiresAt = tx.ExpiresAt,
            _score = score,
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

    private static double? ExtractScore(string json)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<JsonElement>(json);
            if (doc.TryGetProperty("_score", out var scoreElem))
            {
                return scoreElem.GetDouble();
            }
        }
        catch (JsonException) { }
        return null;
    }
}
