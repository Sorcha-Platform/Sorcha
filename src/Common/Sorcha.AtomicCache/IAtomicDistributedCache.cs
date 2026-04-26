// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.AtomicCache;

/// <summary>
/// Distributed cache primitive that exposes the atomic operations
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// does not: GETDEL (single round-trip get-and-remove) and Lua-backed
/// compare-and-set. Used by replay-protection stores (HAIP c_nonce,
/// pre-authorisation codes, presentation request state) to close the
/// GET+DEL time-of-check / time-of-use window.
/// </summary>
/// <remarks>
/// On the audited storage list. In Production or Staging without a Redis
/// connection string, services refuse to start (unless overridden by
/// <c>Storage:AllowInMemoryInProduction</c>).
/// </remarks>
public interface IAtomicDistributedCache
{
    /// <summary>
    /// Reads the value at <paramref name="key"/>, or returns null if the
    /// key is absent or has expired.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="value"/> at <paramref name="key"/> with the
    /// given TTL. Overwrites any existing value. The TTL is set on every
    /// write — there is no rolling-window semantic.
    /// </summary>
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);

    /// <summary>
    /// Deletes <paramref name="key"/>. Idempotent — returns true if the
    /// key existed and was deleted, false if it was absent.
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct);

    /// <summary>
    /// Atomically reads <paramref name="key"/> and deletes it. Returns the
    /// value that was at the key, or null if the key was absent. Single
    /// round-trip — closes the GET+DEL TOCTOU window. Implemented as
    /// Redis GETDEL or <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}.TryRemove(TKey, out TValue)"/>.
    /// </summary>
    Task<string?> GetAndRemoveAsync(string key, CancellationToken ct);

    /// <summary>
    /// Atomically replaces the value at <paramref name="key"/> if and only
    /// if the current value equals <paramref name="expected"/>. Refreshes
    /// the TTL on success. Returns true on success, false if the current
    /// value did not match (including the absent-key case). Implemented as
    /// a Lua script in Redis; as a guarded read-then-write in memory.
    /// </summary>
    Task<bool> TryUpdateIfMatchAsync(
        string key,
        string expected,
        string newValue,
        TimeSpan ttl,
        CancellationToken ct);
}
