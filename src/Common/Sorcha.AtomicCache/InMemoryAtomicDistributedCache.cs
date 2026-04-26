// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;

namespace Sorcha.AtomicCache;

/// <summary>
/// In-memory <see cref="IAtomicDistributedCache"/> for development and
/// tests. Backed by <see cref="ConcurrentDictionary{TKey,TValue}"/> with
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/>
/// providing atomic GETDEL semantics within a single process. CAS is
/// guarded by a lock for read-then-write atomicity.
/// </summary>
/// <remarks>
/// On the audited storage list — Production and Staging refuse to start
/// when this implementation is selected.
/// </remarks>
public sealed class InMemoryAtomicDistributedCache : IAtomicDistributedCache
{
    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.Ordinal);
    private readonly object _casLock = new();

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        if (_store.TryGetValue(key, out var entry) && !entry.IsExpired)
        {
            return Task.FromResult<string?>(entry.Value);
        }

        if (entry is not null && entry.IsExpired)
        {
            // Best-effort eviction of expired entries.
            _store.TryRemove(key, out _);
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "TTL must be positive.");
        }
        ct.ThrowIfCancellationRequested();

        _store[key] = new Entry(value, DateTimeOffset.UtcNow.Add(ttl));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> RemoveAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_store.TryRemove(key, out _));
    }

    /// <inheritdoc />
    public Task<string?> GetAndRemoveAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ct.ThrowIfCancellationRequested();

        // ConcurrentDictionary.TryRemove(key, out value) is atomic at the
        // dictionary level — exactly the GETDEL semantics we need within
        // a single process. Multi-process coordination is the Redis
        // implementation's job.
        if (_store.TryRemove(key, out var entry) && !entry.IsExpired)
        {
            return Task.FromResult<string?>(entry.Value);
        }

        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<bool> TryUpdateIfMatchAsync(
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

        lock (_casLock)
        {
            if (!_store.TryGetValue(key, out var entry) || entry.IsExpired)
            {
                return Task.FromResult(false);
            }

            if (!string.Equals(entry.Value, expected, StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _store[key] = new Entry(newValue, DateTimeOffset.UtcNow.Add(ttl));
            return Task.FromResult(true);
        }
    }

    private sealed record Entry(string Value, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
