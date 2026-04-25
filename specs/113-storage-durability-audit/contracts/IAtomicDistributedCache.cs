// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Contract reference for feature 113-storage-durability-audit.
// This file is a planning artefact. The implementation lives in
// src/Common/Sorcha.AtomicCache/.

namespace Sorcha.AtomicCache;

/// <summary>
/// Distributed cache primitive that exposes the atomic operations
/// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/>
/// does not: GETDEL (single round-trip get-and-remove) and Lua-backed CAS.
/// Backed by StackExchange.Redis.IDatabase in production; by a
/// ConcurrentDictionary in development.
/// </summary>
/// <remarks>
/// On the audited storage list. In Production or Staging without a Redis
/// connection string, the service refuses to start (unless overridden by
/// Storage:AllowInMemoryInProduction).
/// </remarks>
public interface IAtomicDistributedCache
{
    /// <summary>Reads the value at <paramref name="key"/>, or null if absent.</summary>
    Task<string?> GetAsync(string key, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="value"/> at <paramref name="key"/> with the
    /// given TTL. Overwrites any existing value. The TTL is set on every
    /// write — there is no rolling-window semantic.
    /// </summary>
    Task SetAsync(string key, string value, TimeSpan ttl, CancellationToken ct);

    /// <summary>
    /// Deletes <paramref name="key"/>. Idempotent — returns true if the key
    /// existed and was deleted, false if it was absent.
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken ct);

    /// <summary>
    /// Atomically reads <paramref name="key"/> and deletes it. Returns the
    /// value that was at the key, or null if the key was absent.
    /// Single round-trip — closes the GET+DEL TOCTOU window.
    /// Implemented as Redis GETDEL or ConcurrentDictionary.TryRemove(out value).
    /// </summary>
    Task<string?> GetAndRemoveAsync(string key, CancellationToken ct);

    /// <summary>
    /// Atomically replaces the value at <paramref name="key"/> if and only
    /// if the current value equals <paramref name="expected"/>. Refreshes
    /// the TTL on success. Returns true on success, false if the current
    /// value did not match (including if the key was absent).
    /// Implemented as a Lua script in Redis; as a guarded read-then-write
    /// in memory.
    /// </summary>
    Task<bool> TryUpdateIfMatchAsync(
        string key,
        string expected,
        string newValue,
        TimeSpan ttl,
        CancellationToken ct);
}
