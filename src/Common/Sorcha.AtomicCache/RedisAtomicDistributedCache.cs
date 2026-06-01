// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using StackExchange.Redis;

namespace Sorcha.AtomicCache;

/// <summary>
/// Redis-backed <see cref="IAtomicDistributedCache"/> using
/// <see cref="IDatabase.StringGetDeleteAsync"/> (GETDEL) for atomic
/// get-and-remove and a Lua script for compare-and-set with TTL refresh.
/// </summary>
/// <remarks>
/// Resolves <see cref="IDatabase"/> lazily from the registered
/// <see cref="IConnectionMultiplexer"/> — no extra connection setup.
/// </remarks>
public sealed class RedisAtomicDistributedCache : IAtomicDistributedCache
{
    private readonly IConnectionMultiplexer _multiplexer;

    // Lua compare-and-set: refresh TTL (in milliseconds) only on match.
    // Returns 1 on match, 0 otherwise. Atomic on the Redis side — the entire
    // script runs as one operation. Uses PX (milliseconds) to match the
    // millisecond-precision TimeSpan handling on SetAsync's StringSetAsync path.
    private const string CasScript = """
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            redis.call('SET', KEYS[1], ARGV[2], 'PX', ARGV[3])
            return 1
        else
            return 0
        end
        """;

    /// <summary>Creates a Redis-backed atomic cache over an existing connection multiplexer.</summary>
    public RedisAtomicDistributedCache(IConnectionMultiplexer multiplexer)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        _multiplexer = multiplexer;
    }

    private IDatabase Db => _multiplexer.GetDatabase();

    /// <inheritdoc />
    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        var value = await Db.StringGetAsync(key).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    /// <inheritdoc />
    public async Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }
        ct.ThrowIfCancellationRequested();

        await Db.StringSetAsync(key, value, ttl).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetIfAbsentAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }
        ct.ThrowIfCancellationRequested();

        // SET key value PX <ttl> NX — atomic claim. Returns true only if the key was absent.
        return await Db.StringSetAsync(key, value, ttl, When.NotExists).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        return await Db.KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> GetAndRemoveAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        var value = await Db.StringGetDeleteAsync(key).ConfigureAwait(false);
        return value.IsNullOrEmpty ? null : value.ToString();
    }

    /// <inheritdoc />
    public async Task<bool> TryUpdateIfMatchAsync(
        string key,
        string expected,
        string newValue,
        TimeSpan ttl,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(newValue);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }
        ct.ThrowIfCancellationRequested();

        // Pass TTL as milliseconds so sub-second values from SetAsync's TimeSpan path
        // round-trip without precision loss. PX in the Lua script consumes ms directly.
        var ttlMs = (long)ttl.TotalMilliseconds;

        var result = await Db.ScriptEvaluateAsync(
            CasScript,
            keys: new RedisKey[] { key },
            values: new RedisValue[] { expected, newValue, ttlMs }).ConfigureAwait(false);

        return (long)result == 1;
    }
}
