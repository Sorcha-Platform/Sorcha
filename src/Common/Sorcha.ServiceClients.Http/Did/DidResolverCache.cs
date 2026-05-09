// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Memory-backed, concurrent cache for <see cref="IDidResolverRegistry.ResolveWithAlsoKnownAsAsync"/>
/// results. Per-method TTLs, negative-result entries, and concurrent-call coalescing.
/// </summary>
/// <remarks>
/// <para>Per-method positive TTLs:
/// <list type="bullet">
///   <item><c>did:web</c> — <see cref="DidResolverCacheOptions.WebTtlMinutes"/> (default 60min).</item>
///   <item><c>did:sorcha:*</c> — infinite within process; invalidated explicitly via
///   <see cref="Invalidate"/> when a <c>transaction:confirmed</c> Redis-stream event fires
///   (Feature 120 T014).</item>
///   <item><c>did:key</c> — infinite (deterministic, never goes stale).</item>
/// </list>
/// </para>
/// <para>Negative entries (failed resolutions, including cross-resolution mismatches) live
/// for <see cref="DidResolverCacheOptions.NegativeTtlSeconds"/> across all methods to avoid
/// thundering-herd retries against an unreachable link without masking transient failures
/// for too long.</para>
/// </remarks>
public sealed class DidResolverCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly DidResolverCacheOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>DI-friendly constructor.</summary>
    public DidResolverCache(IOptions<DidResolverCacheOptions> options, TimeProvider? clock = null)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Returns the cached resolution for <paramref name="did"/>, or invokes <paramref name="factory"/>
    /// if the entry is missing or expired. Concurrent calls for the same DID coalesce — only one
    /// factory invocation runs; later callers await the in-flight task.
    /// </summary>
    /// <param name="did">Canonical primary DID (cache key).</param>
    /// <param name="factory">Async factory producing the resolution result. <c>null</c> result is treated as a negative cache entry.</param>
    /// <returns>The cached or freshly-resolved <see cref="DidDocument"/>, or null on negative outcome.</returns>
    public Task<DidDocument?> GetOrAddAsync(string did, Func<Task<DidDocument?>> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(did);
        ArgumentNullException.ThrowIfNull(factory);

        var now = _clock.GetUtcNow();

        // Hot path: fresh hit.
        if (_entries.TryGetValue(did, out var existing) && existing.ExpiresAt > now)
        {
            return existing.Task;
        }

        // Build a fresh entry. AddOrUpdate ensures only one Lazy task runs even under
        // concurrent contention.
        var newEntry = new Entry(_clock, _options, did, factory);
        var winner = _entries.AddOrUpdate(
            did,
            newEntry,
            (_, current) => current.ExpiresAt > now ? current : newEntry);

        return winner.Task;
    }

    /// <summary>Explicitly invalidates the cached entry for <paramref name="did"/>.</summary>
    public void Invalidate(string did)
    {
        if (string.IsNullOrEmpty(did)) return;
        _entries.TryRemove(did, out _);
    }

    /// <summary>Clears every cached entry. Test/diagnostic use.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Internal — enumerates the cached DIDs (used by Redis-stream invalidation).</summary>
    internal IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)_entries.Keys;

    private sealed class Entry
    {
        private readonly Lazy<Task<DidDocument?>> _lazy;
        private readonly TimeProvider _clock;
        private readonly DidResolverCacheOptions _options;
        private readonly string _did;
        private DateTimeOffset _expiresAtAfterFactory = DateTimeOffset.MaxValue;

        public Entry(TimeProvider clock, DidResolverCacheOptions options, string did, Func<Task<DidDocument?>> factory)
        {
            _clock = clock;
            _options = options;
            _did = did;
            _lazy = new Lazy<Task<DidDocument?>>(async () =>
            {
                var result = await factory().ConfigureAwait(false);
                _expiresAtAfterFactory = ComputeExpiry(_did, result, _clock.GetUtcNow(), _options);
                return result;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public Task<DidDocument?> Task => _lazy.Value;

        public DateTimeOffset ExpiresAt =>
            _lazy.IsValueCreated && _lazy.Value.IsCompletedSuccessfully
                ? _expiresAtAfterFactory
                : DateTimeOffset.MaxValue; // in-flight: keep coalescing
    }

    private static DateTimeOffset ComputeExpiry(
        string did,
        DidDocument? result,
        DateTimeOffset now,
        DidResolverCacheOptions options)
    {
        if (result is null)
        {
            // Negative entries: short TTL across all methods.
            return now.AddSeconds(Math.Max(1, options.NegativeTtlSeconds));
        }

        return ParseMethod(did) switch
        {
            "web" => now.AddMinutes(Math.Max(1, options.WebTtlMinutes)),
            "sorcha" => DateTimeOffset.MaxValue,    // invalidated on Redis stream events
            "key" => DateTimeOffset.MaxValue,       // deterministic
            _ => now.AddMinutes(Math.Max(1, options.WebTtlMinutes)) // unknown methods: be conservative
        };
    }

    private static string? ParseMethod(string did)
    {
        if (string.IsNullOrEmpty(did) || !did.StartsWith("did:", StringComparison.OrdinalIgnoreCase))
            return null;
        var firstColon = did.IndexOf(':', 4);
        return firstColon < 0 ? did[4..] : did[4..firstColon];
    }
}
